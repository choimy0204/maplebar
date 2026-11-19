using System.Drawing.Drawing2D;

namespace MapleBar;

/// <summary>
/// 검색칸 아래로 펼쳐지는 최근 검색 목록 (콤보박스처럼).
/// 창을 빼앗지 않아서 목록이 떠 있어도 검색칸에 계속 쓸 수 있다.
/// 줄 왼쪽 ★/☆은 별표, 이름을 누르면 캐릭터정보.
/// </summary>
sealed class RecentDrop : Form
{
    readonly Settings st;
    readonly float sc;
    readonly Font font, small;
    readonly ToolTip tip = new() { InitialDelay = 0, ShowAlways = true };
    readonly List<string> names = new();
    bool hasClear, empty;
    int hot = -1;
    bool hotStar, tipOn;

    public Action<string>? PickInfo;

    int S(float v) => UI.Scale(v, sc);
    int RowH => S(32);
    int Pad => S(6);
    int ClearIdx => names.Count;
    int RowCount => empty ? 1 : names.Count + (hasClear ? 1 : 0);
    Color HotText => UI.Dark ? UI.AccentText : Color.FromArgb(200, 95, 0);

    public RecentDrop(Form owner, Settings st, float sc)
    {
        this.st = st;
        this.sc = sc;
        Owner = owner;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        // TopMost 속성은 쓰지 않는다: 켜는 순간 WinForms가 창을 앞으로 가져오며 이 목록이 창을 빼앗고,
        // 그러면 바가 비활성이 되어 목록을 곧바로 닫아 버린다 (반짝 떴다 사라지던 원인).
        // 맨 위 고정은 아래 CreateParams의 WS_EX_TOPMOST로만 한다.
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = UI.Back;
        font = new Font("맑은 고딕", 10f * UI.Z);
        small = new Font("맑은 고딕", 8.5f * UI.Z);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x08000000 | 0x80 | 0x8; // 창을 빼앗지 않음 · 작업 전환 목록에 안 나옴 · 맨 위
            cp.ClassStyle |= 0x20000;        // 그림자
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        const int WM_MOUSEACTIVATE = 0x21, MA_NOACTIVATE = 3;
        if (m.Msg == WM_MOUSEACTIVATE) { m.Result = MA_NOACTIVATE; return; }
        // 혹시라도 이 목록이 창을 가져가면 기록해 둔다 (다시 생기면 원인 찾기용)
        if (m.Msg == 0x06 && (m.WParam.ToInt64() & 0xFFFF) != 0) Log.Write("RecentDrop", "목록이 활성 창이 됨");
        base.WndProc(ref m);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UI.RoundCorners(Handle);
    }

    /// <summary>목록을 다시 채워 검색칸 아래에 띄운다. 보여 줄 게 없으면 숨긴다.</summary>
    public void Open(Point at, int width, string filter)
    {
        names.Clear();
        names.AddRange(st.Recent);
        // 별표한 캐릭터는 기록에서 밀려나거나 지워져도 목록에 남긴다
        var star = st.StarNick;
        if (star != null && !names.Any(x => Same(x, star))) names.Insert(0, star);
        if (filter.Length > 0) names.RemoveAll(n => n.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0);
        empty = names.Count == 0 && filter.Length == 0;
        hasClear = filter.Length == 0 && st.Recent.Count > 0;
        if (RowCount == 0) { Hide(); return; }
        if (hot >= RowCount) hot = -1;

        Bounds = new Rectangle(at, new Size(width, RowTop(RowCount - 1) + RowH + Pad));
        if (!Visible) Show(Owner);
        Invalidate();
    }

    /// <summary>위·아래 화살표로 고르기</summary>
    public void Step(int d)
    {
        if (empty || names.Count == 0) return;
        hot = hot < 0 ? (d > 0 ? 0 : names.Count - 1) : (hot + d + names.Count) % names.Count;
        hotStar = false;
        Invalidate();
    }

    /// <summary>화살표로 고른 캐릭터 (없으면 null)</summary>
    public string? Selected => Visible && hot >= 0 && hot < names.Count ? names[hot] : null;

    public new void Hide()
    {
        hot = -1;
        if (tipOn) { tipOn = false; tip.Hide(this); }
        if (Visible) base.Hide();
    }

    static bool Same(string a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    bool Starred(string n) => Same(n, st.StarNick);

    int RowTop(int i) => Pad + i * RowH + (hasClear && i == ClearIdx ? S(9) : 0);
    Rectangle RowRect(int i) => new(S(4), RowTop(i), Width - S(8), RowH);
    int StarW => TextRenderer.MeasureText("★", font).Width + S(14);

    int HitRow(Point p)
    {
        for (int i = 0; i < RowCount; i++) if (RowRect(i).Contains(p)) return empty ? -1 : i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int i = HitRow(e.Location);
        bool isName = i >= 0 && i < names.Count;
        bool star = isName && e.X < RowRect(i).X + StarW;
        if (i != hot || star != hotStar) { hot = i; hotStar = star; Invalidate(); }
        Cursor = i >= 0 ? Cursors.Hand : Cursors.Default;
        if (star != tipOn)
        {
            tipOn = star;
            if (star) tip.Show("별표한 캐릭터는 검색칸이 비어 있을 때\n기본으로 불러와요", this, e.Location + new Size(S(14), S(18)));
            else tip.Hide(this);
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        hot = -1; hotStar = false;
        if (tipOn) { tipOn = false; tip.Hide(this); }
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        int i = HitRow(e.Location);
        if (i < 0) return;
        if (hasClear && i == ClearIdx)
        {
            st.Recent.Clear();
            st.Save();
            Hide();
            return;
        }
        var n = names[i];
        if (e.X < RowRect(i).X + StarW)
        {
            // 다른 캐릭터를 누르면 그쪽으로 옮기고, 같은 캐릭터를 다시 누르면 끈다
            st.StarNick = Starred(n) ? null : n;
            st.Save();
            Invalidate();
            return;
        }
        Hide();
        PickInfo?.Invoke(n);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;

        if (empty)
        {
            var r = RowRect(0);
            TextRenderer.DrawText(g, "검색 기록이 없어요", font, new Rectangle(r.X + S(10), r.Y, r.Width - S(20), r.Height), UI.Sub, flags);
            return;
        }

        using var hotBack = new SolidBrush(UI.SoftAccent);
        for (int i = 0; i < names.Count; i++)
        {
            var r = RowRect(i);
            bool isHot = i == hot;
            if (isHot) using (var path = UI.RoundRect(r, S(7))) g.FillPath(hotBack, path);

            var n = names[i];
            bool starred = Starred(n);
            Color starColor = starred ? UI.Accent : (isHot && hotStar ? HotText : UI.Sub);
            TextRenderer.DrawText(g, starred ? "★" : "☆", font, new Rectangle(r.X + S(8), r.Y, StarW, r.Height), starColor, flags);

            int nameX = r.X + StarW;
            int nameR = r.Right - S(8);
            TextRenderer.DrawText(g, n, font, new Rectangle(nameX, r.Y, nameR - nameX, r.Height),
                isHot && !hotStar ? HotText : UI.Text, flags);
        }

        if (hasClear)
        {
            int ly = RowTop(ClearIdx) - S(5);
            using (var pen = new Pen(UI.Line)) g.DrawLine(pen, S(12), ly, Width - S(12), ly);
            var r = RowRect(ClearIdx);
            bool isHot = hot == ClearIdx;
            if (isHot) using (var path = UI.RoundRect(r, S(7))) g.FillPath(hotBack, path);
            TextRenderer.DrawText(g, "기록 지우기", font, new Rectangle(r.X + S(10), r.Y, r.Width - S(20), r.Height), isHot ? HotText : UI.Sub, flags);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { font.Dispose(); small.Dispose(); tip.Dispose(); }
        base.Dispose(disposing);
    }
}
