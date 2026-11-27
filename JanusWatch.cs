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
    DateTime? endsAt => st.JanusStart is DateTime s ? s.AddSeconds(Cycle(st)) : null;
    /// <summary>알림 주기(초). 예전 설정(지속시간 + 5석펫)이면 처음 한 번 주기로 바꿔 둔다.</summary>
    public static int Cycle(Settings st)
    {
        if (st.JanusCycleSec is int c) return c;
        int sec = st.JanusSec;
        c = sec > 120 || (st.PetOn && sec >= GatherSec) ? 0   // 예전 무한대는 세지 않음
            : st.PetOn ? (int)(PetRun(DateTime.MinValue, DateTime.MinValue, sec).end - DateTime.MinValue).TotalSeconds
            : sec;
        st.JanusCycleSec = c; st.Save();
        return c;
    }
    const double IgnoreSec = 50;     // 첫 설치 후 이 시간 안에, 이어서 누른 건 추가 설치로 보고 무시
    const double BurstSec = 10;      // 앞서 누른 지 이 시간 안이면 "이어서 누른 것". 그보다 쉬었다 누르면 다시 깐 것으로 센다
    const double ReinstallSec = 15;  // 남은 시간이 이만큼 이하일 때 누르면 50초 안이어도 다시 깐 것으로 센다
    /// <summary>알림 주기가 0이면 세지도 알리지도 않는다.</summary>
    public static bool Endless(Settings st) => Cycle(st) <= 0;

    // (예전 설정을 알림 주기로 옮길 때만 쓴다) 5석펫: 게더링은 쿨타임 90초. 야누스가 있고 쿨이 돌았으면 바로 쓰고, 그 뒤로 90초마다 쓴다.
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

    /// <summary>세던 야누스를 지운다.</summary>
    void ClearJanus(DateTime now)
    {
        if (st.JanusStart == null) return;
        st.JanusStart = null; st.Save();
    }
    DateTime? lastInstall;           // 마지막으로 센 설치 (50초 안의 추가 설치는 세지 않음)
    DateTime? lastJanusPress;        // 게임에서 마지막으로 누른 설치 키 (센 것이든 무시한 것이든)

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
        lastInstall = lastJanusPress = st.JanusStart;
        runeAlerted = runeAt is DateTime re && now >= re.AddSeconds(RuneAlertLagSec);
        // 꺼져 있는 동안 버프가 끝난 지 오래면 조용히 지운다 (방금 끝났으면 켜자마자 알린다)
        for (int i = 0; i < BuffSlots; i++)
            if (BuffEnd(i) is DateTime be && now >= be.AddMinutes(1)) { st.Buffs[i].Start = null; st.Save(); }
        for (int i = 0; i < DopeSlots; i++)
            if (DopeEnd(i) is DateTime de && now >= de.AddMinutes(1)) { st.Dopes[i].Start = null; st.Save(); }
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

    bool ComboOnBuff(int i) => st.ComboKey != 0 && st.Buffs[i].Combo;
    bool ComboOnDope(int i) => st.ComboKey != 0 && st.Dopes[i].Combo;
    bool ComboOnRepeat(int i) => st.ComboKey != 0 && st.Repeats[i].Combo;
    bool ComboOnOnce(int i) => st.ComboKey != 0 && st.Onces[i].Combo;

    /// <summary>키 하나를 눌렀을 때 셀 버프·도핑 칸.
    /// 조합 키를 켠 칸은 조합 키를 누른 채로 눌렀을 때만 센다.
    /// 조합 키를 안 켠 칸은 평소처럼 세지만, 같은 키를 조합 키로 쓰는 칸이 있으면 조합 키를 누른 채일 땐 그 칸에 양보한다.</summary>
    (int buff, int dope, int rep, int once) SlotMasks(int vk, bool held)
    {
        if (vk == 0) return (0, 0, 0, 0);
        bool comboTaken = false;
        if (held)
        {
            for (int i = 0; i < BuffSlots; i++) if (ComboOnBuff(i) && KeyIs(st.Buffs[i].Key, vk)) comboTaken = true;
            for (int i = 0; i < DopeSlots; i++) if (ComboOnDope(i) && KeyIs(st.Dopes[i].Key, vk)) comboTaken = true;
            for (int i = 0; i < RepeatSlots; i++) if (ComboOnRepeat(i) && KeyIs(st.Repeats[i].Key, vk)) comboTaken = true;
            for (int i = 0; i < OnceSlots; i++) if (ComboOnOnce(i) && KeyIs(st.Onces[i].Key, vk)) comboTaken = true;
        }
        bool Hit(int key, bool combo) => KeyIs(key, vk) && (combo ? held : !comboTaken);
        int b = 0, d = 0, r = 0, o = 0;
        for (int i = 0; i < BuffSlots; i++) if (Hit(st.Buffs[i].Key, ComboOnBuff(i))) b |= 1 << i;
        for (int i = 0; i < DopeSlots; i++) if (Hit(st.Dopes[i].Key, ComboOnDope(i))) d |= 1 << i;
        for (int i = 0; i < RepeatSlots; i++) if (Hit(st.Repeats[i].Key, ComboOnRepeat(i))) r |= 1 << i;
        for (int i = 0; i < OnceSlots; i++) if (Hit(st.Onces[i].Key, ComboOnOnce(i))) o |= 1 << i;
        return (b, d, r, o);
    }

    // 누른 키 기록은 테스트 빌드에서만 켤 수 있다 (배포판은 무엇을 해도 키를 기록하지 않는다)
#if DEBUG
    static readonly bool Debug = Environment.GetEnvironmentVariable("MB_DEBUG") == "1" || TestFlag("test-keylog");
#else
    const bool Debug = false;
#endif
    bool hookOffLogged;
    // 바는 관리자 권한으로 켜져 환경 변수가 안 넘어올 수 있어, 설정 폴더의 test-keylog 파일로도 키 기록을 켠다
    static bool TestFlag(string name) => File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MapleBar", name));

    // 훅에 알려 줄 키 목록. 틱마다 새로 만들지 않고 같은 칸에 다시 채워, 지난번과 다를 때만 훅에 알린다
    readonly List<int> hookKeys = new(), hookKeysSent = new();

    void SyncHookKeys()
    {
        hookKeys.Clear();
        foreach (var b in st.Buffs) hookKeys.Add(b.Key);
        foreach (var d in st.Dopes) hookKeys.Add(d.Key);
        foreach (var r in st.Repeats) hookKeys.Add(r.Key);
        foreach (var o in st.Onces) hookKeys.Add(o.Key);
        hookKeys.AddRange(Arrows);
        hookKeys.Add(st.JanusKey); hookKeys.Add(st.NpcKey); hookKeys.Add(st.ComboKey); hookKeys.Add(st.ErdaKey); hookKeys.Add(0x0D);
        if (CollectionsMarshal.AsSpan(hookKeys).SequenceEqual(CollectionsMarshal.AsSpan(hookKeysSent))) return;
        hookKeysSent.Clear(); hookKeysSent.AddRange(hookKeys);
        KeyHook.Watch(hookKeys);
    }

    void Tick()
    {
        KeyHook.ComboVk = st.ComboKey;
        SyncHookKeys();
        // 전체 알림을 꺼 두면: 누른 키는 버리고 세지도 울리지도 않는다. 끄는 순간 세던 것은 모두 멈춘다
        if (!st.AlertsOn)
        {
            if (alertsWereOn) { alertsWereOn = false; StopAll(); }
            if (KeyHook.Active) KeyHook.Drain();
            return;
        }
        alertsWereOn = true;
        MidnightTick(DateTime.Now);
        if (KeyHook.Active)
        {
            // 누른 키를 하나씩, 누른 순서와 시각 그대로 처리한다 (2와 엔터가 한꺼번에 와도 둘 다 센다)
            foreach (var p in KeyHook.Drain())
            {
                // 훅은 왼쪽·오른쪽 Shift·Ctrl·Alt를 따로 알려 준다. 설정 키와는 KeyIs로 맞춘다
                int vk = p.Vk;
                if (vk == 0) continue;
                bool g = Game.IsGame(p.Pid);
                var (mask, dmask, rmask, omask) = SlotMasks(vk, p.Combo);
                if (Debug && (vk == st.ComboKey || st.Buffs.Any(b => b.Key == vk) || st.Dopes.Any(d => d.Key == vk) || vk == 0x0D))
                    Log.Write("키", $"vk={vk} 조합={p.Combo} 게임={g} pid={p.Pid} 버프칸={mask} 도핑칸={dmask}");
                for (int i = 0; i < RepeatSlots; i++) if ((rmask >> i & 1) != 0) RepeatKey(i, p.At, g);
                for (int i = 0; i < OnceSlots; i++) if ((omask >> i & 1) != 0) OnceKey(i, p.At, g);
                if (KeyIs(st.ErdaKey, vk)) ErdaKey(p.At, g);
                bool janusHit = KeyIs(st.JanusKey, vk) && (st.JanusMod == 0 || (p.Mods & KeyHook.ModBit(st.JanusMod)) != 0);
                Step(p.At, g, janusHit, KeyIs(st.NpcKey, vk), mask, vk == 0x0D,
                    dmask, Array.IndexOf(Arrows, vk) >= 0 ? 1 : 0);
            }
            Step(DateTime.Now, false, false, false, 0, false, 0, 0); // 시간이 다 됐는지만 본다
            return;
        }
        if (Debug && !hookOffLogged) { hookOffLogged = true; Log.Write("키", "키 훅이 꺼져 있어 직접 확인 방식으로 봐요"); }
        // 키 알림을 못 받으면: 키를 뗐는지 계속 봐 두어야 다음 누름을 한 번으로 센다
        bool janus = Pressed(st.JanusKey, ref janusDown)
            && (st.JanusMod == 0 || (GetAsyncKeyState(st.JanusMod) & 0x8000) != 0);
        bool npc = Pressed(st.NpcKey, ref npcDown);
        bool held = st.ComboKey != 0 && (GetAsyncKeyState(st.ComboKey) & 0x8000) != 0;
        int buff = 0;
        for (int i = 0; i < BuffSlots; i++) if (Pressed(st.Buffs[i].Key, ref buffDown[i])) buff |= SlotMasks(st.Buffs[i].Key, held).buff & (1 << i);
        bool enter = Pressed(0x0D, ref enterDown);
        int dope = 0;
        for (int i = 0; i < DopeSlots; i++) if (Pressed(st.Dopes[i].Key, ref dopeDown[i])) dope |= SlotMasks(st.Dopes[i].Key, held).dope & (1 << i);
        int arrows = 0;
        for (int i = 0; i < 4; i++) if (Pressed(Arrows[i], ref arrowDown[i])) arrows++;
        bool fg = Game.Foreground();
        if (Pressed(st.ErdaKey, ref erdaDown)) ErdaKey(DateTime.Now, fg);
        for (int i = 0; i < RepeatSlots; i++)
            if (Pressed(st.Repeats[i].Key, ref repeatDown[i]) && (SlotMasks(st.Repeats[i].Key, held).rep >> i & 1) != 0) RepeatKey(i, DateTime.Now, fg);
        for (int i = 0; i < OnceSlots; i++)
            if (Pressed(st.Onces[i].Key, ref onceDown[i]) && (SlotMasks(st.Onces[i].Key, held).once >> i & 1) != 0) OnceKey(i, DateTime.Now, fg);
        Step(DateTime.Now, fg, janus, npc, buff, enter, dope, arrows);
    }

    // 반복 주기 알림 15칸: 칸마다 키를 누르면 그때부터 정한 주기마다 정한 색으로 알린다. 다시 누르면 처음부터 센다
    public const int RepeatSlots = 15;
    public const int RepeatMinSec = 5;
    readonly bool[] repeatDown = new bool[RepeatSlots];
    readonly DateTime?[] repeatNext = new DateTime?[RepeatSlots]; // 다음에 알릴 시각
    readonly int[] repeatSec = new int[RepeatSlots];              // 다음 시각을 정할 때의 주기 (바뀌면 다시 계산)
    /// <summary>마지막으로 키를 인식한 반복 칸 (설정 창 표시용)</summary>
    public int LastRepeat { get; private set; } = -1;
    public DateTime? LastRepeatAt { get; private set; }

    void RepeatKey(int i, DateTime at, bool inGame)
    {
        var r = st.Repeats[i];
        if (!r.On || r.Key == 0 || !inGame || r.Sec < RepeatMinSec) return;
        r.Start = at; st.Save();
        repeatNext[i] = null; // 아래에서 새로 계산
        LastRepeat = i; LastRepeatAt = at;
        // 엔터 기다리기: 키 뒤 10초 안에 엔터를 누르면 엔터 때부터 다시 센다
        repeatKeyAt[i] = r.WaitEnter && r.Key != 0x0D ? at : null;
    }

    readonly DateTime?[] repeatKeyAt = new DateTime?[RepeatSlots];

    void RepeatEnter(DateTime now, bool inGame)
    {
        if (!inGame) return;
        for (int i = 0; i < RepeatSlots; i++)
        {
            if (repeatKeyAt[i] is not DateTime k) continue;
            repeatKeyAt[i] = null;
            var r = st.Repeats[i];
            if (!r.On || !r.WaitEnter || r.Start == null || (now - k).TotalSeconds > BuffEnterSec) continue;
            r.Start = now; st.Save();
            repeatNext[i] = null;
            LastRepeat = i; LastRepeatAt = now;
        }
    }

    void RepeatTick(DateTime now)
    {
        for (int i = 0; i < RepeatSlots; i++)
        {
            var r = st.Repeats[i];
            if (!r.On || r.Sec < RepeatMinSec || r.Start is not DateTime s)
            {
                if (!r.On && r.Start != null) { r.Start = null; st.Save(); } // 끄면 세던 것도 멈춘다
                repeatNext[i] = null;
                continue;
            }
            if (repeatNext[i] == null || repeatSec[i] != r.Sec)
            {
                // 시작 시각부터 주기마다: 지난 것은 건너뛰고 다음 차례부터
                double n = Math.Max(1, Math.Floor((now - s).TotalSeconds / r.Sec) + 1);
                repeatNext[i] = s.AddSeconds(r.Sec * n);
                repeatSec[i] = r.Sec;
            }
            DateTime? end = r.UntilSec > 0 ? s.AddSeconds(r.UntilSec) : null;
            if (now >= repeatNext[i])
            {
                bool inTime = end == null || repeatNext[i] <= end; // 끝 시각 뒤의 차례는 알리지 않는다
                while (repeatNext[i] <= now) repeatNext[i] = repeatNext[i]!.Value.AddSeconds(r.Sec);
                if (inTime && !Mute) PlayRepeat(sc, r.Title(i), Color.FromArgb(r.Argb));
            }
            // 반복 시간이 다 되면 멈춘다 (트리거 키를 다시 누르면 처음부터)
            if (end is DateTime e && now >= e) { r.Start = null; st.Save(); repeatNext[i] = null; }
        }
    }

    /// <summary>반복이 끝날 때까지 남은 초. 끝없이 반복하거나 세고 있지 않으면 null.</summary>
    public double? RepeatEndLeft(int i)
    {
        var r = st.Repeats[i];
        return r.UntilSec > 0 && r.Start is DateTime s ? Math.Max(0, (s.AddSeconds(r.UntilSec) - DateTime.Now).TotalSeconds) : null;
    }

    /// <summary>다음 알림까지 남은 초. 세고 있지 않으면 null.</summary>
    public double? RepeatRemaining(int i) =>
        repeatNext[i] is DateTime t && st.Repeats[i].Start != null ? Math.Max(0, (t - DateTime.Now).TotalSeconds) : null;
    public void CancelRepeat(int i) { if (st.Repeats[i].Start != null) { st.Repeats[i].Start = null; st.Save(); } repeatNext[i] = null; }

    // 단일 알림 15칸: 칸마다 키를 누르면 그때부터 세어 정한 시간이 되면 한 번 알리고 멈춘다. 세는 중에 다시 누르면 처음부터
    public const int OnceSlots = 15;
    readonly bool[] onceDown = new bool[OnceSlots];
    readonly DateTime?[] onceKeyAt = new DateTime?[OnceSlots];
    /// <summary>마지막으로 키를 인식한 단일 알림 칸 (설정 창 표시용)</summary>
    public int LastOnce { get; private set; } = -1;
    public DateTime? LastOnceAt { get; private set; }

    void OnceKey(int i, DateTime at, bool inGame)
    {
        var o = st.Onces[i];
        if (!o.On || o.Key == 0 || !inGame || o.Sec < 1) return;
        o.Start = at; st.Save();
        LastOnce = i; LastOnceAt = at;
        // 엔터 기다리기: 키 뒤 10초 안에 엔터를 누르면 엔터 때부터 다시 센다
        onceKeyAt[i] = o.WaitEnter && o.Key != 0x0D ? at : null;
    }

    void OnceEnter(DateTime now, bool inGame)
    {
        if (!inGame) return;
        for (int i = 0; i < OnceSlots; i++)
        {
            if (onceKeyAt[i] is not DateTime k) continue;
            onceKeyAt[i] = null;
            var o = st.Onces[i];
            if (!o.On || !o.WaitEnter || o.Start == null || (now - k).TotalSeconds > BuffEnterSec) continue;
            o.Start = now; st.Save();
            LastOnce = i; LastOnceAt = now;
        }
    }

    void OnceTick(DateTime now)
    {
        for (int i = 0; i < OnceSlots; i++)
        {
            var o = st.Onces[i];
            if (o.Start is not DateTime s) continue;
            if (!o.On || o.Sec < 1) { o.Start = null; st.Save(); continue; } // 끄면 세던 것도 멈춘다
            var due = s.AddSeconds(o.Sec);
            if (now < due) continue;
            o.Start = null; st.Save();
            // 바를 꺼 둔 사이에 지난 알림은 늦게 울리지 않는다
            if (!Mute && (now - due).TotalSeconds < 10) PlayRepeat(sc, o.Title(i), Color.FromArgb(o.Argb), once: true);
        }
    }

    /// <summary>알릴 때까지 남은 초. 세고 있지 않으면 null.</summary>
    public double? OnceRemaining(int i) =>
        st.Onces[i].Start is DateTime s ? Math.Max(0, (s.AddSeconds(st.Onces[i].Sec) - DateTime.Now).TotalSeconds) : null;
    public void CancelOnce(int i) { if (st.Onces[i].Start != null) { st.Onces[i].Start = null; st.Save(); } onceKeyAt[i] = null; }

    // ── 00시 알림: 날짜가 바뀌기 n분 전에 한 번 ──
    DateTime lastMidnight;
    public static readonly Color MidnightMain = Color.FromArgb(90, 110, 230);
    static readonly Color MidnightLight = Color.FromArgb(214, 220, 248);

    void MidnightTick(DateTime now)
    {
        if (!st.MidnightOn) return;
        int n = Math.Clamp(st.MidnightMin, 0, 180);
        // 오늘 00시 기준과 내일 00시 기준 둘 다 본다 (n분 전이 어제 날짜에 걸칠 수 있어서)
        foreach (var t in new[] { now.Date.AddMinutes(-n), now.Date.AddDays(1).AddMinutes(-n) })
        {
            if (now < t || (now - t).TotalSeconds >= 10 || lastMidnight == t) continue;
            lastMidnight = t;
            if (!Mute) PlayMidnight(sc, n);
        }
    }

    public static void PlayMidnight(float sc, int n)
    {
        var text = n <= 0 ? "00시가 됐어요" : $"{n}분 뒤 초기화 됩니다";
        JanusAlert.Play(sc, text, MidnightMain, MidnightLight, AlertSound.Kind.Midnight, say: text);
    }

    public static void PlayRepeat(float sc, string text, Color c, bool once = false) =>
        JanusAlert.Play(sc, text, c,
            Color.FromArgb(c.R + (255 - c.R) * 3 / 4, c.G + (255 - c.G) * 3 / 4, c.B + (255 - c.B) * 3 / 4),
            once ? AlertSound.Kind.Once : AlertSound.Kind.Repeat, say: text);

    void Step(DateTime now, bool inGame, bool janus, bool npc, int buff, bool enter, int dope, int arrows)
    {
        if (enter) { RepeatEnter(now, inGame); OnceEnter(now, inGame); }
        RepeatTick(now);
        OnceTick(now);
        ErdaTick(now);
        RuneTick(now, inGame, npc, arrows);
        BuffTick(now, inGame, buff, enter);
        for (int i = 0; i < DopeSlots; i++) DopeTick(i, now, inGame, (dope >> i & 1) != 0, arrows, enter);
        if (!st.JanusOn)
        {
            ClearJanus(now);
            return;
        }

        // 여러 개를 이어서 설치하는 동안(첫 설치 후 50초 안, 앞서 누른 지 10초 안)은 새 설치로 세지 않는다.
        // 10초 넘게 쉬었다 누른 건 다시 깐 것으로 센다 (처음 누른 게 안 나갔거나 잘못 깔아 다시 깐 경우. 예전 기준으로 일찍 울리지 않게)
        // 꺼지기 직전(남은 시간 15초 이내)에 깐 것도 다시 깐 것으로 센다 (지속시간이 짧으면 50초 안에 꺼지기 직전이 온다)
        if (janus && inGame)
        {
            bool nearEnd = endsAt is DateTime ne && (ne - now).TotalSeconds <= ReinstallSec;
            bool inWindow = lastInstall is DateTime li && (now - li).TotalSeconds < IgnoreSec;
            bool burst = lastJanusPress is DateTime lp && (now - lp).TotalSeconds < BurstSec;
            if (nearEnd || !inWindow || !burst)
            {
                lastInstall = now;
                LastKey = now; LastKind = "설치";
                // 도는 중에 다시 깔면 그때부터 알림 주기를 새로 센다
                if (!Endless(st)) { st.JanusStart = now; st.Save(); }
            }
            lastJanusPress = now;
        }

        if (Endless(st))
        {
            ClearJanus(now);
            return;
        }
        // 다시 알리기: 알림 뒤에도 안 깔았으면 정한 초 뒤 한 번 더
        if (realertAt is DateTime ra && (!st.JanusRealertOn || st.JanusStart != null)) realertAt = null;
        else if (realertAt is DateTime rb && now >= rb)
        {
            realertAt = null;
            if (!Mute && (now - rb).TotalSeconds < 10) JanusAlert.Play(sc);
        }
        if (endsAt is not DateTime end) return;
        if (now >= end)
        {
            ClearJanus(end);
            if (st.JanusRealertOn) realertAt = end.AddSeconds(Math.Max(1, st.JanusRealertSec));
            if (!Mute) JanusAlert.Play(sc);
        }
    }

    DateTime? realertAt; // 다시 알릴 시각 (새로 깔면 지운다)

    const int EarlySec = 5; // 에르다 파운틴 '5초 먼저 알리기'

    // 에르다 파운틴: 키를 누르면 60초 뒤 사라진다. 다시 누르면 처음부터 센다
    public const int ErdaSec = 60;
    bool erdaDown;
    DateTime? ErdaEnd => st.ErdaStart is DateTime s ? s.AddSeconds(ErdaSec) : null;
    /// <summary>알릴 시각: 5초 먼저 알리기를 켜면 55초, 아니면 60초</summary>
    DateTime? ErdaAlertAt => ErdaEnd is DateTime e ? (st.ErdaEarly ? e.AddSeconds(-EarlySec) : e) : null;
    /// <summary>사라질 때까지 남은 초. 세고 있지 않으면 null.</summary>
    public double? ErdaRemaining => ErdaEnd is DateTime e ? Math.Max(0, (e - DateTime.Now).TotalSeconds) : null;
    public void CancelErda() { if (st.ErdaStart != null) { st.ErdaStart = null; st.Save(); } }
    public static readonly Color ErdaMain = Color.FromArgb(110, 210, 255), ErdaLight = Color.FromArgb(220, 245, 255);
    public static void PlayErda(float sc, bool early) =>
        JanusAlert.Play(sc, early ? "에르다 파운틴이 곧 사라져요" : "에르다 파운틴이 사라졌어요", ErdaMain, ErdaLight, AlertSound.Kind.Erda);

    void ErdaKey(DateTime at, bool inGame)
    {
        if (!st.ErdaOn || st.ErdaKey == 0 || !inGame) return;
        st.ErdaStart = at; st.Save();
        LastKey = at; LastKind = "에르다";
    }

    void ErdaTick(DateTime now)
    {
        if (!st.ErdaOn) { CancelErda(); return; }
        if (ErdaAlertAt is not DateTime a || now < a) return;
        bool early = st.ErdaEarly;
        st.ErdaStart = null; st.Save();
        // 꺼져 있는 동안 지나간 것은 조용히 지운다
        if (!Mute && (now - a).TotalSeconds < 10) PlayErda(sc, early);
    }

    /// <summary>바의 칩을 눌렀을 때: 세던 시간을 지운다.</summary>
    public void Stop() { poll.Stop(); poll.Dispose(); } // 끝낸 감시는 다시 켜지 않는다 (바를 새로 만들 때 새 감시가 생긴다)
    // 칸을 누르면 50초 무시 기록도 지운다 (잘못 누른 뒤 칸을 누르고 다시 깔면 바로 맞춰지게)
    public void CancelJanus() { ClearJanus(DateTime.Now); lastInstall = lastJanusPress = null; }

    bool alertsWereOn = true; // 처음부터 꺼져 있으면 첫 확인 때 남은 시간을 비운다
    /// <summary>세던 알림을 모두 멈춘다 (전체 알림을 끌 때). 칸마다 켜 둔 값은 바꾸지 않는다</summary>
    void StopAll()
    {
        ClearJanus(DateTime.Now);
        realertAt = null; lastInstall = lastJanusPress = null;
        CancelRune(); arrowCount = 0; runeAlerted = false;
        CancelErda();
        for (int i = 0; i < BuffSlots; i++)
        {
            var b = st.Buffs[i];
            if (b.Start != null || b.KeyAt != null || b.EnterAt != null) { b.Start = null; b.KeyAt = null; b.EnterAt = null; st.Save(); }
            buffKeyAt[i] = null;
        }
        for (int i = 0; i < DopeSlots; i++) { CancelDope(i); dopeKeyAt[i] = null; dopeArrows[i] = 0; }
        for (int i = 0; i < RepeatSlots; i++) { CancelRepeat(i); repeatKeyAt[i] = null; }
        for (int i = 0; i < OnceSlots; i++) CancelOnce(i);
    }
    public void CancelRune() { npcAt = null; if (st.RuneStart != null) { st.RuneStart = null; st.Save(); } }
    public void CancelBuff(int i) { if (st.Buffs[i].Start != null) { st.Buffs[i].Start = null; st.Save(); } }

    // 버프 3칸: 칸마다 버프 키를 누르면 바로 세고, 10초 안에 엔터도 누르면 엔터 시점을 남긴다(켜 두면 엔터 때부터 센다).
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
        // 엔터가 아닌 다른 버프 키를 새로 눌렀으면, 엔터를 기다리던 다른 칸은 더 기다리지 않는다
        int otherKey = 0;
        for (int i = 0; i < BuffSlots; i++)
            if ((buff >> i & 1) != 0 && st.Buffs[i].Key != 0x0D) otherKey = st.Buffs[i].Key;

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
            // 키를 누르면 엔터를 안 눌러도 쓰인 경우가 있어서 바로 키 시점부터 센다.
            // 10초 안에 엔터도 누르면 엔터 시점을 남기고, "엔터 때부터 세기"가 켜져 있으면 엔터 시점으로 옮긴다
            if (hit)
            {
                b.KeyAt = now; b.EnterAt = null;
                buffKeyAt[i] = b.Key == 0x0D ? null : now;
                Start(i, now);
            }
            else if (inGame && otherKey != 0 && otherKey != b.Key) buffKeyAt[i] = null;
            else if (enter && inGame && buffKeyAt[i] is DateTime k)
            {
                buffKeyAt[i] = null;
                if ((now - k).TotalSeconds <= BuffEnterSec)
                {
                    b.EnterAt = now;
                    if (b.WaitEnter) Start(i, now); else st.Save();
                    LastKey = now; LastKind = "버프 엔터"; LastBuff = i;
                }
            }
            if (BuffEnd(i) is DateTime end && now >= end)
            {
                b.Start = null; st.Save();
                if (!Mute) PlayBuff(sc, b.Min, b.Name);
            }
        }

        void Start(int i, DateTime at)
        {
            st.Buffs[i].Start = at; st.Save();
            LastKey = now; LastKind = "버프"; LastBuff = i;
        }
    }

    /// <summary>버프를 키 누른 때(false) 또는 엔터 누른 때(true)부터 세도록 바꾼다. 그 시점으로 아직 안 끝났을 때만.</summary>
    public bool UseBuffTime(int i, bool enter)
    {
        var b = st.Buffs[i];
        if (BuffTimeLeft(i, enter) == null) return false;
        b.Start = enter ? b.EnterAt : b.KeyAt; st.Save();
        return true;
    }
    /// <summary>그 시점으로 셌을 때 남은 초 (기록이 없거나 이미 끝났으면 null)</summary>
    public double? BuffTimeLeft(int i, bool enter)
    {
        var b = st.Buffs[i];
        if (!b.On || (enter ? b.EnterAt : b.KeyAt) is not DateTime t) return null;
        double left = (t.AddMinutes(b.Min) - DateTime.Now).TotalSeconds;
        return left > 0 ? left : null;
    }
    /// <summary>지금 엔터 시점으로 세는 중인지</summary>
    public bool BuffOnEnter(int i) => st.Buffs[i].Start is DateTime s && s == st.Buffs[i].EnterAt;

    // 부스트 도핑 3칸: 쓰면 Sec초를 센다. 다시 쓰면 처음부터 다시 센다
    public const int DopeSlots = 3;
    readonly bool[] dopeDown = new bool[DopeSlots];
    DateTime? DopeEnd(int i) => st.Dopes[i].Start is DateTime s ? s.AddSeconds(st.Dopes[i].Sec) : null;
    // 사용 대기 중에는 아직 효과가 붙지 않았으니 바에 띄우지 않는다
    public double? DopeRemaining(int i) => DopeEnd(i) is DateTime e && DopeWaiting(i) == null ? Math.Max(0, (e - DateTime.Now).TotalSeconds) : null;
    /// <summary>쓰고 효과가 붙기를 기다리는 중이면 남은 초</summary>
    public double? DopeWaiting(int i) => st.Dopes[i].Start is DateTime s && s > DateTime.Now ? (s - DateTime.Now).TotalSeconds : null;
    public void CancelDope(int i) { if (st.Dopes[i].Start != null) { st.Dopes[i].Start = null; st.Save(); } }
    public static readonly Color DopeMain = Color.FromArgb(235, 40, 50), DopeLight = Color.FromArgb(255, 205, 205);
    static readonly (Color main, Color light)[] dopeColors =
    {
        (DopeMain, DopeLight),                                              // 빨강
        (Color.FromArgb(150, 90, 45), Color.FromArgb(235, 215, 195)),       // 갈색
        (Color.FromArgb(240, 90, 170), Color.FromArgb(255, 215, 235)),      // 분홍
    };
    public static (Color main, Color light) DopeColors(int i) => dopeColors[Math.Clamp(i, 0, DopeSlots - 1)];
    public static string DopeDefaultName(int i) => i switch { 0 => "부스트 도핑 1", 1 => "부스트 도핑 2", _ => "부스트 도핑 3" };
    public static void PlayDope(float sc, int i, string? name)
    {
        var (m, l) = DopeColors(i);
        JanusAlert.Play(sc, string.IsNullOrWhiteSpace(name) ? "부스트 도핑이 끝났어요!" : name.Trim() + " 끝났어요!", m, l, AlertSound.Kind.Dope);
    }
    public static void PlayRune(float sc) => JanusAlert.Play(sc, "곧 룬이 나와요!", Color.FromArgb(40, 190, 255), Color.FromArgb(200, 240, 255), AlertSound.Kind.Rune);

    // 도핑 키 → 방향키 한 번(사용 확인 창에서 '예'로 옮김) → 엔터 순서로 눌러야 쓴 것으로 본다.
    // 방향키를 두 번 이상 누르면 다시 원래 버튼으로 돌아갈 수 있어서 세지 않는다
    readonly DateTime?[] dopeKeyAt = new DateTime?[DopeSlots];
    readonly int[] dopeArrows = new int[DopeSlots];
    const double DopeWindowSec = 10;
    /// <summary>도핑 키 뒤에 누른 방향키 수 (기다리는 중이 아니면 -1, 설정 창 표시용)</summary>
    public int DopeProgress(int i) => dopeKeyAt[i] is DateTime t && (DateTime.Now - t).TotalSeconds <= DopeWindowSec ? dopeArrows[i] : -1;

    void DopeTick(int i, DateTime now, bool inGame, bool dope, int arrows, bool enter)
    {
        var d = st.Dopes[i];
        if (!d.On || d.Key == 0)
        {
            dopeKeyAt[i] = null;
            if (d.Start != null) { d.Start = null; st.Save(); }
            return;
        }
        if (dopeKeyAt[i] is DateTime k0 && (now - k0).TotalSeconds > DopeWindowSec) dopeKeyAt[i] = null;
        if (dope && inGame)
        {
            dopeKeyAt[i] = now; dopeArrows[i] = 0;
            LastKey = now; LastKind = "도핑 키";
        }
        else if (dopeKeyAt[i] != null && inGame)
        {
            dopeArrows[i] += arrows;
            if (enter)
            {
                bool ok = dopeArrows[i] == 1;
                dopeKeyAt[i] = null;
                if (ok)
                {
                    // 사용 대기가 있으면 그만큼 뒤에 효과가 붙으니 그때부터 센다
                    d.Start = now.AddSeconds(d.DelayOn ? d.DelaySec : 0); st.Save();
                    LastKey = now; LastKind = "도핑" + i;
                    return;
                }
            }
        }
        if (DopeEnd(i) is DateTime end && now >= end)
        {
            d.Start = null; st.Save();
            if (!Mute) PlayDope(sc, i, d.Name);
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

    /// <summary>누른 키(vk)가 설정 키(set)인지. Shift·Ctrl·Alt는 왼쪽·오른쪽을 같은 키로 본다
    /// (1.1.8에서 왼쪽·오른쪽 Shift(160·161)로 저장된 키도 둘 다로 맞춘다)</summary>
    public static bool KeyIs(int set, int vk) => set != 0 && Side(set) == Side(vk);
    static int Side(int vk) => vk switch { 160 or 161 => 16, 162 or 163 => 17, 164 or 165 => 18, _ => vk };

    /// <summary>같이 누르는 키가 있으면 "Shift+Num 9"처럼 붙여 쓴다</summary>
    public static string KeyName(int mod, int vk) => mod == 0 || vk == 0 ? KeyName(vk) : KeyName(mod) + "+" + KeyName(vk);

    public static string KeyName(int vk) => vk == 0 ? "없음" : (Keys)vk switch
    {
        >= Keys.NumPad0 and <= Keys.NumPad9 => "Num " + (vk - 0x60),
        Keys.Decimal => "Num .",
        Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey => "Shift",
        Keys.ControlKey or Keys.LControlKey or Keys.RControlKey => "Ctrl",
        Keys.Menu or Keys.LMenu or Keys.RMenu => "Alt",
        >= Keys.D0 and <= Keys.D9 => ((char)vk).ToString(),
        Keys.Prior => "PageUp",
        Keys.Next => "PageDown",
        Keys.Delete => "Del",
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
