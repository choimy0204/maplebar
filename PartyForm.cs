using System.Globalization;
using System.Runtime.InteropServices;
using static MapleBar.Native;

namespace MapleBar;

/// <summary>
/// 파티 구성 창 (바의 "파티 구성" 메뉴). 항상 위에 뜨는 작은 창 하나에서
/// 파티 창설 · 파티 입장 · 파티 상태(인원 목록) · 설정 페이지를 바꿔 가며 보인다.
/// </summary>
sealed class PartyForm : Form
{
    /// <summary>메뉴에서 쓰는 주소 (사이트 주소가 아니다). Path는 메뉴 글자를 눌렀을 때 (파티 중이면 상태, 아니면 창설)</summary>
    public const string Path = "maplebar:party";
    public const string CreatePath = "maplebar:party-create";
    public const string JoinPath = "maplebar:party-join";
    public const string SettingsPath = "maplebar:party-settings";
    const string PlaceKey = "party";

    public static bool IsPath(string path) => path.StartsWith(Path, StringComparison.Ordinal);

    enum Page { Create, Join, Status, Settings }

    static PartyForm? open;
    static Action<string>? endedHandler;

    /// <summary>바가 켜질 때: 파티가 끝나면(해체·연결 포기) 창을 열어 알린다 (바를 다시 만들면 새 바로 바꾼다)</summary>
    public static void Watch(Form owner, Settings st, float sc)
    {
        if (endedHandler != null) PartyLink.Ended -= endedHandler;
        endedHandler = why =>
        {
            if (owner.IsDisposed) return;
            Open(owner, st, sc, Path, why == "disband" ? "파티가 해체됐어요" : "연결이 끊겨서 파티에서 나왔어요");
        };
        PartyLink.Ended += endedHandler;
    }

    public static void Open(Form owner, Settings st, float sc, string path = Path, string? notice = null)
    {
        var want = path == SettingsPath ? Page.Settings : path == JoinPath ? Page.Join : Page.Create;
        bool fresh = open == null || open.IsDisposed;
        if (fresh) open = new PartyForm(st, sc) { Owner = owner };
        open!.notice = notice;
        open.ShowPage(want);
        if (fresh) open.Location = WinPlace.Where(st, PlaceKey, open, owner, sc);
        if (!open.Visible) open.Show();
        if (notice == null) open.Activate();
    }

    readonly Settings st;
    readonly float sc;
    readonly List<Font> fonts = new();
    readonly Label title;
    readonly RoundLabel close;
    readonly Panel body;
    readonly System.Windows.Forms.Timer saveTimer = new() { Interval = 600 };
    readonly int pad, headH, W;
    Page page;
    string? notice;           // 처음 화면 위에 한 번 보이는 알림 글
    int y;                    // 지금 만드는 페이지에서 다음 줄의 높이
    bool helloDirty;          // 직업 줄임말이 바뀌어 서버에 다시 알려야 한다
    bool busy;                // 서버 답을 기다리는 중
    int capturing;            // 설정 페이지에서 키를 받는 중인 칸 (1~4, 0이면 없음)
    readonly RoundLabel?[] keyPills = new RoundLabel?[4];
    Action? enterAction;      // 엔터를 누르면 할 일

    int S(float v) => UI.Scale(v, sc);
    Font F(float size, FontStyle style = FontStyle.Regular)
    {
        var f = new Font("맑은 고딕", size * UI.Z, style);
        fonts.Add(f);
        return f;
    }

    PartyForm(Settings st, float sc)
    {
        this.st = st; this.sc = sc;
        st.Party ??= new();

        Text = "파티";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = UI.Back;
        ForeColor = UI.Text;
        Font = F(10);

        pad = S(16); headH = S(48); W = S(330);
        ClientSize = new Size(W, headH + S(100));

        title = new Label { Text = "파티", Font = F(12, FontStyle.Bold), ForeColor = UI.Text, AutoSize = false, UseMnemonic = false };
        Controls.Add(title);
        title.MouseDown += (_, e) => Drag(e);
        MouseDown += (_, e) => Drag(e);
        close = UI.Button("✕", "닫기", Close, sc);
        close.Font = F(10);
        Controls.Add(close);
        close.Location = new Point(W - pad - close.PreferredSize.Width + S(4), (headH - close.PreferredSize.Height) / 2 + S(2));

        body = new Panel { Location = new Point(0, headH), Width = W, BackColor = UI.Back };
        Controls.Add(body);
        body.MouseDown += (_, e) => Drag(e);

        saveTimer.Tick += (_, _) => FlushSave();
        PartyLink.Changed += OnLinkChanged;
        Paint += (_, e) => { using var pen = new Pen(UI.Line); e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1); };
    }

    // ── 창 ──

    void Drag(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
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
        PartyLink.Changed -= OnLinkChanged;
        FlushSave();
        WinPlace.Remember(st, PlaceKey, this);
        saveTimer.Dispose();
        if (open == this) open = null;
        base.OnFormClosed(e);
        foreach (var f in fonts) f.Dispose();
        fonts.Clear();
    }

    /// <summary>파티 인원·코드가 바뀌면 보고 있는 페이지를 다시 그린다 (설정 페이지는 그대로)</summary>
    void OnLinkChanged()
    {
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke(() =>
        {
            if (IsDisposed || page == Page.Settings) return;
            ShowPage(page);
        });
    }

    void FlushSave()
    {
        if (!saveTimer.Enabled && !helloDirty) return;
        saveTimer.Stop();
        st.Save();
        if (helloDirty) { helloDirty = false; PartyLink.SendHello(); }
    }

    // ── 페이지 ──

    void ShowPage(Page want)
    {
        FlushSave();
        // 파티에 들어가 있으면 창설·입장 대신 상태를, 아니면 상태 대신 창설을 보인다
        if (PartyLink.Active && want is Page.Create or Page.Join) want = Page.Status;
        if (!PartyLink.Active && want == Page.Status) want = Page.Create;
        page = want;
        capturing = 0;
        enterAction = null;
        busy = false;
        foreach (var c in body.Controls.Cast<Control>().ToList()) { body.Controls.Remove(c); c.Dispose(); }
        Array.Clear(keyPills);
        y = S(4);

        switch (page)
        {
            case Page.Create: BuildCreate(); break;
            case Page.Join: BuildJoin(); break;
            case Page.Status: BuildStatus(); break;
            default: BuildSettings(); break;
        }
        notice = null;

        body.Height = y + pad;
        ClientSize = new Size(W, headH + body.Height);
        title.Text = page switch { Page.Create => "파티 창설", Page.Join => "파티 입장", Page.Status => "파티", _ => "파티 설정" };
        var tm = TextRenderer.MeasureText(title.Text, title.Font);
        title.Bounds = new Rectangle(pad + S(2), (headH - tm.Height) / 2 + S(2), tm.Width + S(4), tm.Height);
        Invalidate();
    }

    // ── 그리기 도우미 ──

    int FieldW => W - pad * 2;

    /// <summary>줄바꿈되는 글을 지금 높이에 놓고 그만큼 내려간다</summary>
    Label Wrapped(string text, float size, Color color, FontStyle style = FontStyle.Regular, int gap = 6)
    {
        var l = new Label { Text = text, Font = F(size, style), ForeColor = color, AutoSize = false, UseMnemonic = false };
        body.Controls.Add(l);
        var m = TextRenderer.MeasureText(l.Text, l.Font, new Size(FieldW, int.MaxValue), TextFormatFlags.WordBreak);
        l.Bounds = new Rectangle(pad, y, FieldW, m.Height + S(2));
        y += l.Height + S(gap);
        return l;
    }

    /// <summary>글자 크기만큼만 차지하는 한 줄 글. 세로는 높이 h 안의 가운데</summary>
    Label Inline(string text, int x, int top, int h, float size, Color color, FontStyle style = FontStyle.Regular)
    {
        var l = new Label { Text = text, Font = F(size, style), ForeColor = color, AutoSize = false, UseMnemonic = false };
        body.Controls.Add(l);
        var m = TextRenderer.MeasureText(l.Text, l.Font);
        l.Bounds = new Rectangle(x, top + (h - m.Height) / 2, m.Width + S(4), m.Height);
        return l;
    }

    /// <summary>둥근 칸 안에 테두리 없는 글 칸을 넣는다</summary>
    TextBox Field(string hint, bool secret, int max, int x, int top, int w)
    {
        var back = new RoundLabel { AutoSize = false, Radius = S(6), Fill = UI.HoverBack, Cursor = Cursors.IBeam, Bounds = new Rectangle(x, top, w, S(34)) };
        var tb = new TextBox
        {
            BorderStyle = BorderStyle.None, BackColor = UI.HoverBack, ForeColor = UI.Text, Font = F(10), MaxLength = max,
            PlaceholderText = hint, ImeMode = ImeMode.NoControl, UseSystemPasswordChar = secret,
        };
        body.Controls.Add(back);
        back.Controls.Add(tb);
        tb.Location = new Point(S(10), (back.Height - tb.PreferredHeight) / 2);
        tb.Width = w - S(20);
        back.Click += (_, _) => tb.Focus();
        return tb;
    }

    RoundLabel Btn(string text, bool primary, int x, int w, Action click)
    {
        Color back = primary ? P.Fill : UI.HoverBack;
        var b = new RoundLabel
        {
            AutoSize = false, Bounds = new Rectangle(x, y, w, S(36)), Radius = S(8), Text = text, Fill = back,
            ForeColor = primary ? P.OnFill : UI.Text, TextAlign = ContentAlignment.MiddleCenter,
            Font = F(10, FontStyle.Bold), Cursor = Cursors.Hand, UseMnemonic = false,
        };
        b.MouseEnter += (_, _) => b.Fill = ControlPaint.Dark(back, 0.05f);
        b.MouseLeave += (_, _) => b.Fill = back;
        b.Click += (_, _) => click();
        body.Controls.Add(b);
        return b;
    }

    Label ErrLabel()
    {
        var l = new Label { Text = "", Font = F(9), ForeColor = P.Red, AutoSize = false, UseMnemonic = false, Bounds = new Rectangle(pad, y, FieldW, S(20)) };
        body.Controls.Add(l);
        y += S(24);
        return l;
    }

    void AddNotice()
    {
        if (notice == null) return;
        Wrapped(notice, 10, P.Red, FontStyle.Bold, 10);
    }

    // ── 창설 ──

    void BuildCreate()
    {
        AddNotice();
        Wrapped("비밀번호를 정해 주세요. 4자에서 20자까지예요.", 9.5f, UI.Sub, FontStyle.Regular, 10);
        var pw = Field("비밀번호", false, PartyLink.PwMax, pad, y, FieldW);
        y += S(42);
        var err = ErrLabel();
        var go = Btn("만들기", true, pad, FieldW, () => { });
        async void Make()
        {
            if (busy) return;
            if (pw.Text.Length < PartyLink.PwMin) { err.Text = $"비밀번호는 {PartyLink.PwMin}자 이상 넣어 주세요"; pw.Focus(); return; }
            busy = true; err.Text = ""; go.Text = "만드는 중…";
            var e = await PartyLink.CreateAsync(pw.Text);
            if (IsDisposed) return;
            if (e == null) { ShowPage(Page.Status); return; }
            busy = false; go.Text = "만들기"; err.Text = e;
        }
        go.Click += (_, _) => Make();
        enterAction = Make;
        y += S(36);
    }

    // ── 입장 ──

    void BuildJoin()
    {
        AddNotice();
        Wrapped("방장에게 받은 입장 코드와 비밀번호를 넣어 주세요.", 9.5f, UI.Sub, FontStyle.Regular, 10);
        var code = Field("입장 코드", false, 6, pad, y, FieldW);
        code.CharacterCasing = CharacterCasing.Upper;
        y += S(42);
        var pw = Field("비밀번호", true, PartyLink.PwMax, pad, y, FieldW);
        y += S(42);
        var err = ErrLabel();
        var go = Btn("들어가기", true, pad, FieldW, () => { });
        async void Enter()
        {
            if (busy) return;
            if (code.Text.Trim().Length == 0) { err.Text = "입장 코드를 넣어 주세요"; code.Focus(); return; }
            if (pw.Text.Length < PartyLink.PwMin) { err.Text = $"비밀번호는 {PartyLink.PwMin}자 이상 넣어 주세요"; pw.Focus(); return; }
            busy = true; err.Text = ""; go.Text = "들어가는 중…";
            var e = await PartyLink.JoinAsync(code.Text, pw.Text);
            if (IsDisposed) return;
            if (e == null) { ShowPage(Page.Status); return; }
            busy = false; go.Text = "들어가기"; err.Text = e;
        }
        go.Click += (_, _) => Enter();
        enterAction = Enter;
        y += S(36);
    }

    // ── 파티 상태 ──

    void BuildStatus()
    {
        bool owner = PartyLink.IsOwner;
        string? code = PartyLink.Code;
        bool recruiting = owner && code != null;

        string head = recruiting ? "파티원 모집 중" : owner ? "모집을 마쳤어요" : "파티에 들어와 있어요";
        Wrapped(head, 12, recruiting ? UI.AccentText : UI.Text, FontStyle.Bold, 8);

        if (recruiting)
        {
            var box = new RoundLabel
            {
                AutoSize = false, Bounds = new Rectangle(pad, y, FieldW, S(60)), Radius = S(10), Fill = P.Fill, ForeColor = P.OnFill,
                TextAlign = ContentAlignment.MiddleCenter, Font = F(24, FontStyle.Bold), Text = code, Cursor = Cursors.Hand, UseMnemonic = false,
            };
            body.Controls.Add(box);
            y += box.Height + S(4);
            var hint = Wrapped("누르면 복사돼요", 9, UI.Sub, FontStyle.Regular, 10);
            box.Click += (_, _) =>
            {
                try { Clipboard.SetText(code!); hint.Text = "복사했어요"; }
                catch (ExternalException) { hint.Text = "복사가 안 됐어요"; }
            };
        }

        var members = PartyHub.Members;
        Wrapped($"파티원 {members.Count} / 6", 9.5f, UI.Sub, FontStyle.Regular, 4);
        if (members.Count == 0)
        {
            Row("연결하는 중…", UI.Sub);
        }
        foreach (var m in members)
        {
            string text = string.IsNullOrWhiteSpace(m.Job) ? "이름 없음" : m.Job;
            if (m.Owner) text += "  · 방장";
            if (m.Id == PartyHub.MyId) text += "  · 나";
            Row(text, UI.Text);
        }
        y += S(6);

        if (recruiting)
        {
            int gap = S(10), bw = (FieldW - gap) / 2;
            Btn("완료", true, pad, bw, () => { if (FirstClick()) PartyLink.CloseRecruit(); });
            Btn("해체", false, pad + bw + gap, bw, Disband);
        }
        else if (owner) Btn("해체", false, pad, FieldW, Disband);
        else Btn("나가기", false, pad, FieldW, () => { if (FirstClick()) PartyLink.Leave(); });
        y += S(36);
    }

    long lastRequestAt;

    /// <summary>같은 요청 버튼을 연달아 눌러도 서버에는 한 번만 보낸다 (1.5초 안의 두 번째 누름은 건너뛴다)</summary>
    bool FirstClick()
    {
        long now = Environment.TickCount64;
        if (now - lastRequestAt < 1500) return false;
        lastRequestAt = now;
        return true;
    }

    void Row(string text, Color color)
    {
        var r = new RoundLabel
        {
            AutoSize = false, Bounds = new Rectangle(pad, y, FieldW, S(30)), Radius = S(8), Fill = UI.HoverBack, ForeColor = color,
            TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(S(10), 0, S(10), 0), Font = F(10), Text = text, UseMnemonic = false,
        };
        body.Controls.Add(r);
        y += r.Height + S(4);
    }

    void Disband()
    {
        if (!AskBox.Show(this, "파티를 해체할까요?", "모든 파티원이 파티에서 나가게 돼요.", "해체")) return;
        if (FirstClick()) PartyLink.Disband();
    }

    // ── 설정 ──

    PartyBossSettings Cfg => st.Party;

    int GetKey(int i) => i switch { 0 => Cfg.OriginKey, 1 => Cfg.BurstKey, 2 => Cfg.SemiKey, _ => Cfg.AscentKey };
    void SetKey(int i, int vk)
    {
        switch (i)
        {
            case 0: Cfg.OriginKey = vk; break;
            case 1: Cfg.BurstKey = vk; break;
            case 2: Cfg.SemiKey = vk; break;
            default: Cfg.AscentKey = vk; break;
        }
    }

    RoundLabel Pill(int top, int w, Action click)
    {
        var pill = new RoundLabel
        {
            AutoSize = false, Size = new Size(w, S(30)), TextAlign = ContentAlignment.MiddleCenter, Fill = UI.HoverBack,
            Cursor = Cursors.Hand, Font = F(10, FontStyle.Bold), Radius = S(8), UseMnemonic = false,
        };
        pill.Location = new Point(W - pad - w, top);
        pill.Click += (_, _) => click();
        body.Controls.Add(pill);
        return pill;
    }

    void SectionTitle(string text)
    {
        Wrapped(text, 10, UI.Text, FontStyle.Bold, 4);
    }

    void BuildSettings()
    {
        int rowH = S(38), pillW = S(150);

        SectionTitle("내 직업");
        Inline("직업 줄임말", pad, y, S(30), 10, UI.Sub);
        var jobBack = new RoundLabel { AutoSize = false, Radius = S(8), Fill = UI.HoverBack, Cursor = Cursors.IBeam, Bounds = new Rectangle(W - pad - pillW, y, pillW, S(30)) };
        var job = new TextBox
        {
            BorderStyle = BorderStyle.None, BackColor = UI.HoverBack, ForeColor = UI.Text, Font = F(10), MaxLength = 4,
            ImeMode = ImeMode.NoControl, TextAlign = HorizontalAlignment.Center, Text = Cfg.Job,
        };
        body.Controls.Add(jobBack);
        jobBack.Controls.Add(job);
        job.Location = new Point(S(10), (jobBack.Height - job.PreferredHeight) / 2);
        job.Width = pillW - S(20);
        jobBack.Click += (_, _) => job.Focus();
        job.TextChanged += (_, _) =>
        {
            Cfg.Job = job.Text.Trim();
            helloDirty = true;
            saveTimer.Stop(); saveTimer.Start();
        };
        y += rowH + S(4);

        SectionTitle("키");
        string[] keyNames = { "오리진", "극딜 시퀀스", "준극딜 시퀀스", "어센트" };
        for (int i = 0; i < 4; i++)
        {
            int idx = i;
            Inline(keyNames[i], pad, y, S(30), 10, UI.Sub);
            keyPills[i] = Pill(y, pillW, () => { capturing = capturing == idx + 1 ? 0 : idx + 1; ActiveControl = null; UpdateKeys(); });
            y += rowH;
        }
        UpdateKeys();
        y += S(4);

        SectionTitle("쿨타임 줄이기");
        Inline("초 감소", pad, y, S(30), 10, UI.Sub);
        RoundLabel secPill = null!;
        secPill = Pill(y, pillW, () => PickSec(secPill));
        secPill.Text = SecText(Cfg.SecCut);
        y += rowH;

        Inline("% 감소", pad, y, S(30), 10, UI.Sub);
        int pctW = S(80);
        var pctBack = new RoundLabel { AutoSize = false, Radius = S(8), Fill = UI.HoverBack, Cursor = Cursors.IBeam, Bounds = new Rectangle(W - pad - pctW - S(22), y, pctW, S(30)) };
        var pct = new TextBox
        {
            BorderStyle = BorderStyle.None, BackColor = UI.HoverBack, ForeColor = UI.Text, Font = F(10), MaxLength = 4,
            ImeMode = ImeMode.NoControl, TextAlign = HorizontalAlignment.Center, Text = PctText(Cfg.PctCut),
        };
        body.Controls.Add(pctBack);
        pctBack.Controls.Add(pct);
        pct.Location = new Point(S(8), (pctBack.Height - pct.PreferredHeight) / 2);
        pct.Width = pctW - S(16);
        pctBack.Click += (_, _) => pct.Focus();
        Inline("%", W - pad - S(16), y, S(30), 10, UI.Sub);
        pct.KeyPress += (_, e) => { if (!char.IsDigit(e.KeyChar) && e.KeyChar != '.' && e.KeyChar != ',' && !char.IsControl(e.KeyChar)) e.Handled = true; };
        pct.TextChanged += (_, _) =>
        {
            if (!TryPct(pct.Text, out double v)) return;
            Cfg.PctCut = v;
            saveTimer.Stop(); saveTimer.Start();
        };
        pct.Leave += (_, _) =>
        {
            if (!TryPct(pct.Text, out double v)) v = 0;
            Cfg.PctCut = v;
            pct.Text = PctText(v);
            saveTimer.Stop(); saveTimer.Start();
        };
        y += rowH + S(4);

        SectionTitle("알림");
        AddToggle("극딜 알림", () => Cfg.AlertBurst, v => Cfg.AlertBurst = v);
        AddToggle("오리진 알림", () => Cfg.AlertOrigin, v => Cfg.AlertOrigin = v);
        AddToggle("준극 알림", () => Cfg.AlertSemi, v => Cfg.AlertSemi = v);
        y += S(4);

        Wrapped("오리진 6분 · 극딜 2분 · 준극 1분 · 어센트 3회", 9, UI.Sub, FontStyle.Regular, 2);
        Wrapped("% 감소를 먼저 적용하고 초 감소를 빼요", 9, UI.Sub, FontStyle.Regular, 2);
    }

    static bool TryPct(string text, out double value)
    {
        value = 0;
        if (!double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) return false;
        value = Math.Round(Math.Clamp(v, 0, 50), 1);
        return true;
    }

    static string PctText(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);

    static string SecText(int n) => n <= 0 ? "없음" : $"{n}초";

    void PickSec(RoundLabel pill)
    {
        var cms = UI.Rounded(new ContextMenuStrip { Renderer = new RoundMenuRenderer(), ShowImageMargin = false, Font = F(10) });
        for (int n = 0; n <= 9; n++)
        {
            int v = n;
            var item = new ToolStripMenuItem(SecText(n)) { Checked = Cfg.SecCut == n };
            item.Click += (_, _) =>
            {
                Cfg.SecCut = v;
                pill.Text = SecText(v);
                st.Save();
            };
            cms.Items.Add(item);
        }
        cms.Closed += (_, _) => BeginInvoke(cms.Dispose);
        cms.Show(pill, new Point(0, pill.Height));
    }

    void AddToggle(string label, Func<bool> get, Action<bool> set)
    {
        Inline(label, pad, y, S(30), 10, UI.Sub);
        RoundLabel pill = null!;
        void Paint()
        {
            bool on = get();
            pill.Text = on ? "켜기" : "끄기";
            pill.Fill = on ? UI.SoftAccent : UI.HoverBack;
            pill.ForeColor = on ? UI.AccentText : UI.Sub;
        }
        pill = Pill(y, S(150), () => { set(!get()); st.Save(); Paint(); });
        Paint();
        y += S(38);
    }

    void UpdateKeys()
    {
        for (int i = 0; i < 4; i++)
        {
            var pill = keyPills[i];
            if (pill == null) continue;
            bool wait = capturing == i + 1;
            int vk = GetKey(i);
            pill.Text = wait ? "키를 누르세요…" : JanusWatch.KeyName(vk);
            pill.Fill = wait ? UI.SoftAccent : UI.HoverBack;
            pill.ForeColor = wait ? UI.AccentText : vk == 0 ? UI.Sub : UI.Text;
        }
    }

    // ── 키 받기 (JanusForm의 키 칸과 같은 방식) ──

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var k = keyData & Keys.KeyCode;
        if (capturing != 0)
        {
            if (k != Keys.Escape)
            {
                // 한글 입력 상태면 키가 ProcessKey로 온다 → 실제로 누른 키를 찾는다
                if (k == Keys.ProcessKey) k = RealKey(ref msg);
                if (k == Keys.ProcessKey) return true; // 못 찾으면 다시 누르게 기다린다
                // NumLock이 꺼져 있거나 Shift를 누른 채면 넘패드가 PageUp·화살표 등으로 온다 → 확장 키가 아니면 넘패드 키로 받는다
                if (KeyHook.NumpadOf((int)k) is int np and not 0 && ((msg.LParam.ToInt64() >> 24) & 1) == 0) k = (Keys)np;
                int vk = k == Keys.Back ? 0 : (int)k; // 지우기는 Backspace만
                // Shift 하나만 키로 받을 때는 왼쪽·오른쪽을 나눈다 (오른쪽 Shift는 스캔 코드 0x36)
                if (vk == 16) vk = ((msg.LParam.ToInt64() >> 16) & 0xFF) == 0x36 ? 161 : 160;
                SetKey(capturing - 1, vk);
                st.Save();
            }
            capturing = 0;
            UpdateKeys();
            return true;
        }
        if (k == Keys.Enter && enterAction != null) { enterAction(); return true; }
        if (k == Keys.Escape) { Close(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    [DllImport("imm32.dll")] static extern int ImmGetVirtualKey(IntPtr hWnd);
    [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint mapType);

    /// <summary>한글 입력기가 가로챈 키(ProcessKey)의 실제 키를 찾는다.</summary>
    static Keys RealKey(ref Message msg)
    {
        int vk = ImmGetVirtualKey(msg.HWnd);
        if (vk is > 0 and < 255 and not 229) return (Keys)vk;
        uint scan = (uint)((msg.LParam.ToInt64() >> 16) & 0xFF);
        vk = (int)MapVirtualKey(scan, 1); // 스캔 코드 → 키
        return vk is > 0 and < 255 and not 229 ? (Keys)vk : Keys.ProcessKey;
    }
}
