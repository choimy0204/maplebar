using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

using static MapleBar.Native;

namespace MapleBar;
/// <summary>게임 화면 가장자리를 보랏빛 기운이 한 바퀴 돌고, 가운데 위쪽에 "솔 야누스가 꺼졌어요"를 잠깐 띄운다.
/// 마우스와 키보드를 막지 않는 투명 창 네 개(위·아래·왼쪽·오른쪽 띠)에 그린다.</summary>
static class JanusAlert
{
    const double Lap = 1.3;   // 한 바퀴 도는 시간
    const double Fade = 1.8;  // 다 돈 뒤 테두리가 사라지는 시간
    static Color Main = Color.FromArgb(150, 80, 255);
    static Color Light = Color.FromArgb(228, 205, 255);
    static string Text = "";

    static Action? stop;

    /// <summary>기본은 솔 야누스(보라). 룬 알림은 글과 색만 바꿔 같은 효과를 쓴다.</summary>
    public static void Play(float sc, string text = "솔 야누스가 꺼졌어요", Color? main = null, Color? light = null,
        AlertSound.Kind sound = AlertSound.Kind.Janus)
    {
        AlertSound.Play(sound);
        stop?.Invoke();
        Text = text;
        Main = main ?? Color.FromArgb(150, 80, 255);
        Light = light ?? Color.FromArgb(228, 205, 255);
        // 모든 모니터에 띄운다. 게임이 있는 모니터는 게임 화면에 맞춘다
        var areas = Screen.AllScreens.Select(s => s.Bounds).ToList();
        if (Game.Area() is Rectangle gr)
        {
            var c = new Point(gr.X + gr.Width / 2, gr.Y + gr.Height / 2);
            int gi = areas.FindIndex(r => r.Contains(c));
            if (gi >= 0) areas[gi] = gr; else areas.Add(gr);
        }
        int t = Math.Max(8, (int)Math.Round(64 * sc));
        var sets = areas.Select(area =>
        {
            var rects = new[]
            {
                new Rectangle(0, 0, area.Width, t),
                new Rectangle(0, area.Height - t, area.Width, t),
                new Rectangle(0, t, t, area.Height - 2 * t),
                new Rectangle(area.Width - t, t, t, area.Height - 2 * t),
            };
            var strips = rects.Select(_ => new Splash()).ToArray();
            var bmps = rects.Select(r => new Bitmap(Math.Max(1, r.Width), Math.Max(1, r.Height))).ToArray();
            // 화면 전체를 물들이는 막: 가운데는 옅고 가장자리는 진하게 한 번만 그려 두고, 투명도만 바꾼다
            var tint = new Splash();
            using (var tb = Tint(area.Size)) tint.Present(tb, area.Location);
            tint.SetAlpha(0);
            return (area, rects, strips, bmps, tint, pill: new Splash());
        }).ToArray();
        var timer = new System.Windows.Forms.Timer { Interval = 15 };
        var start = DateTime.Now;

        stop = () =>
        {
            timer.Stop(); timer.Dispose();
            foreach (var x in sets)
            {
                x.tint.Dispose();
                foreach (var s in x.strips) s.Dispose();
                foreach (var b in x.bmps) b.Dispose();
                x.pill.Dispose();
            }
            pillBmp?.Dispose(); pillBmp = null;
            stop = null;
        };

        timer.Tick += (_, _) =>
        {
            double el = (DateTime.Now - start).TotalSeconds;
            if (el > Lap + Fade) { stop?.Invoke(); return; }
            // 빛이 도는 동안 점점 물들고, 다 돌면 천천히 빠진다
            double ta = el < Lap ? Math.Sin(el / Lap * Math.PI / 2) : 1 - Math.Pow((el - Lap) / Fade, 1.5);
            // 글씨: 처음에 살짝 늦게 떠서 끝날 때 같이 사라진다
            double a = Math.Clamp((el - 0.15) / 0.3, 0, 1) * Math.Clamp((Lap + Fade - el) / 0.6, 0, 1);
            var pb = PillFor(a, sc);
            foreach (var (area, rects, strips, bmps, tint, pill) in sets)
            {
                tint.SetAlpha((byte)(255 * Math.Clamp(ta, 0, 1)));
                for (int i = 0; i < rects.Length; i++)
                {
                    using (var g = Graphics.FromImage(bmps[i]))
                    {
                        g.Clear(Color.Transparent);
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.TranslateTransform(-rects[i].X, -rects[i].Y);
                        Draw(g, area.Size, el, sc, rects[i]);
                    }
                    strips[i].Present(bmps[i], new Point(area.X + rects[i].X, area.Y + rects[i].Y));
                }
                pill.Present(pb, new Point(area.X + (area.Width - pb.Width) / 2, area.Y + (int)(area.Height * 0.28)));
            }
        };
        timer.Start();
    }

    static void Draw(Graphics g, Size size, double el, float sc, Rectangle clip)
    {
        float inset = 5 * sc;
        var r = new RectangleF(inset, inset, size.Width - 2 * inset, size.Height - 2 * inset);
        float len = 2 * (r.Width + r.Height);

        if (el < Lap)
        {
            double p = el / Lap;
            p = p < 0.5 ? 2 * p * p : 1 - Math.Pow(-2 * p + 2, 2) / 2; // 천천히 출발해 빨라졌다가 천천히 멈춘다
            float head = (float)(p * len);

            // 지나간 자리에 남는 옅은 선
            using (var pen = new Pen(Color.FromArgb(80, Main), 3 * sc) { LineJoin = LineJoin.Round })
            {
                var pts = Span(r, head);
                if (pts.Length >= 2) g.DrawLines(pen, pts);
            }

            // 머리와 꼬리: 머리에 가까울수록 크고 밝다
            float tail = len * 0.28f, step = Math.Max(2f, 4 * sc);
            using var b = new SolidBrush(Color.Empty); // 점마다 새로 만들지 않고 색만 바꿔 쓴다
            for (float d = Math.Max(0, head - tail); d <= head; d += step)
            {
                float k = 1 - (head - d) / tail;
                var pt = At(r, d);
                float rad = (8 + 26 * k) * sc;
                if (pt.X + rad < clip.Left || pt.X - rad > clip.Right || pt.Y + rad < clip.Top || pt.Y - rad > clip.Bottom) continue;
                b.Color = Color.FromArgb(5 + (int)(50 * k * k), Main);
                g.FillEllipse(b, pt.X - rad, pt.Y - rad, rad * 2, rad * 2);
                float core = (3 + 8 * k) * sc;
                b.Color = Color.FromArgb((int)(210 * k * k * k), Light);
                g.FillEllipse(b, pt.X - core, pt.Y - core, core * 2, core * 2);
            }
        }
        else
        {
            // 다 돌고 나면 테두리 전체가 한 번 빛나고 사라진다
            double u = (el - Lap) / Fade;
            float a = (float)(1 - u * u);
            using (var pen = new Pen(Color.FromArgb((int)(50 * a), Main), 20 * sc)) g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
            using (var pen = new Pen(Color.FromArgb((int)(160 * a), Main), 4 * sc)) g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
            using (var pen = new Pen(Color.FromArgb((int)(140 * a), Light), 1.5f * sc)) g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
        }
    }

    // 둘레 위의 점: 위쪽 가운데에서 시작해 시계 방향으로 d만큼 간 곳
    static PointF At(RectangleF r, float d)
    {
        float w = r.Width, h = r.Height, len = 2 * (w + h);
        float s = ((d + w / 2) % len + len) % len;
        if (s < w) return new PointF(r.Left + s, r.Top); s -= w;
        if (s < h) return new PointF(r.Right, r.Top + s); s -= h;
        if (s < w) return new PointF(r.Right - s, r.Bottom); s -= w;
        return new PointF(r.Left, r.Bottom - s);
    }

    // 시작점부터 d까지 지나간 길 (모서리마다 꺾는다)
    static PointF[] Span(RectangleF r, float d)
    {
        float w = r.Width, h = r.Height;
        var list = new List<PointF> { At(r, 0) };
        foreach (var c in new[] { w / 2, w / 2 + h, w / 2 + h + w, w / 2 + 2 * h + w })
            if (c < d) list.Add(At(r, c));
        list.Add(At(r, d));
        return list.ToArray();
    }

    static Bitmap Tint(Size size)
    {
        var bmp = new Bitmap(Math.Max(1, size.Width), Math.Max(1, size.Height));
        using var g = Graphics.FromImage(bmp);
        using (var edge = new SolidBrush(Color.FromArgb(150, Main))) g.FillRectangle(edge, 0, 0, bmp.Width, bmp.Height);
        // 화면보다 큰 타원으로 가운데를 옅게 (가장자리로 갈수록 진해진다)
        var oval = new RectangleF(-size.Width * 0.2f, -size.Height * 0.25f, size.Width * 1.4f, size.Height * 1.5f);
        using var path = new GraphicsPath();
        path.AddEllipse(oval);
        using var pb = new PathGradientBrush(path)
        {
            CenterColor = Color.FromArgb(45, Main),
            SurroundColors = new[] { Color.FromArgb(150, Main) },
        };
        g.CompositingMode = CompositingMode.SourceCopy; // 겹쳐 칠하지 않고 바꿔 칠한다
        g.FillPath(pb, path);
        return bmp;
    }

    // 글씨 알약은 15ms마다 쓰이지만 진하기(a)가 같으면 모양이 같다. 글·색·배율·진하기가 다 같을 때만 지난 것을 다시 쓴다
    static Bitmap? pillBmp;
    static (string, int, float, double) pillKey;
    static Bitmap PillFor(double a, float sc)
    {
        var key = (Text, Main.ToArgb(), sc, a);
        if (pillBmp != null && key == pillKey) return pillBmp;
        pillBmp?.Dispose();
        pillBmp = Pill(a, sc);
        pillKey = key;
        return pillBmp;
    }

    static Bitmap Pill(double a, float sc)
    {
        string text = Text;
        using var font = new Font("맑은 고딕", 30 * sc, FontStyle.Bold, GraphicsUnit.Pixel);
        Size ts;
        using (var tmp = new Bitmap(1, 1))
        using (var tg = Graphics.FromImage(tmp)) ts = Size.Ceiling(tg.MeasureString(text, font));
        int padX = (int)(22 * sc), padY = (int)(10 * sc);
        var bmp = new Bitmap(ts.Width + padX * 2, ts.Height + padY * 2);
        if (a <= 0) return bmp;
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAlias;
        var box = new RectangleF(1, 1, bmp.Width - 2, bmp.Height - 2);
        using var path = Round(box, box.Height / 2);
        using (var b = new SolidBrush(Color.FromArgb((int)(215 * a), Main.R / 4, Main.G / 4, Main.B / 4 + 10))) g.FillPath(b, path);
        using (var pen = new Pen(Color.FromArgb((int)(220 * a), Main), Math.Max(1f, 1.5f * sc))) g.DrawPath(pen, path);
        using (var b = new SolidBrush(Color.FromArgb((int)(255 * a), Color.White))) g.DrawString(text, font, b, padX, padY);
        return bmp;
    }

    static GraphicsPath Round(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 90, 180);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 180);
        p.CloseFigure();
        return p;
    }
}
