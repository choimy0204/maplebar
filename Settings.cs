using System.Text.Json;

namespace MapleBar;

/// <summary>최근 검색한 닉네임, 바 위치, 패널 크기를 저장한다.</summary>
public class Settings
{
    public List<string> Recent { get; set; } = new();
    public int? BarX { get; set; }
    public int? BarY { get; set; }
    public int? BarCX { get; set; } // 바의 가운데 x (폭이 바뀌어도 자리가 밀리지 않게)
    // 사이트 창: 마지막 위치·크기, 항상 위 여부
    public int? WinX { get; set; }
    public int? WinY { get; set; }
    public int WinW { get; set; }
    public int WinH { get; set; }
    public bool WinTopMost { get; set; } = true;
    // 뮤직: 곡이 바뀔 때 레코드 효과음
    public bool MusicFx { get; set; } = true;
    // 바: 마우스가 3초 동안 없으면 위로 접는다 (끄면 항상 펼쳐 둔다)
    public bool BarAutoHide { get; set; } = true;
    // 뮤직 칸을 바에서 꺼내 따로 둔 자리 (MusicFloat가 false면 바 안에 있다)
    public bool MusicFloat { get; set; }
    public int? MusicX { get; set; }
    public int? MusicY { get; set; }
    // 인벤 자유게시판 알림으로 지켜볼 목록 (Inven.Feeds의 Key)
    public List<string> InvenWatch { get; set; } = new();

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
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { }
        return new();
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
