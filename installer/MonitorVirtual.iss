; Instalador do Monitor Virtual para Holyrics (Inno Setup 6)
; Compilar:  "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\MonitorVirtual.iss
; Requer que tools\build.ps1 tenha rodado antes (gera .\publish).

#define MyAppName "Monitor Virtual para Holyrics"
#define MyAppShortName "Monitor Virtual"
#define MyAppVersion "0.2.1"
#define MyAppPublisher "Wilson Lima"
#define MyAppUrl "https://github.com/wilsonllucena/monitor-virtual-holyrics"
#define MyAppExe "MonitorVirtual.exe"

[Setup]
AppId={{B7C2F1A4-6E3D-4B58-9A21-8F5D0C7E9A31}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppUrl}
AppSupportURL={#MyAppUrl}/issues
AppUpdatesURL={#MyAppUrl}/releases
; metadados exigidos para assinatura de código (SignPath Foundation)
VersionInfoVersion={#MyAppVersion}
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=Instalador do {#MyAppName}
VersionInfoCopyright=Copyright (C) 2026 Wilson Lima
DefaultDirName={autopf}\MonitorVirtual
DefaultGroupName={#MyAppShortName}
DisableProgramGroupPage=yes
OutputDir=..\dist
OutputBaseFilename=MonitorVirtualSetup-{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; o driver so instala em x64
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
MinVersion=10.0.19041
UninstallDisplayIcon={app}\{#MyAppExe}
; sem AppMutex de propósito: em instalação silenciosa ele cancela o setup em vez de
; fechar o app. Quem encerra a instância antiga é o PrepareToInstall (taskkill), que
; funciona igual em modo interativo e silencioso.
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Files]
; publish\ já contém MonitorVirtual.exe, mvcli.exe, driver\ e THIRD-PARTY-NOTICES.txt
Source: "..\publish\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppShortName}"; Filename: "{app}\{#MyAppExe}"
Name: "{autodesktop}\{#MyAppShortName}"; Filename: "{app}\{#MyAppExe}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na área de trabalho"; GroupDescription: "Atalhos"
Name: "autostart"; Description: "Iniciar o Monitor Virtual junto com o Windows (recomendado)"; GroupDescription: "Inicialização"

[Run]
; 1) instala o driver e cria o dispositivo (sem UI)
Filename: "{app}\mvcli.exe"; Parameters: "install"; StatusMsg: "Instalando o driver do monitor virtual..."; Flags: runhidden waituntilterminated
; 2) liga o monitor virtual com as configurações padrão
Filename: "{app}\mvcli.exe"; Parameters: "on"; StatusMsg: "Ativando o monitor virtual..."; Flags: runhidden waituntilterminated
; 3) início automático elevado no logon
Filename: "{app}\mvcli.exe"; Parameters: "startup-on"; Flags: runhidden waituntilterminated; Tasks: autostart
; 4) abre o app ao final.
;    postinstall usa runasoriginaluser por padrão (CreateProcess no token
;    limitado). Na 0.1.0 o exe pedia requireAdministrator e o Windows
;    devolvia 740 (ERROR_ELEVATION_REQUIRED). O exe agora é asInvoker e
;    pede UAC sozinho; runascurrentuser ainda assim reaproveita o token
;    já elevado do instalador para não mostrar um segundo UAC.
Filename: "{app}\{#MyAppExe}"; Description: "Abrir o {#MyAppShortName}"; Flags: postinstall nowait skipifsilent runascurrentuser
; 4b) instalação silenciosa: sobe o tray em background (já estamos elevados)
Filename: "{app}\{#MyAppExe}"; Parameters: "--background"; Flags: nowait skipifnotsilent runascurrentuser

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/f /im MonitorVirtual.exe"; Flags: runhidden waituntilterminated; RunOnceId: "closeapp"
Filename: "{app}\mvcli.exe"; Parameters: "startup-off"; Flags: runhidden waituntilterminated; RunOnceId: "startupoff"
Filename: "{app}\mvcli.exe"; Parameters: "uninstall"; Flags: runhidden waituntilterminated; RunOnceId: "driveruninstall"

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  // garante que uma versão anterior não fique segurando os arquivos
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/f /im MonitorVirtual.exe',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    MsgBox(
      'Falta um passo no Holyrics:' + #13#10#13#10 +
      '1) Abra o Holyrics' + #13#10 +
      '2) Configurações -> Projeção (ou o assistente de telas)' + #13#10 +
      '3) Escolha o monitor "Virtual Display Driver" como Tela pública' + #13#10#13#10 +
      'Dica: use "Testar tela..." no ícone perto do relógio para confirmar qual é o monitor virtual.',
      mbInformation, MB_OK);
  end;
end;
