using System.Drawing.Drawing2D;

using static MapleBar.Native;

namespace MapleBar;

/// <summary>
/// 메애기 화면에 보이는 곡 썸네일을 받아 두는 곳. 페이지에 나온 주소만 쓰고, 못 받으면 ♪로 대신한다.
/// </summary>
static class Thumbs
{
    static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(10) };
    static readonly Dictionary<string, Image?> cache = new();
    static readonly HashSet<string> loading = new();
    static readonly SemaphoreSlim gate = new(4); // 한 번에 4개까지만 받는다

    /// <summary>받아 둔 그림을 준다. 아직 없으면 받기 시작하고, 다 받으면 owner를 다시 그린다.</summary>
    public static Image? Get(string? url, Control owner)
    {
        if (string.IsNullOrEmpty(url) || !url.StartsWith("http")) return null;
        lock (cache)
        {
            if (cache.TryGetValue(url, out var img)) return img;
            if (!loading.Add(url)) return null;
        }
        _ = LoadAsync(url, owner);
        return null;
    }

    static async Task LoadAsync(string url, Control owner)
    {
        Image? img = null;
        await gate.WaitAsync();
        try
        {
            var bytes = await http.GetByteArrayAsync(url);
            using var ms = new MemoryStream(bytes);
            using var raw = Image.FromStream(ms);
            img = new Bitmap(raw, new Size(64, 64 * raw.Height / Math.Max(1, raw.Width)));
        }
        catch { } // 받지 못하거나 읽을 수 없는 형식이면 ♪로 둔다
        finally { gate.Release(); }
        lock (cache) { cache[url] = img; loading.Remove(url); }
        if (img != null && !owner.IsDisposed && owner.IsHandleCreated)
            try { owner.BeginInvoke(owner.Invalidate); } catch { }
    }

    /// <summary>둥근 네모 안에 썸네일을 꽉 채워 그린다.</summary>
    public static void Draw(Graphics g, Image img, RectangleF r, float radius)
    {
        using var path = UI.RoundRect(r, radius);
        var old = g.Clip;
        g.SetClip(path, CombineMode.Intersect);
        float s = Math.Max(r.Width / img.Width, r.Height / img.Height);
        float w = img.Width * s, h = img.Height * s;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(img, r.Left + (r.Width - w) / 2, r.Top + (r.Height - h) / 2, w, h);
        g.Clip = old; // Graphics가 복사해 가므로 원래 것은 치운다
        old.Dispose();
    }
}

/// <summary>
/// 바 안의 뮤직 칸: ♪ 곡 제목, 진행 바, ⏮ ⏯ ⏭. 아이콘은 글꼴 대신 직접 그려 또렷하게 보인다.
/// 제목을 누르면 곡 목록을 연다.
/// </summary>
public sealed class MusicPill : Control
{
    readonly float sc;
    readonly Font titleFont = new("맑은 고딕", 9.5f, FontStyle.Bold);
    readonly Font noteFont = new("Segoe UI Symbol", 11f, FontStyle.Bold);
    readonly ToolTip tip = new();
    WebWindow.MusicInfo? info;
    public WebWindow.MusicInfo? Info => info;
    int hot = -1; // 0 이전, 1 재생, 2 다음, 3 제목
    public event Action? TitleClick;

    public MusicPill(float sc)
    {
        this.sc = sc;
        DoubleBuffered = true;
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Size = new Size(S(392), S(36));
    }

    int S(int v) => UI.Scale(v, sc);
    // 버튼 순서(왼→오): 랜덤(4) 이전(0) 재생(1) 다음(2) 반복(5)
    static readonly int[] Order = { 0, 1, 2, 4, 5 }; // 이전·재생·다음 · 랜덤·한 곡 반복
    Rectangle Btn(int i)
    {
        int w = S(30), slot = Array.IndexOf(Order, i);
        return new Rectangle(Width - S(6) - (Order.Length - slot) * w, (Height - S(30)) / 2 - S(1), w, S(28));
    }
    Rectangle TitleRect => new(S(8), 0, Btn(Order[0]).Left - S(10), Height - S(6));

    public void SetInfo(WebWindow.MusicInfo m)
    {
        bool titleChanged = info?.Title != m.Title || info?.Sub != m.Sub;
        info = m;
        dial?.SetValue(m.Volume);
        if (titleChanged) tip.SetToolTip(this, $"{m.Title}\n{m.Sub}\n누르면 곡 목록 · 끌면 꺼내기 · 오른쪽 클릭: 메뉴");
        Invalidate();
    }

    static double Progress(string time)
    {
        // "01:23 / 03:45"
        var parts = time.Split('/');
        if (parts.Length != 2) return 0;
        double Sec(string s)
        {
            double t = 0;
            foreach (var p in s.Trim().Split(':')) t = t * 60 + (double.TryParse(p, out var v) ? v : 0);
            return t;
        }
        double a = Sec(parts[0]), b = Sec(parts[1]);
        return b > 0 ? Math.Clamp(a / b, 0, 1) : 0;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (ghost) return;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var box = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (var path = UI.RoundRect(box, Height / 2f))
        {
            using (var b = new SolidBrush(UI.SoftAccent)) g.FillPath(b, path);
            using (var pen = new Pen(Color.FromArgb(255, 214, 160))) g.DrawPath(pen, path);
        }

        // ♪ + 제목
        var tr = TitleRect;
        if (hot == 3) using (var b = new SolidBrush(Color.FromArgb(40, UI.Accent)))
            using (var p = UI.RoundRect(new RectangleF(tr.Left - S(2), S(4), tr.Width + S(4), Height - S(10)), S(8))) g.FillPath(b, p);
        // 썸네일 (없으면 ♪)
        var thumb = Thumbs.Get(info?.Img, this);
        var tb = new RectangleF(tr.Left + S(2), (Height - S(26)) / 2f, S(26), S(26));
        if (thumb != null) Thumbs.Draw(g, thumb, tb, S(7));
        else TextRenderer.DrawText(g, "♪", noteFont, Rectangle.Round(tb), UI.Accent,
            TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
        var textR = new Rectangle(tr.Left + S(34), 0, tr.Width - S(36), tr.Height);
        TextRenderer.DrawText(g, info?.Title ?? "곡을 고르세요", titleFont, textR, UI.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

        // 진행 바 (제목 아래 얇은 줄)
        var bar = new RectangleF(textR.Left, Height - S(10), textR.Width, S(3));
        using (var b = new SolidBrush(Color.FromArgb(255, 222, 186))) using (var p = UI.RoundRect(bar, bar.Height / 2)) g.FillPath(b, p);
        double pr = info != null ? Progress(info.Time) : 0;
        if (pr > 0)
        {
            var fill = new RectangleF(bar.Left, bar.Top, Math.Max(bar.Height, (float)(bar.Width * pr)), bar.Height);
            using var b = new SolidBrush(UI.Accent); using var p = UI.RoundRect(fill, bar.Height / 2); g.FillPath(b, p);
        }

        // 버튼
        foreach (int i in Order)
        {
            var r = Btn(i);
            bool on = (i == 4 && info?.Shuffle == true) || (i == 5 && info?.Repeat == true);
            if (on && hot != i)
                using (var b = new SolidBrush(Color.FromArgb(36, UI.Accent))) g.FillEllipse(b, r.Left + (r.Width - S(26)) / 2f, r.Top + (r.Height - S(26)) / 2f, S(26), S(26));
            if (i == 1)
                using (var b = new SolidBrush(hot == 1 ? ControlPaint.Dark(UI.Accent, 0.05f) : UI.Accent)) g.FillEllipse(b, r.Left + (r.Width - S(26)) / 2f, r.Top + (r.Height - S(26)) / 2f, S(26), S(26));
            else if (hot == i)
                using (var b = new SolidBrush(Color.FromArgb(50, UI.Accent))) g.FillEllipse(b, r.Left + (r.Width - S(26)) / 2f, r.Top + (r.Height - S(26)) / 2f, S(26), S(26));
            DrawIcon(g, i, r);
        }
    }

    void DrawIcon(Graphics g, int i, Rectangle r)
    {
        float cx = r.Left + r.Width / 2f, cy = r.Top + r.Height / 2f, u = S(5);
        if (i >= 4) { DrawModeIcon(g, i, cx, cy, u); return; }
        var col = i == 1 ? Color.White : Color.FromArgb(70, 50, 30);
        using var b = new SolidBrush(col);
        if (i == 1)
        {
            if (info?.Playing == true)
            {
                g.FillRectangle(b, cx - u * 0.9f, cy - u, u * 0.65f, u * 2);
                g.FillRectangle(b, cx + u * 0.25f, cy - u, u * 0.65f, u * 2);
            }
            else g.FillPolygon(b, new[] { new PointF(cx - u * 0.7f, cy - u * 1.05f), new PointF(cx - u * 0.7f, cy + u * 1.05f), new PointF(cx + u * 1.1f, cy) });
            return;
        }
        float d = i == 0 ? -1 : 1; // 왼쪽(이전) / 오른쪽(다음)
        g.FillPolygon(b, new[] { new PointF(cx - d * u * 0.8f, cy - u), new PointF(cx - d * u * 0.8f, cy + u), new PointF(cx + d * u * 0.6f, cy) });
        g.FillRectangle(b, d > 0 ? cx + u * 0.6f : cx - u * 0.6f - u * 0.4f, cy - u, u * 0.4f, u * 2);
    }

    // 랜덤(교차 화살표) / 반복(돌아오는 화살표 + 1). 켜져 있으면 주황, 꺼져 있으면 회색
    Font? oneFont; // 한 곡 반복 아이콘의 "1". 배율(UI.Z)이 바뀌면 다시 만든다

    void DrawModeIcon(Graphics g, int i, float cx, float cy, float u)
    {
        bool on = i == 4 ? info?.Shuffle == true : info?.Repeat == true;
        var col = on ? UI.Accent : Color.FromArgb(165, 150, 135);
        using var pen = new Pen(col, S(2)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var b = new SolidBrush(col);
        float l = cx - u * 1.2f, rt = cx + u * 1.2f, t = cy - u * 0.8f, bt = cy + u * 0.8f, a = u * 0.55f;
        if (i == 4)
        {
            g.DrawBezier(pen, l, t, cx - u * 0.2f, t, cx + u * 0.2f, bt, rt - a * 0.6f, bt);
            g.DrawBezier(pen, l, bt, cx - u * 0.2f, bt, cx + u * 0.2f, t, rt - a * 0.6f, t);
            foreach (float y in new[] { t, bt })
                g.FillPolygon(b, new[] { new PointF(rt + a * 0.4f, y), new PointF(rt - a * 0.7f, y - a), new PointF(rt - a * 0.7f, y + a) });
            return;
        }
        // 한 곡 반복: 돌아가는 고리 (위는 오른쪽, 아래는 왼쪽 화살촉) + 가운데 굵은 1
        var ring = new RectangleF(l, t, rt - l, bt - t);
        using (var p = UI.RoundRect(ring, (bt - t) / 2)) g.DrawPath(pen, p);
        g.FillPolygon(b, new[] { new PointF(cx + a * 1.3f, t), new PointF(cx - a * 0.2f, t - a), new PointF(cx - a * 0.2f, t + a) });
        g.FillPolygon(b, new[] { new PointF(cx - a * 1.3f, bt), new PointF(cx + a * 0.2f, bt - a), new PointF(cx + a * 0.2f, bt + a) });
        if (oneFont == null || oneFont.Size != 7f * UI.Z) { oneFont?.Dispose(); oneFont = new Font("Segoe UI", 7f * UI.Z, FontStyle.Bold); }
        TextRenderer.DrawText(g, "1", oneFont, new Rectangle((int)(cx - u), (int)(cy - u), (int)(u * 2), (int)(u * 2)), col,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    int HitTest(Point p)
    {
        foreach (int i in Order) if (Btn(i).Contains(p)) return i;
        return TitleRect.Contains(p) ? 3 : -1;
    }

    // 끌어서 꺼내기: 누른 채로 조금 움직이면 클릭 대신 DragStart(잡은 자리)를 알린다
    public event Action<Point>? DragStart;
    Point? downAt;
    bool dragged;
    bool ghost;
    /// <summary>true면 자리만 차지하고 그리지 않는다 (다시 넣는 효과가 끝날 때까지).</summary>
    public bool Ghost { get => ghost; set { ghost = value; Invalidate(); } }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left) { downAt = e.Location; dragged = false; }
    }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); downAt = null; }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (downAt is Point d && e.Button == MouseButtons.Left && !dragged &&
            (Math.Abs(e.X - d.X) > S(5) || Math.Abs(e.Y - d.Y) > S(5)))
        {
            dragged = true; downAt = null;
            dial?.Hide(); tip.Hide(this);
            hot = -1; Invalidate();
            Capture = false;
            DragStart?.Invoke(d);
            return;
        }
        int h = HitTest(e.Location);
        if (h != hot)
        {
            hot = h; Invalidate();
            // 버튼에 올리면 무슨 버튼인지 알려 준다
            string text = h switch
            {
                0 => "이전 곡",
                1 => info?.Playing == true ? "일시정지" : "재생",
                2 => "다음 곡",
                4 => info?.Shuffle == true ? "랜덤 재생 켜짐 (누르면 끄기)" : "랜덤 재생 (누르면 켜기)",
                5 => info?.Repeat == true ? "한 곡 반복 켜짐 (누르면 끄기)" : "한 곡 반복: 지금 곡만 계속 듣기 (누르면 켜기)",
                _ => info != null ? $"{info.Title}\n{info.Sub}\n누르면 곡 목록 · 끌면 꺼내기 · 오른쪽 클릭: 메뉴" : "",
            };
            tip.SetToolTip(this, text);
        }
        // 제목에 마우스를 올리면 아래에 소리 조절기를 띄운다
        if (h == 3 && info != null) (dial ??= new VolumeDial(this, sc)).ShowAt(info.Volume);
        base.OnMouseMove(e);
    }
    VolumeDial? dial;
    protected override void OnMouseLeave(EventArgs e) { hot = -1; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button != MouseButtons.Left) return;
        if (dragged) { dragged = false; return; }
        int h = HitTest(e.Location);
        if (h is >= 0 and <= 2) WebWindow.MusicCmd(h);
        else if (h == 4) WebWindow.MusicMode("shuf");
        else if (h == 5) WebWindow.MusicMode("rep");
        else if (h == 3) TitleClick?.Invoke();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { titleFont.Dispose(); noteFont.Dispose(); tip.Dispose(); oneFont?.Dispose(); }
        base.Dispose(disposing);
    }
}

/// <summary>
/// 곡 제목에 마우스를 올리면 그 아래에 뜨는 반원 소리 조절기.
/// 손잡이를 반원을 따라 끌면 왼쪽 끝 0%부터 오른쪽 끝 100%까지 바뀐다.
/// </summary>
public sealed class VolumeDial : Form
{
    readonly Control anchor;
    readonly float sc;
    readonly Font pctFont = new("맑은 고딕", 13f, FontStyle.Bold);
    readonly Font capFont = new("맑은 고딕", 8.5f);
    readonly System.Windows.Forms.Timer watch = new() { Interval = 150 };
    double value = 1;
    bool dragging;
    int outside; // 마우스가 밖에 있던 횟수
    int S(int v) => UI.Scale(v, sc);
    double beforeMute = 0.5; // 음소거 전 소리 크기
    PointF Center => new(Width / 2f, S(24));
    float Radius => S(66);
    // 가운데 스피커 아이콘 자리 (누르면 음소거/해제)
    Rectangle IconRect => new(Width / 2 - S(14), S(30), S(28), S(22));

    public VolumeDial(Control anchor, float sc)
    {
        this.anchor = anchor; this.sc = sc;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.White;
        DoubleBuffered = true;
        Cursor = Cursors.Hand;
        Size = new Size(S(184), S(112));
        // 조절기와 뮤직 칸 둘 다에서 마우스가 벗어나 잠깐 지나면 닫는다
        watch.Tick += (_, _) =>
        {
            if (dragging) return;
            var p = Cursor.Position;
            var pill = anchor.RectangleToScreen(anchor.ClientRectangle);
            // 둘 사이 틈 (조절기가 아래에 있든 위에 있든)
            var gap = Rectangle.FromLTRB(Math.Min(pill.Left, Left), Math.Min(pill.Bottom, Bottom),
                                         Math.Max(pill.Right, Right), Math.Max(pill.Top, Top));
            outside = Bounds.Contains(p) || pill.Contains(p) || gap.Contains(p) ? 0 : outside + 1;
            if (outside >= 3) Hide();
        };
    }

    public void ShowAt(double v)
    {
        if (!dragging) value = Math.Clamp(v, 0, 1);
        var pill = anchor.RectangleToScreen(anchor.ClientRectangle);
        int titleCx = pill.Left + S(110); // 제목 아래쯤
        var host = anchor.FindForm();
        var wa = Screen.FromPoint(pill.Location).WorkingArea;
        int top;
        if (host is BarForm) top = Math.Max(pill.Bottom, host.Bottom) + S(10); // 바 아래로 조금 띄운다
        else
        {
            // 꺼낸 카드: 카드 바로 아래에 붙이고, 화면 아래가 모자라면 카드 위로 띄운다
            var card = host?.Bounds ?? pill;
            top = card.Bottom + S(6);
            if (top + Height > wa.Bottom) top = card.Top - S(6) - Height;
        }
        int x = Math.Clamp(titleCx - Width / 2, wa.Left, Math.Max(wa.Left, wa.Right - Width));
        Location = new Point(x, Math.Clamp(top, wa.Top, Math.Max(wa.Top, wa.Bottom - Height)));
        outside = 0;
        // 뮤직 칸이 바↔카드로 옮겨 다니므로 매번 지금 붙어 있는 창 위에 뜨게 주인을 바꾼다
        if (host != null && Owner != host) Owner = host;
        if (!Visible) Show();
        // 맨 위로만 올리고 포커스는 가져가지 않는다 (가져가면 열려 있던 곡 목록이 닫힌다)
        else SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_KEEP);
        watch.Start();
        Invalidate();
    }


    public void SetValue(double v) { if (!dragging && Visible) { value = Math.Clamp(v, 0, 1); Invalidate(); } }

    // 반원은 왼쪽(180°)에서 아래(90°)를 지나 오른쪽(0°)으로 간다
    double AngleOf(double v) => 180 - 180 * v;

    void SetFromPoint(Point p)
    {
        var c = Center;
        double dx = p.X - c.X, dy = p.Y - c.Y;
        double a = Math.Atan2(dy, dx) * 180 / Math.PI; // 아래쪽이 양수
        if (dy < 0) a = dx < 0 ? 180 : 0;              // 위로 벗어나면 가까운 끝에 붙인다
        value = Math.Round(Math.Clamp((180 - a) / 180, 0, 1) * 100) / 100;
        WebWindow.MusicVolume(value);
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        if (IconRect.Contains(e.Location))
        {
            if (value > 0.005) { beforeMute = value; value = 0; }
            else value = beforeMute > 0.005 ? beforeMute : 0.5;
            WebWindow.MusicVolume(value);
            Invalidate();
            return;
        }
        dragging = true;
        Capture = true;
        SetFromPoint(e.Location);
    }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (dragging) SetFromPoint(e.Location); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); dragging = false; Capture = false; }
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        value = Math.Clamp(Math.Round((value + (e.Delta > 0 ? 0.05 : -0.05)) * 100) / 100, 0, 1);
        WebWindow.MusicVolume(value);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var c = Center;
        float r = Radius;
        var rect = new RectangleF(c.X - r, c.Y - r, r * 2, r * 2);
        float sw = S(6);
        var gray = Color.FromArgb(150, 150, 150);

        // 카드 테두리
        using (var border = new Pen(Color.FromArgb(232, 232, 232))) g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);

        // 눈금 (25%마다 작은 점)
        for (int i = 1; i < 4; i++)
        {
            double a = AngleOf(i / 4.0) * Math.PI / 180;
            float tr = r + S(10), d = S(3);
            float tx = c.X + (float)(tr * Math.Cos(a)), ty = c.Y + (float)(tr * Math.Sin(a));
            using var tb = new SolidBrush(Color.FromArgb(215, 215, 215));
            g.FillEllipse(tb, tx - d / 2, ty - d / 2, d, d);
        }

        // 바탕 길과 채워진 부분 (연한 주황 → 진한 주황)
        using (var track = new Pen(Color.FromArgb(240, 240, 240), sw) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawArc(track, rect, 0, 180);
        if (value > 0.005)
            using (var grad = new LinearGradientBrush(new PointF(c.X - r - sw, 0), new PointF(c.X + r + sw, 0),
                       Color.FromArgb(255, 196, 120), UI.Accent))
            using (var fill = new Pen(grad, sw) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawArc(fill, rect, 180, -(float)(180 * value));

        // 손잡이: 흰 테두리가 있는 주황 점 + 옅은 그림자
        double rad = AngleOf(value) * Math.PI / 180;
        float kx = c.X + (float)(r * Math.Cos(rad)), ky = c.Y + (float)(r * Math.Sin(rad));
        float kr = S(8);
        using (var sh = new SolidBrush(Color.FromArgb(40, 0, 0, 0))) g.FillEllipse(sh, kx - kr, ky - kr + S(2), kr * 2, kr * 2);
        using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, kx - kr, ky - kr, kr * 2, kr * 2);
        using (var b = new SolidBrush(UI.Accent)) g.FillEllipse(b, kx - kr + S(3), ky - kr + S(3), kr * 2 - S(6), kr * 2 - S(6));

        // 양 끝 0 / 100
        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
        TextRenderer.DrawText(g, "0", capFont, new Rectangle((int)(c.X - r) - S(12), (int)c.Y - S(22), S(24), S(14)), gray, flags);
        TextRenderer.DrawText(g, "100", capFont, new Rectangle((int)(c.X + r) - S(14), (int)c.Y - S(22), S(28), S(14)), gray, flags);

        // 가운데: 스피커 아이콘 + 숫자
        DrawSpeaker(g, IconRect, value);
        TextRenderer.DrawText(g, $"{Math.Round(value * 100)}%", pctFont, new Rectangle(0, S(52), Width, S(26)), UI.Text, flags);
    }

    // 스피커 모양. 소리가 클수록 물결이 늘고, 0이면 X 표시
    void DrawSpeaker(Graphics g, Rectangle box, double v)
    {
        var col = v > 0.005 ? UI.Accent : Color.FromArgb(170, 170, 170);
        float x = box.X + S(3), cy = box.Y + box.Height / 2f, h = S(6);
        using (var b = new SolidBrush(col))
        {
            g.FillRectangle(b, x, cy - h / 2, S(4), h);
            g.FillPolygon(b, new[] { new PointF(x + S(3), cy - h / 2), new PointF(x + S(10), cy - S(8)),
                                     new PointF(x + S(10), cy + S(8)), new PointF(x + S(3), cy + h / 2) });
        }
        using var pen = new Pen(col, S(2)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        float wx = x + S(12);
        if (v <= 0.005)
        {
            g.DrawLine(pen, wx + S(1), cy - S(4), wx + S(8), cy + S(4));
            g.DrawLine(pen, wx + S(1), cy + S(4), wx + S(8), cy - S(4));
            return;
        }
        int waves = v < 0.34 ? 1 : v < 0.67 ? 2 : 3;
        for (int i = 0; i < waves; i++)
        {
            float wr = S(4 + i * 4);
            g.DrawArc(pen, wx - wr, cy - wr, wr * 2, wr * 2, -45, 90);
        }
    }

    protected override bool ShowWithoutActivation => true;
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); UI.RoundCorners(Handle); }
    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (!Visible) watch.Stop(); }
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE; // 작업 표시줄에 안 나오고 포커스를 뺏지 않게
            cp.ClassStyle |= CS_DROPSHADOW;        // 그림자
            return cp;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { watch.Dispose(); pctFont.Dispose(); capFont.Dispose(); }
        base.Dispose(disposing);
    }
}

/// <summary>바 아래에 뜨는 곡 목록: 인기 TOP 100 / 검색. 곡을 누르면 숨은 메애기 창에서 재생한다.</summary>
public sealed class MusicPicker : Form
{
    static MusicPicker? current;

    public static void Open(Form bar, Settings st, float sc, bool search)
    {
        if (current == null || current.IsDisposed) current = new MusicPicker(bar, st, sc);
        current.Place();
        current.Show(bar);
        current.Activate();
        current.SwitchTab(search);
    }

    readonly Form bar;
    readonly Settings st;
    readonly float sc;
    readonly RoundLabel tabTop, tabSearch;
    readonly Panel searchRow;
    readonly TextBox box = new() { BorderStyle = BorderStyle.None, Font = new Font("맑은 고딕", 10.5f * UI.Z), PlaceholderText = "브금 이름이나 지역 (Enter)" };
    readonly ListBox list = new()
    {
        DrawMode = DrawMode.OwnerDrawFixed, BorderStyle = BorderStyle.None, IntegralHeight = false,
        Font = new Font("맑은 고딕", 9.5f * UI.Z), Dock = DockStyle.Fill,
    };
    readonly Label status = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.FromArgb(140, 140, 140), Font = new Font("맑은 고딕", 9.5f * UI.Z) };
    readonly Font bold = new("맑은 고딕", 9.5f, FontStyle.Bold);
    readonly Font small = new("맑은 고딕", 8.5f);
    List<WebWindow.Song> songs = new();
    bool searchTab;
    int hover = -1, loadId;
    int S(int v) => UI.Scale(v, sc);

    MusicPicker(Form bar, Settings st, float sc)
    {
        this.bar = bar; this.st = st; this.sc = sc;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.White;
        Padding = new Padding(S(10));
        Size = new Size(S(380), S(460));

        // 위: 탭 두 개
        var tabs = new FlowLayoutPanel { Dock = DockStyle.Top, Height = S(38), WrapContents = false, BackColor = Color.White };
        tabTop = UI.Button("인기 TOP 100", "", () => SwitchTab(false), sc);
        tabSearch = UI.Button("브금 검색", "", () => SwitchTab(true), sc);
        tabs.Controls.Add(tabTop);
        tabs.Controls.Add(tabSearch);

        // 검색 칸
        searchRow = new Panel { Dock = DockStyle.Top, Height = S(40), Padding = new Padding(S(2), S(4), S(2), S(4)), BackColor = Color.White };
        var frame = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
        frame.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(box.Focused ? UI.Accent : UI.Line, S(1));
            using var p = UI.RoundRect(new RectangleF(0.5f, 0.5f, frame.Width - 1.5f, frame.Height - 1.5f), S(8));
            e.Graphics.DrawPath(pen, p);
        };
        box.Location = new Point(S(10), S(7));
        frame.Resize += (_, _) => box.Width = frame.Width - S(20);
        frame.Controls.Add(box);
        box.GotFocus += (_, _) => frame.Invalidate();
        box.LostFocus += (_, _) => frame.Invalidate();
        box.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; _ = Load(true, box.Text.Trim()); }
            else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; Hide(); }
        };
        searchRow.Controls.Add(frame);

        list.ItemHeight = S(44);
        list.DrawItem += DrawSong;
        list.MouseMove += (_, e) => { int i = list.IndexFromPoint(e.Location); if (i != hover) { hover = i; list.Invalidate(); } };
        list.MouseLeave += (_, _) => { hover = -1; list.Invalidate(); };
        list.MouseClick += async (_, e) =>
        {
            int i = list.IndexFromPoint(e.Location);
            if (i < 0 || i >= songs.Count) return;
            list.SelectedIndex = i;
            try { await WebWindow.MusicPlayAsync(i); } catch { } // async void라 예외가 새면 앱이 꺼진다
        };
        list.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Hide(); };

        var body = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
        body.Controls.Add(list);
        body.Controls.Add(status);
        Controls.Add(body);
        Controls.Add(searchRow);
        Controls.Add(tabs);
        Deactivate += (_, _) => Hide(); // 다른 곳을 누르면 닫힌다
    }

    void Place()
    {
        var wa = Screen.FromControl(bar).WorkingArea;
        int x = Math.Min(bar.Right - Width, wa.Right - Width);
        Location = new Point(Math.Max(wa.Left, x), bar.Bottom + S(6));
    }

    void SwitchTab(bool search)
    {
        searchTab = search;
        tabTop.Fill = search ? Color.Transparent : UI.SoftAccent;
        tabTop.ForeColor = search ? UI.Text : Color.FromArgb(150, 80, 0);
        tabSearch.Fill = search ? UI.SoftAccent : Color.Transparent;
        tabSearch.ForeColor = search ? Color.FromArgb(150, 80, 0) : UI.Text;
        searchRow.Visible = search;
        if (search) { box.Focus(); box.SelectAll(); if (songs.Count == 0 || !lastWasSearch) _ = Load(true, box.Text.Trim()); }
        else _ = Load(false, null);
    }
    bool lastWasSearch;

    async Task Load(bool search, string? q)
    {
        int id = ++loadId;
        lastWasSearch = search;
        ShowStatus("불러오는 중…");
        var r = await WebWindow.MusicListAsync(st, sc, search ? "/music/search" : "/music", search && q != "" ? q : null);
        if (id != loadId || IsDisposed) return;
        songs = r;
        list.BeginUpdate();
        list.Items.Clear();
        foreach (var s in songs) list.Items.Add(s.Title);
        list.EndUpdate();
        if (songs.Count == 0) ShowStatus(search ? "찾은 곡이 없어요" : "목록을 불러오지 못했어요");
        else { status.Visible = false; list.Visible = true; }
    }

    void ShowStatus(string t) { status.Text = t; status.Visible = true; list.Visible = false; }

    void DrawSong(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= songs.Count) return;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = e.Bounds;
        g.FillRectangle(Brushes.White, r);
        if (e.Index == hover)
            using (var b = new SolidBrush(UI.SoftAccent)) using (var p = UI.RoundRect(new RectangleF(r.Left + 1, r.Top + 2, r.Width - 2, r.Height - 4), S(8))) g.FillPath(b, p);
        var s = songs[e.Index];
        int x = r.Left + S(8);
        if (!searchTab)
        {
            TextRenderer.DrawText(g, (e.Index + 1).ToString(), bold, new Rectangle(x, r.Top, S(28), r.Height),
                e.Index < 3 ? UI.Accent : Color.FromArgb(150, 150, 150), TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
            x += S(32);
        }
        // 썸네일 (없으면 연한 칸에 ♪)
        var tb = new RectangleF(x, r.Top + (r.Height - S(34)) / 2f, S(34), S(34));
        var thumb = Thumbs.Get(s.Img, list);
        if (thumb != null) Thumbs.Draw(g, thumb, tb, S(8));
        else
        {
            using (var b = new SolidBrush(UI.SoftAccent)) using (var p = UI.RoundRect(tb, S(8))) g.FillPath(b, p);
            TextRenderer.DrawText(g, "♪", bold, Rectangle.Round(tb), UI.Accent,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
        }
        x += S(42);
        var flags = TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        int w = r.Right - x - S(34);
        TextRenderer.DrawText(g, s.Title, bold, new Rectangle(x, r.Top + S(4), w, S(20)), UI.Text, flags);
        TextRenderer.DrawText(g, s.Sub, small, new Rectangle(x, r.Top + S(23), w, S(18)), Color.FromArgb(140, 140, 140), flags);
        if (e.Index == hover)
        {
            // 재생 표시
            float cx = r.Right - S(20), cy = r.Top + r.Height / 2f, u = S(5);
            using var b = new SolidBrush(UI.Accent);
            g.FillEllipse(b, cx - S(12), cy - S(12), S(24), S(24));
            g.FillPolygon(Brushes.White, new[] { new PointF(cx - u * 0.6f, cy - u), new PointF(cx - u * 0.6f, cy + u), new PointF(cx + u, cy) });
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UI.RoundCorners(Handle);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW;          // 작업 표시줄에 안 나오게
            cp.ClassStyle |= CS_DROPSHADOW;    // 그림자
            return cp;
        }
    }

    protected override void Dispose(bool disposing)
    {
        // 글꼴은 자식 컨트롤이 치워지고 나서 치운다
        Font[] fonts = disposing ? new[] { box.Font, list.Font, status.Font } : Array.Empty<Font>();
        base.Dispose(disposing);
        foreach (var f in fonts) f.Dispose();
        if (disposing) { bold.Dispose(); small.Dispose(); }
    }
}
