#define Version "1.1.0"
[Setup]
SetupIconFile=..\assets\app.ico
AppId=TimeGuard.Parent
AppName=TimeGuard Parent
AppVersion={#Version}
DefaultDirName={localappdata}\Programs\TimeGuardParent
DefaultGroupName=TimeGuard
PrivilegesRequired=lowest
OutputDir=dist
OutputBaseFilename=TimeGuard-Parent-Setup
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\TimeGuard.Parent.exe
[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
[Files]
Source: "stage\parent\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{autoprograms}\TimeGuard Parent"; Filename: "{app}\TimeGuard.Parent.exe"
Name: "{autodesktop}\TimeGuard Parent"; Filename: "{app}\TimeGuard.Parent.exe"
[Run]
Filename: "{app}\TimeGuard.Parent.exe"; Description: "Запустить TimeGuard"; Flags: nowait postinstall skipifsilent
