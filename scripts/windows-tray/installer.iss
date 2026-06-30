#define AppName "RYNAT"
#define AppVersion "0.1.0"
#define PublisherName "RYNAT"
#define SourceDir GetEnv("RYNAT_PUBLISH_DIR")
#if SourceDir == ""
  #error "Set RYNAT_PUBLISH_DIR to a completed windows-tray release output directory before compiling this installer."
#endif

[Setup]
AppId={{B8E4BDFB-A2E3-49AC-B830-7E0E9B32F3A6}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#PublisherName}
AppVerName=RYNAT 共享网盘 {#AppVersion}
DefaultDirName={autopf}\RYNAT
DefaultGroupName=RYNAT
DisableProgramGroupPage=yes
OutputBaseFilename=RYNAT-Windows-Setup
SetupIconFile={#SourceDir}\Assets\RynatApp.ico
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
UninstallDisplayIcon={app}\Assets\RynatApp.ico
UninstallDisplayName=RYNAT 共享网盘

[Languages]
Name: "chinesesimp"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{commonprograms}\RYNAT"; Filename: "{app}\Rynat.WindowsTray.exe"
Name: "{commondesktop}\RYNAT"; Filename: "{app}\Rynat.WindowsTray.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Classes\*\shell\RynatCopyLink"; Flags: deletekey noerror
Root: HKCU; Subkey: "Software\Classes\Directory\shell\RynatCopyLink"; Flags: deletekey noerror
Root: HKLM; Subkey: "Software\Classes\*\shell\RynatCopyLink"; ValueType: string; ValueName: ""; ValueData: "复制 RYNAT 分享链接"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\Classes\*\shell\RynatCopyLink"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\Assets\RynatApp.ico"""; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\Classes\*\shell\RynatCopyLink\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Rynat.WindowsContextHelper.exe"" copy-link ""%1"" --kind file"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\Classes\Directory\shell\RynatCopyLink"; ValueType: string; ValueName: ""; ValueData: "复制 RYNAT 分享链接"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\Classes\Directory\shell\RynatCopyLink"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\Assets\RynatApp.ico"""; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\Classes\Directory\shell\RynatCopyLink\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Rynat.WindowsContextHelper.exe"" copy-link ""%1"" --kind directory"; Flags: uninsdeletekey

[Tasks]
Name: "desktopicon"; Description: "创建 RYNAT 桌面快捷方式"; GroupDescription: "快捷方式："; Flags: checkedonce

[Run]
Filename: "{app}\Rynat.WindowsTray.exe"; Description: "启动 RYNAT"; Flags: nowait postinstall skipifsilent
