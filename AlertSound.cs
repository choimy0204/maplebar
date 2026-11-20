using System.Media;

namespace MapleBar;

/// <summary>알림 소리: 소리 파일 없이 음을 직접 만들어 낸다. 소리마다 2~3초짜리 가락이다.
/// 서로 다른 소리 30가지 중에서 알림마다 하나를 고를 수 있다.</summary>
static class AlertSound
{
    // 순서를 바꾸지 말 것: 설정(SoundPicks)이 이 순서로 저장된다. 새 알림은 맨 뒤에 붙인다.
    public enum Kind { Janus, Rune, Buff, Dope, Erda, Repeat, Once, Pendant, Midnight }

    /// <summary>알림 이름 (Kind 순서)</summary>
    public static readonly string[] KindNames =
        { "솔 야누스", "룬", "버프", "도핑", "에르다 파운틴", "반복 알림", "단일 알림", "정령의 펜던트", "00시 알림" };

    /// <summary>알림마다 처음 쓰는 소리 (예전 소리와 같게)</summary>
    static readonly int[] Defaults = { 0, 1, 2, 3, 2, 2, 2, 2, 1 };

    /// <summary>켜기·크기·고른 소리를 읽을 설정 (JanusWatch가 넣는다)</summary>
    public static Settings? St;

    const int Rate = 44100;
    static SoundPlayer? player;   // 재생 중에 사라지지 않게 잡아 둔다
    static readonly Dictionary<(int, int), byte[]> cache = new();

    // 음 하나: 주파수(Hz), 시작(초), 길이(초), 2배음 세기(높을수록 날카롭다),
    // To: 0이 아니면 길이 동안 이 주파수까지 미끄러진다, Odd: 3배음 세기(삑삑한 소리), Amp: 크기 비율
    readonly record struct Note(double Hz, double At, double Len, double Bright = 0.25, double To = 0, double Odd = 0, double Amp = 1);

    // 음 높이
    const double G4 = 392, A4 = 440, C5 = 523.25, D5 = 587.33, E5 = 659.25, F5 = 698.46, G5 = 783.99, A5 = 880, B5 = 987.77,
        C6 = 1046.5, D6 = 1174.66, E6 = 1318.5, G6 = 1568, A6 = 1760, B6 = 1975.5, C7 = 2093, D7 = 2349.3, E7 = 2637, G7 = 3136;

    static Note N(double hz, double at, double len, double bright = 0.25, double to = 0, double odd = 0, double amp = 1)
        => new(hz, at, len, bright, to, odd, amp);

    // 소리마다 2~3초짜리 짧은 가락. 같은 음을 되풀이하지 않고 끝에서 화음이나 긴 음으로 마무리한다.
    static readonly (string Name, Note[] Notes)[] Lib =
    {
        ("띵동", new[] { N(E6, 0, 0.9), N(C6, 0.45, 1.0), N(C6, 1.0, 1.5, 0.15, amp: 0.6), N(G5, 1.0, 1.5, 0.15, amp: 0.45), N(E5, 1.0, 1.5, 0.15, amp: 0.4) }),
        ("띠링", new[] { N(C6, 0, 0.6), N(E6, 0.1, 0.6), N(G6, 0.2, 0.7), N(C7, 0.3, 1.0), N(G6, 0.6, 1.8, amp: 0.45), N(E6, 0.6, 1.8, amp: 0.35) }),
        ("띵 띵", new[] { N(A5, 0, 0.6), N(A5, 0.3, 0.7), N(E6, 0.9, 0.6), N(A6, 1.2, 1.3, amp: 0.8) }),
        ("삐빅", new[] { N(B6, 0, 0.1, 0.5), N(B6, 0.13, 0.14, 0.5), N(E7, 0.45, 0.12, 0.4), N(E7, 0.6, 0.16, 0.4), N(B6, 0.95, 1.5, 0.1, amp: 0.55) }),
        ("딩동댕", new[] { N(C5, 0, 0.7), N(E5, 0.3, 0.7), N(G5, 0.6, 0.8), N(C6, 0.9, 1.6), N(E5, 0.9, 1.6, amp: 0.4), N(G5, 0.9, 1.6, amp: 0.4) }),
        ("내려가는 띠링", new[] { N(G6, 0, 0.5), N(E6, 0.12, 0.5), N(C6, 0.24, 0.6), N(G5, 0.36, 0.8), N(C6, 0.7, 1.8, 0.15, amp: 0.7), N(E5, 0.7, 1.8, amp: 0.3) }),
        ("맑은 종", new[] { N(A6, 0, 1.4, 0.1), N(A6 * 2.76, 0, 0.4, 0, amp: 0.25), N(E6, 0.6, 1.9, 0.1, amp: 0.6), N(E6 * 2.76, 0.6, 0.3, 0, amp: 0.15) }),
        ("낮은 종", new[] { N(C5, 0, 1.4, 0.4), N(C6, 0, 0.6, 0.1, amp: 0.3), N(G4, 0.7, 1.8, 0.4, amp: 0.8), N(G5, 0.7, 0.6, 0.1, amp: 0.25) }),
        ("뾰롱", new[] { N(600, 0, 0.25, 0.2, to: 1400), N(C7, 0.3, 0.4, 0), N(E7, 0.45, 0.4, 0, amp: 0.7), N(G6, 0.6, 1.8, 0, amp: 0.6) }),
        ("뿅", new[] { N(1400, 0, 0.2, 0.2, to: 500), N(1000, 0.35, 0.2, 0.2, to: 400, amp: 0.7), N(C5, 0.7, 1.7, 0.2, amp: 0.6) }),
        ("휘익", new[] { N(400, 0, 0.5, 0.1, to: 1800), N(A6, 0.5, 1.9, 0.05, amp: 0.7), N(E6, 0.5, 1.9, 0.05, amp: 0.4) }),
        ("삐삐삐", new[] { N(B6, 0, 0.08, 0.5), N(B6, 0.12, 0.08, 0.5), N(B6, 0.24, 0.08, 0.5), N(E7, 0.6, 0.08, 0.5, amp: 0.8), N(E7, 0.72, 0.08, 0.5, amp: 0.8), N(B6, 1.0, 1.4, 0.2, amp: 0.5) }),
        ("똑딱", new[] { N(2000, 0, 0.05, 0.8, odd: 0.5), N(1500, 0.25, 0.05, 0.8, odd: 0.5), N(2000, 0.5, 0.05, 0.8, odd: 0.5), N(1500, 0.75, 0.05, 0.8, odd: 0.5), N(E6, 1.0, 1.5, 0.1), N(C6, 1.0, 1.5, 0.1, amp: 0.5) }),
        ("경쾌한 네 음", new[] { N(C6, 0, 0.3), N(D6, 0.08, 0.3), N(E6, 0.16, 0.3), N(G6, 0.24, 0.5), N(A6, 0.5, 0.4), N(G6, 0.7, 0.4), N(E6, 0.9, 0.4), N(C7, 1.1, 1.4) }),
        ("펼친 화음", new[] { N(C5, 0, 1.8, amp: 0.7), N(E5, 0.08, 1.8, amp: 0.7), N(G5, 0.16, 1.8, amp: 0.7), N(C6, 0.24, 1.8, amp: 0.7), N(E6, 0.32, 1.8, amp: 0.7), N(G6, 0.4, 1.8, amp: 0.7), N(C7, 0.48, 2.0, amp: 0.7) }),
        ("도레미", new[] { N(C5, 0, 0.45), N(D5, 0.25, 0.45), N(E5, 0.5, 0.45), N(G5, 0.75, 0.45), N(C6, 1.0, 1.5) }),
        ("화음", new[] { N(C6, 0, 1.2), N(E6, 0, 1.2), N(G6, 0, 1.2), N(B5, 1.0, 1.5), N(D6, 1.0, 1.5), N(G6, 1.0, 1.5) }),
        ("높은 화음", new[] { N(E6, 0, 0.6, 0.4), N(G6, 0, 0.6, 0.4), N(C7, 0, 0.6, 0.4), N(G6, 0.5, 0.6, 0.4), N(B6, 0.5, 0.6, 0.4), N(D7, 0.5, 0.6, 0.4), N(E6, 1.0, 1.5, 0.3), N(G6, 1.0, 1.5, 0.3), N(C7, 1.0, 1.5, 0.3) }),
        ("두 번 화음", new[] { N(G5, 0, 0.3), N(B5, 0, 0.3), N(D6, 0, 0.3), N(G5, 0.22, 0.45), N(B5, 0.22, 0.45), N(D6, 0.22, 0.45), N(C6, 0.8, 1.7), N(E6, 0.8, 1.7), N(G6, 0.8, 1.7) }),
        ("물방울", new[] { N(1800, 0, 0.12, 0, to: 900), N(2200, 0.15, 0.12, 0, to: 1100), N(1600, 0.45, 0.12, 0, to: 800), N(2400, 0.7, 0.12, 0, to: 1200, amp: 0.8), N(2000, 1.1, 0.15, 0, to: 1000, amp: 0.6), N(E6, 1.3, 1.2, 0, amp: 0.4) }),
        ("실로폰", new[] { N(E6, 0, 0.3, 0, odd: 0.5), N(G6, 0.15, 0.3, 0, odd: 0.5), N(E6, 0.3, 0.3, 0, odd: 0.5), N(C7, 0.45, 0.3, 0, odd: 0.5), N(D7, 0.6, 0.3, 0, odd: 0.5), N(C7, 0.75, 0.3, 0, odd: 0.5), N(G6, 0.9, 0.3, 0, odd: 0.5), N(C7, 1.1, 1.4, 0, odd: 0.5) }),
        ("오르골", new[] { N(E6, 0, 0.6, 0.05), N(C6, 0.3, 0.6, 0.05), N(D6, 0.6, 0.6, 0.05), N(G5, 0.9, 0.8, 0.05), N(G5, 1.3, 0.6, 0.05), N(D6, 1.55, 0.6, 0.05), N(E6, 1.8, 0.7, 0.05), N(C6, 1.8, 0.7, 0.05, amp: 0.5) }),
        ("알람", new[] { N(A5, 0, 0.14, 0.2, odd: 0.6), N(E6, 0.15, 0.14, 0.2, odd: 0.6), N(A5, 0.3, 0.14, 0.2, odd: 0.6), N(E6, 0.45, 0.14, 0.2, odd: 0.6), N(B5, 0.6, 0.14, 0.2, odd: 0.6), N(G6, 0.75, 0.14, 0.2, odd: 0.6), N(C6, 0.9, 0.14, 0.2, odd: 0.6), N(A6, 1.05, 0.14, 0.2, odd: 0.6), N(A6, 1.3, 1.2, 0.2, odd: 0.3, amp: 0.6) }),
        ("경보", new[] { N(800, 0, 0.4, 0.2, to: 1200, odd: 0.4), N(1200, 0.4, 0.4, 0.2, to: 800, odd: 0.4), N(800, 0.8, 0.4, 0.2, to: 1200, odd: 0.4), N(1200, 1.2, 0.4, 0.2, to: 800, odd: 0.4), N(800, 1.6, 0.8, 0.2, to: 1400, odd: 0.4, amp: 0.8) }),
        ("부드러운 띵", new[] { N(E5, 0, 2.4, 0), N(B5, 0.3, 2.1, 0, amp: 0.35) }),
        ("반짝", new[] { N(E7, 0, 0.2, 0), N(B6, 0.06, 0.2, 0), N(G7, 0.12, 0.5, 0), N(C7, 0.4, 0.4, 0, amp: 0.6), N(E7, 0.55, 0.4, 0, amp: 0.5), N(B6, 0.7, 1.6, 0, amp: 0.4) }),
        ("레이저", new[] { N(2000, 0, 0.25, 0.2, to: 300, odd: 0.3), N(1600, 0.3, 0.25, 0.2, to: 300, odd: 0.3, amp: 0.7), N(1200, 0.6, 0.3, 0.2, to: 300, odd: 0.3, amp: 0.5), N(A4, 0.95, 1.5, 0.3, odd: 0.3, amp: 0.5) }),
        ("노크", new[] { N(300, 0, 0.06, 0.8, odd: 0.5), N(300, 0.15, 0.06, 0.8, odd: 0.5), N(300, 0.3, 0.06, 0.8, odd: 0.5), N(C6, 0.6, 1.8, 0.2), N(G5, 0.6, 1.8, 0.2, amp: 0.5) }),
        ("팡파레", new[] { N(G5, 0, 0.12, 0.5), N(G5, 0.14, 0.12, 0.5), N(G5, 0.28, 0.12, 0.5), N(C6, 0.42, 0.5, 0.5), N(G5, 0.9, 0.15, 0.5), N(C6, 1.05, 0.15, 0.5), N(E6, 1.2, 1.3, 0.5), N(C6, 1.2, 1.3, 0.5, amp: 0.5) }),
        ("마무리", new[] { N(C6, 0, 0.3), N(G5, 0.2, 0.3), N(E5, 0.4, 0.3), N(C5, 0.6, 0.5), N(G4, 0.9, 0.4), N(C5, 1.1, 1.4), N(E5, 1.1, 1.4, amp: 0.4), N(G5, 1.1, 1.4, amp: 0.4) }),
        // 1.1.9 전에 쓰던 짧고 또렷한 소리 (울림 없이 그대로)
        ("(구) 띵동", new[] { N(E6, 0, 0.45), N(C6, 0.2, 0.6) }),
        ("(구) 띠링", new[] { N(C6, 0, 0.4), N(E6, 0.09, 0.4), N(G6, 0.18, 0.55) }),
        ("(구) 띵 띵", new[] { N(A5, 0, 0.28), N(A5, 0.24, 0.4) }),
        ("(구) 삐빅", new[] { N(B6, 0, 0.1, 0.5), N(B6, 0.13, 0.14, 0.5) }),
    };

    /// <summary>이 번호부터는 예전 짧은 소리 (울림을 넣지 않는다)</summary>
    const int OldFrom = 30;

    /// <summary>소리 이름 (번호 순서)</summary>
    public static readonly string[] Names = Lib.Select(s => s.Name).ToArray();
    public static int Count => Lib.Length;

    public static int DefaultPick(Kind k) => Defaults[(int)k];

    /// <summary>이 알림에 고른 소리 번호</summary>
    public static int Pick(Kind k)
    {
        var p = St?.SoundPicks;
        int i = (int)k;
        return p != null && i < p.Count && p[i] >= 0 && p[i] < Count ? p[i] : Defaults[i];
    }

    /// <summary>이 알림의 소리를 바꿔 설정에 적는다 (저장은 부르는 쪽이 한다)</summary>
    public static void SetPick(Settings s, Kind k, int idx)
    {
        var p = s.SoundPicks ??= new List<int>();
        while (p.Count < Defaults.Length) p.Add(Defaults[p.Count]);
        p[(int)k] = (idx % Count + Count) % Count;
    }

    /// <summary>설정이 켜져 있으면 알림 소리를 낸다. 앞 소리가 나고 있으면 끊고 낸다.</summary>
    public static void Play(Kind k)
    {
        if (St is { SoundOn: false }) return;
        PlaySound(Pick(k), St?.SoundVol ?? 60);
    }

    /// <summary>설정과 상관없이 주어진 크기로 낸다 (소리 크기를 바꿀 때 들려주기용)</summary>
    public static void Play(Kind k, int vol) => PlaySound(Pick(k), vol);

    /// <summary>제목을 읽기 전에 기다릴 초: 고른 소리의 가락이 대충 끝날 때까지 (길어도 1.2초)</summary>
    public static double Lead(Kind k) => Math.Min(1.2, Lib[Pick(k)].Notes.Max(n => n.At + n.Len * 0.5));

    /// <summary>나고 있는 알림음을 멈춘다 (제목을 읽기 시작할 때)</summary>
    public static void Stop()
    {
        try { player?.Stop(); } catch (Exception ex) { Log.Write("sound", "알림음을 멈추지 못함", ex); }
    }

    /// <summary>번호로 고른 소리를 주어진 크기로 낸다 (소리 고르기 창에서 들려주기용)</summary>
    public static void PlaySound(int idx, int vol)
    {
        vol = Math.Clamp(vol, 0, 100);
        if (vol == 0 || idx < 0 || idx >= Count) return;
        try
        {
            if (!cache.TryGetValue((idx, vol), out var wav)) cache[(idx, vol)] = wav = Make(Lib[idx].Notes, vol / 100.0, echo: idx < OldFrom);
            player?.Stop();
            player?.Dispose();
            player = new SoundPlayer(new MemoryStream(wav));
            player.Play(); // 기다리지 않고 바로 돌아온다
        }
        catch (Exception ex) { Log.Write("sound", "알림음을 재생하지 못함", ex); }
    }

    // 울림: 0.2초 뒤에 작게 한 번 더 들리게 해서 소리가 부드럽게 이어지고 사라지게 한다
    const double EchoDelay = 0.2, EchoGain = 0.25;

    static byte[] Make(Note[] notes, double vol, bool echo = true)
    {
        double total = notes.Max(n => n.At + n.Len) + (echo ? 0.45 : 0.05);
        int count = (int)(total * Rate);
        var mix = new double[count];
        foreach (var n in notes)
        {
            int start = (int)(n.At * Rate), len = (int)(n.Len * Rate);
            double slide = n.To > 0 ? n.To - n.Hz : 0;
            for (int i = 0; i < len && start + i < count; i++)
            {
                double t = (double)i / Rate;
                // 5ms 동안 올라오고, 그 뒤 부드럽게 줄어든다 (끝이 뚝 끊기지 않게)
                double env = Math.Min(1, t / 0.005) * Math.Exp(-t * 5.5 / n.Len) * (1 - (double)i / len);
                // 미끄러지는 음은 주파수가 길이 동안 고르게 바뀐다
                double ph = 2 * Math.PI * (n.Hz * t + slide * t * t / (2 * n.Len));
                double w = Math.Sin(ph) + n.Bright * Math.Sin(2 * ph) + n.Odd * Math.Sin(3 * ph);
                mix[start + i] += n.Amp * w * env / (1 + n.Bright + n.Odd);
            }
        }
        int d = (int)(EchoDelay * Rate);
        if (echo) for (int i = d; i < count; i++) mix[i] += EchoGain * mix[i - d];
        // 크기: 가장 클 때 0.55 정도까지만 (게임 소리 위에서도 거슬리지 않게)
        double peak = Math.Max(1, mix.Max(Math.Abs));
        double gain = 0.55 * vol * vol / peak; // 귀에 맞게 제곱으로 줄인다

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        int bytes = count * 2;
        bw.Write("RIFF"u8); bw.Write(36 + bytes); bw.Write("WAVE"u8);
        bw.Write("fmt "u8); bw.Write(16); bw.Write((short)1); bw.Write((short)1);
        bw.Write(Rate); bw.Write(Rate * 2); bw.Write((short)2); bw.Write((short)16);
        bw.Write("data"u8); bw.Write(bytes);
        foreach (var v in mix) bw.Write((short)Math.Clamp(v * gain * short.MaxValue, short.MinValue, short.MaxValue));
        bw.Flush();
        return ms.ToArray();
    }
}
