using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MapleBar;

/// <summary>
/// 사이트별 로그인 여부를 알아 둔다. 쿠키는 이름만 보고(값은 읽지 않음) 어디로도 보내지 않는다.
/// 로그인과 로그아웃은 사용자가 열린 창에서 직접 한다.
/// </summary>
static class Logins
{
    public sealed record Service(string Key, string Name, string Hint, string LoginUrl, string CookieUrl, string[] CookieNames);

    // 인벤 로그인 쿠키 이름 후보 (실제 이름을 아직 확인하지 못했다). 틀리면 여기만 고친다.
    // 이름이 아래 중 하나와 같거나(대소문자 무시), 아래 조각을 포함하면 로그인으로 본다.
    static readonly string[] InvenNameEquals = { "M_ID", "MemberID", "inven_member", "INVEN_LOGIN" };
    static readonly string[] InvenNameContains = { "member", "login" };

    public static readonly Service[] Services =
    {
        new("nexon", "넥슨", "경매장", Nexon.LoginUrl, "https://www.nexon.com/", new[] { "NPP" }),
        new("google", "구글 (유튜브)", "유튜브 · 유튜브 뮤직", YouTube.LoginUrl, "https://www.youtube.com/", new[] { "LOGIN_INFO", "SID", "__Secure-3PSID" }),
        new("naver", "네이버", "웹툰", Webtoon.LoginUrl, "https://nid.naver.com/", new[] { "NID_AUT" }),
        new("inven", "인벤", "자유게시판 · 알림", Inven.LoginUrl, "https://www.inven.co.kr/", Array.Empty<string>()),
    };

    static readonly Dictionary<string, bool?> state = new();

    /// <summary>true 로그인됨, false 로그인 안 됨, null 아직 모름</summary>
    public static bool? IsIn(string key) => state.TryGetValue(key, out var v) ? v : null;

    /// <summary>로그인 여부가 바뀌었을 때 (화면 스레드에서)</summary>
    public static event Action? Changed;

    static Form? host;
    static WebView2? view;
    static bool refreshing;
    static System.Windows.Forms.Timer? soon;

    /// <summary>잠깐 뒤 한 번만 확인한다. 자주 불러도 괜찮다.</summary>
    public static void RefreshSoon()
    {
        if (soon == null)
        {
            soon = new System.Windows.Forms.Timer { Interval = 1500 };
            soon.Tick += async (_, _) =>
            {
                soon!.Stop();
                await RefreshAsync();
            };
        }
        soon.Stop();
        soon.Start();
    }

    public static async Task RefreshAsync()
    {
        if (refreshing) return;
        refreshing = true;
        try
        {
            var cm = await GetCookieManagerAsync();
            if (cm == null) return;
            bool changed = false;
#if DEBUG
            bool debug = Environment.GetEnvironmentVariable("MB_DEBUG") == "1";
#else
            const bool debug = false; // 쿠키 이름 기록은 테스트 빌드에서만
#endif
            foreach (var s in Services)
            {
                try
                {
                    var cookies = await cm.GetCookiesAsync(s.CookieUrl);
                    var names = cookies.Select(c => c.Name).ToList(); // 이름만 쓴다
                    bool isIn = Check(s, names);
                    if (debug && (s.Key == "inven" || s.Key == "nexon"))
                        Log.Write("로그인", s.Key + " 쿠키 이름: " + string.Join(", ", names));
                    if (IsIn(s.Key) != isIn) { state[s.Key] = isIn; changed = true; }
                }
                catch (Exception ex) { Log.Write("로그인", s.Key + " 확인 실패", ex); }
            }
            if (changed) Changed?.Invoke();
        }
        catch (Exception ex) { Log.Write("로그인", "확인 실패", ex); }
        finally { refreshing = false; }
    }

    // 로그아웃할 때 지울 쿠키의 주소 (이 주소와 그 아래 주소의 쿠키를 지운다)
    static readonly Dictionary<string, string[]> LogoutDomains = new()
    {
        ["nexon"] = new[] { "nexon.com" },
        ["google"] = new[] { "google.com", "youtube.com" },
        ["naver"] = new[] { "naver.com" },
        ["inven"] = new[] { "inven.co.kr" },
    };

    // 로그인 창이 머물러도 되는 곳 (이 밖으로 가는 이동은 막는다. 주소 칸이 없어 가짜 페이지를 알아보기 어렵기 때문)
    static readonly Dictionary<string, string[]> StayDomains = new()
    {
        // 넥슨은 구글·네이버·애플 등으로도 로그인할 수 있어 그 로그인 사이트도 둔다
        ["nexon"] = new[] { "nexon.com", "nexon.co.kr", "google.com", "naver.com", "apple.com", "kakao.com", "facebook.com" },
        ["google"] = new[] { "google.com", "google.co.kr", "youtube.com" },
        ["naver"] = new[] { "naver.com" },
        ["inven"] = new[] { "inven.co.kr" },
    };

    /// <summary>그 주소가 로그인 화면 주소면 머물러도 되는 도메인 목록, 아니면 null</summary>
    public static string[]? StayDomainsFor(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return null;
        foreach (var s in Services)
            if (Uri.TryCreate(s.LoginUrl, UriKind.Absolute, out var lu) && u.Host.Equals(lu.Host, StringComparison.OrdinalIgnoreCase) &&
                u.AbsolutePath.Equals(lu.AbsolutePath, StringComparison.OrdinalIgnoreCase))
                return StayDomains.TryGetValue(s.Key, out var d) ? d : null;
        return null;
    }

    /// <summary>구글·애플 로그인 팝업이 머물러도 되는 도메인 (연 페이지의 도메인은 따로 더한다)</summary>
    public static readonly string[] PopupDomains = { "google.com", "google.co.kr", "youtube.com", "apple.com", "icloud.com" };

    /// <summary>점까지 맞춰 보고 목록 안의 도메인인지 (http·https만)</summary>
    public static bool MayStay(Uri u, IEnumerable<string> domains) =>
        (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp) && domains.Any(d => Hosts.Is(u, d));

    /// <summary>이 프로그램 안에서만 로그아웃한다 (그 사이트의 쿠키를 지운다). 다른 브라우저에는 영향이 없다.</summary>
    public static async Task LogoutAsync(string key)
    {
        if (!LogoutDomains.TryGetValue(key, out var domains)) return;
        try
        {
            var cm = await GetCookieManagerAsync();
            if (cm == null) return;
            var all = await cm.GetCookiesAsync(null); // 이 프로그램의 쿠키 전체
            foreach (var c in all)
            {
                var d = c.Domain.TrimStart('.');
                if (domains.Any(x => d.Equals(x, StringComparison.OrdinalIgnoreCase) || d.EndsWith("." + x, StringComparison.OrdinalIgnoreCase)))
                    cm.DeleteCookie(c);
            }
        }
        catch (Exception ex) { Log.Write("로그인", key + " 로그아웃 실패", ex); }
        await RefreshAsync();
    }

    static bool Check(Service s, List<string> names)
    {
        if (s.Key == "inven")
        {
            return names.Any(n =>
                InvenNameEquals.Any(e => n.Equals(e, StringComparison.OrdinalIgnoreCase)) ||
                InvenNameContains.Any(c => n.Contains(c, StringComparison.OrdinalIgnoreCase)));
        }
        return names.Any(n => s.CookieNames.Contains(n));
    }

    /// <summary>쿠키 창구를 얻으려고 보이지 않는 작은 웹뷰를 하나만 만들어 계속 쓴다.</summary>
    static async Task<CoreWebView2CookieManager?> GetCookieManagerAsync()
    {
        try
        {
            if (view?.CoreWebView2 != null) return view.CoreWebView2.CookieManager;
            if (view == null)
            {
                host = new Form { ShowInTaskbar = false, Opacity = 0, Size = new Size(1, 1), FormBorderStyle = FormBorderStyle.None };
                host.CreateControl();
                _ = host.Handle; // 보이지 않아도 창 핸들은 만들어 둔다
                view = new WebView2 { Dock = DockStyle.Fill };
                host.Controls.Add(view);
            }
            var env = await WebWindow.Env;
            await view.EnsureCoreWebView2Async(env);
            return view.CoreWebView2?.CookieManager;
        }
        catch (Exception ex)
        {
            Log.Write("로그인", "쿠키 창구를 못 만들었어요", ex);
            return null;
        }
    }
}
