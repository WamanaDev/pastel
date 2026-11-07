; Instalador do Pastel para Windows (Inno Setup 6).
; Compilar:
;   dotnet publish Pastel.csproj -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false "-p:SatelliteResourceLanguages=en%3Bpt-BR" -o publish\win-x64-sc
;   ISCC installer\Pastel.iss
;
; O app é AUTÔNOMO: leva o .NET dentro. Por isso o instalador não confere nem baixa nada, funciona offline e, como não
; precisa instalar o .NET (que exigia administrador), instala só para o usuário atual, sem pedido de administrador.

#define AppName "Pastel"
#define AppVersion "0.1.1"
#define AppPublisher "Wictor Pamplona"
#define AppURL "https://github.com/WamanaDev/pastel"
#define AppExe "Pastel.exe"

[Setup]
AppId={{B7E9D8C4-3A51-4F0B-9C27-5D1E6A8F2C90}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}/issues
AppUpdatesURL={#AppURL}/releases
; sem administrador: instala em %LocalAppData%\Programs\Pastel (quem quiser pode escolher "para todos os usuários")
DefaultDirName={autopf}\{#AppName}
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=Pastel-Setup
SetupIconFile=..\Pastel.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=force
RestartApplications=no
ShowLanguageDialog=no
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription=Instalador do Pastel

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Files]
Source: "..\publish\win-x64-sc\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
; o Pastel vive na bandeja; o atalho abre as configurações
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; Parameters: "--settings"

[Run]
Filename: "{app}\{#AppExe}"; Parameters: "--register"; Flags: runhidden
; atualização automática (instalação silenciosa): o Pastel volta para a bandeja sozinho
Filename: "{app}\{#AppExe}"; Flags: nowait runasoriginaluser; Check: WizardSilent
Filename: "{app}\{#AppExe}"; Parameters: "--settings"; Description: "Abrir o Pastel agora"; Flags: nowait postinstall skipifsilent

[Registry]
; Ao desinstalar, remove o que o Pastel registrou para o seu usuário (tipo de arquivo das pastas, menus do botão direito e inicialização).
; "dontcreatekey" = não cria nada na instalação; só apaga na desinstalação.
Root: HKCU; Subkey: "Software\Classes\.wfgroup"; Flags: dontcreatekey uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Pastel.Group"; Flags: dontcreatekey uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Applications\Pastel.exe"; Flags: dontcreatekey uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\DesktopBackground\Shell\Pastel.Menu"; Flags: dontcreatekey uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\DesktopBackground\Shell\Pastel.NovoGrupo"; Flags: dontcreatekey uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\*\shell\Pastel.Enviar"; Flags: dontcreatekey uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Directory\shell\Pastel.Enviar"; Flags: dontcreatekey uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\InternetShortcut\shell\Pastel.Enviar"; Flags: dontcreatekey uninsdeletekey
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "Pastel"; Flags: dontcreatekey uninsdeletevalue

[Code]
function InitializeUninstall: Boolean;
begin
  // desinstalação silenciosa (ex.: pelo winget) não pode ficar presa numa pergunta
  if UninstallSilent then
  begin
    Result := True;
    Exit;
  end;
  Result := MsgBox('Ao desinstalar, os atalhos que estão nas suas pastas e flutuantes voltam para a área de trabalho,' + #13#10 +
                   'e as pastas do Pastel (arquivos .wfgroup) e as configurações são apagadas.' + #13#10#13#10 +
                   'Quer desinstalar?', mbConfirmation, MB_YESNO) = IDYES;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Code: Integer;
begin
  // antes de apagar os arquivos: devolve os atalhos, apaga as pastas e os dados
  if CurUninstallStep = usUninstall then
    Exec(ExpandConstant('{app}\{#AppExe}'), '--uninstall-cleanup', '', SW_HIDE, ewWaitUntilTerminated, Code);
end;
