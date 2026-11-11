using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;

using static MapleBar.Native;

namespace MapleBar;

/// <summary>
/// 게임 화면 위에 떠 있는 작은 시계 (메이플에는 시계가 없다).
/// 끌어서 옮기면 게임 창 안의 그 자리를 기억하고 게임 창을 따라간다. 오른쪽 버튼으로 모양을 고른다.
/// 눌러도 게임에서 키보드를 빼앗지 않는다.
/// </summary>
sealed class ClockForm : Form
{
    readonly Settings st;
    readonly float sc;
    readonly System.Windows.Forms.Timer tick = new() { Interval = 200 };
    static readonly CultureInfo Ko = new("ko-KR");

    readonly Font bigFont, smallFont, secFont, dateFont;
    Point pos;          // 창 왼쪽 위 (그림자 포함)
    Size size;
    string drawnKey = "";
    bool hover, menuOpen;

    int S(float v) => UI.Scale(v, sc);
    int M => S(14); // 그림자 여백

    public ClockForm(Settings st, float sc)
    {
        this.st = st; this.sc = sc;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Cursor = Cursors.SizeAll;
        bigFont = new Font("Segoe UI Semibold", 36f * sc, GraphicsUnit.Pixel);
        secFont = new Font("Segoe UI Semibold", 16f * sc, GraphicsUnit.Pixel);
        smallFont = new Font("맑은 고딕", 13f * sc, FontStyle.Bold, GraphicsUnit.Pixel);
        dateFont = new Font("맑은 고딕", 12.5f * sc, FontStyle.Bold, GraphicsUnit.Pixel);
        tick.Tick += (_, _) => Redraw();
        BuildMenu();
    }

    /// <summary>보여야 하면 띄우고 게임 창을 따라 자리를 맞춘다. 바의 게임 확인 타이머(0.5초)에서 부른다.</summary>
    public void Sync(bool want, Rectangle? game)
    {
        want &= st.ClockOn;
        if (!want)
        {
            if (Visible) { Hide(); tick.Stop(); }
            return;
        }
        if (!Visible) { using (Draw(DateTime.Now)) { } pos = Home(game); } // 크기를 먼저 알아야 제자리에 처음부터 뜬다
        Redraw(force: !Visible);
        if (dragFrom == null) MoveTo(Home(game));
        if (!tick.Enabled) tick.Start();
    }

    // ── 자리: 게임 창 기준 비율로 기억한다 (해상도나 창 위치가 바뀌어도 같은 곳) ──
    Rectangle? lastGame;

    Point Home(Rectangle? game)
    {
        lastGame = game;
        var area = game ?? Screen.PrimaryScreen!.WorkingArea;
        // 기본: 오른쪽 위 구석
        double fx = st.ClockFX ?? double.NaN, fy = st.ClockFY ?? double.NaN;
        int cx = double.IsNaN(fx) ? area.Right - S(14) - (size.Width - 2 * M) / 2 : area.Left + (int)Math.Round(fx * area.Width);
        int top = double.IsNaN(fy) ? area.Top + S(10) : area.Top + (int)Math.Round(fy * area.Width);
        var p = new Point(cx - size.Width / 2, top - M);
        // 화면 밖으로 나가지 않게
        var scr = Screen.FromPoint(new Point(cx, top)).Bounds;
        p.X = Math.Clamp(p.X, scr.Left - M, Math.Max(scr.Left - M, scr.Right - size.Width + M));
        p.Y = Math.Clamp(p.Y, scr.Top - M, Math.Max(scr.Top - M, scr.Bottom - size.Height + M));
        return p;
    }

    void SaveHome()
    {
        var area = lastGame ?? Screen.PrimaryScreen!.WorkingArea;
        st.ClockFX = (pos.X + size.Width / 2 - area.Left) / (double)area.Width;
        st.ClockFY = (pos.Y + M - area.Top) / (double)area.Width;
        st.Save();
    }

    void MoveTo(Point p)
    {
        if (p == pos) return;
        pos = p;
        Present(null);
    }

    // ── 그리기 ──
    Bitmap? bmp;

    void Redraw(bool force = false)
    {
        var now = DateTime.Now;
        string key = now.ToString(st.ClockSeconds ? "yyyyMMddHHmmss" : "yyyyMMddHHmm") + st.Clock24 + st.ClockDate + st.ClockSeconds + hover;
        if (!force && key == drawnKey) { KeepTop(); return; }
        drawnKey = key;
        var old = bmp;
        bmp = Draw(now);
        old?.Dispose();
        Present(bmp);
    }

    static float Ascent(Font f) => f.Size * f.FontFamily.GetCellAscent(f.Style) / f.FontFamily.GetEmHeight(f.Style);
    static float LineH(Font f) => f.Size * (f.FontFamily.GetCellAscent(f.Style) + f.FontFamily.GetCellDescent(f.Style)) / f.FontFamily.GetEmHeight(f.Style);

    // 유리판 둥근 네모: 왼쪽 주황 띠 · 큰 시각 | 세로선 | 오전/오후(주황) · 날짜
    // 연한 연기색 위에 흰 빛을 얹어 어떤 배경에서도 흰 글씨가 읽히게 한다
    Bitmap Draw(DateTime now)
    {
        bool h24 = st.Clock24, sec = st.ClockSeconds, date = st.ClockDate;
        string hm = now.ToString(h24 ? "H:mm" : "h:mm", Ko);
        string ss = ":" + now.ToString("ss");
        string? top = h24 ? null : (now.Hour < 12 ? "오전" : "오후");
        string? bottom = date ? now.ToString("M월 d일 ddd", Ko) : null;
        if (top == null && bottom == null && date) top = now.ToString("dddd", Ko);
        using var fmt = (StringFormat)StringFormat.GenericTypographic.Clone();
        fmt.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;

        using var probe = new Bitmap(1, 1);
        using var pg = Graphics.FromImage(probe);
        pg.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        float W(string s, Font f) => pg.MeasureString(s, f, PointF.Empty, fmt).Width;

        // 폭은 가장 넓은 값 기준 (분·날짜가 바뀔 때마다 들썩이지 않게)
        float bigW = W(h24 ? "23:59" : "12:59", bigFont);
        float secW = sec ? S(2) + W(":59", secFont) : 0;
        float colW = Math.Max(top == null ? 0 : W("오후", smallFont), bottom == null ? 0 : W("12월 31일 수", dateFont));
        bool col = top != null || bottom != null;
        int stripe = S(4), padL = S(20), padR = S(20), gap = S(14);
        float digitH = bigFont.Size * 0.72f;              // 숫자 실제 높이
        float digitTop = Ascent(bigFont) - digitH;        // 줄 위쪽에서 숫자 윗면까지
        int cardW = (int)Math.Ceiling(padL + bigW + secW + (col ? gap * 2 + Math.Max(1, sc) + colW : 0) + padR);
        int cardH = (int)Math.Ceiling(digitH + S(30));
        size = new Size(cardW + 2 * M, cardH + 2 * M);

        var b = new Bitmap(size.Width, size.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(b);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.Transparent);

        var card = new RectangleF(M, M, cardW, cardH);
        float r = S(14);
        // 부드럽게 퍼지는 그림자
        for (int i = 8; i >= 1; i--)
        {
            var sr = card; sr.Inflate(i * sc, i * sc); sr.Offset(0, S(4));
            using var sp = UI.RoundRect(sr, r + i * sc);
            using var sb = new SolidBrush(Color.FromArgb(9, 20, 10, 40));
            g.FillPath(sb, sp);
        }
        using (var cp = UI.RoundRect(card, r))
        {
            using (var smoke = new SolidBrush(Color.FromArgb(hover ? 175 : 160, 22, 18, 34))) g.FillPath(smoke, cp);
            using (var glass = new LinearGradientBrush(card, Color.FromArgb(hover ? 50 : 40, 255, 255, 255), Color.FromArgb(hover ? 18 : 10, 255, 255, 255), 90f)) g.FillPath(glass, cp);
            // 왼쪽 주황 띠 (판 모서리를 따라 잘린다)
            var keep = g.Clip;
            g.SetClip(cp);
            using (var ob = new LinearGradientBrush(new RectangleF(card.X, card.Y - 1, stripe, card.Height + 2), Color.FromArgb(255, 197, 107), Color.FromArgb(255, 122, 26), 90f))
                g.FillRectangle(ob, card.X, card.Y, stripe, card.Height);
            g.Clip = keep;
            using var edge = new Pen(Color.FromArgb(105, 255, 255, 255), Math.Max(1, sc));
            using var ep = UI.RoundRect(RectangleF.Inflate(card, -0.5f, -0.5f), r - 0.5f); g.DrawPath(edge, ep); // 1px 선을 픽셀 칸 안에
        }
        // 윗면 빛 반사 한 줄
        var hl = RectangleF.Inflate(card, -sc, -sc);
        using (var hp = UI.RoundRect(hl, r - sc))
        using (var hb = new LinearGradientBrush(new RectangleF(hl.X, hl.Y - 1, hl.Width, hl.Height / 2 + 2), Color.FromArgb(90, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
        using (var hpen = new Pen(hb, Math.Max(1, sc)))
            g.DrawPath(hpen, hp);

        void Text(string s, Font f, Color c, float tx, float ty)
        {
            tx = (float)Math.Round(tx); ty = (float)Math.Round(ty); // 픽셀에 맞춰야 글씨가 번지지 않는다
            using var tb = new SolidBrush(c);
            g.DrawString(s, f, tb, tx, ty, fmt);
        }
        // 숫자는 글자 모양 그대로 칠한다: 반투명 판 위에 글자를 찍으면 가장자리가 뿌옇게 번진다.
        // 얇은 어두운 테두리를 먼저 둘러 밝은 배경 위에서도 또렷하게 한다.
        void Digits(string s, Font f, Color c, float tx, float ty)
        {
            using var p = new GraphicsPath();
            p.AddString(s, f.FontFamily, (int)f.Style, f.Size, new PointF((float)Math.Round(tx), (float)Math.Round(ty)), fmt);
            using (var rim = new Pen(Color.FromArgb(120, 8, 4, 18), Math.Max(2f, 2.5f * sc)) { LineJoin = LineJoin.Round })
                g.DrawPath(rim, p);
            using var tb = new SolidBrush(c);
            g.FillPath(tb, p);
        }
        float x = card.Left + padL, y = card.Top + (cardH - digitH) / 2f, baseY = y + digitH;
        Digits(hm, bigFont, Color.White, x, y - digitTop);
        float tx = x + W(hm, bigFont);
        if (sec) { tx += S(2); Digits(ss, secFont, Color.FromArgb(235, 255, 255, 255), tx, baseY - Ascent(secFont) + S(1)); }
        if (col)
        {
            float lx = (float)Math.Round(x + bigW + secW + gap) + 0.5f;
            using (var lp = new Pen(Color.FromArgb(100, 255, 255, 255), Math.Max(1, sc)))
                g.DrawLine(lp, lx, card.Top + S(13), lx, card.Bottom - S(13));
            float cx = lx + Math.Max(1, sc) + gap;
            var orange = Color.FromArgb(255, 197, 107);
            float th = top == null ? 0 : LineH(smallFont), bh = bottom == null ? 0 : LineH(dateFont);
            float cy = card.Top + (cardH - th - bh - (top != null && bottom != null ? S(1) : 0)) / 2f;
            if (top != null) { Text(top, smallFont, h24 ? Color.FromArgb(215, 255, 255, 255) : orange, cx, cy); cy += th + S(1); }
            if (bottom != null) Text(bottom, dateFont, Color.FromArgb(245, 255, 255, 255), cx, cy);
        }
        return b;
    }

    // ── 화면에 올리기 (반투명 가장자리까지) ──
    void Present(Bitmap? fresh)
    {
        if (!Visible) { Show(); }
        if (!IsHandleCreated) return;
        var src = fresh ?? bmp;
        IntPtr screen = GetDC(IntPtr.Zero), mem = CreateCompatibleDC(screen), hb = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
            var dst = new POINT { x = pos.X, y = pos.Y };
            var blend = new BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
            if (src == null) { UpdateLayeredWindow(Handle, IntPtr.Zero, ref dst, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, ref blend, 2); return; }
            if (fresh == null)
            {
                // 그림은 그대로, 자리만 옮긴다
                UpdateLayeredWindow(Handle, IntPtr.Zero, ref dst, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, ref blend, 2);
                return;
            }
            hb = src.GetHbitmap(Color.FromArgb(0));
            old = SelectObject(mem, hb);
            var sz = new SIZE { cx = src.Width, cy = src.Height };
            var sp = new POINT();
            UpdateLayeredWindow(Handle, screen, ref dst, ref sz, mem, ref sp, 0, ref blend, 2);
        }
        finally
        {
            if (old != IntPtr.Zero) SelectObject(mem, old);
            if (hb != IntPtr.Zero) DeleteObject(hb);
            DeleteDC(mem);
            ReleaseDC(IntPtr.Zero, screen);
            KeepTop();
        }
    }

    void KeepTop()
    {
        if (menuOpen || !IsHandleCreated || !Visible) return;
        SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_KEEP | SWP_NOOWNERZORDER);
    }

    // ── 끌어서 옮기기 ──
    Point? dragFrom;
    Point dragStart;

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left) { dragFrom = Cursor.Position; dragStart = pos; }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (dragFrom is Point f && MouseButtons == MouseButtons.Left)
            MoveTo(new Point(dragStart.X + Cursor.Position.X - f.X, dragStart.Y + Cursor.Position.Y - f.Y));
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (dragFrom == null) return;
        dragFrom = null;
        if (pos != dragStart) SaveHome();
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Redraw(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Redraw(); }

    // ── 오른쪽 버튼 메뉴 ──
    void BuildMenu()
    {
        var menu = UI.Rounded(new ContextMenuStrip { Font = new Font("맑은 고딕", 9.5f * UI.Z), ShowImageMargin = false, ShowCheckMargin = true });
        ToolStripMenuItem Check(string text, Func<bool> get, Action<bool> set)
        {
            var it = new ToolStripMenuItem(text) { CheckOnClick = true };
            it.CheckedChanged += (_, _) => { if (it.Checked == get()) return; set(it.Checked); st.Save(); Redraw(force: true); MoveTo(Home(lastGame)); };
            menu.Opening += (_, _) => it.Checked = get();
            return it;
        }
        menu.Items.Add(Check("초 보기", () => st.ClockSeconds, v => st.ClockSeconds = v));
        menu.Items.Add(Check("날짜 보기", () => st.ClockDate, v => st.ClockDate = v));
        menu.Items.Add(Check("24시간으로 보기", () => st.Clock24, v => st.Clock24 = v));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("처음 자리로", null, (_, _) => { st.ClockFX = st.ClockFY = null; st.Save(); MoveTo(Home(lastGame)); });
        menu.Items.Add("시계 숨기기", null, (_, _) => { st.ClockOn = false; st.Save(); Sync(false, null); });
        menu.Opened += (_, _) => menuOpen = true;
        menu.Closed += (_, _) => menuOpen = false;
        ContextMenuStrip = menu;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
            return cp;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            tick.Dispose(); bmp?.Dispose();
            bigFont.Dispose(); smallFont.Dispose(); secFont.Dispose(); dateFont.Dispose();
        }
        base.Dispose(disposing);
    }

    [StructLayout(LayoutKind.Sequential)] struct POINT { public int x, y; }
    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
    [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
    [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, IntPtr psize, IntPtr hdcSrc, IntPtr pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
}
