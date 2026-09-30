#ifndef SourceDir
  #error SourceDir must be supplied by the build script
#endif
#ifndef AppVersion
  #error AppVersion must be supplied by the build script
#endif
#ifndef OutputDir
  #error OutputDir must be supplied by the build script
#endif

[Setup]
AppId={{B13B4E81-37D2-4895-8325-BC712D0A0CC2}
AppName=JTC Stamper
AppVersion={#AppVersion}
AppPublisher=JTC Stamper contributors
DefaultDirName={localappdata}\Programs\JTCStamper
DefaultGroupName=JTC Stamper
PrivilegesRequired=lowest
OutputDir={#OutputDir}
OutputBaseFilename=JTCStamper-{#AppVersion}-Standard-win-x64-Setup
Compression=lzma
SolidCompression=yes
ArchitecturesAllowed=x64compatible
UninstallDisplayIcon={app}\JTCStamper.App.exe
CloseApplications=yes
RestartApplications=no

[Files]
Source: "{#SourceDir}\JTCStamper.App.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\README.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\build.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\NOTICE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\JTC Stamper"; Filename: "{app}\JTCStamper.App.exe"
Name: "{autodesktop}\JTC Stamper"; Filename: "{app}\JTCStamper.App.exe"; Tasks: desktopicon

[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"; Flags: unchecked
