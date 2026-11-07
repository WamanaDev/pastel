; Instalador do Pastel para Windows (Inno Setup 6).
; Compilar:  ISCC installer\Pastel.iss   (depois de: dotnet publish -c Release -r win-x64 --self-contained false -o publish\win-x64)
;
; O Pastel precisa do ".NET 10 Desktop Runtime". Este instalador confere se ele já existe e, se não existir, baixa o
; instalador oficial da Microsoft (só o runtime de área de trabalho, ~60 MB) e instala em silêncio. Quem já tem o
; .NET 10 não baixa nada.

#define AppName "Pastel"
#define AppVersion "0.1.0"
#define AppPublisher "Wictor Pamplona"
#define AppURL "https://github.com/WamanaDev/pastel"
#define AppExe "Pastel.exe"
; Sempre aponta para o runtime 10.0 mais recente da Microsoft
#define RuntimeUrl "https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe"

[Setup]
AppId={{B7E9D8C4-3A51-4F0B-9C27-5D1E6A8F2C90}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}/issues
AppUpdatesURL={#AppURL}/releases
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=Pastel-Setup
SetupIconFile=..\Pastel.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; precisa de administrador para instalar o .NET (quando falta) e para a pasta Programas
PrivilegesRequired=admin
CloseApplications=force
RestartApplications=no
ShowLanguageDialog=no
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription=Instalador do Pastel

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Files]
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
; o Pastel vive na bandeja; o atalho abre as configurações
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; Parameters: "--settings"

[Run]
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
var
  DownloadPage: TDownloadWizardPage;

// O .NET 10 Desktop Runtime existe? Confere a pasta onde o Pastel.exe o procura (é o que o "dotnet --list-runtimes"
// lê; o SDK instala sem gravar no registro, então só o registro não bastaria) e, por garantia, o registro.
function IsDesktopRuntime10Installed: Boolean;
var
  FindRec: TFindRec;
  Names: TArrayOfString;
  I: Integer;
begin
  Result := False;

  if FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\10.*'), FindRec) then
  begin
    FindClose(FindRec);
    Result := True;
    Exit;
  end;

  if RegGetValueNames(HKLM64, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App', Names) then
    for I := 0 to GetArrayLength(Names) - 1 do
      if Copy(Names[I], 1, 3) = '10.' then
      begin
        Result := True;
        Exit;
      end;
end;

function OnDownloadProgress(const Url, FileName: String; const Progress, ProgressMax: Int64): Boolean;
begin
  Result := True;
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), SetupMessage(msgPreparingDesc), @OnDownloadProgress);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';
  if IsDesktopRuntime10Installed then
  begin
    Log('.NET 10 Desktop Runtime ja instalado: nada a baixar.');
    Exit;
  end;

  Log('.NET 10 Desktop Runtime nao encontrado: baixando o instalador oficial da Microsoft.');
  DownloadPage.Clear;
  DownloadPage.Add('{#RuntimeUrl}', 'windowsdesktop-runtime-win-x64.exe', '');
  DownloadPage.Show;
  try
    try
      DownloadPage.Download;
    except
      Result := 'Não consegui baixar o .NET 10, que o Pastel precisa. Confira a sua internet e rode este instalador de novo. ' +
                'Se preferir, instale o ".NET Desktop Runtime 10" em dotnet.microsoft.com/download e depois rode este instalador.' + #13#10#13#10 +
                '(' + GetExceptionMessage + ')';
      Exit;
    end;
  finally
    DownloadPage.Hide;
  end;

  // instala em silêncio; 0 = ok, 3010 = ok mas pede reiniciar, 1638 = já há uma versão igual ou mais nova
  if not Exec(ExpandConstant('{tmp}\windowsdesktop-runtime-win-x64.exe'), '/install /quiet /norestart', '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
  begin
    Result := 'Não consegui abrir o instalador do .NET 10. Instale o ".NET Desktop Runtime 10" em dotnet.microsoft.com/download e rode este instalador de novo.';
    Exit;
  end;

  if (ResultCode <> 0) and (ResultCode <> 3010) and (ResultCode <> 1638) then
  begin
    Result := 'A instalação do .NET 10 não terminou (código ' + IntToStr(ResultCode) + '). Instale o ".NET Desktop Runtime 10" em dotnet.microsoft.com/download e rode este instalador de novo.';
    Exit;
  end;
  if ResultCode = 3010 then NeedsRestart := True;
end;

function InitializeUninstall: Boolean;
begin
  // desinstalação silenciosa (ex.: pelo winget) não pode ficar presa numa pergunta
  if UninstallSilent then
  begin
    Result := True;
    Exit;
  end;
  Result := MsgBox('As suas pastas do Pastel (os arquivos .wfgroup na área de trabalho) deixam de abrir.' + #13#10 +
                   'Os atalhos que você levou para elas continuam guardados em %AppData%\Pastel\Grupos; ' +
                   'se quiser que voltem para a área de trabalho, exclua cada pasta no Pastel antes de desinstalar.' + #13#10#13#10 +
                   'Quer desinstalar mesmo assim?', mbConfirmation, MB_YESNO) = IDYES;
end;
