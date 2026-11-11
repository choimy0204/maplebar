using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

using static MapleBar.Native;

namespace MapleBar;

/// <summary>
/// 바의 뮤직 칸을 끌어서 꺼내 원하는 곳에 두고, 다시 바 위로 끌어 놓으면 바 안으로 돌아간다.
/// 꺼낼 때는 칸 아래에서 물방울이 늘어나다 똑 떨어져 나와 칸 모양으로 펴진다.
/// </summary>
sealed partial class MusicDock
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
        BackColor = MusicPill.Paper;
    }

    public void Hold(MusicPill pill)
    {
        pill.Bare = true; // 칸 모양은 그리지 않고 이 창이 곧 칸이 된다
        pill.Margin = Padding.Empty;
        pill.Location = new Point(Pad, Pad);
        ClientSize = new Size(pill.Width + Pad * 2, pill.Height + Pad * 2);
        if (pill.Parent != this) Controls.Add(pill);
    }
    public void Release(MusicPill pill) { if (pill.Parent == this) { Controls.Remove(pill); pill.Bare = false; } }

    protected override bool ShowWithoutActivation => true;
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UI.RoundCorners(Handle);
        int border = 0xD2E0EC; // 옅은 베이지 테두리 (0x00BBGGRR)
        try { DwmSetWindowAttribute(Handle, 34, ref border, sizeof(int)); } catch { }
    }
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
