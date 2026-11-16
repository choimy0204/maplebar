using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace MapleBar;

/// <summary>바와 사이트 창에서 같이 쓰는 작은 UI 도우미.</summary>
public static class UI
{
    static Icon? appIcon;
    static bool appIconTried;

    /// <summary>프로그램 아이콘. 창마다 새로 꺼내면 닫아도 풀리지 않고 쌓이므로 한 번만 꺼내 함께 쓴다.</summary>
    public static Icon? AppIcon
    {
        get
        {
            if (appIconTried) return appIcon;
            appIconTried = true;
            try { appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch (Exception ex) { Log.Write("UI", "프로그램 아이콘을 못 불러옴", ex); }
            return appIcon;
        }
    }

    /// <summary>모니터 해상도에 맞춘 배율. 가로 1920(배율 100% 기준)이면 1, 더 좁거나 넓으면 그만큼 줄이거나 키운다.
    /// 크기(S)와 글꼴 크기에 같이 곱한다.</summary>
    public static float Z = 1f;
    /// <summary>배율을 곱해 반올림한 픽셀 수. 창마다 있는 S()가 이걸 부른다.</summary>
    public static int Scale(float v, float sc) => (int)Math.Round(v * sc);

    public static float FitZoom(Screen s, int dpi)
    {
        float logical = s.WorkingArea.Width / (dpi / 96f);
        return Balance(logical / 1920f);
    }

    /// <summary>
    /// 화면 폭 비율(1920 = 1)을 바·창 크기로 바꾼다. 1920보다 크면 그대로 키우고,
    /// 작으면 덜 줄인다: 메이플 UI는 해상도를 낮춰도 크기가 그대로라, 바만 정비례로 줄면 글자가 너무 작아진다.
    /// 1366 → 0.84 · 1280 → 0.82 · 1024 → 0.74 (예전엔 0.71 · 0.67 · 0.53)
    /// </summary>
    public static float Balance(float ratio) =>
        ratio >= 1f ? Math.Min(ratio, 1.5f) : Math.Max(0.7f, 0.45f + 0.55f * ratio);

    /// <summary>다크 모드. 바를 만들 때 설정에서 읽는다. 창은 만들 때의 색을 쓰므로 바꾸면 바와 작은 창을 다시 만든다.</summary>
    public static bool Dark;

    // 다크 모드: 차콜 바탕 · 쨍한 주황 대신 부드러운 호박색 강조
    public static Color Accent => Dark ? Color.FromArgb(242, 166, 90) : Color.FromArgb(255, 138, 0);
    public static Color Text => Dark ? Color.FromArgb(232, 232, 234) : Color.FromArgb(31, 31, 31);
    public static Color Line => Dark ? Color.FromArgb(76, 82, 90) : Color.FromArgb(226, 226, 226);
    public static Color HoverBack => Dark ? Color.FromArgb(64, 70, 77) : Color.FromArgb(242, 242, 242);
    /// <summary>연한 강조 바탕 위 글씨 (다크에서는 조금 더 밝은 호박색)</summary>
    public static Color AccentText => Dark ? Color.FromArgb(242, 184, 119) : Accent;
    public static Color SoftAccent => Dark ? Color.FromArgb(74, 58, 42) : Color.FromArgb(255, 243, 224);
    /// <summary>창·카드 바탕 (밝은 모드에서는 흰색)</summary>
    public static Color Back => Dark ? Color.FromArgb(49, 54, 60) : Color.White;
    /// <summary>흐린 글자 (설명·꺼진 값)</summary>
    public static Color Sub => Dark ? Color.FromArgb(178, 180, 188) : Color.FromArgb(135, 135, 135);

    // ── 프리텐다드 글꼴 (실행 파일 옆 Fonts 폴더) ──
    // GDI+ 글꼴 개체는 PrivateFontCollection으로, Label이 쓰는 GDI 그리기는 AddFontResourceEx(이 프로그램 안에서만)로 알려 준다
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern int AddFontResourceEx(string name, uint fl, IntPtr res);
    static System.Drawing.Text.PrivateFontCollection? pretendard;
    static bool pretendardTried;

    static FontFamily? PretendardFamily()
    {
        if (!pretendardTried)
        {
            pretendardTried = true;
            try
            {
                var dir = Path.Combine(AppContext.BaseDirectory, "Fonts");
                var files = new[] { "Pretendard-Regular.ttf", "Pretendard-Bold.ttf" }.Select(f => Path.Combine(dir, f)).Where(File.Exists).ToList();
                if (files.Count > 0)
                {
                    var pfc = new System.Drawing.Text.PrivateFontCollection();
                    foreach (var f in files) { pfc.AddFontFile(f); AddFontResourceEx(f, 0x10 /*FR_PRIVATE*/, IntPtr.Zero); }
                    pretendard = pfc; // 앱이 끝날 때까지 쓴다
                }
            }
            catch (Exception ex) { Log.Write("ui", "프리텐다드 글꼴을 못 불러옴", ex); }
        }
        return pretendard?.Families.FirstOrDefault();
    }

    /// <summary>프리텐다드 글꼴. 없으면 맑은 고딕. 만든 쪽에서 Dispose한다.</summary>
    public static Font Pretendard(float size, FontStyle style = FontStyle.Regular)
    {
        var fam = PretendardFamily();
        if (fam != null && fam.IsStyleAvailable(style)) return new Font(fam, size, style);
        return new Font("맑은 고딕", size, style);
    }

    /// <summary>
    /// 밝은 모드 기준으로 정한 색을 지금 모드에 맞게 바꾼다. 밝은 모드면 그대로 돌려준다.
    /// 다크 모드에서는 흰색·회색·옅은 바탕색처럼 밝기가 중요한 색은 밝기를 뒤집고,
    /// 주황·파랑 같은 뚜렷한 색은 그대로 둔다.
    /// </summary>
    public static Color T(Color c)
    {
        if (!Dark || c.A == 0) return c;
        float h = c.GetHue(), s = c.GetSaturation(), l = c.GetBrightness();
        bool vivid = s >= 0.25f && l > 0.3f && l < 0.78f;
        if (vivid) return c;
        // 흰색(1) → 어두운 바탕, 거의 검정(0.12) → 밝은 글자
        float nl = 0.2f + (1 - l) * 0.79f;
        if (l < 0.62f) nl = Math.Max(nl, 0.68f); // 회색 설명 글씨는 차콜 바탕에서 묻히지 않게 더 밝게
        float ns = l > 0.78f ? s * 0.45f : s; // 옅은 색 바탕은 어두워지면 너무 진해 보여서 채도를 낮춘다
        if (nl < 0.5f && ns < 0.1f) { h = 210; ns = 0.1f; } // 어두운 회색 바탕은 살짝 푸른 차콜색으로
        return FromHsl(c.A, h, ns, Math.Clamp(nl, 0, 1));
    }

    static Color FromHsl(int a, float h, float s, float l)
    {
        float q = l < 0.5f ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
        float hk = h / 360f;
        int Ch(float t)
        {
            if (t < 0) t += 1; if (t > 1) t -= 1;
            float v = t < 1 / 6f ? p + (q - p) * 6 * t : t < 0.5f ? q : t < 2 / 3f ? p + (q - p) * (2 / 3f - t) * 6 : p;
            return Math.Clamp((int)Math.Round(v * 255), 0, 255);
        }
        return s == 0 ? Color.FromArgb(a, (int)Math.Round(l * 255), (int)Math.Round(l * 255), (int)Math.Round(l * 255))
                      : Color.FromArgb(a, Ch(hk + 1 / 3f), Ch(hk), Ch(hk - 1 / 3f));
    }

    [DllImport("dwmapi.dll", EntryPoint = "DwmSetWindowAttribute")] static extern int DwmSetBool(IntPtr h, int attr, ref int val, int size);
    /// <summary>테두리가 있는 창의 제목줄을 다크 모드에 맞춘다.</summary>
    public static void DarkTitle(IntPtr handle)
    {
        if (!Dark) return;
        int on = 1;
        try { DwmSetBool(handle, 20, ref on, sizeof(int)); }
        catch (Exception ex) { Log.Write("UI", "제목줄을 어둡게 못 바꿈", ex); }
    }

    static readonly ToolTip tip = new();

    /// <summary>둥근 알약 모양 버튼. 마우스를 올리면 배경이 바뀐다.</summary>
    public static RoundLabel Button(string text, string tooltip, Action onClick, float sc,
        Color? fore = null, Color? back = null)
    {
        var normal = back ?? Color.Transparent;
        var lbl = new RoundLabel
        {
            Text = text,
            AutoSize = true,
            UseMnemonic = false, // '&'를 그대로 보이게
            Cursor = Cursors.Hand,
            ForeColor = fore ?? Text,
            Fill = normal,
            Padding = new Padding((int)(10 * sc), (int)(5 * sc), (int)(10 * sc), (int)(5 * sc)),
            Margin = new Padding((int)(2 * sc), 0, (int)(2 * sc), 0),
        };
        lbl.MouseEnter += (_, _) => lbl.Fill = back.HasValue ? ControlPaint.Dark(back.Value, 0.05f) : HoverBack;
        lbl.MouseLeave += (_, _) => lbl.Fill = normal;
        lbl.Click += (_, _) => onClick();
        if (!string.IsNullOrEmpty(tooltip)) tip.SetToolTip(lbl, tooltip);
        return lbl;
    }

    /// <summary>FlowLayoutPanel 안의 컨트롤을 세로 가운데에 놓는다.</summary>
    public static void CenterV(Control c, int rowHeight)
    {
        var m = c.Margin;
        int h = c.AutoSize ? c.PreferredSize.Height : c.Height;
        c.Margin = new Padding(m.Left, Math.Max(0, (rowHeight - h) / 2), m.Right, 0);
    }

    public static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var p = new GraphicsPath();
        if (d <= 0) { p.AddRectangle(r); return p; }
        p.AddArc(r.Left, r.Top, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    // ── 윈도우 11 둥근 모서리 (창·펼침 메뉴) ──
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);

    public static void RoundCorners(IntPtr handle, bool small = false)
    {
        int pref = small ? 3 : 2; // 3 = 작게 둥글게, 2 = 둥글게
        try { DwmSetWindowAttribute(handle, 33, ref pref, sizeof(int)); }
        catch (Exception ex) { Log.Write("UI", "창 모서리를 둥글게 못 바꿈", ex); }
    }

    /// <summary>펼침 메뉴(하위 메뉴 포함)를 둥근 모양으로 만든다.</summary>
    public static T Rounded<T>(T menu) where T : ToolStripDropDown
    {
        void Hook(ToolStripDropDown d)
        {
            if (d.Tag as string == "round") return;
            d.Tag = "round";
            d.Padding = new Padding(4, 6, 4, 6);
            if (d.IsHandleCreated) RoundCorners(d.Handle);
            d.HandleCreated += (_, _) => RoundCorners(d.Handle);
            d.Opening += (_, _) =>
            {
                foreach (var it in d.Items.OfType<ToolStripMenuItem>())
                {
                    it.Padding = new Padding(2, 3, 2, 3);
                    if (it.HasDropDownItems) Hook(it.DropDown);
                }
            };
        }
        Hook(menu);
        return menu;
    }
}

/// <summary>배경을 둥글게 칠하는 글자 버튼.</summary>
public class RoundLabel : Label
{
    Color fill = Color.Transparent;
    public Color Fill { get => fill; set { fill = value; Invalidate(); } }
    /// <summary>0이면 높이의 1/4 (둥근 직사각형).</summary>
    public int Radius { get; set; }

    public RoundLabel()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        if (fill.A == 0) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        // 기본은 알약 모양 대신 모서리만 둥근 직사각형
        using var path = UI.RoundRect(new RectangleF(0, 0, Width - 0.5f, Height - 0.5f), Radius > 0 ? Radius : Height * 0.25f);
        using var b = new SolidBrush(fill);
        e.Graphics.FillPath(b, path);
    }
}

/// <summary>흰 바탕, 둥근 강조 칸, 주황 체크를 쓰는 메뉴 그리기.</summary>
public class RoundMenuRenderer : ToolStripProfessionalRenderer
{
    public RoundMenuRenderer() { RoundedEdges = true; }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) =>
        e.Graphics.Clear(UI.Back);

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { } // 테두리는 윈도우가 둥글게 그린다

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(3, 1, e.Item.Width - 6, e.Item.Height - 2);
        using var path = UI.RoundRect(r, 7);
        using var b = new SolidBrush(UI.SoftAccent);
        e.Graphics.FillPath(b, path);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        if (e.Item.Enabled) e.TextColor = e.Item.Selected ? (UI.Dark ? UI.AccentText : Color.FromArgb(200, 95, 0)) : UI.Text;
        else e.TextColor = UI.Sub;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        int y = e.Item.Height / 2;
        using var pen = new Pen(UI.Line);
        e.Graphics.DrawLine(pen, 10, y, e.Item.Width - 10, y);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var r = e.ImageRectangle;
        using var pen = new Pen(UI.Accent, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        e.Graphics.DrawLines(pen, new[]
        {
            new PointF(r.Left + r.Width * 0.2f, r.Top + r.Height * 0.52f),
            new PointF(r.Left + r.Width * 0.42f, r.Top + r.Height * 0.74f),
            new PointF(r.Left + r.Width * 0.82f, r.Top + r.Height * 0.3f),
        });
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = UI.Sub;
        base.OnRenderArrow(e);
    }
}

/// <summary>카드형 메뉴 칸의 모양: 아이콘 글자, 아래 설명, 오른쪽 단축키</summary>
public sealed record MenuLook(string Glyph, string Sub, string? Key = null);

/// <summary>톱니 메뉴처럼 칸마다 아이콘과 짧은 설명을 붙여 그리는 메뉴.</summary>
public sealed class CardMenuRenderer : RoundMenuRenderer, IDisposable
{
    public void Dispose() { title.Dispose(); sub.Dispose(); icon.Dispose(); }

    readonly float sc;
    readonly Font title, sub, icon;

    public CardMenuRenderer(float sc)
    {
        this.sc = sc;
        title = new Font("맑은 고딕", 10f * UI.Z, FontStyle.Bold);
        sub = new Font("맑은 고딕", 8.5f * UI.Z);
        // 윈도우 11은 Segoe Fluent Icons, 10은 Segoe MDL2 Assets에 같은 아이콘이 있다
        string fam = FontFamily.Families.Any(f => f.Name == "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
        icon = new Font(fam, 11f * UI.Z);
    }

    int S(float v) => UI.Scale(v, sc);

    /// <summary>카드 칸 하나를 만든다</summary>
    public ToolStripItem Item(string glyph, string text, string desc, Action onClick, string? key = null)
    {
        var size = new Size(S(270), S(50));
        var it = new CardItem(size) { Text = text, AutoSize = false, Size = size, Tag = new MenuLook(glyph, desc, key), DisplayStyle = ToolStripItemDisplayStyle.Text };
        it.Click += (_, _) => { (it.Owner as ToolStripDropDown)?.Close(ToolStripDropDownCloseReason.ItemClicked); onClick(); };
        return it;
    }

    // 메뉴 항목(ToolStripMenuItem)은 메뉴가 너비를 글자 길이로 다시 재서 오른쪽이 잘린다.
    // 버튼 칸은 정해 둔 크기를 그대로 쓴다
    sealed class CardItem : ToolStripButton
    {
        readonly Size fixedSize;
        public CardItem(Size s) { fixedSize = s; }
        public override Size GetPreferredSize(Size constrainingSize) => fixedSize;
    }

    protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
    {
        if (e.Item.Tag is not MenuLook) { base.OnRenderButtonBackground(e); return; }
        OnRenderMenuItemBackground(e);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = UI.RoundRect(new RectangleF(S(3), S(1), e.Item.Width - S(6), e.Item.Height - S(2)), S(8));
        using var b = new SolidBrush(UI.HoverBack);
        e.Graphics.FillPath(b, path);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        if (e.Item.Tag is not MenuLook look) { base.OnRenderItemText(e); return; }
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int h = e.Item.Height;
        bool hot = e.Item.Selected;

        // 왼쪽 아이콘 칸 (둥근 직사각형)
        int box = S(32), x = S(10), y = (h - box) / 2;
        using (var path = UI.RoundRect(new RectangleF(x, y, box, box), S(8)))
        // 다크 모드: 갈색 칸 대신 조금 밝은 차콜 칸에 호박색 아이콘
        using (var b = new SolidBrush(hot ? UI.Accent : UI.Dark ? Color.FromArgb(80, 86, 94) : UI.SoftAccent))
            g.FillPath(b, path);
        TextRenderer.DrawText(g, look.Glyph, icon, new Rectangle(x, y, box, box),
            hot ? Color.White : UI.AccentText,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        // 제목과 설명
        int tx = x + box + S(12);
        int th = TextRenderer.MeasureText(g, "가", title, Size.Empty, TextFormatFlags.NoPadding).Height;
        int sh = TextRenderer.MeasureText(g, "가", sub, Size.Empty, TextFormatFlags.NoPadding).Height;
        int ty = (h - th - sh - S(2)) / 2;
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        TextRenderer.DrawText(g, e.Item.Text, title, new Point(tx, ty), UI.Text, flags);
        TextRenderer.DrawText(g, look.Sub, sub, new Point(tx, ty + th + S(2)), UI.Sub, flags);

        // 오른쪽 단축키
        if (look.Key != null)
        {
            var size = TextRenderer.MeasureText(g, look.Key, sub, Size.Empty, flags);
            var r = new Rectangle(e.Item.Width - S(12) - size.Width - S(12), (h - size.Height - S(6)) / 2, size.Width + S(12), size.Height + S(6));
            using (var path = UI.RoundRect(r, S(6)))
            using (var pen = new Pen(UI.Line))
                g.DrawPath(pen, path);
            TextRenderer.DrawText(g, look.Key, sub, r, UI.Sub, flags | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}
