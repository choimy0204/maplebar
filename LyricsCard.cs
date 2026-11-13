using System.Drawing.Drawing2D;
using static MapleBar.Native;

namespace MapleBar;

/// <summary>
/// 유튜브 뮤직 가사 카드: 뮤직 칸 바로 아래(공간이 없으면 위)에 뜬다. 포커스를 뺏지 않는다.
/// 시간이 맞춰진 가사면 지금 부르는 줄을 주황으로 칠하고 그 줄을 가운데로 따라간다.
/// 휠로 직접 넘기면 잠시(6초) 따라가지 않는다. ✕를 누르면 가사 보기를 끈다.
/// </summary>
sealed class LyricsCard : Form
{
    readonly float sc;
    readonly Action onClose;
    string text = "";
    string[] lines = Array.Empty<string>();
    int[] tops = Array.Empty<int>(), heights = Array.Empty<int>();
    int cur = -1, scroll, target, contentH;
    DateTime userScrolledAt = DateTime.MinValue;
    readonly Font font, bold, head;
    readonly System.Windows.Forms.Timer ease = new() { Interval = 16 };
    const TextFormatFlags Flags = TextFormatFlags.WordBreak | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix;
    int S(int v) => (int)Math.Round(v * sc);
    Rectangle CloseRect => new(Width - S(26), S(6), S(18), S(18));

    public LyricsCard(float scale, Action onClose)
    {
        sc = scale; this.onClose = onClose;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        BackColor = Color.FromArgb(255, 253, 250);
        DoubleBuffered = true;
        font = new Font("맑은 고딕", 9.5f * UI.Z);
        bold = new Font("맑은 고딕", 10f * UI.Z, FontStyle.Bold);
        head = new Font("맑은 고딕", 8.5f * UI.Z, FontStyle.Bold);
        Size = new Size(S(300), S(230));
        // 지금 줄로 부드럽게 옮겨 간다
        ease.Tick += (_, _) =>
        {
            int d = target - scroll;
            if (Math.Abs(d) <= 1) { scroll = target; ease.Stop(); }
            else scroll += (int)Math.Round(d * 0.25);
            Invalidate();
        };
    }

    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE; // 작업 표시줄에 안 나오고 포커스를 뺏지 않게
            return cp;
        }
    }

    /// <summary>가사와 지금 줄(-1이면 모름)을 바꾼다.</summary>
    string song = "";
    public void SetText(string t, int line, string time, string title)
    {
        bool changed = t != text;
        if (changed)
        {
            text = t; lines = t.Split('\n'); Measure();
            // 맨 위로는 곡이 바뀔 때만 (같은 곡 가사가 조금 달리 읽혀도 자리는 그대로)
            if (title != song) { song = title; scroll = target = 0; userScrolledAt = DateTime.MinValue; }
            else { scroll = Math.Min(scroll, MaxScroll); target = Math.Min(target, MaxScroll); }
        }
        int c = line >= 0 && line < lines.Length ? line : -1;
        progress = Progress(time);
        bool redraw = changed || c != cur;
        cur = c;
        Follow();
        if (redraw) Invalidate();
    }

    double progress;

    // 지금 줄 띠 폭 계산 캐시 (줄 내용, 그릴 폭, 잰 글자 폭)
    string? measuredText;
    int measuredW, measuredTextW;

    // "1:23 / 3:45" → 0~1 (뮤직 칸과 같은 계산을 쓴다)
    static double Progress(string time) => MusicPill.Progress(time);

    // 시간 맞춘 가사면 지금 줄을 가운데로, 아니면 곡 진행만큼 넘긴다. 휠로 넘긴 뒤 6초는 그대로 둔다
    void Follow()
    {
        if ((DateTime.Now - userScrolledAt).TotalSeconds < 6) return;
        var r = TextRect;
        int t = cur >= 0 ? tops[cur] + heights[cur] / 2 - r.Height / 2 : (int)(progress * MaxScroll);
        target = Math.Clamp(t, 0, MaxScroll);
        if (target != scroll) ease.Start();
    }

    Rectangle TextRect => new(S(16), S(32), Width - S(32), Height - S(40));
    int MaxScroll => Math.Max(0, contentH - TextRect.Height + S(8));

    void Measure()
    {
        int w = TextRect.Width, y = 0, gap = S(6);
        tops = new int[lines.Length]; heights = new int[lines.Length];
        for (int i = 0; i < lines.Length; i++)
        {
            // 지금 줄은 굵게 그리므로 굵은 글씨 기준으로 잰다 (줄이 바뀌어도 높이가 안 흔들리게)
            int h = TextRenderer.MeasureText(lines[i].Length == 0 ? " " : lines[i], bold, new Size(w, int.MaxValue), Flags).Height;
            tops[i] = y; heights[i] = h; y += h + gap;
        }
        contentH = y;
    }

    /// <summary>뮤직 칸(화면 좌표) 바로 아래에 둔다. 아래 공간이 모자라면 위에 둔다.</summary>
    public void Place(Rectangle pill)
    {
        var wa = Screen.FromRectangle(pill).WorkingArea;
        int w = Math.Max(S(280), pill.Width), h = S(230);
        int x = Math.Clamp(pill.Left + (pill.Width - w) / 2, wa.Left, Math.Max(wa.Left, wa.Right - w));
        int y = pill.Bottom + S(6);
        if (y + h > wa.Bottom) y = Math.Max(wa.Top, pill.Top - S(6) - h);
        var b = new Rectangle(x, y, w, h);
        if (Bounds != b) { Bounds = b; Measure(); Follow(); Invalidate(); }
    }

    // 모서리는 윈도우가 매끄럽게 둥글린다 (직접 자르면 가장자리에 하얀 계단이 생긴다)
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UI.RoundCorners(Handle);
        int border = 0xD4E0EA; // 옅은 베이지 테두리 (0x00BBGGRR)
        try { DwmSetWindowAttribute(Handle, 34, ref border, sizeof(int)); } catch { }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        ease.Stop();
        scroll = target = Math.Clamp(scroll - Math.Sign(e.Delta) * S(40), 0, MaxScroll);
        userScrolledAt = DateTime.Now;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && CloseRect.Contains(e.Location)) onClose();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        // 머리: 주황 점 + 옅은 '가사'
        using (var dot = new SolidBrush(UI.Accent))
            g.FillEllipse(dot, S(14), S(13), S(6), S(6));
        TextRenderer.DrawText(g, "가사", head, new Point(S(24), S(8)), Color.FromArgb(150, 140, 130), TextFormatFlags.NoPrefix);
        // ✕ (가사 보기 끄기)
        var c = CloseRect;
        using (var p = new Pen(Color.FromArgb(185, 180, 175), Math.Max(1.2f, S(2) * 0.6f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            float cx = c.X + c.Width / 2f, cy = c.Y + c.Height / 2f, d = S(4);
            g.DrawLine(p, cx - d, cy - d, cx + d, cy + d);
            g.DrawLine(p, cx - d, cy + d, cx + d, cy - d);
        }
        var r = TextRect;
        g.SetClip(r);
        for (int i = 0; i < lines.Length; i++)
        {
            int y = r.Y + tops[i] - scroll;
            if (y + heights[i] < r.Y || y > r.Bottom) continue;
            bool now = i == cur;
            var lr = new Rectangle(r.X, y, r.Width, heights[i]);
            if (now && lines[i].Trim().Length > 0)
            {
                // 지금 줄 뒤에 옅은 주황 띠
                // 줄 내용과 폭이 같으면 지난번에 잰 값을 쓴다 (글꼴은 바뀌지 않는다)
                if (measuredText != lines[i] || measuredW != r.Width)
                {
                    measuredText = lines[i]; measuredW = r.Width;
                    measuredTextW = TextRenderer.MeasureText(lines[i], bold, new Size(r.Width, int.MaxValue), Flags).Width;
                }
                int tw = Math.Min(r.Width, measuredTextW + S(16));
                using var hb = new SolidBrush(Color.FromArgb(26, UI.Accent));
                using var hp = UI.RoundRect(new RectangleF(r.X + (r.Width - tw) / 2f, y - S(1), tw, heights[i] + S(2)), S(8));
                g.FillPath(hb, hp);
            }
            // 시간 맞춘 가사: 지난 줄은 아주 옅게, 다음 줄은 회색, 지금 줄은 주황 굵게
            var col = now ? UI.Accent
                : cur < 0 ? Color.FromArgb(70, 66, 62)
                : i < cur ? Color.FromArgb(200, 196, 192) : Color.FromArgb(125, 120, 115);
            TextRenderer.DrawText(g, lines[i], now ? bold : font, lr, col, Flags);
        }
        // 위아래 끝은 잘리지 않고 스르륵 사라지게
        int fh = S(18);
        if (scroll > 0)
            using (var fb = new LinearGradientBrush(new Rectangle(r.X, r.Y - 1, r.Width, fh + 1), BackColor, Color.FromArgb(0, BackColor), 90f))
                g.FillRectangle(fb, r.X, r.Y, r.Width, fh);
        if (scroll < MaxScroll)
            using (var fb = new LinearGradientBrush(new Rectangle(r.X, r.Bottom - fh - 1, r.Width, fh + 1), Color.FromArgb(0, BackColor), BackColor, 90f))
                g.FillRectangle(fb, r.X, r.Bottom - fh, r.Width, fh);
        g.ResetClip();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { ease.Dispose(); font.Dispose(); bold.Dispose(); head.Dispose(); }
        base.Dispose(disposing);
    }
}
