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
    public static bool IsMusic(string url) => IsMeaegi(url) && !Coordi.IsCoordi(url) || IsYtMusic(url);
    public static bool IsMeaegi(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Host.EndsWith("meaegi.com", StringComparison.OrdinalIgnoreCase);

    // 유튜브 뮤직: 메이플 브금과 같은 숨은 뮤직 창 하나를 같이 쓴다 (둘 중 하나만 켜진다)
    public const string YtHome = "https://music.youtube.com/";
    public static bool IsYtMusic(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Host.Equals("music.youtube.com", StringComparison.OrdinalIgnoreCase);
}

/// <summary>유튜브: 목록 창에서 고르고, 영상·쇼츠는 재생 창 하나에서 본다</summary>
public static class YouTube
{
    public const string Home = "https://www.youtube.com/";
    public const string Shorts = "https://www.youtube.com/shorts";
    public static string LoginUrl => "https://accounts.google.com/ServiceLogin?service=youtube&continue=" + Uri.EscapeDataString(Home);
    static bool On(Uri u, string host) =>
        u.Host.Equals(host, StringComparison.OrdinalIgnoreCase) || u.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase);
    /// <summary>유튜브 주소, 또는 그 로그인 화면 (앱 안에서 연다)</summary>
    public static bool IsYouTube(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) &&
        (On(u, "youtube.com") || On(u, "youtu.be") || u.Host.Equals("accounts.google.com", StringComparison.OrdinalIgnoreCase));
    /// <summary>재생 창에서 열 주소: 영상(/watch), 쇼츠(/shorts), 짧은 주소(youtu.be)</summary>
    public static bool IsVideo(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) &&
        (On(u, "youtu.be") && u.AbsolutePath.Length > 1 ||
         On(u, "youtube.com") && (u.AbsolutePath.Equals("/watch", StringComparison.OrdinalIgnoreCase) ||
                                  u.AbsolutePath.StartsWith("/shorts", StringComparison.OrdinalIgnoreCase)));
    public static bool IsShorts(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && On(u, "youtube.com") &&
        u.AbsolutePath.StartsWith("/shorts", StringComparison.OrdinalIgnoreCase);

    // 댓글 창: 영상 페이지 주소 끝에 이 표시를 붙여 재생 창과 구분한다
    const string CommentsMark = "#mb-comments";
    public static bool IsComments(string? url) => url != null && url.EndsWith(CommentsMark, StringComparison.Ordinal) && IsVideo(url);
    public static string CommentsUrl(string id, bool shorts) =>
        (shorts ? "https://www.youtube.com/shorts/" : "https://www.youtube.com/watch?v=") + Uri.EscapeDataString(id) + CommentsMark;

    /// <summary>영상·쇼츠 주소에서 영상 번호를 꺼낸다 (없으면 null).</summary>
    public static string? VideoId(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return null;
        var p = u.AbsolutePath;
        if (On(u, "youtu.be")) { var t = p.Trim('/'); return t.Length > 0 ? t : null; }
        if (!On(u, "youtube.com")) return null;
        if (p.StartsWith("/shorts/", StringComparison.OrdinalIgnoreCase))
        {
            var id = p[8..].Split('/')[0];
            return id.Length > 0 ? id : null;
        }
        if (p.Equals("/watch", StringComparison.OrdinalIgnoreCase))
            foreach (var part in u.Query.TrimStart('?').Split('&'))
                if (part.StartsWith("v=", StringComparison.Ordinal) && part.Length > 2) return Uri.UnescapeDataString(part[2..]);
        return null;
    }
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
