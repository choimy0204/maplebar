using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

using static MapleBar.Native;

namespace MapleBar;
/// <summary>솔 야누스 · 룬 알림 설정 창. 바와 같은 흰 바탕, 둥근 카드, 주황 스위치를 쓴다.</summary>
sealed class JanusForm : Form
{
    static JanusForm? alertForm, toonForm;

    public static void Open(Form owner, Settings st, JanusWatch watch, float sc, bool toon = false, bool buff = false)
    {
        // 야누스·룬·버프·도핑은 한 창에 있다
        var open = toon ? toonForm : alertForm;
        if (open == null || open.IsDisposed) open = new JanusForm(st, watch, sc, toon, buff) { Owner = owner };
        if (toon) toonForm = open; else alertForm = open;
        var wa = Screen.FromControl(owner).WorkingArea;
        // 화면보다 크면 화면에 맞추고 스크롤로 본다
        if (open.Height > wa.Height || open.Width > wa.Width)
        {
            open.AutoScroll = true;
            open.Size = new Size(Math.Min(open.Width, wa.Width), Math.Min(open.Height, wa.Height));
        }
        open.Location = new Point(
            Math.Clamp(owner.Left + (owner.Width - open.Width) / 2, wa.Left, Math.Max(wa.Left, wa.Right - open.Width)),
            Math.Clamp(owner.Bottom + (int)(12 * sc), wa.Top, Math.Max(wa.Top, wa.Bottom - open.Height)));
        if (!open.Visible) open.Show();
        open.Activate();
    }

    static readonly Color Purple = Color.FromArgb(135, 80, 235);
    static readonly Color Blue = Color.FromArgb(20, 150, 220);
    static readonly Color Gray = Color.FromArgb(135, 135, 135);
    const string Waiting = "키를 누르세요…";

    readonly Settings st;
    readonly JanusWatch watch;
    readonly float sc;
    // 알림 창에는 야누스·룬 칸만, 웹툰 키 창에는 웹툰 칸만 있다 (없는 칸은 null)
    readonly bool toon;
    readonly RoundLabel? janusKey, npcKey, upKey, downKey;
    RoundLabel? nextKey, prevKey, dopeKey, shortsNextKey, shortsPrevKey;
    Label? dopeStatus;
    // 버프 칸마다: 키 칸, 상태 줄 (버프 창이 아니면 비어 있다)
    readonly RoundLabel?[] buffKeys = new RoundLabel?[JanusWatch.BuffSlots];
    readonly Label?[] buffStatus = new Label?[JanusWatch.BuffSlots];
    Action<bool>? editingName; // 버프 이름 칸을 고치는 중이면 끝내는 함수 (true = 저장)
    static readonly Color Orange = Color.FromArgb(255, 140, 0);
    static readonly Color Green = Color.FromArgb(0, 190, 90);
    readonly Label? janusStatus, runeStatus;
    readonly System.Windows.Forms.Timer refresh = new() { Interval = 250 };
    int capturing; // 1 = 솔 야누스 키, 3 = NPC 대화 키, 4 = 웹툰 위로, 5 = 웹툰 아래로 키를 받는 중

    int S(float v) => UI.Scale(v, sc);
    static Font F(float size, FontStyle style = FontStyle.Regular) => new("맑은 고딕", size * UI.Z, style);

    JanusForm(Settings st, JanusWatch watch, float sc, bool toon, bool buff)
    {
        this.st = st; this.watch = watch; this.sc = sc; this.toon = toon;
        Text = toon ? "웹툰 키" : "알림 설정";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.White;
        ForeColor = UI.Text;
        Font = F(10);
        DoubleBuffered = true;

        // 알림 창은 세 줄(야누스·룬 | 버프 | 도핑)로 옆으로 펼쳐 세로를 줄인다
        int pad = S(18), W = toon ? S(372) : pad * 2 + S(352) * 3 + S(28) * 2, y = S(14);

        // 머리: 제목과 닫기. 빈 곳을 끌면 창이 움직인다
        var title = new Label { Text = toon ? "웹툰 키" : "알림 설정", Font = F(13, FontStyle.Bold), AutoSize = true, Location = new Point(pad, y) };
        title.MouseDown += (_, e) => Drag(e);
        MouseDown += (_, e) => Drag(e);
        var close = UI.Button("✕", "닫기", Close, sc);
        close.Font = F(10);
        Controls.Add(title);
        Controls.Add(close);
        close.Location = new Point(W - pad - close.PreferredSize.Width + S(6), y + S(2));
        y += S(34);
        var sub = new Label
        {
            Text = toon ? "웹툰 창이나 메이플 창에서 누른 키만 읽어요. 게임에는 아무것도 보내지 않아요."
                        : "메이플 창에서 누른 키만 읽어요. 게임에는 아무것도 보내지 않아요.",
            Font = F(9), ForeColor = Gray, AutoSize = true, MaximumSize = new Size(W - pad * 2, 0), Location = new Point(pad, y),
        };
        sub.MouseDown += (_, e) => Drag(e);
        Controls.Add(sub);
        y += sub.PreferredSize.Height + S(12);

        if (!toon) y += MakeSoundCard(pad, y, W - pad * 2).Height + S(10);

        if (toon)
        {
            var toonCard = MakeScrollCard(pad, y, W - pad * 2, out var u, out var d);
            upKey = u; downKey = d;
            y += toonCard.Height + S(10);
        }
        else
        {
        // 왼쪽 줄: 솔 야누스 · 룬 / 오른쪽 줄: 버프 · 도핑. 제목과 세로선으로 나눈다
        int colW = S(352), gap = S(28);
        int lx = pad, rx = pad + colW + gap, top = y;
        y = top + Section("솔 야누스 · 룬", Purple, lx, top, colW);
        var janusCard = MakeCard(lx, y, colW, new CardSpec
        {
            Color = Purple, Title = "솔 야누스", On = st.JanusOn, SetOn = v => st.JanusOn = v,
            KeyLabel = "솔 야누스 키", Which = 1,
            StepLabel = "지속시간", Value = st.JanusSec, Options = new[] { 60, 70, 80, 120 }, Fmt = v => JanusWatch.Endless(st, v) ? "무한대" : v + "초", SetValue = v => st.JanusSec = v,
            Hint =
                "1레벨 60초 · 10레벨 70초 · 20레벨 80초 · 30레벨 120초(루나 익스텐션이 있으면 무한대)\n" +
                "첫 설치 후 50초 안에 더 설치한 건 세지 않아요. 루나 익스텐션이 없으면 다시 깔아도 갱신하지 않고 첫 설치만 세요.\n" +
                "5석펫을 켜면 설치 때 한 번, 그 뒤 90초마다 게더링으로 지속시간만큼 더해서(최대 300초) 실제로 꺼질 때 알려 드려요. 20레벨은 약 12분, 10레벨은 약 5분 50초 유지돼요.\n" +
                "아이템은 설치 1분 30초 뒤 사라져서, 65초에 연두색 빛으로 회수하라고 알려 드려요.",
            Preview = () => JanusAlert.Play(sc),
            Switches = new (string, bool, Action<bool>)[]
            {
                ("루나 익스텐션", st.LunaExt, v => st.LunaExt = v),
                ("5석펫 (90초마다 게더링)", st.PetOn, v => st.PetOn = v),
                ("아이템 회수 알림", st.PickupOn, v => st.PickupOn = v),
            },
        }, out var jk, out var js);
        janusKey = jk; janusStatus = js;
        y += janusCard.Height + S(10);

        var runeCard = MakeCard(lx, y, colW, new CardSpec
        {
            Color = Blue, Title = "룬", On = st.RuneOn, SetOn = v => st.RuneOn = v,
            KeyLabel = "NPC 대화 키", Which = 3,
            StepLabel = "룬 주기", Value = st.RuneMin, Options = Enumerable.Range(1, 60).ToArray(), Fmt = v => v + "분", SetValue = v => st.RuneMin = v,
            Hint = "평소 15분, 이벤트 땐 10분이에요. NPC 키를 누르고 방향키 4번을 누르면 세기 시작하고, 실패해서 다시 풀면 처음부터 다시 셉니다.",
            Preview = () => JanusWatch.PlayRune(sc),
        }, out var nk, out var rs);
        npcKey = nk; runeStatus = rs;
        y += runeCard.Height + S(10);
        int leftEnd = y;

        y = top + Section("버프", Orange, rx, top, colW);
        var buffTip = new Label
        {
            Text = "버프를 3개까지 따로 셀 수 있어요. 버프 키를 누르고 10초 안에 엔터까지 눌러야 세기 시작해요(칸마다 끌 수 있어요). 다시 쓰면 처음부터 다시 셉니다. 30분은 분홍, 20분은 주황, 10분은 노랑으로 알려 드려요.",
            Font = F(8.5f), ForeColor = Gray, AutoSize = true, MaximumSize = new Size(colW, 0), Location = new Point(rx, y),
        };
        Controls.Add(buffTip);
        y += buffTip.PreferredSize.Height + S(8);
        for (int i = 0; i < JanusWatch.BuffSlots; i++)
            y += MakeBuffCard(rx, y, colW, i).Height + S(10);
        int rightEnd = y;

        // 도핑은 세 번째 줄에 둔다
        int dx = rx + colW + gap;
        y = top + Section("부스트 도핑", JanusWatch.DopeMain, dx, top, colW);
        var dopeCard = MakeCard(dx, y, colW, new CardSpec
        {
            Color = JanusWatch.DopeMain, Title = "부스트 도핑", On = st.DopeOn, SetOn = v => st.DopeOn = v,
            KeyLabel = "도핑 사용 키", Which = 9,
            StepLabel = "지속시간", Value = st.DopeSec, Options = Enumerable.Range(3, 28).Select(i => i * 10).ToArray(), Fmt = v => v + "초", SetValue = v => st.DopeSec = v,
            Hint = "도핑 키 → 방향키 한 번 → 엔터를 10초 안에 누르면 쓴 것으로 보고, 사용 대기(기본 3초) 뒤부터 세요. 끝나면 빨간색으로 알려 드려요. 다시 쓰면 처음부터 다시 셉니다.",
            Preview = () => JanusWatch.PlayDope(sc),
            Switches = new (string, bool, Action<bool>)[] { ("사용 대기", st.DopeDelayOn, v => st.DopeDelayOn = v) },
            Step2 = ("대기 시간", st.DopeDelaySec, Enumerable.Range(1, 10).ToArray(), v => v + "초", v => st.DopeDelaySec = v),
        }, out var dk, out var ds);
        dopeKey = dk; dopeStatus = ds;
        y += dopeCard.Height + S(10);
        y = Math.Max(y, Math.Max(leftEnd, rightEnd));

        // 줄 사이 세로선
        for (int c = 1; c <= 2; c++)
            Controls.Add(new Panel { BackColor = Color.FromArgb(225, 225, 230), Bounds = new Rectangle(pad + colW * c + gap * (c - 1) + gap / 2, top + S(4), Math.Max(1, S(1)), y - top - S(14)) });
        }

        var foot = new Label
        {
            Text = "키 칸을 누른 뒤 원하는 키를 누르세요 · Del 지우기 · Esc 취소",
            Font = F(8.5f), ForeColor = Gray, AutoSize = true, MaximumSize = new Size(W - pad * 2, 0), Location = new Point(pad, y),
        };
        Controls.Add(foot);
        y += foot.PreferredSize.Height + S(14);

        ClientSize = new Size(W, y);
        refresh.Tick += (_, _) => UpdateStatus();
        refresh.Start();
        UpdateKeys();
        UpdateStatus();
    }

    /// <summary>알림 소리: 켜기 스위치와 소리 크기 (− 값 +). 크기를 바꾸면 그 크기로 한 번 들려준다.</summary>
    Card MakeSoundCard(int x, int y, int w)
    {
        var card = new Card { Location = new Point(x, y), Width = w, Radius = S(12) };
        Controls.Add(card);
        int ip = S(14), cy = S(12);

        var dot = new Label { Text = "●", ForeColor = UI.Accent, Font = F(9), AutoSize = true, Location = new Point(ip, cy + S(3)) };
        var name = new Label { Text = "알림 소리", Font = F(11, FontStyle.Bold), AutoSize = true, Location = new Point(ip + S(18), cy) };
        var sw = new Toggle { On = st.SoundOn, Size = new Size(S(40), S(22)) };
        sw.Location = new Point(w - ip - sw.Width, cy + S(1));
        card.Controls.AddRange(new Control[] { dot, name, sw });
        // 넓은 창에서는 한 줄에: 이름 … 소리 크기 − 값 + · 스위치
        bool wide = w > S(500);
        int right = wide ? sw.Left - S(18) : w - ip;
        if (wide) cy -= S(4); else cy += S(38);

        var volLbl = RowLabel("소리 크기", ip, cy);
        card.Controls.Add(volLbl);
        int vol = Math.Clamp((int)Math.Round(st.SoundVol / 10.0) * 10, 10, 100);
        var shown = ValueLabel(vol + "%");
        void Change(int d)
        {
            vol = Math.Clamp(vol + d, 10, 100);
            shown.Text = vol + "%";
            st.SoundVol = vol; st.Save();
            AlertSound.Play(AlertSound.Kind.Janus, vol);
        }
        var (minus, plus) = AddStepper(card, shown, right, cy, Change, 10);
        if (wide) volLbl.Left = minus.Left - volLbl.PreferredWidth - S(8);
        cy += S(38);

        // 끄면 크기 줄을 흐리게 해서 소리가 안 난다는 걸 보여 준다
        void Dim() { shown.ForeColor = st.SoundOn ? UI.Text : Gray; plus.Enabled = minus.Enabled = st.SoundOn; }
        sw.Changed += v => { st.SoundOn = v; st.Save(); Dim(); if (v) AlertSound.Play(AlertSound.Kind.Janus); };
        Dim();

        card.Height = cy;
        return card;
    }

    /// <summary>MakeCard에 넘기는 카드 내용. 칸 이름으로 적어서 무엇이 무엇인지 보이게 한다.</summary>
    sealed class CardSpec
    {
        public required Color Color { get; init; }
        public required string Title { get; init; }
        public required bool On { get; init; }
        public required Action<bool> SetOn { get; init; }
        /// <summary>키 줄 이름과 키 칸 번호 (capturing에 쓰는 번호)</summary>
        public required string KeyLabel { get; init; }
        public required int Which { get; init; }
        /// <summary>숫자 줄: 이름 · 저장된 값 · 선택지 · 표시 방법 · 저장 방법</summary>
        public required string StepLabel { get; init; }
        public required int Value { get; init; }
        public required int[] Options { get; init; }
        public required Func<int, string> Fmt { get; init; }
        public required Action<int> SetValue { get; init; }
        public required string Hint { get; init; }
        public required Action Preview { get; init; }
        public (string label, bool on, Action<bool> set)[]? Switches { get; init; }
        public (string label, int value, int[] options, Func<int, string> fmt, Action<int> set)? Step2 { get; init; }
    }

    Card MakeCard(int x, int y, int w, CardSpec spec, out RoundLabel keyPill, out Label status)
    {
        var (color, title, on, setOn) = (spec.Color, spec.Title, spec.On, spec.SetOn);
        var (keyLabel, which) = (spec.KeyLabel, spec.Which);
        var (stepLabel, value, options, fmt, setValue) = (spec.StepLabel, spec.Value, spec.Options, spec.Fmt, spec.SetValue);
        var (hint, preview, switches, step2) = (spec.Hint, spec.Preview, spec.Switches, spec.Step2);
        var card = new Card { Location = new Point(x, y), Width = w, Radius = S(12) };
        Controls.Add(card);
        int ip = S(14), cy = S(12);

        // 제목 줄: 색 점 · 이름 · 켜기 스위치
        var dot = new Label { Text = "●", ForeColor = color, Font = F(9), AutoSize = true, Location = new Point(ip, cy + S(3)) };
        var name = new Label { Text = title, Font = F(11, FontStyle.Bold), AutoSize = true, Location = new Point(ip + S(18), cy) };
        var sw = new Toggle { On = on, Size = new Size(S(40), S(22)) };
        sw.Location = new Point(w - ip - sw.Width, cy + S(1));
        sw.Changed += v => { setOn(v); st.Save(); UpdateStatus(); };
        card.Controls.AddRange(new Control[] { dot, name, sw });
        cy += S(38);

        // 키 줄
        card.Controls.Add(RowLabel(keyLabel, ip, cy));
        var pill = new RoundLabel
        {
            AutoSize = false, Size = new Size(S(150), S(30)), TextAlign = ContentAlignment.MiddleCenter,
            Fill = UI.HoverBack, Cursor = Cursors.Hand, Font = F(10, FontStyle.Bold), Radius = S(8),
        };
        pill.Location = new Point(w - ip - pill.Width, cy);
        pill.Click += (_, _) => { capturing = capturing == which ? 0 : which; UpdateKeys(); };
        card.Controls.Add(pill);
        keyPill = pill;
        cy += S(40);

        // 숫자 줄: − 값 +
        card.Controls.Add(RowLabel(stepLabel, ip, cy));
        // 저장된 값과 가장 가까운 선택지에서 시작한다
        int idx = Nearest(options, value);
        var shown = ValueLabel(fmt(options[idx]));
        if (options[idx] != value) { setValue(options[idx]); st.Save(); }
        void Change(int d)
        {
            idx = Math.Clamp(idx + d, 0, options.Length - 1);
            shown.Text = fmt(options[idx]);
            setValue(options[idx]); st.Save();
        }
        AddStepper(card, shown, w - ip, cy, Change);
        cy += S(38);

        // 켜고 끄는 줄 (바꾸면 값 표시도 다시 쓴다: 루나 익스텐션에 따라 120초 ↔ 무한대)
        foreach (var (label, on2, set) in switches ?? Array.Empty<(string, bool, Action<bool>)>())
        {
            card.Controls.Add(RowLabel(label, ip, cy));
            var t = new Toggle { On = on2, Size = new Size(S(40), S(22)) };
            t.Location = new Point(w - ip - t.Width, cy + S(4));
            t.Changed += v => { set(v); st.Save(); shown.Text = fmt(options[idx]); UpdateStatus(); };
            card.Controls.Add(t);
            cy += S(34);
        }

        // 두 번째 숫자 줄 (부스트 도핑의 사용 대기 초)
        if (step2 is var (l2, v2, o2, f2, s2))
        {
            card.Controls.Add(RowLabel(l2, ip, cy));
            int i2 = Nearest(o2, v2);
            var shown2 = ValueLabel(f2(o2[i2]));
            void Change2(int d) { i2 = Math.Clamp(i2 + d, 0, o2.Length - 1); shown2.Text = f2(o2[i2]); s2(o2[i2]); st.Save(); }
            AddStepper(card, shown2, w - ip, cy, Change2);
            cy += S(38);
        }

        var tip = new Label { Text = hint, Font = F(8.5f), ForeColor = Gray, AutoSize = true, MaximumSize = new Size(w - ip * 2, 0), Location = new Point(ip, cy) };
        card.Controls.Add(tip);
        cy += tip.PreferredSize.Height + S(10);

        // 상태 줄: 남은 시간 · 미리 보기
        var look = UI.Button("미리 보기", "", preview, sc, fore: color, back: Color.FromArgb(24, color));
        look.Font = F(9, FontStyle.Bold);
        card.Controls.Add(look);
        look.Location = new Point(w - ip - look.PreferredSize.Width, cy);
        var st2 = new Label { AutoSize = false, ForeColor = color, Font = F(9.5f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft,
            Location = new Point(ip, cy), Size = new Size(look.Left - ip - S(6), look.PreferredSize.Height) };
        card.Controls.Add(st2);
        status = st2;
        cy += look.PreferredSize.Height + S(12);

        card.Height = cy;
        return card;
    }

    /// <summary>네이버 웹툰 스크롤 키(위·아래)와 속도</summary>
    Card MakeScrollCard(int x, int y, int w, out RoundLabel up, out RoundLabel down)
    {
        var card = new Card { Location = new Point(x, y), Width = w, Radius = S(12) };
        Controls.Add(card);
        int ip = S(14), cy = S(12);
        card.Controls.Add(new Label { Text = "●", ForeColor = Green, Font = F(9), AutoSize = true, Location = new Point(ip, cy + S(3)) });
        card.Controls.Add(new Label { Text = "웹툰 키", Font = F(11, FontStyle.Bold), AutoSize = true, Location = new Point(ip + S(18), cy) });
        cy += S(38);

        RoundLabel Pill(string label, int which)
        {
            card.Controls.Add(RowLabel(label, ip, cy));
            var pill = new RoundLabel
            {
                AutoSize = false, Size = new Size(S(150), S(30)), TextAlign = ContentAlignment.MiddleCenter,
                Fill = UI.HoverBack, Cursor = Cursors.Hand, Font = F(10, FontStyle.Bold), Radius = S(8),
            };
            pill.Location = new Point(w - ip - pill.Width, cy);
            pill.Click += (_, _) => { capturing = capturing == which ? 0 : which; UpdateKeys(); };
            card.Controls.Add(pill);
            cy += S(40);
            return pill;
        }
        up = Pill("위로 키", 4);
        down = Pill("아래로 키", 5);
        nextKey = Pill("다음 화 키", 6);
        prevKey = Pill("이전 화 키", 7);
        shortsNextKey = Pill("쇼츠 다음 키", 10);
        shortsPrevKey = Pill("쇼츠 이전 키", 11);

        // 속도: − 값 +
        card.Controls.Add(RowLabel("속도", ip, cy));
        int[] speeds = { 300, 450, 600, 900, 1200, 1600, 2200, 3000 };
        int idx = Nearest(speeds, st.WebtoonSpeed);
        string Fmt(int i) => $"{i + 1}단계";
        var shown = ValueLabel(Fmt(idx));
        void Change(int d) { idx = Math.Clamp(idx + d, 0, speeds.Length - 1); shown.Text = Fmt(idx); st.WebtoonSpeed = speeds[idx]; st.Save(); }
        AddStepper(card, shown, w - ip, cy, Change);
        cy += S(38);

        var tip = new Label
        {
            Text = "바 메뉴의 '웹툰'으로 연 창에서, 키를 누르고 있는 동안 부드럽게 쭉 내려가거나 올라가요. 다음 화·이전 화 키는 한 번 누르면 한 화씩 넘어가요. " +
                   "웹툰 창이나 메이플 창이 앞에 있을 때 동작해요. 유료분은 '네이버 로그인'으로 직접 로그인해서 보세요.\n" +
                   "쇼츠 키는 바 메뉴의 '유튜브' 재생 창에 쇼츠가 떠 있을 때, 한 번 누르면 다음·이전 쇼츠로 넘어가요. 재생 창이나 메이플 창이 앞에 있을 때 동작해요.",
            Font = F(8.5f), ForeColor = Gray, AutoSize = true, MaximumSize = new Size(w - ip * 2, 0), Location = new Point(ip, cy),
        };
        card.Controls.Add(tip);
        cy += tip.PreferredSize.Height + S(12);
        card.Height = cy;
        return card;
    }

    /// <summary>묶음 제목: 색 막대 · 굵은 이름 · 아래 얇은 선. 차지한 높이를 돌려준다.</summary>
    int Section(string text, Color color, int x, int y, int w)
    {
        var bar = new Panel { BackColor = color, Bounds = new Rectangle(x, y + S(3), S(4), S(18)) };
        var lbl = new Label { Text = text, Font = F(11.5f, FontStyle.Bold), ForeColor = UI.Text, AutoSize = true, Location = new Point(x + S(10), y) };
        var line = new Panel { BackColor = Color.FromArgb(70, color), Bounds = new Rectangle(x, y + S(28), w, Math.Max(1, S(1))) };
        lbl.MouseDown += (_, e) => Drag(e);
        Controls.AddRange(new Control[] { bar, lbl, line });
        return S(38);
    }

    /// <summary>버프 상태 글씨 색 (노란 글씨는 흰 바탕에서 잘 안 보여서 조금 진하게)</summary>
    static Color BuffText(int min) => min >= 20 ? JanusWatch.BuffColors(min).main : Color.FromArgb(205, 160, 0);

    /// <summary>버프 한 칸: 이름·켜기 / 키·시간(10·20·30분) / 남은 시간·미리 보기</summary>
    Card MakeBuffCard(int x, int y, int w, int i)
    {
        var b = st.Buffs[i];
        var card = new Card { Location = new Point(x, y), Width = w, Radius = S(12) };
        Controls.Add(card);
        int ip = S(14), cy = S(12);

        var dot = new Label { Text = "●", Font = F(9), AutoSize = true, Location = new Point(ip, cy + S(3)) };
        // 이름: 누르면 그 자리에서 바꾼다 (비우면 기본 이름 "버프 1")
        string Shown() => string.IsNullOrWhiteSpace(b.Name) ? $"버프 {i + 1}" : b.Name.Trim();
        var name = new Label { Text = Shown(), Font = F(11, FontStyle.Bold), AutoSize = true, Location = new Point(ip + S(18), cy), Cursor = Cursors.IBeam };
        var pen = new Label { Text = "✎", Font = F(9), ForeColor = Gray, AutoSize = true, Cursor = Cursors.Hand };
        var sw = new Toggle { On = b.On, Size = new Size(S(40), S(22)) };
        sw.Location = new Point(w - ip - sw.Width, cy + S(1));
        sw.Changed += v => { b.On = v; st.Save(); UpdateStatus(); };
        void PlacePen() => pen.Location = new Point(name.Right + S(4), cy + S(3));
        PlacePen();
        var box = new TextBox
        {
            Font = F(10.5f, FontStyle.Bold), MaxLength = 12, Visible = false, BorderStyle = BorderStyle.FixedSingle,
            Location = new Point(ip + S(16), cy - S(1)), Width = sw.Left - ip - S(16) - S(10),
            PlaceholderText = $"버프 {i + 1}",
        };
        bool done = false;
        void Edit()
        {
            done = false;
            box.Text = b.Name ?? "";
            name.Visible = pen.Visible = false;
            box.Visible = true; box.Focus(); box.SelectAll();
        }
        void Finish(bool keep)
        {
            if (done) return;
            done = true;
            if (keep) { b.Name = string.IsNullOrWhiteSpace(box.Text) ? null : box.Text.Trim(); st.Save(); }
            name.Text = Shown(); PlacePen();
            box.Visible = false; name.Visible = pen.Visible = true;
            editingName = null;
        }
        name.Click += (_, _) => Edit();
        pen.Click += (_, _) => Edit();
        box.Enter += (_, _) => editingName = Finish;
        box.Leave += (_, _) => Finish(true);
        card.Controls.AddRange(new Control[] { dot, name, pen, box, sw });
        cy += S(38);

        // 키 칸 (왼쪽) · 시간 − 값 + (오른쪽)
        var keyLbl = RowLabel("키", ip, cy);
        card.Controls.Add(keyLbl);
        var pill = new RoundLabel
        {
            AutoSize = false, Size = new Size(S(112), S(30)), TextAlign = ContentAlignment.MiddleCenter,
            Fill = UI.HoverBack, Cursor = Cursors.Hand, Font = F(10, FontStyle.Bold), Radius = S(8),
            Location = new Point(ip + S(26), cy),
        };
        int which = 20 + i;
        pill.Click += (_, _) => { capturing = capturing == which ? 0 : which; UpdateKeys(); };
        card.Controls.Add(pill);
        buffKeys[i] = pill;

        int[] options = { 10, 20, 30 };
        int idx = Array.IndexOf(options, b.Min);
        if (idx < 0) { idx = 2; b.Min = 30; st.Save(); }
        var shown = ValueLabel("", 56);
        RoundLabel? look = null;
        void Paint()
        {
            var c = JanusWatch.BuffColors(b.Min).main;
            shown.Text = b.Min + "분";
            dot.ForeColor = c;
            if (look != null) { look.ForeColor = c; look.Fill = Color.FromArgb(24, c); }
        }
        void Change(int d)
        {
            idx = Math.Clamp(idx + d, 0, options.Length - 1);
            b.Min = options[idx]; st.Save();
            Paint(); UpdateStatus();
        }
        AddStepper(card, shown, w - ip, cy, Change);
        cy += S(40);

        // 엔터 기다리기: 켜면 키 다음 엔터까지 눌러야 세고, 끄면 키만 눌러도 센다
        card.Controls.Add(RowLabel("키 다음 엔터까지 눌러야 세기", ip, cy - S(2)));
        var waitSw = new Toggle { On = b.WaitEnter, Size = new Size(S(40), S(22)) };
        waitSw.Location = new Point(w - ip - waitSw.Width, cy + S(2));
        waitSw.Changed += v => { b.WaitEnter = v; st.Save(); UpdateStatus(); };
        card.Controls.Add(waitSw);
        cy += S(34);

        // 상태 줄: 남은 시간 · 미리 보기
        var c0 = JanusWatch.BuffColors(b.Min).main;
        look = UI.Button("미리 보기", "", () => JanusWatch.PlayBuff(sc, b.Min, b.Name), sc, fore: c0, back: Color.FromArgb(24, c0));
        look.Font = F(9, FontStyle.Bold);
        card.Controls.Add(look);
        look.Location = new Point(w - ip - look.PreferredSize.Width, cy);
        var status = new Label { AutoSize = false, Font = F(9.5f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft,
            Location = new Point(ip, cy), Size = new Size(look.Left - ip - S(6), look.PreferredSize.Height) };
        card.Controls.Add(status);
        buffStatus[i] = status;
        cy += look.PreferredSize.Height + S(12);
        Paint();

        card.Height = cy;
        return card;
    }

    // "− 값 +" 줄의 값 칸
    Label ValueLabel(string text, int width = 64) =>
        new() { AutoSize = false, Size = new Size(S(width), S(30)), TextAlign = ContentAlignment.MiddleCenter, Font = F(10, FontStyle.Bold), Text = text };

    /// <summary>"− 값 +" 줄: 값 칸 양쪽에 − · + 버튼을 붙여 오른쪽 끝(right)에 맞춰 놓는다. 누르면 change(±step).</summary>
    (RoundLabel minus, RoundLabel plus) AddStepper(Control card, Label shown, int right, int cy, Action<int> change, int step = 1)
    {
        var plus = UI.Button("+", "", () => change(step), sc, back: UI.HoverBack);
        var minus = UI.Button("−", "", () => change(-step), sc, back: UI.HoverBack);
        plus.Font = minus.Font = F(10, FontStyle.Bold);
        card.Controls.AddRange(new Control[] { minus, shown, plus });
        int top = cy + (S(30) - plus.PreferredSize.Height) / 2;
        plus.Location = new Point(right - plus.PreferredSize.Width, top);
        shown.Location = new Point(plus.Left - shown.Width, cy);
        minus.Location = new Point(shown.Left - minus.PreferredSize.Width, top);
        return (minus, plus);
    }

    /// <summary>저장된 값과 가장 가까운 선택지의 번호 (같으면 앞쪽)</summary>
    static int Nearest(int[] options, int value)
    {
        int idx = 0;
        for (int i = 1; i < options.Length; i++)
            if (Math.Abs(options[i] - value) < Math.Abs(options[idx] - value)) idx = i;
        return idx;
    }

    Label RowLabel(string text, int x, int y) =>
        new() { Text = text, AutoSize = true, ForeColor = Color.FromArgb(80, 80, 80), Location = new Point(x, y + S(5)) };

    void UpdateKeys()
    {
        Style(janusKey, 1, st.JanusKey);
        Style(npcKey, 3, st.NpcKey);
        Style(upKey, 4, st.WebtoonUpKey);
        Style(downKey, 5, st.WebtoonDownKey);
        Style(nextKey, 6, st.WebtoonNextKey);
        Style(prevKey, 7, st.WebtoonPrevKey);
        Style(shortsNextKey, 10, st.ShortsNextKey);
        Style(shortsPrevKey, 11, st.ShortsPrevKey);
        for (int i = 0; i < JanusWatch.BuffSlots; i++) Style(buffKeys[i], 20 + i, st.Buffs[i].Key);
        Style(dopeKey, 9, st.DopeKey);
    }

    void Style(RoundLabel? pill, int which, int vk)
    {
        if (pill == null) return;
        bool wait = capturing == which;
        pill.Text = wait ? Waiting : JanusWatch.KeyName(vk);
        pill.Fill = wait ? UI.SoftAccent : UI.HoverBack;
        pill.ForeColor = wait ? UI.Accent : vk == 0 ? Gray : UI.Text;
    }

    void UpdateStatus()
    {
        bool recent = watch.LastKey is DateTime k && (DateTime.Now - k).TotalSeconds < 3;
        if (janusStatus != null && runeStatus != null)
        {

        if (!st.JanusOn) janusStatus.Text = "꺼져 있어요";
        else if (JanusWatch.Endless(st)) janusStatus.Text = "무한대";
        else if (st.JanusKey == 0) janusStatus.Text = "키를 정해 주세요";
        else if (watch.Remaining is double r) janusStatus.Text = $"남은 시간 {(int)r / 60}:{(int)r % 60:00}" + (recent && watch.LastKind == "설치" ? " · 인식됨" : "");
        else janusStatus.Text = "키를 누르면 세기 시작해요";

        if (!st.RuneOn) runeStatus.Text = "꺼져 있어요";
        else if (st.NpcKey == 0) runeStatus.Text = "NPC 대화 키를 정해 주세요";
        else if (watch.RuneProgress >= 0) runeStatus.Text = $"방향키 {watch.RuneProgress} / 4";
        else if (watch.RuneRemaining is double rr) runeStatus.Text = $"다음 룬까지 {(int)rr / 60}:{(int)rr % 60:00}" + (recent && watch.LastKind == "룬 해방" ? " · 인식됨" : "");
        else runeStatus.Text = "NPC 키 + 방향키 4번을 기다려요";

        foreach (var l in new[] { janusStatus, runeStatus })
            if (l.Text == "꺼져 있어요") l.ForeColor = Gray;
            else l.ForeColor = l == janusStatus ? Purple : Blue;
        }

        for (int i = 0; i < JanusWatch.BuffSlots; i++)
        {
            var bl = buffStatus[i];
            if (bl == null) return;
            var b = st.Buffs[i];
            if (!b.On) bl.Text = "꺼져 있어요";
            else if (b.Key == 0) bl.Text = "버프 키를 정해 주세요";
            else if (watch.BuffWaitingEnter(i)) bl.Text = "키 인식됨 · 엔터를 누르세요";
            else if (watch.BuffRemaining(i) is double br) bl.Text = $"남은 시간 {(int)br / 60}:{(int)br % 60:00}" + (recent && watch.LastKind == "버프" && watch.LastBuff == i ? " · 인식됨" : "");
            else bl.Text = b.WaitEnter && b.Key != 0x0D ? "키 다음 엔터를 기다려요" : "버프 키를 기다려요";
            bl.ForeColor = b.On ? BuffText(b.Min) : Gray;
        }

        if (dopeStatus == null) return;
        if (!st.DopeOn) dopeStatus.Text = "꺼져 있어요";
        else if (st.DopeKey == 0) dopeStatus.Text = "도핑 사용 키를 정해 주세요";
        else if (watch.DopeProgress == 0) dopeStatus.Text = "도핑 키 인식됨 · 방향키 한 번";
        else if (watch.DopeProgress == 1) dopeStatus.Text = "방향키 인식됨 · 엔터를 누르세요";
        else if (watch.DopeProgress > 1) dopeStatus.Text = "방향키를 한 번만 눌러야 해요";
        else if (watch.DopeWaiting is double dw) dopeStatus.Text = $"사용 대기 {Math.Ceiling(dw)}초";
        else if (watch.DopeRemaining is double dr) dopeStatus.Text = $"남은 시간 {(int)dr / 60}:{(int)dr % 60:00}" + (recent && watch.LastKind == "도핑" ? " · 인식됨" : "");
        else dopeStatus.Text = "도핑 키 → 방향키 → 엔터를 기다려요";
        dopeStatus.ForeColor = st.DopeOn ? JanusWatch.DopeMain : Gray;
    }

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
                int vk = k is Keys.Delete or Keys.Back ? 0 : (int)k;
                switch (capturing)
                {
                    case 1: st.JanusKey = vk; break;
                    case 3: st.NpcKey = vk; break;
                    case 4: st.WebtoonUpKey = vk; break;
                    case 5: st.WebtoonDownKey = vk; break;
                    case 6: st.WebtoonNextKey = vk; break;
                    case 7: st.WebtoonPrevKey = vk; break;
                    case 10: st.ShortsNextKey = vk; break;
                    case 11: st.ShortsPrevKey = vk; break;
                    case >= 20 and < 20 + JanusWatch.BuffSlots: st.Buffs[capturing - 20].Key = vk; break;
                    case 9: st.DopeKey = vk; break;
                }
                st.Save();
            }
            capturing = 0;
            UpdateKeys();
            UpdateStatus();
            return true;
        }
        // 버프 이름을 고치는 중: 엔터는 저장, Esc는 취소
        if (editingName is Action<bool> fin && k is Keys.Enter or Keys.Escape)
        {
            fin(k == Keys.Enter);
            ActiveControl = null;
            return true;
        }
        if (k == Keys.Escape || keyData == (Keys.Control | Keys.W)) { Close(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ClassStyle |= CS_DROPSHADOW; return cp; } // 그림자
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

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UI.RoundCorners(Handle);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        refresh.Stop();
        base.OnFormClosed(e);
    }

    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);

    void Drag(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        ReleaseCapture();
        SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero); // 제목줄을 잡은 것처럼 끈다
    }

    /// <summary>둥근 테두리 카드</summary>
    sealed class Card : Panel
    {
        public int Radius { get; set; } = 12;
        public Card() { DoubleBuffered = true; ResizeRedraw = true; BackColor = Color.White; }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = UI.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Radius);
            using var pen = new Pen(UI.Line);
            e.Graphics.DrawPath(pen, path);
        }
    }

    /// <summary>켜고 끄는 알약 스위치 (켜면 주황)</summary>
    sealed class Toggle : Control
    {
        bool on;
        public bool On { get => on; set { on = value; Invalidate(); } }
        public event Action<bool>? Changed;
        public Toggle()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.White;
            Cursor = Cursors.Hand;
        }
        protected override void OnClick(EventArgs e) { On = !On; Changed?.Invoke(On); base.OnClick(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = UI.RoundRect(new RectangleF(0, 0, Width - 1, Height - 1), (Height - 1) / 2f))
            using (var b = new SolidBrush(on ? UI.Accent : Color.FromArgb(210, 210, 210)))
                g.FillPath(b, path);
            float p = Height / 7f, d = Height - 1 - p * 2;
            using var knob = new SolidBrush(Color.White);
            g.FillEllipse(knob, on ? Width - 1 - p - d : p, p, d, d);
        }
    }
}
