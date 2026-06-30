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
DefaultDirName={localappdata}\Programs\RYNAT
DefaultGroupName=RYNAT
DisableProgramGroupPage=yes
OutputBaseFilename=RYNAT-Windows-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
UninstallDisplayIcon={app}\Rynat.WindowsTray.exe

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\RYNAT"; Filename: "{app}\Rynat.WindowsTray.exe"
Name: "{autoprograms}\打开共享网盘"; Filename: "{app}\Rynat.WindowsTray.exe"; Parameters: "--open-share"
Name: "{autodesktop}\RYNAT"; Filename: "{app}\Rynat.WindowsTray.exe"; Tasks: desktopicon
Name: "{autodesktop}\打开共享网盘"; Filename: "{app}\Rynat.WindowsTray.exe"; Parameters: "--open-share"; Tasks: shareicon

[Registry]
Root: HKCU; Subkey: "Software\Classes\*\shell\RynatCopyLink"; ValueType: string; ValueName: ""; ValueData: "复制 RYNAT 分享链接"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\*\shell\RynatCopyLink"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\Assets\RynatApp.ico"""; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\*\shell\RynatCopyLink\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Rynat.WindowsContextHelper.exe"" copy-link ""%1"" --kind file"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Directory\shell\RynatCopyLink"; ValueType: string; ValueName: ""; ValueData: "复制 RYNAT 分享链接"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Directory\shell\RynatCopyLink"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\Assets\RynatApp.ico"""; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Directory\shell\RynatCopyLink\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Rynat.WindowsContextHelper.exe"" copy-link ""%1"" --kind directory"; Flags: uninsdeletekey

[Tasks]
Name: "desktopicon"; Description: "创建 RYNAT 桌面快捷方式"; GroupDescription: "快捷方式："; Flags: unchecked
Name: "shareicon"; Description: "创建“打开共享网盘”桌面快捷方式"; GroupDescription: "快捷方式："; Flags: checkedonce

[Run]
Filename: "{app}\Rynat.WindowsTray.exe"; Description: "启动 RYNAT"; Flags: nowait postinstall skipifsilent
