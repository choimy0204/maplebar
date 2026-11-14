using System.Text.Json;

namespace MapleBar;

/// <summary>최근 검색한 닉네임, 바 위치, 패널 크기를 저장한다.</summary>
public class Settings
{
    public List<string> Recent { get; set; } = new();
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
    public int WinW { get; set; }
    public int WinH { get; set; }
    public bool WinTopMost { get; set; } = true;
    public int WinOpacity { get; set; } = 100; // 사이트 창 투명도(%), 마지막으로 고른 값
    // 뮤직: 곡이 바뀔 때 레코드 효과음
    public bool MusicFx { get; set; } = true;
    // 바: 마우스가 3초 동안 없으면 위로 접는다 (끄면 항상 펼쳐 둔다)
    public bool BarAutoHide { get; set; } = true;
    // 바를 숨겨도(Ctrl+Alt+M) 야누스·룬·버프·도핑 알림을 띄운다
    public bool AlertWhenHidden { get; set; } = true;
    // 시계: 켜기, 초·날짜·24시간 보기, 게임 창 안에서의 자리(비율, 없으면 오른쪽 위)
    public bool ClockOn { get; set; } = false;
    // 유튜브 목록 창에서 영상을 누르면 재생 창을 따로 띄우지 않고 그 창에서 본다
    public bool YtSameWindow { get; set; }
    public bool ClockSeconds { get; set; } = true;
    public bool ClockDate { get; set; } = true;
    public bool Clock24 { get; set; } = false;
    public double? ClockFX { get; set; }
    public double? ClockFY { get; set; }
    // 알림 소리: 켜기와 크기(%)
    public bool SoundOn { get; set; } = true;
    public int SoundVol { get; set; } = 60;
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
    public int JanusSec { get; set; } = 80;
    public bool LunaExt { get; set; } = true;   // 루나 익스텐션: 있으면 다시 깔 때 시간을 새로 센다
    public bool PickupOn { get; set; } = true;  // 설치 65초 뒤 아이템 회수 알림
    public bool PetOn { get; set; }             // 5석펫: 게더링(쿨 90초)마다 야누스 시간이 더해진다
    public DateTime? PetGatherAt { get; set; }   // 지금 세는 야누스의 게더링 박자 기준 (이 시각 + 90초×k)
    public DateTime? PetLastGather { get; set; } // 마지막 게더링 (다음 설치 때 쿨이 남았는지 본다)
    // 룬 알림: NPC 대화 키 뒤에 방향키 4번을 누르면 룬 주기(분)를 세고, 다 되면 알린다
    public bool RuneOn { get; set; }
    public int NpcKey { get; set; }
    public int RuneMin { get; set; } = 15;
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
    public bool PendantOn { get; set; }
    public int PendantBuff { get; set; }           // 버프 칸 번호 (0~2)
    // 반복 주기 알림 3칸: 키를 누르면 정한 주기마다 정한 색으로 알린다
    public List<RepeatSlot> Repeats { get; set; } = new();
    // 알림을 띄울 모니터 (Screen.DeviceName). "ALL"이면 모든 모니터, 비어 있어도 모든 모니터
    public List<string> AlertScreens { get; set; } = new() { "ALL" };
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
                return FixBuffs(JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new());
        }
        catch (Exception ex) { Log.Write("settings", "설정을 읽지 못해 기본값으로 시작함", ex); }
        return FixBuffs(new());
    }

    /// <summary>버프 칸을 3개로 맞춘다 (기본 30·20·10분). 예전 버프 하나짜리 설정은 같은 시간 칸으로 옮긴다.</summary>
    static Settings FixBuffs(Settings s)
    {
        bool fresh = s.Buffs.Count == 0;
        int[] mins = { 30, 20, 10 };
        while (s.Buffs.Count < 3) s.Buffs.Add(new BuffSlot { Min = mins[s.Buffs.Count] });
        if (s.Buffs.Count > 3) s.Buffs.RemoveRange(3, s.Buffs.Count - 3);
        int[] colors = { RepeatSlot.Palette[2], RepeatSlot.Palette[4], RepeatSlot.Palette[6] };
        while (s.Repeats.Count < 3) s.Repeats.Add(new RepeatSlot { Argb = colors[s.Repeats.Count] });
        if (s.Repeats.Count > 3) s.Repeats.RemoveRange(3, s.Repeats.Count - 3);
        s.AlertScreens ??= new() { "ALL" };
        s.Decks ??= new();
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
    public string? Name { get; set; }          // 비어 있으면 "도핑 1" 같은 기본 이름
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
    public int Sec { get; set; } = 30;    // 주기 (초)
    public int UntilSec { get; set; }     // 키를 누른 뒤 이만큼 반복하고 멈춘다 (0이면 끌 때까지)
    public int Argb { get; set; }
    public DateTime? Start { get; set; }  // 키를 누른 시각 (이때부터 주기마다 알린다)
    public string? Name { get; set; }     // 알림에 뜨는 글. 비어 있으면 "반복 알림 1" 같은 기본 이름
    public string Title(int i) => string.IsNullOrWhiteSpace(Name) ? $"반복 알림 {i + 1}" : Name.Trim();
}
