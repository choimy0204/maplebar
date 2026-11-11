; 메이플 바 설치 스크립트 (Inno Setup 6)
; 만들기: 먼저 dotnet publish로 ..\publish\MapleBar.exe를 만든 뒤 이 파일을 ISCC로 컴파일한다.

#define AppName "메이플 바"
; 깃허브 빌드는 /DAppVer=로 버전을 넘긴다
#ifndef AppVer
#define AppVer "1.0.6"
#endif
#define AppExe "MapleBar.exe"

[Setup]
AppId={{6F3C2A9E-4B7D-4E21-9A55-8C1D0E7B3F42}
AppName={#AppName}
AppVersion={#AppVer}
AppVerName={#AppName} {#AppVer}
DefaultDirName={autopf}\MapleBar
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; 관리자 권한 없이 내 계정에만 설치한다 (원하면 설치 중에 모든 사용자로 바꿀 수 있다)
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=MapleBar-Setup-{#AppVer}
SetupIconFile=..\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; 설치·제거할 때 실행 중인 메이플 바를 닫는다
CloseApplications=force
RestartApplications=no

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Tasks]
Name: "desktopicon"; Description: "바탕화면에 바로가기 만들기"; GroupDescription: "바로가기:"
; PC 켤 때 자동 실행은 메이플 바 메뉴에서 고른다 (관리자 권한 프로그램이라 시작 프로그램 폴더로는 실행이 막힌다)

[Files]
; 한 파일로 묶어 압축하면 켤 때마다 풀어야 해서 느리다. 폴더째 설치해 바로 뜨게 한다
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
; 1.0.0의 한 파일짜리 exe를 폴더 방식으로 덮어쓸 때 남는 찌꺼기가 없게 한다
Type: files; Name: "{app}\MapleBar.pdb"

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\{#AppName} 제거"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "지금 메이플 바 실행"; Flags: nowait postinstall skipifsilent shellexec

[UninstallRun]
; 메이플 바 메뉴에서 켜 둔 자동 실행 등록을 지운다
Filename: "schtasks.exe"; Parameters: "/Delete /TN MapleBar_AutoStart /F"; Flags: runhidden; RunOnceId: "DelAutoStart"

[UninstallDelete]
; 예전 버전이 만들던 시작 프로그램 바로가기
Type: files; Name: "{userstartup}\{#AppName}.lnk"

[Code]
// 사이트 창은 Microsoft Edge WebView2 런타임이 있어야 뜬다 (Windows 11에는 기본으로 들어 있다)
function HasWebView2(): Boolean;
var v: String;
begin
  Result :=
    RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', v) or
    RegQueryStringValue(HKLM, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', v) or
    RegQueryStringValue(HKCU, 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', v);
  if Result then Result := (v <> '') and (v <> '0.0.0.0');
end;

function InitializeSetup(): Boolean;
var code: Integer;
begin
  Result := True;
  if not HasWebView2() then
    if MsgBox('이 PC에 Microsoft Edge WebView2 런타임이 없어 사이트 창이 열리지 않을 수 있어요.' #13#10 +
              '지금 다운로드 페이지를 열까요? (설치 후 이 설치를 다시 실행해 주세요)', mbConfirmation, MB_YESNO) = IDYES then
    begin
      ShellExec('open', 'https://developer.microsoft.com/microsoft-edge/webview2/', '', '', SW_SHOWNORMAL, ewNoWait, code);
      Result := False;
    end;
end;
