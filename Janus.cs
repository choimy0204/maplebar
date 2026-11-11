using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace MapleBar;

/// <summary>솔 야누스 키와 룬(NPC 키 + 방향키 4번)을 보고 남은 시간을 센다. 다 되면 보랏빛 알림을 띄운다.
/// 키보드 상태만 읽고 게임에 아무 입력도 보내지 않는다. 메이플 창을 쓰고 있을 때 누른 키만 센다.</summary>
sealed class JanusWatch
{
    /// <summary>바를 숨겼는지 (BarForm이 넣는다)</summary>
    public static bool BarHidden;
    // 바를 숨겼고 "바를 숨겨도 알림 받기"가 꺼져 있으면 알림만 띄우지 않는다 (시간은 계속 센다)
    bool Mute => BarHidden && !st.AlertWhenHidden;

    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);

    readonly Settings st;
    readonly float sc;
    readonly System.Windows.Forms.Timer poll = new() { Interval = 30 };
    bool janusDown;
    // 시작 시각은 설정 파일에 남겨 껐다 켜도 이어서 센다
    DateTime? endsAt => st.JanusStart is DateTime s
        ? (st.PetOn ? PetRun(s, st.PetGatherAt ?? s, st.JanusSec).end : s.AddSeconds(st.JanusSec)) : null;
    const double IgnoreSec = 50;     // 첫 설치 후 이 시간 안에 누른 건 추가 설치로 보고 무시
    /// <summary>끝나지 않는 경우는 세지도 알리지도 않는다.
    /// 루나 익스텐션 + 30레벨(120초), 또는 5석펫에서 지속시간이 게더링 주기(90초) 이상일 때.</summary>
    public static bool Endless(Settings st) => st.PetOn ? st.JanusSec >= GatherSec : st.LunaExt && st.JanusSec >= 120;

    // 5석펫: 게더링은 쿨타임 90초. 야누스가 있고 쿨이 돌았으면 바로 쓰고, 그 뒤로 90초마다 쓴다.
    // 게더링마다 남은 시간에 지속시간을 더하고 최대 300초. 설치 때 쿨이 돌아 있으면 20레벨은 160초로 시작한다.
    // 공략과 맞춰 봄: 20레벨 1분30초 150초 · 9분 100초 · 10분 40초 · 12분에 꺼짐, 10레벨 5분50초에 꺼짐
    const int GatherSec = 90, PetCapSec = 300;
    /// <summary>s에 설치(지속시간으로 시작)하고 a + 90초×k 마다 게더링이 나갈 때, 꺼지는 시각과
    /// until까지 마지막으로 나간 게더링 시각. 야누스가 꺼지면 게더링도 멈춘다.</summary>
    static (DateTime end, DateTime? last) PetRun(DateTime s, DateTime a, int sec, DateTime? until = null)
    {
        var end = s.AddSeconds(sec);
        DateTime? last = null;
        int k = (int)Math.Max(0, Math.Ceiling((s - a).TotalSeconds / GatherSec - 1e-6)); // s 이후 첫 게더링
        for (int n = 0; n < 1000; n++, k++)
        {
            var g = a.AddSeconds(GatherSec * (double)k);
            if (g >= end || until is DateTime u && g > u) break; // 게더링 전에 이미 꺼짐
            end = g.AddSeconds(Math.Min(PetCapSec, (end - g).TotalSeconds + sec));
            last = g;
        }
        return (end, last);
    }

    /// <summary>지금까지 마지막으로 나간 게더링 (쿨타임 계산용).</summary>
    DateTime? PetLastGather(DateTime now) =>
        st.JanusStart is DateTime s ? PetRun(s, st.PetGatherAt ?? s, st.JanusSec, now).last ?? st.PetLastGather : st.PetLastGather;

    /// <summary>세던 야누스를 지운다. 5석펫이면 다음 설치 때 쿨타임을 알 수 있게 마지막 게더링을 남긴다.</summary>
    void ClearJanus(DateTime now)
    {
        if (st.JanusStart == null) return;
        if (st.PetOn) st.PetLastGather = PetLastGather(now);
        st.JanusStart = null; st.PetGatherAt = null; st.Save();
    }
    // 아이템은 설치 1분 30초 뒤 사라지므로 65초에 회수하라고 알린다
    const double PickupSec = 65;
    readonly List<DateTime> pickups = new();
    DateTime? lastInstall;           // 마지막으로 센 설치 (50초 안의 추가 설치는 세지 않음)

    public DateTime? LastKey { get; private set; }
    public string? LastKind { get; private set; }
    /// <summary>남은 초. 세고 있지 않으면 null.</summary>
    public double? Remaining => endsAt is DateTime e ? Math.Max(0, (e - DateTime.Now).TotalSeconds) : null;

    public JanusWatch(Settings st, float sc)
    {
        this.st = st; this.sc = sc;
        AlertSound.St = st;
        // 꺼져 있는 동안 끝난 야누스는 조용히 지운다. 꺼져 있는 동안 나온 룬은 알림 없이 "나옴"으로 보여 준다
        var now = DateTime.Now;
        if (endsAt is DateTime je && now >= je) ClearJanus(je);
        lastInstall = st.JanusStart;
        runeAlerted = runeAt is DateTime re && now >= re.AddSeconds(RuneAlertLagSec);
        // 꺼져 있는 동안 버프가 끝난 지 오래면 조용히 지운다 (방금 끝났으면 켜자마자 알린다)
        for (int i = 0; i < BuffSlots; i++)
            if (BuffEnd(i) is DateTime be && now >= be.AddMinutes(1)) { st.Buffs[i].Start = null; st.Save(); }
        if (dopeEnd is DateTime de && now >= de.AddMinutes(1)) { st.DopeStart = null; st.Save(); }
        KeyHook.Start();
        poll.Tick += (_, _) => Tick();
        poll.Start();
    }

    // 룬: NPC 키를 누른 뒤 8초 안에 방향키 4번
    const double RuneWindow = 8;
    static readonly int[] Arrows = { 0x25, 0x26, 0x27, 0x28 };
    readonly bool[] arrowDown = new bool[4];
    bool npcDown;
    DateTime? npcAt;
    int arrowCount;
    bool runeAlerted;
    DateTime? runeAt => st.RuneStart is DateTime s ? s.AddMinutes(st.RuneMin) : null;
    const double RuneAlertLagSec = 8; // 시간은 그대로 세고, 알림만 8초 늦게 띄운다
    static readonly TimeSpan StaleAfter = TimeSpan.FromHours(1); // 끝난 뒤 이만큼 갱신이 없으면 사냥 종료로 본다
    /// <summary>룬 시간이 다 되어 맵에 룬이 있을 때 (다음에 룬을 풀 때까지 true)</summary>
    public bool RuneReady => runeAt is DateTime e && DateTime.Now >= e;
    /// <summary>룬이 다시 나올 때까지 남은 초. 세고 있지 않거나 이미 나왔으면 null.</summary>
    public double? RuneRemaining => runeAt is DateTime e && DateTime.Now < e ? (e - DateTime.Now).TotalSeconds : null;
    /// <summary>NPC 키 뒤에 지금까지 누른 방향키 수 (설정 창 표시용)</summary>
    public int RuneProgress => npcAt is DateTime t && (DateTime.Now - t).TotalSeconds < RuneWindow ? arrowCount : -1;

    void Tick()
    {
        if (KeyHook.Active)
        {
            // 누른 키를 하나씩, 누른 순서와 시각 그대로 처리한다 (2와 엔터가 한꺼번에 와도 둘 다 센다)
            foreach (var p in KeyHook.Drain())
            {
                int vk = p.Vk;
                if (vk == 0) continue;
                int mask = 0;
                for (int i = 0; i < BuffSlots; i++) if (vk == st.Buffs[i].Key) mask |= 1 << i;
                Step(p.At, Game.IsGame(p.Pid), vk == st.JanusKey, vk == st.NpcKey, mask, vk == 0x0D,
                    vk == st.DopeKey, Array.IndexOf(Arrows, vk) >= 0 ? 1 : 0);
            }
            Step(DateTime.Now, false, false, false, 0, false, false, 0); // 시간이 다 됐는지만 본다
            return;
        }
        // 키 알림을 못 받으면: 키를 뗐는지 계속 봐 두어야 다음 누름을 한 번으로 센다
        bool janus = Pressed(st.JanusKey, ref janusDown);
        bool npc = Pressed(st.NpcKey, ref npcDown);
        int buff = 0;
        for (int i = 0; i < BuffSlots; i++) if (Pressed(st.Buffs[i].Key, ref buffDown[i])) buff |= 1 << i;
        bool enter = Pressed(0x0D, ref enterDown);
        bool dope = Pressed(st.DopeKey, ref dopeDown);
        int arrows = 0;
        for (int i = 0; i < 4; i++) if (Pressed(Arrows[i], ref arrowDown[i])) arrows++;
        Step(DateTime.Now, Game.Foreground(), janus, npc, buff, enter, dope, arrows);
    }

    void Step(DateTime now, bool inGame, bool janus, bool npc, int buff, bool enter, bool dope, int arrows)
    {
        RuneTick(now, inGame, npc, arrows);
        BuffTick(now, inGame, buff, enter);
        DopeTick(now, inGame, dope, arrows, enter);
        if (!st.JanusOn)
        {
            pickups.Clear();
            ClearJanus(now);
            return;
        }

        // 여러 개를 이어서 설치하는 동안(첫 설치 후 50초)은 새 설치로 세지 않는다
        if (janus && inGame && !(lastInstall is DateTime li && (now - li).TotalSeconds < IgnoreSec))
        {
            lastInstall = now;
            LastKey = now; LastKind = "설치";
            if (st.PickupOn) pickups.Add(now.AddSeconds(PickupSec));
            // 루나 익스텐션이 없으면 도는 중에 다시 깔아도 갱신하지 않고 첫 설치만 센다
            bool running = endsAt is DateTime cur && cur > now;
            if (!Endless(st) && (st.LunaExt || !running))
            {
                // 5석펫: 게더링 쿨(90초)이 돌아 있으면 설치하자마자 한 번 더하고 지금부터 새 박자로 센다.
                // 도는 중에 다시 깔았거나 막 꺼진 뒤라 쿨이 남아 있으면 원래 박자대로 다음 게더링을 기다린다.
                if (st.PetOn)
                {
                    var last = PetLastGather(now);
                    st.PetGatherAt = last is DateTime l && (now - l).TotalSeconds < GatherSec ? l : now;
                }
                st.JanusStart = now; st.Save();
            }
        }

        // 아이템 회수 알림 (설치마다 한 번)
        if (!st.PickupOn) pickups.Clear();
        for (int i = pickups.Count - 1; i >= 0; i--)
            if (now >= pickups[i])
            {
                pickups.RemoveAt(i);
                if (!Mute) JanusAlert.Play(sc, "아이템을 회수하세요", Color.FromArgb(120, 220, 60), Color.FromArgb(225, 255, 200), AlertSound.Kind.Pickup);
            }

        if (Endless(st))
        {
            ClearJanus(now);
            return;
        }
        if (endsAt is DateTime end && now >= end)
        {
            ClearJanus(end);
            if (!Mute) JanusAlert.Play(sc);
        }
    }

    /// <summary>바의 칩을 눌렀을 때: 세던 시간을 지운다.</summary>
    public void CancelJanus() => ClearJanus(DateTime.Now);
    public void CancelRune() { npcAt = null; if (st.RuneStart != null) { st.RuneStart = null; st.Save(); } }
    public void CancelBuff(int i) { if (st.Buffs[i].Start != null) { st.Buffs[i].Start = null; st.Save(); } }

    // 버프 3칸: 칸마다 버프 키를 누르고 10초 안에 엔터까지 누르면(사용 확인) 그때부터 그 칸의 시간을 센다.
    // 다시 쓰면 처음부터 다시 센다. 키를 엔터로 정한 칸은 키 하나로 바로 센다
    public const int BuffSlots = 3;
    readonly bool[] buffDown = new bool[BuffSlots];
    bool enterDown;
    readonly DateTime?[] buffKeyAt = new DateTime?[BuffSlots]; // 버프 키를 누른 시각 (엔터를 기다리는 중)
    const double BuffEnterSec = 10;
    /// <summary>마지막으로 인식한 버프 칸 (설정 창의 "인식됨" 표시용)</summary>
    public int LastBuff { get; private set; } = -1;
    /// <summary>버프 키를 눌러 엔터를 기다리는 중인지 (설정 창 표시용)</summary>
    public bool BuffWaitingEnter(int i) => buffKeyAt[i] is DateTime t && (DateTime.Now - t).TotalSeconds <= BuffEnterSec;
    DateTime? BuffEnd(int i) => st.Buffs[i].Start is DateTime s ? s.AddMinutes(st.Buffs[i].Min) : null;
    /// <summary>버프가 끝날 때까지 남은 초. 세고 있지 않으면 null.</summary>
    public double? BuffRemaining(int i) => BuffEnd(i) is DateTime e ? Math.Max(0, (e - DateTime.Now).TotalSeconds) : null;
    public int BuffMin(int i) => st.Buffs[i].Min;
    /// <summary>30분은 분홍, 20분은 주황, 10분은 노랑</summary>
    public static (Color main, Color light) BuffColors(int min) => min >= 30
        ? (Color.FromArgb(235, 70, 140), Color.FromArgb(255, 212, 232))
        : min >= 20
        ? (Color.FromArgb(255, 140, 0), Color.FromArgb(255, 222, 180))
        : (Color.FromArgb(245, 205, 0), Color.FromArgb(255, 246, 185));
    /// <summary>버프가 끝났다고 알린다. 이름을 정해 두었으면 그 이름으로 알린다.</summary>
    public static void PlayBuff(float sc, int min, string? name = null)
    {
        var (main, light) = BuffColors(min);
        string text = string.IsNullOrWhiteSpace(name) ? $"{min}분 버프가 끝났어요!" : $"{name.Trim()} 끝났어요!";
        JanusAlert.Play(sc, text, main, light, AlertSound.Kind.Buff);
    }

    void BuffTick(DateTime now, bool inGame, int buff, bool enter)
    {
        // 엔터가 아닌 다른 버프 키를 새로 눌렀으면, 엔터를 기다리던 다른 칸은 쓰지 않은 것으로 본다
        int otherKey = 0;
        for (int i = 0; i < BuffSlots; i++)
            if ((buff >> i & 1) != 0 && st.Buffs[i].Key != 0x0D && st.Buffs[i].WaitEnter) otherKey = st.Buffs[i].Key;

        for (int i = 0; i < BuffSlots; i++)
        {
            var b = st.Buffs[i];
            if (!b.On || b.Key == 0)
            {
                buffKeyAt[i] = null;
                if (b.Start != null) { b.Start = null; st.Save(); }
                continue;
            }
            bool hit = (buff >> i & 1) != 0 && inGame;
            // 엔터를 기다리지 않는 칸, 또는 키가 엔터인 칸은 키 하나로 바로 센다
            if (hit && (b.Key == 0x0D || !b.WaitEnter)) Start(i);
            else if (hit) { buffKeyAt[i] = now; LastKey = now; LastKind = "버프 키"; LastBuff = i; }
            else if (inGame && otherKey != 0 && otherKey != b.Key) buffKeyAt[i] = null;
            else if (enter && inGame && buffKeyAt[i] is DateTime k)
            {
                buffKeyAt[i] = null;
                if ((now - k).TotalSeconds <= BuffEnterSec) Start(i);
            }
            if (BuffEnd(i) is DateTime end && now >= end)
            {
                b.Start = null; st.Save();
                if (!Mute) PlayBuff(sc, b.Min, b.Name);
            }
        }

        void Start(int i)
        {
            buffKeyAt[i] = null;
            st.Buffs[i].Start = now; st.Save();
            LastKey = now; LastKind = "버프"; LastBuff = i;
        }
    }

    // 부스트 도핑: 키를 누르면 바로 DopeSec초를 센다. 다시 쓰면 처음부터 다시 센다
    bool dopeDown;
    DateTime? dopeEnd => st.DopeStart is DateTime s ? s.AddSeconds(st.DopeSec) : null;
    // 사용 대기 중에는 아직 효과가 붙지 않았으니 바에 띄우지 않는다
    public double? DopeRemaining => dopeEnd is DateTime e && DopeWaiting == null ? Math.Max(0, (e - DateTime.Now).TotalSeconds) : null;
    /// <summary>쓰고 효과가 붙기를 기다리는 중이면 남은 초</summary>
    public double? DopeWaiting => st.DopeStart is DateTime s && s > DateTime.Now ? (s - DateTime.Now).TotalSeconds : null;
    public void CancelDope() { if (st.DopeStart != null) { st.DopeStart = null; st.Save(); } }
    public static readonly Color DopeMain = Color.FromArgb(235, 40, 50), DopeLight = Color.FromArgb(255, 205, 205);
    public static void PlayDope(float sc) => JanusAlert.Play(sc, "부스트 도핑이 끝났어요!", DopeMain, DopeLight, AlertSound.Kind.Dope);

    // 도핑 키 → 방향키 한 번(사용 확인 창에서 '예'로 옮김) → 엔터 순서로 눌러야 쓴 것으로 본다.
    // 방향키를 두 번 이상 누르면 다시 원래 버튼으로 돌아갈 수 있어서 세지 않는다
    DateTime? dopeKeyAt;
    int dopeArrows;
    const double DopeWindowSec = 10;
    /// <summary>도핑 키 뒤에 누른 방향키 수 (기다리는 중이 아니면 -1, 설정 창 표시용)</summary>
    public int DopeProgress => dopeKeyAt is DateTime t && (DateTime.Now - t).TotalSeconds <= DopeWindowSec ? dopeArrows : -1;

    void DopeTick(DateTime now, bool inGame, bool dope, int arrows, bool enter)
    {
        if (!st.DopeOn || st.DopeKey == 0)
        {
            dopeKeyAt = null;
            if (st.DopeStart != null) { st.DopeStart = null; st.Save(); }
            return;
        }
        if (dopeKeyAt is DateTime k0 && (now - k0).TotalSeconds > DopeWindowSec) dopeKeyAt = null;
        if (dope && inGame)
        {
            dopeKeyAt = now; dopeArrows = 0;
            LastKey = now; LastKind = "도핑 키";
        }
        else if (dopeKeyAt != null && inGame)
        {
            dopeArrows += arrows;
            if (enter)
            {
                bool ok = dopeArrows == 1;
                dopeKeyAt = null;
                if (ok)
                {
                    // 사용 대기가 있으면 그만큼 뒤에 효과가 붙으니 그때부터 센다
                    st.DopeStart = now.AddSeconds(st.DopeDelayOn ? st.DopeDelaySec : 0); st.Save();
                    LastKey = now; LastKind = "도핑";
                    return;
                }
            }
        }
        if (dopeEnd is DateTime end && now >= end)
        {
            st.DopeStart = null; st.Save();
            if (!Mute) PlayDope(sc);
        }
    }

    void RuneTick(DateTime now, bool inGame, bool npc, int arrows)
    {
        if (!st.RuneOn || st.NpcKey == 0)
        {
            npcAt = null;
            if (st.RuneStart != null) { st.RuneStart = null; st.Save(); }
            return;
        }
        if (inGame && npc) { npcAt = now; arrowCount = 0; } // 실패해서 다시 해도 처음부터 센다

        else if (npcAt is DateTime t && inGame && arrows > 0)
        {
            if ((now - t).TotalSeconds > RuneWindow) npcAt = null;
            else if ((arrowCount += arrows) >= 4)
            {
                npcAt = null;
                st.RuneStart = now; st.Save();
                runeAlerted = false;
                LastKey = now; LastKind = "룬 해방";
            }
        }
        // 나온 지 1시간이 지나도 다시 풀지 않으면 사냥을 끝낸 것으로 보고 조용히 지운다
        if (runeAt is DateTime old && now >= old + StaleAfter)
        {
            st.RuneStart = null; st.Save();
            runeAlerted = false;
            return;
        }
        // 다 되면 한 번만 알리고, 다음에 룬을 풀 때까지 "나옴"으로 둔다
        if (runeAt is DateTime r && now >= r.AddSeconds(RuneAlertLagSec) && !runeAlerted)
        {
            runeAlerted = true;
            if (!Mute) JanusAlert.Play(sc, "곧 룬이 나와요!", Color.FromArgb(40, 190, 255), Color.FromArgb(200, 240, 255), AlertSound.Kind.Rune);
        }
    }


    static bool Pressed(int vk, ref bool was)
    {
        if (vk == 0) { was = false; return false; }
        short s = GetAsyncKeyState(vk);
        bool down = (s & 0x8000) != 0;
        bool hit = !was && (down || (s & 1) != 0); // 아주 짧게 눌렀다 뗀 것도 잡는다
        was = down;
        return hit;
    }

    public static string KeyName(int vk) => vk == 0 ? "없음" : (Keys)vk switch
    {
        Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey => "Shift",
        Keys.ControlKey or Keys.LControlKey or Keys.RControlKey => "Ctrl",
        Keys.Menu or Keys.LMenu or Keys.RMenu => "Alt",
        >= Keys.D0 and <= Keys.D9 => ((char)vk).ToString(),
        Keys.Prior => "PageUp",
        Keys.Next => "PageDown",
        Keys.Oemtilde => "`",
        Keys.OemMinus => "-",
        Keys.Oemplus => "=",
        Keys.OemOpenBrackets => "[",
        Keys.OemCloseBrackets => "]",
        Keys.OemSemicolon => ";",
        Keys.OemQuotes => "'",
        Keys.Oemcomma => ",",
        Keys.OemPeriod => ".",
        Keys.OemQuestion => "/",
        Keys.OemPipe => "\\",
        var k => k.ToString(),
    };
}

/// <summary>게임 화면 가장자리를 보랏빛 기운이 한 바퀴 돌고, 가운데 위쪽에 "솔 야누스가 꺼졌어요"를 잠깐 띄운다.
/// 마우스와 키보드를 막지 않는 투명 창 네 개(위·아래·왼쪽·오른쪽 띠)에 그린다.</summary>
static class JanusAlert
{
    const double Lap = 1.3;   // 한 바퀴 도는 시간
    const double Fade = 1.8;  // 다 돈 뒤 테두리가 사라지는 시간
    static Color Main = Color.FromArgb(150, 80, 255);
    static Color Light = Color.FromArgb(228, 205, 255);
    static string Text = "";

    static Action? stop;

    /// <summary>기본은 솔 야누스(보라). 룬 알림은 글과 색만 바꿔 같은 효과를 쓴다.</summary>
    public static void Play(float sc, string text = "솔 야누스가 꺼졌어요", Color? main = null, Color? light = null,
        AlertSound.Kind sound = AlertSound.Kind.Janus)
    {
        AlertSound.Play(sound);
        stop?.Invoke();
        Text = text;
        Main = main ?? Color.FromArgb(150, 80, 255);
        Light = light ?? Color.FromArgb(228, 205, 255);
        // 모든 모니터에 띄운다. 게임이 있는 모니터는 게임 화면에 맞춘다
        var areas = Screen.AllScreens.Select(s => s.Bounds).ToList();
        if (Game.Area() is Rectangle gr)
        {
            var c = new Point(gr.X + gr.Width / 2, gr.Y + gr.Height / 2);
            int gi = areas.FindIndex(r => r.Contains(c));
            if (gi >= 0) areas[gi] = gr; else areas.Add(gr);
        }
        int t = Math.Max(8, (int)Math.Round(64 * sc));
        var sets = areas.Select(area =>
        {
            var rects = new[]
            {
                new Rectangle(0, 0, area.Width, t),
                new Rectangle(0, area.Height - t, area.Width, t),
                new Rectangle(0, t, t, area.Height - 2 * t),
                new Rectangle(area.Width - t, t, t, area.Height - 2 * t),
            };
            var strips = rects.Select(_ => new Splash()).ToArray();
            var bmps = rects.Select(r => new Bitmap(Math.Max(1, r.Width), Math.Max(1, r.Height))).ToArray();
            // 화면 전체를 물들이는 막: 가운데는 옅고 가장자리는 진하게 한 번만 그려 두고, 투명도만 바꾼다
            var tint = new Splash();
            using (var tb = Tint(area.Size)) tint.Present(tb, area.Location);
            tint.SetAlpha(0);
            return (area, rects, strips, bmps, tint, pill: new Splash());
        }).ToArray();
        var timer = new System.Windows.Forms.Timer { Interval = 15 };
        var start = DateTime.Now;

        stop = () =>
        {
            timer.Stop(); timer.Dispose();
            foreach (var x in sets)
            {
                x.tint.Dispose();
                foreach (var s in x.strips) s.Dispose();
                foreach (var b in x.bmps) b.Dispose();
                x.pill.Dispose();
            }
            stop = null;
        };

        timer.Tick += (_, _) =>
        {
            double el = (DateTime.Now - start).TotalSeconds;
            if (el > Lap + Fade) { stop?.Invoke(); return; }
            // 빛이 도는 동안 점점 물들고, 다 돌면 천천히 빠진다
            double ta = el < Lap ? Math.Sin(el / Lap * Math.PI / 2) : 1 - Math.Pow((el - Lap) / Fade, 1.5);
            // 글씨: 처음에 살짝 늦게 떠서 끝날 때 같이 사라진다
            double a = Math.Clamp((el - 0.15) / 0.3, 0, 1) * Math.Clamp((Lap + Fade - el) / 0.6, 0, 1);
            using var pb = Pill(a, sc);
            foreach (var (area, rects, strips, bmps, tint, pill) in sets)
            {
                tint.SetAlpha((byte)(255 * Math.Clamp(ta, 0, 1)));
                for (int i = 0; i < rects.Length; i++)
                {
                    using (var g = Graphics.FromImage(bmps[i]))
                    {
                        g.Clear(Color.Transparent);
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.TranslateTransform(-rects[i].X, -rects[i].Y);
                        Draw(g, area.Size, el, sc, rects[i]);
                    }
                    strips[i].Present(bmps[i], new Point(area.X + rects[i].X, area.Y + rects[i].Y));
                }
                pill.Present(pb, new Point(area.X + (area.Width - pb.Width) / 2, area.Y + (int)(area.Height * 0.28)));
            }
        };
        timer.Start();
    }

    static void Draw(Graphics g, Size size, double el, float sc, Rectangle clip)
    {
        float inset = 5 * sc;
        var r = new RectangleF(inset, inset, size.Width - 2 * inset, size.Height - 2 * inset);
        float len = 2 * (r.Width + r.Height);

        if (el < Lap)
        {
            double p = el / Lap;
            p = p < 0.5 ? 2 * p * p : 1 - Math.Pow(-2 * p + 2, 2) / 2; // 천천히 출발해 빨라졌다가 천천히 멈춘다
            float head = (float)(p * len);

            // 지나간 자리에 남는 옅은 선
            using (var pen = new Pen(Color.FromArgb(80, Main), 3 * sc) { LineJoin = LineJoin.Round })
            {
                var pts = Span(r, head);
                if (pts.Length >= 2) g.DrawLines(pen, pts);
            }

            // 머리와 꼬리: 머리에 가까울수록 크고 밝다
            float tail = len * 0.28f, step = Math.Max(2f, 4 * sc);
            for (float d = Math.Max(0, head - tail); d <= head; d += step)
            {
                float k = 1 - (head - d) / tail;
                var pt = At(r, d);
                float rad = (8 + 26 * k) * sc;
                if (pt.X + rad < clip.Left || pt.X - rad > clip.Right || pt.Y + rad < clip.Top || pt.Y - rad > clip.Bottom) continue;
                using (var b = new SolidBrush(Color.FromArgb(5 + (int)(50 * k * k), Main)))
                    g.FillEllipse(b, pt.X - rad, pt.Y - rad, rad * 2, rad * 2);
                float core = (3 + 8 * k) * sc;
                using (var b = new SolidBrush(Color.FromArgb((int)(210 * k * k * k), Light)))
                    g.FillEllipse(b, pt.X - core, pt.Y - core, core * 2, core * 2);
            }
        }
        else
        {
            // 다 돌고 나면 테두리 전체가 한 번 빛나고 사라진다
            double u = (el - Lap) / Fade;
            float a = (float)(1 - u * u);
            using (var pen = new Pen(Color.FromArgb((int)(50 * a), Main), 20 * sc)) g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
            using (var pen = new Pen(Color.FromArgb((int)(160 * a), Main), 4 * sc)) g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
            using (var pen = new Pen(Color.FromArgb((int)(140 * a), Light), 1.5f * sc)) g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
        }
    }

    // 둘레 위의 점: 위쪽 가운데에서 시작해 시계 방향으로 d만큼 간 곳
    static PointF At(RectangleF r, float d)
    {
        float w = r.Width, h = r.Height, len = 2 * (w + h);
        float s = ((d + w / 2) % len + len) % len;
        if (s < w) return new PointF(r.Left + s, r.Top); s -= w;
        if (s < h) return new PointF(r.Right, r.Top + s); s -= h;
        if (s < w) return new PointF(r.Right - s, r.Bottom); s -= w;
        return new PointF(r.Left, r.Bottom - s);
    }

    // 시작점부터 d까지 지나간 길 (모서리마다 꺾는다)
    static PointF[] Span(RectangleF r, float d)
    {
        float w = r.Width, h = r.Height;
        var list = new List<PointF> { At(r, 0) };
        foreach (var c in new[] { w / 2, w / 2 + h, w / 2 + h + w, w / 2 + 2 * h + w })
            if (c < d) list.Add(At(r, c));
        list.Add(At(r, d));
        return list.ToArray();
    }

    static Bitmap Tint(Size size)
    {
        var bmp = new Bitmap(Math.Max(1, size.Width), Math.Max(1, size.Height));
        using var g = Graphics.FromImage(bmp);
        using (var edge = new SolidBrush(Color.FromArgb(150, Main))) g.FillRectangle(edge, 0, 0, bmp.Width, bmp.Height);
        // 화면보다 큰 타원으로 가운데를 옅게 (가장자리로 갈수록 진해진다)
        var oval = new RectangleF(-size.Width * 0.2f, -size.Height * 0.25f, size.Width * 1.4f, size.Height * 1.5f);
        using var path = new GraphicsPath();
        path.AddEllipse(oval);
        using var pb = new PathGradientBrush(path)
        {
            CenterColor = Color.FromArgb(45, Main),
            SurroundColors = new[] { Color.FromArgb(150, Main) },
        };
        g.CompositingMode = CompositingMode.SourceCopy; // 겹쳐 칠하지 않고 바꿔 칠한다
        g.FillPath(pb, path);
        return bmp;
    }

    static Bitmap Pill(double a, float sc)
    {
        string text = Text;
        using var font = new Font("맑은 고딕", 30 * sc, FontStyle.Bold, GraphicsUnit.Pixel);
        Size ts;
        using (var tmp = new Bitmap(1, 1))
        using (var tg = Graphics.FromImage(tmp)) ts = Size.Ceiling(tg.MeasureString(text, font));
        int padX = (int)(22 * sc), padY = (int)(10 * sc);
        var bmp = new Bitmap(ts.Width + padX * 2, ts.Height + padY * 2);
        if (a <= 0) return bmp;
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAlias;
        var box = new RectangleF(1, 1, bmp.Width - 2, bmp.Height - 2);
        using var path = Round(box, box.Height / 2);
        using (var b = new SolidBrush(Color.FromArgb((int)(215 * a), Main.R / 4, Main.G / 4, Main.B / 4 + 10))) g.FillPath(b, path);
        using (var pen = new Pen(Color.FromArgb((int)(220 * a), Main), Math.Max(1f, 1.5f * sc))) g.DrawPath(pen, path);
        using (var b = new SolidBrush(Color.FromArgb((int)(255 * a), Color.White))) g.DrawString(text, font, b, padX, padY);
        return bmp;
    }

    static GraphicsPath Round(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 90, 180);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 180);
        p.CloseFigure();
        return p;
    }
}

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
    RoundLabel? nextKey, prevKey, dopeKey;
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

    int S(float v) => (int)Math.Round(v * sc);
    static Font F(float size, FontStyle style = FontStyle.Regular) => new("맑은 고딕", size * UI.Z, style);

    JanusForm(Settings st, JanusWatch watch, float sc, bool toon, bool buff)
    {
        this.st = st; this.watch = watch; this.sc = sc; this.toon = toon;
        CloseKeys.Track(this);
        Text = toon ? "웹툰 키" : "알림 설정";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.White;
        ForeColor = UI.Text;
        Font = F(10);
        DoubleBuffered = true;

        // 알림 창은 두 줄(야누스·룬 | 버프·도핑)이라 넓게
        int pad = S(18), W = toon ? S(372) : pad * 2 + S(352) * 2 + S(28), y = S(14);

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
        var janusCard = MakeCard(lx, y, colW, Purple, "솔 야누스", st.JanusOn, v => st.JanusOn = v,
            "솔 야누스 키", 1, out var jk,
            "지속시간", st.JanusSec, new[] { 60, 70, 80, 120 }, v => (st.PetOn ? v >= 90 : v >= 120 && st.LunaExt) ? "무한대" : v + "초", v => st.JanusSec = v,
            "1레벨 60초 · 10레벨 70초 · 20레벨 80초 · 30레벨 120초(루나 익스텐션이 있으면 무한대)\n" +
            "첫 설치 후 50초 안에 더 설치한 건 세지 않아요. 루나 익스텐션이 없으면 다시 깔아도 갱신하지 않고 첫 설치만 세요.\n" +
            "5석펫을 켜면 설치 때 한 번, 그 뒤 90초마다 게더링으로 지속시간만큼 더해서(최대 300초) 실제로 꺼질 때 알려 드려요. 20레벨은 약 12분, 10레벨은 약 5분 50초 유지돼요.\n" +
            "아이템은 설치 1분 30초 뒤 사라져서, 65초에 연두색 빛으로 회수하라고 알려 드려요.",
            () => JanusAlert.Play(sc), out var js,
            new (string, bool, Action<bool>)[]
            {
                ("루나 익스텐션", st.LunaExt, v => st.LunaExt = v),
                ("5석펫 (90초마다 게더링)", st.PetOn, v => st.PetOn = v),
                ("아이템 회수 알림", st.PickupOn, v => st.PickupOn = v),
            });
        janusKey = jk; janusStatus = js;
        y += janusCard.Height + S(10);

        var runeCard = MakeCard(lx, y, colW, Blue, "룬", st.RuneOn, v => st.RuneOn = v,
            "NPC 대화 키", 3, out var nk,
            "룬 주기", st.RuneMin, Enumerable.Range(1, 60).ToArray(), v => v + "분", v => st.RuneMin = v,
            "평소 15분, 이벤트 땐 10분이에요. NPC 키를 누르고 방향키 4번을 누르면 세기 시작하고, 실패해서 다시 풀면 처음부터 다시 셉니다.",
            () => JanusAlert.Play(sc, "곧 룬이 나와요!", Color.FromArgb(40, 190, 255), Color.FromArgb(200, 240, 255), AlertSound.Kind.Rune), out var rs);
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

        // 도핑은 더 짧은 줄 아래에 붙여 창이 세로로 너무 길어지지 않게 한다
        bool dopeLeft = leftEnd <= rightEnd;
        int dx = dopeLeft ? lx : rx;
        y = dopeLeft ? leftEnd : rightEnd;
        y += S(6);
        y += Section("부스트 도핑", JanusWatch.DopeMain, dx, y, colW);
        var dopeCard = MakeCard(dx, y, colW, JanusWatch.DopeMain, "부스트 도핑", st.DopeOn, v => st.DopeOn = v,
            "도핑 사용 키", 9, out var dk,
            "지속시간", st.DopeSec, Enumerable.Range(3, 28).Select(i => i * 10).ToArray(), v => v + "초", v => st.DopeSec = v,
            "도핑 키 → 방향키 한 번 → 엔터를 10초 안에 누르면 쓴 것으로 보고, 사용 대기(기본 3초) 뒤부터 세요. 끝나면 빨간색으로 알려 드려요. 다시 쓰면 처음부터 다시 셉니다.",
            () => JanusWatch.PlayDope(sc), out var ds,
            new (string, bool, Action<bool>)[] { ("사용 대기", st.DopeDelayOn, v => st.DopeDelayOn = v) },
            ("대기 시간", st.DopeDelaySec, Enumerable.Range(1, 10).ToArray(), v => v + "초", v => st.DopeDelaySec = v));
        dopeKey = dk; dopeStatus = ds;
        y += dopeCard.Height + S(10);
        y = Math.Max(y, dopeLeft ? rightEnd : leftEnd);

        // 두 줄 사이 세로선
        Controls.Add(new Panel { BackColor = Color.FromArgb(225, 225, 230), Bounds = new Rectangle(pad + colW + gap / 2, top + S(4), Math.Max(1, S(1)), y - top - S(14)) });
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
        var shown = new Label { AutoSize = false, Size = new Size(S(64), S(30)), TextAlign = ContentAlignment.MiddleCenter, Font = F(10, FontStyle.Bold), Text = vol + "%" };
        void Change(int d)
        {
            vol = Math.Clamp(vol + d, 10, 100);
            shown.Text = vol + "%";
            st.SoundVol = vol; st.Save();
            AlertSound.Play(AlertSound.Kind.Janus, vol);
        }
        var plus = UI.Button("+", "", () => Change(10), sc, back: UI.HoverBack);
        var minus = UI.Button("−", "", () => Change(-10), sc, back: UI.HoverBack);
        plus.Font = minus.Font = F(10, FontStyle.Bold);
        card.Controls.AddRange(new Control[] { minus, shown, plus });
        int top = cy + (S(30) - plus.PreferredSize.Height) / 2;
        plus.Location = new Point(right - plus.PreferredSize.Width, top);
        shown.Location = new Point(plus.Left - shown.Width, cy);
        minus.Location = new Point(shown.Left - minus.PreferredSize.Width, top);
        if (wide) volLbl.Left = minus.Left - volLbl.PreferredWidth - S(8);
        cy += S(38);

        // 끄면 크기 줄을 흐리게 해서 소리가 안 난다는 걸 보여 준다
        void Dim() { shown.ForeColor = st.SoundOn ? UI.Text : Gray; plus.Enabled = minus.Enabled = st.SoundOn; }
        sw.Changed += v => { st.SoundOn = v; st.Save(); Dim(); if (v) AlertSound.Play(AlertSound.Kind.Janus); };
        Dim();

        card.Height = cy;
        return card;
    }

    Card MakeCard(int x, int y, int w, Color color, string title, bool on, Action<bool> setOn,
        string keyLabel, int which, out RoundLabel keyPill,
        string stepLabel, int value, int[] options, Func<int, string> fmt, Action<int> setValue,
        string hint, Action preview, out Label status, (string label, bool on, Action<bool> set)[]? switches = null,
        (string label, int value, int[] options, Func<int, string> fmt, Action<int> set)? step2 = null)
    {
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
        int idx = 0;
        for (int i = 1; i < options.Length; i++)
            if (Math.Abs(options[i] - value) < Math.Abs(options[idx] - value)) idx = i;
        var shown = new Label { AutoSize = false, Size = new Size(S(64), S(30)), TextAlign = ContentAlignment.MiddleCenter, Font = F(10, FontStyle.Bold), Text = fmt(options[idx]) };
        if (options[idx] != value) { setValue(options[idx]); st.Save(); }
        void Change(int d)
        {
            idx = Math.Clamp(idx + d, 0, options.Length - 1);
            shown.Text = fmt(options[idx]);
            setValue(options[idx]); st.Save();
        }
        var plus = UI.Button("+", "", () => Change(1), sc, back: UI.HoverBack);
        var minus = UI.Button("−", "", () => Change(-1), sc, back: UI.HoverBack);
        plus.Font = minus.Font = F(10, FontStyle.Bold);
        card.Controls.AddRange(new Control[] { minus, shown, plus });
        int bh = plus.PreferredSize.Height, top = cy + (S(30) - bh) / 2;
        plus.Location = new Point(w - ip - plus.PreferredSize.Width, top);
        shown.Location = new Point(plus.Left - shown.Width, cy);
        minus.Location = new Point(shown.Left - minus.PreferredSize.Width, top);
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
            int i2 = 0;
            for (int i = 1; i < o2.Length; i++)
                if (Math.Abs(o2[i] - v2) < Math.Abs(o2[i2] - v2)) i2 = i;
            var shown2 = new Label { AutoSize = false, Size = new Size(S(64), S(30)), TextAlign = ContentAlignment.MiddleCenter, Font = F(10, FontStyle.Bold), Text = f2(o2[i2]) };
            void Change2(int d) { i2 = Math.Clamp(i2 + d, 0, o2.Length - 1); shown2.Text = f2(o2[i2]); s2(o2[i2]); st.Save(); }
            var plus2 = UI.Button("+", "", () => Change2(1), sc, back: UI.HoverBack);
            var minus2 = UI.Button("−", "", () => Change2(-1), sc, back: UI.HoverBack);
            plus2.Font = minus2.Font = F(10, FontStyle.Bold);
            card.Controls.AddRange(new Control[] { minus2, shown2, plus2 });
            int top2 = cy + (S(30) - plus2.PreferredSize.Height) / 2;
            plus2.Location = new Point(w - ip - plus2.PreferredSize.Width, top2);
            shown2.Location = new Point(plus2.Left - shown2.Width, cy);
            minus2.Location = new Point(shown2.Left - minus2.PreferredSize.Width, top2);
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

        // 속도: − 값 +
        card.Controls.Add(RowLabel("속도", ip, cy));
        int[] speeds = { 300, 450, 600, 900, 1200, 1600, 2200, 3000 };
        int idx = 0;
        for (int i = 1; i < speeds.Length; i++)
            if (Math.Abs(speeds[i] - st.WebtoonSpeed) < Math.Abs(speeds[idx] - st.WebtoonSpeed)) idx = i;
        string Fmt(int i) => $"{i + 1}단계";
        var shown = new Label { AutoSize = false, Size = new Size(S(64), S(30)), TextAlign = ContentAlignment.MiddleCenter, Font = F(10, FontStyle.Bold), Text = Fmt(idx) };
        void Change(int d) { idx = Math.Clamp(idx + d, 0, speeds.Length - 1); shown.Text = Fmt(idx); st.WebtoonSpeed = speeds[idx]; st.Save(); }
        var plus = UI.Button("+", "", () => Change(1), sc, back: UI.HoverBack);
        var minus = UI.Button("−", "", () => Change(-1), sc, back: UI.HoverBack);
        plus.Font = minus.Font = F(10, FontStyle.Bold);
        card.Controls.AddRange(new Control[] { minus, shown, plus });
        int bh = plus.PreferredSize.Height, top = cy + (S(30) - bh) / 2;
        plus.Location = new Point(w - ip - plus.PreferredSize.Width, top);
        shown.Location = new Point(plus.Left - shown.Width, cy);
        minus.Location = new Point(shown.Left - minus.PreferredSize.Width, top);
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
        var shown = new Label { AutoSize = false, Size = new Size(S(56), S(30)), TextAlign = ContentAlignment.MiddleCenter, Font = F(10, FontStyle.Bold) };
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
        var plus = UI.Button("+", "", () => Change(1), sc, back: UI.HoverBack);
        var minus = UI.Button("−", "", () => Change(-1), sc, back: UI.HoverBack);
        plus.Font = minus.Font = F(10, FontStyle.Bold);
        card.Controls.AddRange(new Control[] { minus, shown, plus });
        int top = cy + (S(30) - plus.PreferredSize.Height) / 2;
        plus.Location = new Point(w - ip - plus.PreferredSize.Width, top);
        shown.Location = new Point(plus.Left - shown.Width, cy);
        minus.Location = new Point(shown.Left - minus.PreferredSize.Width, top);
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
                int vk = k is Keys.Delete or Keys.Back ? 0 : (int)k;
                switch (capturing)
                {
                    case 1: st.JanusKey = vk; break;
                    case 3: st.NpcKey = vk; break;
                    case 4: st.WebtoonUpKey = vk; break;
                    case 5: st.WebtoonDownKey = vk; break;
                    case 6: st.WebtoonNextKey = vk; break;
                    case 7: st.WebtoonPrevKey = vk; break;
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
        get { var cp = base.CreateParams; cp.ClassStyle |= 0x20000; return cp; } // 그림자
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
