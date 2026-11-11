using System.Diagnostics;
using System.Drawing.Drawing2D;

using static MapleBar.Native;

namespace MapleBar;

/// <summary>
/// 바 바로 아래에서 가운데부터 천천히 늘어나며 새 글 제목을 보여 주는 알림 띠.
/// 알림이 여러 개면 하나씩 차례로 보여 준다. 누르면 글을 열고, ✕를 누르면 닫는다.
/// </summary>
public sealed class NoticeStrip : Form
{
    record Item(string Tag, string Title, string Sub, Action Click);

    // 줄마다 따로 차례를 기다린다. 0번 줄: 자주 오는 새 글, 1번 줄: 가끔 오는 추천 글 (새 글 줄 아래)
    const int Lanes = 2;
    static readonly Queue<Item>[] queues = { new(), new() };
    static readonly NoticeStrip?[] current = new NoticeStrip?[Lanes];

    public static void Show(Form bar, int lane, string tag, string title, string sub, Action onClick, float sc)
    {
        lane = Math.Clamp(lane, 0, Lanes - 1);
        queues[lane].Enqueue(new Item(tag, title, sub, onClick));
        if (current[lane] == null) Next(bar, lane, sc);
        else current[lane]!.Invalidate(); // 남은 개수 표시 갱신
    }

    /// <summary>바를 숨길 때: 기다리던 알림을 버리고 떠 있는 알림을 닫는다.</summary>
    public static void CloseAll()
    {
        for (int i = 0; i < Lanes; i++)
        {
            queues[i].Clear();
            var c = current[i];
            if (c != null && !c.IsDisposed) c.Finish(); // 큐가 비어 있어 다음 알림은 뜨지 않는다
            current[i] = null;
        }
    }

    static void Next(Form bar, int lane, float sc)
    {
        current[lane] = null;
        if (queues[lane].Count == 0 || bar.IsDisposed) return;
        var strip = new NoticeStrip(bar, lane, queues[lane].Dequeue(), sc);
        current[lane] = strip;
        // 바가 숨어 있으면 바에 붙이지 않고 따로 띄운다
        if (strip.hiddenTop != null) strip.Show(); else strip.Show(bar);
    }

    readonly int lane;
    Queue<Item> queue => queues[lane];

    // 늘어나기 → 머무르기 → 줄어들기
    const int GrowMs = 650, HoldMs = 7000, ShrinkMs = 350;
    enum Phase { Grow, Hold, Shrink }

    readonly Form bar;
    readonly Item item;
    readonly float sc;
    readonly System.Windows.Forms.Timer tick = new() { Interval = 15 };
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly Font tagFont = new("맑은 고딕", 9f, FontStyle.Bold);
    readonly Font titleFont = new("맑은 고딕", 10f, FontStyle.Bold);
    readonly Font subFont = new("맑은 고딕", 9f);
    Phase phase = Phase.Grow;
    double holdLeft = HoldMs;
    long lastMs;
    bool hover, hoverClose, done;
    int S(int v) => UI.Scale(v, sc);
    int H => S(36);
    int FullW => Math.Max(bar.Width, S(420));

    NoticeStrip(Form bar, int lane, Item item, float sc)
    {
        this.bar = bar; this.lane = lane; this.item = item; this.sc = sc;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        // TopMost 속성은 창을 띄울 때 활성화까지 시켜 입력 중인 칸의 포커스를 뺏는다. 항상 위는 CreateParams로 준다.
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.White;
        DoubleBuffered = true;
        Cursor = Cursors.Hand;
        Opacity = 0;
        // 바가 숨어 있으면 게임 화면(없으면 주 모니터) 위 가운데에 띄운다
        if (!bar.Visible || bar.WindowState == FormWindowState.Minimized)
        {
            var area = Game.Area() ?? Screen.PrimaryScreen!.WorkingArea;
            hiddenTop = new Point(area.Left + area.Width / 2, area.Top + S(6));
        }
        Place(0);
        tick.Tick += (_, _) => Step();
        tick.Start();
    }

    static double EaseOut(double t) => 1 - Math.Pow(1 - t, 3);

    /// <summary>바 아래 가운데를 기준으로 너비 비율 p만큼 펼친다.</summary>
    void Place(double p)
    {
        int w = Math.Max(H, (int)Math.Round(FullW * p));
        int cx = hiddenTop?.X ?? bar.Left + bar.Width / 2;
        int top = hiddenTop?.Y ?? (bar is BarForm b ? b.NoticeTop : bar.Bottom);
        // 위쪽 줄에 떠 있는 알림이 있으면 그 아래에, 없으면 바로 바 아래에
        int row = 0;
        for (int i = 0; i < lane; i++) if (current[i] != null) row++;
        Bounds = new Rectangle(cx - w / 2, top + S(6) + row * (H + S(6)), w, H);
    }

    /// <summary>바가 숨어 있을 때 띄울 위치 (가운데 x, 위 y). 바가 보이면 null이고 바 아래에 붙는다.</summary>
    Point? hiddenTop;

    void Step()
    {
        long now = clock.ElapsedMilliseconds;
        long dt = now - lastMs;
        lastMs = now;
        switch (phase)
        {
            case Phase.Grow:
                double g = Math.Min(1, now / (double)GrowMs);
                Opacity = Math.Min(1, g * 2);
                Place(EaseOut(g));
                if (g >= 1) phase = Phase.Hold;
                break;
            case Phase.Hold:
                if (!hover) holdLeft -= dt; // 마우스를 올려 두면 기다린다
                // 뒤에 알림이 더 있으면 조금 빨리 넘긴다
                if (holdLeft <= (queue.Count > 0 ? HoldMs / 2 : 0)) { phase = Phase.Shrink; clock.Restart(); lastMs = 0; }
                Place(1); // 바를 옮기면 따라간다
                break;
            case Phase.Shrink:
                double s = Math.Min(1, now / (double)ShrinkMs);
                Opacity = 1 - s;
                Place(1 - EaseOut(s));
                if (s >= 1) Finish();
                break;
        }
        Invalidate();
    }

    void Finish()
    {
        if (done) return;
        done = true;
        tick.Stop();
        Close();
        Next(bar, lane, sc);
    }

    // 다 펼친 모양 기준의 배치. 늘어나는 동안은 가운데부터 드러난다.
    int Offset => (Width - FullW) / 2;
    Rectangle CloseRect => new(Offset + FullW - S(30), (H - S(22)) / 2, S(22), S(22));

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(hover ? Color.FromArgb(255, 250, 243) : Color.White);
        using (var pen = new Pen(UI.Line)) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        if (Width < H * 2) return;

        int x = Offset + S(10);
        // 말머리 (주황 알약)
        var ts = TextRenderer.MeasureText(item.Tag, tagFont, Size.Empty, TextFormatFlags.NoPadding);
        var tagRect = new Rectangle(x, (H - (ts.Height + S(6))) / 2, ts.Width + S(16), ts.Height + S(6));
        using (var path = UI.RoundRect(tagRect, tagRect.Height / 2f))
        using (var b = new SolidBrush(UI.Accent)) g.FillPath(b, path);
        TextRenderer.DrawText(g, item.Tag, tagFont, tagRect, Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        // 오른쪽: 남은 알림 수, 닫기
        int right = CloseRect.Left - S(6);
        if (queue.Count > 0)
        {
            var more = "+" + queue.Count;
            var ms = TextRenderer.MeasureText(more, subFont);
            right -= ms.Width;
            TextRenderer.DrawText(g, more, subFont, new Rectangle(right, 0, ms.Width, H), UI.Accent,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            right -= S(4);
        }
        if (hoverClose) using (var b = new SolidBrush(UI.HoverBack)) g.FillEllipse(b, CloseRect);
        TextRenderer.DrawText(g, "✕", subFont, CloseRect, Color.FromArgb(150, 150, 150),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        // 말머리(게시판 분류)는 회색으로 오른쪽에, 제목은 남는 칸에
        int subW = 0;
        if (item.Sub != "")
        {
            subW = Math.Min(TextRenderer.MeasureText(item.Sub, subFont).Width, S(90));
            TextRenderer.DrawText(g, item.Sub, subFont, new Rectangle(right - subW, 0, subW, H), Color.FromArgb(140, 140, 140),
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            subW += S(8);
        }
        int tx = tagRect.Right + S(10);
        TextRenderer.DrawText(g, item.Title, titleFont, new Rectangle(tx, 0, Math.Max(0, right - subW - tx), H), UI.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = hoverClose = false; base.OnMouseLeave(e); }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        hoverClose = CloseRect.Contains(e.Location);
        base.OnMouseMove(e);
    }
    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button != MouseButtons.Left) return;
        if (!CloseRect.Contains(e.Location)) item.Click();
        Finish();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        tick.Dispose();
        base.OnFormClosed(e);
    }

    protected override bool ShowWithoutActivation => true;

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
            cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST; // 작업 표시줄에 안 나오고, 눌러도 포커스를 뺏지 않고, 항상 위
            cp.ClassStyle |= CS_DROPSHADOW;        // 그림자
            return cp;
        }
    }
}
