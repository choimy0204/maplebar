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
    public bool ClockOn { get; set; } = true;
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
    // 네이버 웹툰: 누르고 있는 동안 부드럽게 위/아래로 스크롤하는 키와 속도(초당 픽셀)
    public int WebtoonUpKey { get; set; }
    public int WebtoonDownKey { get; set; }
    public int WebtoonSpeed { get; set; } = 900;
    public int WebtoonNextKey { get; set; } // 다음 화
    public int WebtoonPrevKey { get; set; } // 이전 화

    static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MapleBar");
    static string FilePath => Path.Combine(Dir, "settings.json");

    // 사이트 로그인·사람 확인 쿠키가 남도록 WebView2 데이터를 여기에 둔다
    public static string WebDataDir => Path.Combine(Dir, "WebView2");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return FixBuffs(JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new());
        }
        catch { }
        return FixBuffs(new());
    }

    /// <summary>버프 칸을 3개로 맞춘다 (기본 30·20·10분). 예전 버프 하나짜리 설정은 같은 시간 칸으로 옮긴다.</summary>
    static Settings FixBuffs(Settings s)
    {
        bool fresh = s.Buffs.Count == 0;
        int[] mins = { 30, 20, 10 };
        while (s.Buffs.Count < 3) s.Buffs.Add(new BuffSlot { Min = mins[s.Buffs.Count] });
        if (s.Buffs.Count > 3) s.Buffs.RemoveRange(3, s.Buffs.Count - 3);
        if (fresh && (s.BuffOn || s.BuffKey != 0 || s.BuffStart != null))
        {
            var b = s.Buffs.FirstOrDefault(x => x.Min == s.BuffMin) ?? s.Buffs[0];
            b.On = s.BuffOn; b.Key = s.BuffKey; b.Min = s.BuffMin; b.Start = s.BuffStart;
            s.BuffOn = false; s.BuffKey = 0; s.BuffStart = null;
            s.Save();
        }
        return s;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public void AddRecent(string name)
    {
        Recent.RemoveAll(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
        Recent.Insert(0, name);
        if (Recent.Count > 10) Recent.RemoveRange(10, Recent.Count - 10);
    }
}

/// <summary>버프 알림 한 칸</summary>
public class BuffSlot
{
    public bool On { get; set; }
    public int Key { get; set; }
    public int Min { get; set; } = 30;
    public DateTime? Start { get; set; }
    public bool WaitEnter { get; set; } = true; // 키 다음 엔터까지 눌러야 센다 (끄면 키만 눌러도 센다)
    public string? Name { get; set; } // 비어 있으면 "버프 1" 같은 기본 이름
}
