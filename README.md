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

Ao final o instalador já deixa tudo pronto: driver instalado, monitor virtual criado, o
ícone perto do relógio (bandeja) e a janela **Monitor Virtual** na barra de tarefas
(dois projetores + junta azul). Clique nela — ou clique **esquerdo** no ícone da bandeja — para
abrir Configurações e **Ajustar blend do telão** sem depender do menu de clique direito.

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

O programa tem **duas** portas de entrada, de propósito: o menu da bandeja e a janela
na barra de tarefas. O ícone é o mesmo nos dois sítios: **dois projetores cinza e a
junta azul no meio** (atalho da área de trabalho, exe, taskbar e janela). Clique
**esquerdo** no ícone perto do relógio abre a janela. Clique **direito** abre o menu.
**Duplo clique** no ícone, com o telão surround ligado, abre direto **Ajustar blend do telão**.
Clique no ícone dentro da janela também abre o blend.

| Opção | O que faz |
|---|---|
| **Janela do Monitor Virtual** (barra de tarefas) | Botão grande **Ajustar blend do telão** + Configurações |
| **Ligar / desligar monitor virtual** | Cria ou remove a tela na hora, sem pedir senha |
| **Ligar / desligar telão surround** | Dois projetores viram **uma tela só**, com blend na junta |
| **Ajustar blend do telão...** | Sliders de overposição, gama e intensidade — valem **ao vivo no projetor** |
| **Ver o monitor em uma janela** | Espelha a tela virtual numa janela — acompanhe a projeção sem projetor |
| **Testar tela...** | Mostra barras coloridas (ou o padrão ESQUERDA/DIREITA no surround) |
| **Reiniciar programa** | Aparece quando o Holyrics abriu antes do monitor; reinicia ele para reconhecer a tela |
| **Configurações...** | Resolução, posição, programas, início automático |
| **Reparar / reinstalar driver** | Conserta a tela após uma atualização do Windows ou da placa de vídeo |

O programa também se vigia sozinho: se a tela sumir depois de uma suspensão, de uma
atualização de driver de vídeo ou de alguém apertar `Win+P`, ele recria e recoloca a tela
no lugar em poucos segundos.

### Resolução recomendada

Use a **mesma resolução do projetor** (normalmente 1920x1080). Em
**Configurações → Resolução** dá para escolher entre os padrões ou digitar uma
personalizada. No surround o app ajusta sozinho o canvas (ex.: 3840×1080).

## Telão com 2 projetores (surround + blend)

Se os dois projetores mostram o **mesmo slide** lado a lado, o Windows (ou o
Holyrics) está em **clone/espelho**. Não é surround: a imagem não estica, e a
junta no meio fica uma costura clara.

O Monitor Virtual faz o telão virar **um monitor só**, no mesmo espírito do
NVIDIA Surround: a barra de tarefas atravessa a parede de ponta a ponta.

1. Detecta os projetores físicos.
2. Sai do clone e força **Estender**.
3. **Com GPU NVIDIA:** liga Surround/Mosaic nos dois HDMI. O Windows passa a
   ver um monitor lógico (dois Full HD com blend de 192 px → **3648×1080**;
   sem overposição → **3840×1080**). A taskbar é uma faixa só. O Holyrics
   projeta nesse telão. O monitor virtual IddCx sai do desktop (não vira
   tela extra).
4. **Se o driver recusar o Mosaic:** o canvas virtual vira o **primário**
   naquele tamanho, o app recorta esquerda/direita com **soft-edge blend**.
   A taskbar mora no canvas, então também atravessa o telão na parede.

O preview no monitor do PC **não** é a verdade da parede. Dois projetores
somam luz: uma curva que parece boa no preview deixa uma **faixa preta** no
telão. Os sliders mexem nas fatias (ou no scanout NVIDIA), no próximo quadro.

### Como ligar

1. Menu do ícone → **Ligar telão surround (2 projetores = 1 tela)**.
2. Ou **Configurações → Telão surround / blending**: marque os dois projetores.
3. **Feche e abra o Holyrics** (ou deixe o app abri-lo **depois** do surround).
   Em **Projeção**, a Tela pública deve ser o **telão único** (no Surround
   NVIDIA ele aparece como um monitor largo; no fallback, o Virtual Display
   Driver). O app tenta apontar isso sozinho se o token da API estiver em
   Configurações.
4. **Testar tela...**: no projetor esquerdo deve aparecer sobretudo **ESQUERDA**,
   no direito **DIREITA**. Se os dois mostram as duas palavras, ainda está em
   clone.

### Ajustar a junta até ficar invisível (olhe o TELÃO)

Abra **Ajustar blend do telão** por qualquer um destes caminhos (o painel fica na
frente, na tela do operador):

1. Janela **Monitor Virtual** na barra de tarefas → botão verde **Ajustar blend do telão**.
2. Clique esquerdo no ícone da bandeja → o mesmo botão.
3. Duplo clique no ícone da bandeja (com surround ligado).
4. Clique direito → **Ajustar blend do telão...**
5. Na janela de visualização: menu **Programa** ou a barra **Ajustar blend do telão**.

Mexa olhando a **parede**, não a janela de visualização:

| Controle | O que faz | Faixa preta no meio | Costura clara |
|---|---|---|---|
| **Overposição (px)** | Largura do fade na junta (128–256 é o ponto de partida) | — | Aumente um pouco |
| **Gama** | Compensa a gama da lâmpada. 2,2 clareia o overlap; 1,0 é linear | **Aumente** (2,2–2,8) | Diminua |
| **Intensidade** | Ganho extra na zona de overlap | **Aumente** (> 1,00) | Diminua |

Marque **Mostrar padrão de junta** (fundo branco): se o centro ficar mais
escuro que as laterais, a curva ainda está baixa. Ajuste até a faixa sumir.
**Fechar e guardar** grava em `config.json` — sem reinstalar.

A **gama maior clareia** a junta nos projetores (compensação `pow(cosseno, 1/gama)`).
A v0.2.0 usava a potência ao contrário e escurecia o meio.

Se os lados saíram trocados, marque **Inverter esquerda/direita**.

Com **1 monitor** o surround não faz nada. Com **3 telas** (mesa + 2 projetores)
o primário fica com o operador e só os projetores entram no telão.

Quem usa o Resolume para o telão pode deixar o surround **desligado** e continuar
no Advanced Output. Este modo é o caminho direto Holyrics → telão, quando o
Resolume não faz o blend certo.

## Problemas comuns

**A tela virtual não aparece na lista do Holyrics**
Feche e abra o Holyrics. Ele só enxerga telas que já existiam quando abriu.

**A barra de tarefas só aparece na metade esquerda do telão**
O Windows ainda está tratando os projetores como dois monitores. Ligue
**Telão surround**. Com GPU NVIDIA o app junta as saídas num monitor só
(taskbar de ponta a ponta). Se o driver recusar, o canvas virtual vira o
primário e a barra atravessa a parede nas fatias. Com 1 monitor o surround
não altera nada.

**No telão aparecem 2 telas / o mesmo slide duas vezes**
Isso é clone, não surround. Ligue **Telão surround** no menu, confirme que o
Holyrics está projetando no **Virtual Display Driver** e use **Testar tela...**.
Ajuste a overposição até a costura do meio sumir. Com 1 monitor o surround
não altera nada.

**Abri o Holyrics e o telão voltou a ficar dividido**
O Holyrics lista os monitores na abertura e costuma escolher os dois projetores
físicos em vez do canvas único. O app, com o token da API em
**Configurações → Holyrics — API local**, aponta a **Tela pública** para o
Virtual Display Driver e oculta `screen_2`/`screen_3` que caíam nos projetores.
Deixe o Monitor Virtual abrir o Holyrics (depois do surround). Se o Holyrics
já estava aberto, use **Reiniciar programa** no menu depois do telão ligar.

**Faixa preta vertical no meio do telão (preview no PC parece ok)**
Dois projetores somam luz; o preview de monitor não. Abra **Ajustar blend do
telão** (janela na barra de tarefas, ou duplo clique no ícone): aumente a **gama**
(2,2–2,8) ou a **intensidade**, olhando a parede.
Marque **Mostrar padrão de junta**. Os sliders valem no próximo quadro nas
fatias físicas, sem reinstalar.

**Clique direito no ícone da bandeja e o menu fecha sozinho**
Isso acontecia na v0.2.1 com o telão surround: as fatias TOPMOST nos projetores
(e o overflow «mostrar ícones ocultos» do Windows 10) tiravam o foco do menu.
A v0.2.2 mantém o menu aberto. Se ainda falhar, **clique esquerdo** no ícone ou
use a janela **Monitor Virtual** na barra de tarefas — o botão **Ajustar blend
do telão** não depende do menu.

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

**O instalador terminou com "CreateProcess falhou; código 740"**
A versão 0.1.0 tentava abrir `MonitorVirtual.exe` no token não-elevado do assistente.
Atualize para **0.1.1** (este repositório). Se ainda tiver a 0.1.0 instalada, abra
**Monitor Virtual** pelo menu Iniciar — o Windows pede UAC e o app sobe.

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
- **telão surround**: dois projetores viram um monitor lógico (NVIDIA Surround
  quando o GPU deixa — taskbar contínua de ponta a ponta; senão canvas virtual
  primário + blend nas saídas). Opt-in; 1 monitor não muda nada;
- mantém **resolução e posição fixas**, para o Holyrics não perder a configuração da tela;
- nunca deixa a tela virtual virar o monitor principal;
- vigia e reprovisiona depois de suspensão, atualização de GPU ou mudança em `Win+P`;
- inicia os programas **depois** que a tela está pronta.

Detalhes de arquitetura, decisões e resultados dos testes em [DESIGN.md](DESIGN.md).

## Para desenvolvedores

Tudo abaixo é no **Windows 10 64 bits (versão 2004 / build 19041 ou mais nova)** ou Windows 11.
O projeto é `net8.0-windows` + WinForms + driver IddCx — não compila nem roda em Linux/macOS.

1. Instale o **SDK do .NET 8** (não basta o runtime):
   ```powershell
   winget install Microsoft.DotNet.SDK.8
   ```
   Confira com `dotnet --list-sdks` — precisa aparecer `8.0.x`.
2. Abra `MonitorVirtual.sln` no Visual Studio 2022 (carga *Desenvolvimento para desktop com .NET*)
   **como Administrador**, ou use o terminal:
   ```powershell
   powershell -ExecutionPolicy Bypass -File tools\build.ps1            # gera publish\
   .\publish\MonitorVirtual.exe                                        # pede UAC na primeira execução
   ```
3. (Opcional) para gerar o instalador:
   ```powershell
   winget install JRSoftware.InnoSetup
   powershell -ExecutionPolicy Bypass -File tools\build-installer.ps1  # gera dist\
   ```

O manifesto do app é `asInvoker`: se você abrir o `.exe` sem elevação, ele relança com `runas` e o Windows mostra o UAC. Para **depurar no Visual Studio**, abra o VS como Administrador — senão o F5 perde o depurador no relançamento.

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
mvcli holyrics --tela-unica # Tela pública = monitor virtual (some a divisão)
mvcli surround        # detecta projetores e mostra o canvas único
mvcli surround --on --overlap 192 --gamma 2.2 --gain 1
mvcli watch           # watchdog em primeiro plano
```

### Instalação silenciosa (várias máquinas)

```powershell
MonitorVirtualSetup-0.3.0.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /TASKS=autostart
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
