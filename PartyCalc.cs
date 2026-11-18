namespace MapleBar;

/// <summary>파티 보스 스킬 종류</summary>
public enum PartySkill { Origin, Burst, Semi, Ascent }

/// <summary>쿨타임 계산 (화면에 기대지 않는 순수 계산). 시각은 모두 초 단위 double</summary>
public static class PartyCalc
{
    public const double OriginBase = 360, BurstBase = 120, SemiBase = 60, BuffSec = 15;
    public const int AscentMax = 3;

    /// <summary>줄어든 최종 재사용 시간 = 기본 × (1 − %감소/100) − 초감소. 0 밑으로는 안 내려간다</summary>
    public static double Full(double baseSec, double pct, int sec) =>
        Math.Max(0, baseSec * (1 - pct / 100) - sec);

    /// <summary>극딜이 다 도는 시각. burstUsedAt에 쓰고 burstFull초가 걸리는데,
    /// 오리진 버프(originAt부터 15초) 동안은 두 배 빠르게 돈다</summary>
    public static double BurstReady(double burstUsedAt, double burstFull, double? originAt)
    {
        double ready = burstUsedAt + burstFull;
        if (originAt is not double t0) return ready;
        double s = Math.Max(t0, burstUsedAt);
        double e = t0 + BuffSec;
        if (e <= s || ready <= s) return ready;          // 버프가 이미 끝났거나, 버프 전에 다 돎
        if (2 * (e - s) >= ready - s) return s + (ready - s) / 2; // 버프 안에서 다 돎
        return ready - (e - s);                           // 버프 내내 두 배로 돌고 남은 만큼 더
    }

    /// <summary>"오리진 ON!" / "오리진 5분" / "오리진 54초"</summary>
    public static string Text(double left, string name)
    {
        if (left <= 0) return name + " ON!";
        int secs = (int)Math.Ceiling(left);
        if (secs >= 60) return name + " " + (int)Math.Ceiling(left / 60) + "분";
        return name + " " + secs + "초";
    }
}
