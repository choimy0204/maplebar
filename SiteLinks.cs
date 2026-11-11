namespace MapleBar;

public static class Webtoon
{
    public const string Home = "https://m.comic.naver.com/"; // 내 웹툰은 페이지의 MY 버튼으로 간다
    public static string LoginUrl => "https://nid.naver.com/nidlogin.login?mode=form&url=" + Uri.EscapeDataString(Home);
    /// <summary>웹툰·네이버 로그인 화면 (세로로 긴 창, 스크롤 키 대상)</summary>
    public static bool IsWebtoon(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) &&
        (u.Host.EndsWith("comic.naver.com", StringComparison.OrdinalIgnoreCase) || u.Host.Equals("nid.naver.com", StringComparison.OrdinalIgnoreCase));
    /// <summary>네이버 안의 주소 (새 창 링크를 앱 안에서 연다)</summary>
    public static bool IsNaver(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) &&
        (u.Host.Equals("naver.com", StringComparison.OrdinalIgnoreCase) || u.Host.EndsWith(".naver.com", StringComparison.OrdinalIgnoreCase));
}

public static class Music
{
    public const string Home = "https://meaegi.com/music";
    /// <summary>메애기 주소 중 코디 페이지를 뺀 나머지 (숨은 뮤직 창 하나가 맡는다)</summary>
    public static bool IsMusic(string url) => IsMeaegi(url) && !Coordi.IsCoordi(url);
    public static bool IsMeaegi(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Host.EndsWith("meaegi.com", StringComparison.OrdinalIgnoreCase);
}

/// <summary>메애기 코디 페이지: 뮤직 창과 따로, 보통 사이트 창으로 연다 (광고는 막지 않는다)</summary>
public static class Coordi
{
    public const string Base = "https://meaegi.com";
    static readonly string[] Paths = { "/dressing-room", "/analysis/coordi", "/lookbook" };
    public static bool IsCoordi(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Host.EndsWith("meaegi.com", StringComparison.OrdinalIgnoreCase) &&
        Paths.Any(p => u.AbsolutePath.StartsWith(p, StringComparison.OrdinalIgnoreCase));
}
