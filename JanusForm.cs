using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

using static MapleBar.Native;

namespace MapleBar;
/// <summary>솔 야누스 · 룬 알림 설정 창. 바와 같은 흰 바탕, 둥근 카드, 주황 스위치를 쓴다.</summary>
sealed class JanusForm : Form, IMessageFilter
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
        open.Location = WinPlace.Where(st, open.PlaceKey, open, owner, sc);
        if (!open.Visible) open.Show();
        open.Activate();
    }

    static readonly Color Purple = Color.FromArgb(135, 80, 235);
    static readonly Color Blue = Color.FromArgb(20, 150, 220);
    static Color Gray => UI.T(Color.FromArgb(135, 135, 135)); // 모드마다 색이 달라 속성으로
    const string Waiting = "키를 누르세요…";

    readonly Settings st;
    readonly JanusWatch watch;
    readonly float sc;
    // 알림 창에는 야누스·룬 칸만, 웹툰 키 창에는 웹툰 칸만 있다 (없는 칸은 null)
    readonly bool toon;
    readonly RoundLabel? janusKey, npcKey, upKey, downKey;
    RoundLabel? erdaKey;
    RoundLabel? nextKey, prevKey, shortsNextKey, shortsPrevKey, comboKey;
    // 도핑 칸마다: 키 칸, 상태 줄
    readonly RoundLabel?[] dopeKeys = new RoundLabel?[JanusWatch.DopeSlots];
    readonly Label?[] dopeStatus = new Label?[JanusWatch.DopeSlots];
    // 버프·도핑은 한 번에 한 칸만 보이고 ◀ ▶로 넘긴다
    readonly Card?[] buffCards = new Card?[JanusWatch.BuffSlots], dopeCards = new Card?[JanusWatch.DopeSlots];
    int buffPage, dopePage;
    readonly List<Action> slotNavs = new(); // 넘기기 줄의 글과 화살표를 다시 그리는 함수들
    // 버프 칸마다: 키 칸, 상태 줄 (버프 창이 아니면 비어 있다)
    readonly RoundLabel?[] buffKeys = new RoundLabel?[JanusWatch.BuffSlots];
    readonly Label?[] buffStatus = new Label?[JanusWatch.BuffSlots];
    // 버프 칸마다: "키 누른 때" · "엔터 누른 때" 고르기
    readonly RoundLabel?[] buffUseKey = new RoundLabel?[JanusWatch.BuffSlots], buffUseEnter = new RoundLabel?[JanusWatch.BuffSlots];
    Action<bool>? editingName; // 버프 이름 칸을 고치는 중이면 끝내는 함수 (true = 저장)
    static readonly Color Orange = Color.FromArgb(255, 140, 0);
    static readonly Color Green = Color.FromArgb(0, 190, 90);
    readonly Label? janusStatus, runeStatus;
    Label? erdaStatus;
    readonly System.Windows.Forms.Timer refresh = new() { Interval = 250 };
    int capturing;
    bool captureCombo; // 키를 받는 중에 조합 키를 먼저 눌렀다
    int captureMod; // 야누스 키를 받는 중에 먼저 누른 Shift·Ctrl·Alt (다음 키와 같이 쓴다)
    readonly Dictionary<int, Toggle> comboSws = new(); // 칸 번호(capturing 값) → 그 칸의 조합 키 스위치 // 1 = 솔 야누스 키, 3 = NPC 대화 키, 4 = 웹툰 위로, 5 = 웹툰 아래로 키를 받는 중, 30~ = 반복 알림 키

    // 알림 창은 두 페이지: 1 = 야누스·룬·버프·도핑·정펜, 2 = 반복 주기 알림 · 알림 띄울 모니터
    readonly List<Control>[] pages = { new(), new() };
    readonly int[] pageEnd = new int[2];
    const int PageCount = 2;
    readonly List<(RoundLabel prev, RoundLabel label, RoundLabel next)> pageNavs = new();
    Label? foot;
    int page;
    readonly List<Action> offAll = new(); // '알림 모두 끄기'가 부를 끄기 동작들
    readonly RoundLabel?[] repeatKeys = new RoundLabel?[JanusWatch.RepeatSlots];
    readonly Label?[] repeatStatus = new Label?[JanusWatch.RepeatSlots];
    readonly RoundLabel?[] onceKeys = new RoundLabel?[JanusWatch.OnceSlots];
    readonly Label?[] onceStatus = new Label?[JanusWatch.OnceSlots];
    readonly Card?[] repeatCards = new Card?[JanusWatch.RepeatSlots], onceCards = new Card?[JanusWatch.OnceSlots];
    int repeatPage, oncePage;

    void ShowPage(int p)
    {
        page = p;
        if (capturing != 0) { capturing = 0; UpdateKeys(); }
        // 새 페이지를 먼저 보이고 이전 페이지를 숨긴다
        foreach (var c in pages[p]) c.Visible = true;
        foreach (var c in pages[1 - p]) c.Visible = false;
        ApplySlotPages(); // 1페이지를 다 보이게 했으니 버프·도핑은 고른 칸만 다시 남긴다
        foreach (var (prev, label, next) in pageNavs)
        {
            label.Text = $"{p + 1} / {PageCount} 페이지";
            // 더 갈 곳이 없는 화살표는 흐리게
            foreach (var (b, ok) in new[] { (prev, p > 0), (next, p < PageCount - 1) })
            {
                // 다크 모드: 갈색 칸 대신 조금 밝은 차콜 칸에 호박색 화살표 (최근 검색 칩 글씨와 같은 색)
                b.Fill = ok ? (UI.Dark ? Color.FromArgb(80, 86, 94) : UI.SoftAccent) : UI.HoverBack;
                b.ForeColor = ok ? UI.AccentText : UI.T(Color.FromArgb(190, 190, 196));
                b.Cursor = ok ? Cursors.Hand : Cursors.Default;
            }
        }
        if (foot == null) return;
        AutoScrollPosition = Point.Empty;
        int y = pageEnd[p];
        if (presetPanel != null)
        {
            // 프리셋 칸은 그 페이지 알림 칸 끝까지 늘린다 (너무 짧으면 최소 높이)
            presetPanel.Height = Math.Max(S(300), y - S(10) - presetPanel.Top);
            LayoutPresetPanel();
            y = Math.Max(y, presetPanel.Bottom + S(10));
        }
        foot.Location = new Point(foot.Left, y);
        y += foot.PreferredSize.Height + S(14);
        var wa = Screen.FromControl(this).WorkingArea;
        AutoScroll = false;
        ClientSize = new Size(ClientSize.Width, y);
        if (Height > wa.Height) { AutoScroll = true; Height = wa.Height; }
        if (Visible && Bottom > wa.Bottom) Top = Math.Max(wa.Top, wa.Bottom - Height);
        UpdateStatus();
    }

    int S(float v) => UI.Scale(v, sc);
    static Font F(float size, FontStyle style = FontStyle.Regular) => new("맑은 고딕", size * UI.Z, style);

    JanusForm(Settings st, JanusWatch watch, float sc, bool toon, bool buff)
    {
        this.st = st; this.watch = watch; this.sc = sc; this.toon = toon;
        Text = toon ? "웹툰·쇼츠 키" : "알림 설정";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = UI.Back;
        ForeColor = UI.Text;
        Font = F(10);
        DoubleBuffered = true;

        // 알림 창은 세 줄(야누스·룬 | 버프 | 도핑)로 옆으로 펼쳐 세로를 줄인다
        // 웹툰 키 창은 두 칸(웹툰 | 쇼츠)으로 나란히 놓는다
        int pad = S(18), toonW = S(336), toonGap = S(16);
        int W = toon ? pad * 2 + toonW * 2 + toonGap : pad * 2 + S(352) * 3 + S(28) * 2, y = S(14);
        // 알림 창은 오른쪽에 프리셋 칸을 더 둔다. contentR = 알림 칸들이 닿는 오른쪽 끝
        int contentR = W - pad, presetW = S(220), presetGap = S(20);
        if (!toon) W += presetW + presetGap;

        // 머리: 제목과 닫기. 빈 곳을 끌면 창이 움직인다
        var title = new Label { Text = toon ? "웹툰 · 쇼츠 키" : "알림 설정", Font = F(13, FontStyle.Bold), AutoSize = true, Location = new Point(pad, y) };
        title.MouseDown += (_, e) => Drag(e);
        MouseDown += (_, e) => Drag(e);
        var close = UI.Button("✕", "닫기", Close, sc);
        close.Font = F(10);
        Controls.Add(title);
        Controls.Add(close);
        close.Location = new Point(W - pad - close.PreferredSize.Width + S(6), y + S(2));
        if (!toon)
        {
            // 모든 페이지의 알림 켜기 스위치를 한 번에 끈다 (소리·세부 스위치는 그대로)
            var allOff = UI.Button("알림 모두 끄기", "", () => { foreach (var off in offAll) off(); st.Save(); UpdateStatus(); }, sc, null, UI.HoverBack);
            allOff.Radius = S(6);
            Controls.Add(allOff);
            allOff.Location = new Point(close.Left - S(8) - allOff.PreferredWidth, y + S(2));
            // 전체 알림 켜기: 끄면 각 알림 스위치는 그대로 두고 아무 알림도 세지 않는다
            var allSw = new Toggle { On = st.AlertsOn, Size = new Size(S(40), S(22)) };
            allSw.Location = new Point(allOff.Left - S(12) - allSw.Width, allOff.Top + (allOff.PreferredSize.Height - allSw.Height) / 2);
            var allLabel = new Label { Text = "전체 알림", Font = F(9.5f, FontStyle.Bold), AutoSize = true, ForeColor = UI.Text };
            allLabel.Location = new Point(allSw.Left - S(6) - allLabel.PreferredWidth, allOff.Top + (allOff.PreferredSize.Height - allLabel.PreferredHeight) / 2);
            allSw.Changed += v => { st.AlertsOn = v; st.Save(); UpdateStatus(); };
            // 바의 OFF 칩을 눌러 다시 켜면 이 스위치도 따라간다
            liveHints.Add(() => { if (allSw.On != st.AlertsOn) allSw.On = st.AlertsOn; });
            Controls.Add(allSw); Controls.Add(allLabel);
            // 1 · 2 페이지 넘기기는 머리 줄 가운데에 하나만 둔다 (페이지를 넘겨도 그 자리)
            int navW = S(230);
            MakePageTabs((pad + contentR - navW) / 2, y, navW);
        }
        y += S(34);
        var sub = new Label
        {
            Text = toon ? "웹툰·유튜브 창이나 메이플 창에서 누른 키만 읽어요. 게임에는 아무것도 보내지 않아요."
                        : "메이플 창에서 누른 키만 읽어요. 게임에는 아무것도 보내지 않아요.",
            Font = F(9), ForeColor = Gray, AutoSize = true, MaximumSize = new Size(W - pad * 2, 0), Location = new Point(pad, y),
        };
        sub.MouseDown += (_, e) => Drag(e);
        Controls.Add(sub);
        y += sub.PreferredSize.Height + S(12);

        if (!toon)
        {
            // 알림 소리 · 조합 키 · 화면 알림은 왼쪽에 좁게 모으고, 남은 자리에 알림 방식
            // 프리셋 칸은 두 페이지에 다 보이게 페이지 목록 밖에 둔다 (높이는 ShowPage에서 맞춘다)
            presetPanel = MakePresetPanel(contentR + presetGap, y, presetW);
            int g10 = S(10), sx = pad;
            var sound = MakeSoundCard(sx, y, S(260)); sx += sound.Width + g10;
            var combo = MakeComboCard(sx, y, S(270)); sx += combo.Width + g10;
            var visual = MakeVisualCard(sx, y, S(220), out var visualSw); sx += visual.Width + g10;
            var method = MakeMethodCard(sx, y, contentR - sx, visualSw);
            int h4 = new[] { sound.Height, combo.Height, visual.Height, method.Height }.Max();
            sound.Height = combo.Height = visual.Height = method.Height = h4;
            y += h4 + S(10);
        }
        int pageStart = Controls.Count; // 여기부터 1페이지 칸

        if (toon)
        {
            var toonCard = MakeScrollCard(pad, y, toonW, out var u, out var d);
            upKey = u; downKey = d;
            var shortsCard = MakeShortsCard(pad + toonW + toonGap, y, toonW);
            int h = Math.Max(toonCard.Height, shortsCard.Height);
            toonCard.Height = shortsCard.Height = h; // 두 칸 높이를 맞춘다
            y += h + S(10);
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
            StepLabel = "알림 주기", MinSec = true, Value = JanusWatch.Cycle(st), SetValue = v => st.JanusCycleSec = v,
            Hint = "줄\n줄",
            More =
                "야누스 키를 누르면 그때부터 알림 주기만큼 세고 알려 드려요. 다시 깔면 처음부터 다시 셉니다.\n" +
                "처음 깐 뒤 50초 안에 또 깐 건 세지 않아요. 0분 0초면 세지 않아요.\n" +
                "다시 알리기를 켜면 알림 뒤에도 안 깔았을 때 정한 초 뒤에 한 번 더 알려요.\n" +
                "아이템은 깔고 1분 30초 뒤 사라져요. 65초에 연두색 빛으로 주우라고 알려 드려요.",
            Preview = () => JanusAlert.Play(sc),
            Switches = new (string, bool, Action<bool>)[]
            {
                ("다시 알리기", st.JanusRealertOn, v => st.JanusRealertOn = v),
                ("아이템 회수 알림", st.PickupOn, v => st.PickupOn = v),
            },
            Step2 = ("다시 알리기", st.JanusRealertSec, Enumerable.Range(1, 12).Select(n => n * 5).ToArray(), v => v + "초", v => st.JanusRealertSec = v),
            LiveHint = () =>
            {
                int c = JanusWatch.Cycle(st);
                if (c <= 0) return "알림 주기를 넣어 주세요.";
                string t = (c / 60 > 0 ? $"{c / 60}분 " : "") + (c % 60 > 0 || c < 60 ? $"{c % 60}초 " : "");
                return $"{t}뒤에 알림을 받습니다." + (st.JanusRealertOn ? $"\n그로부터 {st.JanusRealertSec}초 뒤, 다시 알림을 받습니다." : "");
            },
        }, out var jk, out var js);
        janusKey = jk; janusStatus = js;
        y += janusCard.Height + S(10);

        var runeCard = MakeCard(lx, y, colW, new CardSpec
        {
            Color = Blue, Title = "룬", On = st.RuneOn, SetOn = v => st.RuneOn = v,
            KeyLabel = "NPC 대화 키", Which = 3,
            StepLabel = "룬 주기", Value = st.RuneMin, Options = Enumerable.Range(1, 60).ToArray(), Fmt = v => v + "분", SetValue = v => st.RuneMin = v,
            Hint = "NPC 키 + 방향키 4번이면 세기 시작해요.",
            More = "평소 15분, 이벤트 땐 10분이에요. NPC 키를 누르고 방향키 4번을 누르면 세기 시작하고, 실패해서 다시 풀면 처음부터 다시 셉니다.",
            Preview = () => JanusWatch.PlayRune(sc),
        }, out var nk, out var rs);
        npcKey = nk; runeStatus = rs;
        y += runeCard.Height + S(10);
        int leftEnd = y;

        y = top + Section("버프", Orange, rx, top, colW);
        var buffTip = new Label
        {
            Text = "버프 키를 누르면 세요 · 30분 분홍 · 20분 주황 · 10분 노랑",
            Font = F(8.5f), ForeColor = Gray, AutoSize = true, MaximumSize = new Size(colW, 0), Location = new Point(rx, y), Cursor = Cursors.Help,
        };
        new ToolTip { AutoPopDelay = 30000 }.SetToolTip(buffTip,
            "버프를 3개까지 따로 세요. 버프 키를 누르면 그때부터 세고, 10초 안에 엔터도 누르면 엔터 때부터도 같이 세요.\n" +
            "'알릴 시간'에서 둘 중 하나를 고르고, 나중에 바꿔도 돼요. 다시 쓰면 처음부터 다시 세요.\n" +
            "30분은 분홍, 20분은 주황, 10분은 노랑으로 알려 드려요.");
        Controls.Add(buffTip);
        y += buffTip.PreferredSize.Height + S(8);
        // 버프 3칸은 같은 자리에 겹쳐 두고 ◀ ▶로 한 칸씩 본다
        y += MakeSlotNav(rx, y, colW, JanusWatch.BuffSlots, () => buffPage, v => buffPage = v, BuffName, i => BuffText(st.Buffs[i].Min)) + S(10);
        int buffH = 0;
        for (int i = 0; i < JanusWatch.BuffSlots; i++)
            buffH = Math.Max(buffH, (buffCards[i] = MakeBuffCard(rx, y, colW, i)).Height);
        y += buffH + S(10);
        // 정펜 확인은 고른 버프가 돌 때 보니까 버프 아래에 둔다
        y += MakePendantCard(rx, y, colW).Height + S(10);
        int rightEnd = y;

        // 도핑은 세 번째 줄에 둔다: 3칸을 ◀ ▶로 넘긴다
        int dx = rx + colW + gap;
        y = top + Section("부스트 도핑", JanusWatch.DopeMain, dx, top, colW);
        y += MakeSlotNav(dx, y, colW, JanusWatch.DopeSlots, () => dopePage, v => dopePage = v, DopeName, i => JanusWatch.DopeColors(i).main) + S(10);
        int dopeH = 0;
        for (int i = 0; i < JanusWatch.DopeSlots; i++)
        {
            int slot = i;
            var d = st.Dopes[i];
            var dopeCard = MakeCard(dx, y, colW, new CardSpec
            {
                Color = JanusWatch.DopeColors(i).main, Title = JanusWatch.DopeDefaultName(i), On = d.On, SetOn = v => d.On = v,
                KeyLabel = "도핑 사용 키", Which = 40 + i,
                StepLabel = "지속시간", Value = d.Sec, Options = Enumerable.Range(3, 28).Select(n => n * 10).ToArray(), Fmt = v => v + "초", SetValue = v => d.Sec = v,
                Hint = "도핑 키 → 방향키 → 엔터를 누르면 세기 시작해요.",
                More = "도핑 키 → 방향키 한 번 → 엔터를 10초 안에 누르면 쓴 것으로 보고, 사용 대기(기본 3초) 뒤부터 세요. 끝나면 이 칸 색으로 알려 드려요. 다시 쓰면 처음부터 다시 셉니다.",
                Preview = () => JanusWatch.PlayDope(sc, slot, d.Name),
                Switches = new (string, bool, Action<bool>)[]
                {
                    ("사용 대기", d.DelayOn, v => d.DelayOn = v),
                    ("조합 키 누른 채로 쓰기", d.Combo, v => d.Combo = v),
                },
                Step2 = ("대기 시간", d.DelaySec, Enumerable.Range(1, 10).ToArray(), v => v + "초", v => d.DelaySec = v),
                GetName = () => d.Name, SetName = v => d.Name = v,
            }, out var dk, out var ds);
            dopeKeys[i] = dk; dopeStatus[i] = ds; dopeCards[i] = dopeCard;
            dopeH = Math.Max(dopeH, dopeCard.Height);
        }
        y += dopeH + S(10);

        // 에르다 파운틴은 도핑 칸 바로 밑에 둔다
        var erdaCard = MakeCard(dx, y, colW, new CardSpec
        {
            Color = JanusWatch.ErdaMain, Title = "에르다 파운틴", On = st.ErdaOn, SetOn = v => st.ErdaOn = v,
            KeyLabel = "에르다 파운틴 키", Which = 8,
            Hint = "키를 누르면 60초를 세요.",
            More = "에르다 파운틴 키를 누르면 그때부터 60초를 세요. 다시 누르면 처음부터 다시 셉니다.\n" +
                   "5초 먼저 알리기를 켜면 55초에 '곧 사라져요', 끄면 60초에 '사라졌어요'로 알려 드려요.",
            Preview = () => JanusWatch.PlayErda(sc, st.ErdaEarly),
            Switches = new (string, bool, Action<bool>)[] { ("5초 먼저 알리기", st.ErdaEarly, v => st.ErdaEarly = v) },
        }, out var ek, out var es);
        erdaKey = ek; erdaStatus = es;
        y += erdaCard.Height + S(10);
        y = Math.Max(y, Math.Max(leftEnd, rightEnd));

        // 줄 사이 세로선
        for (int c = 1; c <= 2; c++)
            Controls.Add(new Panel { BackColor = UI.T(Color.FromArgb(225, 225, 230)), Bounds = new Rectangle(pad + colW * c + gap * (c - 1) + gap / 2, top + S(4), Math.Max(1, S(1)), y - top - S(14)) });

        // 1페이지 칸을 모아 두고, 같은 자리에 2페이지(반복 주기 알림 · 모니터)를 만든다
        pages[0] = Controls.Cast<Control>().Skip(pageStart).ToList();
        pageEnd[0] = y;
        int page2Start = Controls.Count;
        pageEnd[1] = MakePage2(pad, top, colW, gap, contentR - pad);
        pages[1] = Controls.Cast<Control>().Skip(page2Start).ToList();
        foreach (var c in pages[1]) c.Visible = false;
        ApplySlotPages();
        RefreshSlotNavs();

        }

        foot = new Label
        {
            Text = "키 칸을 누른 뒤 원하는 키를 누르세요 · Backspace 지우기 · Esc 취소",
            Font = F(8.5f), ForeColor = Gray, AutoSize = true, MaximumSize = new Size(W - pad * 2, 0), Location = new Point(pad, y),
        };
        Controls.Add(foot);
        y += foot.PreferredSize.Height + S(14);

        ClientSize = new Size(W, y);
        if (!toon) ShowPage(0);
        refresh.Tick += (_, _) => UpdateStatus();
        refresh.Start();
        UpdateKeys();
        UpdateStatus();
    }

    // ── 오른쪽 프리셋 칸: 목록 · 이름 칸 · 저장/제거. 1·2페이지 알림 값을 함께 저장한다 ──
    Card? presetPanel;
    Panel? presetList;
    RoundLabel? presetInputBack, presetSave, presetDel;
    TextBox? presetInput;

    Card MakePresetPanel(int x, int y, int w)
    {
        var card = new Card { Location = new Point(x, y), Size = new Size(w, S(300)) };
        var title = new Label { Text = "프리셋", Font = F(10.5f, FontStyle.Bold), AutoSize = true, Location = new Point(S(14), S(12)) };
        presetList = new Panel { AutoScroll = true, BackColor = UI.Back };
        // 이름 칸: 둥근 바탕 안에 테두리 없는 글 칸 (바탕이 Label이라 한글 입력을 따로 켠다)
        presetInputBack = new RoundLabel { AutoSize = false, Radius = S(6), Fill = UI.HoverBack, Cursor = Cursors.IBeam };
        presetInput = new TextBox
        {
            BorderStyle = BorderStyle.None, BackColor = UI.HoverBack, ForeColor = UI.Text, Font = F(9.5f), MaxLength = 20,
            PlaceholderText = "프리셋 이름", ImeMode = ImeMode.NoControl, Text = st.AlertPresetName ?? "",
        };
        presetInputBack.Controls.Add(presetInput);
        presetInputBack.Click += (_, _) => presetInput.Focus();
        presetSave = UI.Button("저장", "", SavePreset, sc, Color.White, UI.Accent);
        presetDel = UI.Button("제거", "", DeletePreset, sc, null, UI.HoverBack);
        foreach (var b in new[] { presetSave, presetDel })
        {
            b.AutoSize = false; b.Radius = S(6); b.TextAlign = ContentAlignment.MiddleCenter; b.Font = F(9.5f, FontStyle.Bold); b.Padding = Padding.Empty;
        }
        card.Controls.AddRange(new Control[] { title, presetList, presetInputBack, presetSave, presetDel });
        Controls.Add(card);
        return card;
    }

    /// <summary>프리셋 칸 높이에 맞춰 목록·이름 칸·단추 자리를 잡는다</summary>
    void LayoutPresetPanel()
    {
        if (presetPanel == null || presetList == null || presetInputBack == null || presetInput == null || presetSave == null || presetDel == null) return;
        int ip = S(12), w = presetPanel.Width - ip * 2, bh = S(34), g = S(8);
        int by = presetPanel.Height - ip - bh, half = (w - g) / 2;
        presetSave.Bounds = new Rectangle(ip, by, half, bh);
        presetDel.Bounds = new Rectangle(ip + half + g, by, w - half - g, bh);
        presetInputBack.Bounds = new Rectangle(ip, by - g - bh, w, bh);
        presetInput.Width = w - S(20);
        presetInput.Location = new Point(S(10), (bh - presetInput.Height) / 2);
        int top = S(42);
        presetList.Bounds = new Rectangle(ip, top, w, presetInputBack.Top - S(10) - top);
        BuildPresetList();
    }

    AlertPreset? FindPreset(string? name) => string.IsNullOrWhiteSpace(name) ? null
        : st.AlertPresets.FirstOrDefault(p => string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>프리셋 목록 줄을 다시 만든다 (고른 프리셋은 주황빛)</summary>
    void BuildPresetList()
    {
        if (presetList == null) return;
        presetList.SuspendLayout();
        foreach (var c in presetList.Controls.Cast<Control>().ToList()) c.Dispose();
        presetList.AutoScrollPosition = Point.Empty;
        var list = st.AlertPresets;
        if (list.Count == 0)
            presetList.Controls.Add(new Label { Text = "저장한 프리셋이 없어요.", Font = F(9), ForeColor = Gray, AutoSize = true, Location = new Point(S(2), S(4)) });
        int rowH = S(34), g = S(6);
        bool over = list.Count * (rowH + g) - g > presetList.Height;
        int rw = presetList.Width - (over ? SystemInformation.VerticalScrollBarWidth + S(2) : 0);
        for (int i = 0; i < list.Count; i++) presetList.Controls.Add(MakePresetRow(list[i], i * (rowH + g), rw, rowH));
        presetList.ResumeLayout();
    }

    RoundLabel MakePresetRow(AlertPreset p, int y, int w, int h)
    {
        bool sel = string.Equals(p.Name, st.AlertPresetName, StringComparison.OrdinalIgnoreCase);
        var back = sel ? UI.SoftAccent : UI.HoverBack;
        var row = new RoundLabel { AutoSize = false, Bounds = new Rectangle(0, y, w, h), Radius = S(6), Cursor = Cursors.Hand, Fill = back };
        var pen = new Label { Text = "✎", Font = F(10), ForeColor = Gray, AutoSize = true, Cursor = Cursors.Hand, BackColor = Color.Transparent };
        pen.Location = new Point(w - S(10) - pen.PreferredWidth, (h - pen.PreferredHeight) / 2);
        var name = new Label
        {
            Text = p.Name, Font = F(9.5f, sel ? FontStyle.Bold : FontStyle.Regular), ForeColor = sel ? UI.AccentText : UI.Text,
            AutoSize = false, AutoEllipsis = true, UseMnemonic = false, BackColor = Color.Transparent, Cursor = Cursors.Hand,
            TextAlign = ContentAlignment.MiddleLeft, Bounds = new Rectangle(S(10), 0, pen.Left - S(14), h),
        };
        row.Controls.AddRange(new Control[] { name, pen });
        presetTip.SetToolTip(pen, "이름 바꾸기");
        void Hover(bool on) => row.Fill = on ? ControlPaint.Dark(back, 0.05f) : back;
        foreach (var c in new Control[] { row, name })
        {
            c.MouseEnter += (_, _) => Hover(true);
            c.MouseLeave += (_, _) => Hover(false);
            c.Click += (_, _) => LoadPreset(p);
        }
        pen.Click += (_, _) => RenamePreset(row, name, pen, p);
        return row;
    }
    static readonly ToolTip presetTip = new();

    /// <summary>목록 줄에서 바로 이름을 고친다 (엔터 저장, Esc 취소, 비우면 그대로)</summary>
    void RenamePreset(RoundLabel row, Label name, Label pen, AlertPreset p)
    {
        editingName?.Invoke(true);
        var tb = new TextBox
        {
            BorderStyle = BorderStyle.None, BackColor = row.Fill, ForeColor = UI.Text, Font = name.Font, MaxLength = 20,
            Text = p.Name, ImeMode = ImeMode.NoControl,
        };
        tb.Width = row.Width - S(20);
        tb.Location = new Point(S(10), (row.Height - tb.Height) / 2);
        name.Visible = pen.Visible = false;
        row.Controls.Add(tb);
        bool done = false;
        void Finish(bool keep)
        {
            if (done) return;
            done = true;
            editingName = null;
            string nn = tb.Text.Trim();
            string? msg = null;
            if (keep && nn.Length > 0 && nn != p.Name)
            {
                if (FindPreset(nn) is AlertPreset other && other != p) msg = "같은 이름이 이미 있어요";
                else
                {
                    if (string.Equals(st.AlertPresetName, p.Name, StringComparison.OrdinalIgnoreCase)) st.AlertPresetName = nn;
                    if (presetInput != null && string.Equals(presetInput.Text.Trim(), p.Name, StringComparison.OrdinalIgnoreCase)) presetInput.Text = nn;
                    p.Name = nn;
                    st.Save();
                }
            }
            BeginInvoke(() =>
            {
                BuildPresetList();
                if (msg != null) AskBox.Choose(this, msg, "다른 이름으로 바꿔 주세요.", true, "확인");
            });
        }
        tb.Leave += (_, _) => Finish(true);
        editingName = Finish;
        tb.Focus();
        tb.SelectAll();
    }

    /// <summary>이름 칸에 쓴 이름으로 지금 알림 설정(1·2페이지)을 저장한다</summary>
    void SavePreset()
    {
        if (presetInput == null) return;
        editingName?.Invoke(true);
        string n = presetInput.Text.Trim();
        if (n.Length == 0)
        {
            AskBox.Choose(this, "이름을 넣어 주세요", "이름이 비어 있으면 저장할 수 없어요.", true, "확인");
            presetInput.Focus();
            return;
        }
        var old = FindPreset(n);
        if (old != null && !AskBox.Show(this, $"'{old.Name}'에 덮어쓸까요?", "지금 알림 설정으로 바뀌어요.", "덮어쓰기", "취소", true)) return;
        var p = AlertPreset.From(st, old?.Name ?? n);
        if (old != null) st.AlertPresets[st.AlertPresets.IndexOf(old)] = p; else st.AlertPresets.Add(p);
        st.AlertPresetName = p.Name;
        presetInput.Text = p.Name;
        st.Save();
        BuildPresetList();
    }

    /// <summary>이름 칸의 프리셋(없으면 고른 프리셋)을 지운다</summary>
    void DeletePreset()
    {
        editingName?.Invoke(true);
        var p = FindPreset(presetInput?.Text) ?? FindPreset(st.AlertPresetName);
        if (p == null) { AskBox.Choose(this, "지울 프리셋을 골라 주세요", "목록에서 프리셋을 누르면 골라져요.", true, "확인"); return; }
        if (!AskBox.Show(this, $"'{p.Name}'을 지울까요?", "지운 프리셋은 되돌릴 수 없어요.", "지우기")) return;
        st.AlertPresets.Remove(p);
        if (string.Equals(st.AlertPresetName, p.Name, StringComparison.OrdinalIgnoreCase)) st.AlertPresetName = null;
        if (presetInput != null) presetInput.Text = "";
        st.Save();
        BuildPresetList();
    }

    /// <summary>지금 알림 설정이 고른 프리셋과 똑같은지 (같으면 불러올 때 묻지 않는다)</summary>
    bool SameAsSelected()
    {
        if (FindPreset(st.AlertPresetName) is not AlertPreset cur) return false;
        return System.Text.Json.JsonSerializer.Serialize(AlertPreset.From(st, cur.Name)) == System.Text.Json.JsonSerializer.Serialize(cur);
    }

    /// <summary>프리셋을 불러와 알림 설정에 넣고, 창을 새 값으로 다시 그린다</summary>
    void LoadPreset(AlertPreset p)
    {
        editingName?.Invoke(true);
        if (presetInput != null) presetInput.Text = p.Name;
        if (!SameAsSelected() && !AskBox.Show(this, $"'{p.Name}'을 불러올까요?", "저장하지 않은 지금 설정은 사라져요.", "불러오기", "취소", true)) return;
        p.ApplyTo(st);
        st.AlertPresetName = p.Name;
        st.Save();
        // 칸들은 만들 때 설정 값을 읽으므로, 같은 자리·같은 페이지로 창을 새로 연다
        if (Owner is not Form owner) return;
        int pg = page; var loc = Location;
        BeginInvoke(() =>
        {
            Close();
            Open(owner, st, watch, sc);
            if (alertForm is JanusForm f && !f.IsDisposed) { f.ShowPage(pg); f.Location = loc; }
        });
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

    /// <summary>조합 키: 이 키를 누른 채로 칸 키를 눌러야 세는 칸을 만들 때 쓰는 키</summary>
    Card MakeComboCard(int x, int y, int w)
    {
        var card = new Card { Location = new Point(x, y), Width = w, Radius = S(12) };
        Controls.Add(card);
        int ip = S(14), cy = S(8);

        var pill = new RoundLabel
        {
            AutoSize = false, Size = new Size(S(112), S(30)), TextAlign = ContentAlignment.MiddleCenter,
            Fill = UI.HoverBack, Cursor = Cursors.Hand, Font = F(10, FontStyle.Bold), Radius = S(8),
        };
        pill.Location = new Point(w - ip - pill.Width, cy);
        pill.Click += (_, _) => { capturing = capturing == 2 ? 0 : 2; UpdateKeys(); };
        comboKey = pill;

        var dot = new Label { Text = "●", ForeColor = UI.Accent, Font = F(9), AutoSize = true, Location = new Point(ip, cy + S(7)) };
        var name = new Label { Text = "조합 키", Font = F(11, FontStyle.Bold), AutoSize = true, Location = new Point(ip + S(18), cy + S(4)) };
        var tip = new Label { Text = "칸에서 켜면 이 키를 누른 채로 눌러야 세요", Font = F(8.5f), ForeColor = Gray, AutoSize = true };
        // 칸이 좁으면 설명은 아랫줄에
        bool oneLine = name.Left + name.PreferredWidth + S(10) + tip.PreferredWidth + S(10) <= pill.Left;
        tip.Location = oneLine ? new Point(name.Left + name.PreferredWidth + S(10), cy + S(8)) : new Point(ip, cy + pill.Height + S(12));
        card.Controls.AddRange(new Control[] { dot, name, tip, pill });

        card.Height = oneLine ? cy + pill.Height + S(8) : tip.Bottom + S(12);
        return card;
    }

    /// <summary>화면 알림: 끄면 화면에 빛·글자를 띄우지 않고 소리만 낸다</summary>
    Card MakeVisualCard(int x, int y, int w, out Toggle visualSw)
    {
        var card = new Card { Location = new Point(x, y), Width = w, Radius = S(12) };
        Controls.Add(card);
        int ip = S(14), cy = S(12);

        var dot = new Label { Text = "●", ForeColor = UI.Accent, Font = F(9), AutoSize = true, Location = new Point(ip, cy + S(3)) };
        var name = new Label { Text = "화면 알림", Font = F(11, FontStyle.Bold), AutoSize = true, Location = new Point(ip + S(18), cy) };
        var sw = new Toggle { On = st.AlertVisual, Size = new Size(S(40), S(22)) };
        sw.Location = new Point(w - ip - sw.Width, cy + S(1));
        var tip = new Label { Text = "끄면 화면엔 안 뜨고 소리만 나요", Font = F(8.5f), ForeColor = Gray, AutoSize = true, Location = new Point(ip, cy + S(40)) };
        sw.Changed += v => { st.AlertVisual = v; st.Save(); };
        card.Controls.AddRange(new Control[] { dot, name, sw, tip });
        visualSw = sw;

        card.Height = tip.Bottom + S(12);
        return card;
    }

    /// <summary>알림 방식: 화면 플래시(화면 전체가 물듦)와 테두리 강조(가장자리를 빛이 한 바퀴 돎)를 따로 켜고 끈다.
    /// 화면 알림을 끄면 둘 다 흐리게 한다.</summary>
    Card MakeMethodCard(int x, int y, int w, Toggle visualSw)
    {
        var card = new Card { Location = new Point(x, y), Width = w, Radius = S(12) };
        Controls.Add(card);
        int ip = S(14), cy = S(12);

        var dot = new Label { Text = "●", ForeColor = UI.Accent, Font = F(9), AutoSize = true, Location = new Point(ip, cy + S(3)) };
        var name = new Label { Text = "알림 방식", Font = F(11, FontStyle.Bold), AutoSize = true, Location = new Point(ip + S(18), cy) };
        card.Controls.AddRange(new Control[] { dot, name });
        cy += S(38);

        // 두 항목을 한 줄에 반씩
        int half = (w - ip * 2 - S(16)) / 2;
        var tipper = new ToolTip { AutoPopDelay = 30000 };
        Toggle Item(string text, string more, int left, bool on, Action<bool> set)
        {
            var lbl = RowLabel(text, left, cy);
            lbl.Cursor = Cursors.Help;
            tipper.SetToolTip(lbl, more);
            var t = new Toggle { On = on, Size = new Size(S(40), S(22)) };
            t.Location = new Point(left + half - t.Width, cy + (lbl.Height - t.Height) / 2);
            t.Changed += v => { set(v); st.Save(); };
            card.Controls.AddRange(new Control[] { lbl, t });
            return t;
        }
        var flash = Item("화면 플래시", "화면 전체가 색으로 물들어요", ip, st.AlertFlash, v => st.AlertFlash = v);
        var edge = Item("테두리 강조", "화면 끝을 따라 빛이 한 바퀴 돌아요", ip + half + S(16), st.AlertEdge, v => st.AlertEdge = v);
        cy += S(38);

        void Dim() => flash.Enabled = edge.Enabled = st.AlertVisual;
        visualSw.Changed += _ => Dim();
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
        /// <summary>숫자 줄: 이름 · 저장된 값 · 선택지 · 표시 방법 · 저장 방법 (이름이 없으면 숫자 줄을 안 만든다)</summary>
        public string? StepLabel { get; init; }
        public int Value { get; init; }
        public int[] Options { get; init; } = { 0 };
        public Func<int, string> Fmt { get; init; } = v => v.ToString();
        public Action<int> SetValue { get; init; } = _ => { };
        /// <summary>숫자 줄을 − + 대신 [ ]분 [ ]초 칸으로 받는다 (Value는 초)</summary>
        public bool MinSec { get; init; }
        /// <summary>설명 글 (비우면 줄을 만들지 않는다)</summary>
        public required string Hint { get; init; }
        /// <summary>설정에 따라 바뀌는 설명 글 (Hint는 줄 수만 정한다)</summary>
        public Func<string>? LiveHint { get; init; }
        /// <summary>설명 글에 마우스를 올리면 보이는 자세한 설명</summary>
        public string? More { get; init; }
        public required Action Preview { get; init; }
        public (string label, bool on, Action<bool> set)[]? Switches { get; init; }
        public (string label, int value, int[] options, Func<int, string> fmt, Action<int> set)? Step2 { get; init; }
        /// <summary>이름을 바꿀 수 있는 칸이면 저장된 이름 읽기·쓰기 (비우면 Title이 기본 이름)</summary>
        public Func<string?>? GetName { get; init; }
        public Action<string?>? SetName { get; init; }
    }

    /// <summary>제목 글을 누르면 그 자리에서 이름을 바꾸게 한다 (엔터 저장, Esc 취소, 비우면 기본 이름)</summary>
    void AddNameEditor(Card card, Label name, int ip, int cy, int right, string fallback, Func<string?> get, Action<string?> set)
    {
        var (pen, box) = MakeNameEditor(name, ip, cy, right, fallback, 12,
            () => string.IsNullOrWhiteSpace(get()) ? fallback : get()!.Trim(), get, set, refreshNavs: true);
        card.Controls.AddRange(new Control[] { pen, box });
    }

    /// <summary>이름 바꾸기에 쓰는 ✎ 표시와 글 칸을 만든다 (카드에 올리는 것은 부르는 쪽이 한다).
    /// shown = 지금 보여 줄 이름, right = 글 칸이 닿을 오른쪽 끝</summary>
    (Label pen, TextField box) MakeNameEditor(Label name, int ip, int cy, int right, string placeholder, int maxLength,
        Func<string> shown, Func<string?> get, Action<string?> set, bool refreshNavs)
    {
        name.Text = shown();
        name.Cursor = Cursors.IBeam;
        var pen = new Label { Text = "✎", Font = F(9), ForeColor = Gray, AutoSize = true, Cursor = Cursors.Hand };
        void PlacePen() => pen.Location = new Point(name.Right + S(4), cy + S(3));
        PlacePen();
        var box = new TextField
        {
            Font = F(10.5f, FontStyle.Bold), MaxLength = maxLength, Visible = false,
            Location = new Point(ip + S(12), cy - S(5)), Width = right - ip - S(16) - S(10), PlaceholderText = placeholder,
        };
        bool done = false;
        void Edit()
        {
            done = false;
            box.Text = get() ?? "";
            name.Visible = pen.Visible = false;
            box.Visible = true; box.Focus(); box.SelectAll();
        }
        void Finish(bool keep)
        {
            if (done) return;
            done = true;
            if (keep) { set(string.IsNullOrWhiteSpace(box.Text) ? null : box.Text.Trim()); st.Save(); }
            name.Text = shown(); PlacePen();
            box.Visible = false; name.Visible = pen.Visible = true;
            editingName = null;
            if (refreshNavs) RefreshSlotNavs();
        }
        name.Click += (_, _) => Edit();
        pen.Click += (_, _) => Edit();
        box.Enter += (_, _) => editingName = Finish;
        box.Leave += (_, _) => Finish(true);
        return (pen, box);
    }

    /// <summary>버프·도핑 넘기기 줄: ◀ [이름 1 / 3] ▶. 한 번에 한 칸만 보인다</summary>
    int MakeSlotNav(int x, int y, int w, int count, Func<int> get, Action<int> set, Func<int, string> nameOf, Func<int, Color> colorOf)
    {
        int bh = S(32), aw = S(44), g = S(6);
        RoundLabel Btn(string text, int left, int width, float size) => new()
        {
            AutoSize = false, Size = new Size(width, bh), TextAlign = ContentAlignment.MiddleCenter,
            Font = F(size, FontStyle.Bold), Radius = S(8), Text = text, Location = new Point(left, y), UseMnemonic = false,
        };
        var prev = Btn("◀", x, aw, 10.5f);
        var label = Btn("", x + aw + g, w - (aw + g) * 2, 10);
        label.Fill = UI.HoverBack;
        var next = Btn("▶", x + w - aw, aw, 10.5f);
        void Go(int d)
        {
            int to = get() + d;
            if (to < 0 || to >= count) return;
            editingName?.Invoke(true);
            if (capturing != 0) { capturing = 0; UpdateKeys(); }
            set(to);
            ApplySlotPages();
            RefreshSlotNavs();
        }
        prev.Click += (_, _) => Go(-1);
        next.Click += (_, _) => Go(1);
        slotNavs.Add(() =>
        {
            int p = get();
            label.Text = $"{nameOf(p)}   {p + 1} / {count}";
            label.ForeColor = Readable(colorOf(p));
            foreach (var (b, ok) in new[] { (prev, p > 0), (next, p < count - 1) })
            {
                // 다크 모드: 갈색 칸 대신 조금 밝은 차콜 칸에 호박색 화살표 (최근 검색 칩 글씨와 같은 색)
                b.Fill = ok ? (UI.Dark ? Color.FromArgb(80, 86, 94) : UI.SoftAccent) : UI.HoverBack;
                b.ForeColor = ok ? UI.AccentText : UI.T(Color.FromArgb(190, 190, 196));
                b.Cursor = ok ? Cursors.Hand : Cursors.Default;
            }
        });
        Controls.Add(prev); Controls.Add(label); Controls.Add(next);
        return bh;
    }

    void RefreshSlotNavs() { foreach (var f in slotNavs) f(); }

    /// <summary>버프·도핑 칸 중 고른 한 칸만 보인다 (1페이지일 때만)</summary>
    void ApplySlotPages()
    {
        for (int i = 0; i < buffCards.Length; i++) if (buffCards[i] is Card c) c.Visible = page == 0 && i == buffPage;
        for (int i = 0; i < dopeCards.Length; i++) if (dopeCards[i] is Card c) c.Visible = page == 0 && i == dopePage;
        for (int i = 0; i < repeatCards.Length; i++) if (repeatCards[i] is Card c) c.Visible = page == 1 && i == repeatPage;
        for (int i = 0; i < onceCards.Length; i++) if (onceCards[i] is Card c) c.Visible = page == 1 && i == oncePage;
    }

    string BuffName(int i) => string.IsNullOrWhiteSpace(st.Buffs[i].Name) ? $"버프 {i + 1}" : st.Buffs[i].Name!.Trim();
    string DopeName(int i) => string.IsNullOrWhiteSpace(st.Dopes[i].Name) ? JanusWatch.DopeDefaultName(i) : st.Dopes[i].Name!.Trim();

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
        offAll.Add(() => { if (sw.On) { sw.On = false; setOn(false); } });
        card.Controls.AddRange(new Control[] { dot, name, sw });
        if (spec.SetName is Action<string?> setName && spec.GetName is Func<string?> getName)
            AddNameEditor(card, name, ip, cy, sw.Left, title, getName, setName);
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

        // 숫자 줄: − 값 + (에르다 파운틴처럼 시간이 정해진 칸은 없다)
        int cur = Math.Clamp(value, options[0], options[^1]);
        var shown = ValueLabel(fmt(cur));
        if (stepLabel != null && spec.MinSec)
        {
            // [ ]분 [ ]초 (오른쪽부터 놓는다)
            card.Controls.Add(RowLabel(stepLabel, ip, cy));
            int sec0 = Math.Max(0, value);
            var boxes = new NumBox[2];
            int right = w - ip;
            for (int k = 1; k >= 0; k--)
            {
                var unit = new Label { Text = k == 0 ? "분" : "초", AutoSize = true, ForeColor = UI.T(Color.FromArgb(80, 80, 80)) };
                card.Controls.Add(unit);
                unit.Location = new Point(right - unit.PreferredWidth, cy + (S(30) - unit.PreferredHeight) / 2);
                var nb = new NumBox
                {
                    Maximum = k == 0 ? 99 : 59, Value = k == 0 ? Math.Min(99, sec0 / 60) : sec0 % 60,
                    Size = new Size(S(54), S(30)), Radius = S(8), Font = F(10.5f, FontStyle.Bold), ForeColor = UI.Text,
                };
                nb.Location = new Point(unit.Left - S(4) - nb.Width, cy);
                card.Controls.Add(nb);
                boxes[k] = nb;
                right = nb.Left - S(10);
            }
            foreach (var nb in boxes)
                nb.ValueChanged += (_, _) => { setValue((int)boxes[0].Value * 60 + (int)boxes[1].Value); st.Save(); UpdateStatus(); };
            cy += S(40);
        }
        else if (stepLabel != null)
        {
            card.Controls.Add(RowLabel(stepLabel, ip, cy));
            // − + 는 선택지 사이를 오가고, 값 칸을 두 번 누르면 범위 안의 아무 숫자나 칠 수 있다
            if (cur != value) { setValue(cur); st.Save(); }
            void Set(int v) { cur = v; shown.Text = fmt(cur); setValue(cur); st.Save(); UpdateStatus(); }
            void Change(int d) => Set(StepOption(options, cur, d));
            AddStepper(card, shown, w - ip, cy, Change);
            AddTypeIn(card, shown, () => cur, options[0], options[^1], Set);
            cy += S(38);
        }

        // 켜고 끄는 줄 (바꾸면 값 표시도 다시 쓴다: 5석펫에 따라 초 ↔ 무한대)
        // 두 번째 숫자(부스트 도핑의 사용 대기 초)는 첫 스위치 줄에 같이 놓아 칸을 줄인다: 사용 대기 [− 3초 +] [스위치]
        bool first = true;
        foreach (var (label, on2, set) in switches ?? Array.Empty<(string, bool, Action<bool>)>())
        {
            card.Controls.Add(RowLabel(label, ip, cy));
            var t = new Toggle { On = on2, Size = new Size(S(40), S(22)) };
            t.Location = new Point(w - ip - t.Width, cy + S(4));
            t.Changed += v => { set(v); st.Save(); shown.Text = fmt(cur); UpdateStatus(); };
            card.Controls.Add(t);
            if (label == "조합 키 누른 채로 쓰기") comboSws[spec.Which] = t;
            if (first && step2 is var (_, v2, o2, f2, s2))
            {
                int c2 = Math.Clamp(v2, o2[0], o2[^1]);
                var shown2 = ValueLabel(f2(c2));
                void Set2(int v) { c2 = v; shown2.Text = f2(c2); s2(c2); st.Save(); UpdateStatus(); }
                AddStepper(card, shown2, t.Left - S(12), cy, d => Set2(StepOption(o2, c2, d)));
                AddTypeIn(card, shown2, () => c2, o2[0], o2[^1], Set2);
                cy += S(38);
            }
            else cy += S(34);
            first = false;
        }

        if (hint.Length > 0)
        {
            var tip = new Label { Text = hint, Font = F(8.5f), ForeColor = Gray, AutoSize = true, MaximumSize = new Size(w - ip * 2, 0), Location = new Point(ip, cy) };
            card.Controls.Add(tip);
            if (spec.LiveHint is Func<string> live)
            {
                // 글이 바뀌어도 칸 높이는 그대로: 처음 넣은 줄 수만큼 자리를 잡아 둔다
                tip.AutoSize = false; tip.Size = tip.PreferredSize with { Width = w - ip * 2 };
                tip.Text = live();
                liveHints.Add(() => tip.Text = live());
            }
            if (spec.More != null) { tip.Cursor = Cursors.Help; new ToolTip { AutoPopDelay = 30000 }.SetToolTip(tip, spec.More); }
            cy += tip.PreferredSize.Height + S(10);
        }
        else
        {
            if (spec.More != null) { name.Cursor = Cursors.Help; new ToolTip { AutoPopDelay = 30000 }.SetToolTip(name, spec.More); }
            cy += S(4);
        }

        // 상태 줄: 남은 시간 · 미리 보기
        var look = UI.Button("미리 보기", "", preview, sc, fore: color, back: Color.FromArgb(24, color));
        look.Font = F(9, FontStyle.Bold);
        card.Controls.Add(look);
        look.Location = new Point(w - ip - look.PreferredSize.Width, cy);
        var st2 = new Label { AutoSize = false, ForeColor = Readable(color), Font = F(9.5f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft,
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
        var card = KeyCard(x, y, w, Green, "웹툰 키", out int ip, out int cy);
        RoundLabel Pill(string label, int which) => KeyPill(card, label, which, w, ip, ref cy);
        up = Pill("위로 키", 4);
        down = Pill("아래로 키", 5);
        nextKey = Pill("다음 화 키", 6);
        prevKey = Pill("이전 화 키", 7);

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
                   "웹툰 창이나 메이플 창이 앞에 있을 때 동작해요. 유료분은 '네이버 로그인'으로 직접 로그인해서 보세요.",
            Font = F(8.5f), ForeColor = Gray, AutoSize = true, MaximumSize = new Size(w - ip * 2, 0), Location = new Point(ip, cy),
        };
        card.Controls.Add(tip);
        cy += tip.PreferredSize.Height + S(12);
        card.Height = cy;
        return card;
    }

    /// <summary>유튜브 쇼츠 넘기기 키(다음·이전)</summary>
    Card MakeShortsCard(int x, int y, int w)
    {
        var card = KeyCard(x, y, w, Color.FromArgb(235, 51, 51), "쇼츠 키", out int ip, out int cy);
        shortsNextKey = KeyPill(card, "다음 쇼츠 키", 10, w, ip, ref cy);
        shortsPrevKey = KeyPill(card, "이전 쇼츠 키", 11, w, ip, ref cy);
        var tip = new Label
        {
            Text = "바 메뉴 '영상'의 유튜브 재생 창에 쇼츠가 떠 있을 때, 한 번 누르면 다음·이전 쇼츠로 넘어가요. " +
                   "재생 창이나 메이플 창이 앞에 있을 때 동작해요.",
            Font = F(8.5f), ForeColor = Gray, AutoSize = true, MaximumSize = new Size(w - ip * 2, 0), Location = new Point(ip, cy),
        };
        card.Controls.Add(tip);
        cy += tip.PreferredSize.Height + S(12);
        card.Height = cy;
        return card;
    }

    /// <summary>키 칸 묶음: 색 점과 굵은 제목이 있는 둥근 카드</summary>
    Card KeyCard(int x, int y, int w, Color dot, string name, out int ip, out int cy)
    {
        var card = new Card { Location = new Point(x, y), Width = w, Radius = S(12) };
        Controls.Add(card);
        ip = S(14); cy = S(12);
        card.Controls.Add(new Label { Text = "●", ForeColor = dot, Font = F(9), AutoSize = true, Location = new Point(ip, cy + S(3)) });
        card.Controls.Add(new Label { Text = name, Font = F(11, FontStyle.Bold), AutoSize = true, Location = new Point(ip + S(18), cy) });
        cy += S(38);
        return card;
    }

    /// <summary>이름과 키 칸 한 줄. 키 칸을 누르면 그 키를 받는다.</summary>
    RoundLabel KeyPill(Card card, string label, int which, int w, int ip, ref int cy)
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

    Label? pendantStatus;

    /// <summary>정펜 확인: 켜기 / 닉네임 / 어느 버프가 돌 때 볼지 / 상태 · 미리 보기</summary>
    Card MakePendantCard(int x, int y, int w)
    {
        var card = new Card { Location = new Point(x, y), Width = w, Radius = S(12) };
        Controls.Add(card);
        int ip = S(14), cy = S(12);
        var blue = PendantCheck.Blue;

        var dot = new Label { Text = "●", ForeColor = blue, Font = F(9), AutoSize = true, Location = new Point(ip, cy + S(3)) };
        var name = new Label { Text = "정펜 확인", Font = F(11, FontStyle.Bold), AutoSize = true, Location = new Point(ip + S(18), cy) };
        var sw = new Toggle { On = st.PendantOn, Size = new Size(S(40), S(22)) };
        sw.Location = new Point(w - ip - sw.Width, cy + S(1));
        sw.Changed += v => { st.PendantOn = v; st.Save(); UpdateStatus(); };
        offAll.Add(() => { if (sw.On) { sw.On = false; st.PendantOn = false; } });
        // 끼셨나요? 창에서 "미착용 예정"을 누르면 스위치도 끈다
        Action off = () => { if (!IsDisposed && sw.On) { sw.On = false; sw.Invalidate(); UpdateStatus(); } };
        PendantCheck.TurnedOff += off;
        FormClosed += (_, _) => PendantCheck.TurnedOff -= off;
        card.Controls.AddRange(new Control[] { dot, name, sw });
        cy += S(38);

        // 닉네임 칸
        card.Controls.Add(RowLabel("캐릭터 닉네임", ip, cy));
        var nick = new TextField
        {
            Font = F(10.5f), MaxLength = 12,
            Text = st.MapleNick ?? "", PlaceholderText = "닉네임", Width = S(160),
        };
        nick.Location = new Point(w - ip - nick.Width, cy + (S(30) - nick.Height) / 2);
        nick.TextChanged += (_, _) =>
        {
            st.MapleNick = string.IsNullOrWhiteSpace(nick.Text) ? null : nick.Text.Trim();
            st.Save(); UpdateStatus();
        };
        card.Controls.Add(nick);
        cy += S(40);

        // 어느 버프가 돌 때 볼지: 버프 1 · 2 · 3 중 하나
        card.Controls.Add(RowLabel("볼 때", ip, cy));
        var picks = new RoundLabel[JanusWatch.BuffSlots];
        void PaintPicks()
        {
            for (int i = 0; i < picks.Length; i++)
            {
                bool on = st.PendantBuff == i;
                picks[i].Fill = on ? Wash(blue, 32) : UI.HoverBack;
                picks[i].ForeColor = on ? Readable(blue) : Gray;
            }
        }
        int right = w - ip;
        for (int i = picks.Length - 1; i >= 0; i--)
        {
            int k = i;
            var p = new RoundLabel
            {
                AutoSize = false, Size = new Size(S(62), S(30)), TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand, Font = F(9.5f, FontStyle.Bold), Radius = S(8), Text = $"버프 {i + 1}",
            };
            p.Location = new Point(right - p.Width, cy);
            right = p.Left - S(6);
            p.Click += (_, _) => { st.PendantBuff = k; st.Save(); PaintPicks(); UpdateStatus(); };
            card.Controls.Add(p);
            picks[i] = p;
        }
        if (st.PendantBuff is < 0 or >= JanusWatch.BuffSlots) { st.PendantBuff = 0; st.Save(); }
        PaintPicks();
        cy += S(40);

        var hint = new Label
        {
            Text = "고른 버프가 도는 동안 1분마다 정펜을 확인해요.",
            Font = F(8.5f), ForeColor = Gray, AutoSize = true, MaximumSize = new Size(w - ip * 2, 0), Location = new Point(ip, cy), Cursor = Cursors.Help,
        };
        new ToolTip { AutoPopDelay = 30000 }.SetToolTip(hint,
            "고른 버프가 도는 동안 maple.gg에서 목걸이·펜던트를 1분마다 확인해요. 확인할 때 아래 가운데가 잠깐 파랗게 빛나요.\n" +
            "정펜이 없으면 낄 때까지 20초마다 노란 빛으로 알려 드려요.\n" +
            "maple.gg는 장비를 바꾸고 반영까지 15분쯤 걸려요.");
        card.Controls.Add(hint);
        cy += hint.PreferredSize.Height + S(10);

        // 상태 줄 · 미리 보기
        var look = UI.Button("미리 보기", "", () => PendantCheck.PlayAlert(sc), sc, fore: blue, back: Color.FromArgb(24, blue));
        look.Font = F(9, FontStyle.Bold);
        card.Controls.Add(look);
        look.Location = new Point(w - ip - look.PreferredSize.Width, cy);
        pendantStatus = new Label { AutoSize = false, AutoEllipsis = true, Font = F(9.5f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft,
            Location = new Point(ip, cy), Size = new Size(look.Left - ip - S(6), look.PreferredSize.Height) };
        card.Controls.Add(pendantStatus);
        cy += look.PreferredSize.Height + S(12);

        card.Height = cy;
        return card;
    }

    static readonly Color Teal = Color.FromArgb(0, 165, 155);

    /// <summary>2페이지: 왼쪽 반복 주기 알림 3칸 · 오른쪽 단일 알림 3칸 (◀ ▶로 한 칸씩) · 알림 띄울 모니터. 끝난 높이를 돌려준다.</summary>
    int MakePage2(int x, int top, int colW, int gap, int fullW)
    {
        int hw = (fullW - gap) / 2, ox = x + hw + gap;
        int Column(int cx, string title, string tipText)
        {
            int cy = top + Section(title, Teal, cx, top, hw);
            var tip = new Label
            {
                Text = tipText, Font = F(8.5f), ForeColor = Gray, AutoSize = true, MaximumSize = new Size(hw, 0), Location = new Point(cx, cy),
            };
            Controls.Add(tip);
            return cy + tip.PreferredSize.Height + S(8);
        }
        int y = Math.Max(
            Column(x, "반복 주기 알림", "트리거 키를 누르면 정한 주기마다 알려요. 다시 누르면 처음부터 세요."),
            Column(ox, "단일 알림", "트리거 키를 누르면 정한 시간 뒤에 한 번 알려요. 다시 누르면 처음부터 세요."));
        // 3칸은 같은 자리에 겹쳐 두고 ◀ ▶로 한 칸씩 본다
        MakeSlotNav(x, y, hw, JanusWatch.RepeatSlots, () => repeatPage, v => repeatPage = v,
            i => st.Repeats[i].Title(i), i => Color.FromArgb(st.Repeats[i].Argb));
        y += MakeSlotNav(ox, y, hw, JanusWatch.OnceSlots, () => oncePage, v => oncePage = v,
            i => st.Onces[i].Title(i), i => Color.FromArgb(st.Onces[i].Argb)) + S(10);
        int h = 0;
        for (int i = 0; i < JanusWatch.RepeatSlots; i++) h = Math.Max(h, (repeatCards[i] = MakeRepeatCard(x, y, hw, i)).Height);
        for (int i = 0; i < JanusWatch.OnceSlots; i++) h = Math.Max(h, (onceCards[i] = MakeRepeatCard(ox, y, hw, i, once: true)).Height);
        foreach (var c in repeatCards.Concat(onceCards)) if (c != null) c.Height = h;
        y += h + S(20);

        y += Section("알림 띄울 모니터", Teal, x, y, fullW);
        y += MakeScreenCard(x, y, fullW).Height + S(10);
        return y;
    }

    /// <summary>◀ 1 / 2 페이지 ▶ 한 줄 (페이지마다 하나씩 있다). 높이를 돌려준다.</summary>
    int MakePageTabs(int x, int y, int w)
    {
        int bh = S(30), aw = S(40), g = S(6);
        RoundLabel Btn(string text, int left, int width, float size) => new()
        {
            AutoSize = false, Size = new Size(width, bh), TextAlign = ContentAlignment.MiddleCenter,
            Font = F(size, FontStyle.Bold), Radius = S(8), Text = text, Location = new Point(left, y),
        };
        var prev = Btn("◀", x, aw, 11);
        var label = Btn("", x + aw + g, w - (aw + g) * 2, 10.5f);
        label.Fill = UI.HoverBack; label.ForeColor = UI.T(Color.FromArgb(60, 60, 70));
        var next = Btn("▶", x + w - aw, aw, 11);
        prev.Click += (_, _) => { if (page > 0) ShowPage(page - 1); };
        next.Click += (_, _) => { if (page < PageCount - 1) ShowPage(page + 1); };
        Controls.Add(prev); Controls.Add(label); Controls.Add(next);
        pageNavs.Add((prev, label, next));
        return bh;
    }

    /// <summary>흰 바탕에서 읽히게: 밝은 색(노랑 등)은 조금 어둡게</summary>
    /// 다크 모드에서는 쨍한 색을 흰색 쪽으로 섞어 차콜 위에서 부드럽게 보이게 한다
    static Color Readable(Color c) => UI.Dark
        ? Color.FromArgb(c.R + (255 - c.R) * 2 / 5, c.G + (255 - c.G) * 2 / 5, c.B + (255 - c.B) * 2 / 5)
        : (c.R * 299 + c.G * 587 + c.B * 114) / 1000 > 165 ? Color.FromArgb(c.R * 3 / 4, c.G * 3 / 4, c.B * 3 / 4) : c;
    /// <summary>색이 살짝 비치는 칸 바탕 (다크에서는 조금 더 진하게)</summary>
    static Color Wash(Color c, int a = 24) => Color.FromArgb(UI.Dark ? a + 18 : a, c);

    /// <summary>반복 알림·단일 알림 한 칸: 켜기 / 트리거 키 / 주기(시·분·초) / 색 / 상태 · 미리 보기.
    /// 단일 알림(once)은 '반복 시간' 줄이 없고 시간은 분·초만 쓴다.</summary>
    Card MakeRepeatCard(int x, int y, int w, int i, bool once = false)
    {
        // 두 칸은 값 이름이 같아 아래에서 한 번에 다룬다
        RepeatSlot? rs = once ? null : st.Repeats[i];
        OnceSlot? os = once ? st.Onces[i] : null;
        int code = (once ? 50 : 30) + i;
        string defName = once ? $"단일 알림 {i + 1}" : $"반복 알림 {i + 1}";
        int Argb() => rs?.Argb ?? os!.Argb;
        void SetArgb(int v) { if (rs != null) rs.Argb = v; else os!.Argb = v; }
        string Title() => rs?.Title(i) ?? os!.Title(i);
        void Cancel() { if (once) watch.CancelOnce(i); else watch.CancelRepeat(i); }
        void SetOn(bool v) { if (rs != null) rs.On = v; else os!.On = v; }
        if (Array.IndexOf(RepeatSlot.Palette, Argb()) < 0) { SetArgb(RepeatSlot.Palette[2]); st.Save(); }
        var card = new Card { Location = new Point(x, y), Width = w, Radius = S(12) };
        Controls.Add(card);
        int ip = S(14), cy = S(12);

        var dot = new Label { Text = "●", Font = F(9), AutoSize = true, Location = new Point(ip, cy + S(3)) };
        // 이름 = 알림에 뜨는 글: 누르면 그 자리에서 바꾼다 (비우면 기본 이름 "반복 알림 1")
        var name = new Label { Text = Title(), Font = F(11, FontStyle.Bold), AutoSize = true, Location = new Point(ip + S(18), cy), Cursor = Cursors.IBeam };
        var sw = new Toggle { On = rs?.On ?? os!.On, Size = new Size(S(40), S(22)) };
        sw.Location = new Point(w - ip - sw.Width, cy + S(1));
        sw.Changed += v => { SetOn(v); if (!v) Cancel(); st.Save(); UpdateStatus(); };
        offAll.Add(() => { if (sw.On) { sw.On = false; SetOn(false); Cancel(); } });
        var (pen, box) = MakeNameEditor(name, ip, cy, sw.Left, defName, 20,
            Title, () => rs?.Name ?? os!.Name, v => { if (rs != null) rs.Name = v; else os!.Name = v; }, refreshNavs: true);
        card.Controls.AddRange(new Control[] { dot, name, pen, box, sw });
        cy += S(38);

        (once ? onceKeys : repeatKeys)[i] = KeyPill(card, "트리거 키", code, w, ip, ref cy);

        // 엔터 기다리기 · 조합 키 (일반 버프와 같은 방식)
        foreach (var (label, on, set, combo) in new (string, bool, Action<bool>, bool)[]
        {
            ("엔터를 누르면 엔터 시간부터 세기", rs?.WaitEnter ?? os!.WaitEnter, v => { if (rs != null) rs.WaitEnter = v; else os!.WaitEnter = v; }, false),
            ("조합 키 누른 채로 쓰기", rs?.Combo ?? os!.Combo, v => { if (rs != null) rs.Combo = v; else os!.Combo = v; }, true),
        })
        {
            card.Controls.Add(RowLabel(label, ip, cy - S(2)));
            var t = new Toggle { On = on, Size = new Size(S(40), S(22)) };
            t.Location = new Point(w - ip - t.Width, cy + S(2));
            t.Changed += v => { set(v); st.Save(); UpdateStatus(); };
            card.Controls.Add(t);
            if (combo) comboSws[code] = t;
            cy += S(34);
        }

        // [ ]시간 [ ]분 [ ]초 한 줄 (오른쪽부터 놓는다). 초 칸이 없으면 시간·분만, noHour면 분·초만
        void TimeRow(string label, int sec, bool withSec, Action<int> set, bool noHour = false)
        {
            card.Controls.Add(RowLabel(label, ip, cy));
            string[] units = { "시간", "분", "초" };
            int[] max = { 99, noHour ? 99 : 59, 59 };
            int[] vals = { Math.Min(99, sec / 3600), noHour ? Math.Min(99, sec / 60) : sec / 60 % 60, sec % 60 };
            int last = withSec ? 2 : 1, first = noHour ? 1 : 0;
            var boxes = new NumBox?[last + 1];
            int right = w - ip;
            for (int k = last; k >= first; k--)
            {
                var unit = new Label { Text = units[k], AutoSize = true, ForeColor = UI.T(Color.FromArgb(80, 80, 80)) };
                card.Controls.Add(unit);
                unit.Location = new Point(right - unit.PreferredWidth, cy + (S(30) - unit.PreferredHeight) / 2);
                var nb = new NumBox
                {
                    Maximum = max[k], Value = vals[k], Size = new Size(S(54), S(30)), Radius = S(8),
                    Font = F(10.5f, FontStyle.Bold), ForeColor = UI.Text,
                };
                nb.Location = new Point(unit.Left - S(4) - nb.Width, cy);
                card.Controls.Add(nb);
                boxes[k] = nb;
                right = nb.Left - S(10);
            }
            foreach (var nb in boxes)
                if (nb != null) nb.ValueChanged += (_, _) =>
                {
                    set((int)(boxes[0]?.Value ?? 0) * 3600 + (int)boxes[1]!.Value * 60 + (withSec ? (int)boxes[2]!.Value : 0));
                    st.Save(); UpdateStatus();
                };
            cy += S(40);
        }
        if (rs is RepeatSlot r)
        {
            TimeRow("주기", r.Sec, true, v => r.Sec = v);
            // 반복 시간: 키를 누른 뒤 이만큼만 반복하고 멈춘다
            TimeRow("반복 시간", r.UntilSec, false, v => r.UntilSec = v);
            var untilTip = new Label { Text = "0시간 0분이면 끌 때까지 반복해요", Font = F(8.5f), ForeColor = Gray, AutoSize = true };
            card.Controls.Add(untilTip);
            untilTip.Location = new Point(w - ip - untilTip.PreferredWidth, cy - S(8));
            cy += untilTip.PreferredHeight + S(4);
        }
        else TimeRow("시간", os!.Sec, true, v => os.Sec = v, noHour: true);

        // 색: 둥근 네모 8개 중 하나
        card.Controls.Add(RowLabel("색", ip, cy));
        int n = RepeatSlot.Palette.Length, sz = S(26), sg = S(5);
        var swatches = new RoundLabel[n];
        RoundLabel? look = null;
        void Paint()
        {
            var c = Color.FromArgb(Argb());
            dot.ForeColor = c;
            for (int k = 0; k < n; k++)
            {
                var pc = Color.FromArgb(RepeatSlot.Palette[k]);
                swatches[k].Text = RepeatSlot.Palette[k] == Argb() ? "✓" : "";
                swatches[k].ForeColor = (pc.R * 299 + pc.G * 587 + pc.B * 114) / 1000 > 165 ? Color.FromArgb(60, 60, 60) : Color.White;
            }
            if (look != null) { look.ForeColor = Readable(c); look.Fill = Wash(c); }
            UpdateStatus();
        }
        int sx = w - ip - (sz * n + sg * (n - 1));
        for (int k = 0; k < n; k++)
        {
            int argb = RepeatSlot.Palette[k];
            var s = new RoundLabel
            {
                AutoSize = false, Size = new Size(sz, sz), Radius = S(6), Fill = Color.FromArgb(argb), Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter, Font = F(9, FontStyle.Bold), Location = new Point(sx + (sz + sg) * k, cy + (S(30) - sz) / 2),
            };
            s.Click += (_, _) => { SetArgb(argb); st.Save(); Paint(); RefreshSlotNavs(); };
            card.Controls.Add(s);
            swatches[k] = s;
        }
        cy += S(42);

        // 상태 줄 · 미리 보기
        var c0 = Color.FromArgb(Argb());
        look = UI.Button("미리 보기", "", () => JanusWatch.PlayRepeat(sc, Title(), Color.FromArgb(Argb())), sc, fore: Readable(c0), back: Wash(c0));
        look.Font = F(9, FontStyle.Bold);
        card.Controls.Add(look);
        look.Location = new Point(w - ip - look.PreferredSize.Width, cy);
        var status = new Label { AutoSize = false, AutoEllipsis = true, Font = F(9.5f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft,
            Location = new Point(ip, cy), Size = new Size(look.Left - ip - S(6), look.PreferredSize.Height) };
        card.Controls.Add(status);
        (once ? onceStatus : repeatStatus)[i] = status;
        cy += look.PreferredSize.Height + S(12);
        Paint();

        card.Height = cy;
        return card;
    }

    /// <summary>목록·콤보 한 줄: 저장 값(모니터 이름 또는 ALL)과 보이는 글</summary>
    sealed record ScreenItem(string Key, string Text) { public override string ToString() => Text; }

    /// <summary>알림 띄울 모니터: 콤보(남은 모니터) · 추가 · 삭제 / 목록. ALL이 있으면 ALL만 둔다.</summary>
    Card MakeScreenCard(int x, int y, int w)
    {
        var card = new Card { Location = new Point(x, y), Width = w, Radius = S(12) };
        Controls.Add(card);
        int ip = S(14), cy = S(14), left = S(480);

        // 위: [모니터 고르기 ▾] [추가]   아래: 넣은 모니터 줄마다 [이름 ……… ×]
        int listW = S(420), addW = S(64);
        // 고르기 칸: 누르면 둥근 펼침 메뉴가 칸 바로 밑에 열린다
        var choices = new List<ScreenItem>();
        ScreenItem? picked = null;
        var combo = new RoundLabel
        {
            AutoSize = false, Size = new Size(listW - addW - S(8), S(36)), Radius = S(8), Fill = UI.HoverBack,
            TextAlign = ContentAlignment.MiddleLeft, Font = F(10, FontStyle.Bold), Location = new Point(ip, cy),
        };
        var arrow = new Label
        {
            Text = "▾", Font = F(11, FontStyle.Bold), AutoSize = false, Size = new Size(S(24), S(36)),
            TextAlign = ContentAlignment.MiddleCenter, BackColor = UI.HoverBack, Location = new Point(combo.Width - S(30), 0),
        };
        combo.Controls.Add(arrow);
        var menu = UI.Rounded(new ContextMenuStrip { Font = F(10), ShowImageMargin = false, ShowCheckMargin = true });
        void ShowPicked()
        {
            combo.Text = "   " + (picked?.Text ?? "더 넣을 모니터가 없어요");
            combo.ForeColor = arrow.ForeColor = picked != null ? UI.Text : UI.T(Color.FromArgb(170, 170, 176));
            combo.Cursor = arrow.Cursor = picked != null ? Cursors.Hand : Cursors.Default;
        }
        void Open()
        {
            if (choices.Count == 0) return;
            menu.Items.Clear();
            foreach (var c in choices)
            {
                var it = new ToolStripMenuItem(c.Text) { Checked = c == picked, AutoSize = false, Size = new Size(combo.Width - S(8), S(30)) };
                var cc = c;
                it.Click += (_, _) => { picked = cc; ShowPicked(); };
                menu.Items.Add(it);
            }
            menu.MinimumSize = new Size(combo.Width, 0);
            menu.Show(combo, new Point(0, combo.Height + S(4)));
        }
        combo.Click += (_, _) => Open();
        arrow.Click += (_, _) => Open();
        var add = new RoundLabel
        {
            AutoSize = false, Size = new Size(addW, combo.Height), Radius = S(8), Text = "추가",
            TextAlign = ContentAlignment.MiddleCenter, Font = F(9.5f, FontStyle.Bold), Cursor = Cursors.Hand,
            Location = new Point(ip + listW - addW, cy),
        };
        int rowH = S(36), rowGap = S(6);
        var list = new Panel
        {
            Location = new Point(ip, cy + combo.Height + S(12)),
            Size = new Size(listW, rowH * 3 + rowGap * 2), AutoScroll = true, BackColor = card.BackColor,
        };
        var look = UI.Button("미리 보기", "", () =>
            JanusAlert.Play(sc, "여기에 알림이 떠요", Teal, Color.FromArgb(200, 245, 240), AlertSound.Kind.Buff), sc,
            fore: Teal, back: Color.FromArgb(24, Teal));
        look.Font = F(9, FontStyle.Bold);
        var tip = new Label
        {
            Text = "알림을 띄울 모니터를 골라 추가하세요. 빼려면 ✕를 누르세요.\n" +
                   "ALL은 모든 모니터예요. 비어 있으면 모든 모니터에 떠요.",
            Font = F(8.5f), ForeColor = Gray, AutoSize = true, Location = new Point(ip + left, cy),
            MaximumSize = new Size(w - ip * 2 - left, 0),
        };
        look.Location = new Point(ip + left, cy + tip.PreferredSize.Height + S(10));
        card.Controls.AddRange(new Control[] { combo, add, list, tip, look });

        string Name(string key)
        {
            if (key == "ALL") return "ALL (모든 모니터)";
            var all = Screen.AllScreens;
            int k = Array.FindIndex(all, s => s.DeviceName == key);
            if (k < 0) return $"연결 안 된 모니터 ({key.Replace(@"\\.\", "")})";
            var b = all[k].Bounds;
            return $"모니터 {k + 1} ({b.Width}×{b.Height}{(all[k].Primary ? ", 주 모니터" : "")})";
        }
        void Fill()
        {
            // 연결된 모니터를 하나씩 전부 넣었으면 ALL 하나로 바꾼다
            var screens = Screen.AllScreens;
            if (!st.AlertScreens.Contains("ALL") && screens.Length > 0 && screens.All(s => st.AlertScreens.Contains(s.DeviceName)))
            {
                st.AlertScreens = new() { "ALL" };
                st.Save();
            }
            var keep = st.AlertScreens;
            list.SuspendLayout();
            foreach (Control c in list.Controls.Cast<Control>().ToList()) { list.Controls.Remove(c); c.Dispose(); }
            int ry = 0, rw = list.Width - (keep.Count > 3 ? SystemInformation.VerticalScrollBarWidth + S(2) : 0);
            foreach (var key in keep)
            {
                string k = key;
                bool missingScreen = key != "ALL" && !Screen.AllScreens.Any(s => s.DeviceName == key);
                // ✕ 칸 바탕을 줄 색과 맞추려고 흐린 청록을 불투명한 색으로 섞어 둔다
                var bg = card.BackColor;
                Color fill = key == "ALL"
                    ? Color.FromArgb(bg.R + (Teal.R - bg.R) * 24 / 255, bg.G + (Teal.G - bg.G) * 24 / 255, bg.B + (Teal.B - bg.B) * 24 / 255)
                    : UI.HoverBack;
                var row = new RoundLabel
                {
                    AutoSize = false, Size = new Size(rw, rowH), Radius = S(8), Location = new Point(0, ry),
                    Fill = fill,
                    ForeColor = missingScreen ? Gray : key == "ALL" ? Readable(Teal) : UI.Text,
                    Font = F(10, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft,
                    Text = "   " + (key == "ALL" ? "▣  " : "▢  ") + Name(key),
                };
                var x = new RoundLabel
                {
                    AutoSize = false, Size = new Size(S(28), S(28)), Radius = S(6), Text = "✕",
                    TextAlign = ContentAlignment.MiddleCenter, Font = F(9, FontStyle.Bold), Cursor = Cursors.Hand,
                    ForeColor = Gray, BackColor = fill, Location = new Point(rw - S(32), ry + (rowH - S(28)) / 2),
                };
                x.MouseEnter += (_, _) => { x.Fill = UI.Dark ? Color.FromArgb(60, 210, 50, 50) : Color.FromArgb(255, 228, 228); x.ForeColor = Readable(Color.FromArgb(210, 50, 50)); };
                x.MouseLeave += (_, _) => { x.Fill = Color.Transparent; x.ForeColor = Gray; };
                x.Click += (_, _) => { st.AlertScreens.Remove(k); st.Save(); BeginInvoke(Fill); };
                list.Controls.Add(x);
                list.Controls.Add(row);
                x.BringToFront();
                ry += rowH + rowGap;
            }
            if (keep.Count == 0)
                list.Controls.Add(new Label
                {
                    AutoSize = false, Size = new Size(list.Width, rowH), TextAlign = ContentAlignment.MiddleCenter,
                    Text = "비어 있어요 · 지금은 모든 모니터에 떠요", Font = F(9.5f), ForeColor = Gray,
                });
            list.ResumeLayout();
            choices.Clear();
            bool hasAll = keep.Contains("ALL");
            // ALL이 있으면 지우기 전까지 추가할 수 없다
            if (!hasAll)
            {
                choices.Add(new ScreenItem("ALL", Name("ALL")));
                foreach (var s in Screen.AllScreens)
                    if (!keep.Contains(s.DeviceName)) choices.Add(new ScreenItem(s.DeviceName, Name(s.DeviceName)));
            }
            picked = choices.FirstOrDefault();
            ShowPicked();
            bool can = picked != null;
            add.Tag = can;
            add.Fill = can ? UI.SoftAccent : UI.HoverBack;
            add.ForeColor = can ? UI.AccentText : UI.T(Color.FromArgb(190, 190, 196));
            add.Cursor = can ? Cursors.Hand : Cursors.Default;
        }
        add.Click += (_, _) =>
        {
            if (add.Tag is not true || picked is not ScreenItem it) return;
            if (it.Key == "ALL") st.AlertScreens = new() { "ALL" }; // ALL을 넣으면 나머지는 지운다
            else if (!st.AlertScreens.Contains(it.Key)) st.AlertScreens.Add(it.Key);
            st.Save(); Fill();
        };
        Fill();

        card.Height = Math.Max(list.Bottom, look.Bottom) + S(14);
        return card;
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
        var sw = new Toggle { On = b.On, Size = new Size(S(40), S(22)) };
        sw.Location = new Point(w - ip - sw.Width, cy + S(1));
        sw.Changed += v => { b.On = v; st.Save(); UpdateStatus(); };
        offAll.Add(() => { if (sw.On) { sw.On = false; b.On = false; } });
        var (pen, box) = MakeNameEditor(name, ip, cy, sw.Left, $"버프 {i + 1}", 12,
            Shown, () => b.Name, v => b.Name = v, refreshNavs: true);
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
            if (look != null) { look.ForeColor = Readable(c); look.Fill = Wash(c); }
        }
        void Change(int d)
        {
            idx = Math.Clamp(idx + d, 0, options.Length - 1);
            b.Min = options[idx]; st.Save();
            Paint(); UpdateStatus();
        }
        AddStepper(card, shown, w - ip, cy, Change);
        cy += S(40);

        // 엔터 때부터 세기: 켜면 키 다음 엔터도 눌렀을 때 엔터 시점으로 옮긴다 (끄면 키 누른 때 그대로)
        card.Controls.Add(RowLabel("엔터를 누르면 엔터 시간으로 알리기", ip, cy - S(2)));
        var waitSw = new Toggle { On = b.WaitEnter, Size = new Size(S(40), S(22)) };
        waitSw.Location = new Point(w - ip - waitSw.Width, cy + S(2));
        waitSw.Changed += v => { b.WaitEnter = v; st.Save(); UpdateStatus(); };
        card.Controls.Add(waitSw);
        cy += S(34);

        // 조합 키: 켜면 위 조합 키를 누른 채로 이 칸 키를 눌러야 센다
        card.Controls.Add(RowLabel("조합 키 누른 채로 쓰기", ip, cy - S(2)));
        var comboSw = new Toggle { On = b.Combo, Size = new Size(S(40), S(22)) };
        comboSw.Location = new Point(w - ip - comboSw.Width, cy + S(2));
        comboSw.Changed += v => { b.Combo = v; st.Save(); UpdateStatus(); };
        card.Controls.Add(comboSw);
        comboSws[20 + i] = comboSw;
        cy += S(34);

        // 세는 때: 키 누른 때 · 엔터 누른 때 중 하나로 나중에라도 바꾼다 (둘 다 남겨 둔다)
        card.Controls.Add(RowLabel("알릴 시간", ip, cy));
        int tr = w - ip;
        for (int k = 1; k >= 0; k--)
        {
            bool enter = k == 1;
            var t = new RoundLabel
            {
                AutoSize = false, Size = new Size(S(98), S(30)), TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand, Font = F(9.5f, FontStyle.Bold), Radius = S(8), Text = enter ? "엔터 누른 때" : "키 누른 때",
            };
            t.Location = new Point(tr - t.Width, cy);
            tr = t.Left - S(6);
            t.Click += (_, _) => { if (watch.UseBuffTime(i, enter)) UpdateStatus(); };
            card.Controls.Add(t);
            if (enter) buffUseEnter[i] = t; else buffUseKey[i] = t;
        }
        cy += S(40);

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
    RoundLabel ValueLabel(string text, int width = 64) =>
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
        int gap = S(8); // 값 칸과 − + 사이 여백
        shown.Location = new Point(plus.Left - gap - shown.Width, cy);
        minus.Location = new Point(shown.Left - gap - minus.PreferredSize.Width, top);
        return (minus, plus);
    }

    /// <summary>지금 값에서 d 방향으로 바로 다음 선택지 (없으면 그대로)</summary>
    static int StepOption(int[] options, int cur, int d) =>
        d > 0 ? options.Where(o => o > cur).DefaultIfEmpty(cur).Min() : options.Where(o => o < cur).DefaultIfEmpty(cur).Max();

    /// <summary>값 칸을 누르면 숫자를 직접 친다. Enter나 다른 곳을 누르면 확정, Esc는 취소. 범위 밖이면 범위 안으로 맞춘다</summary>
    void AddTypeIn(Control card, RoundLabel shown, Func<int> get, int min, int max, Action<int> apply)
    {
        // 글 칸처럼 보이게: 흰 바탕 둥근 네모 + 회색 테두리 (마우스를 올리면 파란 테두리)
        bool hover = false;
        shown.Fill = UI.Back;
        shown.Radius = S(8);
        shown.Cursor = Cursors.IBeam;
        shown.MouseEnter += (_, _) => { hover = true; shown.Invalidate(); };
        shown.MouseLeave += (_, _) => { hover = false; shown.Invalidate(); };
        shown.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var path = UI.RoundRect(new RectangleF(0.5f, 0.5f, shown.Width - 1.5f, shown.Height - 1.5f), S(8));
            using var pen = new Pen(hover ? UI.Accent : UI.T(Color.FromArgb(205, 205, 212)), hover ? 1.5f : 1f);
            e.Graphics.DrawPath(pen, path);
        };
        new ToolTip().SetToolTip(shown, "눌러서 직접 입력");
        var box = new TextField { Font = shown.Font, MaxLength = max.ToString().Length, DigitsOnly = true,
            TextAlign = HorizontalAlignment.Center, ImeMode = ImeMode.Disable, Visible = false };
        card.Controls.Add(box);
        bool done = true;
        void Finish(bool keep)
        {
            if (done) return;
            done = true;
            if (keep && int.TryParse(box.Text, out int v)) apply(Math.Clamp(v, min, max));
            box.Visible = false; shown.Visible = true;
            editingName = null;
        }
        shown.Click += (_, _) =>
        {
            done = false;
            box.Width = shown.Width;
            box.Location = new Point(shown.Left, shown.Top + (shown.Height - box.Height) / 2);
            box.Text = get().ToString();
            shown.Visible = false; box.Visible = true; box.BringToFront();
            box.Focus(); box.SelectAll();
        };
        box.Enter += (_, _) => editingName = Finish;
        box.Leave += (_, _) => Finish(true);
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
        new() { Text = text, AutoSize = true, ForeColor = UI.T(Color.FromArgb(80, 80, 80)), Location = new Point(x, y + S(5)) };

    void UpdateKeys()
    {
        Style(janusKey, 1, st.JanusKey, st.JanusMod);
        Style(npcKey, 3, st.NpcKey);
        Style(erdaKey, 8, st.ErdaKey);
        Style(comboKey, 2, st.ComboKey);
        Style(upKey, 4, st.WebtoonUpKey);
        Style(downKey, 5, st.WebtoonDownKey);
        Style(nextKey, 6, st.WebtoonNextKey);
        Style(prevKey, 7, st.WebtoonPrevKey);
        Style(shortsNextKey, 10, st.ShortsNextKey);
        Style(shortsPrevKey, 11, st.ShortsPrevKey);
        for (int i = 0; i < JanusWatch.BuffSlots; i++) Style(buffKeys[i], 20 + i, st.Buffs[i].Key);
        for (int i = 0; i < JanusWatch.DopeSlots; i++) Style(dopeKeys[i], 40 + i, st.Dopes[i].Key);
        for (int i = 0; i < JanusWatch.RepeatSlots; i++) Style(repeatKeys[i], 30 + i, st.Repeats[i].Key);
        for (int i = 0; i < JanusWatch.OnceSlots; i++) Style(onceKeys[i], 50 + i, st.Onces[i].Key);
    }

    static string Clock(double sec)
    {
        int t = (int)Math.Ceiling(sec);
        return t >= 3600 ? $"{t / 3600}:{t / 60 % 60:00}:{t % 60:00}" : $"{t / 60}:{t % 60:00}";
    }

    void Style(RoundLabel? pill, int which, int vk, int mod = 0)
    {
        if (pill == null) return;
        bool wait = capturing == which;
        pill.Text = wait ? (which == 1 && captureMod != 0 ? JanusWatch.KeyName(captureMod) + "+ 다음 키" : Waiting)
            : JanusWatch.KeyName(mod, vk);
        pill.Fill = wait ? UI.SoftAccent : UI.HoverBack;
        pill.ForeColor = wait ? UI.AccentText : vk == 0 ? Gray : UI.Text;
    }

    readonly List<Action> liveHints = new();

    void UpdateStatus()
    {
        foreach (var h in liveHints) h();
        bool recent = watch.LastKey is DateTime k && (DateTime.Now - k).TotalSeconds < 3;
        for (int i = 0; i < JanusWatch.RepeatSlots; i++)
        {
            var rl = repeatStatus[i];
            if (rl == null) break;
            var r = st.Repeats[i];
            bool seen = watch.LastRepeat == i && watch.LastRepeatAt is DateTime ra && (DateTime.Now - ra).TotalSeconds < 3;
            if (!r.On) rl.Text = "꺼져 있어요";
            else if (r.Key == 0) rl.Text = "트리거 키를 정해 주세요";
            else if (r.Sec < JanusWatch.RepeatMinSec) rl.Text = "주기를 5초 이상으로 정해 주세요";
            else if (watch.RepeatRemaining(i) is double rr)
                rl.Text = (watch.RepeatEndLeft(i) is double el ? $"다음 {Clock(rr)} · 끝까지 {Clock(el)}" : $"다음 알림까지 {Clock(rr)}") + (seen ? " · 인식됨" : "");
            else rl.Text = "트리거 키를 기다려요";
            rl.ForeColor = r.On ? Readable(Color.FromArgb(r.Argb)) : Gray;
        }
        for (int i = 0; i < JanusWatch.OnceSlots; i++)
        {
            var ol = onceStatus[i];
            if (ol == null) break;
            var o = st.Onces[i];
            bool seen = watch.LastOnce == i && watch.LastOnceAt is DateTime oa && (DateTime.Now - oa).TotalSeconds < 3;
            if (!o.On) ol.Text = "꺼져 있어요";
            else if (o.Key == 0) ol.Text = "트리거 키를 정해 주세요";
            else if (o.Sec < 1) ol.Text = "시간을 정해 주세요";
            else if (watch.OnceRemaining(i) is double orr) ol.Text = $"알림까지 {Clock(orr)}" + (seen ? " · 인식됨" : "");
            else ol.Text = "트리거 키를 기다려요";
            ol.ForeColor = o.On ? Readable(Color.FromArgb(o.Argb)) : Gray;
        }
        if (janusStatus != null && runeStatus != null)
        {

        if (!st.JanusOn) janusStatus.Text = "꺼져 있어요";
        else if (JanusWatch.Endless(st)) janusStatus.Text = "알림 주기를 넣어 주세요";
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
            else l.ForeColor = Readable(l == janusStatus ? Purple : Blue);
        }

        if (erdaStatus != null)
        {
            if (!st.ErdaOn) erdaStatus.Text = "꺼져 있어요";
            else if (st.ErdaKey == 0) erdaStatus.Text = "키를 정해 주세요";
            else if (watch.ErdaRemaining is double er) erdaStatus.Text = $"남은 시간 0:{(int)Math.Ceiling(er):00}" + (recent && watch.LastKind == "에르다" ? " · 인식됨" : "");
            else erdaStatus.Text = "키를 누르면 세기 시작해요";
            erdaStatus.ForeColor = st.ErdaOn ? Readable(JanusWatch.ErdaMain) : Gray;
        }

        for (int i = 0; i < JanusWatch.BuffSlots; i++)
        {
            var bl = buffStatus[i];
            if (bl == null) return;
            var b = st.Buffs[i];
            if (!b.On) bl.Text = "꺼져 있어요";
            else if (b.Key == 0) bl.Text = "버프 키를 정해 주세요";
            else if (watch.BuffRemaining(i) is double br)
                bl.Text = $"남은 시간 {(int)br / 60}:{(int)br % 60:00}"
                    + (b.Key == 0x0D ? "" : watch.BuffOnEnter(i) ? " · 엔터 시간으로 알림" : " · 키 시간으로 알림");
            else bl.Text = "버프 키를 기다려요";
            bl.ForeColor = b.On ? Readable(BuffText(b.Min)) : Gray;

            // 세는 때 고르기: 지금 세는 쪽은 색칠, 기록이 없거나 끝난 쪽은 흐리게
            if (buffUseKey[i] is RoundLabel uk && buffUseEnter[i] is RoundLabel ue)
            {
                var c = BuffText(b.Min);
                bool running = b.Start != null, onEnter = watch.BuffOnEnter(i);
                foreach (var (t, enter) in new[] { (uk, false), (ue, true) })
                {
                    // 키 시간·엔터 시간을 따로 같이 센다 (엔터를 안 눌렀으면 엔터 시간은 없다)
                    var left = watch.BuffTimeLeft(i, enter);
                    bool can = left != null;
                    bool sel = running && enter == onEnter && can;
                    t.Text = (enter ? "엔터 " : "키 ") + (left is double l ? $"{(int)Math.Ceiling(l) / 60}:{(int)Math.Ceiling(l) % 60:00}"
                        : enter && watch.BuffWaitingEnter(i) ? "기다림" : "없음");
                    t.Fill = sel ? Wash(c, 32) : UI.HoverBack;
                    t.ForeColor = sel ? Readable(c) : can ? UI.Text : UI.T(Color.FromArgb(190, 190, 190));
                    t.Cursor = can ? Cursors.Hand : Cursors.Default;
                }
            }
        }

        for (int i = 0; i < JanusWatch.DopeSlots; i++)
        {
            var dl = dopeStatus[i];
            if (dl == null) return;
            var d = st.Dopes[i];
            if (!d.On) dl.Text = "꺼져 있어요";
            else if (d.Key == 0) dl.Text = "도핑 사용 키를 정해 주세요";
            else if (watch.DopeProgress(i) == 0) dl.Text = "도핑 키 인식됨 · 방향키 한 번";
            else if (watch.DopeProgress(i) == 1) dl.Text = "방향키 인식됨 · 엔터를 누르세요";
            else if (watch.DopeProgress(i) > 1) dl.Text = "방향키를 한 번만 눌러야 해요";
            else if (watch.DopeWaiting(i) is double dw) dl.Text = $"사용 대기 {Math.Ceiling(dw)}초";
            else if (watch.DopeRemaining(i) is double dr) dl.Text = $"남은 시간 {(int)dr / 60}:{(int)dr % 60:00}" + (recent && watch.LastKind == "도핑" + i ? " · 인식됨" : "");
            else dl.Text = "도핑 키 → 방향키 → 엔터를 기다려요";
            dl.ForeColor = d.On ? Readable(JanusWatch.DopeColors(i).main) : Gray;
        }

        if (pendantStatus == null) return;
        pendantStatus.Text = !st.PendantOn ? "꺼져 있어요"
            : string.IsNullOrWhiteSpace(st.MapleNick) ? "닉네임을 넣어 주세요"
            : !st.Buffs[st.PendantBuff].On ? $"버프 {st.PendantBuff + 1} 칸이 꺼져 있어요"
            : PendantCheck.Status;
        pendantStatus.ForeColor = st.PendantOn ? Readable(PendantCheck.Blue) : Gray;
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
                // NumLock이 꺼져 있거나 Shift를 누른 채면 넘패드가 PageUp·화살표 등으로 온다 → 확장 키가 아니면 넘패드 키로 받는다
                if (KeyHook.NumpadOf((int)k) is int np and not 0 && ((msg.LParam.ToInt64() >> 24) & 1) == 0) k = (Keys)np;
                int vk = k == Keys.Back ? 0 : (int)k; // Del은 스킬 키로 쓰는 사람이 있어 지우기는 Backspace만
                if (capturing == 1)
                {
                    // 야누스: Shift·Ctrl·Alt를 먼저 누르면 다음 키와 같이 쓴다 (꾹 눌러 반복돼 와도 계속 기다린다).
                    // 그것만 눌렀다 떼면 그 키 하나로 정한다 (PreFilterMessage)
                    if (KeyHook.ModBit(vk) != 0) { captureMod = vk; UpdateKeys(); return true; }
                    st.JanusMod = vk == 0 ? 0 : captureMod;
                }
                // 버프·반복·도핑 키를 받을 때 조합 키를 먼저 누르면: 조합 키는 칸 키로 받지 않고 다음 키를 기다린다.
                // 그 다음 키를 칸 키로 받고 이 칸의 '조합 키 누른 채로 쓰기'를 켠다.
                if (capturing >= 20 && st.ComboKey != 0 && vk == st.ComboKey) { captureCombo = true; return true; }
                if (capturing >= 20 && captureCombo && vk != 0 && comboSws.TryGetValue(capturing, out var csw))
                {
                    switch (capturing)
                    {
                        case >= 20 and < 20 + JanusWatch.BuffSlots: st.Buffs[capturing - 20].Combo = true; break;
                        case >= 40 and < 40 + JanusWatch.DopeSlots: st.Dopes[capturing - 40].Combo = true; break;
                        case >= 30 and < 30 + JanusWatch.RepeatSlots: st.Repeats[capturing - 30].Combo = true; break;
                        case >= 50 and < 50 + JanusWatch.OnceSlots: st.Onces[capturing - 50].Combo = true; break;
                    }
                    csw.On = true;
                }
                captureCombo = false;
                switch (capturing)
                {
                    case 1: st.JanusKey = vk; break;
                    case 2: st.ComboKey = vk; break;
                    case 3: st.NpcKey = vk; break;
                    case 8: st.ErdaKey = vk; break;
                    case 4: st.WebtoonUpKey = vk; break;
                    case 5: st.WebtoonDownKey = vk; break;
                    case 6: st.WebtoonNextKey = vk; break;
                    case 7: st.WebtoonPrevKey = vk; break;
                    case 10: st.ShortsNextKey = vk; break;
                    case 11: st.ShortsPrevKey = vk; break;
                    case >= 20 and < 20 + JanusWatch.BuffSlots: st.Buffs[capturing - 20].Key = vk; break;
                    case >= 40 and < 40 + JanusWatch.DopeSlots: st.Dopes[capturing - 40].Key = vk; break;
                    case >= 30 and < 30 + JanusWatch.RepeatSlots: st.Repeats[capturing - 30].Key = vk; break;
                    case >= 50 and < 50 + JanusWatch.OnceSlots: st.Onces[capturing - 50].Key = vk; break;
                }
                st.Save();
            }
            capturing = 0; captureCombo = false; captureMod = 0;
            UpdateKeys();
            UpdateStatus();
            return true;
        }
        // 프리셋 이름 칸에서 엔터를 누르면 저장
        if (k == Keys.Enter && editingName == null && presetInput?.Focused == true) { SavePreset(); return true; }
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
        Application.AddMessageFilter(this);
    }

    [DllImport("user32.dll")] static extern IntPtr GetFocus();

    /// <summary>글 칸을 고치는 중에 다른 곳을 누르면 고치기를 끝낸다 (빈 곳·글자는 포커스를 가져가지 않아서 직접 끝낸다)</summary>
    public bool PreFilterMessage(ref Message m)
    {
        // 야누스 키를 받는 중 Shift·Ctrl·Alt만 눌렀다 떼면 그 키 하나를 야누스 키로 정한다.
        // Shift+넘패드에서 윈도우가 끼워 보내는 가짜 Shift 뗌은 확장 키 표시가 붙어 오므로 건너뛴다
        if (capturing == 1 && captureMod != 0 && (m.Msg == 0x101 || m.Msg == 0x105)
            && (int)m.WParam == captureMod && ((m.LParam.ToInt64() >> 24) & 1) == 0)
        {
            st.JanusKey = captureMod; st.JanusMod = 0; st.Save();
            capturing = 0; captureMod = 0;
            UpdateKeys(); UpdateStatus();
            return true;
        }
        const int WM_LBUTTONDOWN = 0x201, WM_RBUTTONDOWN = 0x204, WM_NCLBUTTONDOWN = 0xA1;
        if (editingName == null || m.Msg is not (WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_NCLBUTTONDOWN)) return false;
        var focus = GetFocus();
        if (m.HWnd == focus) return false;
        // 둥근 글 칸의 여백을 누른 건 같은 칸이다
        if (FromHandle(m.HWnd) is TextField tf && tf.ContainsFocus) return false;
        var fin = editingName;
        BeginInvoke(() => { if (editingName == fin) fin(true); });
        return false;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        refresh.Stop();
        refresh.Dispose(); // 닫힌 창은 다시 열지 않는다
        Application.RemoveMessageFilter(this);
        WinPlace.Remember(st, PlaceKey, this);
        base.OnFormClosed(e);
    }

    string PlaceKey => toon ? "toon" : "alert";

    void Drag(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        DragWindow(Handle); // 제목줄을 잡은 것처럼 끈다
        WinPlace.Remember(st, PlaceKey, this); // 다 끈 뒤 자리를 기억
    }

    /// <summary>둥근 테두리 카드</summary>
    sealed class Card : Panel
    {
        public int Radius { get; set; } = 12;
        readonly Pen pen = new(UI.Line); // 색·굵기가 고정이라 재사용
        public Card() { DoubleBuffered = true; ResizeRedraw = true; BackColor = UI.Back; }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = UI.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Radius);
            e.Graphics.DrawPath(pen, path);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) pen.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>켜고 끄는 알약 스위치 (켜면 주황)</summary>
    internal sealed class Toggle : Control
    {
        bool on;
        readonly SolidBrush knob = new(Color.White); // 손잡이 색은 고정이라 재사용
        public bool On { get => on; set { on = value; Invalidate(); } }
        public event Action<bool>? Changed;
        public Toggle()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = UI.Back;
            Cursor = Cursors.Hand;
        }
        protected override void OnClick(EventArgs e) { On = !On; Changed?.Invoke(On); base.OnClick(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var fill = on ? UI.Accent : UI.T(Color.FromArgb(210, 210, 210));
            if (!Enabled) fill = Color.FromArgb(90, fill); // 쓸 수 없을 땐 흐리게
            using (var path = UI.RoundRect(new RectangleF(0, 0, Width - 1, Height - 1), (Height - 1) / 2f))
            using (var b = new SolidBrush(fill))
                g.FillPath(b, path);
            float p = Height / 7f, d = Height - 1 - p * 2;
            g.FillEllipse(knob, on ? Width - 1 - p - d : p, p, d, d);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) knob.Dispose();
            base.Dispose(disposing);
        }
    }
}
