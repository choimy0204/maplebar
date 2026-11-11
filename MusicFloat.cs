using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

using static MapleBar.Native;

namespace MapleBar;

/// <summary>
/// 바의 뮤직 칸을 끌어서 꺼내 원하는 곳에 두고, 다시 바 위로 끌어 놓으면 바 안으로 돌아간다.
/// 꺼낼 때는 칸 아래에서 물방울이 늘어나다 똑 떨어져 나와 칸 모양으로 펴진다.
/// </summary>
sealed class MusicDock
{
    readonly Form bar;
    readonly Control slot;           // 바 안에서 뮤직 칸이 들어 있는 줄
    readonly MusicPill pill;
    readonly Settings st;
    readonly float sc;
    readonly Action relayout;
    readonly Func<Rectangle> barArea; // 지금 보이는 바 영역 (여기에 놓으면 다시 넣는다)
    int slotIndex;
    Padding slotMargin;
    MusicCard? card, spare;
    readonly Splash fx = new();
    readonly System.Windows.Forms.Timer tick = new() { Interval = 15 };
    readonly System.Diagnostics.Stopwatch clock = new();
    enum Mode { None, Stretch, SpringBack, Snap, MoveCard, DockIn }
    Mode mode;
    Point grab;          // 칸 안에서 잡은 자리
    PointF anchor;       // 물방울이 늘어나기 시작하는 칸 아래쪽 점
    Rectangle origin;    // 바에 있던 칸 자리 (화면)
    PointF springFrom, snapAt;
    Bitmap? shot;        // 칸 모양 사진 (펴질 때 안에 비친다)
    RectangleF dockFrom, dockTo;
    (PointF P, PointF V, float R)[] splash = Array.Empty<(PointF, PointF, float)>();

    const int SnapMs = 520, SpringMs = 170, DockMs = 320;
    int S(int v) => UI.Scale(v, sc);
    float Pull => S(70);

    public bool Floating => card != null;

    public MusicDock(Form bar, Control slot, MusicPill pill, Settings st, float sc, Action relayout, Func<Rectangle> barArea)
    {
        this.bar = bar; this.slot = slot; this.pill = pill; this.st = st; this.sc = sc;
        this.relayout = relayout; this.barArea = barArea;
        pill.DragStart += OnDragStart;
        // 효과 중에 오류가 나도 앱이 멈추지 않게 하고, 칸은 바로 돌려 놓는다
        tick.Tick += (_, _) => { try { Tick(); } catch { Recover(); } };
    }

    // ── 시작 ──
    void OnDragStart(Point at)
    {
        if (mode != Mode.None) return;
        grab = at;
        if (Floating) { mode = Mode.MoveCard; tick.Start(); return; }
        origin = pill.RectangleToScreen(pill.ClientRectangle);
        float r = origin.Height / 2f;
        anchor = new PointF(Math.Clamp(origin.Left + at.X, origin.Left + r, origin.Right - r), origin.Bottom - S(5));
        shot?.Dispose(); shot = Shot();
        mode = Mode.Stretch;
        clock.Restart();
        tick.Start();
    }

    Bitmap Shot()
    {
        var b = new Bitmap(Math.Max(1, pill.Width), Math.Max(1, pill.Height), PixelFormat.Format32bppArgb);
        try { pill.DrawToBitmap(b, pill.ClientRectangle); } catch { }
        return b;
    }

    void Tick()
    {
        var c = (PointF)Cursor.Position;
        bool down = (Control.MouseButtons & MouseButtons.Left) != 0;
        double ms = clock.Elapsed.TotalMilliseconds;
        switch (mode)
        {
            case Mode.Stretch:
                if (!down) { springFrom = c; mode = Mode.SpringBack; clock.Restart(); break; }
                if (Dist(c, anchor) >= Pull) { BeginSnap(c); break; }
                Render(g => DrawStretch(g, c, 1f));
                break;

            case Mode.SpringBack:
            {
                double p = Math.Min(1, ms / SpringMs);
                var q = (float)EaseOut(p);
                var pt = Lerp(springFrom, anchor, q);
                Render(g => DrawStretch(g, pt, 1 - q * 0.7f));
                if (p >= 1) Stop();
                break;
            }

            case Mode.Snap:
            {
                double p = Math.Min(1, ms / SnapMs);
                Render(g => DrawSnap(g, c, (float)p, ms));
                if (p >= 1)
                {
                    ShowCard(new Point((int)c.X - grab.X, (int)c.Y - grab.Y));
                    fx.Hide();
                    if (down) mode = Mode.MoveCard; else { Save(); Stop(); }
                }
                break;
            }

            case Mode.MoveCard:
                if (card == null) { Stop(); break; }
                if (down)
                {
                    card.Location = new Point((int)c.X - grab.X - card.Pad, (int)c.Y - grab.Y - card.Pad);
                    // 바에 넣을 자리를 주황 물방울로 보여 준다 (바 위에 올리면 커지며 퍼진다)
                    // 카드를 바 위에 올렸을 때만 보인다 (올리는 순간 톡 부풀어 나타난다)
                    if (barArea().Contains(Point.Round(c)) && DropSpot() is PointF spot)
                    {
                        if (overAt < 0) overAt = Environment.TickCount64;
                        float grow = (float)EaseOutBack(Math.Min(1, (Environment.TickCount64 - overAt) / 220.0));
                        int R = S(30);
                        Render(g => DrawSpot(g, spot, true, grow), new Rectangle((int)spot.X - R, (int)spot.Y - R, R * 2, R * 2));
                    }
                    else { overAt = -1; fx.Hide(); }
                    break;
                }
                if (barArea().Contains(Point.Round(c))) BeginDockIn();
                else { Save(); Stop(); }
                break;

            case Mode.DockIn:
            {
                double p = Math.Min(1, ms / DockMs);
                Render(g => DrawDock(g, (float)p));
                if (p >= 1) { pill.Ghost = false; fx.Hide(); Stop(); }
                break;
            }
        }
    }

    // 오류가 나면 효과를 멈추고 칸이 어딘가에 꼭 보이게 돌려 놓는다
    void Recover()
    {
        try
        {
            pill.Ghost = false;
            if (pill.Parent == null)
            {
                if (card != null) { card.Hold(pill); if (!card.Visible) card.Show(); }
                else { pill.Margin = slotMargin; slot.Controls.Add(pill); relayout(); }
            }
        }
        catch { }
        Stop();
    }

    void Stop()
    {
        mode = Mode.None;
        overAt = -1;
        tick.Stop();
        fx.Hide();
        fxBmp?.Dispose(); fxBmp = null; // 다음 효과 때 다시 만든다 (창은 그림을 복사해 갔다)
    }

    // ── 떨어져 나오기 ──
    void BeginSnap(PointF c)
    {
        snapAt = Lerp(anchor, c, 0.45f);
        // 끊어질 때 작은 방울 몇 개가 튄다
        var dir = Norm(new PointF(c.X - anchor.X, c.Y - anchor.Y));
        var n = new PointF(-dir.Y, dir.X);
        splash = new[]
        {
            (snapAt, new PointF(n.X * 0.10f - dir.X * 0.05f, n.Y * 0.10f - 0.10f), S(3) * 1f),
            (snapAt, new PointF(-n.X * 0.09f - dir.X * 0.04f, -n.Y * 0.09f - 0.12f), S(2) * 1.2f),
            (snapAt, new PointF(dir.X * 0.02f, -0.16f), S(2) * 1f),
        };
        Detach();
        mode = Mode.Snap;
        clock.Restart();
    }

    void Detach()
    {
        slotIndex = slot.Controls.GetChildIndex(pill);
        slotMargin = pill.Margin;
        slot.Controls.Remove(pill);
        relayout();
    }

    void ShowCard(Point pillAt)
    {
        card ??= spare ?? new MusicCard(sc); spare = null;
        card.Hold(pill);
        card.Location = Clamp(new Point(pillAt.X - card.Pad, pillAt.Y - card.Pad), card.Size);
        if (!card.Visible) card.Show();
    }

    void Save()
    {
        if (card == null) return;
        st.MusicFloat = true; st.MusicX = card.Left; st.MusicY = card.Top; st.Save();
    }

    // ── 다시 넣기 ──
    void BeginDockIn()
    {
        if (card == null) return;
        dockFrom = card.RectangleToScreen(new Rectangle(pill.Location, pill.Size));
        shot?.Dispose(); shot = Shot();
        card.Release(pill);
        card.Hide(); spare = card; card = null; // 다 넣었으면 카드는 숨겨 두고 다음에 다시 쓴다
        pill.Margin = slotMargin;
        pill.Ghost = true;
        slot.Controls.Add(pill);
        slot.Controls.SetChildIndex(pill, Math.Min(slotIndex, slot.Controls.Count - 1));
        relayout();
        dockTo = pill.RectangleToScreen(pill.ClientRectangle);
        st.MusicFloat = false; st.Save();
        mode = Mode.DockIn;
        clock.Restart();
        tick.Start();
    }

    /// <summary>메뉴에서 "바에 다시 넣기"를 고르면.</summary>
    public void DockBack()
    {
        if (!Floating || mode != Mode.None) return;
        BeginDockIn();
    }

    /// <summary>지난번에 꺼내 둔 자리로 바로 꺼내 둔다 (효과 없이).</summary>
    public void RestoreFloat()
    {
        if (Floating || pill.Parent != slot) return;
        Detach();
        ShowCard(new Point(st.MusicX ?? bar.Left + S(40), st.MusicY ?? bar.Bottom + S(60)));
        card!.Location = Clamp(new Point(st.MusicX ?? card.Left, st.MusicY ?? card.Top), card.Size);
    }

    /// <summary>바가 자기를 맨 위로 올린 뒤 부른다. 꺼낸 카드(와 거기 붙은 소리 조절기)가 바 뒤에 깔리지 않게 그 위로 다시 올린다.</summary>
    public void RaiseCard()
    {
        if (card is not { Visible: true, IsHandleCreated: true }) return;
        SetWindowPos(card.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_KEEP);
    }

    /// <summary>뮤직이 켜지고 꺼질 때, 바를 숨기고 다시 열 때 꺼내 둔 칸도 같이 숨기고 보인다.</summary>
    public void SetShown(bool show)
    {
        if (card == null) return;
        if (show && !card.Visible) card.Show();
        else if (!show && card.Visible) card.Hide();
    }

    static Point Clamp(Point p, Size s)
    {
        var wa = Screen.FromPoint(new Point(p.X + s.Width / 2, p.Y + s.Height / 2)).WorkingArea;
        return new Point(Math.Max(wa.Left, Math.Min(p.X, wa.Right - s.Width)), Math.Max(wa.Top, Math.Min(p.Y, wa.Bottom - s.Height)));
    }

    // ── 그리기 ──
    static readonly Color Fill = UI.SoftAccent, Edge = Color.FromArgb(255, 214, 160);

    Bitmap? fxBmp; // Render가 15ms마다 쓰는 판

    void Render(Action<Graphics> draw, Rectangle? area = null)
    {
        // 그릴 범위: 바 칸 자리와 마우스 주변을 모두 덮는다
        var c = Cursor.Position;
        var r = Rectangle.Union(origin, new Rectangle(c.X - pill.Width - S(40), c.Y - pill.Height - S(40), pill.Width * 2 + S(80), pill.Height * 2 + S(80)));
        if (mode == Mode.DockIn) r = Rectangle.Union(Rectangle.Round(dockFrom), Rectangle.Round(dockTo));
        if (area is Rectangle a) r = a;
        else r.Inflate(S(30), S(30));
        r.Intersect(SystemInformation.VirtualScreen);
        if (r.Width <= 0 || r.Height <= 0) return;
        // 크기가 같으면 지난 판을 지우고 다시 쓴다 (새 판과 같게 모든 칸을 0으로)
        if (fxBmp == null || fxBmp.Width != r.Width || fxBmp.Height != r.Height)
        {
            fxBmp?.Dispose();
            fxBmp = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb);
        }
        else using (var cg = Graphics.FromImage(fxBmp)) cg.Clear(Color.FromArgb(0, 0, 0, 0));
        var bmp = fxBmp;
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.TranslateTransform(-r.X, -r.Y);
            draw(g);
        }
        fx.Present(bmp, r.Location);
    }

    // 칸 아래에서 마우스 쪽으로 늘어나는 물방울 (k: 크기 비율)
    void DrawStretch(Graphics g, PointF c, float k)
    {
        float d = Dist(c, anchor);
        if (d < S(4)) return;
        float t = Math.Clamp(d / Pull, 0, 1);
        float rDrop = (S(8) + S(6) * t) * k;
        float wBase = S(9) * (1 - 0.35f * t) * k;
        float wMid = (S(6) * (1 - t) + S(1.6f)) * k;
        DrawNeck(g, anchor, wBase, c, rDrop, wMid, 255);
        DrawDrop(g, c, rDrop, rDrop * (1 + 0.12f * t), 255);
    }

    int S(float v) => UI.Scale(v, sc);

    void DrawSnap(Graphics g, PointF c, float p, double ms)
    {
        // 바에 남은 자리: 칸 모양이 줄어들며 사라진다
        if (p < 0.32f)
        {
            float k = p / 0.32f;
            var o = new RectangleF(origin.X, origin.Y, origin.Width, origin.Height);
            var ghost = Scale(o, 1 - 0.12f * k, 1 - 0.35f * k);
            DrawBlob(g, ghost, (int)(255 * (1 - k)), 1 - k * 1.6f);
        }
        // 끊어진 목은 위로 짧게 튕겨 올라간다
        if (p < 0.2f)
        {
            float k = 1 - p / 0.2f;
            var dir = Norm(new PointF(c.X - anchor.X, c.Y - anchor.Y));
            var tip = new PointF(anchor.X + dir.X * S(16) * k, anchor.Y + dir.Y * S(16) * k);
            DrawNeck(g, anchor, S(6) * k, tip, S(2.5f) * k + 0.5f, S(2) * k + 0.5f, 255);
        }
        // 튀는 작은 방울
        if (p < 0.55f)
        {
            int a = (int)(255 * (1 - p / 0.55f));
            foreach (var (P, V, R) in splash)
            {
                float t = (float)ms;
                var q = new PointF(P.X + V.X * t * sc, P.Y + (V.Y * t + 0.00045f * t * t) * sc);
                DrawDrop(g, q, R, R, a);
            }
        }
        // 떨어진 방울: 출렁이다가 칸 모양으로 펴진다
        float rEnd = S(14);
        if (p < 0.24f)
        {
            float q = p / 0.24f;
            float w = (float)Math.Sin(q * Math.PI * 2) * 0.28f * (1 - q);
            DrawDrop(g, c, rEnd * (1 + w), rEnd * (1 - w), 255);
            return;
        }
        float e = (float)EaseOutBack((p - 0.24f) / 0.76f);
        var from = new RectangleF(c.X - rEnd, c.Y - rEnd, rEnd * 2, rEnd * 2);
        var to = new RectangleF(c.X - grab.X, c.Y - grab.Y, pill.Width, pill.Height);
        var rr = new RectangleF(
            from.X + (to.X - from.X) * e, from.Y + (to.Y - from.Y) * e,
            Math.Max(2, from.Width + (to.Width - from.Width) * e), Math.Max(2, from.Height + (to.Height - from.Height) * e));
        DrawBlob(g, rr, 255, Math.Clamp(((p - 0.24f) / 0.76f - 0.45f) / 0.4f, 0, 1));
    }

    // 꺼낸 칸이 바 안 자리로 쏙 들어간다: 가운데에서 동그랗게 오므라들었다가 자리에서 다시 펴진다
    void DrawDock(Graphics g, float p)
    {
        float e = (float)EaseInOut(p);
        float cx = Mid(dockFrom).X + (Mid(dockTo).X - Mid(dockFrom).X) * e;
        float cy = Mid(dockFrom).Y + (Mid(dockTo).Y - Mid(dockFrom).Y) * e;
        float squeeze = (float)Math.Sin(p * Math.PI);
        float w = (dockFrom.Width + (dockTo.Width - dockFrom.Width) * e);
        float h = (dockFrom.Height + (dockTo.Height - dockFrom.Height) * e);
        w = Math.Max(h * 0.9f, w * (1 - 0.72f * squeeze));
        h *= 1 - 0.12f * squeeze;
        DrawBlob(g, new RectangleF(cx - w / 2, cy - h / 2, w, h), 255, 1 - squeeze * 1.4f);
    }

    // 바 안에서 뮤직 칸이 다시 들어갈 자리 (화면 좌표의 가운데). 바가 접혀 안 보이면 null
    PointF? DropSpot()
    {
        var s = slot.RectangleToScreen(slot.ClientRectangle);
        int n = slot.Controls.Count, i = Math.Min(slotIndex, n);
        float x;
        if (n == 0) x = s.Left + S(14);
        else if (i == 0) x = s.Left + slot.Controls[0].Left - slot.Controls[0].Margin.Left - S(26);
        else if (i == n) x = s.Left + slot.Controls[n - 1].Right + S(14);
        else x = s.Left + (slot.Controls[i - 1].Right + slot.Controls[i].Left) / 2f;
        var p = new PointF(x, s.Top + s.Height / 2f);
        return barArea().Contains(Point.Round(p)) ? p : null;
    }

    // 넣는 자리 표시: 동그란 주황 비눗방울. 평소엔 살짝 숨 쉬고, 바 위에 올리면 커지며 고리가 퍼진다
    long overAt = -1; // 카드가 바 위에 처음 올라온 시각
    void DrawSpot(Graphics g, PointF c, bool over, float grow = 1)
    {
        double t = Environment.TickCount64 % 100000;
        float breathe = 1 + 0.07f * (float)Math.Sin(t / 1100 * Math.PI * 2);
        float r = S(over ? 16 : 13) * breathe * Math.Max(0.05f, grow);
        if (r < 1) return;
        var accent = UI.Accent;
        var box = new RectangleF(c.X - r, c.Y - r, r * 2, r * 2);
        if (over)
        {
            // 바깥으로 퍼지는 고리
            float ph = (float)(t / 900 % 1);
            float rr = r * (1.05f + 0.45f * ph);
            using var ring = new Pen(Color.FromArgb((int)(140 * (1 - ph)), accent), Math.Max(1.5f, 2 * sc));
            g.DrawEllipse(ring, c.X - rr, c.Y - rr, rr * 2, rr * 2);
        }
        // 비눗방울: 속은 옅게 비치고 가장자리로 갈수록 주황이 짙어진다
        using (var path = new GraphicsPath())
        {
            path.AddEllipse(box);
            using var pg = new PathGradientBrush(path)
            {
                CenterPoint = new PointF(c.X - r * 0.25f, c.Y - r * 0.25f),
                CenterColor = Color.FromArgb(over ? 70 : 40, accent),
                SurroundColors = new[] { Color.FromArgb(over ? 200 : 150, accent) },
            };
            g.FillPath(pg, path);
        }
        using (var rim = new Pen(Color.FromArgb(over ? 255 : 210, accent), Math.Max(1.5f, 1.8f * sc)))
            g.DrawEllipse(rim, box);
        // 반짝이는 빛: 왼쪽 위 굽은 띠 + 작은 점
        using (var hi = new Pen(Color.FromArgb(220, Color.White), Math.Max(1.5f, 2.2f * sc)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawArc(hi, c.X - r * 0.68f, c.Y - r * 0.68f, r * 1.36f, r * 1.36f, 195, 55);
        using (var dot = new SolidBrush(Color.FromArgb(230, Color.White)))
            g.FillEllipse(dot, c.X + r * 0.32f, c.Y + r * 0.3f, r * 0.2f, r * 0.2f);
    }

    // 칸 모양 (둥근 알약). content는 칸 사진이 비치는 정도
    void DrawBlob(Graphics g, RectangleF r, int alpha, float content)
    {
        if (r.Width < 1 || r.Height < 1 || alpha <= 0) return;
        using var path = UI.RoundRect(r, Math.Min(r.Height, r.Width) / 2f);
        using (var b = new SolidBrush(Color.FromArgb(alpha, Fill))) g.FillPath(b, path);
        if (shot != null && content > 0)
        {
            var state = g.Save();
            g.SetClip(path);
            using var ia = new ImageAttributes();
            ia.SetColorMatrix(new ColorMatrix { Matrix33 = Math.Clamp(content, 0, 1) * alpha / 255f });
            g.DrawImage(shot, Rectangle.Round(r), 0, 0, shot.Width, shot.Height, GraphicsUnit.Pixel, ia);
            g.Restore(state);
        }
        using (var pen = new Pen(Color.FromArgb(alpha, Edge), Math.Max(1f, sc))) g.DrawPath(pen, path);
    }

    // 동그란 물방울 + 살짝 비치는 빛
    void DrawDrop(Graphics g, PointF c, float rx, float ry, int alpha)
    {
        if (rx < 0.5f || alpha <= 0) return;
        var r = new RectangleF(c.X - rx, c.Y - ry, rx * 2, ry * 2);
        using (var b = new SolidBrush(Color.FromArgb(alpha, Fill))) g.FillEllipse(b, r);
        using (var pen = new Pen(Color.FromArgb(alpha, Edge), Math.Max(1f, sc))) g.DrawEllipse(pen, r);
        if (rx > S(4))
            using (var b = new SolidBrush(Color.FromArgb(alpha * 150 / 255, Color.White)))
                g.FillEllipse(b, c.X - rx * 0.55f, c.Y - ry * 0.6f, rx * 0.5f, ry * 0.38f);
    }

    // a(폭 wa)에서 c(반지름 rc)로 가운데가 잘록하게(wm) 이어지는 물방울 목
    static void DrawNeck(Graphics g, PointF a, float wa, PointF c, float rc, float wm, int alpha)
    {
        var dir = Norm(new PointF(c.X - a.X, c.Y - a.Y));
        var n = new PointF(-dir.Y, dir.X);
        var m = Lerp(a, c, 0.5f);
        PointF P(PointF o, float k) => new(o.X + n.X * k, o.Y + n.Y * k);
        using var path = new GraphicsPath();
        path.AddBezier(P(a, wa), P(Lerp(a, m, 0.6f), wm), P(Lerp(m, c, 0.4f), wm), P(c, rc * 0.92f));
        path.AddLine(P(c, rc * 0.92f), P(c, -rc * 0.92f));
        path.AddBezier(P(c, -rc * 0.92f), P(Lerp(m, c, 0.4f), -wm), P(Lerp(a, m, 0.6f), -wm), P(a, -wa));
        path.CloseFigure();
        using var b = new SolidBrush(Color.FromArgb(alpha, Fill));
        g.FillPath(b, path);
        using var pen = new Pen(Color.FromArgb(alpha, Edge), 1f);
        // 옆선만 그린다 (위쪽은 칸에, 아래쪽은 방울에 붙어 있어야 한다)
        g.DrawBezier(pen, P(a, wa), P(Lerp(a, m, 0.6f), wm), P(Lerp(m, c, 0.4f), wm), P(c, rc * 0.92f));
        g.DrawBezier(pen, P(a, -wa), P(Lerp(a, m, 0.6f), -wm), P(Lerp(m, c, 0.4f), -wm), P(c, -rc * 0.92f));
    }

    static RectangleF Scale(RectangleF r, float kx, float ky)
    {
        float w = r.Width * kx, h = r.Height * ky;
        return new RectangleF(r.X + (r.Width - w) / 2, r.Y + (r.Height - h) / 2, w, h);
    }
    static PointF Mid(RectangleF r) => new(r.X + r.Width / 2, r.Y + r.Height / 2);
    static float Dist(PointF a, PointF b) => (float)Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    static PointF Lerp(PointF a, PointF b, float t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
    static PointF Norm(PointF v) { float l = (float)Math.Sqrt(v.X * v.X + v.Y * v.Y); return l < 0.001f ? new PointF(0, 1) : new PointF(v.X / l, v.Y / l); }
    static double EaseOut(double t) => 1 - Math.Pow(1 - t, 3);
    static double EaseInOut(double t) => t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
    static double EaseOutBack(double t) { const double c1 = 1.9, c3 = c1 + 1; return 1 + c3 * Math.Pow(t - 1, 3) + c1 * Math.Pow(t - 1, 2); }
}

/// <summary>바에서 꺼낸 뮤직 칸을 담는 작은 흰 카드 창.</summary>
sealed class MusicCard : Form
{
    public readonly int Pad;
    public MusicCard(float sc)
    {
        Pad = (int)Math.Round(4 * sc);
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.White;
    }

    public void Hold(MusicPill pill)
    {
        pill.Margin = Padding.Empty;
        pill.Location = new Point(Pad, Pad);
        ClientSize = new Size(pill.Width + Pad * 2, pill.Height + Pad * 2);
        if (pill.Parent != this) Controls.Add(pill);
    }
    public void Release(MusicPill pill) { if (pill.Parent == this) Controls.Remove(pill); }

    protected override bool ShowWithoutActivation => true;
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); UI.RoundCorners(Handle); }
    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ClassStyle |= CS_DROPSHADOW; /* 그림자 */ return cp; }
    }
}

/// <summary>마우스를 막지 않는 투명 효과 창. 반투명 가장자리까지 그대로 화면에 올린다.</summary>
sealed class Splash : Form
{
    public Splash()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
    }
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    public void Present(Bitmap bmp, Point at)
    {
        if (!Visible) { Show(); TopMost = true; } // 내용을 올리기 전이라 아직 보이지 않는다
        // 바보다 위에 그린다 (바를 눌러 바가 앞으로 와도)
        SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_KEEP);
        IntPtr screen = GetDC(IntPtr.Zero), mem = CreateCompatibleDC(screen), hb = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
            hb = bmp.GetHbitmap(Color.FromArgb(0));
            old = SelectObject(mem, hb);
            var size = new SIZE { cx = bmp.Width, cy = bmp.Height };
            var src = new POINT();
            var dst = new POINT { x = at.X, y = at.Y };
            var blend = new BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
            UpdateLayeredWindow(Handle, screen, ref dst, ref size, mem, ref src, 0, ref blend, 2);
        }
        finally
        {
            if (old != IntPtr.Zero) SelectObject(mem, old);
            if (hb != IntPtr.Zero) DeleteObject(hb);
            DeleteDC(mem);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    /// <summary>그림은 그대로 두고 창 전체 투명도만 바꾼다 (큰 그림을 매번 다시 올리지 않으려고).</summary>
    public void SetAlpha(byte a)
    {
        if (!IsHandleCreated) return;
        var blend = new BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = a, AlphaFormat = 1 };
        UpdateLayeredWindow(Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, ref blend, 2);
    }
    [DllImport("user32.dll", EntryPoint = "UpdateLayeredWindow")]
    static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, IntPtr pptDst, IntPtr psize, IntPtr hdcSrc, IntPtr pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

    [StructLayout(LayoutKind.Sequential)] struct POINT { public int x, y; }
    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
    [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
}
