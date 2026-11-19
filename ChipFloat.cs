using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using static MapleBar.Native;

namespace MapleBar;

/// <summary>바의 남은 시간 칩을 끌어서 떼어 내 화면 아무 곳에나 둔다.
/// · 바에서 끌면 바에 있던 칩이 모두 한 덩어리로 떨어져 나온다 (나중에 켜지는 칩도 그 덩어리 옆에 생긴다).
/// · 덩어리에서 칩 하나를 끌면 쭉 늘어나다 떨어져 따로 논다. 가까이 가져가면 자석처럼 끌려가 찰싹 붙는다.
/// · 바에 놓으면 덩어리째(또는 하나만) 바의 제자리로 빨려 들어간다.
/// 덩어리와 자리는 설정에 저장해 다시 켜도 그대로 뜬다.</summary>
sealed class ChipDock
{
    readonly Settings st;
    readonly Action relayout;
    readonly Func<bool> shown;
    readonly List<TimerChip> chips = new();
    readonly List<ChipGroup> groups = new();
    int beat;

    public Form Bar { get; }
    public float Sc { get; }
    public int Gap => S(4);

    /// <summary>떼어 낸 칩을 보여도 되는지 (바가 보이거나, 직접 숨겼어도 남기기로 했을 때)</summary>
    public bool Shown => shown();

    public ChipDock(Settings st, Form bar, float sc, Action relayout, Func<bool> shown)
    {
        this.st = st; Bar = bar; Sc = sc; this.relayout = relayout; this.shown = shown;
    }

    int S(float v) => UI.Scale(v, Sc);

    /// <summary>칩을 끌 수 있게 한다 (칩을 다 넣은 뒤 Restore로 저장된 덩어리를 되살린다)</summary>
    public void Add(TimerChip chip)
    {
        chips.Add(chip);
        chip.DragOut += PullOut;
        chip.Changed += () => GroupOf(chip)?.Wake();
        // 바에서 끌어낸 직후엔 마우스를 바의 칩이 쥐고 있으니, 여기서도 바로 따라오게 한다
        chip.MouseMove += (_, e) => { if (e.Button == MouseButtons.Left) GroupOf(chip)?.Pump(); };
    }

    ChipGroup? GroupOf(TimerChip chip) => groups.FirstOrDefault(g => g.Has(chip));

    /// <summary>저장된 덩어리를 그 자리에 되살린다. 값: [x, y, 덩어리 번호, 순서] (옛 [x, y]는 혼자인 덩어리)</summary>
    public void Restore()
    {
        var sets = new Dictionary<int, List<(int ord, TimerChip c, Point at)>>();
        int solo = 1 << 20;
        foreach (var c in chips)
        {
            if (!st.ChipSpots.TryGetValue(c.Key, out var p) || p is not { Length: >= 2 }) continue;
            int gid = p.Length >= 4 ? p[2] : solo++;
            int ord = p.Length >= 4 ? p[3] : 0;
            if (!sets.TryGetValue(gid, out var list)) sets[gid] = list = new();
            list.Add((ord, c, new Point(p[0], p[1])));
        }
        foreach (var list in sets.Values)
        {
            var row = list.OrderBy(e => e.ord).ToList();
            var at = row[0].at;
            if (!OnAnyScreen(at, row[0].c.Size)) continue; // 모니터를 빼서 화면 밖이 된 자리는 버린다 (바에 남는다)
            foreach (var e in row) e.c.Detached = true;
            groups.Add(ChipGroup.Restored(this, row.Select(e => e.c), at));
        }
        if (groups.Count > 0) relayout();
        Save();
    }

    public void Save()
    {
        st.ChipSpots.Clear();
        for (int gi = 0; gi < groups.Count; gi++)
        {
            var g = groups[gi];
            var at = g.Spot;
            int oi = 0;
            foreach (var c in g.Chips) st.ChipSpots[c.Key] = new[] { at.X, at.Y, gi, oi++ };
        }
        st.Save();
    }

    /// <summary>바의 칩을 끌기 시작: 바에 남은 칩이 모두 한 덩어리로 떨어져 나온다 (꺼져 있는 칩도 덩어리에 들어가 켜지면 옆에 생긴다)</summary>
    void PullOut(TimerChip chip, Point grab)
    {
        if (!Bar.Visible) return;
        var onBar = chips.Where(c => !c.Detached && !c.Ghost).ToList();
        var shownNow = onBar.Where(c => c.Visible).OrderBy(c => c.RectangleToScreen(c.ClientRectangle).X).ToList();
        if (!shownNow.Contains(chip)) return;
        var rects = shownNow.Select(c => (RectangleF)c.RectangleToScreen(c.ClientRectangle)).ToList();
        var hidden = onBar.Where(c => !c.Visible).ToList();
        var at = chip.PointToScreen(grab);
        // 바의 자리는 비워 둔 채로 시작한다 (바 위에 있는 동안 놓으면 그대로 다시 들어간다)
        foreach (var c in onBar) c.Ghost = true;
        groups.Add(ChipGroup.FromBar(this, shownNow, rects, hidden, chip, at));
        Save();
    }

    /// <summary>끄는 덩어리가 바 위에 있는 동안 바에 들어갈 자리를 벌려 둔다 (그리지는 않는다)</summary>
    public void Pocket(ChipGroup g, bool on)
    {
        foreach (var c in g.Chips) { c.Ghost = on; c.Detached = !on; }
        relayout();
    }

    /// <summary>떼어 낸 알림 창을 모두 바보다 위로 올린다 (바가 스스로를 맨 위로 올린 뒤에 부른다)</summary>
    public void RaiseAll()
    {
        foreach (var g in groups) g.RaiseUp();
        foreach (var f in Application.OpenForms.OfType<ChipFlyer>().ToList()) f.Raise();
    }

    /// <summary>칩 시간이 바뀔 때마다 부른다: 떠 있는 덩어리를 보이거나 숨긴다</summary>
    public void Sync()
    {
        bool raise = ++beat % 4 == 0; // 1초에 한 번 맨 위로 (바나 게임에 가려지지 않게)
        bool show = Shown;
        foreach (var g in groups.ToList()) g.Sync(show, raise);
    }

    /// <summary>여기에 놓으면 바에 들어간다: 바와 그 둘레 조금</summary>
    public bool OverBar(Point p) => Bar.Visible && Rectangle.Inflate(Bar.Bounds, S(18), S(22)).Contains(p);

    // ── 자석 ──

    /// <summary>끌고 있는 덩어리 g가 붙을 만한 덩어리와 끼어들 자리를 찾는다.
    /// at: 그 덩어리에서 앞에 남는 칩 수 (0이면 맨 앞, 칩 수와 같으면 맨 뒤, 그 사이면 비집고 들어간다) · s: 0(멀다)~1(딱 맞는 자리)</summary>
    public ChipGroup? FindMagnet(ChipGroup g, PointF want, out PointF snap, out float s, out int at)
    {
        float range = S(44);
        ChipGroup? best = null;
        float bd = range;
        float w = g.RowWidthT;
        snap = want; at = 0;
        foreach (var t in groups)
        {
            if (t == g || !t.CanJoin) continue;
            foreach (var (i, p) in t.Snaps(w))
            {
                float d = Dist(want, p);
                if (d < bd) { bd = d; best = t; snap = p; at = i; }
            }
        }
        s = best == null ? 0 : 1 - bd / range;
        foreach (var t in groups) if (t != g) t.Court(t == best ? s : 0, t == best ? at : -1, w);
        return best;
    }

    public void ClearCourt() { foreach (var t in groups) t.Court(0, -1, 0); }

    static float Dist(PointF a, PointF b)
    {
        float dx = a.X - b.X, dy = (a.Y - b.Y) * 1.3f; // 옆으로 붙으니 위아래로 어긋난 건 더 멀게 친다
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>moving을 target의 at 자리(앞·뒤·사이)에 끼운다 (찰싹)</summary>
    public void Merge(ChipGroup moving, ChipGroup target, int at)
    {
        ClearCourt();
        target.Absorb(moving.TakeAll(), at, moving.RowWidthT);
        groups.Remove(moving);
        moving.Hide();
        moving.Dispose();
        Save();
    }

    /// <summary>덩어리에서 칩 하나가 떨어져 나와 새 덩어리가 된다</summary>
    public void SplitOff(ChipGroup fresh)
    {
        groups.Add(fresh);
        Save();
    }

    /// <summary>덩어리를 바에 넣는다: 바에 자리를 먼저 비워 두고(그리지는 않고) 칩마다 그 자리로 빨려 들어간다</summary>
    public void IntoBar(ChipGroup g)
    {
        var fly = new List<(TimerChip c, RectangleF from)>();
        foreach (var (c, r, seen) in g.Departing())
        {
            bool go = seen && c.On;
            if (go) fly.Add((c, r));
            c.Ghost = go;
            c.Detached = false;
        }
        relayout();
        // 바의 제자리에서 가까운 칩부터 차례로
        var order = fly.OrderBy(f =>
        {
            var to = f.c.RectangleToScreen(f.c.ClientRectangle);
            float dx = to.X + to.Width / 2f - (f.from.X + f.from.Width / 2f), dy = to.Y + to.Height / 2f - (f.from.Y + f.from.Height / 2f);
            return dx * dx + dy * dy;
        }).ToList();
        for (int i = 0; i < order.Count; i++) new ChipFlyer(this, order[i].c, order[i].from, i * 0.045f);
        groups.Remove(g);
        g.Hide();
        g.Dispose();
        Save();
    }

    /// <summary>칩이 다 보이게 그 모니터 작업 영역 안으로 넣는다</summary>
    public static Point Clamp(Point spot, Size size)
    {
        var wa = Screen.FromPoint(new Point(spot.X + size.Width / 2, spot.Y + size.Height / 2)).WorkingArea;
        return new Point(Math.Clamp(spot.X, wa.Left, Math.Max(wa.Left, wa.Right - size.Width)),
                         Math.Clamp(spot.Y, wa.Top, Math.Max(wa.Top, wa.Bottom - size.Height)));
    }

    static bool OnAnyScreen(Point spot, Size size) =>
        Screen.AllScreens.Any(s => s.WorkingArea.Contains(spot.X + size.Width / 2, spot.Y + size.Height / 2));
}

/// <summary>값이 목표로 용수철처럼 다가간다. 중간에 목표가 바뀌어도 속도가 이어져 끊기지 않는다.</summary>
struct ChipSpring
{
    public float V, Vel, T;
    public void Set(float v) { V = T = v; Vel = 0; }
    public void Snap() { V = T; Vel = 0; }
    public void Step(float dt, float k, float z)
    {
        float acc = k * (T - V) - 2 * z * MathF.Sqrt(k) * Vel;
        Vel += acc * dt;
        V += Vel * dt;
    }
    public bool Rest(float eps) => Math.Abs(T - V) < eps && Math.Abs(Vel) < eps * 20;
}

/// <summary>떠 있는 창의 공통: 겹창(그림 그대로 투명) + 움직일 때만 도는 타이머.
/// 판은 먼저 흰 틀(mask)에 그린 뒤 그림자·테두리·바탕을 한꺼번에 입혀, 여러 판이 붙으면 한 몸처럼 보인다.</summary>
abstract class GooWindow : Form
{
    protected readonly ChipDock dock;
    readonly System.Windows.Forms.Timer anim = new() { Interval = 10 };
    readonly Stopwatch clock = Stopwatch.StartNew();
    double last;
    LayerSurface? surf;          // 창에 바로 올리는 그림판 (한 번 만들어 계속 씀)
    protected Bitmap? mask, small;
    protected Bitmap Frame => surf!.Bmp;

    protected GooWindow(ChipDock dock)
    {
        this.dock = dock;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        Owner = dock.Bar; // 바보다 늘 위에
        anim.Tick += (_, _) => Step();
    }

    void Step()
    {
        double now = clock.Elapsed.TotalSeconds;
        float dt = (float)Math.Clamp(now - last, 0.001, 0.05);
        last = now;
        Tick(dt);
    }

    /// <summary>마우스가 움직이면 타이머를 기다리지 않고 바로 한 번 그린다 (끄는 손을 바로 따라오게)</summary>
    public void Pump()
    {
        if (IsDisposed || !anim.Enabled) return;
        if (clock.Elapsed.TotalSeconds - last >= 0.004) Step();
    }

    protected int S(float v) => UI.Scale(v, dock.Sc);

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
        if (disposing) { anim.Stop(); anim.Dispose(); surf?.Dispose(); mask?.Dispose(); small?.Dispose(); surf = null; mask = small = null; }
        base.Dispose(disposing);
    }

    protected abstract void Tick(float dt);

    protected void Kick()
    {
        if (IsDisposed || anim.Enabled) return;
        last = clock.Elapsed.TotalSeconds - 0.01;
        anim.Start();
    }

    protected void Halt() => anim.Stop();

    public void Raise() { if (Visible && !IsDisposed) SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_KEEP); }

    /// <summary>그림판 크기를 맞춘다 (자주 바꾸지 않게 64 단위로 올림)</summary>
    protected void Ensure(int w, int h)
    {
        int rw = (w + 63) / 64 * 64, rh = (h + 63) / 64 * 64;
        if (surf != null && surf.Width == rw && surf.Height == rh) return;
        surf?.Dispose(); mask?.Dispose(); small?.Dispose();
        surf = new LayerSurface(rw, rh);
        mask = new Bitmap(rw, rh, PixelFormat.Format32bppArgb);
        small = new Bitmap(rw / 4, rh / 4, PixelFormat.Format32bppArgb);
    }

    protected void Present(Point origin)
    {
        if (IsDisposed || surf == null) return;
        if (!Visible) { Show(); SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_KEEP); }
        surf.Push(Handle, origin);
    }

    protected static Graphics Begin(Bitmap b)
    {
        var g = Graphics.FromImage(b);
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        return g;
    }

    /// <summary>흰 틀(mask)로 판을 그린다: 흐린 그림자 → (붙일 곳 표시) 빛 → 1px 테두리 → 바탕.
    /// up: 떠 있는 정도(0이면 바의 칩처럼 판·그림자 없음) · fill: 바탕 진하기</summary>
    protected void Compose(Graphics g, float up, float lift, float hint, float fill = 1)
    {
        if (mask == null || small == null) return;
        using (var sg = Graphics.FromImage(small))
        {
            sg.Clear(Color.Transparent);
            sg.InterpolationMode = InterpolationMode.HighQualityBilinear;
            sg.DrawImage(mask, new Rectangle(0, 0, small.Width, small.Height), 0, 0, mask.Width, mask.Height, GraphicsUnit.Pixel);
        }
        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        var full = new RectangleF(0, 0, mask.Width, mask.Height);
        if (up > 0.01f)
        {
            // 그림자: 작게 줄였다 늘린 흐린 틀. 들수록 아래로 깊고 진하다
            float lf = Math.Clamp(lift, 0, 1.3f);
            float drop = S(1) + S(3) * lf;
            float a = (UI.Dark ? 0.5f : 0.2f + 0.1f * lf) * up;
            var r = full; r.Offset(0, drop);
            Tint(g, small, r, Color.Black, a);
            r = RectangleF.Inflate(full, S(2) * lf, S(2) * lf); r.Offset(0, drop * 1.6f);
            Tint(g, small, r, Color.Black, a * 0.5f * lf);
        }
        if (hint > 0.01f) Tint(g, small, RectangleF.Inflate(full, S(2), S(2)), UI.Accent, 0.75f * hint);
        if (up > 0.01f)
        {
            var line = Lerp(UI.Line, UI.Accent, Math.Clamp(hint, 0, 1));
            float a = up * (0.55f + 0.45f * Math.Clamp(hint, 0, 1));
            foreach (var (dx, dy) in Around)
            {
                var r = full; r.Offset(dx, dy);
                Tint(g, mask, r, line, a);
            }
        }
        if (fill > 0.01f) Tint(g, mask, full, UI.Back, fill);
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    }

    /// <summary>src의 모양(투명도)만 써서 한 가지 색으로 칠한다</summary>
    static readonly (int, int)[] Around = { (-1, 0), (1, 0), (0, -1), (0, 1) };
    static readonly ColorMatrix tintM = new();
    static readonly ImageAttributes tintA = new();
    static readonly PointF[] tintP = new PointF[3];

    static void Tint(Graphics g, Bitmap src, RectangleF dst, Color c, float a)
    {
        // 색 바꾸는 표는 하나를 계속 고쳐 쓴다 (매번 새로 만들지 않게)
        tintM.Matrix00 = tintM.Matrix11 = tintM.Matrix22 = 0;
        tintM.Matrix33 = Math.Clamp(a, 0, 1);
        tintM.Matrix40 = c.R / 255f; tintM.Matrix41 = c.G / 255f; tintM.Matrix42 = c.B / 255f;
        tintA.SetColorMatrix(tintM);
        tintA.SetWrapMode(WrapMode.TileFlipXY); // 가장자리 번짐 막기
        tintP[0] = dst.Location; tintP[1] = new PointF(dst.Right, dst.Top); tintP[2] = new PointF(dst.Left, dst.Bottom);
        g.DrawImage(src, tintP, new RectangleF(0, 0, src.Width, src.Height), GraphicsUnit.Pixel, tintA);
    }

    /// <summary>a에서 z로 가는 끈적한 목: 가운데가 wm만큼 잘록하다</summary>
    protected static void FillNeck(Graphics g, Brush b, PointF a, PointF z, float wa, float wz, float wm)
    {
        float dx = z.X - a.X, dy = z.Y - a.Y, len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 0.5f) return;
        float ux = dx / len, uy = dy / len, nx = -uy, ny = ux;
        var m = new PointF((a.X + z.X) / 2, (a.Y + z.Y) / 2);
        PointF P(PointF o, float w, float along) => new(o.X + nx * w / 2 + ux * along, o.Y + ny * w / 2 + uy * along);
        float q = len * 0.25f;
        using var path = new GraphicsPath();
        path.AddBezier(P(a, wa, 0), P(a, wa, q), P(m, wm, -q), P(m, wm, 0));
        path.AddBezier(P(m, wm, 0), P(m, wm, q), P(z, wz, -q), P(z, wz, 0));
        path.AddLine(P(z, wz, 0), P(z, -wz, 0));
        path.AddBezier(P(z, -wz, 0), P(z, -wz, -q), P(m, -wm, q), P(m, -wm, 0));
        path.AddBezier(P(m, -wm, 0), P(m, -wm, -q), P(a, -wa, q), P(a, -wa, 0));
        path.CloseFigure();
        g.FillPath(b, path);
    }

    protected static Color Lerp(Color a, Color b, float t) =>
        Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    protected static float Ease(float s) { s = Math.Clamp(s, 0, 1); return s * s * (3 - 2 * s); }
}

/// <summary>떼어 낸 칩 덩어리 하나. 칩들이 한 몸처럼 붙은 판 위에 줄지어 있고, 모든 움직임은 용수철로 따라간다.
/// · 손잡이(점 여섯 개)나 판을 끌면 덩어리째, 칩을 끌면 그 칩만 쭉 늘어나다 떨어진다.
/// · 끄는 동안 칩들이 살짝 늦게 따라와 말랑하게 보인다.</summary>
sealed class ChipGroup : GooWindow
{
    sealed class Member
    {
        public readonly TimerChip Chip;
        public ChipSpring X, Y, W, A, P; // X, Y: 칩 왼쪽 위 화면 좌표 · W: 자리 너비(0이면 없음) · A: 진하기 · P: 누름 크기
        public Bitmap? Fade;
        public RectangleF Plate;         // 마지막으로 그린 판 (눌렀을 때 찾기)
        public Member(TimerChip c) { Chip = c; P.Set(1); }
        public float Wv => Math.Clamp(W.V, 0, 1);
        public bool Seen => W.V > 0.04f;
        public PointF Center => new(X.V + Chip.Width * Wv / 2f, Y.V + Chip.Height / 2f);
    }

    /// <summary>떨어져 나간 자리에 남는 짧은 꼬리: 쏙 들어가며 사라진다</summary>
    sealed class Nub
    {
        public required Member On;
        public PointF Dir;
        public ChipSpring L;
    }

    static readonly ToolTip tip = new();
    const string GroupTip = "끌면 한꺼번에 옮겨요\n바에 놓으면 다시 들어가요";

    readonly List<Member> members = new();
    readonly List<Nub> nubs = new();
    ChipSpring ax, ay, lift, flat, hint, wob, grip;

    enum Mode { Rest, Press, Drag, Pull }
    Mode mode = Mode.Rest;
    bool live;
    Member? held;        // 누르거나 당기는 칩 (손잡이·판이면 null)
    int holdOrd = -1;    // 잡은 칩의 차례 (끌 때 멀수록 늦게 따라온다)
    Point pressAt;
    PointF grab;         // 덩어리 자리(ax, ay)에서 잡은 점까지
    PointF grabInChip;   // 잡은 칩 왼쪽 위에서 잡은 점까지
    PointF disp;         // 당길 때 칩이 끌려 나온 만큼
    float splat;         // 남은 시간 동안 출렁이게 (붙거나 떨어진 직후)
    float court;         // 다른 덩어리가 붙으러 오는 정도
    int courtAt = -1;    // 그 덩어리가 끼어들 자리 (앞에 남는 칩 수)
    float courtW;        // 그 덩어리의 너비 (사이에 끼면 뒤 칩들이 이만큼 비켜난다)
    ChipGroup? bridgeTo; // 끌고 있는 덩어리가 붙으려는 곳
    float bridgeS;
    bool bridgeRight;
    string? tipNow;
    bool pocket;         // 바 위에 올라와 바에 자리를 벌려 둔 상태 (놓으면 들어간다)
    SlotGlow? glow;      // 바에서 들어갈 자리를 빛낸다
    ChipSpring glowV;
    float glowT;
    readonly List<RectangleF> slots = new();    // 바에 벌려 둔 자리 (바 위에서 끄는 동안만 새로 잰다)
    readonly List<Member> seenBuf = new();       // 매 장면 다시 쓰는 목록 (새로 만들지 않게)
    (float k, float z)[] ks = Array.Empty<(float, float)>();

    int GW => S(14);

    ChipGroup(ChipDock dock) : base(dock)
    {
        Cursor = Cursors.Hand;
    }

    // ── 만들기 ──

    public static ChipGroup Restored(ChipDock dock, IEnumerable<TimerChip> chips, Point at)
    {
        var g = new ChipGroup(dock);
        g.InitRestored(chips, at);
        return g;
    }

    void InitRestored(IEnumerable<TimerChip> chips, Point at)
    {
        ax.Set(at.X); ay.Set(at.Y);
        foreach (var c in chips) members.Add(new Member(c));
    }

    public static ChipGroup FromBar(ChipDock dock, List<TimerChip> shown, List<RectangleF> rects, List<TimerChip> hidden, TimerChip grabbed, Point at)
    {
        var g = new ChipGroup(dock);
        g.InitFromBar(shown, rects, hidden, grabbed, at);
        return g;
    }

    void InitFromBar(List<TimerChip> shown, List<RectangleF> rects, List<TimerChip> hidden, TimerChip grabbed, Point at)
    {
        ax.Set(rects[0].X); ay.Set(rects[0].Y);
        for (int i = 0; i < shown.Count; i++)
        {
            var m = new Member(shown[i]);
            m.X.Set(rects[i].X); m.Y.Set(rects[i].Y); m.W.Set(1); m.A.Set(1);
            members.Add(m);
        }
        foreach (var c in hidden) members.Add(new Member(c));
        live = true;
        mode = Mode.Drag;
        pocket = true; // 칩들이 아직 바에 자리를 차지하고 있다
        holdOrd = shown.IndexOf(grabbed);
        grab = new PointF(at.X - ax.V, at.Y - ay.V);
        // 바의 칩 모양 그대로에서 판이 자라 나온다
        flat.Set(1); flat.T = 0;
        grip.Set(0);
        Draw();
        Kick();
    }

    /// <summary>덩어리에서 떨어져 나온 칩 하나로 새 덩어리: 커서 쪽으로 톡 튀어 오고, 떨어진 쪽에 꼬리가 남는다</summary>
    static ChipGroup Split(ChipDock dock, Member m, PointF grabInChip, PointF back)
    {
        var g = new ChipGroup(dock);
        g.InitSplit(m, grabInChip, back);
        return g;
    }

    void InitSplit(Member m, PointF grabInChip, PointF back)
    {
        m.P.T = 1;
        members.Add(m);
        ax.Set(m.X.V); ay.Set(m.Y.V);
        live = true;
        mode = Mode.Drag;
        holdOrd = 0;
        grab = grabInChip;
        lift.Set(0.4f);
        splat = 0.45f;
        wob.Vel = 9;
        AddNub(m, back);
        Draw();
        Kick();
    }

    void AddNub(Member on, PointF dir)
    {
        float len = MathF.Sqrt(dir.X * dir.X + dir.Y * dir.Y);
        if (len < 0.001f) return;
        var n = new Nub { On = on, Dir = new PointF(dir.X / len, dir.Y / len) };
        n.L.Set(S(14)); n.L.T = 0;
        nubs.Add(n);
    }

    // ── 덩어리 정보 ──

    public bool Has(TimerChip c) => members.Any(m => m.Chip == c);
    public IEnumerable<TimerChip> Chips => members.Select(m => m.Chip);
    public Point Spot => new((int)Math.Round(ax.T), (int)Math.Round(ay.T));
    float RowH => members.Count == 0 ? 1 : members.Max(m => m.Chip.Height);
    int CountT => members.Count(m => m.W.T > 0);
    int CountV => members.Count(m => m.Seen);

    /// <summary>다 자리 잡았을 때 칩 줄의 너비</summary>
    public float RowWidthT
    {
        get
        {
            float w = 0; int n = 0;
            foreach (var m in members) if (m.W.T > 0) { w += m.Chip.Width + dock.Gap; n++; }
            return n == 0 ? 0 : w - dock.Gap;
        }
    }

    public bool CanJoin => live && mode == Mode.Rest && !IsDisposed && CountT > 0;
    /// <summary>끼어들 수 있는 자리마다 (앞에 남는 칩 수, 끄는 덩어리의 왼쪽 위가 올 곳).
    /// 맨 앞은 덩어리 왼쪽에, 사이는 그 칩이 있던 자리에(뒤 칩들이 비켜난다), 맨 뒤는 오른쪽에.</summary>
    public IEnumerable<(int at, PointF p)> Snaps(float movingWidth)
    {
        yield return (0, new PointF(ax.T - dock.Gap - movingWidth, ay.T));
        float run = 0; int i = 0;
        foreach (var m in members)
        {
            if (m.W.T <= 0) continue;
            run += m.Chip.Width + dock.Gap;
            yield return (++i, new PointF(ax.T + run, ay.T));
        }
    }

    public void Court(float s, int at, float width)
    {
        if (Math.Abs(s - court) < 0.001f && at == courtAt && Math.Abs(width - courtW) < 0.5f) return;
        court = s; courtAt = at; courtW = width;
        Kick();
    }

    /// <summary>사이에 끼어들려는 칩이 있으면 그 뒤 칩들이 비켜나는 거리</summary>
    float OpenBy()
    {
        int n = CountT;
        if (court <= 0 || courtAt <= 0 || courtAt >= n) return 0;
        return Ease(Math.Min(1, court / 0.6f)) * (courtW + dock.Gap);
    }

    public void Wake() { if (Visible) Kick(); }

    /// <summary>바보다 위로: 자리 빛 → 덩어리 순으로 올려 덩어리가 맨 위에 온다</summary>
    public void RaiseUp() { glow?.Raise(); Raise(); }

    /// <summary>바에 벌려 둔 자리를 잰다 (화면 좌표)</summary>
    void MeasureSlots()
    {
        slots.Clear();
        foreach (var m in members)
            if (m.Seen && m.Chip.Visible && m.Chip.IsHandleCreated)
                slots.Add(m.Chip.RectangleToScreen(m.Chip.ClientRectangle));
    }

    /// <summary>합칠 때 칩들을 다 넘긴다</summary>
    public List<object> TakeAll()
    {
        var list = members.Cast<object>().ToList();
        members.Clear();
        return list;
    }

    /// <summary>바에 들어갈 때: 칩마다 지금 그려진 자리와 보이는지</summary>
    public IEnumerable<(TimerChip c, RectangleF r, bool seen)> Departing()
    {
        foreach (var m in members.ToList())
        {
            var c = m.Center;
            float s = m.P.V;
            var r = new RectangleF(c.X - m.Chip.Width * s / 2, c.Y - m.Chip.Height * s / 2, m.Chip.Width * s, m.Chip.Height * s);
            yield return (m.Chip, r, m.Seen && m.A.V > 0.3f);
        }
    }

    /// <summary>다른 덩어리의 칩들을 at 자리(앞·뒤·사이)에 끼운다: 지금 있던 자리에서 미끄러져 들어와 찰싹 출렁인다</summary>
    public void Absorb(List<object> list, int at, float movingWidth)
    {
        var add = list.Cast<Member>().ToList();
        foreach (var m in add) m.P.T = 1;
        var present = members.Where(m => m.W.T > 0).ToList();
        Member? before = null, after = null;
        if (at <= 0)
        {
            ax.Set(ax.T - dock.Gap - movingWidth); // 칩들은 제 자리에서 용수철로 옮겨 가니 자리값은 바로 옮긴다
            after = present.FirstOrDefault();
            members.InsertRange(0, add);
        }
        else if (at >= present.Count)
        {
            before = present.LastOrDefault();
            members.AddRange(add);
        }
        else
        {
            before = present[at - 1]; after = present[at];
            members.InsertRange(members.IndexOf(after), add);
        }
        // 끼워질 때 양옆이 살짝 밀려났다가 돌아온다 (찰싹)
        if (before != null) before.X.Vel -= S(90);
        if (after != null) after.X.Vel += S(90);
        foreach (var m in add) m.Y.Vel += S(40);
        splat = 0.7f;
        wob.Vel += 16;
        ApplyLive();
        Kick();
    }

    // ── 타이머·바 상태 따라가기 ──

    public void Sync(bool show, bool raise)
    {
        live = show;
        if (ApplyLive()) Kick();
        if (raise) Raise();
    }

    bool ApplyLive()
    {
        bool changed = false;
        foreach (var m in members)
        {
            float t = m.Chip.On && live ? 1 : 0;
            if (m.W.T != t) { m.W.T = t; m.A.T = t; changed = true; }
        }
        return changed;
    }

    // ── 마우스 ──

    Member? HitMember(Point p) => members.FirstOrDefault(m => m.Seen && RectangleF.Inflate(m.Plate, S(1), S(1)).Contains(p));

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || mode != Mode.Rest || !live) return;
        var cur = Cursor.Position;
        held = HitMember(cur);
        mode = Mode.Press;
        pressAt = cur;
        grab = new PointF(cur.X - ax.V, cur.Y - ay.V);
        var seen = members.Where(m => m.Seen).ToList();
        holdOrd = held == null ? -1 : seen.IndexOf(held);
        if (held != null)
        {
            grabInChip = new PointF(cur.X - held.X.V, cur.Y - held.Y.V);
            held.P.T = 0.95f; // 누르는 느낌: 살짝 들어간다
        }
        Kick();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (mode != Mode.Rest) { Pump(); return; }
        var m = HitMember(Cursor.Position);
        Cursor = m == null ? Cursors.SizeAll : Cursors.Hand;
        string text = m == null ? GroupTip : m.Chip.TipText + (CountV >= 2 ? "\n끌면 떼어 내요" : "\n끌어서 옮겨요") + "\n바에 놓으면 다시 들어가요";
        if (text != tipNow) { tipNow = text; tip.SetToolTip(this, text); }
    }

    // ── 움직임 ──

    protected override void Tick(float dt)
    {
        var cur = Cursor.Position;
        bool down = (MouseButtons & MouseButtons.Left) != 0;
        splat = Math.Max(0, splat - dt);
        bool over = false;

        if (mode == Mode.Press)
        {
            int moved = Math.Abs(cur.X - pressAt.X) + Math.Abs(cur.Y - pressAt.Y);
            if (!down)
            {
                // 그냥 누른 것: 바에서 누른 것과 같다
                mode = Mode.Rest;
                if (held != null) { held.P.T = 1; var c = held.Chip; held = null; c.Tap(); ApplyLive(); }
            }
            else if (moved >= S(5))
            {
                if (held != null && CountV >= 2) { mode = Mode.Pull; held.P.T = 1.04f; }
                else { mode = Mode.Drag; if (held != null) held.P.T = 1; held = null; }
                RaiseUp(); // 끄는 동안 바나 다른 창에 가려지지 않게
            }
        }

        if (mode == Mode.Pull && held != null)
        {
            float rx = cur.X - pressAt.X, ry = cur.Y - pressAt.Y, len = MathF.Sqrt(rx * rx + ry * ry);
            if (!down)
            {
                // 다 못 당기고 놓음: 찰싹 제자리로
                mode = Mode.Rest; held.P.T = 1; held = null; disp = default; splat = 0.6f;
            }
            else if (len >= S(78))
            {
                BreakOff(held);
            }
            else
            {
                // 잘 안 늘어난다: 당길수록 덜 따라온다
                float R = S(46), k = R * (1 - MathF.Exp(-len / R)) / Math.Max(len, 0.001f);
                disp = new PointF(rx * k, ry * k);
            }
        }

        bridgeTo = null; bridgeS = 0;
        if (mode == Mode.Drag)
        {
            var want = new PointF(cur.X - grab.X, cur.Y - grab.Y);
            over = dock.OverBar(cur);
            ChipGroup? mag = null; float s = 0; int at = 0; var snap = want;
            if (!over) mag = dock.FindMagnet(this, want, out snap, out s, out at);
            else dock.ClearCourt();
            bool right = mag != null && at > 0;
            bool middle = mag != null && at > 0 && at < mag.CountT;
            float e = Ease(s) * 0.92f;
            ax.T = want.X + (snap.X - want.X) * e;
            ay.T = want.Y + (snap.Y - want.Y) * e;
            // 바 위: 바에 들어갈 자리를 벌리고, 칩들은 그쪽으로 살짝 끌려가며 작아진다
            bool wantPocket = over && CountT > 0;
            if (wantPocket != pocket)
            {
                pocket = wantPocket;
                dock.Pocket(this, pocket);
                wob.Vel += pocket ? 6 : 3;
            }
            if (pocket)
            {
                MeasureSlots();
                if (slots.Count > 0)
                {
                    float lx = slots[0].X - ax.T, ly = slots[0].Y - ay.T, ll = MathF.Sqrt(lx * lx + ly * ly);
                    float k = Math.Min(0.16f, S(18) / Math.Max(ll, 1));
                    ax.T += lx * k; ay.T += ly * k;
                }
            }
            hint.T = over ? 1 : 0;
            lift.T = over ? 0.4f : 1;
            flat.T = 0;
            // 사이에 낄 때는 다리 대신 양옆이 벌어져 자리를 낸다
            bridgeTo = middle ? null : mag; bridgeS = s; bridgeRight = right;
            if (!down)
            {
                mode = Mode.Rest; lift.T = 0; hint.T = 0; held = null;
                if (mag != null) { dock.Merge(this, mag, at); return; }
                if (over && CountT > 0) { dock.ClearCourt(); dock.IntoBar(this); return; }
                dock.ClearCourt();
                bridgeTo = null;
                if (pocket) { pocket = false; dock.Pocket(this, false); }
                Settle();
            }
        }
        if (mode != Mode.Drag && pocket) { pocket = false; dock.Pocket(this, false); }
        if (mode != Mode.Drag) { lift.T = mode == Mode.Pull ? 0.25f : 0; hint.T = mode == Mode.Rest ? court * 0.7f : 0; }
        grip.T = CountT >= 2 && court <= 0 && !(bridgeTo != null && bridgeRight) ? 1 : 0;

        // 칩 자리 목표
        float H = RowH, run = 0;
        float open = OpenBy();
        int pi = 0; // 자리 잡은 칩 차례 (끼어들 자리 뒤 칩은 open만큼 비켜난다)
        int oi = 0; // 보이거나 자리 잡은 칩 차례
        int heldOrdNow = -1;
        if (held != null)
            foreach (var m in members)
            {
                if (m == held) { heldOrdNow = oi; break; }
                if (m.W.T > 0 || m.Seen) oi++;
            }
        oi = 0;
        if (ks.Length < members.Count) ks = new (float, float)[members.Count];
        for (int i = 0; i < members.Count; i++)
        {
            var m = members[i];
            float tx = ax.V + run, ty = ay.V + (H - m.Chip.Height) / 2f;
            run += (m.Chip.Width + dock.Gap) * m.Wv;
            if (open > 0 && pi >= courtAt) tx += open;
            if (m.W.T > 0) pi++;
            int ord = m.W.T > 0 || m.Seen ? oi++ : -1;
            if (mode == Mode.Pull && heldOrdNow >= 0 && ord >= 0)
            {
                if (m == held) { tx += disp.X; ty += disp.Y; }
                else if (Math.Abs(ord - heldOrdNow) == 1) { tx += disp.X * 0.14f; ty += disp.Y * 0.14f; } // 옆 칩도 살짝 끌려온다
            }
            m.X.T = tx; m.Y.T = ty;
            if (mode == Mode.Drag) m.P.T = pocket ? 0.88f : 1;
            if (!m.Seen) { m.X.Set(tx); m.Y.Set(ty); } // 안 보이는 칩은 제자리에 붙어 있다가 그 자리에서 자라 나온다
            if (mode == Mode.Pull && m == held) ks[i] = (2500, 1);
            else if (mode == Mode.Drag)
            {
                // 잡은 칩은 손에 바로 붙고, 멀수록 조금씩 늦게 따라온다 (말랑하게)
                int d = ord < 0 ? 0 : holdOrd < 0 ? ord + 1 : Math.Abs(ord - holdOrd);
                ks[i] = (d == 0 ? 2500f : 900f / (1 + 0.7f * d), splat > 0 ? 0.5f : 0.75f);
            }
            else ks[i] = splat > 0 ? (300, 0.42f) : (320, 0.7f);
        }

        // 빠른 용수철도 흔들리지 않게 잘게 나눠 계산
        int n = Math.Max(1, (int)Math.Ceiling(dt * 240));
        float h = dt / n;
        var (ka, za) = mode == Mode.Drag ? (3000f, splat > 0 ? 0.55f : 1f) : (260f, 0.85f);
        for (int step = 0; step < n; step++)
        {
            ax.Step(h, ka, za); ay.Step(h, ka, za);
            for (int i = 0; i < members.Count; i++)
            {
                var m = members[i];
                m.X.Step(h, ks[i].k, ks[i].z); m.Y.Step(h, ks[i].k, ks[i].z);
                m.W.Step(h, 260, 0.9f); m.A.Step(h, 260, 1); m.P.Step(h, 500, 0.6f);
            }
            lift.Step(h, 300, mode == Mode.Rest ? 0.8f : 1f);
            flat.Step(h, 300, 1);
            hint.Step(h, 400, 1);
            wob.Step(h, 380, 0.3f);
            grip.Step(h, 300, 0.9f);
            glowV.Step(h, 400, 1);
            foreach (var nb in nubs) nb.L.Step(h, 500, 0.5f);
        }
        nubs.RemoveAll(nb => !members.Contains(nb.On) || (nb.L.Rest(0.4f) && nb.L.V < 0.5f));
        glowV.T = pocket ? 1 : 0;
        glowT += dt;

        Draw();
        UpdateGlow();

        if (mode == Mode.Rest && Calm())
        {
            Halt();
            ax.Snap(); ay.Snap(); lift.Snap(); flat.Snap(); hint.Snap(); wob.Snap(); grip.Snap(); glowV.Snap();
            glow?.Hide();
            foreach (var m in members) { m.X.Snap(); m.Y.Snap(); m.W.Snap(); m.A.Snap(); m.P.Snap(); }
            if (members.Any(m => m.Seen)) Draw(); else Hide();
        }
    }

    bool Calm() =>
        nubs.Count == 0 && splat <= 0 && glowV.Rest(0.01f) &&
        ax.Rest(0.3f) && ay.Rest(0.3f) && lift.Rest(0.01f) && flat.Rest(0.01f) && hint.Rest(0.01f) && wob.Rest(0.01f) && grip.Rest(0.01f) &&
        members.All(m => m.X.Rest(0.3f) && m.Y.Rest(0.3f) && m.W.Rest(0.005f) && m.A.Rest(0.01f) && m.P.Rest(0.002f));

    /// <summary>쭉 늘어난 칩이 끊어져 떨어진다: 남은 쪽은 출렁이며 틈을 메우고, 양쪽에 꼬리가 쏙 들어간다</summary>
    void BreakOff(Member m)
    {
        var seen = members.Where(x => x.Seen).ToList();
        int i = seen.IndexOf(m);
        var prev = i > 0 ? seen[i - 1] : null;
        var next = i >= 0 && i < seen.Count - 1 ? seen[i + 1] : null;
        var mc = m.Center;
        PointF back = default;
        foreach (var nb in new[] { prev, next })
        {
            if (nb == null) continue;
            var c = nb.Center;
            AddNub(nb, new PointF(mc.X - c.X, mc.Y - c.Y));
            if (back == default) back = new PointF(c.X - mc.X, c.Y - mc.Y);
        }
        members.Remove(m);
        held = null; disp = default;
        mode = Mode.Rest;
        splat = 0.6f;
        wob.Vel += 10;
        // 잡았던 점을 그대로 쥔 채 떨어져 나간다
        dock.SplitOff(Split(dock, m, grabInChip, back));
    }

    /// <summary>바 밖에 놓음: 화면 안으로 넣고 그 자리를 저장한다</summary>
    void Settle()
    {
        float gw = CountT >= 2 ? GW + dock.Gap : 0;
        var size = new Size(Math.Max(1, (int)Math.Ceiling(RowWidthT + gw)), Math.Max(1, (int)Math.Ceiling(RowH)));
        var p = ChipDock.Clamp(new Point((int)Math.Round(ax.T - gw), (int)Math.Round(ay.T)), size);
        ax.T = p.X + gw; ay.T = p.Y;
        foreach (var m in members) m.P.T = 1;
        wob.Vel += 5; // 통 내려앉는다
        dock.Save();
    }

    // ── 그리기 ──

    /// <summary>바에 벌려 둔 자리를 빛낸다: 들어가면 빛이 숨 쉬듯 일렁이고, 바 밖으로 나가면 사그라든다</summary>
    void UpdateGlow()
    {
        float a = Math.Clamp(glowV.V, 0, 1);
        if (a < 0.02f || slots.Count == 0) { if (glow is { Visible: true }) glow.Hide(); return; }
        glow ??= new SlotGlow(dock);
        bool fresh = !glow.Visible;
        glow.Shine(slots, a, 0.5f + 0.5f * MathF.Sin(glowT * 6));
        if (fresh) Raise(); // 빛은 덩어리 아래에
    }

    /// <summary>보이는 맨 앞(또는 맨 뒤) 칩의 바깥쪽 가장자리 가운데</summary>
    PointF Edge(bool right)
    {
        var m = right ? members.LastOrDefault(x => x.Seen) : members.FirstOrDefault(x => x.Seen);
        if (m == null) return new PointF(ax.V, ay.V + RowH / 2);
        return new PointF(right ? m.Plate.Right - S(2) : m.Plate.Left + S(2), m.Plate.Top + m.Plate.Height / 2);
    }

    void Draw()
    {
        if (IsDisposed) return;
        float up = 1 - Math.Clamp(flat.V, 0, 1);
        float pad = S(4) * up;
        float H = RowH;
        var seen = seenBuf;
        seen.Clear();
        foreach (var m in members) if (m.Seen) seen.Add(m);
        nubs.RemoveAll(nb => !nb.On.Seen);
        if (seen.Count == 0) { if (Visible) Hide(); return; }

        // 판 자리 (화면 좌표)
        foreach (var m in seen)
        {
            var c = m.Center;
            float edge = 2 * pad * Math.Min(1, m.Wv * 3);
            float pw = m.Chip.Width * m.P.V * Math.Min(1, m.Wv * 1.4f) + edge;
            float ph = m.Chip.Height * m.P.V * (0.55f + 0.45f * m.Wv) + edge;
            m.Plate = new RectangleF(c.X - pw / 2, c.Y - ph / 2, pw, ph);
        }
        RectangleF? gripR = null;
        float gv = Math.Clamp(grip.V, 0, 1.2f);
        if (gv > 0.02f)
        {
            var f = seen[0].Plate;
            float gw = GW * gv;
            gripR = new RectangleF(f.Left - dock.Gap - gw + S(2), f.Top + f.Height * 0.08f, gw + dock.Gap + S(6), f.Height * 0.84f);
        }

        // 다른 덩어리로 뻗는 다리
        PointF? bFrom = null, bTo = null; float reach = 0;
        if (bridgeTo != null && bridgeS > 0.01f && !bridgeTo.IsDisposed)
        {
            bFrom = Edge(!bridgeRight);
            var tgt = bridgeTo.Edge(bridgeRight);
            reach = Math.Min(1, bridgeS / 0.35f);
            bTo = new PointF(bFrom.Value.X + (tgt.X - bFrom.Value.X) * reach, bFrom.Value.Y + (tgt.Y - bFrom.Value.Y) * reach);
        }

        // 창 크기: 모든 판 + 여백
        RectangleF box = seen[0].Plate;
        foreach (var m in seen) box = RectangleF.Union(box, m.Plate);
        if (gripR != null) box = RectangleF.Union(box, gripR.Value);
        foreach (var nb in nubs)
        {
            var c = nb.On.Center;
            float r = Math.Max(nb.On.Plate.Width, nb.On.Plate.Height) / 2 + Math.Max(0, nb.L.V);
            box = RectangleF.Union(box, new RectangleF(c.X + nb.Dir.X * r - 1, c.Y + nb.Dir.Y * r - 1, 2, 2));
        }
        if (bTo != null) box = RectangleF.Union(box, new RectangleF(bTo.Value.X - H / 2, bTo.Value.Y - H / 2, H, H));
        int mg = S(30);
        int ox = (int)MathF.Floor(box.X) - mg, oy = (int)MathF.Floor(box.Y) - mg;
        Ensure((int)MathF.Ceiling(box.Width) + 2 * mg + 2, (int)MathF.Ceiling(box.Height) + 2 * mg + 2);

        // 덩어리 전체 크기: 들면 살짝 커지고, 출렁임은 가로·세로가 엇갈려 말랑하게
        var rc = new PointF(box.X + box.Width / 2, box.Y + box.Height / 2);
        float lf = Math.Clamp(lift.V, 0, 1.3f), wv = Math.Clamp(wob.V, -2, 2);
        float sx = (1 + 0.045f * lf) * (1 + 0.05f * wv), sy = (1 + 0.045f * lf) * (1 - 0.06f * wv);
        void Place(Graphics g)
        {
            g.TranslateTransform(-ox, -oy);
            g.TranslateTransform(rc.X, rc.Y);
            g.ScaleTransform(sx, sy);
            g.TranslateTransform(-rc.X, -rc.Y);
        }

        using (var gm = Begin(mask!))
        using (var white = new SolidBrush(Color.White))
        {
            // 다리는 덩어리 크기와 상관없이 제자리에
            gm.TranslateTransform(-ox, -oy);
            if (bFrom != null && bTo != null)
            {
                float s = bridgeS, dx = bTo.Value.X - bFrom.Value.X, dy = bTo.Value.Y - bFrom.Value.Y, len = MathF.Sqrt(dx * dx + dy * dy);
                if (len > 0.5f)
                {
                    float ux = dx / len, uy = dy / len;
                    var a = new PointF(bFrom.Value.X - ux * S(6), bFrom.Value.Y - uy * S(6));
                    var z = reach >= 1 ? new PointF(bTo.Value.X + ux * S(4), bTo.Value.Y + uy * S(4)) : bTo.Value;
                    float wz = H * (0.2f + 0.5f * s);
                    FillNeck(gm, white, a, z, H * (0.35f + 0.4f * s), wz, H * (0.12f + 0.5f * s));
                    if (reach < 1) gm.FillEllipse(white, z.X - wz / 2, z.Y - wz / 2, wz, wz);
                }
            }
            gm.ResetTransform();
            Place(gm);
            foreach (var m in seen)
            {
                using var p = UI.RoundRect(m.Plate, m.Plate.Height * 0.25f);
                gm.FillPath(white, p);
            }
            if (gripR != null) { using var p = UI.RoundRect(gripR.Value, gripR.Value.Height * 0.3f); gm.FillPath(white, p); }
            // 칩 사이 목: 떨어져 있을수록 가늘어진다
            for (int i = 0; i + 1 < seen.Count; i++)
            {
                var a = seen[i]; var b = seen[i + 1];
                PointF ca = a.Center, cb = b.Center;
                float rest = a.Chip.Width * a.Wv / 2 + dock.Gap + b.Chip.Width * b.Wv / 2;
                float ex = cb.X - ca.X - rest, ey = cb.Y - ca.Y, excess = MathF.Sqrt(ex * ex + ey * ey);
                float hh = Math.Min(a.Plate.Height, b.Plate.Height);
                float wm = hh * Math.Clamp(1 - excess / S(56), 0.16f, 1) * 0.85f;
                FillNeck(gm, white, ca, cb, a.Plate.Height * 0.8f, b.Plate.Height * 0.8f, wm);
            }
            // 떨어진 자리의 꼬리
            foreach (var nb in nubs)
            {
                var pl = nb.On.Plate; var c = nb.On.Center;
                float L = Math.Max(0, nb.L.V);
                if (L < 0.5f) continue;
                float ext = MathF.Abs(nb.Dir.X) * pl.Width / 2 + MathF.Abs(nb.Dir.Y) * pl.Height / 2 - S(3);
                var bse = new PointF(c.X + nb.Dir.X * (ext - S(4)), c.Y + nb.Dir.Y * (ext - S(4)));
                var tipP = new PointF(c.X + nb.Dir.X * (ext + L), c.Y + nb.Dir.Y * (ext + L));
                float wt = pl.Height * 0.24f;
                FillNeck(gm, white, bse, tipP, pl.Height * 0.6f, wt, pl.Height * 0.3f);
                gm.FillEllipse(white, tipP.X - wt / 2, tipP.Y - wt / 2, wt, wt);
            }
        }

        using (var g = Begin(Frame))
        {
            Compose(g, up, lf, Math.Max(hint.V, 0));
            Place(g);
            // 손잡이: 점 여섯 개
            if (gripR != null)
            {
                float a = Math.Clamp(gv, 0, 1);
                var f = seen[0].Plate;
                float gx = f.Left - dock.Gap - GW * gv / 2 + S(3), gy = f.Top + f.Height / 2;
                float d = S(1.5f), sp = S(4.5f);
                using var b = new SolidBrush(Color.FromArgb((int)(255 * a), UI.Sub));
                for (int cx = 0; cx < 2; cx++)
                    for (int cy = -1; cy <= 1; cy++)
                        g.FillEllipse(b, gx + (cx - 0.5f) * sp * 0.9f - d, gy + cy * sp - d, 2 * d, 2 * d);
            }
            foreach (var m in seen) DrawChip(g, m);
        }
        Present(new Point(ox, oy));
    }

    static readonly ColorMatrix fadeM = new();
    static readonly ImageAttributes fadeA = new();

    static void DrawChip(Graphics g, Member m)
    {
        var c = m.Center;
        float s = m.P.V * (0.4f + 0.6f * m.Wv);
        int w = m.Chip.Width, h = m.Chip.Height;
        var st = g.Save();
        g.TranslateTransform(c.X, c.Y);
        g.ScaleTransform(s, s);
        g.TranslateTransform(-w / 2f, -h / 2f);
        float a = Math.Clamp(m.A.V, 0, 1);
        if (a >= 0.98f) m.Chip.Render(g, m.Chip.Size, smooth: true);
        else if (a > 0.01f)
        {
            // 흐릿하게: 따로 그려 진하기만 낮춘다
            if (m.Fade == null || m.Fade.Width != w || m.Fade.Height != h) { m.Fade?.Dispose(); m.Fade = new Bitmap(Math.Max(1, w), Math.Max(1, h), PixelFormat.Format32bppArgb); }
            using (var fg = Graphics.FromImage(m.Fade)) { fg.Clear(Color.Transparent); m.Chip.Render(fg, m.Chip.Size, smooth: true); }
            fadeM.Matrix33 = a;
            fadeA.SetColorMatrix(fadeM);
            g.DrawImage(m.Fade, new Rectangle(0, 0, w, h), 0, 0, w, h, GraphicsUnit.Pixel, fadeA);
        }
        g.Restore(st);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var m in members) { m.Fade?.Dispose(); m.Fade = null; }
            glow?.Dispose(); glow = null;
        }
        base.Dispose(disposing);
    }
}

/// <summary>바 위에 덩어리를 올렸을 때 바에서 들어갈 자리: 강조색으로 빛나는 둥근 칸</summary>
sealed class SlotGlow : GooWindow
{
    public SlotGlow(ChipDock dock) : base(dock) { }

    protected override void Tick(float dt) { }

    public void Shine(List<RectangleF> slots, float a, float pulse)
    {
        if (IsDisposed || slots.Count == 0) return;
        var box = slots[0];
        foreach (var r in slots) box = RectangleF.Union(box, r);
        int mg = S(14);
        int ox = (int)MathF.Floor(box.X) - mg, oy = (int)MathF.Floor(box.Y) - mg;
        Ensure((int)MathF.Ceiling(box.Width) + 2 * mg + 2, (int)MathF.Ceiling(box.Height) + 2 * mg + 2);
        float grow = S(1) + S(1.5f) * pulse;
        using (var gm = Begin(mask!))
        using (var white = new SolidBrush(Color.White))
        {
            gm.TranslateTransform(-ox, -oy);
            foreach (var r in slots)
            {
                var q = RectangleF.Inflate(r, grow, grow);
                using var p = UI.RoundRect(q, q.Height * 0.25f);
                gm.FillPath(white, p);
            }
        }
        using (var g = Begin(Frame))
        {
            Compose(g, 0, 0, a * (0.55f + 0.45f * pulse), 0);
            g.TranslateTransform(-ox, -oy);
            using var pen = new Pen(Color.FromArgb((int)(220 * a), UI.Accent), Math.Max(1, S(1.5f)));
            using var fill = new SolidBrush(Color.FromArgb((int)(40 * a), UI.Accent));
            foreach (var r in slots)
            {
                var q = RectangleF.Inflate(r, grow - 1, grow - 1);
                using var p = UI.RoundRect(q, q.Height * 0.25f);
                g.FillPath(fill, p);
                g.DrawPath(pen, p);
            }
        }
        Present(new Point(ox, oy));
    }
}

/// <summary>바에 놓은 칩 하나가 바의 제자리로 빨려 들어간다:
/// 천천히 떠나 점점 빨라지며 가는 쪽으로 길게 늘어나고, 판이 걷히며 쏙 들어가 납작하게 출렁인 뒤 멈춘다.</summary>
sealed class ChipFlyer : GooWindow
{
    readonly TimerChip chip;
    readonly PointF from;
    readonly float delay;
    readonly float s0;   // 떠날 때 크기 (덩어리에서 보이던 크기 그대로 이어받는다)
    float t;
    bool handed;         // 바의 칸을 그렸다: 한 번 더 겹쳐 보인 뒤 사라진다
    PointF dir = new(1, 0);
    const float T1 = 0.34f, T2 = 0.3f, Stretch = 0.3f;

    public ChipFlyer(ChipDock dock, TimerChip chip, RectangleF fromRect, float delay) : base(dock)
    {
        this.chip = chip; this.delay = delay;
        from = new PointF(fromRect.X + fromRect.Width / 2, fromRect.Y + fromRect.Height / 2);
        s0 = Math.Clamp(fromRect.Width / Math.Max(1, chip.Width), 0.5f, 1.3f);
        Draw(from, 0, s0, 1);
        Kick();
    }

    protected override void Tick(float dt)
    {
        t += dt;
        if (handed) { Halt(); Hide(); Dispose(); return; }
        if (chip.IsDisposed || !chip.On) { Finish(); return; } // 가는 사이에 타이머가 끝났으면 그냥 바에 맡긴다
        var r = chip.RectangleToScreen(chip.ClientRectangle); // 바가 움직여도 따라간다
        var to = new PointF(r.X + r.Width / 2f, r.Y + r.Height / 2f);
        float dx = to.X - from.X, dy = to.Y - from.Y, len = MathF.Sqrt(dx * dx + dy * dy);
        if (t < delay + T1)
        {
            float p = Math.Clamp((t - delay) / T1, 0, 1);
            float e = MathF.Pow(p, 2.3f); // 처음엔 천천히, 갈수록 빨려 들어간다
            if (len > 1) dir = new PointF(dx / len, dy / len);
            var perp = new PointF(-dir.Y, dir.X);
            if (perp.Y > 0) perp = new PointF(-perp.X, -perp.Y); // 살짝 위로 휘어 간다
            float arc = MathF.Sin(MathF.PI * p) * Math.Min(S(10), len * 0.25f);
            var pos = new PointF(from.X + dx * e + perp.X * arc, from.Y + dy * e + perp.Y * arc);
            float size = s0 + (1 - s0) * e;
            Draw(pos, Stretch * MathF.Pow(p, 1.6f), size * (1 - 0.12f * MathF.Sin(MathF.PI * p)), 1 - e);
        }
        else
        {
            float q = (t - delay - T1) / T2;
            if (q >= 1) { Draw(to, 0, 1, 0, exact: true); Finish(); return; }
            // 쏙 들어가며 납작해졌다가 출렁이며 멈춘다 (끝으로 갈수록 0으로 잦아들어 바의 칸과 똑같아진다)
            float fade = 1 - Ease(Math.Clamp((q - 0.6f) / 0.4f, 0, 1));
            Draw(to, Stretch * MathF.Cos(2.2f * MathF.PI * q) * MathF.Exp(-3.5f * q) * fade, 1, 0);
        }
    }

    /// <summary>바의 칸을 먼저 그려 둔 다음 프레임에 이 창을 닫는다: 화면에서 한 번도 비지 않는다</summary>
    void Finish()
    {
        if (!chip.IsDisposed)
        {
            chip.Ghost = false;
            chip.Update(); // 바로 그린다 (기다리지 않고)
        }
        handed = true;
        Kick();
    }

    /// <summary>exact: 바의 칸과 글자까지 똑같이 그린다 (넘겨주는 마지막 장면)</summary>
    void Draw(PointF pos, float stretch, float size, float up, bool exact = false)
    {
        if (IsDisposed) return;
        int w = chip.Width, h = chip.Height;
        float pad = S(4) * up;
        int half = (int)(Math.Max(w, h) * 0.5f * (1 + Math.Abs(stretch)) + pad) + S(30);
        int ox = (int)MathF.Floor(pos.X) - half, oy = (int)MathF.Floor(pos.Y) - half;
        Ensure(2 * half + 2, 2 * half + 2);
        float ang = MathF.Atan2(dir.Y, dir.X) * 180 / MathF.PI;
        void Place(Graphics g)
        {
            g.TranslateTransform(pos.X - ox, pos.Y - oy);
            g.RotateTransform(ang);
            g.ScaleTransform(1 + stretch, 1 - stretch * 0.5f); // 가는 쪽으로 늘어나고 옆으로 가늘어진다
            g.RotateTransform(-ang);
            g.ScaleTransform(size, size);
        }
        using (var gm = Begin(mask!))
        {
            Place(gm);
            var plate = new RectangleF(-w / 2f - pad, -h / 2f - pad, w + 2 * pad, h + 2 * pad);
            using var p = UI.RoundRect(plate, plate.Height * 0.25f);
            using var b = new SolidBrush(Color.White);
            gm.FillPath(b, p);
        }
        using (var g = Begin(Frame))
        {
            Compose(g, up, 0.4f * up, 0, up);
            Place(g);
            g.TranslateTransform(-w / 2f, -h / 2f);
            if (exact) g.ResetTransform();
            if (exact) g.TranslateTransform((float)Math.Round(pos.X - w / 2f) - ox, (float)Math.Round(pos.Y - h / 2f) - oy);
            chip.Render(g, chip.Size, smooth: !exact);
        }
        Present(new Point(ox, oy));
    }
}
