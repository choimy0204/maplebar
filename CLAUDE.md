# MapleBar 작업 안내

새 세션에서 이어서 작업할 때 먼저 읽는 문서. 재획(메이플 사냥)을 편하게 하려고 만드는 화면 위 바 프로그램이다.

- 기술: .NET 10 WinForms + WebView2, 단일 프로젝트 `MapleBar.csproj`
- 저장소: https://github.com/choimy0204/maplebar (브랜치 main)
- 홈페이지: `docs/` (GitHub Pages), 공지: `docs/dev.json`
- 디자인(색·부품·화면별 모양): [DESIGN.md](DESIGN.md). 화면을 만들거나 고치기 전에 읽는다.

## 일하는 방식

- 대답·중간 안내·UI 문구는 모두 한글로 쓴다.
- UI 문구는 용어 없이 짧고 쉽게 쓴다. "(예: …)" 같은 덧붙임은 넣지 않는다.
- 칸과 버튼은 알약 모양이 아니라 둥근 직사각형으로 만든다.
- 글 칸은 `TextField`(둥근 칸 + 테두리 없는 TextBox)를 쓰고 `ImeMode.NoControl`로 둔다.
- 묻는 창은 `MessageBox`가 아니라 `AskBox.Show` / `AskBox.Choose`를 쓴다.
- 다크 모드에서 칸 바탕에 갈색이나 어두운 주황을 쓰지 않는다. 회색 바탕에 호박색 글자를 쓴다.
- 게임에 지장이 있을 수 있으니 화면 직접 제어나 데스크톱 스크린샷은 하지 않는다.
- 커밋·푸시·태그·버전 올리기는 요청받았을 때만 한다.
- 바가 켜져 있으면 강제로 끄지 않는다. 사용자에게 꺼 달라고 한다.

## 파일 규칙

- `.cs` 파일은 CRLF + UTF-8(BOM)으로 저장한다. `MapleBar.csproj`도 BOM을 유지한다 (BOM이 빠지면 다시 넣는다).
- 커밋하지 않는 것: `graphify-out/`, `기획/`, `presence/.wrangler/`.

## 빌드와 실행

빌드만 확인 (켜져 있는 바와 겹치지 않는 곳):

```powershell
dotnet build MapleBar.csproj -c Release -o "$env:TEMP\maplebar-check"
```

사용자가 바를 끈 뒤 다시 켜기:

```powershell
Wait-Process MapleBar -Timeout 15 -ErrorAction SilentlyContinue; if (Get-Process MapleBar -ErrorAction SilentlyContinue) { "아직 켜져 있음" } else { dotnet build -c Release 2>&1 | Select-String "오류 |error " | Select-Object -First 6 ; if ($LASTEXITCODE -eq 0) { Start-Process "bin\Release\net10.0-windows\MapleBar.exe"; "켜짐" } }
```

경고 CS0108 2개는 예전부터 있던 것이다.

## 버전과 배포

- 버전 규칙: 수정은 끝자리를 올린다(1.2.13 → 1.2.14). 새 기능은 셋째 자리를 올리고, 9를 넘으면 가운데 자리를 올린다.
- 배포할 때 함께 고칠 곳:
  1. `MapleBar.csproj`의 `<Version>` (BOM 유지)
  2. `README.md`: "최신 버전"과 "최근 업데이트"에 새 항목을 맨 위에 추가
  3. `docs/index.html`:
     - 내려받기 버튼의 `dl-ver`
     - 버전 바로 가기 줄: 5개를 유지한다. 새 버전을 "최신"으로 넣고 가장 오래된 것을 뺀다.
     - 새 `<article class="release" id="vXYZ">`에 "최신"을 붙이고, 이전 글에서는 "최신"을 뺀다.
- 커밋 메시지는 `%TEMP%`에 BOM 없이 써서 `git commit -F`로 넣는다. 끝에는 다음 줄을 붙인다:
  - `Agent: main (opus)`
  - `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`
- `git tag vX.Y.Z` 후 main과 태그를 푸시한다. 태그를 푸시해야 `.github/workflows/release.yml`이 돌아 사용자에게 업데이트가 간다.
- 진행 확인은 gh 없이 공개 API로 한다: `https://api.github.com/repos/choimy0204/maplebar/actions/runs?per_page=3` (User-Agent는 일반 이름).
- 공지 요청은 `docs/dev.json`만 고쳐서 그 파일만 커밋·푸시한다.

## 최근 작업 (2026.11.21 ~ 11.23)

### 1.2.12: 알림 설정 칸 정리
`JanusForm`(알림 설정 창)의 윗줄 배치를 고쳤다.

### 1.2.13: 검색 목록 안정화
닉네임 검색칸과 최근 검색 목록을 `SearchBox.cs` 하나로 묶었다.

- 증상: 바의 설정 버튼 등을 누르면 최근 검색 목록이 떴다 사라졌다.
- 원인: 바가 다시 활성화될 때 WinForms가 마지막 칸(검색칸)에 포커스를 돌려준다. 그 GotFocus에서 목록을 자동으로 열었고, 메뉴가 곧바로 닫았다.
- 지금 구조:
  - 여는 길은 사용자가 직접 한 동작뿐이다: 칸 누르기(MouseDown), 글자 치기(TextChanged, 코드가 넣은 글은 `settingText`로 제외), 위·아래 키.
  - 닫는 길은 `CloseList()` 하나다: 칸을 떠남 · 바가 비활성됨(`CloseIfLeft`) · Esc · 고르기 · 검색 · 다른 메뉴 열기(`BarForm.ShowBelowBar`).
  - 바는 타이머 글자 폭이 바뀌면 가운데를 맞추며 움직인다. 그래서 목록은 닫지 않고 `FollowBar`로 따라간다.
  - 엔터는 위·아래 키로 고른 줄(`RecentDrop.sel`)만 쓴다. 마우스로 가리킨 줄(`hot`)은 쓰지 않는다.
- `RecentDrop`은 창을 빼앗지 않는 목록이다(WS_EX_NOACTIVATE, MA_NOACTIVATE, ShowWithoutActivation). `TopMost` 속성을 켜면 창을 빼앗아 목록이 바로 닫히니 쓰지 않는다.
- `BarForm`은 `searchBox.Value`, `FocusBox()`, `Flash()`, `CloseList()`, `Typing`만 쓴다. IME 처리와 돋보기 그리기도 `SearchBox`로 옮겼다.
- 효율:
  - `Settings.Save`는 `JsonSerializerOptions`를 하나만 만들어 다시 쓴다.
  - `RecentDrop`은 별 칸 너비와 칠하기 붓을 한 번만 만든다. 줄 찾기는 세로 위치로 바로 계산한다.

### 1.2.14: 반복 알림 이름 오류
- 증상: 반복 알림 이름을 바꾸려고 누르면 "Object reference not set" 오류 창이 떴다.
- 원인: `JanusForm.MakeRepeatCard`의 `rs?.Name ?? os!.Name` 때문이다. 반복 알림 이름이 비어 있으면(기본 이름) 없는 단일 알림(`os`)을 읽었다.
- 고침: 같은 함수의 값 읽기를 모두 `once ? os!.X : rs!.X`로 골라 읽게 바꿨다. `a?.X ?? b!.X` 꼴은 X가 null일 수 있으면 쓰지 않는다.

## 남은 일

없음. 1.2.14까지 배포했다.
