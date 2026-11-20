using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace MapleBar;

/// <summary>바에 붙는 작은 칩: 솔 야누스·룬 아이콘(직접 그린 것)과 남은 시간.</summary>
sealed class TimerChip : Control
{
    public enum Kind { Janus, Rune, Buff, Off, Custom } // Off: 전체 알림을 꺼 둔 표시 (아이콘 없이 "OFF") · Custom: 반복·단일 알림 (제목 + 남은 시간)
    /// <summary>버프 칩 색 (20분 주황 · 10분 노랑)</summary>
    public Color BuffTint { get => buffTint; set { if (value != buffTint) { buffTint = value; Invalidate(); Changed?.Invoke(); } } }
    Color buffTint = Color.FromArgb(255, 140, 0);

    readonly Kind kind;
    readonly float sc;
    string text = "";
    bool alert; // 룬이 나와 있을 때: 칩을 진하게
    static readonly ToolTip tip = new();
    readonly Font chipFont; // 이 칩이 만든 글꼴 (칩을 정리할 때 푼다)

    /// <summary>떼어 둔 자리를 저장할 때 쓰는 칩 이름</summary>
    public string Key { get; init; } = "";
    /// <summary>칩이 떠 있어야 하는지 (바에서 떼어 내 숨겨 두어도 그대로)</summary>
    public bool On { get; private set; }
    /// <summary>바에서 떼어 냈는지. 떼어 내면 바에서는 숨기고, 따로 뜬 칸이 대신 보인다</summary>
    public bool Detached { get => detached; set { detached = value; Visible = On && !value; } }
    bool detached;
    /// <summary>바로 돌아오는 중: 바에서 자리는 차지하되 그리지는 않는다</summary>
    public bool Ghost { get => ghost; set { if (ghost != value) { ghost = value; Invalidate(); } } }
    bool ghost;
    /// <summary>바에 붙은 칩을 끌기 시작하면 (누른 점, 칩 안 좌표)</summary>
    public event Action<TimerChip, Point>? DragOut;
    /// <summary>마우스를 올렸을 때 보이는 설명 (누르면 무엇을 하는지까지)</summary>
    public string TipText { get; private set; }
    string label = ""; // 반복·단일 알림 칩: 칸 제목

    public TimerChip(Kind kind, float sc)
    {
        this.kind = kind; this.sc = sc;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Font = chipFont = new Font("맑은 고딕", 10f * UI.Z, FontStyle.Bold);
        TipText = kind switch
        {
            Kind.Janus => "솔 야누스 남은 시간 (누르면 취소)",
            Kind.Rune => "다음 룬까지 남은 시간 (누르면 취소)",
            Kind.Off => "알림이 모두 꺼져 있어요 (누르면 켜요)",
            Kind.Custom => "알림까지 남은 시간 (누르면 취소)",
            _ => "버프 남은 시간 (누르면 취소)",
        };
        tip.SetToolTip(this, TipText + "\n끌어서 원하는 곳에 둘 수 있어요");
        Size = new Size(S(kind == Kind.Off ? 50 : 84), S(31));
        if (kind == Kind.Off) text = "OFF";
    }

    /// <summary>OFF 칩을 보이거나 숨긴다. 바에서 보이고 숨김이 바뀌면 true.</summary>
    public bool SetShown(bool show)
    {
        On = show;
        bool vis = show && !detached;
        if (vis == Visible) return false;
        Visible = vis;
        return true;
    }

    // ── 끌기: 조금 움직여야 끌기로 보고, 그냥 누르면 원래처럼 누른 것 ──
    Point downAt;
    bool armed, dragged;

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        dragged = false;
        armed = e.Button == MouseButtons.Left && DragOut != null;
        downAt = e.Location;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!armed || (e.Button & MouseButtons.Left) == 0) return;
        if (Math.Abs(e.X - downAt.X) + Math.Abs(e.Y - downAt.Y) < S(6)) return;
        armed = false;
        dragged = true;
        Capture = false; // 이제부터는 떠 있는 칸이 마우스를 따라간다
        DragOut?.Invoke(this, downAt);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        armed = false;
    }

    protected override void OnClick(EventArgs e)
    {
        if (dragged) { dragged = false; return; } // 끌어서 뗀 것은 누른 것이 아니다
        base.OnClick(e);
    }

    /// <summary>떠 있는 칸을 눌렀을 때: 바에서 누른 것과 같은 일을 한다</summary>
    public void Tap()
    {
        dragged = false;
        base.OnClick(EventArgs.Empty);
    }

    int S(float v) => UI.Scale(v, sc);

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing); // 그리기가 끝난 뒤에 글꼴을 푼다
        if (disposing) chipFont.Dispose();
    }

    /// <summary>남은 초(없으면 숨김)를 넣는다. 보이고 숨김이 바뀌면 true.</summary>
    public bool SetTime(double? sec, bool ready = false)
    {
        bool show = sec != null || ready;
        string t = ready ? "나옴" : sec is double s ? $"{(int)s / 60}:{(int)s % 60:00}" : "";
        if (kind == Kind.Custom && t != "") t = label + " " + t;
        bool resized = false;
        if (t != text || ready != alert) { text = t; alert = ready; resized = FitWidth(); Invalidate(); Changed?.Invoke(); }
        return SetShown(show) || (resized && Visible);
    }

    /// <summary>반복·단일 알림 칩의 제목을 넣는다 (길면 줄인다)</summary>
    public void SetLabel(string title)
    {
        var t = title.Length > 8 ? title[..7] + "…" : title;
        if (t == label) return;
        label = t;
        TipText = $"{title} 남은 시간 (누르면 취소)";
        tip.SetToolTip(this, TipText + "\n끌어서 원하는 곳에 둘 수 있어요");
    }

    /// <summary>반복·단일 알림 칩은 글 길이에 맞춰 넓힌다. 숫자는 0으로 재서 1초마다 흔들리지 않게. 넓이가 바뀌면 true.</summary>
    bool FitWidth()
    {
        if (kind != Kind.Custom || text == "") return false;
        var sample = new string(text.Select(ch => char.IsDigit(ch) ? '0' : ch).ToArray());
        int textW = TextRenderer.MeasureText(sample, Font, Size.Empty, TextFormatFlags.NoPadding).Width;
        int icon = Height - S(4);
        int w = Math.Max(S(84), S(4) + icon + S(3) + textW + S(12));
        if (w == Width) return false;
        Width = w;
        return true;
    }

    /// <summary>글자나 색이 바뀌면 (떠 있는 칸을 다시 그리라고)</summary>
    public event Action? Changed;

    /// <summary>칩 색 (떠 있는 칸의 테두리에도 쓴다)</summary>
    public Color Tint => kind switch
    {
        Kind.Janus => Color.FromArgb(135, 80, 235),
        Kind.Rune => Color.FromArgb(20, 150, 220),
        Kind.Off => Color.FromArgb(130, 130, 140),
        _ => buffTint,
    };

    protected override void OnPaint(PaintEventArgs e)
    {
        if (ghost) return;
        Render(e.Graphics, Size);
    }

    /// <summary>칩을 g에 그린다. 떠 있는 칸은 크기를 바꿔 가며 그리므로 글자 맞춤(GridFit)을 끈다.</summary>
    public void Render(Graphics g, Size size, bool smooth = false)
    {
        int Width = size.Width, Height = size.Height;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = smooth ? TextRenderingHint.AntiAlias : TextRenderingHint.AntiAliasGridFit;

        var c = Tint;
        using (var path = UI.RoundRect(new RectangleF(0, 0, Width - 1, Height - 1), (Height - 1) * 0.25f)) // 둥근 직사각형
        {
            // 다크 모드: 비치는 색 대신 차콜에 색을 조금 섞은 칸 + 옅은 색 테두리
            var fill = alert ? c : UI.Dark ? Mix(UI.Back, c, 0.16f) : Color.FromArgb(26, c);
            using (var b = new SolidBrush(fill)) g.FillPath(b, path);
            if (UI.Dark && !alert) { using var pen = new Pen(Color.FromArgb(90, c), 1f); g.DrawPath(pen, path); }
        }

        float isz = Height - S(4);
        var icon = new RectangleF(S(4), (Height - isz) / 2f, isz, isz);
        if (kind == Kind.Janus) DrawJanus(g, icon);
        else if (kind == Kind.Rune) DrawRune(g, icon);
        else if (kind == Kind.Custom) DrawBell(g, icon, c);
        else if (kind != Kind.Off) DrawBuff(g, icon, c);

        var tr = kind == Kind.Off ? new RectangleF(0, 0, Width, Height) : new RectangleF(icon.Right + S(3), 0, Width - icon.Right - S(8), Height);
        using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        var fore = alert ? Color.White : UI.Dark ? Mix(c, Color.White, 0.62f) : c;
        using var tb = new SolidBrush(fore);
        g.DrawString(text, Font, tb, tr, fmt);
    }

    // 솔 야누스: 보랏빛 원판, 안쪽 고리와 점 무늬 (다른 아이콘처럼 뒤 틀 없이)
    static void DrawJanus(Graphics g, RectangleF r)
    {
        var disc = Inset(r, r.Width * 0.06f);
        using (var path = new GraphicsPath())
        {
            path.AddEllipse(disc);
            using var pb = new PathGradientBrush(path)
            {
                CenterColor = Color.FromArgb(150, 120, 245),
                SurroundColors = new[] { Color.FromArgb(70, 45, 165) },
            };
            g.FillPath(pb, path);
        }
        float w = Math.Max(1f, r.Width / 20f);
        using (var p = new Pen(Color.FromArgb(220, 210, 255), w)) g.DrawEllipse(p, Inset(disc, disc.Width * 0.14f));
        using (var p = new Pen(Color.FromArgb(180, 200, 185, 255), w * 0.8f)) g.DrawEllipse(p, Inset(disc, disc.Width * 0.3f));
        // 고리 위의 점 8개
        float cx = disc.X + disc.Width / 2, cy = disc.Y + disc.Height / 2, rad = disc.Width * 0.36f, dot = disc.Width * 0.07f;
        using (var b = new SolidBrush(Color.FromArgb(235, 228, 255)))
        {
            for (int i = 0; i < 8; i++)
            {
                double a = Math.PI / 4 * i;
                g.FillEllipse(b, cx + (float)Math.Cos(a) * rad - dot, cy + (float)Math.Sin(a) * rad - dot, dot * 2, dot * 2);
            }
            float core = disc.Width * 0.11f;
            g.FillEllipse(b, cx - core, cy - core, core * 2, core * 2);
        }
    }

    // 룬: 뒤에 푸른 빛, 팔각형 보석, 가운데 흰 위쪽 화살표
    static void DrawRune(Graphics g, RectangleF r)
    {
        using (var glow = new GraphicsPath())
        {
            glow.AddEllipse(Inset(r, -r.Width * 0.05f));
            using var gb = new PathGradientBrush(glow)
            {
                CenterColor = Color.FromArgb(170, 175, 200, 255),
                SurroundColors = new[] { Color.FromArgb(0, 175, 200, 255) },
            };
            g.FillPath(gb, glow);
        }
        var gem = Inset(r, r.Width * 0.16f);
        using (var outer = Octagon(gem))
        {
            using (var b = new LinearGradientBrush(gem, Color.FromArgb(215, 240, 255), Color.FromArgb(70, 140, 225), 60f)) g.FillPath(b, outer);
            using (var p = new Pen(Color.FromArgb(240, 250, 255), Math.Max(1f, r.Width / 22f))) g.DrawPath(p, outer);
        }
        var inner = Inset(gem, gem.Width * 0.24f);
        using (var ip = Octagon(inner))
        using (var b = new LinearGradientBrush(inner, Color.FromArgb(40, 95, 190), Color.FromArgb(90, 160, 235), 90f))
            g.FillPath(b, ip);
        // 위쪽 화살표
        float cx = inner.X + inner.Width / 2, top = inner.Y + inner.Height * 0.2f, bot = inner.Bottom - inner.Height * 0.18f, hw = inner.Width * 0.3f;
        using var arrow = new GraphicsPath();
        arrow.AddPolygon(new[]
        {
            new PointF(cx, top), new PointF(cx + hw, top + hw), new PointF(cx + hw * 0.35f, top + hw),
            new PointF(cx + hw * 0.35f, bot), new PointF(cx - hw * 0.35f, bot), new PointF(cx - hw * 0.35f, top + hw),
            new PointF(cx - hw, top + hw),
        });
        using (var b = new SolidBrush(Color.White)) g.FillPath(b, arrow);
    }

    // 버프: 칩 색 원 안에 흰 위쪽 화살표 두 개
    static void DrawBuff(Graphics g, RectangleF r, Color c)
    {
        var disc = Inset(r, r.Width * 0.1f);
        using (var b = new SolidBrush(c)) g.FillEllipse(b, disc);
        using (var p = new Pen(Color.FromArgb(200, 255, 255, 255), Math.Max(1f, r.Width / 22f))) g.DrawEllipse(p, Inset(disc, disc.Width * 0.08f));
        float cx = disc.X + disc.Width / 2, hw = disc.Width * 0.24f, h = disc.Height * 0.18f;
        using var pen = new Pen(Color.White, Math.Max(1.5f, r.Width / 9f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        foreach (float y in new[] { disc.Y + disc.Height * 0.32f, disc.Y + disc.Height * 0.56f })
            g.DrawLines(pen, new[] { new PointF(cx - hw, y + h), new PointF(cx, y), new PointF(cx + hw, y + h) });
    }

    // 반복·단일 알림: 칩 색 원 안에 흰 종
    static void DrawBell(Graphics g, RectangleF r, Color c)
    {
        var disc = Inset(r, r.Width * 0.1f);
        using (var b = new SolidBrush(c)) g.FillEllipse(b, disc);
        using (var p = new Pen(Color.FromArgb(200, 255, 255, 255), Math.Max(1f, r.Width / 22f))) g.DrawEllipse(p, Inset(disc, disc.Width * 0.08f));
        float cx = disc.X + disc.Width / 2, top = disc.Y + disc.Height * 0.24f, bot = disc.Y + disc.Height * 0.66f;
        float hw = disc.Width * 0.24f, rw = disc.Width * 0.3f;
        using var bell = new GraphicsPath();
        bell.AddArc(cx - hw * 0.7f, top, hw * 1.4f, hw * 1.2f, 180, 180);
        bell.AddLine(cx + hw * 0.7f, top + hw * 0.6f, cx + hw, bot - disc.Height * 0.05f);
        bell.AddLine(cx + rw, bot, cx - rw, bot);
        bell.AddLine(cx - hw, bot - disc.Height * 0.05f, cx - hw * 0.7f, top + hw * 0.6f);
        bell.CloseFigure();
        using var wb = new SolidBrush(Color.White);
        g.FillPath(wb, bell);
        float d = disc.Width * 0.12f;
        g.FillEllipse(wb, cx - d / 2, bot + disc.Height * 0.03f, d, d);
    }

    static Color Mix(Color a, Color b, float t) =>
        Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    static RectangleF Inset(RectangleF r, float d) => new(r.X + d, r.Y + d, r.Width - 2 * d, r.Height - 2 * d);

    static GraphicsPath Octagon(RectangleF r)
    {
        float k = r.Width * 0.29f;
        var p = new GraphicsPath();
        p.AddPolygon(new[]
        {
            new PointF(r.X + k, r.Y), new PointF(r.Right - k, r.Y), new PointF(r.Right, r.Y + k), new PointF(r.Right, r.Bottom - k),
            new PointF(r.Right - k, r.Bottom), new PointF(r.X + k, r.Bottom), new PointF(r.X, r.Bottom - k), new PointF(r.X, r.Y + k),
        });
        return p;
    }
}
