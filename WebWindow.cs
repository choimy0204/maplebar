using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

using static MapleBar.Native;
using static MapleBar.WebScripts;

namespace MapleBar;

/// <summary>
/// 링크 하나를 여는 독립된 창. 윈도우 제목줄 없이 얇은 손잡이만 있고,
/// 손잡이를 끌면 옮겨지고 가장자리를 끌면 크기가 바뀐다.
/// </summary>
public partial class WebWindow : Form
{
    // [전역] 모든 창이 같은 WebView2 환경(쿠키·로그인·사람 확인)을 함께 쓴다. 만들기에 실패하면 null로 되돌려 다음 창에서 다시 만든다
    static Task<CoreWebView2Environment>? sharedEnv;
    static Task<CoreWebView2Environment> Env =>
        sharedEnv ??= CoreWebView2Environment.CreateAsync(null, Settings.WebDataDir,
            // 바에서 고른 곡이 숨은 창에서 바로 재생되게 한다
            new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required" + (Environment.GetEnvironmentVariable("MB_DEBUG") == "1" ? " --remote-debugging-port=9333" : "")));

    // [전역] 지금 열려 있는 사이트 창 전부. 생성자에서 넣고 닫힐 때 뺀다 (UI 스레드에서만 쓴다)
    static readonly List<WebWindow> open = new();

    readonly WebView2 web = new() { Dock = DockStyle.Fill };
    readonly Settings settings;
    readonly TitleStrip grip;
    // 어떤 사이트 창인지: 주소를 바꿀 때마다 한 번 정해 두고, 매번 주소를 다시 풀지 않는다
    enum SiteKind { Other, Webtoon, Music, Coordi, YouTube, YouTubePlayer, YouTubeComments }
    string startUrlValue = "";
    SiteKind kind;
    /// <summary>창이 연(또는 이어서 열) 주소. 바꾸면 kind도 같이 바뀐다.</summary>
    string startUrl { get => startUrlValue; set { startUrlValue = value; kind = KindOf(value); } }
    static SiteKind KindOf(string url) =>
        Webtoon.IsWebtoon(url) ? SiteKind.Webtoon :
        YouTube.IsComments(url) ? SiteKind.YouTubeComments :
        YouTube.IsVideo(url) ? SiteKind.YouTubePlayer : YouTube.IsYouTube(url) ? SiteKind.YouTube :
        Coordi.IsCoordi(url) ? SiteKind.Coordi : Music.IsMusic(url) ? SiteKind.Music : SiteKind.Other;

    /// <summary>"아델 | 캐릭터정보 - 환산주스탯"을 닉네임·페이지로 나눠 그리는 제목 줄.</summary>
    sealed class TitleStrip : Panel
    {
        readonly WebWindow owner;
        readonly Font bold = new("맑은 고딕", 9.5f, FontStyle.Bold);
        readonly Font normal = new("맑은 고딕", 9f);
        public TitleStrip(WebWindow owner)
        {
            this.owner = owner;
            DoubleBuffered = true; ResizeRedraw = true; BackColor = Color.White;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            int cy = Height / 2, d = owner.S(8), x = owner.S(6);
            using (var b = new SolidBrush(UI.Accent)) g.FillEllipse(b, x, cy - d / 2f, d, d);
            x += d + owner.S(8);

            var t = owner.Text;
            int dash = t.LastIndexOf(" - 환산주스탯", StringComparison.Ordinal);
            if (dash > 0) t = t[..dash];
            string main = t, sub = "";
            int bar = t.IndexOf(" | ", StringComparison.Ordinal);
            if (bar > 0) { main = t[..bar]; sub = t[(bar + 3)..]; }

            // 오른쪽 버튼·슬라이드 바 앞에서 글자를 끊는다
            int right = Width - Padding.Right - owner.S(30);
            foreach (Control c in Controls) if (c.Visible && c.Width > 0 && c.BackColor != Color.Transparent) right = Math.Min(right, c.Left - owner.S(6));
            // 페이지 부가 설명(예: '메이플 코디·믹스 염색…')은 창을 펼쳤을 때만 보인다
            if (!owner.Expanded) sub = "";
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
            var r1 = new Rectangle(x, 0, Math.Max(0, right - x), Height);
            TextRenderer.DrawText(g, main, bold, r1, UI.Text, flags);
            if (sub.Length == 0) return;
            x += TextRenderer.MeasureText(g, main, bold, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width + owner.S(8);
            if (x < right)
                TextRenderer.DrawText(g, sub, normal, new Rectangle(x, 0, right - x, Height), Color.FromArgb(140, 140, 140), flags);
        }
    }
    readonly float sc;
    readonly int edge; // 크기 조절용 테두리 두께

    int S(int v) => UI.Scale(v, sc);

    public WebWindow(string url, Settings settings, float scale)
    {
        this.settings = settings;
        sc = scale;
        edge = S(5);
        Text = "메이플 바";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = !Music.IsMusic(url); // 뮤직 창은 화면 밖에 있으므로 작업 표시줄에 안 보이게
        StartPosition = FormStartPosition.Manual;
        MinimumSize = new Size(S(300), S(240));
        BackColor = Color.White; // 테두리(크기 조절 영역)가 따로 보이지 않게 흰색
        Padding = new Padding(edge);
        TopMost = settings.WinTopMost || Webtoon.IsWebtoon(url); // 웹툰은 늘 맨 위
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        // 위쪽 제목 줄: 주황 점 + 닉네임(굵게) + 페이지 이름(회색), 오른쪽에 주황 동그라미 ✕
        grip = new TitleStrip(this) { Dock = DockStyle.Top, Height = S(32), Padding = new Padding(0, S(5), S(2), S(5)) };
        var close = new RoundLabel
        {
            Dock = DockStyle.Right, AutoSize = false, Width = S(22), Text = "", Cursor = Cursors.Hand,
            ForeColor = Color.White, Fill = UI.Accent, Radius = S(6), UseMnemonic = false,
        };
        // 글자 대신 직접 그려서 정확히 가운데에 둔다
        close.Paint += (_, e) =>
        {
            var g = e.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            float cx = close.Width / 2f, cy = close.Height / 2f, d = S(4);
            using var p = new Pen(Color.White, Math.Max(1.6f, S(2) * 0.85f)) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
            g.DrawLine(p, cx - d, cy - d, cx + d, cy + d);
            g.DrawLine(p, cx - d, cy + d, cx + d, cy - d);
        };
        close.MouseEnter += (_, _) => close.Fill = ControlPaint.Dark(UI.Accent, 0.05f);
        close.MouseLeave += (_, _) => close.Fill = UI.Accent;
        close.Click += (_, _) => Close();
        grip.Controls.Add(close);
        // ✕ 왼쪽 펼치기 버튼: 누르면 크게(BigSize), 다시 누르면 원래 크기로
        var gap = new Panel { Dock = DockStyle.Right, Width = S(6), BackColor = Color.Transparent };
        grip.Controls.Add(gap); gap.BringToFront();
        var grow = new RoundLabel
        {
            Dock = DockStyle.Right, AutoSize = false, Width = S(22), Text = "", Cursor = Cursors.Hand,
            ForeColor = UI.Accent, Fill = UI.SoftAccent, Radius = S(6), UseMnemonic = false,
        };
        // 작을 때: 큰 네모(펼치기) / 클 때: 작은 네모(접기)
        grow.Paint += (_, e) =>
        {
            var g = e.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            float cx = grow.Width / 2f, cy = grow.Height / 2f, d = smallBounds != null ? S(3) : S(5);
            using var p = new Pen(UI.Accent, Math.Max(1.5f, S(2) * 0.8f));
            using var box = UI.RoundRect(new RectangleF(cx - d, cy - d, d * 2, d * 2), S(2));
            g.DrawPath(p, box);
        };
        var growTip = new ToolTip();
        growTip.SetToolTip(grow, "크게 보기");
        grow.MouseEnter += (_, _) => grow.Fill = Color.FromArgb(255, 226, 196);
        grow.MouseLeave += (_, _) => grow.Fill = UI.SoftAccent;
        grow.Click += (_, _) =>
        {
            ToggleBig();
            grow.Invalidate(); grip.Invalidate();
            growTip.SetToolTip(grow, smallBounds != null ? "원래 크기로" : "크게 보기");
        };
        grip.Controls.Add(grow); grow.BringToFront();
        // 펼치기 버튼 왼쪽: 투명도 슬라이드 바
        var gap2 = new Panel { Dock = DockStyle.Right, Width = S(8), BackColor = Color.Transparent };
        grip.Controls.Add(gap2); gap2.BringToFront();
        alphaBar = new AlphaSlider { Dock = DockStyle.Right, Width = S(70), Value = Music.IsMusic(url) ? 100 : Math.Clamp(settings.WinOpacity, AlphaSlider.Min, 100) };
        alphaBar.Changed += v => SetAlpha(v);
        new ToolTip().SetToolTip(alphaBar, "투명도");
        grip.Controls.Add(alphaBar); alphaBar.BringToFront();
        if (KindOf(url) == SiteKind.YouTubePlayer) { AddCommentsButton(); StartBare(); }
        if (Music.IsMusic(url))
        {
            // 뮤직 창: 곡 정보를 1초마다 읽어 바에 알린다
            musicTimer.Tick += async (_, _) => await PollMusic();
            musicTimer.Start();
        }

        var menu = UI.Rounded(new ContextMenuStrip { Font = new Font("맑은 고딕", 9.5f * UI.Z), ShowImageMargin = false });
        menu.Items.Add("새로고침 (F5)", null, (_, _) => web.CoreWebView2?.Reload());
        menu.Items.Add("뒤로 (Alt+←)", null, (_, _) => { if (web.CanGoBack) web.GoBack(); });
        menu.Items.Add("브라우저로 열기", null, (_, _) => { if (web.Source != null) OpenExternal(web.Source.ToString()); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("닫기 (Ctrl+W)", null, (_, _) => Close());
        // 투명도: 제목 줄의 슬라이드 바(또는 제목 줄에서 휠). 마지막 값은 새 창에도 쓴다
        grip.MouseWheel += (_, e) => SetAlpha((int)Math.Round(Opacity * 100) + (e.Delta > 0 ? 5 : -5));

        grip.ContextMenuStrip = menu;
        grip.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left && e.Clicks == 1) DragMove(); };
        grip.DoubleClick += (_, _) => ToggleMax();

        Controls.Add(web);
        Controls.Add(grip);
        web.BringToFront();

        Bounds = NextBounds(url);
        if (!Music.IsMusic(url)) Opacity = Math.Clamp(settings.WinOpacity, 30, 100) / 100.0;
        startUrl = url;
        open.Add(this);
        _ = InitAsync(url);
        if (Webtoon.IsWebtoon(url)) scrollTimer.Start();
    }

    /// <summary>작은 창(약 420×560)으로, 이미 열린 창과 겹치지 않게 조금씩 비켜서 연다.</summary>
    Rectangle NextBounds(string url)
    {
        // 바가 있는 화면에 연다
        var bar = Application.OpenForms.OfType<BarForm>().FirstOrDefault();
        var wa = (bar != null ? Screen.FromControl(bar) : Screen.FromPoint(Cursor.Position)).WorkingArea;
        // 웹툰은 세로로 길게 본다: 폭은 조금 넓게, 높이는 화면 거의 전부
        bool tall = Webtoon.IsWebtoon(url);
        // 사이트 글자는 해상도와 상관없이 같은 크기로 나오므로, 창은 90% 아래로는 줄이지 않는다 (좁으면 내용이 답답하다)
        float ws = Math.Max(sc, DeviceDpi / 96f * 0.9f);
        int W(int v) => (int)Math.Round(v * ws);
        int w = Math.Min(W(tall ? 460 : 420), wa.Width);
        int h = tall ? Math.Max(Math.Min(W(560), wa.Height), wa.Height - S(40)) : Math.Min(W(560), wa.Height);
        if (KindOf(url) == SiteKind.YouTubePlayer) { var ps = PlayerSize(YouTube.IsShorts(url), wa); w = ps.Width; h = ps.Height; }

        Point p;
        if (open.Count > 0)
        {
            var last = open[^1].Bounds;
            p = new Point(last.Left - S(30), last.Top + S(30));
        }
        else if (settings.WinX is int x && settings.WinY is int y)
            p = new Point(x, y);
        else
            p = new Point(wa.Right - w - S(40), wa.Top + S(80));

        // 화면 밖으로 나가지 않게
        if (p.X < wa.Left || p.Y + h > wa.Bottom) p = new Point(wa.Right - w - S(40), wa.Top + S(80));
        p = new Point(Math.Max(wa.Left, Math.Min(p.X, wa.Right - w)), Math.Max(wa.Top, Math.Min(p.Y, wa.Bottom - h)));
        return new Rectangle(p, new Size(w, h));
    }

    async Task InitAsync(string url)
    {
        try
        {
            var env = await Env;
            await web.EnsureCoreWebView2Async(env);
            var cw = web.CoreWebView2;
            // 페이지(또는 그 안의 iframe)에 포커스가 있을 때도 브라우저 단계에서 Ctrl+W를 받는다
            try
            {
                var ctl = typeof(Microsoft.Web.WebView2.WinForms.WebView2)
                    .GetField("_coreWebView2Controller", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    ?.GetValue(web) as Microsoft.Web.WebView2.Core.CoreWebView2Controller;
                if (ctl != null)
                    ctl.AcceleratorKeyPressed += (_, e) =>
                    {
                        if (e.KeyEventKind == Microsoft.Web.WebView2.Core.CoreWebView2KeyEventKind.KeyDown &&
                            e.VirtualKey == (uint)Keys.W && (ModifierKeys & (Keys.Control | Keys.Alt | Keys.Shift)) == Keys.Control)
                        {
                            e.Handled = true;
                            if (!IsDisposed) BeginInvoke(() => { if (!IsDisposed) Close(); });
                        }
                    };
            }
            catch { }
            // 페이지 안에 포커스가 있어도 Ctrl+W로 창을 닫는다 (한글 입력 중에도 되도록 키 위치로 본다)
            await cw.AddScriptToExecuteOnDocumentCreatedAsync(
                "addEventListener('keydown',e=>{if(e.ctrlKey&&!e.altKey&&!e.shiftKey&&e.code==='KeyW'){e.preventDefault();e.stopImmediatePropagation();" +
                "try{chrome.webview.postMessage('mb:close')}catch(_){}}},true);" +
                // Esc도 창을 닫는다. 페이지가 먼저 쓴 Esc(팝업 닫기 등)는 건드리지 않는다
                "addEventListener('keydown',e=>{if(e.key==='Escape'&&!e.defaultPrevented&&!e.isComposing&&!document.fullscreenElement){" +
                "try{chrome.webview.postMessage('mb:close')}catch(_){}}});");
            cw.WebMessageReceived += (_, e) =>
            {
                string? m = null; try { m = e.TryGetWebMessageAsString(); } catch { }
                if (m == "mb:close" && !IsDisposed) BeginInvoke(() => { if (!IsDisposed) Close(); });
            };
            // 숨은 뮤직 창은 화면에 안 보이니 광고를 건드리지 않는다
            if (kind == SiteKind.Webtoon) await cw.AddScriptToExecuteOnDocumentCreatedAsync(ScrollScript);
            else if (kind == SiteKind.Coordi) // 코디 창: 광고(사용자 요청)와 아래쪽 뮤직 플레이어 바를 숨긴다 (노래는 바의 뮤직이 맡는다)
            {
                await BlockAds(cw, env);
                await cw.AddScriptToExecuteOnDocumentCreatedAsync(
                    "(()=>{const add=()=>{if(document.getElementById('mbNoPlayer'))return;const s=document.createElement('style');s.id='mbNoPlayer';" +
                    "s.textContent='div.fixed.left-0.bottom-0.w-full{display:none!important}';(document.head||document.documentElement).appendChild(s)};" +
                    "if(document.documentElement)add();document.addEventListener('DOMContentLoaded',add);" +
                    // '새 드레스룸 v2도 써 보세요!' 안내 상자: 그 글과 'v2 써보기' 버튼을 함께 감싼 가장 작은 상자를 숨긴다
                    // (글이 들어 있는 가장 안쪽 요소에서 시작하고, 글이 400자 넘는 큰 상자는 건드리지 않는다)
                    "const R=/새\\s*드레스룸\\s*v2도\\s*써\\s*보세요/;" +
                    "const promo=()=>{try{for(const el of document.querySelectorAll('h1,h2,h3,h4,p,span,div,strong,b')){" +
                    "if(el.dataset.mbPromo||!R.test(el.textContent||'')||[...el.children].some(c=>R.test(c.textContent||'')))continue;let n=el;" +
                    "for(let i=0;i<8&&n&&n!==document.body&&(n.textContent||'').length<400;i++,n=n.parentElement){if(/v2\\s*써보기/.test(n.textContent||'')){" +
                    "n.style.setProperty('display','none','important');el.dataset.mbPromo='1';break}}}}catch(e){}};" +
                    "setInterval(promo,700)})();");
            }
            else if (kind is SiteKind.YouTube or SiteKind.YouTubePlayer or SiteKind.YouTubeComments) await InitYouTube(cw); // 유튜브는 광고를 막지 않는다
            else if (kind != SiteKind.Music) await BlockAds(cw, env); // 숨김 스크립트 등록이 끝난 뒤에 페이지를 연다
            else
            {
                await cw.AddScriptToExecuteOnDocumentCreatedAsync(MusicScript);
                fxScriptId = await cw.AddScriptToExecuteOnDocumentCreatedAsync(FxScript(settings.MusicFx));
            }
            // 인벤은 작은 창에 맞는 모바일 화면으로 보이게 한다
            if (Inven.IsInven(startUrl) || kind == SiteKind.Webtoon)
                cw.Settings.UserAgent = Inven.MobileUA;
            // 새 창 링크: 사이트 안이면 또 새 창으로, 바깥이면 기본 브라우저로
            cw.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                // 화면 밖에 숨은 뮤직 창은 사용자가 누를 수 없으니, 페이지가 스스로 연 새 창이다.
                // 메애기 창을 화면에 꺼내거나 다른 창을 띄우지 않는다 (메애기 주소면 숨은 채로 그 안에서 옮긴다)
                if (parked)
                {
                    if (Music.IsMusic(e.Uri) && Uri.TryCreate(e.Uri, UriKind.Absolute, out var pu))
                        _ = cw.ExecuteScriptAsync($"window.mbGo&&window.mbGo({System.Text.Json.JsonSerializer.Serialize(pu.AbsolutePath.TrimEnd('/'))})");
                    return;
                }
                if (Music.IsMusic(e.Uri) && ShowMusic(e.Uri)) return;
                if (YouTube.IsVideo(e.Uri)) { ShowYouTube(e.Uri, settings, sc); return; }
                if (IsSite(e.Uri) || Inven.IsInven(e.Uri) || Music.IsMeaegi(e.Uri) || Webtoon.IsNaver(e.Uri) || YouTube.IsYouTube(e.Uri)) new WebWindow(e.Uri, settings, sc).Show();
                else OpenExternal(e.Uri);
            };
            cw.DocumentTitleChanged += (_, _) => { Text = cw.DocumentTitle; grip.Invalidate(); };
            cw.Navigate(startUrl);
        }
        catch (Exception ex)
        {
            sharedEnv = null;
            MessageBox.Show(this, "사이트 화면을 띄우지 못했어요.\nMicrosoft Edge WebView2 런타임이 필요해요.\n\n" + ex.Message,
                "메이플 바", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // 광고 서버 요청을 막고, 남는 광고 칸은 숨긴다
    static readonly string[] AdHosts =
    {
        "googlesyndication.com", "doubleclick.net", "googleadservices.com", "adservice.google.",
        "adtrafficquality.google", "fundingchoicesmessages.google.com", "amazon-adsystem.com",
        "criteo.com", "taboola.com", "outbrain.com", "adnxs.com", "kakaoad", "adfit",
        "mobon.net", "dable.io",
        // 이 사이트가 쓰는 광고 회사(Venatus)와 연결된 곳들
        "vntsm.com", "vntsm.io", "venatusmedia.com", "rapidedge.io", "rubiconproject.com",
        "affec.tv", "hyth.io", "script.ac", "ad-delivery.net", "clean.gg", "ccgateway.net",
        "pubmatic.com", "openx.net", "adsrvr.org", "casalemedia.com", "33across.com",
        "sharethrough.com", "smartadserver.com", "teads.tv", "media.net", "id5-sync.com",
        // 인벤 자체 광고 서버
        "zicf.inven.co.kr", "ads-partners.coupang.com", "coupangad",
    };

    // 사이트가 직접 올리는 배너 이미지 폴더
    static bool IsAdPath(Uri u) =>
        u.Host.EndsWith("maplescouter.com") && u.AbsolutePath.Contains("/ads/", StringComparison.OrdinalIgnoreCase);

    static async Task BlockAds(CoreWebView2 cw, CoreWebView2Environment env)
    {
        cw.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        cw.WebResourceRequested += (_, e) =>
        {
            if (!Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var u)) return;
            var host = u.Host;
            if (IsAdPath(u) || AdHosts.Any(h => host.Contains(h, StringComparison.OrdinalIgnoreCase)))
                e.Response = env.CreateWebResourceResponse(null, 403, "Blocked", "");
        };
        // 사이트가 화면을 다시 그리며 head를 바꿔도 숨김 스타일이 남도록 계속 확인한다
        await cw.AddScriptToExecuteOnDocumentCreatedAsync(
            "(()=>{const mp=location.hostname.endsWith('maplescouter.com');" +
            "const add=()=>{try{if(document.getElementById('mb-noad'))return;" +
            "const s=document.createElement('style');s.id='mb-noad';s.textContent=mp?\"" + AdCss + MapleOnlyCss + HideRule + "\":\"" + AdCss + HideRule + "\";" +
            "(document.head||document.documentElement).appendChild(s)}catch(x){}};" +
            "add();document.addEventListener('DOMContentLoaded',add);setInterval(add,700);" +
            // 화면 구석에 떠 있는 둥근 'TOP'(맨 위로) 버튼을 숨긴다: 글자가 TOP뿐이고 고정 위치인 작은 요소
            "const top=()=>{try{document.querySelectorAll('button,a,[role=button],[class*=top i],[id*=top i]').forEach(el=>{" +
            "if(el.dataset.mbTop||!/^\\W*top\\W*$/i.test((el.textContent||'').trim()))return;let n=el;" +
            "for(let i=0;i<4&&n&&n!==document.body;i++,n=n.parentElement){const p=getComputedStyle(n).position;if(p==='fixed'||p==='sticky'){" +
            "const r=n.getBoundingClientRect();if(r.width<160&&r.height<160){n.style.setProperty('display','none','important');el.dataset.mbTop='1'}return}}})}catch(e){}};" +
            "top();setInterval(top,700);" +
            // '다시 보지 않기'가 달린 공지 팝업(예: 보스컷 안내)은 체크하고 바로 닫는다. 사람 확인 창은 건드리지 않는다.
            "const noti=()=>{try{document.querySelectorAll(\"[role=dialog][data-state=open]\").forEach(d=>{" +
            "if(!/다시 보지 않기/.test(d.textContent))return;const x=d.querySelector('[data-slot=dialog-close]');if(!x)return;" +
            "d.style.opacity='0';const c=d.querySelector('[role=checkbox][data-state=unchecked]');if(c)c.click();setTimeout(()=>x.click(),60)})}catch(e){}};" +
            "if(mp)setInterval(noti,250);})();");
    }

    // ── 설정 메뉴에서 쓰는 기능 ──
    public static void SetTopMost(bool on)
    {
        foreach (var w in open.ToList()) w.TopMost = on || w.kind == SiteKind.Webtoon;
        RaiseWebtoons();
    }

    public static void CloseAll()
    {
        foreach (var w in open.ToList()) w.Close();
    }

    public static bool AnyOpen => open.Count > 0;

    /// <summary>
    /// 열린 창 중 캐릭터 페이지를 모두 새 닉네임으로 바꾼다.
    /// 지금 path를 보여주는 창이 하나라도 있으면 true.
    /// </summary>
    public static bool ApplyName(string name, string path)
    {
        bool showsPath = false;
        foreach (var w in open.ToList())
        {
            // 아직 불러오는 중인 창은 처음 열려던 주소를 기준으로 본다
            var src = w.web.Source;
            if (src == null || !IsSite(src.ToString())) Uri.TryCreate(w.startUrl, UriKind.Absolute, out src);
            if (src == null || !IsSite(src.ToString())) continue;
            var q = System.Web.HttpUtility.ParseQueryString(src.Query);
            bool hasName = !string.IsNullOrEmpty(q["name"]);
            var p = src.AbsolutePath.TrimEnd('/');
            if (!hasName && !SiteMenu.NamePages.Contains(p)) continue;

            if (string.Equals(p, path, StringComparison.OrdinalIgnoreCase)) showsPath = true;
            if (string.Equals(q["name"], name, StringComparison.OrdinalIgnoreCase)) continue;
            q["name"] = name;
            var url = new UriBuilder(src) { Query = q.ToString(), Fragment = "" }.Uri.ToString();
            w.startUrl = url;
            if (w.web.CoreWebView2 != null) w.web.CoreWebView2.Navigate(url);
            // 아직 준비 중이면 InitAsync가 startUrl로 연다
        }
        return showsPath;
    }

    /// <summary>열린 뮤직 창이 있으면 그 창에서 url을 열고 앞으로 가져온다.</summary>
    public static bool ShowMusic(string url)
    {
        var w = open.FirstOrDefault(x => x.kind == SiteKind.Music);
        if (w == null) return false;
        w.startUrl = url;
        // 페이지를 새로 읽으면 노래가 끊기므로 사이트 안 링크로 옮긴다
        if (w.web.CoreWebView2 != null && Uri.TryCreate(url, UriKind.Absolute, out var u))
            _ = w.web.CoreWebView2.ExecuteScriptAsync($"window.mbGo?window.mbGo({System.Text.Json.JsonSerializer.Serialize(u.AbsolutePath.TrimEnd('/'))}):location.href={System.Text.Json.JsonSerializer.Serialize(url)}");
        if (w.WindowState == FormWindowState.Minimized) w.WindowState = FormWindowState.Normal;
        w.Unpark(); w.Activate();
        return true;
    }

    static bool IsSite(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Host.EndsWith("maplescouter.com");

    static void OpenExternal(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    // ── 제목줄 없는 창 옮기기·크기 조절 ──
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);

    void DragMove()
    {
        if (WindowState == FormWindowState.Maximized) return;
        ReleaseCapture();
        SendMessage(Handle, 0xA1 /*WM_NCLBUTTONDOWN*/, (IntPtr)2 /*HTCAPTION*/, IntPtr.Zero);
    }

    void ToggleMax()
    {
        if (WindowState == FormWindowState.Maximized) { WindowState = FormWindowState.Normal; Padding = new Padding(edge); }
        else
        {
            MaximizedBounds = Screen.FromControl(this).WorkingArea;
            Padding = Padding.Empty;
            WindowState = FormWindowState.Maximized;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int round = 2; // 윈도우 11 둥근 모서리
        int noBorder = unchecked((int)0xFFFFFFFE); // 윈도우가 그리는 회색 테두리 끄기
        try
        {
            DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int));
            DwmSetWindowAttribute(Handle, 34, ref noBorder, sizeof(int));
        }
        catch { }
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.Style |= 0x20000;       // 작업 표시줄 클릭으로 최소화
            cp.ClassStyle |= CS_DROPSHADOW;  // 그림자
            if (kind == SiteKind.Music) cp.ExStyle |= WS_EX_TOOLWINDOW; // 뮤직 창은 Alt+Tab에도 안 나오게
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg != 0x84 /*WM_NCHITTEST*/ || (int)m.Result != 1 /*HTCLIENT*/ || WindowState == FormWindowState.Maximized) return;
        var p = PointToClient(new Point((short)((long)m.LParam & 0xFFFF), (short)(((long)m.LParam >> 16) & 0xFFFF)));
        int g = edge + S(3);
        bool l = p.X < g, r = p.X >= ClientSize.Width - g, t = p.Y < edge, b = p.Y >= ClientSize.Height - g;
        int hit = t && l ? 13 : t && r ? 14 : b && l ? 16 : b && r ? 17 : l ? 10 : r ? 11 : t ? 12 : b ? 15 : 0;
        if (hit != 0) m.Result = (IntPtr)hit;
    }

    // 펼치기: 크게 볼 때의 크기. 화면 밖으로 나가지 않게 맞추고, 접으면 펼치기 전 자리·크기로 돌아간다
    static readonly Size BigSize = new(1040, 565);
    readonly ToolTip alphaTip = new();
    readonly AlphaSlider alphaBar;
    /// <summary>창 투명도(%)를 바꾼다. 너무 흐려져 못 찾는 일이 없게 30% 아래로는 내리지 않는다.</summary>
    void SetAlpha(int pct)
    {
        pct = Math.Clamp(pct, AlphaSlider.Min, 100);
        Opacity = pct / 100.0;
        alphaBar.Value = pct;
        settings.WinOpacity = pct;
        settings.Save();
        alphaTip.Show($"투명도 {pct}%", grip, grip.PointToClient(Cursor.Position) + new Size(S(12), S(12)), 900);
    }

    Rectangle? smallBounds;
    /// <summary>크게 보기(펼치기)나 최대화 상태</summary>
    bool Expanded => smallBounds != null || WindowState == FormWindowState.Maximized;
    void ToggleBig()
    {
        if (WindowState != FormWindowState.Normal) WindowState = FormWindowState.Normal;
        if (smallBounds is Rectangle r) { Bounds = r; smallBounds = null; return; }
        smallBounds = Bounds;
        var wa = Screen.FromControl(this).WorkingArea;
        int w = Math.Min(BigSize.Width, wa.Width), h = Math.Min(BigSize.Height, wa.Height);
        int x = Math.Max(wa.Left, Math.Min(Left, wa.Right - w)), y = Math.Max(wa.Top, Math.Min(Top, wa.Bottom - h));
        Bounds = new Rectangle(x, y, w, h);
    }

    // 창이 앞으로 오면 페이지에 키보드 포커스를 줘서 Ctrl+W 등이 바로 먹게 한다
    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        if (kind == SiteKind.Webtoon) lastWebtoon = this;
        else RaiseWebtoons(); // 다른 창이 앞으로 와도 웹툰 창이 그 위에 있게
        if (!web.ContainsFocus) web.Focus();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.F5) { web.CoreWebView2?.Reload(); return true; }
        if (keyData == (Keys.Alt | Keys.Left)) { if (web.CanGoBack) web.GoBack(); return true; }
        if (keyData == (Keys.Control | Keys.W) || keyData == Keys.Escape) { Close(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // 옮기면 다음 첫 창 위치로 기억한다
    protected override void OnResizeEnd(EventArgs e)
    {
        base.OnResizeEnd(e);
        if (WindowState != FormWindowState.Normal) return;
        settings.WinX = Left; settings.WinY = Top;
        settings.Save();
    }

    // 뮤직 창은 ✕를 눌러도 숨기기만 한다 (노래는 계속). 바의 '뮤직 끄기'로 닫는다.
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (kind == SiteKind.Music && !reallyClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Park();
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (kind == SiteKind.Music) { musicTimer.Dispose(); MusicChanged?.Invoke(null); }
        open.Remove(this);
        base.OnFormClosed(e);
    }
}
