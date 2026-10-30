#define MyAppName "PIXINIT"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "PIXINIT"
#define MyAppExeName "PIXINIT.exe"

[Setup]
AppId={{PIXINIT-1.0.0}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=PIXINIT Storage Diagnostics installer
DefaultDirName={autopf}\PIXINIT
DefaultGroupName=PIXINIT
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts\installer
OutputBaseFilename=PIXINIT-Setup-1.0.0-win-x64
SetupIconFile=..\pixinit\Assets\pixinit.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName=PIXINIT
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Files]
Source: "..\artifacts\publish\win-x64\PIXINIT.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\PIXINIT"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\PIXINIT"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch PIXINIT"; Flags: nowait postinstall skipifsilent
