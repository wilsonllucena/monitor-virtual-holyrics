# Ativar a assinatura de código (depois da aprovação da SignPath)

O fluxo de release **já está pronto**: ele detecta sozinho se a assinatura está
configurada. Enquanto não estiver, publica o instalador não assinado; assim que as
variáveis abaixo existirem, passa a submeter o instalador à SignPath e publica o arquivo
assinado — sem mexer em nenhuma linha de código.

## 1. Do lado da SignPath (após o e-mail de aprovação)

1. Entre em https://app.signpath.io e **ative MFA** na sua conta.
2. Na organização criada para o projeto, anote:
   - **Organization ID** (um GUID, em *Organization → Settings*);
   - **Project slug** (ex.: `monitor-virtual-holyrics`);
   - **Signing policy slug** (normalmente `release-signing`; existe também
     `test-signing`, útil para o primeiro teste).
3. Crie um **API token** de CI (*User → API Tokens*) e copie o valor — ele só aparece uma
   vez.
4. Configure o **artifact configuration** do projeto para o instalador (arquivo `.exe`
   dentro do artefato `instalador`, que o build publica como ZIP).

## 2. Do lado do GitHub

Em **Settings → Secrets and variables → Actions**:

**Variables** (visíveis, não são segredo):

| Nome | Valor |
|---|---|
| `SIGNPATH_ORGANIZATION_ID` | GUID da organização |
| `SIGNPATH_PROJECT_SLUG` | slug do projeto |
| `SIGNPATH_SIGNING_POLICY_SLUG` | `release-signing` (ou `test-signing` no teste) |

**Secrets**:

| Nome | Valor |
|---|---|
| `SIGNPATH_API_TOKEN` | token de API criado no passo 1 |

## 3. Testar antes de publicar

Rode o workflow **release** manualmente (*Actions → release → Run workflow*). Em execução
manual nada é publicado: o instalador fica nos artefatos da execução e o log mostra o
status da assinatura.

Se `SIGNPATH_ORGANIZATION_ID` estiver definido e o instalador voltar **sem assinatura
válida**, o workflow falha de propósito — melhor quebrar o build do que publicar um
executável que todo mundo achará que está assinado.

## 4. Publicar de verdade

```powershell
# ajuste a versão em installer\MonitorVirtual.iss e em Directory.Build.props antes
git tag v0.1.1
git push origin v0.1.1
```

A tag dispara o workflow, que cria a release (se não existir), assina e anexa o instalador.

## 5. Depois da primeira release assinada

- Atualize o **status** em [CODE_SIGNING_POLICY.md](../CODE_SIGNING_POLICY.md): a
  candidatura deixou de estar pendente.
- Ajuste o README e as notas de release: o aviso do SmartScreen deixa de ser esperado
  (pode ainda aparecer nas primeiras semanas, até o certificado ganhar reputação).
- Mantenha o crédito exigido pelos termos, que já está na política:
  *Free code signing provided by SignPath.io, certificate by SignPath Foundation.*
