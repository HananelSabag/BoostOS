; BoostOS installer (Inno Setup 6). Installs for the current user only: no administrator rights, no UAC prompt.
; Build:  set BOOSTOS_VERSION=0.5.2  &&  dotnet publish src/BoostOS -c Release -r win-x64 --self-contained -o publish
;         &&  iscc installer\BoostOS.iss        (the Release workflow does exactly this on every v* tag)

#define AppVersion GetEnv("BOOSTOS_VERSION")
#if AppVersion == ""
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId={{B0057050-6A3E-4C2D-9F1E-7E5D0B005706}
AppName=BoostOS
AppVersion={#AppVersion}
AppPublisher=Hananel Sabag
AppPublisherURL=https://github.com/HananelSabag/BoostOS
AppSupportURL=https://github.com/HananelSabag/BoostOS/issues
DefaultDirName={localappdata}\Programs\BoostOS
DisableProgramGroupPage=yes
DisableDirPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=BoostOS-Setup-{#AppVersion}
SetupIconFile=..\src\BoostOS\app.ico
UninstallDisplayIcon={app}\BoostOS.exe
UninstallDisplayName=BoostOS
LicenseFile=..\LICENSE
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "hebrew"; MessagesFile: "compiler:Languages\Hebrew.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{autoprograms}\BoostOS"; Filename: "{app}\BoostOS.exe"
Name: "{autodesktop}\BoostOS"; Filename: "{app}\BoostOS.exe"; Tasks: desktopicon

[Registry]
; The background recorder registers itself here on first run; take it away with the app.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "BoostOS Recorder"; Flags: uninsdeletevalue dontcreatekey

[Run]
Filename: "{app}\BoostOS.exe"; Description: "{cm:LaunchProgram,BoostOS}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/im BoostOS.exe /f"; Flags: runhidden; RunOnceId: "StopBoostOS"

[Code]
// The window and the background recorder keep BoostOS.exe open: stop them before files are replaced.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/im BoostOS.exe /f', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;
