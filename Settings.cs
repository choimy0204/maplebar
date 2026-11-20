using System.Text.Json;

namespace MapleBar;

/// <summary>최근 검색한 닉네임, 바 위치, 패널 크기를 저장한다.</summary>
public class Settings
{
    public List<string> Recent { get; set; } = new();
    public List<string> BarOrder { get; set; } = new(); // 바 메뉴 버튼 순서 (메뉴 이름 목록)
    public List<string> BarHiddenTops { get; set; } = new();  // 바에서 숨긴 메뉴 이름
    public List<string> BarHiddenItems { get; set; } = new(); // 펼침 메뉴에서 숨긴 항목 ("메뉴/항목")
    public int? BarX { get; set; }
    public int? BarY { get; set; }
    public int? BarCX { get; set; } // 바의 가운데 x (폭이 바뀌어도 자리가 밀리지 않게)
    // 게임 창에 붙어 있을 때의 자리: 게임 화면 가로 가운데·위쪽 끝에서 얼마나 떨어졌는지 (게임 화면 폭에 대한 비율)
    public double? GameDX { get; set; }
    public double? GameDY { get; set; }
    public bool GameFollow { get; set; } = true; // 게임 화면 밖에 놓으면 false
    // 사이트 창: 마지막 위치·크기, 항상 위 여부
    public int? WinX { get; set; }
    public int? WinY { get; set; }
    /// <summary>창 종류마다 마지막 위치·크기 (코디, 웹툰, 사이트 주소별 등). 값은 x, y, 폭, 높이</summary>
    public Dictionary<string, int[]> WinPlaces { get; set; } = new();
    /// <summary>바에서 떼어 둔 남은 시간 칩의 자리 (칩 이름 → 화면 x, y). 없으면 바에 붙어 있다</summary>
    public Dictionary<string, int[]> ChipSpots { get; set; } = new();
    /// <summary>바를 직접 숨기면(Ctrl+Alt+M) 떼어 낸 칩도 같이 숨긴다</summary>
    public bool ChipsHideWithBar { get; set; } = true;
    public int WinW { get; set; }
    public int WinH { get; set; }
    public bool WinTopMost { get; set; } = true;
    public int WebZoom { get; set; } = 100; // 사이트 창 화면 배율(%)
    public int WinOpacity { get; set; } = 100; // 사이트 창 투명도(%), 마지막으로 고른 값
    // 뮤직: 곡이 바뀔 때 레코드 효과음
    public bool MusicFx { get; set; } = true;
    // 바: 마우스가 3초 동안 없으면 위로 접는다 (끄면 항상 펼쳐 둔다)
    public bool BarAutoHide { get; set; } = true;
    // 바를 숨겨도(Ctrl+Alt+M) 야누스·룬·버프·도핑 알림을 띄운다
    public bool AlertWhenHidden { get; set; } = true;
    // 시계: 켜기, 초·날짜·24시간 보기, 게임 창 안에서의 자리(비율, 없으면 오른쪽 위)
    public bool ClockOn { get; set; } = false;
    // 다크 모드 (프로그램 설정 → 화면)
    public bool DarkMode { get; set; }
    // 유튜브 목록 창에서 영상을 누르면 재생 창을 따로 띄우지 않고 그 창에서 본다
    public bool YtSameWindow { get; set; }
    // 실시간 방송 창에서 방송을 누르면 재생 창을 따로 띄우지 않고 그 창에서 본다
    public bool LiveSameWindow { get; set; }
    public bool ClockSeconds { get; set; } = true;
    public bool ClockDate { get; set; } = true;
    public bool Clock24 { get; set; } = false;
    public double? ClockFX { get; set; }
    public double? ClockFY { get; set; }
    // 알림 소리: 켜기와 크기(%)
    public bool SoundOn { get; set; } = true;
    public int SoundVol { get; set; } = 60;
    /// <summary>알림마다 고른 소리 번호 (AlertSound.Kind 순서). 없으면 기본 소리</summary>
    public List<int>? SoundPicks { get; set; }
    /// <summary>알림음 뒤에 알림 제목을 목소리로 읽어 준다</summary>
    public bool SpeakOn { get; set; }
    /// <summary>알림음(띠링 등)을 울린다. 끄면 읽어 주기만 들린다</summary>
    public bool ChimeOn { get; set; } = true;
    /// <summary>화면 가장자리 빛·글자 알림. 끄면 소리만 난다</summary>
    public bool AlertVisual { get; set; } = true;
    /// <summary>알림 방식: 화면 전체가 물드는 효과 / 가장자리를 빛이 한 바퀴 도는 효과</summary>
    public bool AlertFlash { get; set; } = true;
    public bool AlertEdge { get; set; } = true;
    /// <summary>알림 방식: 알림이 울릴 때 알림 이름(글자)을 띄울지</summary>
    public bool AlertText { get; set; } = true;
    // 조합 키: 이 키를 누른 채로 버프·도핑 키를 눌러야 세는 칸이 있다 (0이면 없음)
    public int ComboKey { get; set; }
    // 뮤직 칸을 바에서 꺼내 따로 둔 자리 (MusicFloat가 false면 바 안에 있다)
    public bool MusicFloat { get; set; }
    public int? MusicX { get; set; }
    public int? MusicY { get; set; }
    // 유튜브 뮤직 가사 보기 (가사가 없는 곡은 켜 둬도 안 보인다)
    public bool MusicLyrics { get; set; }
    // 브금 즐겨찾기 (최근에 넣은 곡이 위)
    public List<FavSong> MusicFavs { get; set; } = new();
    // 인벤 자유게시판 알림으로 지켜볼 목록 (Inven.Feeds의 Key)
    public List<string> InvenWatch { get; set; } = new();
    // 솔 야누스 알림: 설치 키를 누르면 지속시간을 세고, 꺼지면 게임 화면 가장자리에 보랏빛을 한 바퀴 돌린다
    public bool JanusOn { get; set; }
    public int JanusKey { get; set; }   // 가상 키 코드 (0이면 없음)
    public int JanusMod { get; set; }   // 같이 눌러야 하는 Shift(16)·Ctrl(17)·Alt(18). 0이면 없음
    public int JanusSec { get; set; } = 80;     // 예전 '지속시간'. 알림 주기로 옮길 때만 쓴다
    public int? JanusCycleSec { get; set; }     // 알림 주기(초): 키를 누르면 이만큼 뒤에 알린다. 0이면 세지 않음
    public bool JanusNoticeSeen { get; set; }   // '솔 야누스 알림이 바뀌었어요' 안내를 봤으면 true (한 번만 띄운다)
    /// <summary>예전 야누스 설정(주기 없음)으로 쓰던 사람이면 true. 불러올 때 주기로 옮기기 전에 정한다 (저장하지 않음)</summary>
    [System.Text.Json.Serialization.JsonIgnore] public bool JanusChanged { get; set; }
    public bool JanusRealertOn { get; set; }    // 알림 뒤에도 안 깔았으면 JanusRealertSec초 뒤 한 번 더 알림
    public int JanusRealertSec { get; set; } = 10;
    public bool LunaExt { get; set; } = true;   // 안 쓴다 (5석펫 스위치로 합침). 예전 설정 파일과 맞추려고 남겨 둔다
    public bool JanusEarly { get; set; }        // 야누스가 꺼지기 5초 전에 미리 알림 (다시 깔 여유)
    public bool PetOn { get; set; }             // 5석펫: 게더링(쿨 90초)마다 야누스 시간이 더해진다
    public DateTime? PetGatherAt { get; set; }   // 지금 세는 야누스의 게더링 박자 기준 (이 시각 + 90초×k)
    public DateTime? PetLastGather { get; set; } // 마지막 게더링 (다음 설치 때 쿨이 남았는지 본다)
    // 룬 알림: NPC 대화 키 뒤에 방향키 4번을 누르면 룬 주기(분)를 세고, 다 되면 알린다
    public bool RuneOn { get; set; }
    public int NpcKey { get; set; }
    public int RuneMin { get; set; } = 15;
    // 에르다 파운틴: 키를 누르면 60초를 세고, 사라지면(5초 먼저 알리기를 켜면 55초에) 밝은 하늘색으로 알린다
    public bool ErdaOn { get; set; }
    public int ErdaKey { get; set; }
    public bool ErdaEarly { get; set; } = true;
    public DateTime? ErdaStart { get; set; }
    // 껐다 켜도 이어서 세도록 시작 시각을 남긴다 (끝 = 시작 + 지속시간)
    public DateTime? JanusStart { get; set; }
    public DateTime? RuneStart { get; set; }
    // 예전 버프 하나짜리 설정. 처음 켤 때 아래 버프 칸으로 옮기고 비운다
    public bool BuffOn { get; set; }
    public int BuffKey { get; set; }
    public int BuffMin { get; set; } = 20;
    public DateTime? BuffStart { get; set; }
    // 버프 3칸: 칸마다 켜기·키·시간(10/20/30분)·시작 시각. 시작 시각을 남겨 껐다 켜도 이어서 센다
    public List<BuffSlot> Buffs { get; set; } = new();
    // 부스트 도핑: 키를 누르면 100초를 세고 빨간색으로 알린다
    public bool DopeOn { get; set; }
    public int DopeKey { get; set; }
    public int DopeSec { get; set; } = 100;
    public DateTime? DopeStart { get; set; }       // 실제로 효과가 시작되는 시각 (사용 대기를 더한 값)
    public bool DopeDelayOn { get; set; } = true;  // 쓰고 몇 초 뒤에 효과가 붙는 경우
    public int DopeDelaySec { get; set; } = 3;
    public List<DopeSlot> Dopes { get; set; } = new(); // 위의 도핑 하나짜리 칸은 예전 설정을 옮기는 데만 쓴다
    // 정령의 펜던트 확인: 고른 버프가 도는 동안 maple.gg에서 장비를 보고, 안 꼈으면 20초마다 알린다
    public string? MapleNick { get; set; }
    // 최근 검색에서 별표한 캐릭터: 검색칸이 비어 있을 때 이 캐릭터로 연다
    public string? StarNick { get; set; }
    public bool PendantOn { get; set; }
    public int PendantBuff { get; set; }           // 버프 칸 번호 (0~2)
    // 반복 주기 알림 15칸: 키를 누르면 정한 주기마다 정한 색으로 알린다
    public List<RepeatSlot> Repeats { get; set; } = new();
    // 단일 알림 15칸: 키를 누르면 정한 시간 뒤에 한 번만 알린다
    public List<OnceSlot> Onces { get; set; } = new();
    // 전체 알림 켜기·끄기: 끄면 모든 알림이 키를 세지도 울리지도 않는다 (칸마다 켜 둔 값은 그대로)
    public bool AlertsOn { get; set; } = true;
    // 00시 알림: 날짜가 바뀌기 MidnightMin분 전에 한 번 알린다
    public bool MidnightOn { get; set; }
    public int MidnightMin { get; set; } = 10;
    // 알림을 띄울 모니터 (Screen.DeviceName). "ALL"이면 모든 모니터, 비어 있어도 모든 모니터
    public List<string> AlertScreens { get; set; } = new() { "ALL" };
    // 알림 설정 프리셋: 알림 창 1·2페이지 값을 이름 붙여 저장해 두고 불러온다
    public List<AlertPreset> AlertPresets { get; set; } = new();
    public string? AlertPresetName { get; set; } // 마지막으로 저장하거나 불러온 프리셋
    // 자기계발 카드: 켜 두면 카드를 띄워 두고 StudySec초마다 다음 카드로 넘긴다. StudyStarOnly면 즐겨찾기한 카드만. 카드 자리는 게임 화면에 대한 비율
    public bool StudyOn { get; set; }
    public bool StudyStarOnly { get; set; }
    public int StudyMin { get; set; } = 10;
    public int StudySec { get; set; } = 12;
    // 설정 창들의 마지막 자리 (창 이름 → [x, y])
    public Dictionary<string, int[]> WinPos { get; set; } = new();
    public double? StudyFX { get; set; }
    public double? StudyFY { get; set; }
    public List<StudyDeck> Decks { get; set; } = new();
    // 네이버 웹툰: 누르고 있는 동안 부드럽게 위/아래로 스크롤하는 키와 속도(초당 픽셀)
    public int WebtoonUpKey { get; set; }
    public int WebtoonDownKey { get; set; }
    public int WebtoonSpeed { get; set; } = 900;
    public int WebtoonNextKey { get; set; } // 다음 화
    public int WebtoonPrevKey { get; set; } // 이전 화
    // 유튜브 재생 창의 쇼츠: 한 번 누르면 다음·이전 쇼츠로
    public int ShortsNextKey { get; set; }
    public int ShortsPrevKey { get; set; }
    // 계산기: 프리셋(계산하던 내용 + 기록)들, 지금 쓰는 프리셋, 기록 칸을 펼쳐 두었는지
    public List<CalcPreset> CalcPresets { get; set; } = new();
    public int CalcPresetIndex { get; set; }
    /// <summary>[저장]한 계산 (이름·계산 기록·주석을 통째로 담은 사본)</summary>
    public List<CalcPreset> CalcSaved { get; set; } = new();
    public bool CalcHistoryOpen { get; set; } = true;
    /// <summary>계산기 오른쪽 칸에서 보던 탭 (0 기록, 1 저장한 것)</summary>
    public int CalcTab { get; set; }
    /// <summary>계산기 스크립트 (이름 + 식 + 변수마다 마지막에 넣은 값)</summary>
    public List<CalcScript> CalcScripts { get; set; } = new();
    /// <summary>스크립트 화면에서 고른 스크립트 번호 (-1이면 고르지 않음)</summary>
    public int CalcScriptOpen { get; set; } = -1;
    /// <summary>계산기를 스크립트 화면으로 보던 중인지 (다음에 열 때 그대로)</summary>
    public bool CalcScriptView { get; set; }
    // 메모: 페이지마다 쓴 글, 보던 페이지, 창 크기(가로·세로)
    public List<string> MemoPages { get; set; } = new();
    public int MemoPage { get; set; }
    public int[]? MemoSize { get; set; }

    static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MapleBar");
    static string FilePath => Path.Combine(Dir, "settings.json");
    /// <summary>실패 기록 파일 (Log가 쓴다)</summary>
    public static string LogPath => Path.Combine(Dir, "log.txt");

    // 사이트 로그인·사람 확인 쿠키가 남도록 WebView2 데이터를 여기에 둔다
    public static string WebDataDir => Path.Combine(Dir, "WebView2");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new();
                // 야누스 알림을 켜 두고 주기가 아직 없으면 예전 버전에서 넘어온 사람이다
                s.JanusChanged = s.JanusOn && s.JanusCycleSec == null && !s.JanusNoticeSeen;
                return FixBuffs(s);
            }
        }
        catch (Exception ex)
        {
            Log.Write("settings", "설정을 읽지 못해 기본값으로 시작함", ex);
            // 다음 저장이 깨진 파일을 덮어쓰기 전에 settings.json.bad로 남겨 둔다 (원래 설정을 되살릴 수 있게)
            try { if (File.Exists(FilePath)) File.Copy(FilePath, FilePath + ".bad", true); } catch { }
        }
        return FixBuffs(new());
    }

    /// <summary>버프 칸을 3개로, 반복·단일 알림 칸을 15개로 맞춘다 (버프 기본 30·20·10분). 예전 버프 하나짜리 설정은 같은 시간 칸으로 옮긴다.</summary>
    static Settings FixBuffs(Settings s)
    {
        bool fresh = s.Buffs.Count == 0;
        int[] mins = { 30, 20, 10 };
        while (s.Buffs.Count < 3) s.Buffs.Add(new BuffSlot { Min = mins[s.Buffs.Count] });
        if (s.Buffs.Count > 3) s.Buffs.RemoveRange(3, s.Buffs.Count - 3);
        // 반복·단일 알림 칸 수를 맞춘다. 새 칸 색은 팔레트를 돌아가며 쓴다 (앞 3칸은 예전 색 그대로)
        int[] colors = { 2, 4, 6, 0, 3, 5, 7, 1 }, onceColors = { 1, 3, 7, 5, 0, 2, 4, 6 };
        s.Repeats ??= new();
        while (s.Repeats.Count < JanusWatch.RepeatSlots)
            s.Repeats.Add(new RepeatSlot { Argb = RepeatSlot.Palette[colors[s.Repeats.Count % colors.Length]] });
        if (s.Repeats.Count > JanusWatch.RepeatSlots) s.Repeats.RemoveRange(JanusWatch.RepeatSlots, s.Repeats.Count - JanusWatch.RepeatSlots);
        s.Onces ??= new();
        while (s.Onces.Count < JanusWatch.OnceSlots)
            s.Onces.Add(new OnceSlot { Argb = RepeatSlot.Palette[onceColors[s.Onces.Count % onceColors.Length]] });
        if (s.Onces.Count > JanusWatch.OnceSlots) s.Onces.RemoveRange(JanusWatch.OnceSlots, s.Onces.Count - JanusWatch.OnceSlots);
        s.AlertScreens ??= new() { "ALL" };
        s.AlertPresets ??= new();
        s.AlertPresets.RemoveAll(p => p == null || string.IsNullOrWhiteSpace(p.Name));
        // "AI" 메뉴가 "도구"로 바뀌었다: 바 순서·숨김 설정을 새 이름으로 옮긴다
        bool renamed = false;
        for (int i = 0; i < s.BarOrder.Count; i++) if (s.BarOrder[i] == "AI") { s.BarOrder[i] = SiteMenu.ToolsName; renamed = true; }
        for (int i = 0; i < s.BarHiddenTops.Count; i++) if (s.BarHiddenTops[i] == "AI") { s.BarHiddenTops[i] = SiteMenu.ToolsName; renamed = true; }
        for (int i = 0; i < s.BarHiddenItems.Count; i++)
            if (s.BarHiddenItems[i].StartsWith("AI/", StringComparison.Ordinal)) { s.BarHiddenItems[i] = SiteMenu.ToolsName + s.BarHiddenItems[i][2..]; renamed = true; }
        if (renamed) s.Save();
        s.Decks ??= new();
        s.CalcPresets ??= new();
        s.CalcPresets.RemoveAll(p => p == null);
        foreach (var p in s.CalcPresets) { p.Parts ??= new(); p.History ??= new(); }
        if (s.CalcPresets.Count == 0) s.CalcPresets.Add(new CalcPreset());
        s.CalcSaved ??= new();
        s.CalcSaved.RemoveAll(p => p == null);
        foreach (var p in s.CalcSaved) { p.Parts ??= new(); p.History ??= new(); }
        s.CalcPresetIndex = Math.Clamp(s.CalcPresetIndex, 0, s.CalcPresets.Count - 1);
        s.CalcScripts ??= new();
        s.CalcScripts.RemoveAll(p => p == null);
        foreach (var p in s.CalcScripts) { p.Title ??= ""; p.Expr ??= ""; p.Values ??= new(); }
        s.CalcTab = Math.Clamp(s.CalcTab, 0, 1);
        if (s.CalcScriptOpen >= s.CalcScripts.Count) s.CalcScriptOpen = -1;
        s.WinPlaces ??= new();
        foreach (var d in s.Decks) d.Stars ??= new();
        if (fresh && (s.BuffOn || s.BuffKey != 0 || s.BuffStart != null))
        {
            var b = s.Buffs.FirstOrDefault(x => x.Min == s.BuffMin) ?? s.Buffs[0];
            b.On = s.BuffOn; b.Key = s.BuffKey; b.Min = s.BuffMin; b.Start = s.BuffStart;
            s.BuffOn = false; s.BuffKey = 0; s.BuffStart = null;
            s.Save();
        }
        // 부스트 도핑 3칸. 예전 도핑 하나짜리 설정은 첫 칸(빨강)으로 옮긴다
        s.Dopes ??= new();
        bool freshDope = s.Dopes.Count == 0;
        while (s.Dopes.Count < 3) s.Dopes.Add(new DopeSlot());
        if (s.Dopes.Count > 3) s.Dopes.RemoveRange(3, s.Dopes.Count - 3);
        if (freshDope && (s.DopeOn || s.DopeKey != 0 || s.DopeStart != null))
        {
            var d = s.Dopes[0];
            d.On = s.DopeOn; d.Key = s.DopeKey; d.Sec = s.DopeSec; d.Start = s.DopeStart;
            d.DelayOn = s.DopeDelayOn; d.DelaySec = s.DopeDelaySec;
            s.DopeOn = false; s.DopeKey = 0; s.DopeStart = null;
            s.Save();
        }
        return s;
    }

    public void Save()
    {
        // 같은 폴더의 임시 파일에 쓴 뒤 바꿔치기해서, 쓰다 끊겨도 기존 설정이 깨지지 않게 한다
        string tmp = FilePath + ".tmp";
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(tmp,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, FilePath, true);
        }
        catch (Exception ex)
        {
            Log.Write("settings", "설정을 저장하지 못함", ex);
            try { File.Delete(tmp); } catch { }
        }
    }

    public void AddRecent(string name)
    {
        Recent.RemoveAll(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
        Recent.Insert(0, name);
        if (Recent.Count > 10) Recent.RemoveRange(10, Recent.Count - 10);
    }
}

/// <summary>즐겨찾기한 브금 한 곡</summary>
public class FavSong
{
    public string Title { get; set; } = "";
    public string Sub { get; set; } = "";
    public string Img { get; set; } = "";
}

/// <summary>버프 알림 한 칸</summary>
public class BuffSlot
{
    public bool On { get; set; }
    public int Key { get; set; }
    public int Min { get; set; } = 30;
    public DateTime? Start { get; set; }
    public bool WaitEnter { get; set; } = true; // 키 다음 엔터도 누르면 엔터 때부터 센다 (끄면 키 누른 때 그대로)
    // 마지막으로 쓴 때의 두 시점. 둘 다 남겨 두고 나중에 어느 쪽으로 셀지 바꿀 수 있다
    public DateTime? KeyAt { get; set; }
    public DateTime? EnterAt { get; set; }
    public string? Name { get; set; } // 비어 있으면 "버프 1" 같은 기본 이름
    public bool Combo { get; set; }   // 켜면 조합 키를 누른 채로 눌러야 센다
}

/// <summary>부스트 도핑 알림 한 칸</summary>
public class DopeSlot
{
    public bool On { get; set; }
    public int Key { get; set; }
    public int Sec { get; set; } = 100;
    public DateTime? Start { get; set; }       // 실제로 효과가 시작되는 시각 (사용 대기를 더한 값)
    public bool DelayOn { get; set; } = true;  // 쓰고 몇 초 뒤에 효과가 붙는 경우
    public int DelaySec { get; set; } = 3;
    public bool Combo { get; set; }            // 켜면 조합 키를 누른 채로 눌러야 센다
    public string? Name { get; set; }          // 비어 있으면 "도핑 1" 같은 기본 이름
}

/// <summary>알림 설정 프리셋: 알림 창 1·2페이지 값 묶음. 세는 중인 시각(Start 등)은 담지 않는다</summary>
public class AlertPreset
{
    public string Name { get; set; } = "";
    public bool SoundOn { get; set; } = true;
    public int SoundVol { get; set; } = 60;
    public List<int>? SoundPicks { get; set; }
    public bool? SpeakOn { get; set; }   // 예전 프리셋에는 없다 (없으면 지금 값을 그대로 둔다)
    public bool? ChimeOn { get; set; }   // 예전 프리셋에는 없다
    public bool AlertVisual { get; set; } = true;
    public bool AlertFlash { get; set; } = true;
    public bool AlertEdge { get; set; } = true;
    public bool AlertText { get; set; } = true;
    public int ComboKey { get; set; }
    public bool JanusOn { get; set; }
    public int JanusKey { get; set; }
    public int JanusMod { get; set; }
    public int? JanusCycleSec { get; set; }
    public bool JanusRealertOn { get; set; }
    public int JanusRealertSec { get; set; } = 10;
    public bool RuneOn { get; set; }
    public int NpcKey { get; set; }
    public int RuneMin { get; set; } = 15;
    public bool ErdaOn { get; set; }
    public int ErdaKey { get; set; }
    public bool ErdaEarly { get; set; } = true;
    public List<BuffSlot> Buffs { get; set; } = new();
    public List<DopeSlot> Dopes { get; set; } = new();
    public string? MapleNick { get; set; }
    public bool PendantOn { get; set; }
    public int PendantBuff { get; set; }
    public List<RepeatSlot> Repeats { get; set; } = new();
    public List<OnceSlot>? Onces { get; set; }
    public List<string> AlertScreens { get; set; } = new() { "ALL" };
    public bool? MidnightOn { get; set; }   // 없던 옛 프리셋이면 지금 값을 그대로 둔다
    public int? MidnightMin { get; set; }

    // 칸 목록은 JSON으로 한 번 돌려 따로 떼어 낸 복사본을 쓴다 (원본을 고쳐도 프리셋이 바뀌지 않게)
    static List<T> Copy<T>(List<T>? src) =>
        System.Text.Json.JsonSerializer.Deserialize<List<T>>(System.Text.Json.JsonSerializer.Serialize(src ?? new())) ?? new();

    /// <summary>지금 설정을 이름 붙여 프리셋으로 만든다</summary>
    public static AlertPreset From(Settings s, string name)
    {
        var p = new AlertPreset
        {
            Name = name,
            SoundOn = s.SoundOn, SoundVol = s.SoundVol, SoundPicks = s.SoundPicks?.ToList(), AlertVisual = s.AlertVisual, AlertFlash = s.AlertFlash, AlertEdge = s.AlertEdge, AlertText = s.AlertText,
            SpeakOn = s.SpeakOn, ChimeOn = s.ChimeOn, ComboKey = s.ComboKey,
            JanusOn = s.JanusOn, JanusKey = s.JanusKey, JanusMod = s.JanusMod, JanusCycleSec = JanusWatch.Cycle(s),
            JanusRealertOn = s.JanusRealertOn, JanusRealertSec = s.JanusRealertSec,
            RuneOn = s.RuneOn, NpcKey = s.NpcKey, RuneMin = s.RuneMin,
            ErdaOn = s.ErdaOn, ErdaKey = s.ErdaKey, ErdaEarly = s.ErdaEarly,
            Buffs = Copy(s.Buffs), Dopes = Copy(s.Dopes), Repeats = Copy(s.Repeats), Onces = Copy(s.Onces),
            MapleNick = s.MapleNick, PendantOn = s.PendantOn, PendantBuff = s.PendantBuff,
            AlertScreens = new(s.AlertScreens ?? new()),
            MidnightOn = s.MidnightOn, MidnightMin = s.MidnightMin,
        };
        foreach (var b in p.Buffs) { b.Start = null; b.KeyAt = null; b.EnterAt = null; }
        foreach (var d in p.Dopes) d.Start = null;
        foreach (var r in p.Repeats) r.Start = null;
        foreach (var o in p.Onces!) o.Start = null;
        return p;
    }

    /// <summary>프리셋 값을 설정에 넣는다. 지금 세는 중인 시각은 칸 번호대로 그대로 둔다</summary>
    public void ApplyTo(Settings s)
    {
        s.SoundOn = SoundOn; s.SoundVol = SoundVol; s.SoundPicks = SoundPicks?.ToList(); s.AlertVisual = AlertVisual; s.AlertFlash = AlertFlash; s.AlertEdge = AlertEdge; s.AlertText = AlertText;
        if (SpeakOn is bool sp) s.SpeakOn = sp;
        if (ChimeOn is bool ch) s.ChimeOn = ch;
        s.ComboKey = ComboKey;
        s.JanusOn = JanusOn; s.JanusKey = JanusKey; s.JanusMod = JanusMod; s.JanusCycleSec = JanusCycleSec ?? 0;
        s.JanusRealertOn = JanusRealertOn; s.JanusRealertSec = JanusRealertSec;
        s.RuneOn = RuneOn; s.NpcKey = NpcKey; s.RuneMin = RuneMin;
        s.ErdaOn = ErdaOn; s.ErdaKey = ErdaKey; s.ErdaEarly = ErdaEarly;
        s.MapleNick = MapleNick; s.PendantOn = PendantOn; s.PendantBuff = PendantBuff;
        if (MidnightOn is bool mo) s.MidnightOn = mo;
        if (MidnightMin is int mm) s.MidnightMin = Math.Clamp(mm, 0, 180);
        s.AlertScreens = new(AlertScreens ?? new() { "ALL" });
        if (s.AlertScreens.Count == 0) s.AlertScreens.Add("ALL");
        var buffs = Copy(Buffs);
        for (int i = 0; i < buffs.Count && i < s.Buffs.Count; i++)
        { buffs[i].Start = s.Buffs[i].Start; buffs[i].KeyAt = s.Buffs[i].KeyAt; buffs[i].EnterAt = s.Buffs[i].EnterAt; }
        var dopes = Copy(Dopes);
        for (int i = 0; i < dopes.Count && i < s.Dopes.Count; i++) dopes[i].Start = s.Dopes[i].Start;
        var reps = Copy(Repeats);
        for (int i = 0; i < reps.Count && i < s.Repeats.Count; i++) reps[i].Start = s.Repeats[i].Start;
        // 칸 수는 지금 설정에 맞춘다: 모자라면 지금 칸을 그대로 두고, 넘치면 버린다
        for (int i = buffs.Count; i < s.Buffs.Count; i++) buffs.Add(s.Buffs[i]);
        for (int i = dopes.Count; i < s.Dopes.Count; i++) dopes.Add(s.Dopes[i]);
        for (int i = reps.Count; i < s.Repeats.Count; i++) reps.Add(s.Repeats[i]);
        s.Buffs = buffs.Take(s.Buffs.Count).ToList();
        s.Dopes = dopes.Take(s.Dopes.Count).ToList();
        s.Repeats = reps.Take(s.Repeats.Count).ToList();
        // 단일 알림이 없던 옛 프리셋이면 지금 칸을 그대로 둔다
        if (Onces != null)
        {
            var onces = Copy(Onces);
            for (int i = 0; i < onces.Count && i < s.Onces.Count; i++) onces[i].Start = s.Onces[i].Start;
            for (int i = onces.Count; i < s.Onces.Count; i++) onces.Add(s.Onces[i]);
            s.Onces = onces.Take(s.Onces.Count).ToList();
        }
    }
}

/// <summary>단일 알림 한 칸: 키를 누르면 정한 시간 뒤에 한 번만 알린다</summary>
public class OnceSlot
{
    public bool On { get; set; }
    public int Key { get; set; }
    public bool Combo { get; set; }       // 켜면 조합 키를 누른 채로 눌러야 센다
    public bool WaitEnter { get; set; }   // 켜면 키 다음 엔터를 누른 때부터 센다
    public int Sec { get; set; } = 60;    // 키를 누르고 알릴 때까지 (초)
    public int Argb { get; set; }
    public DateTime? Start { get; set; }  // 키를 누른 시각 (이때부터 센다)
    public string? Name { get; set; }     // 알림에 뜨는 글. 비어 있으면 "단일 알림 1" 같은 기본 이름
    public string Title(int i) => string.IsNullOrWhiteSpace(Name) ? $"단일 알림 {i + 1}" : Name.Trim();
}

/// <summary>반복 주기 알림 한 칸</summary>
public class RepeatSlot
{
    /// <summary>고를 수 있는 색: 빨강·주황·노랑·초록·하늘·파랑·보라·분홍</summary>
    public static readonly int[] Palette =
    {
        Color.FromArgb(240, 60, 60).ToArgb(), Color.FromArgb(255, 140, 0).ToArgb(), Color.FromArgb(255, 240, 0).ToArgb(),
        Color.FromArgb(70, 210, 90).ToArgb(), Color.FromArgb(40, 200, 240).ToArgb(), Color.FromArgb(40, 110, 255).ToArgb(),
        Color.FromArgb(150, 80, 240).ToArgb(), Color.FromArgb(235, 70, 140).ToArgb(),
    };
    public bool On { get; set; }
    public int Key { get; set; }
    public bool Combo { get; set; }       // 켜면 조합 키를 누른 채로 눌러야 센다
    public bool WaitEnter { get; set; }   // 켜면 키 다음 엔터를 누른 때부터 센다
    public int Sec { get; set; } = 30;    // 주기 (초)
    public int UntilSec { get; set; }     // 키를 누른 뒤 이만큼 반복하고 멈춘다 (0이면 끌 때까지)
    public int Argb { get; set; }
    public DateTime? Start { get; set; }  // 키를 누른 시각 (이때부터 주기마다 알린다)
    public string? Name { get; set; }     // 알림에 뜨는 글. 비어 있으면 "반복 알림 1" 같은 기본 이름
    public string Title(int i) => string.IsNullOrWhiteSpace(Name) ? $"반복 알림 {i + 1}" : Name.Trim();
}
