# 메이플 바

maplescouter 메뉴를 화면 위쪽 막대로 띄워 두는 Windows 프로그램입니다.

- 메뉴를 누르면 사이트가 작은 창으로 열립니다.
- 캐릭터 이름을 검색하면 캐릭터 정보가 바로 열립니다.
- 메애기 브금을 바에서 재생하고 넘기고 볼륨을 조절할 수 있습니다.
- 인벤 새 글 알림을 보여 줍니다.

## 설치

[MapleBar-Setup-1.0.2.exe 다운로드](https://github.com/choimy0204/maplebar/raw/main/dist/MapleBar-Setup-1.0.2.exe)

- Windows 10/11 64비트용입니다.
- 처음 실행할 때 "Windows의 PC 보호" 창이 뜨면 **추가 정보 → 실행**을 누르세요.
- 새 버전은 지우지 않고 그대로 덮어 설치하면 됩니다.
- 단축키 Ctrl+Alt+M으로 숨긴 바를 다시 열 수 있습니다.

## 직접 빌드

.NET 7 SDK와 Inno Setup 6이 필요합니다.

```
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -p:DebugType=none -o publish
ISCC installer\MapleBar.iss
```
