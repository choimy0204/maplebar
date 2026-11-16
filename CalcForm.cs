using System.Globalization;
using static MapleBar.Native;

namespace MapleBar;

/// <summary>계산 기록 한 줄 (설정에 저장한다)</summary>
public class CalcEntry
{
    public string Expr { get; set; } = "";   // 보여 줄 식 (예: "1,000 + 2 × 3 =")
    public string Result { get; set; } = ""; // 결과 (쉼표 없는 수)
    public string Note { get; set; } = "";   // 이 줄에 단 주석 (계산 이름과는 따로)
    public bool Saved { get; set; }          // 예전(줄마다 ☆ 저장) 설정에 남은 값. 지금은 쓰지 않는다
}

/// <summary>계산 중인 식의 한 칸 (수와 그 뒤 연산자)</summary>
public class CalcPart
{
    public decimal Num { get; set; }
    public string Op { get; set; } = "+";
}

/// <summary>계산 하나: 이름, 계산하던 내용, 그 계산의 기록(줄마다 주석)을 통째로 담는다</summary>
public class CalcPreset
{
    public string Name { get; set; } = "";              // 이름을 안 정했을 때 보이는 이름 (계산 1, 계산 2 …)
    public string Memo { get; set; } = "";              // 사용자가 정한 계산 이름 (있으면 Name 대신 보인다)
    public DateTime SavedAt { get; set; }               // [저장]한 때 (저장한 것 목록에만 쓴다)
    public List<CalcPart> Parts { get; set; } = new();
    public string Cur { get; set; } = "";
    public decimal Shown { get; set; }
    public bool JustDone { get; set; }
    public string? LastExpr { get; set; }
    public List<CalcEntry> History { get; set; } = new(); // 새 것이 앞
}

/// <summary>
/// 계산기 (도구 → 유틸리티). 곱셈·나눗셈을 먼저 계산하고, 메소 계산이 편하게 [만]·[억] 버튼과
/// "1억 2,345만" 같은 한글 단위 읽기를 보여 준다. 키보드로도 쓸 수 있다 (Ctrl+C로 결과 복사).
/// 위 칸은 지금 계산의 이름이다. 그 이름으로 한 계산 기록은 모두 이 계산에 묶이고, 기록 줄에는 주석만 단다.
/// [저장]은 이 계산(이름·기록·주석)을 통째로 [저장한 것]에 넣고, 거기서 언제든 다시 불러온다.
/// 계산(프리셋)마다 하던 내용을 따로 기억해서, 하던 계산을 두고 새 계산을 처음부터 할 수 있다.
/// </summary>
sealed partial class CalcForm : Form
{
    /// <summary>메뉴에서 쓰는 주소 (사이트 주소가 아니다)</summary>
    public const string Path = "maplebar:calc";
    const string PlaceKey = "calc";
    const int MaxHistory = 100; // 계산 하나에 남기는 기록 수

    static CalcForm? open;

    public static void Open(Form owner, Settings st, float sc)
    {
        if (open == null || open.IsDisposed)
        {
            open = new CalcForm(st, sc) { Owner = owner };
            open.Location = WinPlace.Where(st, PlaceKey, open, owner, sc);
        }
        if (!open.Visible) open.Show();
        open.Activate();
    }

    readonly Settings st;
    readonly float sc;
    readonly Label title, exprLabel, mainLabel, unitLabel;
    readonly RoundLabel close, histBtn, clearBtn, saveBtn, tabAll, tabSaved, presetBtn, scriptBtn;
    readonly List<Control> calcParts; // 스크립트 화면에서 숨기는 계산기 부분
    readonly TextBox memo;
    bool memoPick; // 사용자가 메모 칸을 직접 눌렀는지
    readonly Panel hist, histList;
    readonly SlimScroll histBox; // histList를 담아 얇은 막대로 스크롤한다
    readonly ToolTip tips = new();
    readonly int pad, baseW, bodyTop, bodyH, bottom, listTop, listH;
    readonly System.Windows.Forms.Timer noteTimer = new() { Interval = 1500 };

    // 계산 상태: 앞에 쌓인 (수, 연산자)들과 지금 넣는 수 (지금 프리셋에 그대로 옮겨 적는다)
    readonly List<(decimal num, char op)> parts = new();
    string cur = "";
    decimal shown;          // 큰 글자로 보이는 값
    bool justDone;          // 방금 [=]을 눌렀다 (숫자를 누르면 새 계산)
    string? lastExpr;       // 방금 계산한 식
    string? note;           // 잠깐 보여 줄 말
    int tab;                // 오른쪽 칸에서 보는 탭 (0 기록, 1 저장한 것)
    bool showSaved { get => tab == 1; set => tab = value ? 1 : 0; } // [저장한 것]을 보는 중
    bool loading;           // 프리셋을 불러오는 중 (메모 칸 바뀜을 무시)
    CalcEntry? lastEntry;   // 방금 계산했거나 불러온 기록

    CalcPreset P => st.CalcPresets[st.CalcPresetIndex];
    List<CalcEntry> H => P.History;

    int S(float v) => UI.Scale(v, sc);
    int HistW => S(230);
    static Font F(float size, FontStyle style = FontStyle.Regular) => new("맑은 고딕", size * UI.Z, style);

    CalcForm(Settings st, float sc)
    {
        this.st = st; this.sc = sc;
        Text = "계산기";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        BackColor = UI.Back;
        ForeColor = UI.Text;
        Font = F(10);
        DoubleBuffered = true;

        pad = S(16);
        int gap = S(6), btnW = S(62), btnH = S(46);
        int W = pad * 2 + btnW * 4 + gap * 3;
        int y = S(12);

        // 머리: 제목, 프리셋, [기록], 닫기. 빈 곳을 끌면 창이 움직인다
        title = new Label { Text = "계산기", Font = F(12, FontStyle.Bold), AutoSize = true, Location = new Point(pad, y) };
        title.MouseDown += (_, e) => Drag(e);
        MouseDown += (_, e) => Drag(e);
        presetBtn = new RoundLabel
        {
            UseMnemonic = false, AutoSize = false, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Cursor = Cursors.Hand,
            Font = F(9.5f, FontStyle.Bold), Radius = S(8), Fill = UI.HoverBack, ForeColor = UI.Text, Padding = new Padding(S(8), 0, S(4), 0),
        };
        presetBtn.Click += (_, _) => ShowPresetMenu();
        tips.SetToolTip(presetBtn, "하던 계산을 두고 새 계산하기");
        close = UI.Button("✕", "닫기", Close, sc);
        close.Font = F(10);
        histBtn = UI.Button("기록", "계산 기록 보기·숨기기", ToggleHistory, sc);
        histBtn.Font = F(9.5f, FontStyle.Bold);
        scriptBtn = UI.Button("스크립트", "식을 저장해 두고 값만 넣어 계산하는 화면", () => SetScriptView(!scriptView), sc);
        scriptBtn.Font = F(9.5f, FontStyle.Bold);
        Controls.Add(scriptBtn);
        Controls.Add(title);
        Controls.Add(presetBtn);
        Controls.Add(close);
        Controls.Add(histBtn);
        y += S(38);
        bodyTop = y;
        baseW = W;

        // 계산 이름 칸. [저장]은 이 계산을 기록·주석까지 통째로 저장한 것에 넣는다
        int saveW = S(56), memoH = S(32);
        var memoBack = new RoundLabel { AutoSize = false, Bounds = new Rectangle(pad, y, W - pad * 2 - saveW - gap, memoH), Radius = S(8), Fill = UI.HoverBack, Cursor = Cursors.IBeam };
        memo = new TextBox
        {
            BorderStyle = BorderStyle.None, BackColor = UI.HoverBack, ForeColor = UI.Text, Font = F(9.5f), MaxLength = 60,
            PlaceholderText = "계산 이름", TabStop = false, ImeMode = ImeMode.NoControl, // 한글도 쓸 수 있게
        };
        memo.Width = memoBack.Width - S(20);
        memo.Location = new Point(S(10), (memoH - memo.Height) / 2);
        memo.TextChanged += (_, _) => { if (loading) return; P.Memo = memo.Text.Trim(); UpdatePresetBtn(); };
        // 메모 칸은 눌렀을 때만 들어간다. 창이 켜지거나 다시 앞으로 올 때 저절로 들어가면 바로 뺀다
        memo.MouseDown += (_, _) => memoPick = true;
        memo.Enter += (_, _) =>
        {
            // 누를 때는 Enter가 MouseDown보다 먼저 오므로, 한 박자 뒤에 확인한다
            BeginInvoke(() => { if (!memoPick && memo.Focused) Unfocus(); });
        };
        memo.Leave += (_, _) => { memoPick = false; st.Save(); };
        memoBack.Controls.Add(memo);
        memoBack.Click += (_, _) => { memoPick = true; memo.Focus(); };
        saveBtn = new RoundLabel
        {
            Text = "저장", UseMnemonic = false, AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Cursor = Cursors.Hand,
            Font = F(9.5f, FontStyle.Bold), Radius = S(8), Bounds = new Rectangle(memoBack.Right + gap, y, saveW, memoH),
            Fill = UI.SoftAccent, ForeColor = UI.AccentText,
        };
        var saveHover = UI.Dark ? ControlPaint.Light(UI.SoftAccent, 0.15f) : ControlPaint.Dark(UI.SoftAccent, 0.04f);
        saveBtn.MouseEnter += (_, _) => saveBtn.Fill = saveHover;
        saveBtn.MouseLeave += (_, _) => saveBtn.Fill = UI.SoftAccent;
        saveBtn.Click += (_, _) => SaveProject();
        tips.SetToolTip(saveBtn, "이 계산을 기록·주석까지 통째로 저장");
        Controls.Add(memoBack);
        Controls.Add(saveBtn);
        y += memoH + S(8);

        // 화면: 식(작게) · 값(크게) · 한글 단위
        exprLabel = new Label
        {
            AutoSize = false, Size = new Size(W - pad * 2, S(20)), Location = new Point(pad, y), Font = F(9.5f), ForeColor = UI.Sub,
            TextAlign = ContentAlignment.MiddleRight, UseMnemonic = false, AutoEllipsis = true,
        };
        y += exprLabel.Height;
        mainLabel = new Label
        {
            AutoSize = false, Size = new Size(W - pad * 2, S(42)), Location = new Point(pad, y), Font = F(22, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleRight, UseMnemonic = false, AutoEllipsis = true,
        };
        y += mainLabel.Height;
        unitLabel = new Label
        {
            AutoSize = false, Size = new Size(W - pad * 2, S(20)), Location = new Point(pad, y), Font = F(9.5f, FontStyle.Bold), ForeColor = UI.AccentText,
            TextAlign = ContentAlignment.MiddleRight, UseMnemonic = false, AutoEllipsis = true,
        };
        y += unitLabel.Height + S(10);
        foreach (var l in new[] { exprLabel, mainLabel, unitLabel }) l.MouseDown += (_, e) => Drag(e);
        Controls.AddRange(new Control[] { exprLabel, mainLabel, unitLabel });

        // 버튼판
        string[,] keys =
        {
            { "C", "⌫", "%", "÷" },
            { "7", "8", "9", "×" },
            { "4", "5", "6", "−" },
            { "1", "2", "3", "+" },
            { "억", "만", "0", "." },
        };
        for (int r = 0; r < keys.GetLength(0); r++)
        {
            for (int c = 0; c < 4; c++)
                AddKey(keys[r, c], new Rectangle(pad + c * (btnW + gap), y, btnW, btnH));
            y += btnH + gap;
        }
        AddKey("=", new Rectangle(pad, y, W - pad * 2, btnH));
        y += btnH;
        bodyH = y - bodyTop;
        bottom = y + pad;

        // 계산 기록: 계산기 오른쪽 칸 ([전체] / [저장한 것])
        hist = new Panel { Location = new Point(W, bodyTop), Size = new Size(HistW, bodyH), BackColor = UI.Back };
        hist.Paint += (_, e) => { using var pen = new Pen(UI.Line); e.Graphics.DrawLine(pen, 0, 0, 0, hist.Height); };
        hist.MouseDown += (_, e) => Drag(e);
        clearBtn = UI.Button("기록 지우기", "이 계산의 기록을 지워요 (저장한 것은 남아요)", ClearHistory, sc, UI.Sub);
        clearBtn.Font = F(9f);
        tabAll = UI.Button("기록", "", () => ShowTab(0), sc);
        tabSaved = UI.Button("저장한 것", "", () => ShowTab(1), sc);
        tabAll.Font = tabSaved.Font = F(9f, FontStyle.Bold);
        tabAll.Location = new Point(S(10), 0);
        tabSaved.Location = new Point(S(10) + tabAll.PreferredSize.Width + S(4), 0);
        hist.Controls.Add(tabAll);
        hist.Controls.Add(tabSaved);
        listTop = tabAll.PreferredSize.Height + S(6);
        listH = bodyH - listTop - clearBtn.PreferredSize.Height - S(8);
        // 목록이 길면 기본 스크롤바 대신 얇은 막대로 내려 본다. histList는 내용 높이만큼 늘어난다 (FitList)
        int boxW = HistW - S(10) - pad + S(6);
        histList = new Panel { Location = Point.Empty, Size = new Size(boxW - S(12), 1), BackColor = UI.Back };
        histBox = new SlimScroll(histList, sc) { Location = new Point(S(10), listTop), Size = new Size(boxW, listH), FocusOnHover = false };
        hist.Controls.Add(histBox);
        hist.Controls.Add(clearBtn);
        clearBtn.Location = new Point(HistW - pad - clearBtn.PreferredSize.Width + S(6), bodyH - clearBtn.PreferredSize.Height);
        Controls.Add(hist);
        // 스크립트 화면으로 바꾸면 이 계산기 부분은 숨긴다 (제목·닫기·[스크립트] 버튼은 남는다)
        calcParts = Controls.Cast<Control>().Where(c => c != title && c != close && c != scriptBtn).ToList();
        BuildScriptPanel();

        noteTimer.Tick += (_, _) => { noteTimer.Stop(); note = null; if (scriptNoteL is { IsDisposed: false }) scriptNoteL.Text = ""; Render(); };
        SetHistoryOpen(st.CalcHistoryOpen);
        tab = st.CalcTab;
        StyleTabs();
        LoadPreset();
        SetScriptView(st.CalcScriptView);
    }

    /// <summary>글 칸에서 키보드를 빼서 계산기가 받게 한다. ActiveControl = null만으로는 글 칸에 키보드가 남는다</summary>
    void Unfocus() { ActiveControl = null; if (IsHandleCreated) Focus(); }

    /// <summary>
    /// 다른 창(게임)으로 갔다 오면 윈도우가 마지막 글 칸에 키보드를 돌려준다.
    /// 그래서 이름을 쓰다 나갔다 와서 숫자를 치면 이름 칸에 들어갔다. 나갈 때 글 칸을 비워 둔다
    /// </summary>
    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        // 스크립트 칸도 같다: 값을 넣다 게임에 갔다 오면 숫자가 계산기 대신 그 칸에 들어간다
        if (ActiveControl is TextBox) { memoPick = false; ActiveControl = null; st.Save(); }
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        BeginInvoke(() => { if (!memoPick && memo.Focused) Unfocus(); });
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Unfocus(); // 처음엔 메모 칸이 아니라 계산기가 키를 받는다
    }

    void AddKey(string k, Rectangle r)
    {
        bool op = "÷×−+".Contains(k), eq = k == "=", fn = k is "C" or "⌫" or "%", unit = k is "만" or "억";
        Color back = eq ? UI.Accent : op ? UI.SoftAccent : UI.HoverBack;
        Color fore = eq ? Color.White : op || unit ? UI.AccentText : fn ? UI.Sub : UI.Text;
        var b = new RoundLabel
        {
            Text = k, UseMnemonic = false, AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Cursor = Cursors.Hand,
            Font = F(op || eq ? 15 : 13, FontStyle.Bold), Radius = S(8), Bounds = r, Fill = back, ForeColor = fore,
        };
        var hover = UI.Dark ? ControlPaint.Light(back, 0.15f) : ControlPaint.Dark(back, 0.04f);
        b.MouseEnter += (_, _) => b.Fill = hover;
        b.MouseLeave += (_, _) => b.Fill = back;
        b.Click += (_, _) => Press(k);
        Controls.Add(b);
    }

    // ── 프리셋 ──

    string Title(int i)
    {
        var p = st.CalcPresets[i];
        if (p.Memo.Trim().Length > 0) return p.Memo.Trim();
        return p.Name.Length > 0 ? p.Name : $"계산 {i + 1}";
    }

    void UpdatePresetBtn()
    {
        string t = Title(st.CalcPresetIndex) + "  ▾";
        presetBtn.Text = t;
        int x = pad + title.PreferredWidth + S(8);
        int max = scriptBtn.Left - S(6) - x;
        int want = TextRenderer.MeasureText(t, presetBtn.Font).Width + S(16);
        presetBtn.Bounds = new Rectangle(x, S(10), Math.Max(S(40), Math.Min(want, max)), S(28));
    }

    void ShowPresetMenu()
    {
        StoreState();
        var menu = UI.Rounded(new ContextMenuStrip { Font = F(9.5f), ShowImageMargin = false, ShowCheckMargin = true });
        for (int i = 0; i < st.CalcPresets.Count; i++)
        {
            int k = i;
            var it = new ToolStripMenuItem(Title(i).Replace("&", "&&")) { Checked = i == st.CalcPresetIndex };
            it.Click += (_, _) => SwitchPreset(k);
            menu.Items.Add(it);
        }
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("새 계산 시작", null, (_, _) => NewPreset());
        menu.Items.Add("이 계산 지우기", null, (_, _) => DeletePreset());
        menu.Closed += (_, _) => BeginInvoke((Action)menu.Dispose);
        menu.Show(presetBtn, new Point(0, presetBtn.Height + S(2)));
    }

    void SwitchPreset(int i)
    {
        if (i == st.CalcPresetIndex || i < 0 || i >= st.CalcPresets.Count) return;
        StoreState();
        st.CalcPresetIndex = i;
        st.Save();
        LoadPreset();
    }

    /// <summary>하던 계산은 그대로 두고, 빈 프리셋으로 처음부터 계산한다</summary>
    void NewPreset()
    {
        StoreState();
        int n = st.CalcPresets.Count + 1;
        while (st.CalcPresets.Any(p => p.Name == $"계산 {n}")) n++;
        st.CalcPresets.Add(new CalcPreset { Name = $"계산 {n}" });
        st.CalcPresetIndex = st.CalcPresets.Count - 1;
        st.Save();
        LoadPreset();
        Note("새 계산을 시작했어요");
        Render();
    }

    void DeletePreset()
    {
        string name = Title(st.CalcPresetIndex);
        if (!AskBox.Show(this, $"'{name}' 계산을 지울까요?", "계산하던 내용과 기록이 모두 지워져요. 저장한 것은 남아요.", "지우기")) return;
        int i = st.CalcPresetIndex;
        st.CalcPresets.RemoveAt(i);
        if (st.CalcPresets.Count == 0) st.CalcPresets.Add(new CalcPreset { Name = "계산 1" });
        st.CalcPresetIndex = Math.Min(i, st.CalcPresets.Count - 1);
        st.Save();
        LoadPreset();
        Note("계산을 지웠어요");
        Render();
    }

    /// <summary>지금 프리셋의 계산 상태를 화면으로 불러온다</summary>
    void LoadPreset()
    {
        var p = P;
        parts.Clear();
        parts.AddRange(p.Parts.Select(x => (x.Num, string.IsNullOrEmpty(x.Op) ? '+' : x.Op[0])));
        cur = p.Cur ?? "";
        shown = p.Shown;
        justDone = p.JustDone;
        lastExpr = p.LastExpr;
        lastEntry = justDone && p.History.Count > 0 && p.History[0].Expr == lastExpr ? p.History[0] : null;
        note = null;
        loading = true;
        memo.Text = p.Memo ?? "";
        loading = false;
        UpdatePresetBtn();
        BuildHistory();
        Render();
    }

    /// <summary>화면의 계산 상태를 지금 프리셋에 적어 둔다 (파일 저장은 따로)</summary>
    void StoreState()
    {
        var p = P;
        p.Parts = parts.Select(x => new CalcPart { Num = x.num, Op = x.op.ToString() }).ToList();
        p.Cur = cur;
        p.Shown = shown;
        p.JustDone = justDone;
        p.LastExpr = lastExpr;
        p.Memo = memo.Text.Trim();
    }

    // ── 계산 기록 ──

    void ToggleHistory()
    {
        st.CalcHistoryOpen = !st.CalcHistoryOpen;
        st.Save();
        SetHistoryOpen(st.CalcHistoryOpen);
    }

    void SetHistoryOpen(bool on)
    {
        hist.Visible = on;
        int W = baseW + (on ? HistW : 0);
        ClientSize = new Size(W, bottom);
        close.Location = new Point(W - pad - close.PreferredSize.Width + S(6), S(12));
        histBtn.Location = new Point(close.Left - histBtn.PreferredSize.Width - S(2), S(12));
        histBtn.ForeColor = on ? UI.AccentText : UI.Text;
        histBtn.Fill = on ? UI.SoftAccent : Color.Transparent;
        scriptBtn.Location = new Point(histBtn.Left - scriptBtn.PreferredSize.Width - S(2), S(12));
        UpdatePresetBtn();
    }

    void ShowTab(int t)
    {
        if (ActiveControl is TextBox) Unfocus(); // 쓰던 칸을 먼저 닫아 저장한다
        tab = t;
        StyleTabs();
        BuildHistory();
    }

    void StyleTabs()
    {
        st.CalcTab = tab;
        foreach (var (b, on) in new[] { (tabAll, tab == 0), (tabSaved, tab == 1) })
        {
            b.Fill = on ? UI.SoftAccent : Color.Transparent;
            b.ForeColor = on ? UI.AccentText : UI.Sub;
        }
    }

    void BuildHistory()
    {
        histList.SuspendLayout();
        foreach (var c in histList.Controls.Cast<Control>().ToList()) c.Dispose();
        int w = histList.Width;
        if (showSaved) { BuildSaved(w); histList.ResumeLayout(); FitList(); return; }
        if (H.Count == 0) histList.Controls.Add(Empty("아직 기록이 없어요", w));
        int y = 0;
        foreach (var h in H)
        {
            histList.Controls.Add(MakeRow(h, w, y));
            y += S(66) + S(2);
        }
        clearBtn.Visible = H.Count > 0;
        histList.ResumeLayout();
        FitList();
    }

    /// <summary>목록 높이를 내용만큼 맞춘다. 얇은 스크롤 막대는 이 높이를 보고 나타나거나 숨는다</summary>
    void FitList()
    {
        int h = 1;
        foreach (Control c in histList.Controls) if (!c.IsDisposed) h = Math.Max(h, c.Bottom + S(2));
        histList.Height = h;
        histBox.ScrollTo(histBox.Pos); // 줄어들었으면 맨 아래 너머로 가지 않게
    }

    Label Empty(string text, int w) => new()
    {
        Text = text, AutoSize = false, Size = new Size(w, S(40)), Location = new Point(0, S(4)),
        ForeColor = UI.Sub, Font = F(9.5f), TextAlign = ContentAlignment.MiddleCenter,
    };

    /// <summary>기록 한 줄: 주석 · ✎(주석 쓰기) / 식 / 결과. 줄을 누르면 결과를 불러온다</summary>
    RoundLabel MakeRow(CalcEntry h, int w, int y)
    {
        var row = new RoundLabel { AutoSize = false, Size = new Size(w, S(66)), Location = new Point(0, y), Radius = S(8), Cursor = Cursors.Hand };
        var noteL = new Label
        {
            Text = h.Note, AutoSize = false, Size = new Size(w - S(36), S(20)), Location = new Point(S(6), S(3)), Font = F(9f, FontStyle.Bold),
            ForeColor = UI.AccentText, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false, AutoEllipsis = true, BackColor = Color.Transparent, Cursor = Cursors.Hand,
        };
        var pen = new Label
        {
            Text = "✎", AutoSize = false, Size = new Size(S(24), S(22)), Location = new Point(w - S(28), S(2)), Font = F(10.5f),
            ForeColor = UI.Sub, TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.Transparent, Cursor = Cursors.Hand,
        };
        tips.SetToolTip(pen, "주석 달기");
        var ex = new Label
        {
            Text = h.Expr, AutoSize = false, Size = new Size(w - S(12), S(18)), Location = new Point(S(6), S(23)), Font = F(8.5f), ForeColor = UI.Sub,
            TextAlign = ContentAlignment.MiddleRight, UseMnemonic = false, AutoEllipsis = true, BackColor = Color.Transparent, Cursor = Cursors.Hand,
        };
        var rs = new Label
        {
            Text = decimal.TryParse(h.Result, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? Fmt(v) : h.Result,
            AutoSize = false, Size = new Size(w - S(12), S(22)), Location = new Point(S(6), S(41)), Font = F(11f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleRight, UseMnemonic = false, AutoEllipsis = true, BackColor = Color.Transparent, Cursor = Cursors.Hand,
        };
        row.Controls.AddRange(new Control[] { noteL, pen, ex, rs });
        Hover(row, noteL, pen, ex, rs);
        foreach (Control c in new Control[] { row, noteL, ex, rs }) c.Click += (_, _) => LoadEntry(h);
        pen.Click += (_, _) => EditNote(row, noteL, h);
        return row;
    }

    /// <summary>줄 위에 마우스를 올리면 줄 바탕을 칠한다</summary>
    static void Hover(RoundLabel row, params Control[] kids)
    {
        foreach (Control c in kids.Prepend(row))
        {
            c.MouseEnter += (_, _) => row.Fill = UI.HoverBack;
            c.MouseLeave += (_, _) => { if (!row.IsDisposed && !row.ClientRectangle.Contains(row.PointToClient(Cursor.Position))) row.Fill = Color.Transparent; };
        }
    }

    // ── 저장한 것 ──

    /// <summary>저장한 계산 목록: 이름 · ✕ / 기록 수 · 저장한 때. 줄을 누르면 그 계산을 연다</summary>
    void BuildSaved(int w)
    {
        clearBtn.Visible = false;
        if (st.CalcSaved.Count == 0) { histList.Controls.Add(Empty("[저장]을 누르면 여기 남아요", w)); return; }
        int y = 0;
        foreach (var s in st.CalcSaved)
        {
            var row = new RoundLabel { AutoSize = false, Size = new Size(w, S(48)), Location = new Point(0, y), Radius = S(8), Cursor = Cursors.Hand };
            var name = new Label
            {
                Text = SavedName(s), AutoSize = false, Size = new Size(w - S(36), S(22)), Location = new Point(S(6), S(3)), Font = F(9.5f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false, AutoEllipsis = true, BackColor = Color.Transparent, Cursor = Cursors.Hand,
            };
            var info = new Label
            {
                Text = $"기록 {s.History.Count}개 · {s.SavedAt:MM.dd HH:mm}", AutoSize = false, Size = new Size(w - S(12), S(18)), Location = new Point(S(6), S(26)),
                Font = F(8.5f), ForeColor = UI.Sub, TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.Transparent, Cursor = Cursors.Hand,
            };
            var del = new Label
            {
                Text = "✕", AutoSize = false, Size = new Size(S(24), S(22)), Location = new Point(w - S(28), S(3)), Font = F(9.5f),
                ForeColor = UI.Sub, TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.Transparent, Cursor = Cursors.Hand,
            };
            tips.SetToolTip(del, "지우기");
            row.Controls.AddRange(new Control[] { name, info, del });
            Hover(row, name, info, del);
            var saved = s;
            foreach (Control c in new Control[] { row, name, info }) c.Click += (_, _) => OpenSaved(saved);
            del.Click += (_, _) => DeleteSaved(saved);
            histList.Controls.Add(row);
            y += S(48) + S(2);
        }
    }

    static string SavedName(CalcPreset p) => p.Memo.Trim().Length > 0 ? p.Memo.Trim() : p.Name.Length > 0 ? p.Name : "이름 없는 계산";

    /// <summary>설정 파일에 따로 담기도록 계산을 통째로 복사한다</summary>
    /// <summary>저장한 것이 지금 계산과 이름뿐 아니라 내용(기록·식·주석)까지 같은지. 저장 시각은 보지 않는다</summary>
    bool SameAsNow(CalcPreset s)
    {
        var a = Clone(P); var b = Clone(s);
        if (a.Memo.Trim().Length == 0) a.Memo = Title(st.CalcPresetIndex);
        a.SavedAt = b.SavedAt = default;
        return System.Text.Json.JsonSerializer.Serialize(a) == System.Text.Json.JsonSerializer.Serialize(b);
    }

    static CalcPreset Clone(CalcPreset p) =>
        System.Text.Json.JsonSerializer.Deserialize<CalcPreset>(System.Text.Json.JsonSerializer.Serialize(p))!;

    /// <summary>[저장]: 이 계산을 이름·기록·주석까지 통째로 저장한 것에 넣는다. 같은 이름이 있으면 새것으로 바꾼다</summary>
    void SaveProject()
    {
        StoreState();
        var copy = Clone(P);
        copy.SavedAt = DateTime.Now;
        if (copy.Memo.Trim().Length == 0) copy.Memo = Title(st.CalcPresetIndex);
        int i = st.CalcSaved.FindIndex(s => SavedName(s) == SavedName(copy));
        if (i >= 0) st.CalcSaved.RemoveAt(i);
        st.CalcSaved.Insert(0, copy);
        st.Save();
        if (!st.CalcHistoryOpen) ToggleHistory();
        Note(i >= 0 ? "새로 저장했어요" : "저장했어요");
        Render();
        if (showSaved) BuildHistory();
    }

    /// <summary>
    /// 저장한 계산을 연다. 지금 계산에 기록이 있으면 저장하고 열지, 지우고 열지 먼저 묻는다.
    /// 기록이 없으면 비어 있을 땐 그 자리에, 아니면 새 계산으로 연다
    /// </summary>
    void OpenSaved(CalcPreset s)
    {
        StoreState();
        bool replace;
        // 여는 것과 같거나, 지금 계산이 바뀐 것 없이 이미 저장돼 있으면 잃을 게 없으니 묻지 않고 연다
        if (P.History.Count > 0 && (SameAsNow(s) || st.CalcSaved.Any(SameAsNow))) replace = true;
        else if (P.History.Count > 0)
        {
            int c = AskBox.Choose(this, $"'{SavedName(s)}'을(를) 불러올까요?",
                $"지금 하던 '{Title(st.CalcPresetIndex)}' 계산은 어떻게 할까요?", false,
                "저장하고 불러오기", "지우고 불러오기", "취소");
            if (c < 0) return;
            if (c == 0)
            {
                string name = SavedName(s);
                SaveProject();
                // 같은 이름이면 방금 저장한 것으로 바뀌었으니 새것을 연다
                if (!st.CalcSaved.Contains(s)) s = st.CalcSaved.FirstOrDefault(x => SavedName(x) == name) ?? s;
            }
            replace = true;
        }
        else replace = P.Parts.Count == 0 && P.Cur.Length == 0 && P.Memo.Trim().Length == 0;
        var copy = Clone(s);
        copy.SavedAt = default;
        if (replace) st.CalcPresets[st.CalcPresetIndex] = copy;
        else { st.CalcPresets.Add(copy); st.CalcPresetIndex = st.CalcPresets.Count - 1; }
        st.Save();
        showSaved = false;
        StyleTabs();
        LoadPreset();
        Note("불러왔어요");
        Render();
    }

    void DeleteSaved(CalcPreset s)
    {
        if (!AskBox.Show(this, $"저장한 '{SavedName(s)}'을(를) 지울까요?", "지우면 되돌릴 수 없어요.", "지우기")) return;
        st.CalcSaved.Remove(s);
        st.Save();
        BeginInvoke((Action)BuildHistory);
    }

    /// <summary>기록 줄의 메모를 그 자리에서 고친다 (Enter 저장, Esc 취소)</summary>
    void EditNote(RoundLabel row, Label noteL, CalcEntry h)
    {
        // 위 메모 칸과 같은 둥근 칸 안에 테두리 없는 글 칸을 넣는다.
        // 바탕이 Label이라 글 칸이 한글 입력 끔을 물려받으므로 ImeMode를 따로 정한다
        var box = new RoundLabel { AutoSize = false, Radius = S(6), Fill = UI.HoverBack, Cursor = Cursors.IBeam,
            Bounds = new Rectangle(noteL.Left, noteL.Top, noteL.Width, S(24)) };
        var tb = new TextBox
        {
            BorderStyle = BorderStyle.None, BackColor = UI.HoverBack, ForeColor = UI.Text, Font = F(9f), MaxLength = 60,
            Text = h.Note, PlaceholderText = "주석 쓰기", ImeMode = ImeMode.NoControl,
        };
        tb.Width = box.Width - S(12);
        tb.Location = new Point(S(6), (box.Height - tb.Height) / 2);
        box.Controls.Add(tb);
        box.Click += (_, _) => tb.Focus();
        noteL.Visible = false;
        row.Controls.Add(box);
        box.BringToFront();
        tb.Focus();
        tb.SelectAll();
        bool done = false;
        void Finish(bool keep)
        {
            if (done) return;
            done = true;
            if (keep) { h.Note = tb.Text.Trim(); st.Save(); }
            BeginInvoke((Action)BuildHistory);
        }
        tb.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Finish(true); Unfocus(); }
            else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; Finish(false); Unfocus(); }
        };
        tb.Leave += (_, _) => Finish(true);
    }

    /// <summary>기록 줄을 누르면 그 결과(와 메모)를 다시 불러와 이어서 계산한다</summary>
    void LoadEntry(CalcEntry h)
    {
        if (!decimal.TryParse(h.Result, NumberStyles.Number, CultureInfo.InvariantCulture, out var v)) return;
        parts.Clear();
        cur = Plain(v);
        shown = v;
        lastExpr = h.Expr.Length > 0 ? h.Expr : null;
        justDone = true;
        lastEntry = h;
        Render();
    }

    void AddHistory(string expr, decimal v)
    {
        lastEntry = new CalcEntry { Expr = expr, Result = Plain(v) }; // 주석은 사용자가 ✎로 단다
        H.Insert(0, lastEntry);
        Trim();
        StoreState();
        st.Save();
        BuildHistory();
    }

    /// <summary>계산 하나에 기록은 최근 MaxHistory개만 남긴다</summary>
    void Trim()
    {
        if (H.Count > MaxHistory) H.RemoveRange(MaxHistory, H.Count - MaxHistory);
    }

    void ClearHistory()
    {
        if (!AskBox.Show(this, $"'{Title(st.CalcPresetIndex)}'의 기록을 지울까요?", "저장한 것은 그대로 남아요.", "지우기")) return;
        H.Clear();
        lastEntry = null;
        st.Save();
        BuildHistory();
    }

    // ── 계산 ──

    void Press(string k)
    {
        if (ActiveControl is TextBox) Unfocus(); // 버튼을 누르면 메모 칸에서 나온다
        try
        {
            switch (k)
            {
                case "C": parts.Clear(); cur = ""; shown = 0; justDone = false; break;
                case "⌫": Back(); break;
                case "%": SetCur(Cur() / 100); break;
                case "만": SetCur((cur.Length == 0 ? 1 : Cur()) * 10_000m); break;
                case "억": SetCur((cur.Length == 0 ? 1 : Cur()) * 100_000_000m); break;
                case ".":
                    if (justDone) { parts.Clear(); cur = ""; justDone = false; }
                    if (!cur.Contains('.')) cur = (cur.Length == 0 ? "0" : cur) + ".";
                    break;
                case "=": Calc(); break;
                case "÷" or "×" or "−" or "+": Op(k[0]); break;
                default:
                    if (justDone) { parts.Clear(); cur = ""; justDone = false; }
                    if (cur.Count(char.IsDigit) >= 20) return; // 너무 긴 수는 더 받지 않는다
                    cur = cur == "0" ? k : cur + k;
                    break;
            }
            if (cur.Length > 0) shown = Cur();
        }
        catch (OverflowException) { Reset("수가 너무 커요"); }
        catch (DivideByZeroException) { Reset("0으로 나눌 수 없어요"); }
        Render();
    }

    decimal Cur() => decimal.Parse(cur.Length == 0 ? "0" : cur.TrimEnd('.'), NumberStyles.Number, CultureInfo.InvariantCulture);

    void SetCur(decimal v)
    {
        cur = Plain(v);
        justDone = false;
    }

    void Back()
    {
        if (justDone) { justDone = false; parts.Clear(); return; } // 결과는 그대로 두고 식만 지운다
        if (cur.Length > 0)
        {
            cur = cur[..^1]; if (cur == "-") cur = "";
            if (cur.Length == 0 && parts.Count == 0) shown = 0; // 마지막 글자를 지우면 0으로 돌아간다
            return;
        }
        // 넣던 수가 없으면 마지막 연산자를 지우고 그 앞 수를 다시 고친다
        if (parts.Count > 0) { var (n, _) = parts[^1]; parts.RemoveAt(parts.Count - 1); cur = Plain(n); return; }
        shown = 0; // 지울 게 더 없으면 화면에 남은 수도 0으로
    }

    /// <summary>Esc: 지금 계산을 지운다. 4번 넘게 이어 계산하던 중이면 먼저 묻는다 (Enter = 예)</summary>
    void EscClear()
    {
        if (!justDone && parts.Count >= 4 &&
            !AskBox.Show(this, "지금 계산을 초기화할까요?", "엔터를 눌러도 초기화돼요.", "초기화", enterYes: true))
            return;
        Press("C");
    }

    void Op(char op)
    {
        justDone = false;
        if (cur.Length == 0)
        {
            if (parts.Count > 0) { parts[^1] = (parts[^1].num, op); return; } // 연산자만 바꾼다
            parts.Add((shown, op));                                           // 앞 결과에 이어서 계산
            return;
        }
        parts.Add((Cur(), op));
        cur = "";
    }

    void Calc()
    {
        if (parts.Count == 0) { justDone = true; lastExpr = null; return; }
        var all = new List<(decimal num, char op)>(parts);
        if (cur.Length > 0) all.Add((Cur(), '='));
        else all[^1] = (all[^1].num, '='); // 끝에 남은 연산자는 버린다
        string expr = string.Join(" ", all.Select(p => Fmt(p.num) + " " + p.op));
        decimal v = Eval(all);
        lastExpr = expr;
        parts.Clear();
        cur = Plain(v);
        shown = v;
        justDone = true;
        AddHistory(expr, v);
    }

    /// <summary>곱셈·나눗셈을 먼저, 그다음 덧셈·뺄셈</summary>
    static decimal Eval(List<(decimal num, char op)> list)
    {
        var terms = new List<decimal>();
        var signs = new List<char>();
        decimal t = list[0].num;
        for (int i = 0; i < list.Count; i++)
        {
            char op = list[i].op;
            if (op is '×' or '÷')
            {
                decimal n = list[i + 1].num;
                t = op == '×' ? t * n : t / n;
                list[i + 1] = (t, list[i + 1].op);
                continue;
            }
            terms.Add(t);
            if (op == '=') break;
            signs.Add(op);
            t = list[i + 1].num;
        }
        decimal sum = terms[0];
        for (int i = 0; i < signs.Count; i++) sum = signs[i] == '+' ? sum + terms[i + 1] : sum - terms[i + 1];
        return Math.Round(sum, 10);
    }

    void Reset(string msg)
    {
        parts.Clear(); cur = ""; shown = 0; justDone = true; lastExpr = null; lastEntry = null;
        Note(msg);
    }

    // ── 보여 주기 ──

    /// <summary>계산에 쓰는 글자 (쉼표 없음)</summary>
    static string Plain(decimal v) => Math.Round(v, 10).ToString("0.##########", CultureInfo.InvariantCulture);

    /// <summary>보여 줄 글자 (세 자리마다 쉼표)</summary>
    static string Fmt(decimal v) => Math.Round(v, 10).ToString("#,0.##########", CultureInfo.InvariantCulture);

    /// <summary>넣고 있는 수는 친 그대로(끝의 점·0 포함) 쉼표만 넣는다</summary>
    static string FmtTyping(string s)
    {
        bool neg = s.StartsWith('-');
        if (neg) s = s[1..];
        int dot = s.IndexOf('.');
        string ip = dot < 0 ? s : s[..dot], fp = dot < 0 ? "" : s[dot..];
        if (ip.Length == 0) ip = "0";
        string withComma = decimal.TryParse(ip, NumberStyles.None, CultureInfo.InvariantCulture, out var n)
            ? n.ToString("#,0", CultureInfo.InvariantCulture) : ip;
        return (neg ? "-" : "") + withComma + fp;
    }

    /// <summary>1만 이상이면 "1억 2,345만 6,789"처럼 읽어 준다</summary>
    static string Korean(decimal v)
    {
        decimal a = Math.Abs(Math.Truncate(v));
        if (a < 10_000) return "";
        var units = new (decimal size, string name)[] { (1_0000_0000_0000_0000m, "경"), (1_0000_0000_0000m, "조"), (1_0000_0000m, "억"), (1_0000m, "만") };
        var sb = new List<string>();
        foreach (var (size, name) in units)
        {
            decimal q = Math.Truncate(a / size);
            if (q > 0) { sb.Add(q.ToString("#,0", CultureInfo.InvariantCulture) + name); a -= q * size; }
        }
        if (a > 0) sb.Add(a.ToString("#,0", CultureInfo.InvariantCulture));
        return (v < 0 ? "-" : "") + string.Join(" ", sb);
    }

    void Render()
    {
        string expr = string.Join(" ", parts.Select(p => Fmt(p.num) + " " + p.op));
        if (justDone && lastExpr != null) expr = lastExpr;
        exprLabel.Text = note ?? expr;
        exprLabel.ForeColor = note != null ? UI.AccentText : UI.Sub;
        mainLabel.Text = cur.Length > 0 && !justDone ? FmtTyping(cur) : Fmt(shown);
        unitLabel.Text = Korean(shown);
        StoreState();
    }

    void Note(string msg)
    {
        note = msg;
        if (scriptNoteL is { IsDisposed: false }) scriptNoteL.Text = scriptView ? msg : "";
        noteTimer.Stop(); noteTimer.Start();
    }

    /// <summary>Ctrl+C로 지금 결과를 복사한다 (결과를 눌러서는 복사하지 않는다)</summary>
    void Copy()
    {
        try { Clipboard.SetText(Plain(shown)); Note("복사했어요"); }
        catch (Exception ex) { Log.Write("calc", "결과를 복사하지 못함", ex); Note("복사하지 못했어요"); }
        Render();
    }

    // ── 키보드 ──

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // 글 칸에 쓰는 중이면 키는 글 칸 몫이다. 메모 칸에서 Enter는 계산, Esc는 칸에서 나오기
        if (ActiveControl is TextBox tb)
        {
            if (tb == memo && keyData == Keys.Enter) { if (parts.Count > 0) Press("="); else Unfocus(); return true; }
            if (tb == memo && keyData == Keys.Escape) { Unfocus(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        if (scriptView) // 스크립트 화면: Esc는 계산기로, Ctrl+C는 결과 복사. 계산기 키는 받지 않는다
        {
            if (keyData == Keys.Escape) { SetScriptView(false); return true; }
            if (keyData == (Keys.Control | Keys.C)) { CopyScript(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        switch (keyData)
        {
            case Keys.Escape: EscClear(); return true; // 창을 닫지 않고 지금 계산만 지운다
            case Keys.Enter: Press("="); return true;
            case Keys.Back: Press("⌫"); return true;
            case Keys.Delete: Press("C"); return true;
            case Keys.Control | Keys.C: Copy(); return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        if (ActiveControl is TextBox || scriptView) { base.OnKeyPress(e); return; }
        string? k = e.KeyChar switch
        {
            >= '0' and <= '9' => e.KeyChar.ToString(),
            '.' => ".",
            '+' => "+",
            '-' => "−",
            '*' or 'x' or 'X' => "×",
            '/' => "÷",
            '%' => "%",
            '=' => "=",
            _ => null,
        };
        if (k != null) { Press(k); e.Handled = true; }
        base.OnKeyPress(e);
    }

    // ── 창 ──

    void Drag(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        if (ActiveControl is TextBox) Unfocus(); // 빈 곳이나 결과 화면을 누르면 글 칸에서 나온다
        DragWindow(Handle);
        WinPlace.Remember(st, PlaceKey, this);
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
        StoreState();
        WinPlace.Remember(st, PlaceKey, this); // 창 자리와 함께 계산하던 내용도 저장된다
        st.Save();
        noteTimer.Dispose();
        tips.Dispose();
        base.OnFormClosed(e);
    }
}
