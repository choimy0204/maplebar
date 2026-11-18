using System.Text.Json.Nodes;

namespace MapleBar;

/// <summary>줄 창에 보여 줄 한 사람의 상태. Left는 다 돌기까지 남은 초 (0이면 ON)</summary>
public sealed record PartyRowInfo(string Id, string Job, bool Me, bool Known,
    double OriginLeft, double BurstLeft, double SemiLeft, double BuffLeft, int Ascent);

/// <summary>내 보스 쿨타임과 파티원 쿨타임을 관리한다.
/// 메이플 창에서 누른 키만 JanusWatch가 Key()로 넘겨 준다. 게임에는 아무것도 보내지 않는다.</summary>
public static class PartyBoss
{
    // 한 사람의 상태. 시각은 모두 Now() 기준 초
    sealed class Peer
    {
        public readonly double[] Ready = new double[3]; // 오리진·극딜·준극이 다 도는 시각
        public double BuffEnd;                          // 오리진 버프가 끝나는 시각
        public int Ascent = PartyCalc.AscentMax;
        public bool Known;
        public readonly bool[] WasOn = { true, true, true };

        public Peer Copy()
        {
            var p = new Peer { BuffEnd = BuffEnd, Ascent = Ascent, Known = Known };
            Array.Copy(Ready, p.Ready, 3);
            return p;
        }
        public void Load(Peer o)
        {
            Array.Copy(o.Ready, Ready, 3);
            BuffEnd = o.BuffEnd; Ascent = o.Ascent;
        }
        public void SyncOn(double now)
        {
            for (int i = 0; i < 3; i++) WasOn[i] = Ready[i] <= now;
        }
    }

    const int UndoMax = 20;
    const double SameKeySec = 0.3;

    static readonly Peer me = new() { Known = true };
    static readonly List<Peer> undo = new();
    static readonly Dictionary<string, Peer> others = new();
    static readonly double[] lastKeyAt = new double[4];
    static readonly object sync = new();
    static SynchronizationContext? ui;
    static System.Windows.Forms.Timer? timer;
    static bool inited;

    static double Now() => Environment.TickCount64 / 1000.0;
    static PartyBossSettings? Cfg => PartyHub.St?.Party;

    /// <summary>프로그램 시작 때 한 번 부른다 (화면 글 쪽에서). 여러 번 불러도 한 번만 한다</summary>
    public static void Init(float sc)
    {
        PartyRows.Init(sc);
        PartyAlert.Init(sc);
        if (inited) return;
        inited = true;
        ui = SynchronizationContext.Current;
        PartyHub.CurrentState = BuildState;
        PartyHub.MembersChanged += list => OnUi(() => OnMembers(list));
        PartyHub.StateReceived += (id, s, use) => OnUi(() => OnState(id, s, use));
        PartyHub.Left += () => OnUi(OnLeft);
        timer = new System.Windows.Forms.Timer { Interval = 250 };
        timer.Tick += (_, _) => Tick();
        timer.Start();
    }

    static void OnUi(Action a)
    {
        if (ui == null) { a(); return; }
        ui.Post(_ => a(), null);
    }

    // ── 내 키 ──

    /// <summary>메이플 창에서 누른 키 하나 (가상 키 코드)</summary>
    public static void Key(int vk)
    {
        if (!PartyHub.Active || Cfg is not PartyBossSettings p) return;
        PartySkill skill;
        if (JanusWatch.KeyIs(p.OriginKey, vk)) skill = PartySkill.Origin;
        else if (JanusWatch.KeyIs(p.BurstKey, vk)) skill = PartySkill.Burst;
        else if (JanusWatch.KeyIs(p.SemiKey, vk)) skill = PartySkill.Semi;
        else if (JanusWatch.KeyIs(p.AscentKey, vk)) skill = PartySkill.Ascent;
        else return;

        double now = Now();
        if (now - lastKeyAt[(int)skill] < SameKeySec) return; // 같은 키가 바로 또 오면 무시
        lastKeyAt[(int)skill] = now;

        lock (sync)
        {
            if (skill == PartySkill.Ascent && me.Ascent <= 0) return;
            PushUndo();
            switch (skill)
            {
                case PartySkill.Origin:
                    me.Ready[0] = now + PartyCalc.Full(PartyCalc.OriginBase, p.PctCut, p.SecCut);
                    me.BuffEnd = now + PartyCalc.BuffSec;
                    // 극딜이 아직 돌고 있으면 버프로 더 빨리 돈다 (남은 시간을 지금부터 센다)
                    if (me.Ready[1] > now) me.Ready[1] = PartyCalc.BurstReady(now, me.Ready[1] - now, now);
                    break;
                case PartySkill.Burst:
                    double? originAt = me.BuffEnd > now ? me.BuffEnd - PartyCalc.BuffSec : null;
                    me.Ready[1] = PartyCalc.BurstReady(now, PartyCalc.Full(PartyCalc.BurstBase, p.PctCut, p.SecCut), originAt);
                    break;
                case PartySkill.Semi:
                    me.Ready[2] = now + PartyCalc.Full(PartyCalc.SemiBase, p.PctCut, p.SecCut);
                    break;
                case PartySkill.Ascent:
                    me.Ascent--;
                    break;
            }
            me.SyncOn(now);
        }
        PartyHub.RaiseSend(BuildState(), UseName(skill));
        Refresh();
    }

    static string UseName(PartySkill k) => k switch
    {
        PartySkill.Origin => "origin",
        PartySkill.Burst => "burst",
        PartySkill.Semi => "semi",
        _ => "ascent",
    };

    static void PushUndo()
    {
        undo.Add(me.Copy());
        if (undo.Count > UndoMax) undo.RemoveAt(0);
    }

    /// <summary>방금 한 것을 되돌린다</summary>
    public static void Undo()
    {
        lock (sync)
        {
            if (undo.Count == 0) return;
            me.Load(undo[^1]);
            undo.RemoveAt(undo.Count - 1);
            me.SyncOn(Now());
        }
        PartyHub.RaiseSend(BuildState(), null);
        Refresh();
    }

    /// <summary>모두 ON, 어센트 3회로 되돌린다</summary>
    public static void Reset()
    {
        lock (sync) { ResetMe(); }
        PartyHub.RaiseSend(BuildState(), null);
        Refresh();
    }

    static void ResetMe()
    {
        undo.Clear();
        Array.Clear(me.Ready);
        me.BuffEnd = 0;
        me.Ascent = PartyCalc.AscentMax;
        me.SyncOn(Now());
    }

    /// <summary>지금 내 상태 (서버로 보내는 모양)</summary>
    static JsonObject BuildState()
    {
        double now = Now();
        lock (sync)
        {
            return new JsonObject
            {
                ["job"] = Cfg?.Job ?? "",
                ["origin"] = Math.Max(0, me.Ready[0] - now),
                ["burst"] = Math.Max(0, me.Ready[1] - now),
                ["semi"] = Math.Max(0, me.Ready[2] - now),
                ["buff"] = Math.Max(0, me.BuffEnd - now),
                ["ascent"] = me.Ascent,
            };
        }
    }

    // ── 파티원 ──

    static double Num(JsonNode? n)
    {
        if (n is JsonValue v)
        {
            if (v.TryGetValue(out double d)) return d;
            if (v.TryGetValue(out int i)) return i;
        }
        return 0;
    }

    static void OnState(string id, JsonObject s, string? use)
    {
        if (id == PartyHub.MyId) return;
        double now = Now();
        if (!others.TryGetValue(id, out var peer)) others[id] = peer = new Peer();
        peer.Ready[0] = now + Math.Max(0, Num(s["origin"]));
        peer.Ready[1] = now + Math.Max(0, Num(s["burst"]));
        peer.Ready[2] = now + Math.Max(0, Num(s["semi"]));
        peer.BuffEnd = now + Math.Max(0, Num(s["buff"]));
        peer.Ascent = Math.Clamp((int)Num(s["ascent"]), 0, PartyCalc.AscentMax);
        peer.Known = true;
        peer.SyncOn(now);

        if (use == null) return;
        string? sentJob = s["job"] is JsonValue jv && jv.TryGetValue(out string? js) ? js : null; // 글이 아니면 버린다
        string job = JobOf(id, sentJob);
        switch (use)
        {
            case "origin": PartyAlert.Show(job, PartySkill.Origin, "오리진", false); break;
            case "burst": PartyAlert.Show(job, PartySkill.Burst, "극딜 시퀀스", false); break;
            case "semi": PartyAlert.Show(job, PartySkill.Semi, "준극딜 시퀀스", false); break;
            case "ascent": PartyAlert.Show(job, PartySkill.Ascent, $"어센트 {peer.Ascent}회 남음", false); break;
        }
    }

    static string JobOf(string id, string? fallback)
    {
        foreach (var m in PartyHub.Members)
            if (m.Id == id && m.Job != "") return m.Job;
        return fallback ?? "";
    }

    static void OnMembers(IReadOnlyList<PartyMember> list)
    {
        var ids = new HashSet<string>(list.Select(m => m.Id));
        foreach (var id in others.Keys.Where(k => !ids.Contains(k)).ToList()) others.Remove(id);
        Refresh();
    }

    static void OnLeft()
    {
        others.Clear();
        lock (sync) { ResetMe(); }
        PartyRows.CloseAll();
        PartyAlert.CloseAll();
    }

    // ── 화면 ──

    static void Tick()
    {
        if (!PartyHub.Active) return;
        double now = Now();
        var p = Cfg;
        if (p != null)
        {
            lock (sync) { CheckReady(me, PartyHub.MyId ?? "", Cfg?.Job ?? "", now, p); }
            foreach (var (id, peer) in others) CheckReady(peer, id, JobOf(id, null), now, p);
        }
        Refresh();
    }

    // 다 돌아 ON!이 되는 순간 한 번 알린다
    static void CheckReady(Peer peer, string id, string job, double now, PartyBossSettings p)
    {
        if (!peer.Known) return;
        string[] names = { "오리진", "극딜", "준극" };
        bool[] want = { p.AlertOrigin, p.AlertBurst, p.AlertSemi };
        var kinds = new[] { PartySkill.Origin, PartySkill.Burst, PartySkill.Semi };
        var sounds = new[] { AlertSound.Kind.Janus, AlertSound.Kind.Dope, AlertSound.Kind.Buff };
        for (int i = 0; i < 3; i++)
        {
            bool on = peer.Ready[i] <= now;
            bool turned = on && !peer.WasOn[i];
            peer.WasOn[i] = on;
            if (!turned || !want[i]) continue;
            PartyAlert.Show(job, kinds[i], names[i] + " ON!", true);
            AlertSound.Play(sounds[i]);
        }
    }

    static void Refresh()
    {
        if (!PartyHub.Active) { PartyRows.CloseAll(); return; }
        double now = Now();
        var rows = new List<PartyRowInfo>();
        foreach (var m in PartyHub.Members)
        {
            bool mine = m.Id == PartyHub.MyId;
            if (mine)
            {
                lock (sync)
                {
                    rows.Add(new PartyRowInfo(m.Id, Cfg?.Job is { Length: > 0 } j ? j : m.Job, true, true,
                        Math.Max(0, me.Ready[0] - now), Math.Max(0, me.Ready[1] - now), Math.Max(0, me.Ready[2] - now),
                        Math.Max(0, me.BuffEnd - now), me.Ascent));
                }
                continue;
            }
            if (!others.TryGetValue(m.Id, out var peer) || !peer.Known)
            {
                rows.Add(new PartyRowInfo(m.Id, m.Job, false, false, 0, 0, 0, 0, PartyCalc.AscentMax));
                continue;
            }
            rows.Add(new PartyRowInfo(m.Id, m.Job, false, true,
                Math.Max(0, peer.Ready[0] - now), Math.Max(0, peer.Ready[1] - now), Math.Max(0, peer.Ready[2] - now),
                Math.Max(0, peer.BuffEnd - now), peer.Ascent));
        }
        PartyRows.Update(rows);
    }
}
