; HanVGamepad Windows installer (Inno Setup 6)
;
; Compiled by windows-build.ps1:
;   ISCC.exe /DAppVersion=1.0.0 /DSourceDir=<stage> /DOutputDir=<packages> /DOutputBase=<name> HanVGamepad.iss
; The #ifndef defaults below only exist so the script can also be compiled standalone from this folder.
;
; Install layout equals the portable layout: app files sit next to the exe (no single-file
; self-extracting stub), plus a cli\ subfolder for the headless host.
;
; Encoding: UTF-8 with BOM. The publisher strings below are Chinese on purpose —
; without a BOM the compiler would read this file as ANSI and garble them.

#define MyAppName "HanVGamepad"
#define MyAppExeName "HanVGamepad.Desktop.exe"
#define MyAppCliName "hanvgamepad.exe"
; Publisher identity: display alias in front, account name in parentheses.
#define MyAppPublisher "涵涵大王 (lanzeweie)"
#define MyAppContact "lanzeweie@foxmail.com"
#define MyAppURL "https://github.com/lanzeweie/HanVGamepad"

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
; Four-part form for the setup exe's own file version (Inno rejects 3-part here).
#ifndef AppVersion4
  #define AppVersion4 "1.0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\..\artifacts\win-x64\HanVGamepad"
#endif
#ifndef OutputDir
  #define OutputDir "..\..\artifacts\packages"
#endif
#ifndef OutputBase
  #define OutputBase "HanVGamepad-" + AppVersion + "-win-x64-setup"
#endif

[Setup]
; AppId identifies upgrades of this same product. Never reuse it for another app.
AppId={{8C3F1D6A-2B47-4E19-9A5C-71D0E3B48F62}
AppName={#MyAppName}
AppVersion={#AppVersion}
AppVerName={#MyAppName} {#AppVersion}
VersionInfoVersion={#AppVersion4}
AppPublisher={#MyAppPublisher}
AppContact={#MyAppContact}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} Setup
VersionInfoCopyright=Copyright © 2026 涵涵大王
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; lowest: install per-user without UAC by default; the wizard still offers all-users mode.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir={#OutputDir}
OutputBaseFilename={#OutputBase}
SetupIconFile=..\..\src\desktop\Resources\HanVGamepadIcon.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; Let the Restart Manager close a running instance before files are replaced.
CloseApplications=yes
RestartApplications=no
ShowLanguageDialog=no
AllowNoIcons=yes

[Languages]
; English stays first (fallback). Chinese is picked automatically on a Simplified Chinese system.
; The .isl is the official 6.5.0+ translation, vendored so the build does not depend on
; which translations the local Inno Setup installation happens to include.
Name: "english"; MessagesFile: "compiler:Default.isl"
#if FileExists(AddBackslash(SourcePath) + "languages\ChineseSimplified.isl")
Name: "chinesesimplified"; MessagesFile: "languages\ChineseSimplified.isl"
#endif

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Whole publish tree: exe + loose dlls + wwwroot + cli\ subfolder + license files.
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
{ Both the tray app and the headless host may keep files locked.
  Restart Manager handles most cases; taskkill covers processes it does not track. }
procedure KillRunningInstances();
var
  ResultCode: Integer;
begin
  Exec('taskkill.exe', '/f /im {#MyAppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec('taskkill.exe', '/f /im {#MyAppCliName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(300);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
    KillRunningInstances();
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    KillRunningInstances();
end;
