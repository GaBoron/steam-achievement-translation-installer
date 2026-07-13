#ifndef SourceRoot
  #error SourceRoot is required
#endif
#ifndef OutputRoot
  #error OutputRoot is required
#endif
#ifndef MyAppVersion
  #error MyAppVersion is required
#endif
#ifndef MyAppIcon
  #error MyAppIcon is required
#endif

#define MyAppName "Steam 成就翻译安装器"
#define MyAppExeName "SATLInstaller.exe"
#define MyAppPublisher "GaBoron"
#define MyAppUrl "https://github.com/GaBoron/steam-achievement-translation-installer"

[Setup]
AppId={{8E4CF3D1-13E7-4FF7-A979-CE07F27F020A}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppUrl}
AppSupportURL={#MyAppUrl}/issues
AppUpdatesURL={#MyAppUrl}/releases
UninstallDisplayName={#MyAppName}
DefaultDirName={autopf}\Steam Achievement Translation Installer
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
PrivilegesRequired=admin
; HKCU is touched only to remove the legacy per-user uninstall entry during migration.
UsedUserAreasWarning=no
OutputDir={#OutputRoot}
OutputBaseFilename=SATLInstaller-Setup-v{#MyAppVersion}
SetupIconFile={#MyAppIcon}
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ChangesEnvironment=no
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "chinesesimp"; MessagesFile: "{#SourcePath}\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "其他选项："; Flags: unchecked

[Files]
Source: "{#SourceRoot}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Registry]
; Remove the legacy per-user uninstall entry when migrating to the elevated installer.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Uninstall\{{8E4CF3D1-13E7-4FF7-A979-CE07F27F020A}_is1"; Flags: deletekey dontcreatekey

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Description: "启动 {#MyAppName}"; Flags: nowait postinstall skipifsilent runascurrentuser
