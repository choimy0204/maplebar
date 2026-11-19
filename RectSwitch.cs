using System.Drawing.Drawing2D;
using System.Media;

namespace MapleBar;

/// <summary>
/// 둥근 직사각형 토글 스위치: 왼쪽에 이름, 오른쪽에 칸 안에서 미끄러지는 손잡이.
/// 누를 때마다 딸깍 소리를 내고 손잡이가 부드럽게 옮겨 간다.
/// </summary>
sealed class RectSwitch : Control
{
    readonly float sc;
    readonly System.Windows.Forms.Timer anim = new() { Interval = 15 };
    bool on, hot;
    float knob; // 0 = 꺼짐 자리, 1 = 켜짐 자리
    int S(float v) => UI.Scale(v, sc);

    public event Action<bool>? Changed;
    public string Caption { get; set; } = "";
    public Color OnColor { get; set; } = UI.Accent;
    /// <summary>손잡이 안에 종 그림 (꺼지면 빗금)</summary>
    public bool Bell { get; set; }

    public RectSwitch(float sc, string caption, bool on)
    {
        this.sc = sc;
        Caption = caption;
        this.on = on;
        knob = on ? 1 : 0;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
            | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        Cursor = Cursors.Hand;
        anim.Tick += (_, _) =>
        {
            float to = this.on ? 1 : 0;
            knob += (to - knob) * 0.35f;
            if (Math.Abs(to - knob) < 0.02f) { knob = to; anim.Stop(); }
            Invalidate();
        };
    }

    public bool On
    {
        get => on;
        set { if (on == value) return; on = value; anim.Start(); }
    }

    Size TrackSize => new(S(40), S(22));

    public override Size GetPreferredSize(Size proposed)
    {
        int textW = Caption.Length > 0 ? TextRenderer.MeasureText(Caption, Font).Width + S(4) : 0;
        var t = TrackSize;
        return new Size(textW + t.Width + S(2), Math.Max(t.Height, Font.Height) + S(2));
    }

    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); Size = GetPreferredSize(Size.Empty); }
    protected override void OnCreateControl() { base.OnCreateControl(); Size = GetPreferredSize(Size.Empty); }
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hot = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hot = false; Invalidate(); }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        On = !on;
        Click2.Play(on);
        Changed?.Invoke(on);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var t = TrackSize;
        int tx = Width - t.Width - S(1), ty = (Height - t.Height) / 2;

        if (Caption.Length > 0)
        {
            var textRect = new Rectangle(0, 0, tx - S(4), Height);
            TextRenderer.DrawText(g, Caption, Font, textRect, on ? UI.Text : UI.Sub,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        // 칸: 켜질수록 강조색으로 물든다
        Color off = UI.Dark ? Color.FromArgb(70, 72, 78) : Color.FromArgb(205, 208, 214);
        Color fill = Mix(off, OnColor, knob);
        if (hot) fill = ControlPaint.Dark(fill, 0.06f);
        using (var path = UI.RoundRect(new RectangleF(tx, ty, t.Width, t.Height), S(6)))
        using (var b = new SolidBrush(fill))
            g.FillPath(b, path);

        // 손잡이: 칸 안쪽에서 미끄러지는 흰 둥근 직사각형
        int inset = S(3);
        float kw = t.Height - inset * 2, kh = kw;
        float kx = tx + inset + (t.Width - inset * 2 - kw) * knob;
        using (var path = UI.RoundRect(new RectangleF(kx, ty + inset, kw, kh), S(4)))
        using (var b = new SolidBrush(Color.White))
            g.FillPath(b, path);

        if (Bell) DrawBell(g, new RectangleF(kx, ty + inset, kw, kh), fill);
    }

    /// <summary>손잡이 안 종: 켜지면 칸 색으로 또렷하게, 꺼지면 흐리게 빗금</summary>
    void DrawBell(Graphics g, RectangleF k, Color color)
    {
        float pad = k.Width * 0.18f;
        float x = k.X + pad, y = k.Y + pad, w = k.Width - pad * 2, h = k.Height - pad * 2;
        Color c = on ? color : (UI.Dark ? Color.FromArgb(120, 122, 128) : Color.FromArgb(160, 164, 170));
        using var p = new GraphicsPath();
        p.AddArc(x + w * 0.2f, y + h * 0.04f, w * 0.6f, w * 0.6f, 180, 180);
        p.AddLine(x + w * 0.8f, y + h * 0.34f, x + w * 0.92f, y + h * 0.74f);
        p.AddLine(x + w * 0.92f, y + h * 0.74f, x + w * 0.08f, y + h * 0.74f);
        p.AddLine(x + w * 0.08f, y + h * 0.74f, x + w * 0.2f, y + h * 0.34f);
        p.CloseFigure();
        using (var b = new SolidBrush(c))
        {
            g.FillPath(b, p);
            g.FillEllipse(b, x + w * 0.38f, y + h * 0.78f, w * 0.24f, h * 0.2f);
        }
        if (!on)
        {
            using var white = new Pen(Color.White, Math.Max(2f, w * 0.2f));
            using var pen = new Pen(c, Math.Max(1.2f, w * 0.1f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(white, x + w * 0.05f, y + h * 0.02f, x + w * 0.95f, y + h * 0.98f);
            g.DrawLine(pen, x + w * 0.05f, y + h * 0.02f, x + w * 0.95f, y + h * 0.98f);
        }
    }

    static Color Mix(Color a, Color b, float k) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * k), (int)(a.G + (b.G - a.G) * k), (int)(a.B + (b.B - a.B) * k));

    protected override void Dispose(bool disposing)
    {
        if (disposing) anim.Dispose();
        base.Dispose(disposing);
    }

    /// <summary>
    /// 휴대폰 볼륨 버튼 같은 짧고 마른 딸깍 소리 (메모리에서 만든 WAV).
    /// 켤 때는 조금 높게, 끌 때는 조금 낮게 난다.
    /// </summary>
    static class Click2
    {
        static SoundPlayer? onPlayer, offPlayer;

        public static void Play(bool on)
        {
            try
            {
                var p = on ? (onPlayer ??= new SoundPlayer(Make(1.0))) : (offPlayer ??= new SoundPlayer(Make(0.82)));
                p.Play();
            }
            catch { }
        }

        static MemoryStream Make(double pitch)
        {
            const int rate = 44100;
            int n = rate * 18 / 1000; // 18ms: 울림 없이 톡 끊기게
            var pcm = new short[n];
            var rnd = new Random(11);
            double lp = 0, prev = 0;
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / rate;
                // 처음 0.6ms는 살짝 올라오며 '딱' 하고 부딪히는 소리
                double attack = Math.Min(1, t / 0.0006);
                // 나무 두드리듯 낮은 몸통(이 소리가 '딸'), 빨리 사라짐
                double body = Math.Sin(2 * Math.PI * 1150 * pitch * t) * Math.Exp(-t * 520) * 0.55
                            + Math.Sin(2 * Math.PI * 520 * pitch * t) * Math.Exp(-t * 380) * 0.35;
                // 플라스틱이 걸리는 짧은 잡음(이 소리가 '깍'), 부드럽게 다듬어 날카롭지 않게
                double noise = rnd.NextDouble() * 2 - 1;
                lp += (noise - lp) * 0.45;
                double tick = (lp - prev * 0.3) * Math.Exp(-t * 1400) * 0.7;
                prev = lp;
                double s = (body + tick) * attack;
                pcm[i] = (short)(Math.Clamp(s * 0.55, -1, 1) * short.MaxValue);
            }
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write("RIFF"u8); w.Write(36 + n * 2); w.Write("WAVE"u8);
            w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write("data"u8); w.Write(n * 2);
            foreach (var v in pcm) w.Write(v);
            ms.Position = 0;
            return ms;
        }
    }
}
