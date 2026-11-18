using System.Text.Json.Nodes;

namespace MapleBar;

/// <summary>파티 보스 설정 (Settings.Party, 알림 프리셋에도 들어간다)</summary>
public class PartyBossSettings
{
    public string Job { get; set; } = "";      // 직업 줄임말 (내 줄 맨 앞에 보인다)
    public int OriginKey { get; set; }         // 가상 키 코드 (0이면 없음)
    public int BurstKey { get; set; }          // 극딜 시퀀스
    public int SemiKey { get; set; }           // 준극딜 시퀀스
    public int AscentKey { get; set; }         // 어센트
    public int SecCut { get; set; }            // 초 감소 0~9 (0이면 없음)
    public double PctCut { get; set; }         // 재사용 대기시간 % 감소 (0이면 없음)
    public bool AlertOrigin { get; set; } = true; // 쿨타임이 다 돌면 알림
    public bool AlertBurst { get; set; } = true;
    public bool AlertSemi { get; set; } = true;

    public PartyBossSettings Clone() => (PartyBossSettings)MemberwiseClone();
}

/// <summary>파티에 들어와 있는 사람 (들어온 순서). Id는 서버가 정한다</summary>
public sealed record PartyMember(string Id, string Job, bool Owner);

/// <summary>파티 연결(바 화면 쪽)과 보스 표시 쪽을 잇는 곳.
/// 연결 쪽: St·MyId를 넣고, 서버에서 받은 것을 Raise… 로 알린다. SendState를 구독해 서버로 보낸다.
/// 보스 표시 쪽: MembersChanged·StateReceived·Left를 구독하고, 내 상태가 바뀌면 RaiseSend를 부른다.</summary>
public static class PartyHub
{
    /// <summary>바의 설정 (바가 켜질 때 넣는다)</summary>
    public static Settings? St;
    /// <summary>파티에 들어와 있으면 서버가 준 내 번호, 아니면 null</summary>
    public static string? MyId { get; private set; }
    public static bool Active => MyId != null;
    public static IReadOnlyList<PartyMember> Members { get; private set; } = Array.Empty<PartyMember>();

    // ── 연결 쪽 → 보스 표시 쪽 ──
    public static event Action<IReadOnlyList<PartyMember>>? MembersChanged;
    /// <summary>(보낸 사람 id, 상태, 쓴 스킬 "origin"·"burst"·"semi"·"ascent" 또는 null). 상태 안의 남은 초는 이미 age만큼 빼서 넘긴다</summary>
    public static event Action<string, JsonObject, string?>? StateReceived;
    /// <summary>파티를 나갔거나 해체됐을 때</summary>
    public static event Action? Left;

    public static void RaiseJoined(string myId) => MyId = myId;
    public static void RaiseMembers(IReadOnlyList<PartyMember> list) { Members = list; MembersChanged?.Invoke(list); }
    public static void RaiseState(string id, JsonObject s, string? use) => StateReceived?.Invoke(id, s, use);
    public static void RaiseLeft() { MyId = null; Members = Array.Empty<PartyMember>(); Left?.Invoke(); }

    // ── 보스 표시 쪽 → 연결 쪽 ──
    /// <summary>(내 상태, 쓴 스킬 또는 null). 연결 쪽이 {t:"state", s, use}로 서버에 보낸다</summary>
    public static event Action<JsonObject, string?>? SendState;
    public static void RaiseSend(JsonObject s, string? use) => SendState?.Invoke(s, use);
    /// <summary>보스 표시 쪽이 지금 내 상태를 돌려준다 (처음 들어갔을 때 연결 쪽이 한 번 보낸다)</summary>
    public static Func<JsonObject>? CurrentState;
}
