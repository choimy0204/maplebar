using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using static MapleBar.Native;

namespace MapleBar;

/// <summary>게임 화면 위에 떠 있는 공부 카드. 목록처럼 위엔 이전 카드, 가운데 지금 카드, 아래엔 다음 카드를 한 줄씩 보여 준다.
/// 시간이 되면 목록이 한 칸 올라가며 아래 줄이 펼쳐지고 지금 카드는 접혀 위로 간다.
/// 아래 줄(또는 가운데)을 누르면 다음 카드, 위 줄을 누르면 이전 카드, 끌면 자리를 옮겨 기억한다,
/// ☆는 즐겨찾기, ✕는 카드 띄우기를 끈다. 마우스를 올려 두는 동안은 넘어가지 않고, 다른 알림이 뜨면 1초 늦게 넘어간다.
/// 게임 포커스는 뺏지 않는다.</summary>
sealed class StudyCard : Form
{
    static readonly Color Deep = Color.FromArgb(200, 95, 0);
    static readonly Color Gray = Color.FromArgb(130, 130, 136);
    static readonly Color RowGray = Color.FromArgb(120, 120, 126);
    static readonly Color Soft = Color.FromArgb(255, 245, 232);
    const double AnimSec = 0.6;
    const int Keep = 200;        // 뒤로 가기용으로 기억하는 카드 수

    readonly Settings st;
    readonly Func<(StudyDeck deck, string[] item)?> pick;
    readonly Action off;
    readonly System.Windows.Forms.Timer tick = new() { Interval = 15 };
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly float sc;
    readonly Font fHead, fBig, fMid, fSmall, fRow, fStar, fNote;
    readonly Bitmap measureBmp;
    readonly Graphics measure;

    // 지나온 카드와 다음 카드. idx가 지금 카드
    readonly List<(StudyDeck deck, string[] item)> line = new();
    int idx;
    readonly Dictionary<string[], (Bitmap row, Bitmap full)> art = new(ReferenceEqualityComparer.Instance);

    double animStart = -1; bool animBack; int fromHeight;
    double left, total, last;
    string note = ""; double noteUntil; int noteSec;
    int hover;                   // 1 = ☆, 2 = ✕, 3 = 위 줄, 4 = 아래 줄
    Point down, start; bool pressed, moved, placed;

    /// <summary>보여 줄 카드가 하나도 없어서 띄울 수 없으면 true</summary>
    public bool Empty => line.Count == 0;

    /// <summary>카드에서 즐겨찾기를 바꿨을 때 (공부 창 개수를 맞춘다)</summary>
    public static event Action? Starred;

    public StudyCard(Settings st, Func<(StudyDeck deck, string[] item)?> pick, Action off)
    {
        this.st = st; this.pick = pick; this.off = off;
        sc = DeviceDpi / 96f * UI.Z;
        fHead = F(8.5f, FontStyle.Bold); fBig = F(14, FontStyle.Bold); fMid = F(10.5f); fSmall = F(9);
        fRow = F(9.5f); fStar = F(11); fNote = F(8, FontStyle.Bold);
        measureBmp = new Bitmap(1, 1);
        measureBmp.SetResolution(DeviceDpi, DeviceDpi);
        measure = Graphics.FromImage(measureBmp);
        measure.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.White;
        AutoScaleMode = AutoScaleMode.None;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);

        if (pick() is not { } first) return;
        line.Add(first);
        if (pick() is { } second) line.Add(second);
        total = left = Math.Max(3, st.StudySec);
        Fit();

        last = clock.Elapsed.TotalSeconds;
        tick.Tick += (_, _) => Tick();
        JanusAlert.Played += OnAlert;
        HandleCreated += (_, _) => UI.RoundCorners(Handle);
        Shown += (_, _) => tick.Start();
    }

    int S(float v) => UI.Scale(v, sc);
    static Font F(float size, FontStyle style = FontStyle.Regular) => new("맑은 고딕", size * UI.Z, style);

    int W => S(360);
    int HeadH => S(34);
    int RowH => S(28);
    int Gap => S(4);
    int Foot => S(10) + S(3);
    int BoxX => S(10);
    int TextX => BoxX + S(14);
    int TextW => W - TextX - S(24);
    Rectangle XRect => new(W - S(8) - S(26), S(6), S(26), S(24));
    Rectangle StarRect => new(XRect.Left - S(28), S(6), S(26), S(24));
    Rectangle PauseRect => new(StarRect.Left - S(28), S(6), S(26), S(24));
    bool paused;

    (StudyDeck deck, string[] item)? At(int i) => i >= 0 && i < line.Count ? line[i] : null;
    (StudyDeck deck, string[] item) Cur => line[idx];

    void Tick()
    {
        double now = clock.Elapsed.TotalSeconds, dt = now - last;
        last = now;
        if (!paused && !Bounds.Contains(Cursor.Position) && animStart < 0) left -= dt; // 멈췄거나 올려 두면 멈춘다
        if (note.Length > 0 && now > noteUntil) { note = ""; noteSec = 0; }
        if (animStart >= 0)
        {
            var (b, e) = Frame();
            ClientSize = new Size(W, HeadH + ViewH(b, e) + Foot);
            if (now - animStart >= AnimSec) { animStart = -1; Fit(); Trim(); }
        }
        if (left <= 0) { Next(); return; }
        Invalidate();
    }

    /// <summary>다른 알림이 뜨면 1초 늦게 넘어간다</summary>
    void OnAlert()
    {
        if (IsDisposed || Empty) return;
        left += 1;
        total = Math.Max(total, left);
        noteSec++;
        note = $"알림으로 {noteSec}초 늘었어요";
        noteUntil = clock.Elapsed.TotalSeconds + 2.5;
        Invalidate();
    }

    /// <summary>다음 카드로 내려간다. 보여 줄 카드가 없으면 닫는다</summary>
    public void Next()
    {
        if (IsDisposed || Empty) return;
        // 아직 안 본 다음 카드가 그새 볼 수 없게 됐으면 (즐겨찾기 해제, 묶음 끄기) 다시 고른다
        if (At(idx + 1) is var (nd, ni) && !StudyRun.Usable(st, nd, ni) && idx + 1 == line.Count - 1)
        {
            line.RemoveAt(idx + 1);
            if (pick() is { } p) line.Add(p);
        }
        if (idx + 1 >= line.Count) { if (pick() is { } p) line.Add(p); else { Close(); return; } }
        Begin(false);
        idx++;
        if (idx + 1 >= line.Count && pick() is { } after) line.Add(after);
    }

    /// <summary>이전 카드로 올라간다</summary>
    public void Back()
    {
        if (IsDisposed || idx == 0) return;
        Begin(true);
        idx--;
    }

    void Begin(bool back)
    {
        fromHeight = ClientSize.Height;
        animBack = back;
        animStart = clock.Elapsed.TotalSeconds;
        total = left = Math.Max(3, st.StudySec);
    }

    /// <summary>오래된 카드는 잊는다</summary>
    void Trim()
    {
        if (idx > Keep) { line.RemoveRange(0, idx - Keep); idx = Keep; }
        var live = new HashSet<string[]>(ReferenceEqualityComparer.Instance);
        for (int i = idx - 2; i <= idx + 2; i++) if (At(i) is var (_, it)) live.Add(it);
        foreach (var k in art.Keys.Where(k => !live.Contains(k)).ToList()) { art[k].row.Dispose(); art[k].full.Dispose(); art.Remove(k); }
    }

    // ── 목록 배치 ──
    // base 카드가 가운데로 펼쳐지는 중(e: 0 → 1). 위에서부터 A(base-2, 빠져나감) B(base-1, 접힘) C(base, 펼쳐짐) D(base+1, 들어옴)

    /// <summary>지금 그릴 장면: 앞으로 가면 새 카드가 펼쳐지고, 뒤로 가면 같은 장면을 거꾸로 돌린다</summary>
    (int b, double e) Frame()
    {
        if (animStart < 0) return (idx, 1);
        double t = Math.Clamp((clock.Elapsed.TotalSeconds - animStart) / AnimSec, 0, 1);
        double e = t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2; // 천천히 출발해서 천천히 멈춘다
        return animBack ? (idx + 1, 1 - e) : (idx, e);
    }

    int Full((StudyDeck deck, string[] item)? c) => c is var (d, it) ? CurHeight(d, it) : RowH;
    int ViewH(int b, double e) => RowH * 2 + Gap * 2 + (int)Math.Round(Lerp(Full(At(b - 1)), Full(At(b)), e));
    static double Lerp(double a, double b, double t) => a + (b - a) * t;

    // ── 그리기 ──

    static string Cell(string[] item, int i) => i < item.Length ? item[i] : "";
    static string Extra(StudyDeck d, string[] item)
    {
        var s = Cell(item, 2);
        return s.Length == 0 ? "" : $"{(d.Cols.Length > 2 ? d.Cols[2] : "첨언")} · {s}";
    }

    int H(string s, Font f) => s.Length == 0 ? 0 : (int)Math.Ceiling(measure.MeasureString(s, f, TextW).Height);

    int CurHeight(StudyDeck d, string[] item)
    {
        int h = S(10) + H(Cell(item, 0), fBig) + S(2) + H(Cell(item, 1), fMid);
        var x = Extra(d, item);
        if (x.Length > 0) h += S(6) + H(x, fSmall);
        return h + S(12);
    }

    Bitmap NewBmp(int h)
    {
        var bmp = new Bitmap(W, Math.Max(1, h), PixelFormat.Format32bppPArgb);
        bmp.SetResolution(DeviceDpi, DeviceDpi);
        return bmp;
    }

    /// <summary>카드 하나의 두 모습: 한 줄로 접힌 것과 펼친 것 (둘 다 배경 없이 글자만)</summary>
    (Bitmap row, Bitmap full) Art((StudyDeck deck, string[] item) c)
    {
        if (art.TryGetValue(c.item, out var a)) return a;
        var (d, item) = c;
        var row = NewBmp(RowH);
        using (var g = Graphics.FromImage(row))
        {
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            var s = (d.Starred(item) ? "★ " : "") + Cell(item, 0) + (Cell(item, 1).Length > 0 ? "  ·  " + Cell(item, 1) : "");
            using var sf = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter, LineAlignment = StringAlignment.Center };
            using var b = new SolidBrush(RowGray);
            g.DrawString(s, fRow, b, new RectangleF(TextX, 0, W - TextX - S(16), RowH), sf);
        }
        var full = NewBmp(CurHeight(d, item));
        using (var g = Graphics.FromImage(full))
        {
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            float y = S(10);
            void Text(string s, Font f, Color col)
            {
                if (s.Length == 0) return;
                using var b = new SolidBrush(col);
                g.DrawString(s, f, b, new RectangleF(TextX, y, TextW, H(s, f) + 2));
                y += H(s, f);
            }
            Text(Cell(item, 0), fBig, UI.Text);
            y += S(2);
            Text(Cell(item, 1), fMid, UI.Text);
            var x = Extra(d, item);
            if (x.Length > 0) { y += S(6); Text(x, fSmall, Gray); }
        }
        return art[c.item] = (row, full);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (Empty) return;
        var (deck, item) = Cur;

        // 위: 묶음 이름 · 알림 문구 · ☆ · ✕
        int nameRight = PauseRect.Left - S(6);
        string tag = paused ? "멈췄어요" : note;
        if (tag.Length > 0)
        {
            int nw = TextRenderer.MeasureText(tag, fNote).Width;
            TextRenderer.DrawText(g, tag, fNote, new Rectangle(nameRight - nw, 0, nw, HeadH), UI.Accent,
                TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            nameRight -= nw + S(8);
        }
        TextRenderer.DrawText(g, deck.Name, fHead, new Rectangle(S(16), 0, Math.Max(0, nameRight - S(16)), HeadH), Deep,
            TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        PauseButton(g);
        bool starred = deck.Starred(item);
        Button(g, StarRect, starred ? "★" : "☆", starred ? UI.Accent : Gray, hover == 1);
        Button(g, XRect, "✕", Gray, hover == 2);

        // 가운데 목록
        var (b, ev) = Frame();
        var view = new Rectangle(0, HeadH, W, Math.Max(0, ClientSize.Height - Foot - HeadH));
        double y = HeadH - (RowH + Gap) * ev;
        y = Item(g, view, At(b - 2), y, RowH, 0, 1 - ev, false);
        y = Item(g, view, At(b - 1), y, Lerp(Full(At(b - 1)), RowH, ev), 1 - ev, 1, animStart < 0 && hover == 3);
        y = Item(g, view, At(b), y, Lerp(RowH, Full(At(b)), ev), ev, 1, false);
        Item(g, view, At(b + 1), y, RowH, 0, ev, animStart < 0 && hover == 4);

        // 아래: 다음 카드까지 남은 시간
        using (var br = new SolidBrush(paused ? UI.Line : UI.Accent))
            g.FillRectangle(br, 0, ClientSize.Height - S(3), (int)(W * Math.Clamp(left / total, 0, 1)), S(3));
        using var pen = new Pen(UI.Line);
        g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    /// <summary>목록 한 칸. open: 0 = 한 줄로 접힘, 1 = 펼침. alpha: 들어오고 나가는 칸은 흐리게. 다음 칸의 y를 돌려준다</summary>
    double Item(Graphics g, Rectangle view, (StudyDeck deck, string[] item)? c, double y, double h, double open, double alpha, bool hot)
    {
        if (c is not { } card) return y + h + Gap;
        var box = new Rectangle(0, (int)Math.Round(y), W, (int)Math.Round(h));
        var clip = Rectangle.Intersect(view, box);
        if (clip.Height > 0 && alpha > 0.01)
        {
            var (row, full) = Art(card);
            var state = g.Save();
            g.SetClip(clip);
            var bg = new RectangleF(BoxX, box.Y, W - BoxX * 2, box.Height);
            if (hot)
            {
                using var path = UI.RoundRect(bg, S(8));
                using var hb = new SolidBrush(UI.HoverBack);
                g.FillPath(hb, path);
            }
            if (open > 0.01)
            {
                // 펼친 칸: 연한 주황 바탕과 왼쪽 주황 줄이 같이 짙어진다
                using var path = UI.RoundRect(bg, S(10));
                using var sb = new SolidBrush(Color.FromArgb((int)(255 * open * alpha), Soft));
                g.FillPath(sb, path);
                using var ab = new SolidBrush(Color.FromArgb((int)(255 * open * alpha), UI.Accent));
                g.FillRectangle(ab, BoxX, box.Y + S(12), S(3), Math.Max(0, box.Height - S(24)));
                Blend(g, full, box.Y, (float)(open * alpha));
            }
            if (open < 0.99) Blend(g, row, box.Y, (float)((1 - open) * alpha));
            g.Restore(state);
        }
        return y + h + Gap;
    }

    void Button(Graphics g, Rectangle r, string text, Color col, bool hot)
    {
        if (hot)
        {
            using var path = UI.RoundRect(new RectangleF(r.X, r.Y, r.Width, r.Height), S(6));
            using var b = new SolidBrush(UI.HoverBack);
            g.FillPath(b, path);
        }
        TextRenderer.DrawText(g, text, text == "✕" ? fSmall : fStar, r, col,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    /// <summary>멈춤(막대 두 개) / 다시 시작(세모). 글자 대신 도형으로 또렷하게, 연한 주황 바탕 위에 그린다</summary>
    void PauseButton(Graphics g)
    {
        var r = PauseRect;
        using (var path = UI.RoundRect(new RectangleF(r.X, r.Y, r.Width, r.Height), S(6)))
        using (var bg = new SolidBrush(paused ? UI.Accent : hover == 5 ? Color.FromArgb(255, 226, 190) : UI.SoftAccent))
            g.FillPath(bg, path);
        float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f, h = S(10);
        using var fg = new SolidBrush(paused ? Color.White : Deep);
        if (paused)
        {
            float w = S(9);
            g.FillPolygon(fg, new[] { new PointF(cx - w / 2 + S(1), cy - h / 2), new PointF(cx + w / 2 + S(1), cy), new PointF(cx - w / 2 + S(1), cy + h / 2) });
        }
        else
        {
            float bw = S(3), gap = S(3);
            g.FillRectangle(fg, cx - gap / 2 - bw, cy - h / 2, bw, h);
            g.FillRectangle(fg, cx + gap / 2, cy - h / 2, bw, h);
        }
    }

    static void Blend(Graphics g, Bitmap bmp, int y, float alpha)
    {
        if (alpha <= 0.01f) return;
        var dest = new Rectangle(0, y, bmp.Width, bmp.Height);
        if (alpha >= 0.99f) { g.DrawImage(bmp, dest, 0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel); return; }
        using var ia = new ImageAttributes();
        ia.SetColorMatrix(new ColorMatrix { Matrix33 = alpha });
        g.DrawImage(bmp, dest, 0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel, ia);
    }

    // ── 자리 ──

    /// <summary>위쪽 자리는 그대로 두고 높이만 맞춘다. 처음이면 기억한 자리에 놓는다</summary>
    void Fit()
    {
        ClientSize = new Size(W, HeadH + ViewH(idx, 1) + Foot);
        if (!placed) { Place(); placed = true; return; }
        var a = Area();
        if (Bottom > a.Bottom) Top = Math.Max(a.Top, a.Bottom - Height);
    }

    static Rectangle Area() => Game.Area() ?? Screen.PrimaryScreen!.WorkingArea;

    /// <summary>게임 화면 안 기억한 자리 (처음엔 오른쪽 위에서 조금 아래). 게임이 없으면 주 모니터</summary>
    void Place()
    {
        var a = Area();
        double fx = st.StudyFX ?? 1, fy = st.StudyFY ?? 0.18;
        int x = a.Left + (int)((a.Width - Width) * fx), y = a.Top + (int)((a.Height - Height) * fy);
        if (st.StudyFX == null) x -= S(24);
        Location = new Point(Math.Clamp(x, a.Left, Math.Max(a.Left, a.Right - Width)), Math.Clamp(y, a.Top, Math.Max(a.Top, a.Bottom - Height)));
    }

    // ── 마우스: 아래 줄·가운데는 다음 카드, 위 줄은 이전 카드, 끌면 옮긴다, ☆ 즐겨찾기, ✕ 끄기 ──

    int Hit(Point p)
    {
        if (StarRect.Contains(p)) return 1;
        if (XRect.Contains(p)) return 2;
        if (PauseRect.Contains(p)) return 5;
        if (p.Y >= HeadH && p.Y < HeadH + RowH) return idx > 0 ? 3 : 0;
        int nextTop = HeadH + RowH + Gap + Full(At(idx)) + Gap;
        if (p.Y >= nextTop && p.Y < nextTop + RowH && At(idx + 1) != null) return 4;
        return 0;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int h = pressed ? 0 : Hit(e.Location);
        if (h != hover) { hover = h; Invalidate(); }
        if (!pressed) return;
        var p = Cursor.Position;
        if (!moved && Math.Abs(p.X - down.X) + Math.Abs(p.Y - down.Y) < 6) return;
        if (!moved) { moved = true; start = Location; }
        Location = new Point(start.X + p.X - down.X, start.Y + p.Y - down.Y);
    }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (hover != 0) { hover = 0; Invalidate(); } }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        switch (Hit(e.Location))
        {
            case 1: ToggleStar(); return;
            case 2: off(); return;
            case 5: paused = !paused; Invalidate(); return; // 다시 시작하면 남은 시간부터 이어 간다
        }
        pressed = true; moved = false; down = Cursor.Position;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!pressed) return;
        pressed = false;
        if (!moved)
        {
            if (animStart >= 0) return; // 넘어가는 중에는 한 번만
            if (Hit(e.Location) == 3) Back(); else Next();
            hover = 0;
            return;
        }
        // 게임 화면에 대한 비율로 기억한다
        var a = Area();
        st.StudyFX = a.Width > Width ? Math.Clamp((double)(Left - a.Left) / (a.Width - Width), 0, 1) : 0;
        st.StudyFY = a.Height > Height ? Math.Clamp((double)(Top - a.Top) / (a.Height - Height), 0, 1) : 0;
        st.Save();
    }

    void ToggleStar()
    {
        if (Empty) return;
        var (d, item) = Cur;
        d.Star(item, !d.Starred(item));
        st.Save();
        // 한 줄 모습에 ★ 표시가 붙으니 다시 그린다
        if (art.Remove(item, out var a)) { a.row.Dispose(); a.full.Dispose(); }
        Starred?.Invoke();
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            JanusAlert.Played -= OnAlert;
            tick.Dispose();
            foreach (var a in art.Values) { a.row.Dispose(); a.full.Dispose(); }
            art.Clear();
            measure.Dispose(); measureBmp.Dispose();
            foreach (var f in new[] { fHead, fBig, fMid, fSmall, fRow, fStar, fNote }) f.Dispose();
        }
        base.Dispose(disposing);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST; // 작업 표시줄에 안 나오고 포커스를 뺏지 않게
            cp.ClassStyle |= CS_DROPSHADOW;
            return cp;
        }
    }
}
