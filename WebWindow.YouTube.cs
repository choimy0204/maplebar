using Microsoft.Web.WebView2.Core;
using static MapleBar.Native;

namespace MapleBar;

public partial class WebWindow
{
    // ── 유튜브: 목록 창에서 영상을 누르면 재생 창 하나에서 연다 ──
    // 재생 창은 하나만 쓴다. 다른 영상을 누르면 그 창의 영상만 바뀐다.
    // 로그인은 사용자가 이 창에서 직접 한다 (다른 브라우저의 로그인은 가져오지 않는다). 광고는 막지 않는다.

    // 목록 창: 영상·쇼츠 링크를 누르면 페이지가 열지 않고 메이플 바에 알린다
    const string YtCatchScript =
        "(()=>{const V=/^\\/(watch$|shorts\\/)/;" +
        "addEventListener('click',e=>{if(e.button!==0||e.ctrlKey||e.shiftKey||e.metaKey||e.altKey)return;" +
        "const a=e.composedPath().find(n=>n&&n.tagName==='A'&&n.href);if(!a)return;let u;try{u=new URL(a.href,location.href)}catch(_){return}" +
        "if(!/(^|\\.)youtube\\.com$/.test(u.hostname)||!V.test(u.pathname))return;" +
        "e.preventDefault();e.stopImmediatePropagation();try{chrome.webview.postMessage('mb:yt:'+u.href)}catch(_){}},true)})();";

    // 쇼츠 이전·다음: 페이지에 위·아래 버튼이 보이면 누른다 (창이 좁으면 유튜브가 버튼을 숨긴다)
    static string ShortsButtonScript(int d) =>
        "(()=>{try{const b=document.querySelector('" + (d > 0 ? "#navigation-button-down" : "#navigation-button-up") + " button');" +
        "if(b&&b.offsetParent!==null&&!b.disabled){b.click();return true}}catch(e){}return false})()";

    // 재생 창: 유튜브 위쪽 메뉴·설명·댓글을 가리고 영상(쇼츠도)만 창에 꽉 채운다. 쇼츠 이전·다음은 제목 줄 버튼으로 넘긴다
    // 페이지가 주소만 바꾸며 옮겨 다니므로 주소를 계속 보고 맞춘다
    const string YtVideoOnlyScript = """
        (()=>{const css=`html.mbv,html.mbv body{overflow:hidden!important}
        html.mbv #masthead-container,html.mbv ytd-masthead{display:none!important}
        html.mbv ytd-watch-flexy #secondary,html.mbv ytd-watch-flexy #below,html.mbv ytd-watch-flexy #panels,html.mbv ytd-watch-flexy #chat-container,html.mbv tp-yt-app-drawer#guide,html.mbv ytd-mini-guide-renderer{display:none!important}
        html.mbv #movie_player{position:fixed!important;left:0!important;top:0!important;width:100vw!important;height:100vh!important;z-index:2147483646!important;background:#000!important}
        html.mbv #movie_player .html5-video-container,html.mbv #movie_player video.html5-main-video{width:100%!important;height:100%!important;left:0!important;top:0!important}
        html.mbv #movie_player video.html5-main-video{object-fit:contain!important}
        html.mbv #movie_player .ytp-chrome-bottom{width:calc(100% - 24px)!important;left:12px!important}
        html.mbs,html.mbs body{overflow:hidden!important}
        html.mbs #masthead-container,html.mbs tp-yt-app-drawer#guide,html.mbs ytd-mini-guide-renderer{display:none!important}
        html.mbs #shorts-player{position:fixed!important;left:0!important;top:0!important;width:100vw!important;height:100vh!important;z-index:2147483646!important;background:#000!important;border-radius:0!important}
        html.mbs #shorts-player .html5-video-container,html.mbs #shorts-player video{width:100%!important;height:100%!important;left:0!important;top:0!important}
        html.mbs #shorts-player video{object-fit:contain!important}`;
        const add=()=>{if(document.getElementById('mbVid'))return;const s=document.createElement('style');s.id='mbVid';s.textContent=css;(document.head||document.documentElement).appendChild(s)};
        let last='',navAt=0,userAt=0;const tick=()=>{const r=document.documentElement;if(!r)return;add();const p=location.pathname,w=p==='/watch',s=p.startsWith('/shorts');
        r.classList.toggle('mbv',w);r.classList.toggle('mbs',s);
        if(w||s){const sp=document.getElementById(s?'shorts-player':'movie_player');for(let e=sp&&sp.parentElement;e&&e!==document.body;e=e.parentElement){const t=e.style;t.setProperty('transform','none','important');t.setProperty('filter','none','important');t.setProperty('contain','none','important');t.setProperty('will-change','auto','important');t.setProperty('perspective','none','important')}}
        const k=p+location.search;if(k!==last){last=k;navAt=Date.now();setTimeout(()=>dispatchEvent(new Event('resize')),300);setTimeout(()=>dispatchEvent(new Event('resize')),1500)}};
        tick();document.addEventListener('DOMContentLoaded',tick);addEventListener('yt-navigate-finish',tick);setInterval(tick,700);
        addEventListener('pointerdown',()=>userAt=Date.now(),true);addEventListener('keydown',e=>{if(!/^Arrow/.test(e.key))userAt=Date.now()},true);
        let lt=-1,same=0;setInterval(()=>{if(!location.pathname.startsWith('/shorts'))return;
        const v=document.querySelector('#shorts-player video');if(!v||v.ended){same=0;return}const t=Date.now()-navAt;
        if(v.paused){same=0;if(t>1200&&t<5000&&userAt<navAt)v.play().catch(()=>{});return}
        if(v.readyState>=2&&v.currentTime===lt){if(++same>=3){same=0;try{v.currentTime=v.currentTime+0.05}catch(e){}}}else same=0;lt=v.currentTime},1000)})();
        """;

    // 댓글 창: 영상은 소리 없이 멈춰 두고 댓글만 보이게 한다
    //  쇼츠(mbcs): 쇼츠 페이지의 댓글 패널을 펼쳐 창에 꽉 채운다
    //  일반 영상(mbcw): 영상 페이지에서 위쪽 메뉴·영상·설명·추천 영상을 가린다
    const string YtCommentsScript = """
        (()=>{const W='html.mbcw ';const css=[`#masthead-container`,`ytd-masthead`,`#player`,`#player-container-outer`,`#player-full-bleed-container`,`#full-bleed-container`,`ytd-watch-metadata`,`#secondary`,`#related`,`ytd-watch-next-secondary-results-renderer`,`#chat-container`,`#panels`,`ytd-merch-shelf-renderer`,`#ticket-shelf`,`#clarify-box`,`ytd-playlist-panel-renderer`].map(x=>W+x).join(',')+`{display:none!important}
        html.mbcw #page-manager{margin-top:0!important}
        html.mbcw ytd-watch-flexy #columns{max-width:none!important;margin:0!important;padding:0!important}
        html.mbcw ytd-watch-flexy #primary{margin:0!important;padding:4px 12px 12px!important;max-width:none!important;min-width:0!important}
        html.mbcw ytd-watch-flexy #primary-inner{max-width:none!important}
        html.mbcs,html.mbcs body{overflow:hidden!important}
        html.mbcs #masthead-container,html.mbcs tp-yt-app-drawer#guide,html.mbcs ytd-mini-guide-renderer{display:none!important}
        html.mbcs #shorts-inner-container{overflow:hidden!important}
        html.mbcs ytd-app,html.mbcs ytd-shorts,html.mbcs video{visibility:hidden!important}
        html.mbcs:not(.mbok)::after{content:'댓글 불러오는 중…';position:fixed;inset:0;background:#fff;z-index:2147483647;display:flex;align-items:center;justify-content:center;color:#888;font:14px sans-serif}
        html.mbcs ytd-engagement-panel-section-list-renderer[target-id*="comment"]{visibility:visible!important;position:fixed!important;left:0!important;top:0!important;right:auto!important;bottom:auto!important;width:100vw!important;max-width:none!important;min-width:0!important;height:100vh!important;max-height:none!important;z-index:2147483646!important;border-radius:0!important;margin:0!important;background:#fff!important;transform:none!important}`;
        const add=()=>{const r=document.documentElement;if(!r)return false;if(!document.getElementById('mbCm')){const s=document.createElement('style');s.id='mbCm';s.textContent=css;(document.head||r).appendChild(s)}
        const p=location.pathname;r.classList.toggle('mbcs',p.startsWith('/shorts'));r.classList.toggle('mbcw',p==='/watch');return true};
        const stop=()=>document.querySelectorAll('video').forEach(v=>{try{v.muted=true;if(!v.paused)v.pause()}catch(e){}});
        document.addEventListener('play',e=>{try{e.target.muted=true;e.target.pause()}catch(_){}},true);
        addEventListener('keydown',e=>{if(/^Arrow(Up|Down)$/.test(e.key)&&!/INPUT|TEXTAREA/.test((e.target&&e.target.tagName)||'')&&!(e.target&&e.target.isContentEditable))e.stopImmediatePropagation()},true);
        let clickAt=0,tries=0,page='';const panel=()=>{const path=location.pathname;if(!path.startsWith('/shorts/'))return;if(path!==page){page=path;tries=0;document.documentElement.classList.remove('mbok')}
        const p=document.querySelector('ytd-engagement-panel-section-list-renderer[target-id*="comment"]');
        const open=p&&((p.getAttribute('visibility')||'').includes('EXPANDED')||(p.offsetHeight>80&&getComputedStyle(p).display!=='none'));
        document.documentElement.classList.toggle('mbok',!!open);
        if(open){for(let e=p.parentElement;e&&e!==document.body;e=e.parentElement){const s=e.style;s.setProperty('transform','none','important');s.setProperty('filter','none','important');s.setProperty('contain','none','important');s.setProperty('will-change','auto','important')}return}
        if(Date.now()-clickAt<2500||tries>=6)return;
        const reel=document.querySelector('ytd-reel-video-renderer[is-active]')||document;
        let b=reel.querySelector('#comments-button button');
        if(!b)b=[...reel.querySelectorAll('button')].find(x=>/댓글|comment/i.test(x.getAttribute('aria-label')||''));
        if(b){clickAt=Date.now();tries++;b.click()}};
        add();document.addEventListener('DOMContentLoaded',add);setInterval(()=>{add();stop();panel()},500)})();
        """;

    static WebWindow? CommentsWindow() => open.FirstOrDefault(x => !x.IsDisposed && x.kind == SiteKind.YouTubeComments);

    // 일반 영상(쇼츠 아님): 제목 줄을 숨기고 영상만 보인다. 마우스를 창 위쪽에 올리면 제목 줄이 영상 위에 잠깐 나온다
    readonly System.Windows.Forms.Timer bareTimer = new() { Interval = 150 };
    bool bareMode;
    void StartBare()
    {
        bareTimer.Tick += (_, _) => BareTick();
        bareTimer.Start();
        FormClosed += (_, _) => bareTimer.Dispose();
    }

    void BareTick()
    {
        if (IsDisposed) return;
        bool bare = true; // 영상·쇼츠 모두 영상만 보인다
        if (bare != bareMode)
        {
            bareMode = bare;
            SuspendLayout();
            if (bare) { grip.Dock = DockStyle.None; grip.Visible = false; BackColor = Color.Black; }
            else { grip.Dock = DockStyle.Top; grip.Visible = true; BackColor = Color.White; }
            ResumeLayout();
        }
        if (!bare) return;
        var r = RectangleToScreen(ClientRectangle);
        var c = Cursor.Position;
        bool show = r.Contains(c) && c.Y < r.Top + S(56) || grip.ContextMenuStrip?.Visible == true;
        if (show == grip.Visible) return;
        if (show)
        {
            grip.Bounds = new Rectangle(Padding.Left, Padding.Top, ClientSize.Width - Padding.Horizontal, S(32));
            grip.BringToFront();
        }
        grip.Visible = show;
    }

    /// <summary>재생 창 제목 줄에 '댓글' 버튼을 단다. 누르면 댓글 창을 열고, 다시 누르면 닫는다.</summary>
    void AddCommentsButton()
    {
        // 제목 글자가 버튼 밑으로 들어가지 않게 흰 상자에 담는다
        var box = new Panel { Dock = DockStyle.Right, Width = S(46), BackColor = Color.White };
        var b = new RoundLabel
        {
            Dock = DockStyle.Fill, AutoSize = false, Text = "댓글", TextAlign = ContentAlignment.MiddleCenter,
            Cursor = Cursors.Hand, UseMnemonic = false, ForeColor = UI.Accent, Fill = UI.SoftAccent, Radius = S(6),
            Font = new Font("맑은 고딕", 8.5f, FontStyle.Bold),
        };
        b.MouseEnter += (_, _) => b.Fill = Color.FromArgb(255, 226, 196);
        b.MouseLeave += (_, _) => b.Fill = UI.SoftAccent;
        b.Click += (_, _) => ToggleComments();
        new ToolTip().SetToolTip(b, "댓글 창 열기·닫기");
        box.Controls.Add(b);
        var gap = new Panel { Dock = DockStyle.Right, Width = S(8), BackColor = Color.Transparent };
        grip.Controls.Add(gap); gap.BringToFront();
        grip.Controls.Add(box); box.BringToFront();
        // 재생 창을 닫으면 댓글 창도 닫는다
        FormClosed += (_, _) => { if (CommentsWindow() is { } c) { c.keepPlayer = true; c.Close(); } };
    }

    bool keepPlayer; // 댓글 버튼으로 댓글 창만 닫을 때는 재생 창을 남긴다

    void ToggleComments()
    {
        var c = CommentsWindow();
        if (c != null) { c.keepPlayer = true; c.Close(); return; }
        var src = web.Source?.ToString() ?? startUrl;
        var id = YouTube.VideoId(src);
        if (id == null) { src = startUrl; id = YouTube.VideoId(src); }
        if (id == null) return; // 아직 영상이 안 열렸다
        var w = new WebWindow(YouTube.CommentsUrl(id, YouTube.IsShorts(src)), settings, sc);
        // 재생 창 왼쪽에 같은 높이로 붙인다 (자리가 없으면 오른쪽)
        var wa = Screen.FromControl(this).WorkingArea;
        int cw = Math.Min((int)Math.Round(400 * Math.Max(sc, DeviceDpi / 96f * 0.9f)), wa.Width);
        int x = Left - cw;
        if (x < wa.Left) x = Math.Max(wa.Left, Math.Min(Right, wa.Right - cw));
        int h = Math.Min(Height, wa.Height);
        int y = Math.Max(wa.Top, Math.Min(Top, wa.Bottom - h));
        w.Bounds = new Rectangle(x, y, cw, h);
        // 댓글 창을 ✕로 닫으면 재생 창도 같이 닫는다
        w.FormClosed += (_, _) => { if (!w.keepPlayer && !IsDisposed) BeginInvoke(Close); };
        w.Show();
    }

    /// <summary>재생 창의 영상이 바뀌면 댓글 창도 그 영상의 댓글로 바꾼다.</summary>
    void SyncComments()
    {
        var c = CommentsWindow();
        if (c == null || IsDisposed) return;
        var src = web.Source?.ToString();
        var id = YouTube.VideoId(src);
        if (id == null) return;
        var url = YouTube.CommentsUrl(id, YouTube.IsShorts(src));
        if (url == c.startUrl) return;
        c.startUrl = url; // 아직 준비 중이면 InitAsync가 이 주소로 연다
        try { c.web.CoreWebView2?.Navigate(url); } catch { }
    }

    /// <summary>재생 창 크기: 일반 영상은 가로 16:9, 쇼츠는 세로 9:16에 맞춘다.</summary>
    Size PlayerSize(bool shorts, Rectangle wa)
    {
        float ws = Math.Max(sc, DeviceDpi / 96f * 0.9f);
        int vw = (int)Math.Round((shorts ? 340 : 480) * ws);
        int vh = shorts ? vw * 16 / 9 : vw * 9 / 16;
        return new Size(Math.Min(vw + edge * 2, wa.Width), Math.Min(vh + S(32) + edge * 2, wa.Height));
    }

    // ── 쇼츠 다음·이전 키 (웹툰 키처럼 키 상태만 읽는다. 게임에는 아무것도 보내지 않는다) ──
    // 재생 창이 앞에 있거나 메이플이 앞에 있을 때, 재생 창에 쇼츠가 떠 있으면 한 번 누를 때마다 한 칸 넘긴다
    static readonly System.Windows.Forms.Timer shortsTimer = CreateShortsTimer();
    static bool shortsNextWas = true, shortsPrevWas = true; // 켜자마자 눌려 있던 키는 무시한다
    static long shortsMovedAt;

    static System.Windows.Forms.Timer CreateShortsTimer()
    {
        var t = new System.Windows.Forms.Timer { Interval = 30 };
        t.Tick += (_, _) => ShortsTick();
        return t;
    }

    static void ShortsTick()
    {
        WebWindow? p = null;
        foreach (var x in open) if (!x.IsDisposed && x.kind == SiteKind.YouTubePlayer) { p = x; break; }
        if (p == null) { shortsTimer.Stop(); shortsNextWas = shortsPrevWas = true; return; }
        var s = p.settings;
        bool next = KeyIsDown(s.ShortsNextKey), prev = KeyIsDown(s.ShortsPrevKey);
        bool active = p.Visible && p.WindowState != FormWindowState.Minimized &&
                      (GetForegroundWindow() is var fg && (fg == p.Handle || fg == CommentsWindow()?.Handle) || Game.Foreground()) &&
                      YouTube.IsShorts(p.web.Source?.ToString() ?? p.startUrl);
        // 너무 빨리 연달아 넘기면 영상이 걸리므로 짧은 간격 안의 입력은 한 번으로 친다
        bool ready = Environment.TickCount64 - shortsMovedAt >= 350;
        if (active && ready && next && !shortsNextWas) { p.MoveShorts(1); shortsMovedAt = Environment.TickCount64; }
        else if (active && ready && prev && !shortsPrevWas) { p.MoveShorts(-1); shortsMovedAt = Environment.TickCount64; }
        shortsNextWas = next; shortsPrevWas = prev;
    }

    // 버튼이 없으면 이 재생 창 안에만 ↑·↓ 키를 넣는다 (유튜브가 쇼츠를 넘기는 키. 게임이나 다른 창에는 가지 않는다)
    async void MoveShorts(int d)
    {
        try
        {
            var cw = web.CoreWebView2;
            if (cw == null) return;
            if (await cw.ExecuteScriptAsync(ShortsButtonScript(d)) == "true") return;
            string k = d > 0 ? "ArrowDown" : "ArrowUp"; int vk = d > 0 ? 40 : 38;
            string Key(string type) => "{\"type\":\"" + type + "\",\"key\":\"" + k + "\",\"code\":\"" + k +
                                       "\",\"windowsVirtualKeyCode\":" + vk + ",\"nativeVirtualKeyCode\":" + vk + "}";
            await cw.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent", Key("rawKeyDown"));
            await cw.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent", Key("keyUp"));
        }
        catch { }
    }

    async Task InitYouTube(CoreWebView2 cw)
    {
        if (kind == SiteKind.YouTubePlayer)
        {
            await cw.AddScriptToExecuteOnDocumentCreatedAsync(YtVideoOnlyScript);
            // 쇼츠를 넘기거나 다른 영상을 틀면 댓글 창도 따라 바꾼다
            cw.HistoryChanged += (_, _) => SyncComments();
            cw.SourceChanged += (_, _) => SyncComments();
            shortsTimer.Start();
            return;
        }
        if (kind == SiteKind.YouTubeComments)
        {
            cw.IsMuted = true; // 댓글 창에서는 소리가 나지 않게
            await cw.AddScriptToExecuteOnDocumentCreatedAsync(YtCommentsScript);
        }
        await cw.AddScriptToExecuteOnDocumentCreatedAsync(YtCatchScript);
        cw.WebMessageReceived += (_, e) =>
        {
            string? m = null; try { m = e.TryGetWebMessageAsString(); } catch { }
            if (m == null || !m.StartsWith("mb:yt:", StringComparison.Ordinal)) return;
            var u = m[6..];
            if (YouTube.IsVideo(u) && !IsDisposed) BeginInvoke(() => ShowYouTube(u, settings, sc));
        };
        // 놓친 경우(뒤로 가기, 페이지가 직접 옮김 등): 목록 창이 영상 페이지로 가면 재생 창으로 넘기고 목록으로 돌아온다
        if (kind == SiteKind.YouTubeComments) return;
        cw.HistoryChanged += (_, _) =>
        {
            var s = cw.Source;
            if (!YouTube.IsVideo(s)) return;
            try { _ = cw.ExecuteScriptAsync("document.querySelectorAll('video').forEach(v=>{try{v.pause()}catch(e){}})"); } catch { }
            ShowYouTube(s, settings, sc);
            if (cw.CanGoBack) cw.GoBack(); else cw.Navigate(YouTube.Home);
        };
    }

    /// <summary>유튜브 재생 창에서 url을 연다. 창이 있으면 영상만 바꾸고, 없으면 새로 띄운다.</summary>
    public static void ShowYouTube(string url, Settings st, float scale)
    {
        var w = open.FirstOrDefault(x => !x.IsDisposed && x.kind == SiteKind.YouTubePlayer);
        if (w == null) { new WebWindow(url, st, scale).Show(); return; }
        // 일반 영상 ↔ 쇼츠로 바뀌면 창 모양도 바꾼다 (펼친 상태·최대화면 그대로 둔다)
        if (YouTube.IsShorts(url) != YouTube.IsShorts(w.startUrl) && w.smallBounds == null && w.WindowState == FormWindowState.Normal)
        {
            var wa = Screen.FromControl(w).WorkingArea;
            var sz = w.PlayerSize(YouTube.IsShorts(url), wa);
            int x = Math.Max(wa.Left, Math.Min(w.Right - sz.Width, wa.Right - sz.Width)); // 오른쪽 끝을 맞춘다
            int y = Math.Max(wa.Top, Math.Min(w.Top, wa.Bottom - sz.Height));
            w.Bounds = new Rectangle(x, y, sz.Width, sz.Height);
        }
        w.startUrl = url; // 아직 준비 중이면 InitAsync가 이 주소로 연다
        try { w.web.CoreWebView2?.Navigate(url); } catch { }
        if (w.WindowState == FormWindowState.Minimized) w.WindowState = FormWindowState.Normal;
        w.Activate();
    }
}
