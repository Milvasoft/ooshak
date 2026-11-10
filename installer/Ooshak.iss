; Ooshak Windows installer. Build with:
;   ISCC.exe /DAppVersion=0.1.0 /DSourceDir=..\artifacts\publish installer\Ooshak.iss

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\publish"
#endif

[Setup]
AppId={{8F3C2A1E-5B7D-4E9A-9C21-6D4B0E7A3F15}
AppName=Ooshak
AppVersion={#AppVersion}
AppVerName=Ooshak {#AppVersion}
AppPublisher=Milvasoft
AppPublisherURL=https://github.com/Milvasoft/ooshak
AppSupportURL=https://github.com/Milvasoft/ooshak/issues
AppUpdatesURL=https://github.com/Milvasoft/ooshak/releases
DefaultDirName={localappdata}\Programs\Ooshak
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
LicenseFile=..\LICENSE
SetupIconFile=..\assets\ooshak.ico
UninstallDisplayIcon={app}\Ooshak.exe
OutputDir=..\artifacts
OutputBaseFilename=Ooshak-Setup-{#AppVersion}-x64
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"

[CustomMessages]
english.StartWithWindows=Start Ooshak when Windows starts
turkish.StartWithWindows=Ooshak'ı Windows ile başlat

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startup"; Description: "{cm:StartWithWindows}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{autoprograms}\Ooshak"; Filename: "{app}\Ooshak.exe"
Name: "{autodesktop}\Ooshak"; Filename: "{app}\Ooshak.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Ooshak"; ValueData: """{app}\Ooshak.exe"" --hidden"; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\Ooshak.exe"; Description: "{cm:LaunchProgram,Ooshak}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Removes Ooshak's hooks from ~/.claude/settings.json and the login entry. Models in ~/.ooshak are kept.
Filename: "{app}\Ooshak.exe"; Parameters: "--uninstall"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveIntegration"
