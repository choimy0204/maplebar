using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

using static MapleBar.Native;
using static MapleBar.WebScripts;

namespace MapleBar;

public partial class WebWindow
{
    // ── 웹툰: 정한 키를 누르고 있는 동안 부드럽게 스크롤 ──
    // 키 상태만 읽는다(GetAsyncKeyState). 게임에는 아무것도 보내지 않는다.
    // 대상: 웹툰 창에 포커스가 있으면 그 창, 메이플이 앞에 있으면 마지막으로 본 웹툰 창
    static readonly System.Windows.Forms.Timer scrollTimer = CreateScrollTimer();
    // [전역] lastWebtoon: 마지막으로 포커스를 받은 웹툰 창, scrollWin: 지금 스크롤 중인 창, scrollDir: 방향(-1·0·1)
    static WebWindow? lastWebtoon, scrollWin;
    static int scrollDir;
    static bool nextWas = true, prevWas = true; // 켜자마자 눌려 있던 키는 무시한다

    static System.Windows.Forms.Timer CreateScrollTimer()
    {
        var t = new System.Windows.Forms.Timer { Interval = 30 };
        t.Tick += (_, _) => ScrollTick();
        return t;
    }

    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
    static bool KeyIsDown(int vk) => vk != 0 && (GetAsyncKeyState(vk) & 0x8000) != 0;

    static readonly List<WebWindow> scrollWins = new();

    static void ScrollTick()
    {
        // 30ms마다 돌아서 LINQ 대신 그냥 반복문으로 고른다 (고르는 순서와 결과는 같다)
        bool anyToon = false;
        foreach (var x in open) if (!x.IsDisposed && x.kind == SiteKind.Webtoon) { anyToon = true; break; }
        if (!anyToon) { scrollTimer.Stop(); scrollWin = null; scrollDir = 0; return; }
        var wins = scrollWins;
        wins.Clear();
        foreach (var x in open)
            if (!x.IsDisposed && x.Visible && x.WindowState != FormWindowState.Minimized && x.kind == SiteKind.Webtoon) wins.Add(x);
        var fg = GetForegroundWindow();
        WebWindow? target = null;
        foreach (var x in wins) if (x.Handle == fg) { target = x; break; }
        if (target == null && wins.Count > 0 && Game.Foreground())
            target = lastWebtoon != null && wins.Contains(lastWebtoon) ? lastWebtoon : wins[^1];
        int dir = 0;
        if (target != null)
        {
            var s = target.settings;
            dir = (KeyIsDown(s.WebtoonDownKey) ? 1 : 0) - (KeyIsDown(s.WebtoonUpKey) ? 1 : 0);
            // 다음 화/이전 화는 누르는 순간 한 번만
            bool next = KeyIsDown(s.WebtoonNextKey), prev = KeyIsDown(s.WebtoonPrevKey);
            if (next && !nextWas) target.SendEpisode(1);
            if (prev && !prevWas) target.SendEpisode(-1);
            nextWas = next; prevWas = prev;
        }
        else nextWas = prevWas = false;
        if (target == scrollWin && dir == scrollDir) return;
        if (scrollWin != null && scrollWin != target && !scrollWin.IsDisposed) scrollWin.SendScroll(0);
        scrollWin = target; scrollDir = dir;
        target?.SendScroll(dir);
    }

    void SendScroll(int dir)
    {
        try
        {
            var keys = $"[{settings.WebtoonUpKey},{settings.WebtoonDownKey},{settings.WebtoonNextKey},{settings.WebtoonPrevKey}]";
            _ = web.CoreWebView2?.ExecuteScriptAsync($"window.__mbScroll&&window.__mbScroll({dir},{Math.Clamp(settings.WebtoonSpeed, 100, 5000)},{keys})");
        }
        catch { }
    }


    /// <summary>웹툰 창들을 포커스는 그대로 둔 채 맨 위로 올린다.</summary>
    static void RaiseWebtoons()
    {
        foreach (var w in open.ToList())
            if (!w.IsDisposed && w.IsHandleCreated && w.Visible && w.kind == SiteKind.Webtoon)
                SetWindowPos(w.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_KEEP);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (kind != SiteKind.Webtoon) BeginInvoke(RaiseWebtoons);
    }

    void SendEpisode(int d)
    {
        try { _ = web.CoreWebView2?.ExecuteScriptAsync($"window.__mbEp&&window.__mbEp({d})"); } catch { }
    }
}
