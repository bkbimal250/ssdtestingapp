#define MyAppName "PIXINIT"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "PIXINIT"
#define MyAppExeName "PIXINIT.exe"

[Setup]
AppId={{5EB9D365-36FB-4FA2-95AD-8EA64BACAB91}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=PIXINIT Storage Diagnostics installer
DefaultDirName={localappdata}\Programs\PIXINIT
DefaultGroupName=PIXINIT
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts\installer
OutputBaseFilename=PIXINIT-Setup-1.0.0-win-x64
SetupIconFile=..\pixinit\Assets\pixinit.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "..\artifacts\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,*.xml"

[Icons]
Name: "{group}\PIXINIT"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\PIXINIT"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch PIXINIT"; Flags: nowait postinstall skipifsilent
