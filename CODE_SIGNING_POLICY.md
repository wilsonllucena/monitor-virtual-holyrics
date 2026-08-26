# Política de assinatura de código

_(Code signing policy — versão em inglês ao final.)_

## Status atual

Candidatura enviada à **SignPath Foundation** para assinatura gratuita de código aberto.
**Enquanto não for aprovada, os binários publicados nas Releases não são assinados** — o
SmartScreen do Windows vai exibir um aviso ao executar o instalador. Isso está declarado
também no [README](README.md) e nas notas de cada release.

## Equipe e papéis

| Papel | Quem | Responsabilidade |
|---|---|---|
| **Author** | [@wilsonllucena](https://github.com/wilsonllucena) (Wilson Lima) | Escreve o código e faz commits diretos no `main` |
| **Reviewer** | [@wilsonllucena](https://github.com/wilsonllucena) | Revisa toda contribuição externa antes do merge |
| **Approver** | [@wilsonllucena](https://github.com/wilsonllucena) | Aprova cada solicitação de assinatura |

Todos os membros usam **autenticação de dois fatores (MFA)** no GitHub e na SignPath.

## O que é assinado

Apenas artefatos gerados a partir do código-fonte deste repositório:

- `MonitorVirtualSetup-x.y.z.exe` — instalador (Inno Setup);
- `MonitorVirtual.exe` — aplicativo de bandeja (.NET 8);
- `mvcli.exe` — utilitário de linha de comando.

O pacote de driver embalado (`driver/MttVDD.*`) **não é assinado por este projeto**: ele já
vem assinado pelo projeto de origem
([Virtual Display Driver](https://github.com/VirtualDrivers/Virtual-Display-Driver),
catálogo assinado pela SignPath Foundation) e é redistribuído sem modificação, sob licença
MIT. Veja [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).

## Como os binários são construídos

Todo artefato publicado sai de um build automatizado no **GitHub Actions**, em runners
hospedados pelo GitHub, a partir do código deste repositório:

- [`.github/workflows/build.yml`](.github/workflows/build.yml) — compila, gera o instalador
  e confere a assinatura do catálogo do driver;
- [`.github/workflows/release.yml`](.github/workflows/release.yml) — publica o instalador na
  release e submete a solicitação de assinatura.

Nenhum binário é assinado a partir de máquina de desenvolvedor.

## Privacidade

**O programa não coleta, armazena nem transmite dados pessoais.** Não há telemetria, não há
comunicação com servidores do projeto e não é preciso criar conta.

O que fica gravado é local à máquina e some na desinstalação:

- `%ProgramData%\MonitorVirtual\config.json` — preferências (resolução, posição, programas);
- `%ProgramData%\MonitorVirtual\logs\` — registro de diagnóstico, mantido por 30 dias.

Opcionalmente, e só se o usuário configurar, o programa consulta a **API local do Holyrics**
(`http://localhost:8091`) para exibir status. É comunicação com o próprio computador; nada
sai da máquina.

## Créditos

Free code signing provided by [SignPath.io](https://signpath.io/), certificate by
[SignPath Foundation](https://signpath.org/).

---

# Code signing policy (English)

**Status:** application submitted to the SignPath Foundation. Until approved, published
binaries are **not signed** and Windows SmartScreen will warn on first run.

**Team roles** — all members use multi-factor authentication on GitHub and SignPath:

- **Author / Reviewer / Approver:** [@wilsonllucena](https://github.com/wilsonllucena)
  (Wilson Lima), sole maintainer.

**Signed artifacts:** `MonitorVirtualSetup-x.y.z.exe`, `MonitorVirtual.exe`, `mvcli.exe` —
all built from this repository. The bundled display driver (`driver/MttVDD.*`) is
redistributed unmodified from the
[Virtual Display Driver](https://github.com/VirtualDrivers/Virtual-Display-Driver) project
(MIT) and is already signed by its own maintainers.

**Build process:** every published artifact is produced by GitHub Actions on
GitHub-hosted runners from the source in this repository. No binary is ever signed from a
developer machine.

**Privacy:** the application collects, stores and transmits **no** personal data. No
telemetry, no accounts, no network calls to project servers. Local configuration and
diagnostic logs live under `%ProgramData%\MonitorVirtual\` and are removed on uninstall.

Free code signing provided by [SignPath.io](https://signpath.io/), certificate by
[SignPath Foundation](https://signpath.org/).
