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
        try { return await web.CoreWebView2.ExecuteScriptAsync(js); } catch { return null; }
    }

    public record Song(string Title, string Sub, string Img);

    /// <summary>뒤에 숨은 뮤직 창에서 path 페이지(인기 차트, 검색)의 곡 목록을 읽는다. 노래는 끊기지 않는다.</summary>
    public static async Task<List<Song>> MusicListAsync(Settings st, float scale, string path, string? query = null)
    {
        var w = EnsureMusic(st, scale, Music.Home.Replace("/music", "") + path, false);
        string p = System.Text.Json.JsonSerializer.Serialize(path);
        // 페이지가 준비될 때까지 기다리며 옮긴다
        for (int i = 0; i < 60; i++)
        {
            var r = await w.Eval($"window.mbGo?(window.mbGo({p}),window.mbHere()):''");
            if (r == p) break;
            await Task.Delay(250);
        }
        if (query != null)
        {
            for (int i = 0; i < 40; i++)
            {
                if (await w.Eval($"window.mbSearch?window.mbSearch({System.Text.Json.JsonSerializer.Serialize(query)}):false") == "true") break;
                await Task.Delay(250);
            }
            await Task.Delay(900); // 결과가 다시 그려질 때까지
        }
        for (int i = 0; i < 40; i++)
        {
            var json = await w.Eval("window.mbList?JSON.stringify(window.mbList()):'[]'");
            var list = ParseSongs(json);
            if (list.Count > 0 || (query != null && i >= 4)) return list;
            await Task.Delay(250);
        }
        return new();
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
        catch { }
        return list;
    }

    /// <summary>지금 읽어 둔 목록의 i번째 곡을 재생한다.</summary>
    public static async Task MusicPlayAsync(int i)
    {
        var w = MusicWin;
        if (w == null) return;
        await w.Eval($"window.mbPlay&&window.mbPlay({i})");
        // 곡은 바뀌었는데 멈춰 있으면 한 번 더 재생을 건다
        await Task.Delay(1500);
        await w.Eval("(()=>{const a=window.__mbA;if(a&&a.paused&&!a.ended)a.play().catch(()=>{})})()");
        _ = w.PollSoon();
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
        catch { }
    }
}
