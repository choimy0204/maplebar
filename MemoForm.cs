using static MapleBar.Native;

namespace MapleBar;

/// <summary>
/// 메모 (도구 → 유틸리티). 윈도우 스티커 메모처럼 항상 위에 뜨는 작은 창.
/// 페이지를 늘려 가며 쓰고 ◀ ▶로 넘긴다. 글은 치는 대로 잠깐 모았다가 설정에 저장한다.
/// 끌어서 옮기고 가장자리를 끌어 크기를 바꾼다 (자리와 크기를 기억한다).
/// </summary>
sealed class MemoForm : Form
{
    /// <summary>메뉴에서 쓰는 주소 (사이트 주소가 아니다)</summary>
    public const string Path = "maplebar:memo";
    const string PlaceKey = "memo";

    static MemoForm? open;

    public static void Open(Form owner, Settings st, float sc)
    {
        if (open == null || open.IsDisposed)
        {
            open = new MemoForm(st, sc) { Owner = owner };
            open.Location = WinPlace.Where(st, PlaceKey, open, owner, sc);
        }
        if (!open.Visible) open.Show();
        open.Activate();
    }

    readonly Settings st;
    readonly float sc;
    readonly Label title;
    readonly RoundLabel close, prev, next, pageLabel, addBtn, delBtn, paper;
    readonly TextBox box;
    readonly ToolTip tips = new();
    readonly System.Windows.Forms.Timer saveTimer = new() { Interval = 800 };
    readonly int pad, headH, footH;
    bool loading; // 페이지를 바꾸는 중 (글 칸 바뀜을 저장하지 않는다)

    int S(float v) => UI.Scale(v, sc);
    static Font F(float size, FontStyle style = FontStyle.Regular) => new("맑은 고딕", size * UI.Z, style);

    // 노란 스티커 느낌을 살짝: 밝은 화면은 연한 노랑, 어두운 화면은 바와 어울리는 어두운 황토색
    static Color PaperBack => UI.AmberPaper;

    MemoForm(Settings st, float sc)
    {
        this.st = st; this.sc = sc;
        st.MemoPages ??= new();
        for (int i = 0; i < st.MemoPages.Count; i++) st.MemoPages[i] ??= "";
        if (st.MemoPages.Count == 0) st.MemoPages.Add("");
        st.MemoPage = Math.Clamp(st.MemoPage, 0, st.MemoPages.Count - 1);

        Text = "메모";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        BackColor = UI.Back;
        ForeColor = UI.Text;
        Font = F(10);
        DoubleBuffered = true;

        pad = S(12); headH = S(44); footH = S(44);
        MinimumSize = new Size(S(240), S(200));
        var size = st.MemoSize is { Length: 2 } m ? new Size(m[0], m[1]) : new Size(S(300), S(320));
        Size = new Size(Math.Max(size.Width, MinimumSize.Width), Math.Max(size.Height, MinimumSize.Height));

        // 머리: 제목, 닫기. 빈 곳을 끌면 창이 움직인다
        title = new Label { Text = "메모", Font = F(12, FontStyle.Bold), AutoSize = true, Location = new Point(pad + S(2), S(12)) };
        title.MouseDown += (_, e) => Drag(e);
        MouseDown += (_, e) => Drag(e);
        close = UI.Button("✕", "닫기", Close, sc);
        close.Font = F(10);

        // 글 칸: 둥근 칸 위에 테두리 없는 여러 줄 글 칸
        paper = new RoundLabel { AutoSize = false, Radius = S(10), Fill = PaperBack, Cursor = Cursors.IBeam };
        box = new TextBox
        {
            Multiline = true, AcceptsReturn = true, WordWrap = true, ScrollBars = ScrollBars.None,
            BorderStyle = BorderStyle.None, BackColor = PaperBack, ForeColor = UI.Text, Font = F(10.5f),
            ImeMode = ImeMode.NoControl, // 한글도 쓸 수 있게
        };
        paper.Click += (_, _) => { box.Focus(); box.SelectionStart = box.TextLength; };
        box.TextChanged += (_, _) =>
        {
            if (loading) return;
            st.MemoPages[st.MemoPage] = box.Text;
            saveTimer.Stop(); saveTimer.Start();
        };
        box.KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.A) { box.SelectAll(); e.SuppressKeyPress = true; }
        };
        saveTimer.Tick += (_, _) => { saveTimer.Stop(); st.Save(); };

        // 아래: ◀ 1 / 3 ▶ 넘기기, 새 페이지, 지우기
        RoundLabel Btn(string text, string tip, float size)
        {
            var b = new RoundLabel
            {
                AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Font = F(size, FontStyle.Bold),
                Radius = S(8), Text = text, UseMnemonic = false, Fill = UI.HoverBack, ForeColor = UI.Text, Cursor = Cursors.Hand,
            };
            if (tip != "") tips.SetToolTip(b, tip);
            return b;
        }
        prev = Btn("◀", "앞 페이지", 10);
        next = Btn("▶", "다음 페이지", 10);
        pageLabel = Btn("", "", 9.5f);
        pageLabel.Cursor = Cursors.Default;
        pageLabel.Fill = Color.Transparent;
        pageLabel.MouseDown += (_, e) => Drag(e);
        addBtn = Btn("＋", "새 페이지", 11);
        delBtn = Btn("지우기", "이 페이지 지우기", 9.5f);
        prev.Click += (_, _) => Go(st.MemoPage - 1);
        next.Click += (_, _) => Go(st.MemoPage + 1);
        addBtn.Click += (_, _) => AddPage();
        delBtn.Click += (_, _) => DeletePage();
        foreach (var b in new[] { addBtn, delBtn })
        {
            b.MouseEnter += (_, _) => b.Fill = UI.AmberSoft;
            b.MouseLeave += (_, _) => b.Fill = UI.HoverBack;
        }

        Controls.Add(title);
        Controls.Add(close);
        Controls.Add(box);
        Controls.Add(paper);
        Controls.Add(prev);
        Controls.Add(pageLabel);
        Controls.Add(next);
        Controls.Add(addBtn);
        Controls.Add(delBtn);

        LayoutAll();
        ShowPage();
        ActiveControl = null;
    }

    // ── 페이지 ──

    void ShowPage()
    {
        loading = true;
        box.Text = st.MemoPages[st.MemoPage];
        box.SelectionStart = box.TextLength;
        box.ScrollToCaret();
        loading = false;

        int p = st.MemoPage, n = st.MemoPages.Count;
        pageLabel.Text = $"{p + 1} / {n}";
        foreach (var (b, ok) in new[] { (prev, p > 0), (next, p < n - 1) })
        {
            b.Fill = ok ? (UI.AmberSoft) : UI.HoverBack;
            b.ForeColor = ok ? UI.AmberText : UI.T(Color.FromArgb(190, 190, 196));
            b.Cursor = ok ? Cursors.Hand : Cursors.Default;
        }
    }

    void Go(int to)
    {
        if (to < 0 || to >= st.MemoPages.Count || to == st.MemoPage) return;
        st.MemoPage = to;
        SaveNow();
        ShowPage();
    }

    void AddPage()
    {
        st.MemoPages.Add("");
        st.MemoPage = st.MemoPages.Count - 1;
        SaveNow();
        ShowPage();
        box.Focus();
    }

    void DeletePage()
    {
        bool only = st.MemoPages.Count == 1;
        bool empty = string.IsNullOrWhiteSpace(st.MemoPages[st.MemoPage]);
        if (only && empty) return;
        if (!empty)
        {
            bool yes = only
                ? AskBox.Show(this, "글을 모두 지울까요?", "지운 글은 되돌릴 수 없어요.", "지우기")
                : AskBox.Show(this, "이 페이지를 지울까요?", "지운 글은 되돌릴 수 없어요.", "지우기");
            if (!yes) return;
        }
        if (only) st.MemoPages[0] = "";
        else
        {
            st.MemoPages.RemoveAt(st.MemoPage);
            st.MemoPage = Math.Min(st.MemoPage, st.MemoPages.Count - 1);
        }
        SaveNow();
        ShowPage();
    }

    void SaveNow()
    {
        saveTimer.Stop();
        st.Save();
    }

    // ── 배치 ──

    void LayoutAll()
    {
        if (paper == null) return; // 만드는 중 크기가 먼저 바뀔 때
        int W = ClientSize.Width, H = ClientSize.Height;
        close.Location = new Point(W - pad - close.Width + S(4), (headH - close.Height) / 2 + S(2));

        paper.Bounds = new Rectangle(pad, headH, W - pad * 2, H - headH - footH);
        int ip = S(10);
        box.Bounds = new Rectangle(paper.Left + ip, paper.Top + ip, paper.Width - ip * 2, paper.Height - ip * 2);

        int bh = S(30), aw = S(36), g = S(6), y = H - footH + (footH - bh) / 2;
        prev.Bounds = new Rectangle(pad, y, aw, bh);
        pageLabel.Bounds = new Rectangle(prev.Right + g / 2, y, S(56), bh);
        next.Bounds = new Rectangle(pageLabel.Right + g / 2, y, aw, bh);
        delBtn.Bounds = new Rectangle(W - pad - S(58), y, S(58), bh);
        addBtn.Bounds = new Rectangle(delBtn.Left - g - aw, y, aw, bh);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutAll();
        Invalidate();
    }

    protected override void OnResizeEnd(EventArgs e)
    {
        base.OnResizeEnd(e);
        RememberSize();
        WinPlace.Remember(st, PlaceKey, this);
    }

    void RememberSize()
    {
        if (WindowState != FormWindowState.Normal) return;
        if (st.MemoSize is { Length: 2 } m && m[0] == Width && m[1] == Height) return;
        st.MemoSize = new[] { Width, Height };
        st.Save();
    }

    // ── 창 ──

    void Drag(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        DragWindow(Handle);
        WinPlace.Remember(st, PlaceKey, this);
    }

    /// <summary>테두리 없는 창이라 가장자리를 잡으면 크기를 바꾸게 알려 준다</summary>
    protected override void WndProc(ref Message m)
    {
        const int WM_NCHITTEST = 0x84;
        base.WndProc(ref m);
        if (m.Msg != WM_NCHITTEST || (int)m.Result != 1) return; // 1 = 안쪽
        var p = PointToClient(new Point(unchecked((short)(long)m.LParam), unchecked((short)((long)m.LParam >> 16))));
        int e = S(6);
        bool l = p.X < e, r = p.X >= ClientSize.Width - e, t = p.Y < e, b = p.Y >= ClientSize.Height - e;
        int hit = (t, b, l, r) switch
        {
            (true, _, true, _) => 13,  // 왼쪽 위
            (true, _, _, true) => 14,  // 오른쪽 위
            (_, true, true, _) => 16,  // 왼쪽 아래
            (_, true, _, true) => 17,  // 오른쪽 아래
            (true, _, _, _) => 12,
            (_, true, _, _) => 15,
            (_, _, true, _) => 10,
            (_, _, _, true) => 11,
            _ => 0,
        };
        if (hit != 0) m.Result = hit;
    }

    /// <summary>다른 창(게임)으로 가면 글을 바로 저장한다</summary>
    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (saveTimer.Enabled) SaveNow();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UI.RoundCorners(Handle);
    }

    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ClassStyle |= 0x20000; return cp; } // 창 그림자
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        st.MemoPages[st.MemoPage] = box.Text;
        RememberSize();
        WinPlace.Remember(st, PlaceKey, this);
        st.Save();
        saveTimer.Dispose();
        tips.Dispose();
        base.OnFormClosed(e);
    }
}
