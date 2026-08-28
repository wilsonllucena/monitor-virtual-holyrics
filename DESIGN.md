# Monitor Virtual para Holyrics — Documento de Projeto

**Objetivo:** um `.exe` para Windows 10/11 que cria um monitor virtual na máquina, de forma
que o Holyrics o reconheça e o use como tela de projeção automaticamente, sem hardware
(projetor/HDMI dummy) conectado.

---

## 1. Como o Windows permite criar um monitor "de mentira"

Só existe um caminho suportado pela Microsoft: um **Indirect Display Driver (IddCx)** —
driver *user-mode* (UMDF) da classe `Display` que registra um adaptador e monitores
virtuais no sistema. Uma vez instalado, o monitor aparece em *Configurações → Vídeo*, tem
EDID, resolução, posição no desktop estendido, e é indistinguível de um monitor físico
para qualquer aplicativo — inclusive o Holyrics.

Alternativas descartadas:

| Abordagem | Por que não |
|---|---|
| Dummy plug HDMI/EDID emulator | É hardware; o pedido é software |
| RDP / sessão secundária | Holyrics roda na sessão do usuário; muda a topologia toda |
| "Fake monitor" via `ChangeDisplaySettings` | Não existe — o Windows só lista monitores vindos de um driver |
| Escrever driver kernel-mode | Desnecessário e muito mais arriscado; IddCx é user-mode |

### Ponto crítico do projeto: **assinatura de driver**

Windows 10 1607+ não instala pacote de driver cujo `.cat` não encadeie a um certificado
confiável. Escrever um driver IddCx do zero é a parte *fácil* (existe sample oficial da
Microsoft); publicá-lo exige certificado EV + submissão de *attestation signing* no
Partner Center (~US$ 250–400/ano + conta Partner Center). **Por isso o MVP não escreve
driver: embala um driver IddCx já assinado e open-source.**

---

## 2. Escolha do driver base (comparativo pesquisado)

| Projeto | Licença | Assinatura | Controle em runtime | Observações |
|---|---|---|---|---|
| **VirtualDrivers / Virtual-Display-Driver (VDD)** | MIT | Assinado (SignPath Foundation), `mttvdd.cat` | `vdd_settings.xml` (`<count>` 1–16) + restart do device | Win10 2004+ e Win11, EDID custom, seleção de GPU, HDR no Win11 23H2+. **Recomendado** |
| nomi-san / parsec-vdd | driver da Parsec (redistribuição não declarada) | Assinado pela Parsec (instala limpo) | IOCTLs `0x801` add / `0x802` remove / `0x803` update / `0x804` version / `0x805` LUID, HWID `Root\Parsec\VDA` | **Exige keep-alive**: sem ping periódico os monitores caem em ~1s. Licença de redistribuição é o risco |
| MolotovCherry / virtual-display-rs | MIT | Certificado self-signed → precisa importar em *Root* + *TrustedPublisher* | app de controle | Win10 2004+ x64. Import de root cert é ruim para distribuição em igrejas |
| Amyuni usbmmidd_v2 | freeware | Assinado, antigo | `usbmmidd.bat` | Limitado (1080p), sem manutenção |

**Decisão: VDD (MIT + assinado).** Sem keep-alive (o monitor vive enquanto o device
estiver habilitado), licença permite redistribuir dentro do nosso instalador mantendo o
aviso de copyright, e cobre Win10 e Win11.

> Plano B, se a política de driver do ambiente reclamar: fallback para o pacote da Parsec
> (instala silencioso com `/S`), com um daemon fazendo o ping.
>
> Fase 2 (marca própria): fork do sample IddCx da Microsoft, EDID com nome
> `"Projecao Holyrics"`, certificado EV + attestation signing. Aí o driver passa a ser
> nosso e o instalador vira 100% silencioso e sem dependência de terceiros.

---

## 3. Arquitetura do produto

```
MonitorVirtual.sln
├─ src/
│  ├─ MonitorVirtual.App/          # app de bandeja (WinForms, .NET 8, x64, single-file) — o produto
│  ├─ MonitorVirtual.Cli/          # mvcli.exe — instalação silenciosa, diagnóstico, testes
│  └─ MonitorVirtual.Core/         # lógica compartilhada, zero dependências NuGet
│      ├─ Devices/DriverManager.cs     # SetupAPI/newdev: device node, INF, enable/disable/restart
│      ├─ Devices/VddSettings.cs       # vdd_settings.xml + VDDPATH no registro
│      ├─ Display/DisplayService.cs    # enumerar, estender, posicionar, resolução, não-primário
│      ├─ Provisioning/MonitorProvisioner.cs  # reconciliação estado desejado × real
│      ├─ Holyrics/HolyricsClient.cs   # API local (8091), detecção e start ordenado
│      └─ Startup/StartupTask.cs       # tarefa de logon elevada
├─ driver/                         # payload: MttVDD.inf, mttvdd.cat, MttVDD.dll
├─ installer/                      # Inno Setup → MonitorVirtualSetup.exe
└─ tools/                          # build.ps1, fetch-driver.ps1
```

**Elevação sem serviço Windows.** O projeto inicial previa um serviço SYSTEM + IPC por
named pipe, porque habilitar/desabilitar o device exige elevação e ninguém quer prompt de
UAC no domingo de manhã. Na implementação isso foi trocado por algo mais simples com o
mesmo efeito: **uma tarefa do Agendador criada com `/RL HIGHEST /SC ONLOGON`** inicia o app
já elevado no logon, sem prompt. Um processo, sem IPC, sem serviço para dar manutenção. O
custo é um UAC quando o app é aberto manualmente pelo atalho (o manifesto pede
`requireAdministrator`) — aceitável, já que o caminho normal é o início automático.

**Fatos verificados do pacote do driver** (release 25.7.23, o mais recente):

| Item | Valor |
|---|---|
| Hardware ID | `Root\MttVDD` (classe `Display`, `{4D36E968-E325-11CE-BFC1-08002BE10318}`) |
| Extensão | `IddCx0102` (UMDF 2.25) → Windows 10 2004+ |
| Arquivos | `MttVDD.inf`, `mttvdd.cat`, `MttVDD.dll` (~250 KB) |
| Assinatura do catálogo | Válida — SignPath Foundation / GlobalSign, expira em **09/07/2027**, com timestamp |
| Config | `vdd_settings.xml` em `C:\VirtualDisplayDriver`, **redirecionável** por `HKLM\SOFTWARE\MikeTheTech\VirtualDisplayDriver\VDDPATH` |

O redirecionamento por `VDDPATH` é importante: apontamos para
`%ProgramData%\MonitorVirtual\driver-config` e não brigamos com uma instalação
pré-existente do VDD na máquina.

> Atenção de manutenção: o último release do driver é de **julho/2025**. Se o projeto
> parar, o plano de contingência é a Fase 3 (driver próprio) ou o fallback Parsec.

### Fluxo de instalação (uma vez, com UAC)

1. Instalador copia `driver/` para `C:\Program Files\MonitorVirtual\driver` e o app.
2. Importa o certificado do `.cat` para `LocalMachine\TrustedPublisher`
   (evita o diálogo "deseja instalar este software de dispositivo?").
3. Cria o nó de dispositivo *root-enumerated* — equivalente ao que o `nefconw` faz:
   - `SetupDiCreateDeviceInfoList` (class `Display`, GUID `{4D36E968-E325-11CE-BFC1-08002BE10318}`)
   - `SetupDiCreateDeviceInfoW` + `SetupDiSetDeviceRegistryProperty(SPDRP_HARDWAREID)`
   - `SetupDiCallClassInstaller(DIF_REGISTERDEVICE)`
   - `UpdateDriverForPlugAndPlayDevices` (newdev.dll) apontando para o `.inf`
4. Escreve `vdd_settings.xml` com 1 monitor e a lista de resoluções desejada.
5. Registra o serviço + tray no logon; roda o *primeiro provisionamento*.

### Fluxo de execução (todo boot, sem UAC)

```
logon → tarefa agendada sobe o app elevado → lê config (ligado? resolução? posição?)
      → garante device habilitado
      → garante topologia ESTENDIDA (SetDisplayConfig, SDC_TOPOLOGY_EXTEND)
      → posiciona o monitor virtual à direita do primário, resolução fixa
      → marca "pronto" → (opcional) inicia o Holyrics (via explorer.exe, sem elevação)
      → watchdog a cada 5 s + eventos DisplaySettingsChanged e PowerModes.Resume:
        se o monitor sumiu (sleep/resume, update de GPU, Win+P), recria e reposiciona
```

APIs Win32 usadas na topologia: `QueryDisplayConfig` / `SetDisplayConfig` (CCD) para
estender e persistir a topologia, `ChangeDisplaySettingsEx` para resolução/posição,
`EnumDisplayDevices` para achar o monitor virtual pelo nome do adaptador.

### Aplicar mudanças sem reiniciar o Windows

Alterar `vdd_settings.xml` só tem efeito depois de reiniciar o device. Fazemos isso via
SetupAPI (`SetupDiSetClassInstallParams` com `DICS_DISABLE` → `DICS_ENABLE` +
`SetupDiCallClassInstaller(DIF_PROPERTYCHANGE)`), que funciona igual em Win10 e Win11 —
mais confiável que `pnputil /disable-device`, que é recente.

---

## 4. Integração com o Holyrics — "reconhecido automaticamente"

O Holyrics é Java e enumera as telas pelo `GraphicsEnvironment` do sistema. Um monitor
IddCx **é** uma tela do sistema, então ele aparece na lista de monitores da configuração de
projeção sem nenhum truque. O trabalho real é garantir três coisas:

**a) O monitor existe *antes* do Holyrics abrir.**
Apps Java nem sempre reagem bem a hot-plug de monitor; a configuração de tela pública pode
não enxergar um monitor que apareceu depois. Solução: o serviço provisiona o monitor no
logon e o app oferece o modo *launcher* — "Iniciar Holyrics junto com o monitor", só
disparando o `Holyrics.exe` depois que o monitor está confirmado ativo. Retirar o Holyrics
do Startup do Windows e deixar o nosso app iniciá-lo.

**b) A identidade da tela é estável entre reinícios.**
O Holyrics guarda qual monitor é a tela pública. Se o monitor virtual mudar de índice,
posição ou resolução, a configuração "escorrega" para a tela errada. Por isso:
EDID/nome fixos, resolução fixa (padrão 1920×1080), posição sempre à direita do primário,
e o monitor virtual **nunca** primário.

**c) A topologia está em "Estender".**
É a causa nº 1 de "o Holyrics não projeta" — o Windows está em Duplicar ou "Somente tela 1"
(`Win+P`). O serviço força `SDC_TOPOLOGY_EXTEND` e o watchdog reaplica se alguém mudar.

**Verificação opcional via API do Holyrics** (Configurações → API Server; `POST` JSON em
`http://localhost:8091/api/<metodo>?token=...`): `GetDisplaySettings` / `SetDisplaySettings`
leem e alteram as telas. A Tela pública expõe `screen` (`"x,y"`) e `area`/`total_area`.
Com surround ligado o app aponta `id=public` para o canvas virtual e manda `hide: true`
nas `screen_2`/`screen_3` cuja área cai num projetor — senão o Holyrics abre em duas
saídas físicas e o telão fica dividido. Sem token da API, o operador ainda precisa
escolher o Virtual Display Driver uma vez no assistente.

Uma alternativa que vale medir na fase 2: o Holyrics também tem saída **NDI**. Para quem só
quer levar a projeção ao OBS, NDI resolve sem driver nenhum. O monitor virtual continua
sendo superior quando se quer captura de tela, ensaio sem projetor, tela de palco extra ou
streaming via Sunshine/Parsec.

---

## 4.1 Outros consumidores do monitor: Resolume Arena, OBS

O mesmo monitor virtual precisa ser reconhecido pelo **Resolume Arena** (Advanced Output).
A pesquisa e o teste com o Holyrics mostraram que o problema **não é específico do
Holyrics** — é o padrão de todo software de projeção: a lista de saídas é montada quando o
programa abre. Por isso o produto trata "programas que consomem o monitor" como conceito de
primeira classe (`ManagedApp`), e não como um campo do Holyrics:

```
ManagedApp { Name, ExePath, ProcessName, LaunchAfterMonitor, AutoRestartIfEarly }
```

O provisionador é indiferente a quem consome; o app cuida da ordem:

1. monitor virtual pronto → 2. abre os programas marcados → 3. se algum já estava aberto
quando o monitor nasceu, sinaliza no menu **Reiniciar programa** (submenu por programa) e,
opcionalmente, reinicia sozinho.

Detecção automática cobre `Holyrics.exe`, `Arena.exe` (Resolume Arena), `Avenue.exe` e
`obs64.exe`, procurando em Program Files, Program Files (x86), LocalAppData e `C:\`
(inclusive em pastas versionadas do tipo `Resolume Arena 7`).

**Particularidades do Resolume** (dos fóruns oficiais):

- o Advanced Output guarda o vínculo *Screen → Display* e é conhecido por **embaralhar ou
  perder esse vínculo quando o conjunto de displays muda entre reinícios** — a estabilidade
  de geometria que o provisionador garante (mesma resolução, mesma posição, nunca primário)
  é ainda mais importante aqui do que no Holyrics;
- displays desligados/ausentes no boot são a causa clássica do problema — com o monitor
  virtual, ele **sempre** existe antes do Arena abrir, o que na prática *melhora* a
  situação em relação a um projetor físico que às vezes está desligado;
- o Resolume não cria saídas: quem cria é o Windows. Nada de especial é preciso do lado do
  driver.

**Limite honesto:** um IddCx entrega o quadro por composição de desktop, sem cabo de vídeo
dedicado. Para VJ em 1080p60 funciona, mas custa CPU/GPU a mais do que uma saída física.
Se o objetivo for levar o Arena para dentro do OBS na mesma máquina, **Spout** (nativo no
Resolume) é mais eficiente que capturar um monitor virtual. O monitor virtual é a escolha
certa quando algo precisa de uma *tela de verdade* — projeção sem projetor, ensaio, tela de
palco a mais, streaming via Sunshine/Parsec.

## 4.2 Telão surround + soft-edge blend

Dois projetores em clone (Win+P Duplicar, ou o Holyrics mandando o mesmo slide para as
duas saídas) produzem **duas cópias** da imagem e uma costura clara no meio. O Windows
não tem surround nativo sem NVIDIA Surround / AMD Eyefinity.

Caminho escolhido (independente da GPU):

1. forçar topologia **Estender** e, se os dois físicos ainda compartilham a origem,
   posicioná-los lado a lado;
2. dimensionar o monitor virtual para o canvas único
   `largura = Σ larguras − overlap × (n−1)` (dois Full HD + 192 px → 3648×1080);
3. o Holyrics projeta nessa tela só (API: Tela pública = origem do virtual;
   `screen_2` nos projetores é ocultada);
4. o app captura o canvas (`CopyFromScreen`) e pinta uma janela sem borda em cada
   projetor, com fade em cosseno **compensado** (`pow(s, 1/gama)`, padrão 2.2) na
   zona compartilhada. Sem a inversão, dois projetores somam ~0,44 no meio e a
   junta fica preta — o preview no monitor (uma tela só, emissiva) não mostra isso.
   Gama, ganho e largura do fade aplicam-se no próximo quadro nas fatias físicas
   (menu **Ajustar blend do telão**); o overlay reafirma TOPMOST para o Holyrics
   não cobrir os projetores com janelas próprias.

A zona de overlap contém **os mesmos pixels** nas duas fatias — é o que diferencia
blend de um corte seco ou de um clone. `SurroundEnabled` é opt-in: 1 monitor não
muda; 3 telas deixam o primário com o operador.

Não usamos NVIDIA Surround: quebra o desktop do operador, é específico de vendor e
não entrega soft-edge de projetor.

## 5. Interface do app (tray)

- Chave liga/desliga **Monitor Virtual** (efeito imediato, sem UAC).
- Chave **Telão surround** (2 projetores = 1 tela, com overposição configurável).
- **Ajustar blend do telão**: sliders ao vivo de overposição, gama e intensidade.
  Abre pelo menu da bandeja, por duplo clique no ícone, pela janela do programa na
  barra de tarefas (botão dedicado) ou pela janela de visualização.
- Janela **Monitor Virtual** (`PainelForm`) com ícone na barra de tarefas: o
  `ApplicationContext` da bandeja sozinho não deixa o app na taskbar; o painel é a
  presença visível. Clique esquerdo no `NotifyIcon` abre este painel — não depende
  do `ContextMenuStrip` persistir.
- Overlays das fatias: `WS_EX_NOACTIVATE` + `WS_EX_TRANSPARENT`, `KeepOnTop` sem
  `SWP_SHOWWINDOW`, e pausa de z-order enquanto o menu da bandeja está aberto.
  Sem isso o Windows 10 fecha o menu sozinho (foco roubado / overflow da bandeja).
- Resolução: `1920×1080` (padrão), `1280×720`, `3840×1080` (2× Full HD), `3840×2160`, personalizada.
- Nome amigável exibido: `Projecao Holyrics`.
- "Iniciar com o Windows" / "Iniciar o Holyrics junto".
- Botão **Testar tela** — janela full-screen colorida no monitor virtual, confirma que o
  destino certo foi escolhido.
- Status: driver instalado ✔ · monitor ativo ✔ · Holyrics detectado ✔.
- **Diagnóstico/reparar**: reinstala o device, reaplica topologia, exporta log.
- Log em `%ProgramData%\MonitorVirtual\logs\` (Serilog, rolling).

---

## 6. Riscos e mitigações

| Risco | Mitigação |
|---|---|
| SmartScreen/antivírus bloqueia o instalador não assinado | Assinar o instalador com certificado OV/EV de code signing (item de custo obrigatório para distribuir) |
| Política de driver corporativa/Secure Boot | IddCx é UMDF (não kernel), instala com o `.cat` confiável; documentar fallback para o pacote Parsec |
| Upgrade de build do Windows remove device root-enumerated | Watchdog detecta ausência e reprovisiona; serviço roda em `Automatic (Delayed Start)` |
| Holyrics não vê o monitor criado depois dele abrir | Modo launcher (ordem garantida) + botão "reiniciar Holyrics" |
| Monitor virtual vira primário e o Windows "some" | Forçar não-primário na topologia a cada reconciliação |
| Escala/DPI diferente distorce a projeção | Fixar escala 100% no monitor virtual |
| ARM64 | Fora de escopo do MVP (driver é x64) |
| Licença MIT do VDD | Incluir `THIRD-PARTY-NOTICES.txt` com o aviso de copyright |

---

## 6.1 Validação em máquina real (26/08/2026)

Testado em Windows 11 build 26200, Radeon RX 5500 XT, monitor único ultrawide 3440×1440.

| Passo | Resultado |
|---|---|
| `mvcli install` | `oem27.inf` registrado, nó `Root\MttVDD` criado, driver instalado — **sem reinício** |
| Monitor após instalar | ativo em `\\.\DISPLAY7`, 800×600@30 (padrão do driver), posicionado pelo Windows em (3440,0) |
| `mvcli apply --w 1920 --h 1080 --hz 60` | XML gravado, dispositivo reiniciado, monitor em **1920×1080@60 em (3440,0)** |
| Topologia | estendida, monitor virtual **não** primário |
| `VDDPATH` | apontando para `%ProgramData%\MonitorVirtual\driver-config` (não tocamos em `C:\VirtualDisplayDriver`) |
| App de bandeja | inicia elevado, watchdog rodando, sem log de ruído |
| Janela de visualização | espelha o monitor virtual via `CopyFromScreen`; captura validada |
| Instalador silencioso | `/VERYSILENT` instala driver + liga monitor + cria tarefa de logon |
| Tarefa de logon | dispara `MonitorVirtual.exe --background` **elevado e sem UAC** — confirmado por `schtasks /Run` |

**Três defeitos encontrados só por rodar de verdade** (todos corrigidos):

1. `%ProgramData%\MonitorVirtual` nasce sem permissão de escrita para usuário comum (o app
   roda elevado e cria os arquivos). CLI e diagnósticos sem elevação não gravavam config
   nem log. Correção: na primeira execução elevada, `icacls` concede *Modificar* ao SID
   `S-1-5-32-545` (grupo Usuários, independente de idioma).
2. A tarefa de logon apontava para `mvcli.exe --background`, porque o instalador chama
   `mvcli startup-on` e `Environment.ProcessPath` é a CLI. No logon subiria a CLI, não o
   app. Correção: `startup-on` resolve `MonitorVirtual.exe` ao lado do executável, e
   `StartupTask.Enable` valida o caminho.
3. `AppMutex` no instalador **cancela** a instalação silenciosa quando o app está aberto
   (a caixa suprimida assume "Cancelar"). Trocado por `CloseApplications` +
   `taskkill` em `PrepareToInstall`, que funciona igual nos dois modos.

**Achado decisivo (Fase 0):** com o Holyrics **já aberto** quando o monitor virtual nasceu,
a tela nova **não apareceu** na lista de monitores do Holyrics. Confirma a hipótese de
hot-plug: o Holyrics (Java) monta a lista de telas na inicialização. Consequências no
produto — todas implementadas:

- o **start ordenado deixa de ser conveniência e vira requisito**: o app sobe o monitor no
  logon e só então abre o Holyrics (`LaunchHolyrics`, via `explorer.exe` para não herdar
  elevação);
- detecção automática do caso ruim: quando o monitor passa de inativo → ativo com o
  Holyrics já rodando, o app avisa por balão e mostra no menu **"Reiniciar o Holyrics"**;
- `AutoRestartHolyrics` (desligado por padrão) faz isso sozinho — desligado porque fechar
  o Holyrics no meio do culto é destrutivo;
- o instalador precisa orientar a **tirar o Holyrics da inicialização do Windows**.

**Achado secundário:** o nome do adaptador **muda** ao reiniciar o dispositivo
(`\\.\DISPLAY7` → `\\.\DISPLAY8`). Ou seja, `DeviceName` não serve como identidade estável.
O que se mantém estável é a **geometria** (resolução + posição), que é justamente o que o
provisionador fixa. Se algum dia o Holyrics guardar a tela pelo nome do dispositivo, será
preciso minimizar reinícios do device — hoje só reiniciamos quando a resolução muda.

## 7. Fases

**Fase 0 — prova de conceito (0,5–1 dia).** Instalar o VDD manualmente na máquina de teste,
confirmar que o Holyrics lista e projeta no monitor virtual, e confirmar o comportamento de
hot-plug (Holyrics aberto antes vs. depois). *Essa medição define o quanto do modo launcher
é obrigatório.*

**Fase 1 — MVP (3–5 dias).** Tray app + serviço + instalador Inno Setup, ligar/desligar,
resolução, topologia estendida, watchdog, autostart, log, passo guiado de configuração do
Holyrics.

**Fase 2 — produto (1–2 semanas).** Instalador assinado, tela de diagnóstico, atualização
automática, integração de status via API do Holyrics, modo multi-tela (público + palco),
telemetria opt-in de erro.

**Fase 3 — driver próprio (opcional).** Fork do sample IddCx, EDID de marca, certificado EV
+ attestation signing no Partner Center; elimina dependência de terceiros e permite
instalação totalmente silenciosa.

---

## 8. Stack

- .NET 8 (C#), WPF para a UI, `Microsoft.Extensions.Hosting.WindowsServices` para o serviço.
- `PublishSingleFile=true`, `SelfContained=true`, `RuntimeIdentifier=win-x64` → um `.exe`.
- P/Invoke: `setupapi.dll`, `newdev.dll`, `cfgmgr32.dll`, `user32.dll` (CCD).
- Inno Setup 6 para o instalador (mais simples que WiX para este porte).
- Serilog + testes de integração manuais em VM Win10 22H2 e Win11 24H2.

## Fontes

- https://github.com/VirtualDrivers/Virtual-Display-Driver
- https://virtualdrivers-virtual-display-driver.mintlify.app/installation
- https://github.com/nomi-san/parsec-vdd — `docs/PARSEC_VDD_SPECS.md`, `docs/VDD_CLI_USAGE.md`
- https://github.com/MolotovCherry/virtual-display-rs
- https://github.com/holyrics/API-Server — `README-en.md`
- https://learn.microsoft.com/en-us/windows-hardware/drivers/install/trusted-publishers-certificate-store
