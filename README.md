# Monitor Virtual para Holyrics

[![build](https://github.com/wilsonllucena/monitor-virtual-holyrics/actions/workflows/build.yml/badge.svg)](https://github.com/wilsonllucena/monitor-virtual-holyrics/actions/workflows/build.yml)
[![licença MIT](https://img.shields.io/badge/licen%C3%A7a-MIT-blue.svg)](LICENSE)

**Projete o Holyrics numa tela a mais sem ter saída de vídeo sobrando na placa.**

Este programa cria um **monitor virtual** no Windows 10/11. Para o Windows — e para o
Holyrics — ele é uma tela igual a qualquer outra: aparece nas configurações de vídeo, tem
resolução, posição e pode receber a projeção. A diferença é que não existe cabo nenhum:
é software.

> Feito para a igreja que quer uma tela de retorno, um palco ou um segundo público, mas
> descobriu que a placa de vídeo só tem duas saídas — e as duas já estão ocupadas.

## Para que serve

- **Terceira tela sem hardware** — projeção pública, retorno de palco e telão de câmera,
  mesmo com placa de duas saídas.
- **Capturar a projeção no OBS** para transmissão, sem tirar o projetor do lugar.
- **Ensaiar em casa ou no meio da semana** sem projetor ligado: a janela de visualização
  mostra exatamente o que iria para a tela.
- **Testar o culto antes** — conferir letras, fundos e vídeos na resolução real do projetor.
- Servir de tela para **Sunshine/Parsec, Resolume Arena, VNC** e afins.

## Requisitos

- Windows 10 (versão 2004 ou mais nova) ou Windows 11, **64 bits**.
- Conta de administrador para instalar (o uso no dia a dia não pede senha).
- Holyrics instalado (qualquer versão recente).

## Instalação

1. Baixe o instalador na aba **[Releases](../../releases)**:
   `MonitorVirtualSetup-x.y.z.exe`.
2. Clique com o botão direito → **Executar como administrador**.
3. O Windows vai mostrar um aviso azul do **SmartScreen** ("O Windows protegeu o
   computador"). Isso acontece porque o instalador ainda não tem certificado digital pago.
   Clique em **Mais informações → Executar assim mesmo**.
4. Siga o assistente. Deixe marcada a opção **Iniciar o Monitor Virtual junto com o
   Windows** — é ela que garante a tela pronta antes do Holyrics abrir.

Ao final o instalador já deixa tudo pronto: driver instalado, monitor virtual criado e o
ícone perto do relógio (bandeja do Windows).

**Nada de reiniciar o computador**: a tela nova aparece na hora.

## Configurando no Holyrics (só uma vez)

> ⚠️ **A ordem importa.** O Holyrics monta a lista de telas **no momento em que abre**.
> Se o monitor virtual nascer depois, ele não aparece na lista. É o erro mais comum.

1. Confirme que o monitor virtual está ligado: clique no ícone do **Monitor Virtual** perto
   do relógio — deve dizer *Monitor virtual ativo em 1920x1080...*
2. **Feche e abra o Holyrics.** Se ele já estava aberto, o próprio programa avisa e oferece
   o atalho **Reiniciar programa** no menu do ícone.
3. No Holyrics, vá em **Configurações → Projeção** (ou no assistente de telas).
4. Na lista de monitores vai aparecer um chamado **Virtual Display Driver**. Escolha ele
   como **Tela pública**.
   - Na dúvida sobre qual é qual, use **Testar tela...** no menu do Monitor Virtual: ele
     pinta a tela virtual com barras coloridas e escreve o nome dela.
5. Pronto. Projete uma música para conferir.

### Para não repetir isso toda semana

Deixe o Monitor Virtual abrir o Holyrics para você, na ordem certa:

1. Menu do ícone → **Configurações → Programas que usam o monitor → Detectar**.
2. O Holyrics aparece na lista com *abre depois = sim*.
3. **Tire o Holyrics da inicialização do Windows** (senão ele abre cedo demais, antes do
   monitor existir).

A partir daí, ao ligar o computador: o monitor virtual sobe primeiro, o Holyrics abre em
seguida e já enxerga a tela.

## Usando no dia a dia

Clique no ícone do Monitor Virtual perto do relógio:

| Opção | O que faz |
|---|---|
| **Ligar / desligar monitor virtual** | Cria ou remove a tela na hora, sem pedir senha |
| **Ver o monitor em uma janela** | Espelha a tela virtual numa janela — acompanhe a projeção sem projetor |
| **Testar tela...** | Mostra barras coloridas na tela virtual para você identificar qual é |
| **Reiniciar programa** | Aparece quando o Holyrics abriu antes do monitor; reinicia ele para reconhecer a tela |
| **Configurações...** | Resolução, posição, programas, início automático |
| **Reparar / reinstalar driver** | Conserta a tela após uma atualização do Windows ou da placa de vídeo |

O programa também se vigia sozinho: se a tela sumir depois de uma suspensão, de uma
atualização de driver de vídeo ou de alguém apertar `Win+P`, ele recria e recoloca a tela
no lugar em poucos segundos.

### Resolução recomendada

Use a **mesma resolução do projetor** (normalmente 1920x1080). Em
**Configurações → Resolução** dá para escolher entre os padrões ou digitar uma
personalizada.

## Problemas comuns

**A tela virtual não aparece na lista do Holyrics**
Feche e abra o Holyrics. Ele só enxerga telas que já existiam quando abriu.

**No Resolume a letra do Holyrics aparece e o fundo some (preview em xadrez)**
Isso não é o monitor virtual: é a saída **NDI nativa do Holyrics** (v2.29+), que
manda só a camada de texto com fundo transparente. O Resolume trata o resto como
alpha (xadrez no preview, preto na composition).

1. No Holyrics, ligue **Configurações → API Server** e copie o token.
2. No Monitor Virtual: **Configurações → Holyrics — API local**, cole o token e
   deixe marcada **Incluir papel de fundo na saída NDI do Holyrics**.
3. Clique em **Testar API** (ou rode `mvcli holyrics --ndi-fundo`).
4. No Resolume, recarregue o clip NDI (`DESKTOP-… (Holyrics - NDI 1)`).

O NDI do Holyrics **não envia vídeo de fundo** (limitação do próprio Holyrics).
Para fundo em vídeo, use a tela **Virtual Display Driver** como Tela pública e
capture esse monitor no Resolume (Advanced Output / captura de tela), não o NDI
de texto.

**O Holyrics projeta na tela errada / o vídeo abre no monitor do operador**
Confira em **Configurações → Projeção** qual monitor está escolhido; use
**Testar tela...** para identificar o virtual sem chutar.

**A projeção sumiu depois que alguém apertou Win+P**
O Monitor Virtual devolve o modo *Estender* sozinho em alguns segundos. Se quiser forçar,
abra o menu do ícone e clique em **Ligar monitor virtual** de novo.

**Depois de uma atualização grande do Windows a tela sumiu**
Menu do ícone → **Reparar / reinstalar driver**.

**O antivírus ou o SmartScreen reclamou**
O executável ainda não é assinado com certificado digital. O código é aberto, o instalador
é gerado automaticamente pelo GitHub Actions a partir dele, e o driver embalado é assinado
digitalmente (SignPath Foundation) — veja a
[política de assinatura de código](CODE_SIGNING_POLICY.md).

**Quero remover tudo**
Painel de Controle → Programas → **Monitor Virtual para Holyrics** → Desinstalar. Ele
remove a tela virtual, o driver e o início automático.

## Como funciona (resumo técnico)

O Windows só cria monitores "de mentira" por meio de um **Indirect Display Driver
(IddCx)**. Este projeto embala o driver de código aberto
[Virtual Display Driver](https://github.com/VirtualDrivers/Virtual-Display-Driver)
(licença MIT, catálogo assinado pela SignPath Foundation) e coloca em volta dele tudo o que
falta para o uso em igreja:

- instala o driver e cria o dispositivo sem `devcon`/`nefconw`;
- liga e desliga a tela habilitando o dispositivo — sem pedir UAC no dia a dia, graças a
  uma tarefa de logon elevada;
- força a topologia **Estender**, a causa nº 1 de "o Holyrics não projeta";
- mantém **resolução e posição fixas**, para o Holyrics não perder a configuração da tela;
- nunca deixa a tela virtual virar o monitor principal;
- vigia e reprovisiona depois de suspensão, atualização de GPU ou mudança em `Win+P`;
- inicia os programas **depois** que a tela está pronta.

Detalhes de arquitetura, decisões e resultados dos testes em [DESIGN.md](DESIGN.md).

## Para desenvolvedores

```powershell
winget install Microsoft.DotNet.SDK.8
powershell -ExecutionPolicy Bypass -File tools\build.ps1            # gera publish\
winget install JRSoftware.InnoSetup
powershell -ExecutionPolicy Bypass -File tools\build-installer.ps1  # gera dist\
```

Estrutura:

```
driver/                    driver embalado (INF, CAT, DLL) — MIT, ver THIRD-PARTY-NOTICES.txt
src/MonitorVirtual.Core/   SetupAPI, topologia de vídeo, configuração, integração
src/MonitorVirtual.App/    aplicativo de bandeja (WinForms) — o produto
src/MonitorVirtual.Cli/    mvcli.exe — instalação silenciosa e diagnóstico
installer/                 script Inno Setup
tools/                     build.ps1, build-installer.ps1, fetch-driver.ps1
```

Sem dependências NuGet: só a biblioteca padrão do .NET 8.

### Linha de comando (diagnóstico)

```powershell
mvcli status          # driver, dispositivo, monitor, topologia
mvcli displays        # todas as telas e suas geometrias
mvcli on | off        # liga/desliga (precisa de Administrador)
mvcli apps --detect   # encontra Holyrics, Resolume, OBS
mvcli holyrics --ndi-fundo  # inclui o papel de fundo na saída NDI
mvcli watch           # watchdog em primeiro plano
```

### Instalação silenciosa (várias máquinas)

```powershell
MonitorVirtualSetup-0.1.0.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /TASKS=autostart
```

## Assinatura de código e privacidade

Os instaladores são gerados por **GitHub Actions** a partir do código deste repositório —
nunca da máquina de um desenvolvedor. A candidatura à assinatura gratuita da
**SignPath Foundation** foi enviada; até a aprovação, os binários não são assinados e o
SmartScreen avisa. Detalhes em [CODE_SIGNING_POLICY.md](CODE_SIGNING_POLICY.md).

**O programa não coleta nem envia dado nenhum.** Sem telemetria, sem conta, sem servidor.
As únicas coisas gravadas são a configuração e os logs locais, em
`%ProgramData%\MonitorVirtual\`, removidos na desinstalação.

## Contribuindo

Problemas e sugestões são bem-vindos nas **Issues**. Se você usa numa igreja e algo não
funcionou, o log ajuda muito: menu do ícone → **Abrir pasta de logs**
(`%ProgramData%\MonitorVirtual\logs`).

## Licença e créditos

Código deste projeto sob licença [MIT](LICENSE).

Embala o driver [Virtual Display Driver](https://github.com/VirtualDrivers/Virtual-Display-Driver)
de MikeTheTech (MIT) — veja [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).

Projeto independente, **sem vínculo** com o Holyrics ou com o Virtual Display Driver.
"Holyrics" é marca do respectivo autor.
