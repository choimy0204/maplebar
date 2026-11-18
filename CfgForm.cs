using System.Drawing.Drawing2D;
using static MapleBar.Native;

namespace MapleBar;

/// <summary>설정 관리 창: 위에 내 설정(저장한 것 · 불러온 것 · 지워지는 날), 아래에 저장 / 불러오기 탭.
/// 저장·불러오기는 각각 30분에 한 번, 하루 3번까지라 막혀 있으면 버튼에 자물쇠를 걸고 오른쪽에 남은 시간을 보인다.</summary>
sealed class CfgForm : Form
{
    const string PlaceKey = "cfgsync";
    static CfgForm? open;

    public static void Open(Form owner, Settings st, float sc)
    {
        if (open == null || open.IsDisposed)
        {
            open = new CfgForm(st, sc) { Owner = owner };
            open.Location = WinPlace.Where(st, PlaceKey, open, owner, sc);
        }
        if (!open.Visible) open.Show();
        open.Activate();
    }

    readonly Settings st;
    readonly float sc;
    readonly List<Font> fonts = new();
    readonly System.Windows.Forms.Timer tick = new() { Interval = 1000 };
    readonly LockButton saveBtn, extendBtn, loadBtn;
    readonly Label saveWait, loadWait, busyLine;
    readonly QuotaView saveQuota, loadQuota;
    readonly StatusCard status;
    int saveLeftN, loadLeftN;
    static int lastTab;   // 마지막으로 본 탭 (0 리스트 · 1 저장 · 2 불러오기)
    readonly TextBox nameBox, pwBox;
    bool working;
    bool fullSave, fullLoad, offline;

    int S(float v) => UI.Scale(v, sc);
    Font F(float size, FontStyle style = FontStyle.Regular) { var x = new Font("맑은 고딕", size * UI.Z, style); fonts.Add(x); return x; }
    Font I(float size) { var x = IconFont(size); fonts.Add(x); return x; }
    internal static Color OnAccent => P.OnFill;

    CfgForm(Settings st, float sc)
    {
        this.st = st; this.sc = sc;
        Text = "설정 관리";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        BackColor = P.Back;
        ForeColor = P.Text;
        AutoScaleMode = AutoScaleMode.None;
        Font = F(10);
        DoubleBuffered = true;

        int pad = S(18), W = S(380), cw = W - pad * 2, ip = S(14);
        var iconFont = IconFont(10f);

        var title = new Label { Text = "설정 관리", Font = F(13, FontStyle.Bold), AutoSize = true, Location = new Point(pad, S(16)) };
        title.MouseDown += (_, e) => Drag(e);
        MouseDown += (_, e) => Drag(e);
        var close = UI.Button("✕", "닫기", Close, sc);
        close.Font = F(10);
        close.Location = new Point(W - pad - close.PreferredWidth + S(6), S(14));
        var sub = new Label
        {
            Text = "알림 설정과 프리셋을 다른 PC로 옮겨요.", Font = F(9), ForeColor = P.Sub, AutoSize = true,
            Location = new Point(pad, title.Bottom + S(4)),
        };
        sub.MouseDown += (_, e) => Drag(e);
        Controls.AddRange(new Control[] { title, close, sub });

        // ── 탭: 리스트 / 저장 / 불러오기 ──
        int y = sub.Bottom + S(14);
        var tabBack = new RoundLabel { AutoSize = false, Radius = S(9), Fill = P.HoverBack, Bounds = new Rectangle(pad, y, cw, S(40)) };
        int tw = (cw - S(8)) / 3;
        RoundLabel Tab(string text, int x) => new RoundLabel
        {
            AutoSize = false, Text = text, TextAlign = ContentAlignment.MiddleCenter, Radius = S(7), Cursor = Cursors.Hand,
            Font = F(9.5f, FontStyle.Bold), Bounds = new Rectangle(x, S(4), tw, S(32)),
        };
        var listTab = Tab("설정 리스트", S(4));
        var saveTab = Tab("설정 저장", S(4) + tw);
        var loadTab = Tab("설정 불러오기", S(4) + tw * 2);
        tabBack.Controls.AddRange(new Control[] { listTab, saveTab, loadTab });
        Controls.Add(tabBack);

        // 탭 카드 위쪽: 남은 횟수 · 다시 채워지는 시간
        y = tabBack.Bottom + S(10);
        int qh = S(72), qw = cw - ip * 2, bh = S(34);

        // ── 설정 리스트: 저장한 설정 · 불러온 설정과 지워지는 날 ──
        var listCard = new Box { Location = new Point(pad, y), Width = cw };
        status = new StatusCard(F(8.5f), F(10, FontStyle.Bold), F(8.5f, FontStyle.Bold), I(11f), ip, S(54))
        { Bounds = new Rectangle(ip, ip, qw, S(54) * 2 + S(1)) };
        listCard.Controls.Add(status);
        listCard.Height = status.Bottom + ip;
        Controls.Add(listCard);
        QuotaView Quota() => new QuotaView(F(8.5f), F(16, FontStyle.Bold), F(9.5f, FontStyle.Bold), F(8.5f, FontStyle.Bold), I(8.5f), sc) { Bounds = new Rectangle(ip, ip, qw, qh) };
        Panel Divider(int top) => new Panel { BackColor = P.Line, Bounds = new Rectangle(ip, top, qw, 1) };

        // ── 설정 저장 ──
        var saveCard = new Box { Location = new Point(pad, y), Width = cw };
        saveQuota = Quota();
        int top = ip + qh + S(12);
        var saveDiv = Divider(top);
        var saveNote = new Label
        {
            Text = "지금 알림 설정과 프리셋을 이름과 비밀번호로 저장해요. 30일 동안 보관돼요.", Font = F(9), ForeColor = P.Sub, AutoSize = true,
            MaximumSize = new Size(qw, 0), Location = new Point(ip, top + S(12)),
        };
        // 글이 줄바꿈될 수 있어 실제 높이(PreferredHeight)로 버튼 자리를 잡는다 (Bottom은 아직 안 맞춰져 있다)
        int by = saveNote.Top + saveNote.PreferredHeight + S(14);
        saveBtn = new LockButton(iconFont) { Text = "저장하기", Bounds = new Rectangle(ip, by, S(104), bh), Primary = true };
        extendBtn = new LockButton(iconFont) { Text = "30일 연장", Bounds = new Rectangle(saveBtn.Right + S(8), by, S(104), bh) };
        saveWait = new Label { Font = F(9, FontStyle.Bold), ForeColor = P.Sub, AutoSize = true };
        foreach (var b in new[] { saveBtn, extendBtn }) b.Font = F(9.5f, FontStyle.Bold);
        saveBtn.Click += (_, _) => _ = SaveOrExtend(false);
        extendBtn.Click += (_, _) => _ = SaveOrExtend(true);
        saveCard.Controls.AddRange(new Control[] { saveQuota, saveDiv, saveNote, saveBtn, extendBtn, saveWait });
        saveCard.Height = by + bh + ip;
        Controls.Add(saveCard);

        // ── 설정 불러오기 (저장 칸과 같은 자리) ──
        var loadCard = new Box { Location = new Point(pad, y), Width = cw };
        loadQuota = Quota();
        var loadDiv = Divider(top);
        int fy = top + S(12);
        var (nameBack, nb) = Field("설정 이름", false, new Rectangle(ip, fy, qw, S(34)));
        var (pwBack, pb) = Field("비밀번호", true, new Rectangle(ip, fy + S(42), qw, S(34)));
        nameBox = nb; pwBox = pb;
        nameBox.MaxLength = CfgSync.NameMax; pwBox.MaxLength = CfgSync.PwMax;
        by = pwBack.Bottom + S(12);
        loadBtn = new LockButton(iconFont) { Text = "불러오기", Bounds = new Rectangle(ip, by, S(104), bh), Primary = true, Font = F(9.5f, FontStyle.Bold) };
        loadWait = new Label { Font = F(9, FontStyle.Bold), ForeColor = P.Sub, AutoSize = true };
        loadBtn.Click += (_, _) => _ = LoadNow();
        pwBox.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; _ = LoadNow(); } };
        nameBox.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; pwBox.Focus(); } };
        loadCard.Controls.AddRange(new Control[] { loadQuota, loadDiv, nameBack, pwBack, loadBtn, loadWait });
        loadCard.Height = by + bh + ip;
        Controls.Add(loadCard);

        busyLine = new Label
        {
            Font = F(9), ForeColor = P.Red, AutoSize = true,
            MaximumSize = new Size(cw, 0), Visible = false,
        };
        Controls.Add(busyLine);
        ClientSize = new Size(W, saveCard.Bottom + pad);

        int tab = lastTab;
        var cards = new Control[] { listCard, saveCard, loadCard };
        var tabs = new[] { listTab, saveTab, loadTab };
        void Refresh()
        {
            Refresh1(true);
            Refresh1(false);
            string reset = CfgSync.Hms(CfgSync.ToReset());
            saveQuota.Set(saveLeftN, CfgSync.PerDay, reset);
            loadQuota.Set(loadLeftN, CfgSync.PerDay, reset);
            status.Set(CfgSync.Saved, CfgSync.Loaded);

            // 30일 연장은 저장하거나 불러온 설정이 아직 남아 있을 때만
            extendBtn.Visible = ExtendName() != null;
            var last = extendBtn.Visible ? extendBtn : saveBtn;
            saveWait.Location = new Point(last.Right + S(10), last.Top + (bh - saveWait.PreferredHeight) / 2);
            loadWait.Location = new Point(loadBtn.Right + S(10), loadBtn.Top + (bh - loadWait.PreferredHeight) / 2);

            var card = cards[tab];
            var msg = offline ? "지금은 서버에 연결할 수 없어요." : CfgSync.BusyText(fullSave, fullLoad);
            busyLine.Text = msg ?? "";
            busyLine.Visible = msg != null;
            busyLine.Location = new Point(pad, card.Bottom + S(10));
            int h = (msg != null ? busyLine.Top + busyLine.PreferredHeight : card.Bottom) + pad;
            if (ClientSize.Height != h) ClientSize = new Size(W, h);
        }
        void Pick(int i)
        {
            tab = lastTab = i;
            for (int n = 0; n < tabs.Length; n++)
            {
                bool on = n == i;
                cards[n].Visible = on;
                tabs[n].Fill = on ? P.Fill : Color.Transparent;
                tabs[n].ForeColor = on ? OnAccent : P.Sub;
            }
            ActiveControl = null;
            Refresh();
        }
        for (int n = 0; n < tabs.Length; n++) { int i = n; tabs[n].Click += (_, _) => Pick(i); }
        Pick(tab);
        refreshAll = Refresh;
        tick.Tick += (_, _) => Refresh();
        tick.Start();

        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { e.Handled = true; Close(); } };
        Paint += (_, e) => { using var pen = new Pen(P.Line); e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1); };
        Shown += (_, _) => { ActiveControl = null; _ = Status(); };
    }

    Action? refreshAll;

    /// <summary>저장(true)·불러오기(false) 버튼을 지금 상태로: 대기 중이거나 오늘 다 썼으면 자물쇠</summary>
    void Refresh1(bool save)
    {
        var (wait, daily, left) = CfgSync.State(save);
        bool locked = daily || wait > TimeSpan.Zero;
        string waitText = daily ? "오늘은 다 썼어요" : wait > TimeSpan.Zero ? $"{CfgSync.Left(wait)} 뒤에 돼요" : "";
        if (save)
        {
            saveBtn.Locked = extendBtn.Locked = locked;
            saveBtn.Busy = extendBtn.Busy = working;
            saveWait.Text = waitText;
            saveLeftN = left;
        }
        else
        {
            loadBtn.Locked = locked;
            loadBtn.Busy = working;
            loadWait.Text = waitText;
            loadLeftN = left;
        }
    }

    /// <summary>둥근 칸 안에 테두리 없는 글 칸</summary>
    (RoundLabel back, TextBox box) Field(string hint, bool secret, Rectangle r)
    {
        var back = new RoundLabel { AutoSize = false, Radius = S(6), Fill = P.HoverBack, Cursor = Cursors.IBeam, Bounds = r };
        var tb = new TextBox
        {
            BorderStyle = BorderStyle.None, BackColor = P.HoverBack, ForeColor = P.Text, Font = F(9.5f),
            PlaceholderText = hint, ImeMode = ImeMode.NoControl, UseSystemPasswordChar = secret,
        };
        back.Controls.Add(tb);
        int ip = S(10);
        tb.Location = new Point(ip, (r.Height - tb.PreferredHeight) / 2);
        tb.Width = r.Width - ip * 2;
        back.Click += (_, _) => tb.Focus();
        return (back, tb);
    }

    internal static Font IconFont(float size)
    {
        string fam = FontFamily.Families.Any(f => f.Name == "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
        return new Font(fam, size * UI.Z);
    }

    // ── 서버 ──

    /// <summary>창을 열면 서버 기준 남은 횟수·대기와 오늘 한도를 받아 온다</summary>
    async Task Status()
    {
        var r = await CfgSync.Call("status", new { });
        if (IsDisposed) return;
        offline = r == null;
        if (r != null) { CfgSync.Sync(r.Q); fullSave = r.FullSave; fullLoad = r.FullLoad; }
        refreshAll?.Invoke();
    }

    bool Blocked(bool save)
    {
        var (wait, daily, _) = CfgSync.State(save);
        if (daily) { Tell("오늘은 3번 다 썼어요", "내일 다시 해 주세요."); return true; }
        if (wait > TimeSpan.Zero) { Tell("조금 뒤에 다시 해 주세요", $"{CfgSync.Left(wait)} 뒤에 할 수 있어요."); return true; }
        return false;
    }

    void Tell(string title, string note) => AskBox.Choose(this, title, note, true, "확인");

    /// <summary>서버 답을 반영하고, 실패면 알린다. 성공이면 true</summary>
    bool Take(CfgSync.Reply? r)
    {
        offline = r == null;
        if (r == null) { refreshAll?.Invoke(); Tell("지금은 연결할 수 없어요", "인터넷을 확인하고 잠시 뒤 다시 해 주세요."); return false; }
        CfgSync.Sync(r.Q);
        fullSave = r.FullSave; fullLoad = r.FullLoad;
        refreshAll?.Invoke();
        if (r.Ok) return true;
        Tell(r.Err == "busy" ? "지금은 할 수 없어요" : "다시 확인해 주세요", CfgSync.Message(r));
        return false;
    }

    static string Until(long ms) =>
        DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime().ToString("M월 d일");

    /// <summary>연장할 수 있는 설정 이름: 저장한 것이 먼저, 없으면 불러온 것. 이미 지워졌으면 null</summary>
    static string? ExtendName()
    {
        foreach (var s in new[] { CfgSync.Saved, CfgSync.Loaded })
            if (s != null && CfgSync.Remain(s) is { } t && t > TimeSpan.Zero) return s.Name;
        return null;
    }

    async Task SaveOrExtend(bool extend)
    {
        if (working || Blocked(true)) return;
        var ask = CfgAsk.Show(this, sc, extend ? "30일 연장" : "설정 저장",
            extend ? "저장한 설정을 30일 더 보관해요." : "같은 이름이 있으면 비밀번호가 같을 때만 덮어써요.",
            extend ? "연장" : "저장", extend ? ExtendName() : CfgSync.Saved?.Name);
        if (ask is not var (name, pw)) return;
        string? data = null;
        if (!extend && (data = CfgSync.Build(st)) == null)
        {
            Tell("저장할 수 없어요", "프리셋이 너무 많아요. 프리셋을 조금 지우고 다시 해 주세요.");
            return;
        }
        working = true; refreshAll?.Invoke();
        var r = extend ? await CfgSync.Call("extend", new { name, pw }) : await CfgSync.Call("save", new { name, pw, data });
        working = false;
        if (IsDisposed) return;
        if (r?.Ok == true) CfgSync.Mark(true);
        if (!Take(r)) return;
        CfgSync.Remember(true, name, r!.Expires);
        refreshAll?.Invoke();
        Tell(extend ? "연장했어요" : "저장했어요", $"'{name}' 설정은 {Until(r!.Expires)}까지 보관돼요.");
    }

    async Task LoadNow()
    {
        if (working || Blocked(false)) return;
        string name = nameBox.Text.Trim(), pw = pwBox.Text;
        if (name.Length == 0) { Tell("설정 이름을 넣어 주세요", "저장할 때 정한 이름이에요."); nameBox.Focus(); return; }
        if (pw.Length < CfgSync.PwMin) { Tell("비밀번호를 넣어 주세요", "저장할 때 정한 비밀번호예요."); pwBox.Focus(); return; }
        working = true; refreshAll?.Invoke();
        var r = await CfgSync.Call("load", new { name, pw });
        working = false;
        if (IsDisposed) return;
        if (r?.Ok == true) CfgSync.Mark(false);
        if (!Take(r)) return;
        var pack = CfgSync.Parse(r!.Data);
        if (pack == null) { Tell("불러올 수 없어요", "저장된 설정이 깨져 있어요."); return; }
        bool soon = r.Expires > 0 && DateTimeOffset.FromUnixTimeMilliseconds(r.Expires) - DateTimeOffset.UtcNow < TimeSpan.FromDays(1);
        if (AskBox.Show(this, "지금 설정을 이 설정으로 덮어쓸까요?", "알림 설정과 프리셋이 모두 바뀌어요.", "덮어쓰기", "취소", true))
        {
            CfgSync.Apply(st, pack);
            CfgSync.Remember(false, name, r.Expires);
            refreshAll?.Invoke();
            // 열려 있는 알림 창은 옛 값을 들고 있으니 닫는다 (다시 열면 새 값으로 그린다)
            foreach (var f in Application.OpenForms.OfType<JanusForm>().ToList()) f.Close();
            pwBox.Text = "";
            Tell("불러왔어요", "알림 설정과 프리셋이 바뀌었어요.");
        }
        if (soon) CfgToast.Show(sc, "하루 안에 지워져요", $"'{name}' 설정이 곧 지워져요. 계속 쓰려면 설정 관리에서 연장하세요.");
    }

    // ── 창 ──

    void Drag(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        DragWindow(Handle);
        WinPlace.Remember(st, PlaceKey, this);
    }

    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); UI.RoundCorners(Handle); }

    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ClassStyle |= 0x20000; return cp; } // 창 그림자
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        tick.Dispose();
        WinPlace.Remember(st, PlaceKey, this);
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) foreach (var f in fonts) f.Dispose();
        base.Dispose(disposing);
    }

    const TextFormatFlags Flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;

    static Color Mix(Color a, Color b, float t) =>
        Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    static void FillRound(Graphics g, Color c, RectangleF r, float radius)
    {
        using var b = new SolidBrush(c);
        using var path = UI.RoundRect(r, radius);
        g.FillPath(b, path);
    }

    /// <summary>탭 카드 위쪽: "오늘 남은 횟수 ··· 2 / 3", 칸 3개짜리 막대, "다시 채워지기까지 ··· 5시간 12분 3초"</summary>
    sealed class QuotaView : Control
    {
        readonly Font small, big, mid, smallBold, icon;
        readonly float sc;
        int left = -1, max;
        string reset = "";

        public QuotaView(Font small, Font big, Font mid, Font smallBold, Font icon, float sc)
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = P.Back;
            this.small = small; this.big = big; this.mid = mid; this.smallBold = smallBold; this.icon = icon; this.sc = sc;
        }

        int S(float v) => UI.Scale(v, sc);

        public void Set(int left, int max, string reset)
        {
            if (this.left == left && this.max == max && this.reset == reset) return;
            this.left = left; this.max = max; this.reset = reset;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Size M(string s, Font f) => TextRenderer.MeasureText(g, s, f, Size.Empty, Flags);

            // 1줄: 오늘 남은 횟수 ········· 2 / 3
            int n = Math.Max(0, left);
            var nS = M(n.ToString(), big);
            string of = $"/ {max}";
            var oS = M(of, mid);
            int sh = M("가", small).Height;
            int x = Width - oS.Width;
            TextRenderer.DrawText(g, of, mid, new Point(x, nS.Height - oS.Height - S(1)), P.Sub, Flags);
            x -= S(5) + nS.Width;
            TextRenderer.DrawText(g, n.ToString(), big, new Point(x, 0), n > 0 ? P.AccentText : P.Sub, Flags);
            TextRenderer.DrawText(g, "오늘 남은 횟수", small, new Point(0, nS.Height - sh - S(2)), P.Sub, Flags);

            // 2줄: 칸 막대 (남은 만큼 색칠)
            int by = nS.Height + S(8), bh = S(6), gap = S(5);
            if (max > 0)
            {
                float w = (Width - gap * (max - 1)) / (float)max;
                for (int i = 0; i < max; i++)
                    FillRound(g, i < n ? P.Accent : P.Line, new RectangleF(i * (w + gap), by, w, bh), bh / 2f);
            }

            // 3줄: 🕑 다시 채워지기까지 ········· 5시간 12분 3초
            int ty = by + bh + S(10);
            var iS = M("", icon);
            TextRenderer.DrawText(g, "", icon, new Point(0, ty + (sh - iS.Height) / 2), P.Sub, Flags);
            TextRenderer.DrawText(g, "다시 채워지기까지", small, new Point(iS.Width + S(6), ty), P.Sub, Flags);
            var rS = M(reset, smallBold);
            TextRenderer.DrawText(g, reset, smallBold, new Point(Width - rS.Width, ty), P.Text, Flags);
        }
    }

    /// <summary>내 설정: 저장한 설정 · 불러온 설정의 이름과 지워지는 날(D-n)</summary>
    sealed class StatusCard : Control
    {
        readonly Font small, value, badge, icon;
        readonly int pad, rowH;
        CfgSync.Slot? saved, loaded;
        string key = "";

        public StatusCard(Font small, Font value, Font badge, Font icon, int pad, int rowH)
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = P.Back;
            this.small = small; this.value = value; this.badge = badge; this.icon = icon; this.pad = pad; this.rowH = rowH;
        }

        public void Set(CfgSync.Slot? saved, CfgSync.Slot? loaded)
        {
            // 날짜가 바뀌는 순간(D-n, 지워짐)만 다시 그린다
            string k = $"{saved?.Name}|{Badge(saved).text}|{loaded?.Name}|{Badge(loaded).text}";
            if (k == key) return;
            key = k; this.saved = saved; this.loaded = loaded;
            Invalidate();
        }

        /// <summary>D-29 · 곧 지워져요 · 지워졌어요</summary>
        static (string text, Color fore, Color back) Badge(CfgSync.Slot? s)
        {
            if (CfgSync.Remain(s) is not { } t) return ("", P.Sub, P.Back);
            if (t <= TimeSpan.Zero) return ("지워졌어요", P.Sub, P.Back);
            if (t < TimeSpan.FromDays(1)) return ("곧 지워져요", P.Red, Mix(P.HoverBack, P.Red, 0.18f));
            return ($"D-{(int)Math.Ceiling(t.TotalDays)}", P.AccentText, (P.Dark ? Mix(P.HoverBack, P.Accent, 0.18f) : P.Fill));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float k = DeviceDpi / 96f;
            FillRound(g, P.HoverBack, new RectangleF(0, 0, Width - 0.5f, Height - 0.5f), UI.Scale(12, k));
            using (var pen = new Pen(P.Line)) g.DrawLine(pen, pad, rowH, Width - pad, rowH);
            Row(g, 0, "", "저장한 설정", saved, "아직 저장한 적이 없어요");
            Row(g, rowH + 1, "", "불러온 설정", loaded, "이 PC 설정을 쓰는 중이에요");
        }

        void Row(Graphics g, int y0, string glyph, string label, CfgSync.Slot? s, string empty)
        {
            float k = DeviceDpi / 96f;
            int S(float v) => UI.Scale(v, k);
            Size M(string t, Font f) => TextRenderer.MeasureText(g, t, f, Size.Empty, Flags);
            var (bt, bf, bb) = Badge(s);
            bool live = s != null && bt != "지워졌어요";

            // 아이콘 칸
            int sz = S(32), ix = pad, iy = y0 + (rowH - sz) / 2;
            FillRound(g, live ? (P.Dark ? Mix(P.HoverBack, P.Accent, 0.18f) : P.Fill) : P.Back, new RectangleF(ix, iy, sz, sz), S(9));
            var gS = M(glyph, icon);
            TextRenderer.DrawText(g, glyph, icon, new Point(ix + (sz - gS.Width) / 2, iy + (sz - gS.Height) / 2), live ? P.AccentText : P.Sub, Flags);

            // 오른쪽: 배지 + 날짜
            int right = Width - pad, textRight = right;
            if (bt.Length > 0)
            {
                var bS = M(bt, badge);
                int bw = bS.Width + S(14), bh = bS.Height + S(6);
                string date = DateTimeOffset.FromUnixTimeMilliseconds(s!.Expires).ToLocalTime().ToString("M월 d일까지");
                var dS = M(date, small);
                int total = bh + S(4) + dS.Height, top = y0 + (rowH - total) / 2;
                var br = new Rectangle(right - bw, top, bw, bh);
                FillRound(g, bb, br, S(6));
                TextRenderer.DrawText(g, bt, badge, new Point(br.X + (bw - bS.Width) / 2, br.Y + (bh - bS.Height) / 2), bf, Flags);
                TextRenderer.DrawText(g, date, small, new Point(right - dS.Width, top + bh + S(4)), P.Sub, Flags);
                textRight = right - Math.Max(bw, dS.Width) - S(10);
            }

            // 가운데: 이름표 + 설정 이름
            int tx = ix + sz + S(12);
            string v = s?.Name ?? empty;
            var vf = s != null ? value : small;
            int lh = M(label, small).Height, vh = M(v, vf).Height, gap = S(3);
            int ty = y0 + (rowH - lh - gap - vh) / 2;
            TextRenderer.DrawText(g, label, small, new Point(tx, ty), P.Sub, Flags);
            TextRenderer.DrawText(g, v, vf, new Rectangle(tx, ty + lh + gap, Math.Max(0, textRight - tx), vh + 2),
                s != null ? (live ? P.Text : P.Sub) : P.Sub, Flags | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>둥근 테두리 칸</summary>
    sealed class Box : Panel
    {
        public Box() { DoubleBuffered = true; ResizeRedraw = true; BackColor = P.Back; }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(P.Line);
            using var path = UI.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), UI.Scale(12, DeviceDpi / 96f));
            e.Graphics.DrawPath(pen, path);
        }
    }

    /// <summary>둥근 직사각형 버튼. 잠기면 회색 바탕에 자물쇠를 붙이고 눌러도 아무것도 하지 않는다</summary>
    sealed class LockButton : RoundLabel
    {
        readonly Font icon;
        bool locked, busy, hover;
        public bool Primary { get; set; }
        public bool Locked { get => locked; set { if (locked == value) return; locked = value; Restyle(); } }
        public bool Busy { get => busy; set { if (busy == value) return; busy = value; Restyle(); } }

        public LockButton(Font icon)
        {
            this.icon = icon;
            AutoSize = false; Radius = UI.Scale(8, 1); TextAlign = ContentAlignment.MiddleCenter; UseMnemonic = false;
            MouseEnter += (_, _) => { hover = true; Restyle(); };
            MouseLeave += (_, _) => { hover = false; Restyle(); };
            HandleCreated += (_, _) => { Radius = UI.Scale(8, DeviceDpi / 96f); Restyle(); };
        }

        bool Off => locked || busy;

        void Restyle()
        {
            Color back = Off ? P.HoverBack : Primary ? P.Fill : P.HoverBack;
            if (hover && !Off) back = ControlPaint.Dark(back, 0.05f);
            Fill = back;
            ForeColor = Off ? P.Sub : Primary ? OnAccent : P.Text;
            Cursor = Off ? Cursors.Default : Cursors.Hand;
            Invalidate();
        }

        protected override void OnClick(EventArgs e) { if (!Off) base.OnClick(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (!locked) { base.OnPaint(e); return; }
            const TextFormatFlags f = TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;
            var g = e.Graphics;
            const string glyph = ""; // 자물쇠
            var gs = TextRenderer.MeasureText(g, glyph, icon, Size.Empty, f);
            var ts = TextRenderer.MeasureText(g, Text, Font, Size.Empty, f);
            int gap = UI.Scale(5, DeviceDpi / 96f), x = (Width - gs.Width - gap - ts.Width) / 2;
            TextRenderer.DrawText(g, glyph, icon, new Rectangle(x, 0, gs.Width, Height), ForeColor, f);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(x + gs.Width + gap, 0, ts.Width + 2, Height), ForeColor, f);
        }
    }
}

/// <summary>설정 저장·연장 때 이름과 비밀번호를 받는 작은 창. 확인을 누르면 (이름, 비밀번호), 취소면 null</summary>
sealed class CfgAsk : Form
{
    public static (string name, string pw)? Show(IWin32Window owner, float sc, string title, string note, string okText, string? name = null)
    {
        using var f = new CfgAsk(sc, title, note, okText, name);
        if (owner is Form o) f.Location = new Point(o.Left + (o.Width - f.Width) / 2, o.Top + (o.Height - f.Height) / 3);
        f.ShowDialog(owner);
        return f.result;
    }

    (string, string)? result;
    readonly List<Font> fonts = new();

    CfgAsk(float sc, string titleText, string noteText, string okText, string? name)
    {
        int S(float v) => UI.Scale(v, sc);
        Font F(float size, FontStyle style = FontStyle.Regular) { var x = new Font("맑은 고딕", size * UI.Z, style); fonts.Add(x); return x; }
        Text = titleText;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = P.Back;
        AutoScaleMode = AutoScaleMode.None;
        KeyPreview = true;
        int w = S(340), pad = S(18), fw = w - pad * 2;

        var bar = new Panel { BackColor = P.Accent, Location = Point.Empty, Size = new Size(w, S(5)) };
        var title = new Label { Text = titleText, Font = F(11.5f, FontStyle.Bold), ForeColor = P.Text, AutoSize = true, Location = new Point(pad, S(20)) };
        var note = new Label
        {
            Text = noteText, Font = F(8.5f), ForeColor = P.Sub, AutoSize = true, MaximumSize = new Size(fw, 0),
            Location = new Point(pad, title.Top + title.PreferredHeight + S(4)),
        };
        int y = note.Top + note.PreferredHeight + S(12);

        TextBox Field(string hint, bool secret, int top, int max)
        {
            var back = new RoundLabel { AutoSize = false, Radius = S(6), Fill = P.HoverBack, Cursor = Cursors.IBeam, Bounds = new Rectangle(pad, top, fw, S(34)) };
            var tb = new TextBox
            {
                BorderStyle = BorderStyle.None, BackColor = P.HoverBack, ForeColor = P.Text, Font = F(9.5f), MaxLength = max,
                PlaceholderText = hint, ImeMode = ImeMode.NoControl, UseSystemPasswordChar = secret,
            };
            back.Controls.Add(tb);
            tb.Location = new Point(S(10), (back.Height - tb.PreferredHeight) / 2);
            tb.Width = fw - S(20);
            back.Click += (_, _) => tb.Focus();
            Controls.Add(back);
            return tb;
        }
        var nameBox = Field("설정 이름", false, y, CfgSync.NameMax);
        var pwBox = Field("비밀번호", true, y + S(42), CfgSync.PwMax);
        y += S(42) * 2;
        var warn = new Label
        {
            Font = F(8.5f), ForeColor = P.Dark ? Color.FromArgb(255, 140, 120) : Color.FromArgb(210, 60, 40),
            AutoSize = true, MaximumSize = new Size(fw, 0), Location = new Point(pad, y), Text = "",
        };
        y += S(20);

        void Ok()
        {
            string n = nameBox.Text.Trim(), p = pwBox.Text;
            if (n.Length == 0) { warn.Text = "설정 이름을 넣어 주세요."; nameBox.Focus(); return; }
            if (p.Length < CfgSync.PwMin) { warn.Text = $"비밀번호는 {CfgSync.PwMin}자 넘게 넣어 주세요."; pwBox.Focus(); return; }
            result = (n, p);
            Close();
        }
        int gap = S(10), bw = (fw - gap) / 2;
        RoundLabel Btn(string text, bool primary, int x, Action click)
        {
            Color back = primary ? P.Fill : P.HoverBack;
            var b = new RoundLabel
            {
                AutoSize = false, Bounds = new Rectangle(x, y, bw, S(36)), Radius = S(8), Text = text, Fill = back,
                ForeColor = primary ? CfgForm.OnAccent : P.Text, TextAlign = ContentAlignment.MiddleCenter,
                Font = F(10, FontStyle.Bold), Cursor = Cursors.Hand, UseMnemonic = false,
            };
            b.MouseEnter += (_, _) => b.Fill = ControlPaint.Dark(back, 0.05f);
            b.MouseLeave += (_, _) => b.Fill = back;
            b.Click += (_, _) => click();
            return b;
        }
        Controls.AddRange(new Control[] { bar, title, note, warn, Btn(okText, true, pad, Ok), Btn("취소", false, pad + bw + gap, Close) });
        ClientSize = new Size(w, y + S(36) + S(16));

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) { e.Handled = true; Close(); }
            else if (e.KeyCode == Keys.Enter) { e.Handled = true; e.SuppressKeyPress = true; if (ActiveControl == nameBox) pwBox.Focus(); else Ok(); }
        };
        Paint += (_, e) => { using var pen = new Pen(P.Line); e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1); };
        HandleCreated += (_, _) => UI.RoundCorners(Handle);
        if (!string.IsNullOrEmpty(name)) nameBox.Text = name;
        Shown += (_, _) => { if (nameBox.TextLength > 0) pwBox.Focus(); else nameBox.Focus(); };
    }

    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ClassStyle |= 0x20000; return cp; }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) foreach (var f in fonts) f.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>화면 오른쪽 아래에 잠깐 뜨는 알림 (포커스는 뺏지 않는다). 10초 뒤 저절로 닫힌다</summary>
sealed class CfgToast : Form
{
    public static void Show(float sc, string title, string note) => new CfgToast(sc, title, note).Show();

    readonly List<Font> fonts = new();
    readonly System.Windows.Forms.Timer life = new() { Interval = 10000 };

    CfgToast(float sc, string titleText, string noteText)
    {
        int S(float v) => UI.Scale(v, sc);
        Font F(float size, FontStyle style = FontStyle.Regular) { var x = new Font("맑은 고딕", size * UI.Z, style); fonts.Add(x); return x; }
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = P.Back;
        AutoScaleMode = AutoScaleMode.None;
        int w = S(300), pad = S(16), iconSize = S(32), textLeft = pad + iconSize + S(12);

        var icon = new RoundLabel
        {
            AutoSize = false, Bounds = new Rectangle(pad, pad, iconSize, iconSize), Radius = S(8),
            Fill = P.Dark ? Color.FromArgb(80, 86, 94) : UI.SoftAccent, ForeColor = UI.AccentText,
            Text = "", TextAlign = ContentAlignment.MiddleCenter, // 달력
        };
        var iconFont = CfgForm.IconFont(12f); fonts.Add(iconFont); icon.Font = iconFont;
        var title = new Label { Text = titleText, Font = F(10.5f, FontStyle.Bold), ForeColor = P.Text, AutoSize = true, UseMnemonic = false, Location = new Point(textLeft, pad - S(1)) };
        var note = new Label
        {
            Text = noteText, Font = F(9), ForeColor = P.Sub, AutoSize = true, UseMnemonic = false,
            MaximumSize = new Size(w - textLeft - pad, 0), Location = new Point(textLeft, title.Top + title.PreferredHeight + S(3)),
        };
        int y = Math.Max(icon.Bottom, note.Top + note.PreferredHeight) + S(12);
        var ok = new RoundLabel
        {
            AutoSize = false, Bounds = new Rectangle(w - pad - S(72), y, S(72), S(30)), Radius = S(8), Text = "확인",
            Fill = P.HoverBack, ForeColor = P.Text, TextAlign = ContentAlignment.MiddleCenter, Font = F(9.5f, FontStyle.Bold), Cursor = Cursors.Hand,
        };
        ok.Click += (_, _) => Close();
        Controls.AddRange(new Control[] { icon, title, note, ok });
        ClientSize = new Size(w, ok.Bottom + pad);

        var wa = Screen.PrimaryScreen!.WorkingArea;
        Location = new Point(wa.Right - Width - S(16), wa.Bottom - Height - S(16));
        life.Tick += (_, _) => Close();
        life.Start();
        Paint += (_, e) => { using var pen = new Pen(P.Line); e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1); };
        HandleCreated += (_, _) => UI.RoundCorners(Handle);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
            cp.ClassStyle |= CS_DROPSHADOW;
            return cp;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { life.Dispose(); foreach (var f in fonts) f.Dispose(); }
        base.Dispose(disposing);
    }
}

/// <summary>설정 관리 창 색: 바와 같은 바탕(다크 차콜 · 밝은 흰색)에 살구색 강조. 살구색 칸 위 글자는 늘 진한 차콜</summary>
static class P
{
    public static bool Dark => UI.Dark;
    public static Color Back => Dark ? Color.FromArgb(48, 54, 60) : Color.White;
    public static Color HoverBack => Dark ? Color.FromArgb(64, 70, 77) : Color.FromArgb(242, 242, 242);
    public static Color Line => Dark ? Color.FromArgb(80, 86, 93) : Color.FromArgb(226, 228, 231);
    public static Color Text => Dark ? Color.FromArgb(234, 234, 234) : Color.FromArgb(31, 33, 36);
    public static Color Sub => Dark ? Color.FromArgb(162, 168, 175) : Color.FromArgb(128, 132, 138);
    public static Color Accent => Dark ? Color.FromArgb(240, 186, 120) : Color.FromArgb(255, 138, 0);
    /// <summary>고른 탭·주 버튼 바탕과 그 위 글자: 다크는 살구색 칸에 차콜 글자, 밝은 모드는 연한 살구 칸에 주황 글자 (바의 고른 칸과 같다)</summary>
    public static Color Fill => Dark ? Accent : Color.FromArgb(255, 243, 224);
    public static Color OnFill => Dark ? Color.FromArgb(40, 44, 50) : Color.FromArgb(255, 138, 0);
    /// <summary>흰 바탕 위 살구색 글자는 흐려 보여서 밝은 모드에서는 조금 진하게</summary>
    public static Color AccentText => Accent;
    public static Color Red => Dark ? Color.FromArgb(240, 128, 110) : Color.FromArgb(205, 70, 50);
}
