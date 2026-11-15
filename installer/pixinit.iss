#ifndef AppVersion
  #error AppVersion must be supplied from pixinit.csproj by scripts/build-release.ps1.
#endif

#define AppName "PIXINIT"
#define AppDescription "SSD Read & Write Speed Testing Utility."
#define AppExeName "PIXINIT.exe"

[Setup]
; Keep the original AppId for upgrades from previous PIXINIT and SSD Tester builds.
AppId={{PIXINIT-1.0.0}}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
#ifdef AppPublisher
AppPublisher={#AppPublisher}
VersionInfoCompany={#AppPublisher}
#endif
AppComments={#AppDescription}
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}
VersionInfoDescription={#AppDescription}
DefaultDirName={autopf}\PIXINIT
DefaultGroupName=PIXINIT
UsePreviousAppDir=yes
UsePreviousTasks=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\release\installer
OutputBaseFilename=PIXINIT-Setup-1.0.0-win-x64
SetupIconFile=..\pixinit\Assets\pixinit.ico
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
; Match the mutex held by App.xaml.cs for the application's lifetime.
; Both Setup and Uninstall must wait for PIXINIT to close before changing files.
AppMutex=Local\PIXINIT.SingleInstance
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "..\release\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\PIXINIT"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\PIXINIT"; Filename: "{app}\{#AppExeName}"
