using System.Net;
using System.Text.RegularExpressions;

namespace MapleBar;

public record InvenPost(long Id, string Title, string Category, string Url);

/// <summary>메이플스토리 인벤 자유게시판 (작은 창에 맞게 모바일 주소를 쓴다).</summary>
public static class Inven
{
    public const string Board = "https://m.inven.co.kr/board/maple/5974";
    /// <summary>모바일 화면을 받으려고 쓰는 브라우저 이름 (알림 확인 요청과 인벤·웹툰 창이 같이 쓴다).</summary>
    public const string MobileUA = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Mobile Safari/537.36";
    public static string LoginUrl => "https://member.inven.co.kr/user/scorpio/mlogin?url=" + Uri.EscapeDataString(Board);

    /// <summary>알림을 받을 수 있는 목록: 설정 키, 메뉴 이름, 알림 꼬리표, 목록 주소 뒤에 붙는 값.</summary>
    public static readonly (string Key, string Label, string Tag, string Query)[] Feeds =
    {
        ("new", "새 글", "새 글", ""),
        ("chu", "새 10추 글", "10추 글", "?my=chu"),
        ("chuchu", "새 30추 글", "30추 글", "?my=chuchu"),
        ("chuver", "새 인증 30추 글", "인증 30추 글", "?my=chuver"),
    };

    public static bool IsInven(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Host.EndsWith("inven.co.kr", StringComparison.OrdinalIgnoreCase);

    static readonly HttpClient http = MakeClient();
    static HttpClient MakeClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd(MobileUA);
        return c;
    }

    static readonly Regex Item = new(
        "<a href=\"https://m\\.inven\\.co\\.kr/board/maple/5974/(\\d+)[^\"]*\" class=\"contentLink\">([\\s\\S]*?)</a>",
        RegexOptions.Compiled);
    static readonly Regex Subject = new("<span class=\"subject\">([\\s\\S]*?)</span>", RegexOptions.Compiled);
    static readonly Regex Cate = new("<span class=\"in-cate\">([\\s\\S]*?)</span>", RegexOptions.Compiled);
    static readonly Regex Tags = new("<[^>]+>", RegexOptions.Compiled);

    static string Clean(string html) => WebUtility.HtmlDecode(Tags.Replace(html, "")).Trim();

    /// <summary>목록 첫 쪽의 글 (공지 제외).</summary>
    public static async Task<List<InvenPost>> FetchAsync(string query)
    {
        var html = await http.GetStringAsync(Board + query);
        var list = new List<InvenPost>();
        foreach (Match m in Item.Matches(html))
        {
            var inner = m.Groups[2].Value;
            var s = Subject.Match(inner);
            if (!s.Success) continue;
            var c = Cate.Match(inner);
            long id = long.Parse(m.Groups[1].Value);
            list.Add(new InvenPost(id, Clean(s.Groups[1].Value), c.Success ? Clean(c.Groups[1].Value) : "", Board + "/" + id));
        }
        return list;
    }
}

/// <summary>
/// 설정에서 고른 자유게시판 목록을 확인해서, 새로 올라온 글을 알린다.
/// 새 글은 5초마다, 추천 글 목록은 천천히 바뀌므로 1분마다 본다.
/// </summary>
public sealed class InvenWatcher
{
    const int TickMs = 5_000, ChuEvery = 12; // 추천 목록은 12번에 1번 (1분)
    readonly Settings settings;
    readonly Action<string, InvenPost> notify; // (꼬리표, 글)
    readonly System.Windows.Forms.Timer timer = new() { Interval = TickMs };
    public readonly Action Stop;
    int ticks;
    // 목록마다 이미 본 글 번호와, 지난번 목록의 가장 오래된 글 번호
    readonly Dictionary<string, (HashSet<long> Seen, long Oldest)> state = new();
    bool busy;

    public InvenWatcher(Settings settings, Action<string, InvenPost> notify)
    {
        this.settings = settings;
        this.notify = notify;
        timer.Tick += async (_, _) => await PollAsync(ticks++ % ChuEvery == 0);
        Stop = timer.Stop;
        timer.Start();
        _ = PollAsync(true);
    }

    /// <summary>체크를 바꿨을 때: 새로 고른 목록을 바로 기억해 두어 다음 확인부터 알릴 수 있게 한다.</summary>
    public void PollNow() => _ = PollAsync(true);

    async Task PollAsync(bool all)
    {
        if (busy) return;
        busy = true;
        try
        {
            foreach (var f in Inven.Feeds)
            {
                if (!settings.InvenWatch.Contains(f.Key)) { state.Remove(f.Key); continue; }
                if (!all && f.Key != "new" && state.ContainsKey(f.Key)) continue;
                List<InvenPost> posts;
                try { posts = await Inven.FetchAsync(f.Query); } catch { continue; }
                if (posts.Count == 0) continue;
                long oldest = posts.Min(p => p.Id);

                // 처음 확인할 때는 지금 있는 글을 기억만 한다
                if (!state.TryGetValue(f.Key, out var st))
                {
                    state[f.Key] = (posts.Select(p => p.Id).ToHashSet(), oldest);
                    continue;
                }

                // 지난번 목록보다 오래된 글이 끝에 끼어든 경우(글 삭제 등)는 새 글로 치지 않는다
                var fresh = posts.Where(p => !st.Seen.Contains(p.Id) && p.Id > st.Oldest)
                                 .OrderBy(p => p.Id).ToList();
                foreach (var p in posts) st.Seen.Add(p.Id);
                state[f.Key] = (st.Seen, oldest);

                foreach (var p in fresh.TakeLast(3)) notify(f.Tag, p); // 한 번에 너무 많이 뜨지 않게
            }
        }
        finally { busy = false; }
    }
}
