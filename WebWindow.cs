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
    internal static Task<CoreWebView2Environment> Env =>
        sharedEnv ??= CoreWebView2Environment.CreateAsync(null, Settings.WebDataDir,
            // 바에서 고른 곡이 숨은 창에서 바로 재생되게 한다
#if DEBUG
            new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required" + (Environment.GetEnvironmentVariable("MB_DEBUG") == "1" ? " --remote-debugging-port=9333" : "")));
#else
            new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required")); // 배포판은 디버깅 포트를 열지 않는다
#endif

    // [전역] 지금 열려 있는 사이트 창 전부. 생성자에서 넣고 닫힐 때 뺀다 (UI 스레드에서만 쓴다)
    static readonly List<WebWindow> open = new();
    /// <summary>열려 있는 창 가운데 그 종류인 것 (없으면 null)</summary>
    static WebWindow? FindOpen(SiteKind k) => open.FirstOrDefault(x => !x.IsDisposed && x.kind == k);

    readonly WebView2 web = new() { Dock = DockStyle.Fill };
    readonly Settings settings;
    readonly TitleStrip grip;
    // 이 창이 만든 Font·ToolTip·메뉴: 창이 닫힐 때 해제한다
    readonly List<IDisposable> owned = new();
    // 투명도 저장은 마지막 휠 뒤 400ms에 한 번만 한다
    readonly System.Windows.Forms.Timer alphaSaveTimer = new() { Interval = 400 };
    // 어떤 사이트 창인지: 주소를 바꿀 때마다 한 번 정해 두고, 매번 주소를 다시 풀지 않는다
    enum SiteKind { Other, Webtoon, Music, Coordi, YouTube, YouTubePlayer, YouTubeComments, Live, LivePlayer, LiveChat }
    string startUrlValue = "";
    SiteKind kind;
    /// <summary>창이 연(또는 이어서 열) 주소. 바꾸면 kind도 같이 바뀐다.</summary>
    string startUrl { get => startUrlValue; set { startUrlValue = value; kind = KindOf(value); } }
    static SiteKind KindOf(string url) =>
        Live.IsChat(url) ? SiteKind.LiveChat : Live.IsPlayer(url) ? SiteKind.LivePlayer : Live.IsLive(url) ? SiteKind.Live :
        Webtoon.IsWebtoon(url) ? SiteKind.Webtoon :
        Music.IsYtMusic(url) ? SiteKind.Music : // 유튜브 뮤직은 유튜브 주소보다 먼저 본다
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
            DoubleBuffered = true; ResizeRedraw = true; BackColor = UI.Back;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { bold.Dispose(); normal.Dispose(); }
            base.Dispose(disposing);
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
            right = owner.DrawHost(g, x, right, Height); // 지금 사이트 주소 (WebWindow.Guard.cs)
            // 페이지 부가 설명(예: '메이플 코디·믹스 염색…')은 창을 펼쳤을 때만 보인다
            if (!owner.Expanded) sub = "";
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
            var r1 = new Rectangle(x, 0, Math.Max(0, right - x), Height);
            TextRenderer.DrawText(g, main, bold, r1, UI.Text, flags);
            if (sub.Length == 0) return;
            x += TextRenderer.MeasureText(g, main, bold, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width + owner.S(8);
            if (x < right)
                TextRenderer.DrawText(g, sub, normal, new Rectangle(x, 0, right - x, Height), UI.T(Color.FromArgb(140, 140, 140)), flags);
        }
    }
    readonly float sc;
    readonly int edge; // 크기 조절용 테두리 두께

    int S(int v) => UI.Scale(v, sc);

    // 페이지보다 먼저 돌릴 스크립트 (환산 보관함에 아이템 넣기 등). 없으면 null
    readonly string? initScript;

    // 경매장 '착용 해보기'로 연 효율·보스컷 창. 바의 캐릭터 검색으로 바뀌지 않고, 경매장에서 고른 캐릭터만 보여 준다
    readonly bool wearWin;

    // 주소 칸이 있는 브라우저 창 (처음 주소가 Browser.Home)
    readonly bool browser;
    TextField? addr;       // 브라우저 창의 주소 칸
    Action? updateNav;     // 뒤로·앞으로 버튼 모양 새로고침

    /// <summary>창 아래쪽 줄: 오른쪽에 뒤로·앞으로, 브라우저 창은 왼쪽에 주소 칸</summary>
    Panel BuildNavBar()
    {
        var bar = new Panel { Dock = DockStyle.Bottom, Height = S(34), BackColor = UI.Back, Padding = new Padding(S(6), S(4), S(6), S(4)) };
        var tip = new ToolTip();
        owned.Add(tip);

        RoundLabel MakeBtn(bool forward)
        {
            var b = new RoundLabel
            {
                Dock = DockStyle.Right, AutoSize = false, Width = S(30), Text = "", Cursor = Cursors.Default,
                Fill = UI.HoverBack, Radius = S(6), UseMnemonic = false,
            };
            b.Paint += (_, e) =>
            {
                var g = e.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                bool usable = forward ? web.CanGoForward : web.CanGoBack;
                float cx = b.Width / 2f, cy = b.Height / 2f, d = S(5), s = forward ? -1f : 1f; // s=1이면 왼쪽을 가리킨다
                using var p = new Pen(usable ? UI.Text : UI.Sub, Math.Max(1.5f, S(2) * 0.8f)) { LineJoin = System.Drawing.Drawing2D.LineJoin.Round };
                g.DrawPolygon(p, new[] { new PointF(cx + s * d * 0.6f, cy - d), new PointF(cx - s * d * 0.7f, cy), new PointF(cx + s * d * 0.6f, cy + d) });
            };
            b.MouseEnter += (_, _) => b.Fill = UI.T(Color.FromArgb(225, 225, 230));
            b.MouseLeave += (_, _) => b.Fill = UI.HoverBack;
            b.Click += (_, _) =>
            {
                if (forward) { if (web.CanGoForward) web.GoForward(); }
                else if (web.CanGoBack) web.GoBack();
            };
            tip.SetToolTip(b, forward ? "앞으로" : "뒤로");
            return b;
        }
        var fwd = MakeBtn(true);
        var back = MakeBtn(false);
        bar.Controls.Add(fwd); fwd.BringToFront();
        var gap = new Panel { Dock = DockStyle.Right, Width = S(6), BackColor = Color.Transparent };
        bar.Controls.Add(gap); gap.BringToFront();
        bar.Controls.Add(back); back.BringToFront();

        updateNav = () =>
        {
            if (IsDisposed) return;
            fwd.Cursor = web.CanGoForward ? Cursors.Hand : Cursors.Default;
            back.Cursor = web.CanGoBack ? Cursors.Hand : Cursors.Default;
            fwd.Invalidate(); back.Invalidate();
        };
        web.SourceChanged += (_, _) => updateNav?.Invoke();

        if (browser)
        {
            var addrFont = new Font("맑은 고딕", 9.5f * UI.Z);
            owned.Add(addrFont);
            var box = new TextField { Dock = DockStyle.Fill, Radius = S(8), PlaceholderText = "주소나 검색어를 넣고 Enter", Font = addrFont };
            addr = box;
            bar.Height = Math.Max(S(34), box.Height + S(8));
            bar.Controls.Add(box); box.BringToFront();
            // 주소 칸을 누르고 있지 않을 때만 지금 주소를 보여 준다
            web.SourceChanged += (_, _) =>
            {
                if (box.ContainsFocus || web.Source == null) return;
                box.Text = web.Source.ToString();
            };
        }
        return bar;
    }

    public WebWindow(string url, Settings settings, float scale, string? initScript = null, bool wear = false)
    {
        this.settings = settings;
        this.initScript = initScript;
        wearWin = wear;
        browser = string.Equals(url, Browser.Home, StringComparison.Ordinal);
        sc = scale;
        yt = Music.IsYtMusic(url);
        edge = S(9); // 바깥 4는 크기 조절, 안쪽은 잡고 옮기기
        Text = "메이플 바";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = !Music.IsMusic(url); // 뮤직 창은 화면 밖에 있으므로 작업 표시줄에 안 보이게
        StartPosition = FormStartPosition.Manual;
        MinimumSize = new Size(S(300), S(240));
        BackColor = UI.Back; // 테두리(크기 조절 영역)가 따로 보이지 않게 바탕색
        Padding = new Padding(edge);
        TopMost = settings.WinTopMost || Webtoon.IsWebtoon(url); // 웹툰은 늘 맨 위
        if (UI.AppIcon is { } appIcon) Icon = appIcon;
        alphaSaveTimer.Tick += (_, _) => { alphaSaveTimer.Stop(); settings.Save(); };

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
        closeBtn = close;
        // ✕ 왼쪽 펼치기 버튼: 누르면 크게(BigSize), 다시 누르면 원래 크기로
        var gap = new Panel { Dock = DockStyle.Right, Width = S(6), BackColor = Color.Transparent };
        grip.Controls.Add(gap); gap.BringToFront();
        var grow = new RoundLabel
        {
            Dock = DockStyle.Right, AutoSize = false, Width = S(22), Text = "", Cursor = Cursors.Hand,
            ForeColor = UI.AccentText, Fill = UI.SoftAccent, Radius = S(6), UseMnemonic = false,
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
        owned.Add(growTip);
        growTip.SetToolTip(grow, "크게 보기");
        grow.MouseEnter += (_, _) => grow.Fill = UI.T(Color.FromArgb(255, 226, 196));
        grow.MouseLeave += (_, _) => grow.Fill = UI.SoftAccent;
        grow.Click += (_, _) =>
        {
            ToggleBig();
            grow.Invalidate(); grip.Invalidate();
            growTip.SetToolTip(grow, smallBounds != null ? "원래 크기로" : "크게 보기");
        };
        grip.Controls.Add(grow); grow.BringToFront();
        // 넥슨 라이브 재생 창은 크기를 고정한다 (크게 보기 버튼도 숨긴다)
        web.SourceChanged += (_, _) =>
        {
            bool fix = FixedSize;
            grow.Visible = !fix;
            if (!fix || WindowState != FormWindowState.Normal) return;
            smallBounds = null;
            var nb = NextBounds(web.Source!.ToString());
            if (Size != nb.Size) Size = nb.Size;
        };
        // 펼치기 버튼 왼쪽: 투명도 슬라이드 바
        var gap2 = new Panel { Dock = DockStyle.Right, Width = S(8), BackColor = Color.Transparent };
        grip.Controls.Add(gap2); gap2.BringToFront();
        alphaBar = new AlphaSlider { Dock = DockStyle.Right, Width = S(70), Value = Music.IsMusic(url) ? 100 : Math.Clamp(settings.WinOpacity, AlphaSlider.Min, 100) };
        alphaBar.Changed += v => SetAlpha(v);
        var alphaBarTip = new ToolTip();
        owned.Add(alphaBarTip);
        alphaBarTip.SetToolTip(alphaBar, "투명도");
        grip.Controls.Add(alphaBar); alphaBar.BringToFront();
        if (KindOf(url) is SiteKind.YouTubePlayer or SiteKind.LivePlayer) { AddCommentsButton(KindOf(url) == SiteKind.LivePlayer ? "채팅" : "댓글"); StartBare(); }
        if (KindOf(url) is SiteKind.YouTube or SiteKind.Live) AddYtModeButton(KindOf(url) == SiteKind.Live);
        if (Music.IsMusic(url))
        {
            // 뮤직 창: 곡 정보를 1초마다 읽어 바에 알린다
            // 앞 확인이 아직 안 끝났으면 이번 틱은 건너뛴다 (페이지가 느릴 때 확인이 겹쳐 쌓이지 않게)
            bool polling = false;
            musicTimer.Tick += async (_, _) =>
            {
                if (polling) return;
                polling = true;
                try { await PollMusic(); } finally { polling = false; }
            };
            if (yt) musicTimer.Interval = 400; // 가사의 지금 줄을 늦지 않게 따라간다
            musicTimer.Start();
            // 로그인 안 했거나 프리미엄이 아니면 유튜브 뮤직이 가입 안내·로그인 페이지로 넘어간다.
            // 그 페이지엔 재생 막대가 없어 바에 칸이 안 생기므로, 숨겨 둔 창을 보여 줘야 눌렀을 때 반응이 있다
            if (yt) web.SourceChanged += (_, _) =>
            {
                if (parked && !Music.IsYtMusic(web.Source?.ToString())) { Unpark(); Activate(); }
            };
        }

        var menuFont = new Font("맑은 고딕", 9.5f * UI.Z);
        var menu = UI.Rounded(new ContextMenuStrip { Font = menuFont, ShowImageMargin = false });
        owned.Add(menu); owned.Add(menuFont); // 메뉴를 먼저 해제하고 글꼴을 해제한다
        menu.Items.Add("새로고침 (F5)", null, (_, _) => web.CoreWebView2?.Reload());
        menu.Items.Add("뒤로 (Alt+←)", null, (_, _) => { if (web.CanGoBack) web.GoBack(); });
        menu.Items.Add("브라우저로 열기", null, (_, _) => { if (web.Source != null) OpenExternal(web.Source.ToString()); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("닫기 (Ctrl+W)", null, (_, _) => Close());
        // 투명도: 제목 줄의 슬라이드 바(또는 제목 줄에서 휠). 마지막 값은 새 창에도 쓴다
        grip.MouseWheel += (_, e) => SetAlpha((int)Math.Round(Opacity * 100) + (e.Delta > 0 ? 5 : -5));

        grip.ContextMenuStrip = menu;
        grip.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left || e.Clicks != 1) return;
            // 최대화 중이면 실제로 끌기 시작할 때 원래 크기로 돌린다 (더블클릭은 그대로 최대화 풀기)
            if (WindowState == FormWindowState.Maximized) maxDownAt = Cursor.Position;
            else DragMove();
        };
        grip.MouseUp += (_, _) => maxDownAt = null;
        grip.MouseMove += (_, e) =>
        {
            if (maxDownAt is not Point p || e.Button != MouseButtons.Left) return;
            var c = Cursor.Position; var ds = SystemInformation.DragSize;
            if (Math.Abs(c.X - p.X) < ds.Width && Math.Abs(c.Y - p.Y) < ds.Height) return;
            maxDownAt = null;
            UnmaxAndDrag(c);
        };
        grip.DoubleClick += (_, _) => ToggleMax();

        Controls.Add(web);
        Controls.Add(grip);
        if (KindOf(url) is SiteKind.Other or SiteKind.Webtoon or SiteKind.Coordi or SiteKind.YouTube or SiteKind.Live && !wearWin && !Nexon.IsAuction(url))
            Controls.Add(BuildNavBar());
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
        if (KindOf(url) is SiteKind.YouTubePlayer or SiteKind.LivePlayer) { var ps = PlayerSize(YouTube.IsShorts(url), wa); w = ps.Width; h = ps.Height; }
        // SOOP 재생 창은 영상과 채팅을 같이 보이므로 넓게 연다 (사용자가 맞춘 1018×515)
        if (KindOf(url) == SiteKind.LivePlayer && Uri.TryCreate(url, UriKind.Absolute, out var lu)
            && (lu.Host == "live.nexon.com" || Hosts.Is(lu, "sooplive.com") || Hosts.Is(lu, "sooplive.co.kr") || Hosts.Is(lu, "afreecatv.com")))
        {
            // 넥슨 라이브: 위 제목 줄 32 + 영상(1000×563) + 아래 재생 바 36 + 테두리
            float d = DeviceDpi / 96f; int bh = lu.Host == "live.nexon.com" ? 649 : 515;
            w = Math.Min((int)Math.Round(1018 * d), wa.Width); h = Math.Min((int)Math.Round(bh * d), wa.Height);
        }
        // 방송은 영상과 채팅이 옆으로 놓이니 가로로 넓게 연다
        if (KindOf(url) == SiteKind.Live) { w = Math.Min(W(800), wa.Width); h = Math.Min(W(500), wa.Height); }
        // 브라우저는 주소 칸과 넓은 사이트를 보니 조금 크게 연다
        if (string.Equals(url, Browser.Home, StringComparison.Ordinal)) { w = Math.Min(W(900), wa.Width); h = Math.Min(W(650), wa.Height); }
        // 경매장: 사이트가 700px 이하면 모바일 화면이 되므로 그보다 넓게, 1100px 전체보다는 작게 연다 (그 사이 폭은 AuctionScript가 맞춘다)
        if (Nexon.IsAuction(url)) { float d = DeviceDpi / 96f; w = Math.Min((int)Math.Round(900 * d), wa.Width); h = Math.Min((int)Math.Round(680 * d), wa.Height); }

        // 이 종류 창을 전에 닫은 자리가 있으면 그 자리에 연다 (같은 종류가 이미 열려 있으면 비켜서 연다)
        var key = PlaceKey(url);
        bool keepSize = KindOf(url) is not (SiteKind.YouTubePlayer or SiteKind.LivePlayer); // 재생 창 크기는 영상 비율에 맞춘다
        Rectangle Fit(Rectangle r)
        {
            var swa = Screen.FromRectangle(r).WorkingArea; // 다른 모니터에 두었으면 그 모니터에 연다
            int rw = Math.Min(Math.Max(r.Width, S(200)), swa.Width), rh = Math.Min(Math.Max(r.Height, S(150)), swa.Height);
            return new Rectangle(Math.Max(swa.Left, Math.Min(r.X, swa.Right - rw)), Math.Max(swa.Top, Math.Min(r.Y, swa.Bottom - rh)), rw, rh);
        }
        // 같은 종류 창이 이미 열려 있으면 그 창 옆으로 살짝 비켜서 연다 (자리가 없으면 반대쪽으로)
        var same = key == null ? null : open.LastOrDefault(o => o != this && !o.IsDisposed && PlaceKey(o.startUrl) == key);
        if (same != null)
        {
            var b = same.WindowState == FormWindowState.Normal && same.smallBounds == null ? same.Bounds : same.RestoreBounds;
            var size = keepSize ? b.Size : new Size(w, h);
            var swa = Screen.FromRectangle(b).WorkingArea;
            int d = S(30);
            foreach (var (dx, dy) in new[] { (-d, d), (d, d), (-d, -d), (d, -d) })
            {
                var r = new Rectangle(b.X + dx, b.Y + dy, size.Width, size.Height);
                if (swa.Contains(r)) return r;
            }
            return Fit(new Rectangle(b.X - d, b.Y + d, size.Width, size.Height));
        }
        // 이 메뉴 창을 닫은 자리가 있으면 그 자리·크기로 연다
        var pageKey = PageKey(url);
        if (pageKey != null && settings.WinPlaces.TryGetValue(pageKey, out var pp) && pp.Length == 4)
            return Fit(new Rectangle(pp[0], pp[1], keepSize ? pp[2] : w, keepSize ? pp[3] : h));
        // 없으면 같은 종류(사이트) 창을 닫은 자리에 연다. 처음 여는 메뉴는 다른 메뉴에서 늘린 크기 말고 기본 크기로
        if (key != null && settings.WinPlaces.TryGetValue(key, out var sp) && sp.Length == 4)
        {
            bool own = pageKey == null && keepSize;
            return Fit(new Rectangle(sp[0], sp[1], own ? sp[2] : w, own ? sp[3] : h));
        }

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
            if (IsDisposed) return; // 기다리는 동안 창이 닫혔다
            await web.EnsureCoreWebView2Async(env);
            var cw = web.CoreWebView2;
            // 바가 다크 모드면 사이트에도 '다크 모드 쓰는 중'이라고 알려 준다 (자체 다크 화면이 있는 사이트는 그걸 띄운다)
            try { cw.Profile.PreferredColorScheme = UI.Dark ? CoreWebView2PreferredColorScheme.Dark : CoreWebView2PreferredColorScheme.Light; }
            catch (Exception ex) { Log.Write("web", "사이트 색 모드를 못 맞춤", ex); }
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
            catch (Exception ex) { Log.Write("web", "Ctrl+W 단축키를 걸지 못함", ex); }
            // 페이지 안에 포커스가 있어도 Ctrl+W로 창을 닫는다 (한글 입력 중에도 되도록 키 위치로 본다)
            await cw.AddScriptToExecuteOnDocumentCreatedAsync(
                "addEventListener('keydown',e=>{if(e.ctrlKey&&!e.altKey&&!e.shiftKey&&e.code==='KeyW'){e.preventDefault();e.stopImmediatePropagation();" +
                "try{chrome.webview.postMessage('mb:close')}catch(_){}}},true);" +
                // Esc도 창을 닫는다. 페이지가 먼저 쓴 Esc(팝업 닫기 등)는 건드리지 않는다
                "addEventListener('keydown',e=>{if(e.key==='Escape'&&!e.defaultPrevented&&!e.isComposing&&!document.fullscreenElement){" +
                "try{chrome.webview.postMessage('mb:close')}catch(_){}}});");
            // 구글 페이지에 뜨는 "Chrome을 선택하세요" 권유 창을 숨긴다 (사용자 요청)
            await cw.AddScriptToExecuteOnDocumentCreatedAsync(NoChromePromoScript);
            cw.DOMContentLoaded += (_, _) => Lift(); // 새 페이지에도 영상 밝기 보정을 다시 건다
            cw.HistoryChanged += (_, _) => updateNav?.Invoke(); // 뒤로·앞으로 버튼 모양
            cw.NavigationCompleted += (_, _) => Logins.RefreshSoon(); // 로그인 상태 다시 확인 (몰아서 한 번)
            cw.WebMessageReceived += (_, e) =>
            {
                string? m = null; try { m = e.TryGetWebMessageAsString(); } catch { }
                if (m == "mb:close" && !IsDisposed) BeginInvoke(() => { if (!IsDisposed) Close(); });
                // 브금 랜덤 재생이 고른 곡이 지금 화면에 없으면 앱이 찾아서 튼다
                else if (m != null && m.StartsWith("mb:play:") && kind == SiteKind.Music && !yt && !IsDisposed) _ = PlayFromPage(m[8..]);
                // 경매장 아이템을 환산 보관함에 넣는다
                // (보낸 쪽이 경매장 페이지 자체일 때만: 창 안에 끼워 넣은 다른 사이트가 보낸 건 받지 않는다)
                else if (m != null && m.StartsWith("mb:scouter:") && Nexon.IsAuction(cw.Source) && Nexon.IsAuction(e.Source) && !IsDisposed) BeginInvoke(() => { _ = SendToScouter(m[11..]); });
                // 경매장 검색 결과를 이 컴퓨터에 보관·불러오기·지우기
                else if (m != null && m.StartsWith("mb:auc:") && Nexon.IsAuction(cw.Source) && Nexon.IsAuction(e.Source) && m.Length < 6_000_000 && !IsDisposed) AuctionSaves.Handle(this, cw, m[7..]);
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
            else if (kind is SiteKind.Live or SiteKind.LivePlayer or SiteKind.LiveChat) await InitLive(cw); // 방송 사이트도 광고를 막지 않는다
            else if (browser) { } // 브라우저 창은 광고를 막지 않는다
            else if (kind != SiteKind.Music) await BlockAds(cw, env); // 숨김 스크립트 등록이 끝난 뒤에 페이지를 연다
            else if (yt) await cw.AddScriptToExecuteOnDocumentCreatedAsync(YtMusicScript); // 유튜브 뮤직: 광고는 그대로 둔다
            else
            {
                await cw.AddScriptToExecuteOnDocumentCreatedAsync(MusicScript);
                fxScriptId = await cw.AddScriptToExecuteOnDocumentCreatedAsync(FxScript(settings.MusicFx));
            }
            // 경매장: 필터 검색 5초 대기를 버튼에 보여 준다 (로그인 뒤 같은 창에서 경매장으로 오므로 넥슨 주소면 넣어 둔다)
            if (Nexon.IsNexon(startUrl))
            {
                await cw.AddScriptToExecuteOnDocumentCreatedAsync(Nexon.AuctionScript);
                await cw.AddScriptToExecuteOnDocumentCreatedAsync(Nexon.PickScript); // 아이템 → 환산 보관함
                await cw.AddScriptToExecuteOnDocumentCreatedAsync(Nexon.AuctionUiScript); // 우리 경매장 화면
            }
            if (initScript != null) await cw.AddScriptToExecuteOnDocumentCreatedAsync(initScript);
            if (wearWin) await cw.AddScriptToExecuteOnDocumentCreatedAsync("window.__mbWearWin = true;");
            if (IsSite(startUrl)) await cw.AddScriptToExecuteOnDocumentCreatedAsync(Nexon.WearViewScript); // '착용 해보기'로 연 보스컷 창
            await cw.AddScriptToExecuteOnDocumentCreatedAsync(CtrlWheelScript); // Ctrl을 누른 채 휠을 굴려도 화면 배율은 그대로
            cw.Settings.IsZoomControlEnabled = false; // 배율은 프로그램 설정에서만 바꾼다
#if !DEBUG
            cw.Settings.AreDevToolsEnabled = false; // 개발자 도구(F12)는 테스트 빌드에서만
#endif
            ApplyZoom();
            // 인벤은 작은 창에 맞는 모바일 화면으로 보이게 한다
            if (!browser && (Inven.IsInven(startUrl) || kind == SiteKind.Webtoon))
                cw.Settings.UserAgent = Inven.MobileUA;
            // 새 창 링크: 사이트 안이면 또 새 창으로, 바깥이면 기본 브라우저로
            cw.NewWindowRequested += async (_, e) =>
            {
                // 구글·애플 로그인 팝업은 원래 창과 이어진 채로 열어야 로그인이 끝난다 (Claude 등)
                if (!parked && IsLoginPopup(e.Uri))
                {
                    var d = e.GetDeferral();
                    try
                    {
                        e.NewWindow = await OpenLoginPopup(cw.Source is { } src && Uri.TryCreate(src, UriKind.Absolute, out var ou) ? ou : null); // WebWindow.Guard.cs
                        e.Handled = true;
                    }
                    catch { e.Handled = true; }
                    finally { d.Complete(); }
                    return;
                }
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
                if (Live.IsPlayer(e.Uri))
                {
                    if (kind == SiteKind.Live && settings.LiveSameWindow) cw.Navigate(e.Uri); else ShowLive(e.Uri, settings, sc);
                    return;
                }
                if (browser) { cw.Navigate(e.Uri); return; } // 브라우저 창은 새 창 링크도 이 창에서 연다
                if (IsSite(e.Uri) || Inven.IsInven(e.Uri) || Music.IsMeaegi(e.Uri) || Webtoon.IsNaver(e.Uri) || YouTube.IsYouTube(e.Uri) || Live.IsLive(e.Uri) || Nexon.IsNexon(e.Uri)) new WebWindow(e.Uri, settings, sc).Show();
                else OpenExternal(e.Uri);
            };
            cw.DocumentTitleChanged += (_, _) => { Text = cw.DocumentTitle; grip.Invalidate(); };
            NoDownloads(cw); // 파일은 평소 쓰는 브라우저에서 받게 한다 (WebWindow.Guard.cs)
            GuardNavigation(cw); // 로그인 창은 로그인 사이트 밖으로 못 가게 (WebWindow.Guard.cs)
            cw.Navigate(startUrl);
        }
        catch (Exception ex)
        {
            sharedEnv = null;
            if (IsDisposed) return; // 이미 닫힌 창에는 안내 창을 띄우지 않는다
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
        Hosts.Is(u, "maplescouter.com") && u.AbsolutePath.Contains("/ads/", StringComparison.OrdinalIgnoreCase);

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
    public const int ZoomMin = 50, ZoomMax = 200;

    void ApplyZoom()
    {
        if (web.IsDisposed || web.CoreWebView2 == null) return;
        try { web.ZoomFactor = Math.Clamp(settings.WebZoom, ZoomMin, ZoomMax) / 100.0; } catch { }
    }

    /// <summary>열린 사이트 창 모두에 화면 배율을 바로 적용한다</summary>
    public static void SetZoom() { foreach (var w in open.ToList()) w.ApplyZoom(); }

    public static void SetTopMost(bool on)
    {
        foreach (var w in open.ToList()) w.TopMost = on || w.kind == SiteKind.Webtoon;
        RaiseWebtoons();
    }

    public static void CloseAll()
    {
        foreach (var w in open.ToList())
        {
            if (w.IsDisposed) continue; // 경매장 창이 닫히며 같이 닫힌 배율표
            w.reallyClose = true; w.Close();
        }
    }

    /// <summary>숨겨 둔 경매장 창이 있으면 다시 보여 준다 (닫을 때 숨겨 둔 그 화면 그대로)</summary>
    public static bool ShowHiddenAuction()
    {
        var w = open.LastOrDefault(x => !x.IsDisposed && !x.Visible && x.TopLevel && Nexon.IsAuction(x.web.Source?.ToString() ?? x.startUrl));
        if (w == null) return false;
        w.Show();
        if (w.WindowState == FormWindowState.Minimized) w.WindowState = FormWindowState.Normal;
        w.Activate();
        return true;
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
            if (w.wearWin) continue; // 착용 창은 경매장에서 고른 캐릭터 그대로 둔다
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
        var w = MusicWin;
        if (w == null) return false;
        // 브금과 유튜브 뮤직은 하나만: 다른 쪽을 열면 지금 것을 끄고 새로 연다
        if (w.yt != Music.IsYtMusic(url))
        {
            var (st, s) = (w.settings, w.sc);
            StopMusic();
            EnsureMusic(st, s, url, true);
            return true;
        }
        if (w.yt)
        {
            // 유튜브 뮤직은 다시 읽으면 노래가 끊기니, 홈이 아닌 주소일 때만 옮긴다
            if (Uri.TryCreate(url, UriKind.Absolute, out var yu) && yu.PathAndQuery.Length > 1) w.web.CoreWebView2?.Navigate(url);
            if (w.WindowState == FormWindowState.Minimized) w.WindowState = FormWindowState.Normal;
            w.Unpark(); w.Activate();
            return true;
        }
        w.startUrl = url;
        // 페이지를 새로 읽으면 노래가 끊기므로 사이트 안 링크로 옮긴다
        if (w.web.CoreWebView2 != null && Uri.TryCreate(url, UriKind.Absolute, out var u))
            _ = w.web.CoreWebView2.ExecuteScriptAsync($"window.mbGo?window.mbGo({System.Text.Json.JsonSerializer.Serialize(u.AbsolutePath.TrimEnd('/'))}):location.href={System.Text.Json.JsonSerializer.Serialize(url)}");
        if (w.WindowState == FormWindowState.Minimized) w.WindowState = FormWindowState.Normal;
        w.Unpark(); w.Activate();
        return true;
    }

    /// <summary>게임에서 Ctrl을 스킬 키로 누른 채 휠을 굴리면 창 배율이 바뀌던 것을 막고, 그냥 스크롤되게 한다</summary>
    const string CtrlWheelScript = """
        (() => {
          if (window.__mbCtrlWheel) return; window.__mbCtrlWheel = 1;
          const can = (n, dy, dx) => {
            const s = getComputedStyle(n);
            const y = dy && /(auto|scroll|overlay)/.test(s.overflowY) && n.scrollHeight > n.clientHeight + 1 &&
              (dy < 0 ? n.scrollTop > 0 : n.scrollTop + n.clientHeight < n.scrollHeight - 1);
            const x = dx && /(auto|scroll|overlay)/.test(s.overflowX) && n.scrollWidth > n.clientWidth + 1;
            return y || x;
          };
          addEventListener('wheel', e => {
            if (!e.ctrlKey) return;
            e.preventDefault();
            const k = e.deltaMode === 1 ? 40 : e.deltaMode === 2 ? innerHeight : 1, dy = e.deltaY * k, dx = e.deltaX * k;
            let n = e.target instanceof Element ? e.target : null;
            while (n && n !== document.body && n !== document.documentElement && !can(n, dy, dx)) n = n.parentElement;
            const t = n && n !== document.body && n !== document.documentElement ? n : (document.scrollingElement || document.documentElement);
            t.scrollBy({ top: dy, left: dx });
          }, { passive: false, capture: true });
        })();
        """;

    static bool IsLoginPopup(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) &&
        (u.Host == "accounts.google.com" || u.Host == "appleid.apple.com");

    static bool IsSite(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && Hosts.Is(u, "maplescouter.com");


    /// <summary>경매장에서 고른 아이템을 환산 보관함(아이템메이커가 쓰는 곳)에 넣고 효율·보스컷을 보여 준다.
    /// 열린 효율·보스컷 창이 있으면 거기에 넣고 다시 읽고, 없으면 새 창을 열면서 넣는다.</summary>
    bool sendingWear;
    string? nextWear;

    // 넣는 사이에 '착용 해보기'를 또 누르면 겹쳐 돌리지 않고, 끝난 뒤 마지막으로 누른 아이템만 이어서 넣는다
    async Task SendToScouter(string json)
    {
        if (sendingWear) { nextWear = json; return; }
        sendingWear = true;
        try
        {
            for (string? cur = json; cur != null && !IsDisposed; )
            {
                nextWear = null;
                await SendToScouterOnce(cur);
                cur = nextWear;
            }
        }
        finally { sendingWear = false; nextWear = null; }
    }

    async Task SendToScouterOnce(string json)
    {
        try
        {
            if (json.Length > 64_000) return;
            if (System.Text.Json.Nodes.JsonNode.Parse(json) is not System.Text.Json.Nodes.JsonObject root ||
                root["name"] is not System.Text.Json.Nodes.JsonValue nm || !nm.TryGetValue<string>(out _)) return;
            // 경매장에서 고른 캐릭터 (아이템 정보에는 넣지 않는다)
            string? who = null;
            if (root["mbWho"] is System.Text.Json.Nodes.JsonValue wv && wv.TryGetValue<string>(out var ws))
            {
                var s = ws.Trim();
                if (System.Text.RegularExpressions.Regex.IsMatch(s, @"^[\p{L}\p{N}]{1,12}$")) who = s;
            }
            root.Remove("mbWho");
            // 다시 써서 넣는다 (페이지가 보낸 글자를 그대로 스크립트에 붙이지 않는다)
            var script = Nexon.AddBookmarkScript(root.ToJsonString(), Guid.NewGuid().ToString("N")[..12]);

            var nick = who ?? settings.StarNick ?? settings.MapleNick;
            var url = SiteMenu.Base + SiteMenu.BossCutPath + (string.IsNullOrWhiteSpace(nick) ? "" : "?name=" + Uri.EscapeDataString(nick));

            // 착용은 늘 착용 창 하나에서 본다 (내가 연 효율·보스컷 창은 건드리지 않는다)
            var target = open.FirstOrDefault(w => w.wearWin && !w.IsDisposed);
            // 배율표는 이 경매장 창 안에 붙인다. 다른 경매장 창에 붙은 것은 닫고 여기에 새로 연다 (닫은 창이 아래 목록에 끼지 않게 먼저 닫는다)
            if (target != null && target.wearHost != this) { target.Close(); target = null; }
            // 보관함을 쓰는 화면(효율·보스컷, 아이템메이커)은 넣은 뒤 다시 읽어야 옛 목록으로 덮어쓰지 않는다
            var scouters = open.Where(w => !w.IsDisposed && w.web.CoreWebView2 != null && w.web.Source != null && IsSite(w.web.Source.ToString())).ToList();
            bool UsesList(WebWindow w) => w.web.Source!.AbsolutePath.TrimEnd('/') is SiteMenu.BossCutPath or "/ko/item";
            if (scouters.Count > 0)
            {
                // 곧 다시 읽을 창(착용 창 포함)은 먼저 멈춘다: 화면에 남은 옛 목록이 새 목록을 덮어쓰면
                // 이전 착용 아이템이 되살아나 '꼈다 → 벗었다 → 다시 낌'이 된다
                var reload = scouters.Where(w => w != target && UsesList(w)).ToList();
                foreach (var w in reload.Append(target))
                    if (w is { IsDisposed: false } && w.web.CoreWebView2 != null)
                        try { await w.web.CoreWebView2.ExecuteScriptAsync(Nexon.FreezeListScript); } catch { }
                await AddThenReload(scouters[0], script, reload);
                if (target != null && !target.IsDisposed && target.web.CoreWebView2 != null)
                {
                    // 착용 창은 고른 캐릭터로 다시 연다
                    target.startUrl = url;
                    target.web.CoreWebView2.Navigate(url);
                    SetFolded(false); // 접어 두었으면 다시 펼친다
                    return;
                }
            }
            if (IsDisposed || Disposing) return; // 기다리는 사이 이 경매장 창이 닫혔으면 붙일 곳이 없다
            if (target != null && !target.IsDisposed) target.Close(); // 아직 준비 안 된 착용 창은 닫고 새로 연다
            // 열린 환산 창이 있으면 거기서 이미 넣었으니 새 창은 그냥 연다
            EmbedWear(new WebWindow(url, settings, sc, scouters.Count > 0 ? null : script, wear: true));
        }
        catch (Exception ex) { Log.Write("web", "경매장 아이템을 환산에 넣지 못함", ex); }
    }

    // ── 경매장 창 안의 보스 배율표 칸 (가운데 작은 칸막이로 나눈다) ──
    WebWindow? wearChild, wearHost;
    Panel? wearPane;
    WearDivider? wearDiv;
    int widened; // 배율표를 붙이며 창을 넓힌 너비 (닫으면 그만큼 줄인다)
    RoundLabel? closeBtn;

    void EmbedWear(WebWindow w)
    {
        if (wearPane == null)
        {
            int pw = (int)Math.Ceiling(740 * DeviceDpi / 96.0);
            var div = new WearDivider(S(10));
            // 보통 크기면 붙일 만큼 창을 넓힌다 (경매장 화면이 좁아지지 않게)
            if (WindowState == FormWindowState.Normal)
            {
                var wa = Screen.FromControl(this).WorkingArea;
                int want = Math.Max(Width, Math.Min(Width + pw + div.Width, wa.Width)); // 이미 화면보다 넓으면 줄이지 않는다
                widened = want - Width;
                int x = Math.Max(wa.Left, Math.Min(Left, wa.Right - want));
                Bounds = new Rectangle(x, Top, want, Height);
            }
            wearPane = new Panel { Dock = DockStyle.Right, Width = pw, BackColor = UI.Back };
            wearDiv = div;
            div.Dragged += dx =>
            {
                if (wearPane == null) return;
                int min = S(320), max = Math.Max(min, ClientSize.Width - Padding.Horizontal - S(360));
                wearPane.Width = Math.Clamp(wearPane.Width - dx, min, max);
            };
            Controls.Add(wearPane);
            Controls.Add(div);
            // 놓는 순서: 제목 줄(위) → 배율표(오른쪽) → 칸막이 → 경매장(나머지)
            Controls.SetChildIndex(web, 0);
            Controls.SetChildIndex(div, 1);
            Controls.SetChildIndex(wearPane, 2);
            Controls.SetChildIndex(grip, 3);
        }
        w.Opacity = 1;
        w.TopLevel = false;
        w.ShowInTaskbar = false;
        w.Padding = Padding.Empty;
        w.Dock = DockStyle.Fill;
        w.wearHost = this;
        // 경매장과 한 화면처럼 보이게 배율표의 제목 줄(닉네임·버튼)은 통째로 숨긴다. 접기는 경매장 제목 줄의 버튼으로 한다
        w.grip.Visible = false;
        EnsureFoldButton();
        if (folded) SetFolded(false);
        wearChild = w;
        w.FormClosed += (_, _) => { if (wearChild == w) RemoveWearPane(); };
        wearPane.Controls.Add(w);
        w.Show();
    }

    // 효율표 접기·펼치기 버튼 (경매장 제목 줄, 투명도 바 왼쪽). 효율표가 붙어 있을 때만 보인다
    Control? foldBtn;
    bool folded;

    void EnsureFoldButton()
    {
        if (foldBtn != null) { foldBtn.Visible = true; return; }
        var gap = new Panel { Dock = DockStyle.Right, Width = S(8), BackColor = Color.Transparent };
        // 옅은 회색 둥근 네모 + 화살표 + 짧은 글자 (주황 버튼들 옆에서 튀지 않게)
        var fb = new Panel { Dock = DockStyle.Right, Width = S(64), Text = "접기", Cursor = Cursors.Hand, BackColor = UI.Back };
        bool hot = false;
        var fnt = new Font("맑은 고딕", 9f);
        owned.Add(fnt);
        typeof(Panel).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(fb, true);
        fb.Paint += (_, e) =>
        {
            var g = e.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, fb.Width - 1.5f, fb.Height - 1.5f);
            using (var box = UI.RoundRect(r, S(6)))
            {
                using var bg = new SolidBrush(hot ? UI.T(Color.FromArgb(234, 236, 240)) : UI.T(Color.FromArgb(244, 245, 247)));
                using var pen = new Pen(UI.T(Color.FromArgb(220, 223, 228)));
                g.FillPath(bg, box); g.DrawPath(pen, box);
            }
            bool open = fb.Text == "접기";
            var col = UI.T(Color.FromArgb(85, 90, 98));
            // 접기: 오른쪽으로 접히는 화살표(›) / 펼치기: 왼쪽으로 펼치는 화살표(‹)
            float cx = S(13), cy = fb.Height / 2f, d = S(3);
            using (var p = new Pen(col, Math.Max(1.4f, S(2) * 0.75f)) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round, LineJoin = System.Drawing.Drawing2D.LineJoin.Round })
                g.DrawLines(p, open
                    ? new[] { new PointF(cx - d / 2, cy - d), new PointF(cx + d / 2, cy), new PointF(cx - d / 2, cy + d) }
                    : new[] { new PointF(cx + d / 2, cy - d), new PointF(cx - d / 2, cy), new PointF(cx + d / 2, cy + d) });
            TextRenderer.DrawText(g, fb.Text, fnt, new Rectangle(S(20), 0, fb.Width - S(24), fb.Height), col,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        };
        fb.TextChanged += (_, _) => fb.Invalidate();
        fb.MouseEnter += (_, _) => { hot = true; fb.Invalidate(); };
        fb.MouseLeave += (_, _) => { hot = false; fb.Invalidate(); };
        foldBtn = fb;
        foldBtn.Click += (_, _) => SetFolded(!folded);
        foldBtn.VisibleChanged += (_, _) => gap.Visible = foldBtn.Visible;
        grip.Controls.Add(gap); gap.BringToFront();
        grip.Controls.Add(foldBtn); foldBtn.BringToFront();
        grip.Invalidate();
    }

    /// <summary>효율표를 접으면 오른쪽 칸이 사라지고 창이 원래 너비로 줄어든다. 펼치면 다시 늘어난다 (보던 화면은 그대로)</summary>
    void SetFolded(bool on)
    {
        if (wearPane == null || wearDiv == null || folded == on) return;
        folded = on;
        int add = wearPane.Width + wearDiv.Width;
        SuspendLayout();
        if (on)
        {
            wearPane.Visible = false; wearDiv.Visible = false;
            if (WindowState == FormWindowState.Normal && widened > 0) { Width -= widened; widened = 0; }
        }
        else
        {
            if (WindowState == FormWindowState.Normal)
            {
                var wa = Screen.FromControl(this).WorkingArea;
                int want = Math.Min(Width + add, wa.Width);
                widened = want - Width;
                int x = Math.Max(wa.Left, Math.Min(Left, wa.Right - want));
                Bounds = new Rectangle(x, Top, want, Height);
            }
            wearPane.Visible = true; wearDiv.Visible = true;
        }
        ResumeLayout();
        if (foldBtn != null) foldBtn.Text = on ? "펼치기" : "접기";
        grip.Invalidate();
    }

    void RemoveWearPane()
    {
        wearChild = null;
        folded = false;
        if (foldBtn != null && !foldBtn.IsDisposed) { foldBtn.Text = "접기"; foldBtn.Visible = false; }
        if (IsDisposed || Disposing) return;
        if (wearPane != null) { Controls.Remove(wearPane); wearPane.Dispose(); wearPane = null; }
        if (wearDiv != null) { Controls.Remove(wearDiv); wearDiv.Dispose(); wearDiv = null; }
        if (widened > 0 && WindowState == FormWindowState.Normal) Width -= widened;
        widened = 0;
    }

    /// <summary>경매장과 배율표 사이의 작은 칸막이. 잡고 옆으로 끌면 배율표 너비가 바뀐다</summary>
    sealed class WearDivider : Panel
    {
        public event Action<int>? Dragged;
        int? downX;
        public WearDivider(int width)
        {
            Dock = DockStyle.Right; Width = width; Cursor = Cursors.SizeWE;
            BackColor = UI.T(Color.FromArgb(243, 244, 246));
            DoubleBuffered = true;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            // 양옆 얇은 선 + 가운데 작은 손잡이(둥근 직사각형)
            using var line = new Pen(UI.T(Color.FromArgb(222, 225, 230)));
            g.DrawLine(line, 0, 0, 0, Height); g.DrawLine(line, Width - 1, 0, Width - 1, Height);
            int hw = Math.Max(3, Width / 3), hh = Math.Max(24, Width * 4), x = (Width - hw) / 2, y = (Height - hh) / 2;
            using var b = new SolidBrush(UI.T(Color.FromArgb(196, 200, 207)));
            using var path = new System.Drawing.Drawing2D.GraphicsPath();
            int r = 2;
            path.AddArc(x, y, r * 2, r * 2, 180, 90); path.AddArc(x + hw - r * 2, y, r * 2, r * 2, 270, 90);
            path.AddArc(x + hw - r * 2, y + hh - r * 2, r * 2, r * 2, 0, 90); path.AddArc(x, y + hh - r * 2, r * 2, r * 2, 90, 90);
            path.CloseFigure();
            g.FillPath(b, path);
        }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { downX = Cursor.Position.X; Capture = true; } base.OnMouseDown(e); }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (downX is int x0) { int x = Cursor.Position.X; if (x != x0) { Dragged?.Invoke(x - x0); downX = x; } }
            base.OnMouseMove(e);
        }
        protected override void OnMouseUp(MouseEventArgs e) { downX = null; Capture = false; base.OnMouseUp(e); }
    }

    static async Task AddThenReload(WebWindow via, string script, List<WebWindow> reload)
    {
        try
        {
            await via.web.CoreWebView2.ExecuteScriptAsync(script);
            foreach (var w in reload) if (!w.IsDisposed) w.web.CoreWebView2?.Reload();
        }
        catch (Exception ex) { Log.Write("web", "환산 보관함에 넣지 못함", ex); }
    }

    static void OpenExternal(string url)
    {
        if (!Hosts.IsWeb(url)) { Log.Write("web", "웹 주소가 아니라 열지 않음"); return; } // 프로그램·파일 주소는 열지 않는다
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Write("web", "기본 브라우저로 열지 못함", ex); }
    }

    // ── 제목줄 없는 창 옮기기·크기 조절 ──
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);

    void DragMove()
    {
        if (WindowState == FormWindowState.Maximized || !TopLevel) return; // 경매장 창 안에 붙은 배율표는 옮기지 않는다
        DragWindow(Handle);
    }

    Point? maxDownAt; // 최대화된 창의 제목 줄을 누른 자리 (끌기 시작 전)

    /// <summary>최대화된 창을 원래 크기로 돌리고, 마우스가 제목 줄의 같은 비율 자리에 오게 놓은 뒤 이어서 끈다</summary>
    void UnmaxAndDrag(Point cursor)
    {
        if (WindowState != FormWindowState.Maximized) return;
        double fx = (double)(cursor.X - Left) / Math.Max(1, Width);
        int dy = Math.Min(cursor.Y - Top, S(16));
        ToggleMax();
        var wa = Screen.FromPoint(cursor).WorkingArea;
        int x = cursor.X - (int)(Width * fx);
        x = Math.Clamp(x, wa.Left - Width + S(80), wa.Right - S(80));
        Location = new Point(x, cursor.Y - dy);
        DragWindow(Handle);
    }

    /// <summary>넥슨 라이브 재생 창: 페이지가 창 넓이에 따라 모양이 크게 바뀌어서, 알맞은 크기로 고정한다</summary>
    bool FixedSize => kind == SiteKind.LivePlayer && Live.IsNexon(web.Source?.ToString() ?? startUrl);

    void ToggleMax()
    {
        if (FixedSize || !TopLevel) return;
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
        catch (Exception ex) { Log.Write("web", "창 모서리·테두리 모양을 바꾸지 못함", ex); }
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
        if (m.Msg != 0x84 /*WM_NCHITTEST*/ || (int)m.Result != 1 /*HTCLIENT*/ || WindowState == FormWindowState.Maximized || !TopLevel) return;
        var p = PointToClient(new Point((short)((long)m.LParam & 0xFFFF), (short)(((long)m.LParam >> 16) & 0xFFFF)));
        // 테두리: 바깥 얇은 줄은 크기 조절, 그 안쪽은 잡고 옮기기. 모서리는 넓게 크기 조절
        int W = ClientSize.Width, H = ClientSize.Height, rz = S(4), cz = S(14);
        bool l = p.X < rz, r = p.X >= W - rz, t = p.Y < rz, b = p.Y >= H - rz;
        bool cl = p.X < cz, cr = p.X >= W - cz, ct = p.Y < cz, cb = p.Y >= H - cz;
        if (FixedSize) { m.Result = (IntPtr)2; return; } // 크기 고정: 테두리를 잡아도 옮기기만 한다
        bool edgeHit = l || r || t || b;
        int hit = edgeHit && ct && cl ? 13 : edgeHit && ct && cr ? 14 : edgeHit && cb && cl ? 16 : edgeHit && cb && cr ? 17 :
                  l ? 10 : r ? 11 : t ? 12 : b ? 15 : 2 /*HTCAPTION: 테두리 안쪽을 잡으면 창이 옮겨진다*/;
        m.Result = (IntPtr)hit;
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
        alphaSaveTimer.Stop(); alphaSaveTimer.Start(); // 저장은 마지막 변경 뒤 400ms에 한 번
        Lift();
        alphaTip.Show($"투명도 {pct}%", grip, grip.PointToClient(Cursor.Position) + new Size(S(12), S(12)), 900);
    }

    /// <summary>창을 흐리게 하면 뒤 게임 화면과 섞여 영상이 어둡고 탁해 보인다.
    /// 흐린 만큼 영상만 밝기·대비·색을 올려 덜 어둡게 보이게 한다 (글자·사진은 그대로).</summary>
    void Lift()
    {
        if (kind == SiteKind.Music) return;
        double t = 1 - Math.Clamp(Opacity, 0.3, 1); // 0(불투명) ~ 0.7
        string f = t < 0.01 ? "none" : FormattableString.Invariant(
            $"brightness({1 + 0.6 * t:0.###}) contrast({1 + 0.35 * t:0.###}) saturate({1 + 0.5 * t:0.###})");
        try
        {
            _ = web.CoreWebView2?.ExecuteScriptAsync(
                "(()=>{let s=document.getElementById('mbLift');if(!s){s=document.createElement('style');s.id='mbLift';" +
                "(document.head||document.documentElement).appendChild(s)}" +
                $"s.textContent='video{{filter:{f}!important}}'}})()");
        }
        catch { }
    }

    Rectangle? smallBounds;
    /// <summary>크게 보기(펼치기)나 최대화 상태</summary>
    bool Expanded => smallBounds != null || WindowState == FormWindowState.Maximized;
    void ToggleBig()
    {
        if (FixedSize) return;
        if (WindowState != FormWindowState.Normal) WindowState = FormWindowState.Normal;
        if (smallBounds is Rectangle r) { Bounds = r; smallBounds = null; return; }
        smallBounds = Bounds;
        var wa = Screen.FromControl(this).WorkingArea;
        var big = BigSize;
        // 경매장은 크게 보기에서 사이트 원래 화면(1100px)이 다 들어가게 연다
        if (Nexon.IsAuction(web.Source?.ToString() ?? startUrl)) { float d = DeviceDpi / 96f; big = new Size((int)Math.Round(1140 * d), (int)Math.Round(820 * d)); }
        int w = Math.Min(big.Width, wa.Width), h = Math.Min(big.Height, wa.Height);
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
        // 주소 칸에서: Enter는 열기, Esc는 창을 닫지 않고 원래 주소로 되돌린 뒤 페이지로 돌아간다
        if (addr != null && addr.ContainsFocus && (keyData == Keys.Enter || keyData == Keys.Escape))
        {
            if (keyData == Keys.Enter) web.CoreWebView2?.Navigate(Browser.ToUrl(addr.Text));
            else if (web.Source != null) addr.Text = web.Source.ToString();
            web.Focus();
            return true;
        }
        if (keyData == (Keys.Control | Keys.W) || keyData == Keys.Escape) { Close(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>창 위치를 따로 기억하는 단위: 코디·웹툰 같은 종류별, 그 밖의 사이트는 주소(호스트)별.
    /// 숨겨 두는 뮤직 창과 재생 창 옆에 붙는 댓글·채팅 창은 기억하지 않는다</summary>
    static string? PlaceKey(string? url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        var k = KindOf(url);
        if (k is SiteKind.Music or SiteKind.YouTubeComments or SiteKind.LiveChat) return null;
        if (string.Equals(url, Browser.Home, StringComparison.Ordinal)) return "Browser"; // 브라우저는 자기 자리를 따로 기억한다
        if (Nexon.IsAuction(url)) return "Auction"; // 예전에 크게 늘려 둔 자리(site:…) 대신 새 기본 크기로 한 번 다시 시작한다
        if (k == SiteKind.YouTubePlayer) return YouTube.IsShorts(url) ? "Shorts" : "YouTubePlayer";
        if (k != SiteKind.Other) return k.ToString();
        return Uri.TryCreate(url, UriKind.Absolute, out var u) ? "site:" + u.Host.ToLowerInvariant() : null;
    }

    /// <summary>같은 사이트 안에서도 메뉴(주소 첫 칸)마다 자리를 따로 기억한다. 예: site:maplescouter.com/boss</summary>
    static string? PageKey(string? url)
    {
        var key = PlaceKey(url);
        if (key == null || !key.StartsWith("site:") || !Uri.TryCreate(url, UriKind.Absolute, out var u)) return null;
        // 언어 칸(/ko/…)은 건너뛰고 그 다음 칸으로 메뉴를 나눈다
        var segs = u.AbsolutePath.Trim('/').ToLowerInvariant().Split('/');
        int i = segs.Length > 1 && segs[0].Length == 2 ? 1 : 0;
        return key + "/" + segs[i];
    }

    void RememberPlace()
    {
        if (WindowState != FormWindowState.Normal || smallBounds != null || !TopLevel) return; // 크게 보기·최대화 상태, 경매장 안 배율표는 기억하지 않는다
        var key = PlaceKey(startUrl);
        if (key == null) return;
        var place = new[] { Left, Top, Width - widened, Height }; // 배율표로 넓힌 만큼은 빼고 기억한다
        settings.WinPlaces[key] = place;
        if (PageKey(startUrl) is string pk) settings.WinPlaces[pk] = place; // 이 메뉴 창 자리도 따로
        settings.Save();
    }

    // 옮기면 다음 첫 창 위치로 기억한다 (창 종류마다 따로)
    protected override void OnResizeEnd(EventArgs e)
    {
        base.OnResizeEnd(e);
        if (WindowState != FormWindowState.Normal) return;
        settings.WinX = Left; settings.WinY = Top;
        RememberPlace();
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
        // 경매장 창은 ✕를 눌러도 숨기기만 한다. 다시 열면 보던 화면 그대로 보여 준다
        if (TopLevel && !reallyClose && e.CloseReason == CloseReason.UserClosing && Nexon.IsAuction(web.Source?.ToString() ?? startUrl))
        {
            e.Cancel = true;
            RememberPlace();
            settings.Save();
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (kind == SiteKind.Music) { musicTimer.Dispose(); MusicChanged?.Invoke(null); }
        else RememberPlace(); // 닫을 때 자리를 기억해서 다음에 같은 자리에 연다
        open.Remove(this);
        if (wearChild is { IsDisposed: false } wc) wc.Close(); // 경매장 창을 닫으면 안의 배율표도 닫는다 (착용 아이템 지우기)
        // 착용 창을 닫으면 '착용 해보기'로 만든 아이템만 지운다 (다른 환산 창이 있으면 거기서 지우고 다시 읽게 한다.
        // 없으면 다음에 환산 페이지가 열릴 때 WearViewScript가 지운다)
        if (wearWin && !open.Any(w => w.wearWin && !w.IsDisposed))
        {
            var others = open.Where(w => !w.IsDisposed && w.web.CoreWebView2 != null && w.web.Source != null && IsSite(w.web.Source.ToString())).ToList();
            if (others.Count > 0)
                _ = AddThenReload(others[0], Nexon.ClearWearScript,
                    others.Where(w => w.web.Source!.AbsolutePath.TrimEnd('/') is SiteMenu.BossCutPath or "/ko/item").ToList());
        }
        // 미뤄 둔 투명도 저장이 있으면 지금 저장한다
        if (alphaSaveTimer.Enabled) { alphaSaveTimer.Stop(); settings.Save(); }
        alphaSaveTimer.Dispose();
        foreach (var d in owned) d.Dispose();
        owned.Clear();
        alphaTip.Dispose();
        Logins.RefreshSoon(); // 창이 닫히면 로그인 상태를 다시 확인한다
        base.OnFormClosed(e);
    }
}
