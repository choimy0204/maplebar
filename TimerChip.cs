using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace MapleBar;

/// <summary>바에 붙는 작은 칩: 솔 야누스·룬 아이콘(직접 그린 것)과 남은 시간.</summary>
sealed class TimerChip : Control
{
    public enum Kind { Janus, Rune, Buff }
    /// <summary>버프 칩 색 (20분 주황 · 10분 노랑)</summary>
    public Color BuffTint { get => buffTint; set { if (value != buffTint) { buffTint = value; Invalidate(); } } }
    Color buffTint = Color.FromArgb(255, 140, 0);

    readonly Kind kind;
    readonly float sc;
    string text = "";
    bool alert; // 룬이 나와 있을 때: 칩을 진하게
    static readonly ToolTip tip = new();

    public TimerChip(Kind kind, float sc)
    {
        this.kind = kind; this.sc = sc;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Font = new Font("맑은 고딕", 10f * UI.Z, FontStyle.Bold);
        tip.SetToolTip(this, kind switch
        {
            Kind.Janus => "솔 야누스 남은 시간 (누르면 취소)",
            Kind.Rune => "다음 룬까지 남은 시간 (누르면 취소)",
            _ => "버프 남은 시간 (누르면 취소)",
        });
        Size = new Size(S(84), S(28));
    }

    int S(float v) => (int)Math.Round(v * sc);

    /// <summary>남은 초(없으면 숨김)를 넣는다. 보이고 숨김이 바뀌면 true.</summary>
    public bool SetTime(double? sec, bool ready = false)
    {
        bool show = sec != null || ready;
        string t = ready ? "나옴" : sec is double s ? $"{(int)s / 60}:{(int)s % 60:00}" : "";
        if (t != text || ready != alert) { text = t; alert = ready; Invalidate(); }
        if (show == Visible) return false;
        Visible = show;
        return true;
    }

    Color Tint => kind switch
    {
        Kind.Janus => Color.FromArgb(135, 80, 235),
        Kind.Rune => Color.FromArgb(20, 150, 220),
        _ => buffTint,
    };

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        var c = Tint;
        using (var path = UI.RoundRect(new RectangleF(0, 0, Width - 1, Height - 1), (Height - 1) / 2f))
        using (var b = new SolidBrush(alert ? c : Color.FromArgb(26, c)))
            g.FillPath(b, path);

        float isz = Height - S(4);
        var icon = new RectangleF(S(4), (Height - isz) / 2f, isz, isz);
        if (kind == Kind.Janus) DrawJanus(g, icon);
        else if (kind == Kind.Rune) DrawRune(g, icon);
        else DrawBuff(g, icon, c);

        var tr = new RectangleF(icon.Right + S(3), 0, Width - icon.Right - S(8), Height);
        using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        using var tb = new SolidBrush(alert ? Color.White : c);
        g.DrawString(text, Font, tb, tr, fmt);
    }

    // 솔 야누스: 연한 테두리 네모 안에 보랏빛 원판, 안쪽 고리와 점 무늬
    static void DrawJanus(Graphics g, RectangleF r)
    {
        using (var frame = UI.RoundRect(r, r.Width * 0.18f))
        {
            using (var b = new LinearGradientBrush(r, Color.FromArgb(240, 242, 252), Color.FromArgb(200, 206, 230), 90f)) g.FillPath(b, frame);
            using (var p = new Pen(Color.FromArgb(150, 158, 200), Math.Max(1f, r.Width / 22f))) g.DrawPath(p, frame);
        }
        var disc = Inset(r, r.Width * 0.12f);
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
