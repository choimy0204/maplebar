using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

using static MapleBar.Native;

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
    public static bool Endless(Settings st) => Endless(st, st.JanusSec);
    /// <summary>지속시간을 sec초로 했을 때 끝나지 않는지 (설정 창의 '무한대' 표시도 이걸 쓴다).</summary>
    public static bool Endless(Settings st, int sec) => st.PetOn ? sec >= GatherSec : st.LunaExt && sec >= 120;

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
    public static void PlayRune(float sc) => JanusAlert.Play(sc, "곧 룬이 나와요!", Color.FromArgb(40, 190, 255), Color.FromArgb(200, 240, 255), AlertSound.Kind.Rune);

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
            if (!Mute) PlayRune(sc);
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
