using System.Drawing.Drawing2D;

using static MapleBar.Native;

namespace MapleBar;

/// <summary>
/// 메애기 화면에 보이는 곡 썸네일을 받아 두는 곳. 페이지에 나온 주소만 쓰고, 못 받으면 ♪로 대신한다.
/// </summary>
static class Thumbs
{
    // [전역] 썸네일 캐시. 주소별 그림(실패는 null)과 받는 중인 주소 목록을 앱이 끝날 때까지 들고 있다
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
        lock (cache) { cache[url] = img; loading.Remove(url); order.Enqueue(url); }
        Trim();
        if (img == null || owner.IsDisposed || !owner.IsHandleCreated) return;
        // 그림이 여러 장 한꺼번에 도착해도 다시 그리기는 잠깐 모아서 한 번만 한다 (스크롤 버벅임 방지)
        lock (pending) if (!pending.Add(owner)) return;
        try
        {
            owner.BeginInvoke(async () =>
            {
                await Task.Delay(120);
                lock (pending) pending.Remove(owner);
                if (!owner.IsDisposed && owner.Visible) owner.Invalidate();
            });
        }
        catch { lock (pending) pending.Remove(owner); }
    }

    // 받아 둔 순서. Max장을 넘으면 오래된 것부터 버린다 (버린 그림이 다시 필요하면 새로 받는다)
    const int Max = 300;
    static readonly Queue<string> order = new();

    /// <summary>캐시가 너무 커지면 오래된 그림과 그 그림으로 깎아 둔 것들을 치운다. 그리는 쪽과 같은 UI 스레드에서만 부른다.</summary>
    static void Trim()
    {
        var drop = new List<Image>();
        lock (cache)
            while (order.Count > Max)
            {
                string u = order.Dequeue();
                if (cache.Remove(u, out var im) && im != null) drop.Add(im);
            }
        foreach (var im in drop)
        {
            foreach (var k in shaped.Keys.Where(k => ReferenceEquals(k.Item1, im)).ToList())
            {
                shaped[k].Dispose();
                shaped.Remove(k);
            }
            im.Dispose();
        }
    }

    static readonly HashSet<Control> pending = new();
    // 크기·모서리까지 맞춰 미리 깎아 둔 그림. 그릴 때는 그대로 찍기만 해서 빠르다
    static readonly Dictionary<(Image, int, int, int), Bitmap> shaped = new();

    /// <summary>둥근 네모 안에 썸네일을 꽉 채워 그린다.</summary>
    public static void Draw(Graphics g, Image img, RectangleF r, float radius)
    {
        int w = Math.Max(1, (int)Math.Round(r.Width)), h = Math.Max(1, (int)Math.Round(r.Height));
        var key = (img, w, h, (int)Math.Round(radius));
        if (!shaped.TryGetValue(key, out var bmp))
        {
            bmp = new Bitmap(w, h);
            using (var bg = Graphics.FromImage(bmp))
            {
                bg.SmoothingMode = SmoothingMode.AntiAlias;
                bg.InterpolationMode = InterpolationMode.HighQualityBicubic;
                float s = Math.Max((float)w / img.Width, (float)h / img.Height);
                float iw = img.Width * s, ih = img.Height * s;
                using var scaled = new Bitmap(w, h);
                using (var sg = Graphics.FromImage(scaled))
                {
                    sg.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    sg.DrawImage(img, (w - iw) / 2, (h - ih) / 2, iw, ih);
                }
                using var tex = new TextureBrush(scaled);
                using var path = UI.RoundRect(new RectangleF(0, 0, w, h), radius);
                bg.FillPath(tex, path);
            }
            shaped[key] = bmp;
        }
        g.DrawImageUnscaled(bmp, (int)Math.Round(r.Left), (int)Math.Round(r.Top));
    }
}

/// <summary>
/// 바 안의 뮤직 칸: ♪ 곡 제목, 진행 바, ⏮ ⏯ ⏭. 아이콘은 글꼴 대신 직접 그려 또렷하게 보인다.
/// 제목을 누르면 곡 목록을 연다.
/// </summary>
public sealed class MusicPill : Control
{
    readonly float sc;
    readonly Font titleFont = new("맑은 고딕", 9f, FontStyle.Bold);
    readonly Font subFont = new("맑은 고딕", 7.5f);
    readonly Font noteFont = new("Segoe UI Symbol", 11f, FontStyle.Bold);
    readonly ToolTip tip = new();
    WebWindow.MusicInfo? info;
    public WebWindow.MusicInfo? Info => info;
    int hot = BtnNone; // 마우스가 올라간 버튼 (아래 Btn… 번호)
    public event Action? TitleClick;

    public MusicPill(float sc)
    {
        this.sc = sc;
        DoubleBuffered = true;
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Size = new Size(BarW, S(36));
    }

    // 바 안에서는 썸네일과 버튼만 두어 바가 덜 넓어지게 한다 (제목은 마우스를 올리면 보인다). 꺼낸 카드는 제목까지 넓게
    int BarW => S(242);
    int CardW => S(392);
    bool Compact => !bare;

    int S(int v) => UI.Scale(v, sc);
    // 버튼 번호. 0~2는 WebWindow.MusicCmd에 그대로 넘기는 번호다 (0 이전, 1 재생/일시정지, 2 다음)
    const int BtnNone = -1, BtnPrev = 0, BtnPlay = 1, BtnNext = 2, BtnTitle = 3, BtnShuffle = 4, BtnRepeat = 5;
    // 버튼 순서(왼→오): 랜덤 이전 재생 다음 반복
    static readonly int[] Order = { BtnPrev, BtnPlay, BtnNext, BtnShuffle, BtnRepeat }; // 이전·재생·다음 · 랜덤·한 곡 반복
    Rectangle Btn(int i)
    {
        // 바 안의 작은 칸은 버튼 사이와 양 끝을 조금 더 띄워 답답하지 않게 한다
        int w = S(Compact ? 34 : 30), slot = Array.IndexOf(Order, i);
        return new Rectangle(Width - S(Compact ? 10 : 6) - (Order.Length - slot) * w, (Height - S(30)) / 2 - S(1), w, S(28));
    }
    Rectangle TitleRect => new(S(Compact ? 10 : 8), 0, Btn(Order[0]).Left - S(Compact ? 20 : 10), Height - S(6));

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
        using var pill = UI.RoundRect(box, S(10)); // 둥근 직사각형 (알약 모양 아님)
        // 바탕은 따뜻한 흰색, 테두리는 옅게 (가사 카드와 같은 톤). 꺼낸 창 안에서는 창이 곧 칸이라 그리지 않는다
        if (!bare) using (var b = new SolidBrush(Paper)) g.FillPath(b, pill);

        var tr = TitleRect;
        if (hot == BtnTitle) using (var b = new SolidBrush(Color.FromArgb(18, UI.Accent)))
            using (var p = UI.RoundRect(new RectangleF(tr.Left - S(2), S(3), tr.Width + S(4), Height - S(7)), S(10))) g.FillPath(b, p);
        // 썸네일 (없으면 ♪)
        var thumb = Thumbs.Get(info?.Img, this);
        var tb = new RectangleF(tr.Left + S(2), (Height - S(26)) / 2f, S(26), S(26));
        if (thumb != null) Thumbs.Draw(g, thumb, tb, S(13)); // 동그랗게
        else
        {
            using (var b = new SolidBrush(Color.FromArgb(28, UI.Accent))) g.FillEllipse(b, tb);
            TextRenderer.DrawText(g, "♪", noteFont, Rectangle.Round(tb), UI.Accent,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
        }
        // 제목 + 가수 (두 줄)
        var textR = new Rectangle(tr.Left + S(36), 0, tr.Width - S(38), Height);
        const TextFormatFlags one = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
        if (Compact || textR.Width < S(20)) { } // 바 안의 작은 칸: 제목은 그리지 않는다
        else if (info == null || string.IsNullOrWhiteSpace(info.Sub))
            TextRenderer.DrawText(g, string.IsNullOrEmpty(info?.Title) ? "눌러서 곡 고르기" : info.Title, titleFont, textR, Color.FromArgb(45, 40, 36), one);
        else
        {
            int mid = Height / 2;
            TextRenderer.DrawText(g, info.Title, titleFont, new Rectangle(textR.X, mid - S(15), textR.Width, S(16)), Color.FromArgb(45, 40, 36), one);
            TextRenderer.DrawText(g, info.Sub, subFont, new Rectangle(textR.X, mid + S(1), textR.Width, S(13)), Color.FromArgb(150, 140, 130), one);
        }

        // 진행 바: 칸 아래쪽 안에 아주 얇게
        float inset = S(8);
        var track = new RectangleF(inset, Height - S(bare ? 2 : 4), Width - inset * 2, Math.Max(1.5f, S(2)));
        using (var b = new SolidBrush(Color.FromArgb(240, 230, 218))) g.FillRectangle(b, track);
        double pr = info != null ? Progress(info.Time) : 0;
        if (pr > 0) using (var b = new SolidBrush(UI.Accent)) g.FillRectangle(b, track.Left, track.Top, (float)(track.Width * pr), track.Height);
        if (!bare) using (var pen = new Pen(Color.FromArgb(236, 224, 210))) g.DrawPath(pen, pill);

        // 재생 버튼들과 반복·랜덤 사이 옅은 구분선
        var sr = Btn(BtnShuffle);
        using (var pen = new Pen(Color.FromArgb(232, 220, 206), Math.Max(1f, S(1))))
            g.DrawLine(pen, sr.Left, Height / 2f - S(7), sr.Left, Height / 2f + S(7));

        // 버튼
        foreach (int i in Order)
        {
            var r = Btn(i);
            float d = i == BtnPlay ? S(26) : S(24);
            var circle = new RectangleF(r.Left + (r.Width - d) / 2f, r.Top + (r.Height - d) / 2f, d, d);
            bool on = (i == BtnShuffle && info?.Shuffle == true) || (i == BtnRepeat && info?.Repeat == true);
            if (i == BtnPlay)
                using (var b = new SolidBrush(hot == BtnPlay ? ControlPaint.Dark(UI.Accent, 0.05f) : UI.Accent)) g.FillEllipse(b, circle);
            else if (hot == i || on)
                using (var b = new SolidBrush(Color.FromArgb(hot == i ? 34 : 22, UI.Accent))) g.FillEllipse(b, circle);
            DrawIcon(g, i, r);
        }
    }

    void DrawIcon(Graphics g, int i, Rectangle r)
    {
        float cx = r.Left + r.Width / 2f, cy = r.Top + r.Height / 2f, u = S(5);
        if (i >= 4) { DrawModeIcon(g, i, cx, cy, u); return; }
        var col = i == BtnPlay ? Color.White : hot == i ? Color.FromArgb(60, 50, 40) : Color.FromArgb(105, 95, 85);
        using var b = new SolidBrush(col);
        if (i == BtnPlay)
        {
            if (info?.Playing == true)
            {
                g.FillRectangle(b, cx - u * 0.9f, cy - u, u * 0.65f, u * 2);
                g.FillRectangle(b, cx + u * 0.25f, cy - u, u * 0.65f, u * 2);
            }
            else g.FillPolygon(b, new[] { new PointF(cx - u * 0.7f, cy - u * 1.05f), new PointF(cx - u * 0.7f, cy + u * 1.05f), new PointF(cx + u * 1.1f, cy) });
            return;
        }
        float d = i == BtnPrev ? -1 : 1; // 왼쪽(이전) / 오른쪽(다음)
        g.FillPolygon(b, new[] { new PointF(cx - d * u * 0.8f, cy - u), new PointF(cx - d * u * 0.8f, cy + u), new PointF(cx + d * u * 0.6f, cy) });
        g.FillRectangle(b, d > 0 ? cx + u * 0.6f : cx - u * 0.6f - u * 0.4f, cy - u, u * 0.4f, u * 2);
    }

    // 랜덤(교차 화살표) / 반복(돌아오는 화살표 + 1). 켜져 있으면 주황, 꺼져 있으면 회색
    Font? oneFont; // 한 곡 반복 아이콘의 "1". 배율(UI.Z)이 바뀌면 다시 만든다

    void DrawModeIcon(Graphics g, int i, float cx, float cy, float u)
    {
        bool on = i == BtnShuffle ? info?.Shuffle == true : info?.Repeat == true;
        var col = on ? UI.Accent : Color.FromArgb(165, 150, 135);
        using var pen = new Pen(col, S(2)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var b = new SolidBrush(col);
        float l = cx - u * 1.2f, rt = cx + u * 1.2f, t = cy - u * 0.8f, bt = cy + u * 0.8f, a = u * 0.55f;
        if (i == BtnShuffle)
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
        return TitleRect.Contains(p) ? BtnTitle : BtnNone;
    }

    // 끌어서 꺼내기: 누른 채로 조금 움직이면 클릭 대신 DragStart(잡은 자리)를 알린다
    public event Action<Point>? DragStart;
    Point? downAt;
    bool dragged;
    bool ghost;
    /// <summary>true면 자리만 차지하고 그리지 않는다 (다시 넣는 효과가 끝날 때까지).</summary>
    public bool Ghost { get => ghost; set { ghost = value; Invalidate(); } }
    /// <summary>뮤직 칸 바탕색 (꺼낸 창도 같은 색을 쓴다).</summary>
    public static readonly Color Paper = Color.FromArgb(255, 252, 248);
    bool bare;
    /// <summary>true면 둥근 칸 모양 없이 내용만 그린다 (꺼낸 창 자체가 칸 모양일 때).</summary>
    public bool Bare { get => bare; set { if (bare != value) { bare = value; Width = value ? CardW : BarW; Invalidate(); } } }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left) { downAt = e.Location; dragged = false; }
        else if (e.Button == MouseButtons.Right) dial?.Hide(); // 메뉴가 뜰 자리를 비운다
    }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); downAt = null; }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (downAt is Point d && e.Button == MouseButtons.Left && !dragged &&
            (Math.Abs(e.X - d.X) > S(5) || Math.Abs(e.Y - d.Y) > S(5)))
        {
            dragged = true; downAt = null;
            dial?.Hide(); tip.Hide(this);
            hot = BtnNone; Invalidate();
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
        // 오른쪽 클릭 메뉴가 열려 있으면 겹치지 않게 띄우지 않는다
        if (h == BtnTitle && info != null && ContextMenuStrip?.Visible != true) (dial ??= new VolumeDial(this, sc)).ShowAt(info.Volume);
        base.OnMouseMove(e);
    }
    VolumeDial? dial;
    protected override void OnMouseLeave(EventArgs e) { hot = BtnNone; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button != MouseButtons.Left) return;
        if (dragged) { dragged = false; return; }
        int h = HitTest(e.Location);
        if (h is >= BtnPrev and <= BtnNext) WebWindow.MusicCmd(h);
        else if (h == BtnShuffle) WebWindow.MusicMode(WebWindow.ModeShuffle);
        else if (h == BtnRepeat) WebWindow.MusicMode(WebWindow.ModeRepeat);
        else if (h == BtnTitle) TitleClick?.Invoke();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { titleFont.Dispose(); subFont.Dispose(); noteFont.Dispose(); tip.Dispose(); oneFont?.Dispose(); }
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
    // 꺼낸 카드에서는 반원 대신 카드 폭에 맞춘 가로 막대로 보여 준다
    bool flat;
    // 스피커 아이콘 자리 (누르면 음소거/해제). 반원은 가운데, 가로 막대는 왼쪽 끝
    Rectangle IconRect => flat ? new(S(10), (Height - S(22)) / 2, S(28), S(22)) : new(Width / 2 - S(14), S(30), S(28), S(22));
    // 가로 막대 길 (왼쪽 스피커와 오른쪽 숫자 사이)
    float TrackL => S(48);
    float TrackR => Width - S(56);

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
        int top, x;
        flat = host is not BarForm;
        if (!flat)
        {
            Size = new Size(S(184), S(112));
            BackColor = Color.White;
            top = Math.Max(pill.Bottom, host!.Bottom) + S(10); // 바 아래로 조금 띄운다
            x = titleCx - Width / 2;
        }
        else
        {
            // 꺼낸 카드: 카드와 같은 폭의 얇은 막대를 카드 바로 아래에 붙이고, 화면 아래가 모자라면 위로 띄운다
            var card = host?.Bounds ?? pill;
            Size = new Size(card.Width, S(34));
            BackColor = MusicPill.Paper;
            top = card.Bottom + S(6);
            if (top + Height > wa.Bottom) top = card.Top - S(6) - Height;
            x = card.Left;
        }
        x = Math.Clamp(x, wa.Left, Math.Max(wa.Left, wa.Right - Width));
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
        if (flat)
        {
            value = Math.Round(Math.Clamp((p.X - TrackL) / (TrackR - TrackL), 0, 1) * 100) / 100;
            WebWindow.MusicVolume(value);
            Invalidate();
            return;
        }
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
        if (flat) { PaintFlat(g); return; }
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

    // 꺼낸 카드용 가로 막대: [스피커] ━━━●──── 75%
    void PaintFlat(Graphics g)
    {
        using (var border = new Pen(Color.FromArgb(236, 224, 210))) g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        DrawSpeaker(g, IconRect, value);
        float cy = Height / 2f, th = S(4), l = TrackL, w = TrackR - TrackL;
        using (var b = new SolidBrush(Color.FromArgb(240, 230, 218)))
        using (var p = UI.RoundRect(new RectangleF(l, cy - th / 2, w, th), th / 2)) g.FillPath(b, p);
        float kx = l + (float)(w * value);
        if (value > 0.005)
            using (var b = new SolidBrush(UI.Accent))
            using (var p = UI.RoundRect(new RectangleF(l, cy - th / 2, Math.Max(th, kx - l), th), th / 2)) g.FillPath(b, p);
        // 손잡이: 흰 테두리가 있는 주황 점
        float kr = S(7);
        using (var sh = new SolidBrush(Color.FromArgb(40, 0, 0, 0))) g.FillEllipse(sh, kx - kr, cy - kr + S(1), kr * 2, kr * 2);
        using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, kx - kr, cy - kr, kr * 2, kr * 2);
        using (var b = new SolidBrush(UI.Accent)) g.FillEllipse(b, kx - kr + S(3), cy - kr + S(3), kr * 2 - S(6), kr * 2 - S(6));
        TextRenderer.DrawText(g, $"{Math.Round(value * 100)}%", capFont, new Rectangle((int)TrackR + S(8), 0, Width - (int)TrackR - S(14), Height),
            Color.FromArgb(45, 40, 36), TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
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
    // [전역] 곡 고르기 창은 하나만 띄운다. 닫혔으면 다음에 새로 만든다
    static MusicPicker? current;

    public static void Open(Form bar, Settings st, float sc, bool search)
    {
        if (current == null || current.IsDisposed) current = new MusicPicker(bar, st, sc);
        current.Place();
        current.Show(bar);
        current.Activate();
        current.SwitchTab(search);
    }

    /// <summary>곡 제목을 누를 때: 열려 있으면 닫고, 닫혀 있으면 연다.</summary>
    public static void Toggle(Form bar, Settings st, float sc)
    {
        if (current is { IsDisposed: false, Visible: true }) current.Hide();
        else Open(bar, st, sc, false);
    }

    readonly Form bar;
    readonly Settings st;
    readonly float sc;
    readonly RoundLabel tabTop, tabSearch, tabFav;
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
    enum Tab { Top, Search, Fav }
    Tab tab;
    int noteAt = -1; string note = ""; // 즐겨찾기 곡 아래에 잠깐 쓰는 말 (찾는 중… 등)
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
        tabFav = UI.Button("즐겨찾기", "", () => SwitchTab(Tab.Fav), sc);
        tabs.Controls.Add(tabTop);
        tabs.Controls.Add(tabSearch);
        tabs.Controls.Add(tabFav);
        // 오른쪽 끝: 닫기 (게임을 눌러도 목록은 그대로 둔다)
        var close = UI.Button("닫기", "", Hide, sc, null, UI.HoverBack);
        close.Radius = S(6);
        var closeWrap = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, BackColor = Color.White };
        closeWrap.Controls.Add(close);
        var top = new Panel { Dock = DockStyle.Top, Height = S(38), BackColor = Color.White };
        tabs.Dock = DockStyle.Fill;
        top.Controls.Add(tabs);
        top.Controls.Add(closeWrap);

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
        // 마우스가 옮겨 가면 전체가 아니라 바뀐 두 줄만 다시 그린다 (전체를 그리면 버벅인다)
        list.MouseMove += (_, e) => { int i = list.IndexFromPoint(e.Location); if (i != hover) SetHover(i); };
        list.MouseLeave += (_, _) => SetHover(-1);
        list.MouseClick += async (_, e) =>
        {
            int i = list.IndexFromPoint(e.Location);
            if (i < 0 || i >= songs.Count) return;
            // 오른쪽 별을 누르면 즐겨찾기에 넣고 뺀다
            if (e.X >= list.GetItemRectangle(i).Right - S(34)) { ToggleFav(songs[i]); return; }
            list.SelectedIndex = i;
            try { await Play(i); }
            catch (Exception ex) { Log.Write("music", "곡 틀기 실패", ex); } // async void라 예외가 새면 앱이 꺼진다
        };
        list.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Hide(); };

        var body = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
        body.Controls.Add(list);
        body.Controls.Add(status);
        Controls.Add(body);
        Controls.Add(searchRow);
        Controls.Add(top);
        // 탭 줄의 빈 곳이나 가장자리를 잡고 끌어 옮긴다 (옮긴 자리는 앱을 끌 때까지 기억)
        foreach (Control c in new Control[] { this, top, tabs, closeWrap }) c.MouseDown += Drag;
    }

    bool moved;

    void Drag(object? s, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        ReleaseCapture();
        SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero); // WM_NCLBUTTONDOWN, HTCAPTION
        moved = true;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool ReleaseCapture();
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);

    void Place()
    {
        if (moved) return; // 사용자가 옮겨 둔 자리를 그대로 쓴다
        var wa = Screen.FromControl(bar).WorkingArea;
        int x = Math.Min(bar.Right - Width, wa.Right - Width);
        Location = new Point(Math.Max(wa.Left, x), bar.Bottom + S(6));
    }

    void SwitchTab(bool search) => SwitchTab(search ? Tab.Search : Tab.Top);

    void SwitchTab(Tab t)
    {
        tab = t;
        void Mark(RoundLabel b, bool on) { b.Fill = on ? UI.SoftAccent : Color.Transparent; b.ForeColor = on ? Color.FromArgb(150, 80, 0) : UI.Text; }
        Mark(tabTop, t == Tab.Top);
        Mark(tabSearch, t == Tab.Search);
        Mark(tabFav, t == Tab.Fav);
        searchRow.Visible = t == Tab.Search;
        noteAt = -1;
        if (t == Tab.Search) { box.Focus(); box.SelectAll(); if (songs.Count == 0 || !lastWasSearch) _ = Load(true, box.Text.Trim()); }
        else if (t == Tab.Fav) ShowFavs();
        else _ = Load(false, null);
    }

    // ── 즐겨찾기 ──
    bool IsFav(WebWindow.Song s) => st.MusicFavs.Any(f => f.Title == s.Title && f.Sub == s.Sub);

    void ToggleFav(WebWindow.Song s)
    {
        int k = st.MusicFavs.FindIndex(f => f.Title == s.Title && f.Sub == s.Sub);
        if (k >= 0) st.MusicFavs.RemoveAt(k);
        else st.MusicFavs.Insert(0, new FavSong { Title = s.Title, Sub = s.Sub, Img = s.Img });
        st.Save();
        if (tab == Tab.Fav) ShowFavs();
        else list.Invalidate();
    }

    void ShowFavs()
    {
        ++loadId; // 불러오던 목록은 버린다
        lastWasSearch = false;
        SetSongs(st.MusicFavs.Select(f => new WebWindow.Song(f.Title, f.Sub, f.Img)).ToList());
        if (songs.Count == 0) ShowStatus("곡 오른쪽 ☆를 누르면 여기에 모여요");
    }

    // 지금 보이는 목록을 읽어 온 곳 (곡이 페이지에 없을 때 여기서 다시 찾는다)
    string listPath = "/music";
    string? listQuery;

    /// <summary>
    /// i번째 곡을 튼다. 순번이 아니라 제목·지역으로 찾아 누르므로 페이지가 바뀌어 있어도 같은 곡이 나온다.
    /// 즐겨찾기 곡은 사이트에서 이름으로 검색해 찾는다. 랜덤 재생은 지금 보이는 목록 안에서 돈다.
    /// </summary>
    async Task Play(int i)
    {
        var s = songs[i];
        var pool = songs;
        bool fav = tab == Tab.Fav;
        if (fav) { noteAt = i; note = "찾는 중…"; list.Invalidate(); }
        var r = fav
            ? await WebWindow.MusicPlayAsync(st, sc, s, "/music/search", s.Title, pool)
            : await WebWindow.MusicPlayAsync(st, sc, s, listPath, listQuery, pool);
        if (IsDisposed || !ReferenceEquals(pool, songs)) return; // 그 사이 다른 목록으로 바뀌었으면 쓸 말이 없다
        string? msg = r switch
        {
            WebWindow.MusicStatus.Ok or WebWindow.MusicStatus.Canceled => null,
            WebWindow.MusicStatus.NotFound or WebWindow.MusicStatus.Empty => "사이트에서 찾지 못했어요",
            _ => FailText(r),
        };
        if (msg != null) { noteAt = i; note = msg; }
        else if (noteAt == i) noteAt = -1;
        list.Invalidate();
    }

    /// <summary>목록을 못 읽었거나 곡을 못 틀었을 때 보여 줄 말.</summary>
    static string FailText(WebWindow.MusicStatus s) => s switch
    {
        WebWindow.MusicStatus.PageChanged => "메애기 화면이 바뀌어 곡을 읽지 못했어요",
        WebWindow.MusicStatus.NoWindow => "뮤직이 꺼졌어요. 다시 눌러 주세요",
        WebWindow.MusicStatus.Timeout => "메애기가 응답하지 않아요. 잠시 뒤 다시 눌러 주세요",
        _ => "목록을 불러오지 못했어요",
    };

    void SetSongs(List<WebWindow.Song> r)
    {
        songs = r;
        list.BeginUpdate();
        list.Items.Clear();
        foreach (var s in songs) list.Items.Add(s.Title);
        list.EndUpdate();
        status.Visible = false; list.Visible = true;
    }
    bool lastWasSearch;

    async Task Load(bool search, string? q)
    {
        int id = ++loadId;
        lastWasSearch = search;
        ShowStatus("불러오는 중…");
        string path = search ? "/music/search" : "/music";
        string? query = search && q != "" ? q : null;
        var r = await WebWindow.MusicListAsync(st, sc, path, query);
        if (id != loadId || IsDisposed) return;
        listPath = path; listQuery = query;
        noteAt = -1;
        SetSongs(r.Songs);
        if (r.Status == WebWindow.MusicStatus.Empty) ShowStatus(search ? "찾은 곡이 없어요" : "목록을 불러오지 못했어요");
        else if (r.Status != WebWindow.MusicStatus.Ok) ShowStatus(FailText(r.Status));
    }

    void ShowStatus(string t) { status.Text = t; status.Visible = true; list.Visible = false; }

    void SetHover(int i)
    {
        int old = hover;
        hover = i;
        foreach (int k in new[] { old, i })
            if (k >= 0 && k < list.Items.Count) list.Invalidate(list.GetItemRectangle(k));
    }

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
        if (tab == Tab.Top)
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
        int w = r.Right - x - S(66);
        bool noted = e.Index == noteAt;
        TextRenderer.DrawText(g, s.Title, bold, new Rectangle(x, r.Top + S(4), w, S(20)), UI.Text, flags);
        TextRenderer.DrawText(g, noted ? note : s.Sub, small, new Rectangle(x, r.Top + S(23), w, S(18)), noted ? UI.Accent : Color.FromArgb(140, 140, 140), flags);
        // 즐겨찾기 별 (맨 오른쪽)
        DrawStar(g, r.Right - S(17), r.Top + r.Height / 2f, S(8), IsFav(s));
        if (e.Index == hover)
        {
            // 재생 표시
            float cx = r.Right - S(48), cy = r.Top + r.Height / 2f, u = S(5);
            using var b = new SolidBrush(UI.Accent);
            g.FillEllipse(b, cx - S(12), cy - S(12), S(24), S(24));
            g.FillPolygon(Brushes.White, new[] { new PointF(cx - u * 0.6f, cy - u), new PointF(cx - u * 0.6f, cy + u), new PointF(cx + u, cy) });
        }
    }

    static void DrawStar(Graphics g, float cx, float cy, float r, bool on)
    {
        var pts = new PointF[10];
        for (int k = 0; k < 10; k++)
        {
            double a = -Math.PI / 2 + k * Math.PI / 5;
            float rr = k % 2 == 0 ? r : r * 0.45f;
            pts[k] = new PointF(cx + (float)Math.Cos(a) * rr, cy + (float)Math.Sin(a) * rr);
        }
        if (on) { using var b = new SolidBrush(UI.Accent); g.FillPolygon(b, pts); }
        else { using var p = new Pen(Color.FromArgb(185, 185, 185), 1.3f) { LineJoin = LineJoin.Round }; g.DrawPolygon(p, pts); }
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
