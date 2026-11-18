using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace MapleBar;

/// <summary>파티원이 스킬을 쓰거나 쿨타임이 다 돌았을 때 화면 오른쪽에서 미끄러져 들어오는 띠 알림.
/// 마우스를 막지 않는 투명 창(Splash)에 그린다.</summary>
static class PartyAlert
{
    const int MaxBands = 4;
    const double SlideIn = 0.35, SlideOut = 0.3;

    sealed class Band
    {
        public Splash Win = new();
        public Bitmap Bmp = null!;
        public long StartMs;
        public double HoldSec;
    }

    static readonly List<Band> bands = new();
    static System.Windows.Forms.Timer? timer;
    static float sc = 1f;

    public static void Init(float scale) => sc = scale;

    static Color ColorOf(PartySkill k) => k switch
    {
        PartySkill.Origin => Color.FromArgb(240, 190, 50),
        PartySkill.Burst => Color.FromArgb(235, 70, 70),
        PartySkill.Semi => Color.FromArgb(70, 175, 240),
        _ => Color.FromArgb(150, 95, 230),
    };

    static string GlyphOf(PartySkill k) => k switch
    {
        PartySkill.Origin => "✦",
        PartySkill.Burst => "⚔",
        PartySkill.Semi => "➶",
        _ => "▲",
    };

    /// <summary>띠를 하나 띄운다. small이면 작고 짧게 (쿨타임 다 돎 알림)</summary>
    public static void Show(string job, PartySkill kind, string text, bool small)
    {
        if (AlertSound.St is { AlertVisual: false }) return;
        try
        {
            while (bands.Count >= MaxBands) Remove(bands[0]);
            var b = new Band
            {
                Bmp = Draw(job, kind, text, small ? 0.72f : 1f),
                StartMs = Environment.TickCount64,
                HoldSec = small ? 1.3 : 2.0,
            };
            bands.Add(b);
            Place(b, Environment.TickCount64, bands.Count - 1);
            if (timer == null)
            {
                timer = new System.Windows.Forms.Timer { Interval = 15 };
                timer.Tick += (_, _) => Step();
            }
            timer.Start();
        }
        catch (Exception ex) { Log.Write("party", "알림 띠를 띄우지 못함", ex); }
    }

    /// <summary>떠 있는 띠를 모두 닫는다 (파티를 나갈 때)</summary>
    public static void CloseAll()
    {
        timer?.Stop();
        while (bands.Count > 0) Remove(bands[0]);
    }

    static void Remove(Band b)
    {
        bands.Remove(b);
        b.Win.Dispose();
        b.Bmp.Dispose();
    }

    static void Step()
    {
        long now = Environment.TickCount64;
        for (int i = bands.Count - 1; i >= 0; i--)
        {
            var b = bands[i];
            if ((now - b.StartMs) / 1000.0 > SlideIn + b.HoldSec + SlideOut) Remove(b);
        }
        if (bands.Count == 0) { timer?.Stop(); return; }
        for (int i = 0; i < bands.Count; i++) Place(bands[i], now, i);
    }

    static Rectangle Area() => Game.Area() ?? Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);

    // 오른쪽 바깥에서 들어와 머물다가 다시 나간다
    static void Place(Band b, long now, int index)
    {
        double el = (now - b.StartMs) / 1000.0;
        double k;
        if (el < SlideIn) k = Ease(el / SlideIn);
        else if (el < SlideIn + b.HoldSec) k = 1;
        else k = 1 - Ease((el - SlideIn - b.HoldSec) / SlideOut);
        k = Math.Clamp(k, 0, 1);

        var area = Area();
        int gap = UI.Scale(8, sc);
        int shownX = area.Right - b.Bmp.Width - UI.Scale(12, sc);
        int x = (int)(area.Right - (area.Right - shownX) * k);
        int y = area.Y + (int)(area.Height * 0.28) + index * (b.Bmp.Height + gap);
        b.Win.Present(b.Bmp, new Point(x, y));
    }

    static double Ease(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return 1 - Math.Pow(1 - t, 3);
    }

    static Bitmap Draw(string job, PartySkill kind, string text, float zoom)
    {
        var main = ColorOf(kind);
        float u = sc * zoom;
        using var jobFont = new Font("맑은 고딕", 24 * u, FontStyle.Bold, GraphicsUnit.Pixel);
        using var textFont = new Font("맑은 고딕", 17 * u, FontStyle.Bold, GraphicsUnit.Pixel);
        using var glyphFont = new Font("Segoe UI Symbol", 22 * u, FontStyle.Regular, GraphicsUnit.Pixel);

        SizeF jobSize, textSize;
        using (var tmp = new Bitmap(1, 1))
        using (var tg = Graphics.FromImage(tmp))
        {
            jobSize = job == "" ? SizeF.Empty : tg.MeasureString(job, jobFont);
            textSize = tg.MeasureString(text, textFont);
        }

        float pad = 10 * u, circle = 44 * u, gap = 10 * u;
        float width = pad + circle + gap + (jobSize.Width > 0 ? jobSize.Width + gap : 0) + textSize.Width + pad + 4 * u;
        float height = circle + pad * 2;
        var bmp = new Bitmap((int)Math.Ceiling(width), (int)Math.Ceiling(height));
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAlias;

        // 띠: 둥근 직사각형, 스킬 색이 어둡게 깔린 바탕 + 색 테두리
        var box = new RectangleF(1, 1, bmp.Width - 2, bmp.Height - 2);
        using (var path = UI.RoundRect(box, box.Height * 0.25f))
        {
            using var back = new SolidBrush(Color.FromArgb(225, main.R / 5, main.G / 5, main.B / 5 + 10));
            g.FillPath(back, path);
            using var pen = new Pen(Color.FromArgb(230, main), Math.Max(1f, 1.8f * u));
            g.DrawPath(pen, path);
        }

        // 아이콘 원
        float cx = pad, cy = (bmp.Height - circle) / 2;
        using (var fill = new SolidBrush(main)) g.FillEllipse(fill, cx, cy, circle, circle);
        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
        using (var white = new SolidBrush(Color.White))
            g.DrawString(GlyphOf(kind), glyphFont, white, new RectangleF(cx, cy, circle, circle), sf);

        // 직업 줄임말(크게) + 스킬 이름
        float x = cx + circle + gap;
        using var tb = new SolidBrush(Color.White);
        using var sb = new SolidBrush(Color.FromArgb(235, 235, 235));
        if (jobSize.Width > 0)
        {
            g.DrawString(job, jobFont, tb, x, (bmp.Height - jobSize.Height) / 2);
            x += jobSize.Width + gap;
        }
        g.DrawString(text, textFont, sb, x, (bmp.Height - textSize.Height) / 2);
        return bmp;
    }
}
