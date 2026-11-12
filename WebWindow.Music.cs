using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

using static MapleBar.Native;
using static MapleBar.WebScripts;

namespace MapleBar;

public partial class WebWindow
{
    // ── 뮤직: 메애기 창은 숨겨 두고 노래만 재생한다. 바의 작은 컨트롤이 이 창을 조종한다. ──
    public record MusicInfo(string Title, string Sub, string Time, bool Playing, double Volume, string Img,
        bool Repeat, bool Shuffle, bool Fx, bool Yt = false, string? Lyrics = null, int LyricLine = -1);

    // 유튜브 뮤직 창인지 (아니면 메애기 브금 창)
    readonly bool yt;
    /// <summary>지금 켜진 뮤직이 유튜브 뮤직이면 true.</summary>
    public static bool MusicIsYt => MusicWin?.yt == true;
    /// <summary>가사 보기를 바꾸면 곡 정보를 바로 다시 읽는다.</summary>
    public static void MusicRefresh() { var w = MusicWin; if (w != null) _ = w.PollSoon(); }

    // 설정의 효과음 켜짐/꺼짐을 페이지에 알려 준다 (새로 열린 페이지에도 적용)
    static string FxScript(bool on) => $"window.mbSetFx&&window.mbSetFx({(on ? "true" : "false")});";
    string? fxScriptId;

    /// <summary>설정에서 곡 넘김 효과음을 켜고 끄면 뮤직 창에 바로 적용한다.</summary>
    public static async void SetMusicFx(bool on)
    {
        var w = MusicWin;
        var cw = w?.web.CoreWebView2;
        if (w == null || cw == null) return;
        try
        {
            if (w.fxScriptId != null) cw.RemoveScriptToExecuteOnDocumentCreated(w.fxScriptId);
            w.fxScriptId = await cw.AddScriptToExecuteOnDocumentCreatedAsync(FxScript(on));
            await cw.ExecuteScriptAsync(FxScript(on));
        }
        catch { }
    }

    // 재생 방식 이름 (페이지 스크립트의 mbMode가 받는 값)
    public const string ModeRepeat = "rep", ModeShuffle = "shuf";

    /// <summary>ModeRepeat 반복 재생, ModeShuffle 랜덤 재생을 켜고 끈다.</summary>
    public static void MusicMode(string m)
    {
        var w = MusicWin;
        if (w?.web.CoreWebView2 == null) return;
        _ = w.web.CoreWebView2.ExecuteScriptAsync($"window.mbMode&&window.mbMode('{(m == ModeRepeat ? ModeRepeat : ModeShuffle)}')");
        _ = w.PollSoon();
    }

    /// <summary>소리 크기(0~1)를 바꾼다.</summary>
    public static void MusicVolume(double v) =>
        _ = MusicWin?.web.CoreWebView2?.ExecuteScriptAsync($"window.mbVol&&window.mbVol({v.ToString(System.Globalization.CultureInfo.InvariantCulture)})");
    /// <summary>곡 정보가 바뀌면 알린다. null이면 뮤직 창이 닫힌 것.</summary>
    public static event Action<MusicInfo?>? MusicChanged;
    /// <summary>뮤직 창이 마지막으로 알린 곡 (없으면 null).</summary>
    public static MusicInfo? CurrentMusic => MusicWin?.lastInfo;
    bool reallyClose;
    readonly System.Windows.Forms.Timer musicTimer = new() { Interval = 1000 };
    string lastTime = "";
    MusicInfo? lastInfo;

    /// <summary>뮤직 창을 찾거나 새로 연다. show가 false면 화면에 띄우지 않고 뒤에서만 연다.</summary>
    public static WebWindow EnsureMusic(Settings st, float scale, string url, bool show)
    {
        var w = MusicWin;
        // 브금과 유튜브 뮤직은 하나만 켠다: 다른 쪽이 켜져 있으면 끄고 새로 연다
        if (w != null && w.yt != Music.IsYtMusic(url)) { StopMusic(); w = null; }
        if (w == null)
        {
            // 처음부터 화면 밖에 투명하게 띄워 둔다 (잠깐 보이지 않게)
            w = new WebWindow(url, st, scale);
            w.Park();
            w.Show();
        }
        else if (show) { ShowMusic(url); return w; }
        if (show) { w.Unpark(); w.Activate(); }
        return w;
    }

    // 뮤직 창은 숨기지 않고 화면 밖으로 치워 둔다. 숨기면 웹 화면이 멈춰 노래가 안 나올 수 있다.
    bool parked;
    Rectangle parkedFrom;
    void Park()
    {
        if (parked) return;
        if (WindowState != FormWindowState.Normal) WindowState = FormWindowState.Normal;
        parkedFrom = Bounds;
        parked = true;
        Opacity = 0;
        Location = new Point(-32000, -32000);
    }
    void Unpark()
    {
        if (parked) { parked = false; Bounds = parkedFrom; Opacity = 1; }
        if (!Visible) Show();
    }

    async Task<string?> Eval(string js)
    {
        for (int i = 0; i < 100 && web.CoreWebView2 == null && !IsDisposed; i++) await Task.Delay(100);
        if (web.CoreWebView2 == null || IsDisposed) return null;
        try { return await web.CoreWebView2.ExecuteScriptAsync(js); }
        catch (Exception ex) { Log.Write("music", "스크립트 실행 실패", ex); return null; }
    }

    public record Song(string Title, string Sub, string Img);

    /// <summary>목록 읽기·곡 틀기가 어떻게 끝났는지.</summary>
    public enum MusicStatus
    {
        Ok,
        /// <summary>검색했는데 맞는 곡이 없다</summary>
        Empty,
        /// <summary>틀려는 곡이 사이트 목록에 없다</summary>
        NotFound,
        /// <summary>사이트가 제때 응답하지 않았다 (느리거나 연결이 끊김)</summary>
        Timeout,
        /// <summary>페이지는 열렸는데 곡 줄·검색 칸을 못 찾았다 (사이트 화면이 바뀐 듯)</summary>
        PageChanged,
        /// <summary>뮤직 창이 닫혔다</summary>
        NoWindow,
        /// <summary>더 새로운 요청에 밀려 그만뒀다</summary>
        Canceled,
    }

    public record MusicList(List<Song> Songs, MusicStatus Status);

    // ── 숨은 뮤직 창은 하나뿐이라, 페이지를 옮기거나 검색하는 일은 한 번에 하나만 한다 ──
    // 사용자가 새로 누른 일(preempt)은 하던 일을 그만두게 하고 그다음에 돈다. 랜덤 다음 곡 같은 뒷일은 줄만 선다.
    static readonly SemaphoreSlim musicGate = new(1, 1);
    static CancellationTokenSource? musicJob;

    static async Task<T> RunMusicJob<T>(Func<CancellationToken, Task<T>> job, T canceled, T failed, bool preempt = true)
    {
        var cts = new CancellationTokenSource();
        if (preempt) { musicJob?.Cancel(); musicJob = cts; }
        bool entered = false;
        try
        {
            await musicGate.WaitAsync(cts.Token);
            entered = true;
            return await job(cts.Token);
        }
        catch (OperationCanceledException) { return canceled; }
        catch (Exception ex) { Log.Write("music", "뮤직 작업 실패", ex); return failed; }
        finally
        {
            if (entered) musicGate.Release();
            if (musicJob == cts) musicJob = null;
            cts.Dispose();
        }
    }

    static string Js(string s) => System.Text.Json.JsonSerializer.Serialize(s);
    const string SearchPath = "/music/search";

    /// <summary>ok가 true를 줄 때까지 step(ms)마다 다시 본다. limit(ms)을 넘기면 false.</summary>
    static async Task<bool> Until(Func<Task<bool>> ok, int limit, int step, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (await ok()) return true;
            if (sw.ElapsedMilliseconds >= limit) return false;
            await Task.Delay(step, ct);
        }
    }

    /// <summary>
    /// 뒤에 숨은 뮤직 창에서 path 페이지(인기 차트 /music, 검색 /music/search)의 곡 목록을 읽는다. 노래는 끊기지 않는다.
    /// 먼저 하던 목록 읽기·곡 틀기가 있으면 그만두게 하고 돈다.
    /// </summary>
    public static Task<MusicList> MusicListAsync(Settings st, float scale, string path, string? query = null) =>
        RunMusicJob(ct => ListCore(st, scale, path, query, ct),
            new MusicList(new(), MusicStatus.Canceled), new MusicList(new(), MusicStatus.Timeout));

    static async Task<MusicList> ListCore(Settings st, float scale, string path, string? query, CancellationToken ct)
    {
        var w = EnsureMusic(st, scale, Music.Home.Replace("/music", "") + path, false);
        bool search = path == SearchPath;
        // 검색 페이지는 검색어가 없어도 칸을 비워야 한다 (앞서 넣은 검색어가 남아 있으면 그 결과만 보인다)
        string? q = search ? query ?? "" : null;
        string p = Js(path);

        // 1) 페이지 옮기기: 2초에 한 번만 옮겨 달라고 하고, 그 사이에는 주소만 본다 (자꾸 옮기면 읽던 페이지가 끊긴다)
        var asked = Stopwatch.StartNew();
        bool first = true;
        bool there = await Until(async () =>
        {
            if (first || asked.ElapsedMilliseconds >= 2000) { first = false; asked.Restart(); await w.Eval($"window.mbGo&&window.mbGo({p})"); }
            return await w.Eval("window.mbHere?window.mbHere():''") == p;
        }, 15000, 200, ct);
        if (!there) return await w.Fail("페이지 이동", path, q);

        // 2) 검색: 곡 자료가 실린 것을 한 번 본 다음에 검색어를 넣는다 (자료가 오기 전의 0곡을 '결과 없음'으로 잘못 보지 않게)
        if (!string.IsNullOrEmpty(q) && await w.Eval("window.mbSeen?window.mbSeen():false") != "true")
        {
            if (await w.Settle("", false, 10000, ct) == null) return await w.Fail("곡 자료 기다리기", path, q);
        }

        // 3) 목록 읽기: 같은 목록이 세 번 이어서 읽히면 다 그려진 것으로 본다
        var list = await w.Settle(q, !string.IsNullOrEmpty(q), search ? 8000 : 10000, ct);
        if (list == null) return await w.Fail("목록 읽기", path, q);
        return new MusicList(list, list.Count > 0 ? MusicStatus.Ok : MusicStatus.Empty);
    }

    /// <summary>
    /// 검색어(q, 검색 페이지가 아니면 null)를 넣고 목록이 더 바뀌지 않을 때까지 기다려 읽는다.
    /// allowEmpty가 false면 곡이 한 줄이라도 나올 때까지 기다린다. 제때 안 되면 null.
    /// </summary>
    async Task<List<Song>?> Settle(string? q, bool allowEmpty, int limit, CancellationToken ct)
    {
        string? last = null;
        int same = 0;
        List<Song>? result = null;
        await Until(async () =>
        {
            // 검색 칸의 글이 q인지 볼 때마다 맞춘다 (페이지가 다시 그려져 지워졌으면 다시 넣는다)
            if (q != null && await Eval($"window.mbSearch?window.mbSearch({Js(q)}):false") != "true") { same = 0; return false; }
            var json = await Eval("window.mbList?JSON.stringify(window.mbList()):null");
            if (string.IsNullOrEmpty(json) || json == "null") { same = 0; return false; }
            if (json == last) same++; else { last = json; same = 1; }
            if (same < 3) return false;
            var list = ParseSongs(json);
            if (list.Count == 0 && !allowEmpty) return false;
            result = list;
            return true;
        }, limit, 150, ct);
        return result;
    }

    /// <summary>목록을 못 읽은 까닭을 가려 기록하고 돌려준다.</summary>
    async Task<MusicList> Fail(string step, string path, string? q)
    {
        var status = MusicStatus.Timeout;
        string? check = null;
        if (IsDisposed) status = MusicStatus.NoWindow;
        else
        {
            check = await Eval("window.mbCheck?JSON.stringify(window.mbCheck()):null");
            if (IsDisposed) status = MusicStatus.NoWindow;
            else
                try
                {
                    var inner = string.IsNullOrEmpty(check) || check == "null" ? null : System.Text.Json.JsonSerializer.Deserialize<string>(check);
                    if (inner != null)
                    {
                        check = inner;
                        using var doc = System.Text.Json.JsonDocument.Parse(inner);
                        var e = doc.RootElement;
                        // 페이지는 다 열렸는데 곡 줄이나 검색 칸이 없으면 사이트 화면이 바뀐 것
                        bool loaded = e.GetProperty("ready").GetString() == "complete" && e.GetProperty("path").GetString() == path;
                        bool noRows = e.GetProperty("rows").GetInt32() == 0 && !e.GetProperty("seen").GetBoolean();
                        bool noBox = path == SearchPath && !e.GetProperty("search").GetBoolean();
                        if (loaded && (noRows || noBox)) status = MusicStatus.PageChanged;
                    }
                }
                catch (Exception ex) { Log.Write("music", "화면 점검 결과를 읽지 못함", ex); }
        }
        Log.Write("music", $"{step} 실패 → {status} (path={path}, q={q ?? "-"}, check={check ?? "없음"})");
        return new MusicList(new(), status);
    }

    static List<Song> ParseSongs(string? json)
    {
        var list = new List<Song>();
        try
        {
            if (json == null) return list;
            // ExecuteScriptAsync는 JSON.stringify 결과를 다시 문자열로 감싸서 준다
            var inner = System.Text.Json.JsonSerializer.Deserialize<string>(json) ?? "[]";
            using var doc = System.Text.Json.JsonDocument.Parse(inner);
            foreach (var e in doc.RootElement.EnumerateArray())
                list.Add(new Song(e.GetProperty("t").GetString() ?? "", e.GetProperty("s").GetString() ?? "",
                    e.TryGetProperty("img", out var im) ? im.GetString() ?? "" : ""));
        }
        catch (Exception ex) { Log.Write("music", "곡 목록 글을 읽지 못함", ex); }
        return list;
    }

    /// <summary>
    /// song을 튼다. 지금 페이지에 그 곡이 있으면 바로 누르고, 없으면 path 페이지(검색이면 query로 검색)에서 찾아 누른다.
    /// pool을 주면 랜덤 재생이 그 곡들 안에서 고른다 (사용자가 곡을 고른 목록).
    /// </summary>
    public static Task<MusicStatus> MusicPlayAsync(Settings st, float scale, Song song, string path, string? query,
        IReadOnlyList<Song>? pool = null, bool preempt = true) =>
        RunMusicJob(ct => PlayCore(st, scale, song, path, query, pool, ct), MusicStatus.Canceled, MusicStatus.Timeout, preempt);

    static async Task<MusicStatus> PlayCore(Settings st, float scale, Song song, string path, string? query,
        IReadOnlyList<Song>? pool, CancellationToken ct)
    {
        static string Click(Song s) => $"window.mbPlay?window.mbPlay({Js(s.Title)},{Js(s.Sub)}):false";
        var w = MusicWin;
        string title = song.Title;
        if (w == null || w.yt || await w.Eval(Click(song)) != "true")
        {
            var r = await ListCore(st, scale, path, query, ct);
            if (r.Status is not (MusicStatus.Ok or MusicStatus.Empty)) return r.Status;
            w = MusicWin;
            if (w == null) return MusicStatus.NoWindow;
            if (await w.Eval(Click(song)) != "true")
            {
                // 지역 이름이 바뀌었을 수 있으니 제목만 같은 곡이라도 튼다
                var alt = r.Songs.FirstOrDefault(x => x.Title == song.Title);
                if (alt == null || await w.Eval(Click(alt)) != "true")
                {
                    Log.Write("music", $"곡을 찾지 못함: {song.Title} / {song.Sub} (path={path}, q={query ?? "-"})");
                    return MusicStatus.NotFound;
                }
            }
        }
        if (pool != null)
            await w.Eval($"window.mbPool&&window.mbPool({System.Text.Json.JsonSerializer.Serialize(pool.Select(x => new { t = x.Title, s = x.Sub }))})");

        // 재생 바의 제목이 그 곡으로 바뀔 때까지 지켜본다. 바뀌었는데 멈춰 있으면 한 번 더 재생을 건다
        string want = Js(title);
        await Until(async () => await w.Eval("(()=>{const s=window.mbState&&window.mbState();return s?s.t:''})()") == want, 3000, 200, ct);
        await Task.Delay(400, ct);
        await w.Eval("(()=>{const a=window.__mbA;if(a&&a.paused&&!a.ended)a.play().catch(()=>{})})()");
        _ = w.PollSoon();
        return MusicStatus.Ok;
    }

    /// <summary>페이지의 랜덤 재생이 고른 곡이 지금 화면에 없을 때 (mb:play): 검색으로 찾아 튼다. 못 틀면 사이트의 다음 곡으로 넘긴다.</summary>
    async Task PlayFromPage(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var song = new Song(doc.RootElement.GetProperty("t").GetString() ?? "", doc.RootElement.GetProperty("s").GetString() ?? "", "");
            if (song.Title == "") return;
            var r = await MusicPlayAsync(settings, sc, song, SearchPath, song.Title, null, preempt: false);
            if (r is not (MusicStatus.Ok or MusicStatus.Canceled)) await Eval("window.mbNextRaw&&window.mbNextRaw()");
        }
        catch (Exception ex) { Log.Write("music", "랜덤 다음 곡 실패", ex); }
    }

    static WebWindow? MusicWin => open.FirstOrDefault(x => x.kind == SiteKind.Music && !x.IsDisposed);
    public static bool MusicOpen => MusicWin != null;

    public static void MusicCmd(int i)
    {
        var w = MusicWin;
        if (w?.web.CoreWebView2 == null) return;
        _ = w.web.CoreWebView2.ExecuteScriptAsync($"window.mbCmd&&window.mbCmd({i})");
        _ = w.PollSoon();
    }

    /// <summary>뮤직 창이 보이면 숨기고, 숨어 있으면 띄운다.</summary>
    public static void ToggleMusicWindow()
    {
        var w = MusicWin;
        if (w == null) return;
        if (!w.parked && w.Visible && w.WindowState != FormWindowState.Minimized) { w.Park(); return; }
        if (w.WindowState == FormWindowState.Minimized) w.WindowState = FormWindowState.Normal;
        w.Unpark();
        w.Activate();
    }

    public static void StopMusic()
    {
        var w = MusicWin;
        if (w == null) return;
        w.reallyClose = true;
        w.Close();
    }

    async Task PollSoon() { await Task.Delay(300); await PollMusic(); }

    async Task PollMusic()
    {
        var cw = web.CoreWebView2;
        if (cw == null || IsDisposed) return;
        try
        {
            var r = await cw.ExecuteScriptAsync($"window.mbState?window.mbState({(settings.MusicLyrics ? "true" : "false")}):null");
            if (string.IsNullOrEmpty(r) || r == "null") return;
            using var doc = System.Text.Json.JsonDocument.Parse(r);
            var js = doc.RootElement;
            string t = js.GetProperty("t").GetString() ?? "", s = js.GetProperty("s").GetString() ?? "";
            string time = js.GetProperty("time").GetString() ?? "";
            bool playing = js.GetProperty("hasA").GetBoolean()
                ? js.GetProperty("playing").GetBoolean()
                : (lastTime != "" && time != lastTime); // 오디오를 못 잡으면 시간이 흐르는지로 판단
            lastTime = time;
            if (t == "" && !yt) return; // 유튜브 뮤직은 곡이 없어도 칸을 띄운다 (눌러서 창을 열게)
            double vol = js.TryGetProperty("vol", out var ve) && ve.ValueKind == System.Text.Json.JsonValueKind.Number ? ve.GetDouble() : 1;
            string img = js.TryGetProperty("img", out var ie) ? ie.GetString() ?? "" : "";
            bool Flag(string k) => js.TryGetProperty(k, out var f) && f.ValueKind == System.Text.Json.JsonValueKind.True;
            string? lyr = js.TryGetProperty("lyr", out var le) && le.ValueKind == System.Text.Json.JsonValueKind.String ? le.GetString() : null;
            var info = new MusicInfo(t, s, time, playing, vol, img, Flag("rep"), Flag("shuf"), Flag("fx"), yt,
                string.IsNullOrWhiteSpace(lyr) ? null : lyr,
                js.TryGetProperty("cur", out var ce) && ce.ValueKind == System.Text.Json.JsonValueKind.Number ? ce.GetInt32() : -1);
            if (info == lastInfo) return;
            lastInfo = info;
            MusicChanged?.Invoke(info);
        }
        catch (Exception ex) { Log.Write("music", "곡 정보를 읽지 못함", ex); }
    }
}
