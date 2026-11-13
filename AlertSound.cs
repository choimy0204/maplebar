using System.Media;

namespace MapleBar;

/// <summary>알림 소리: 소리 파일 없이 짧은 음을 직접 만들어 낸다. 알림마다 소리가 다르다.</summary>
static class AlertSound
{
    public enum Kind { Janus, Pickup, Rune, Buff, Dope }

    /// <summary>켜기·크기를 읽을 설정 (JanusWatch가 넣는다)</summary>
    public static Settings? St;

    const int Rate = 44100;
    static SoundPlayer? player;   // 재생 중에 사라지지 않게 잡아 둔다
    static readonly Dictionary<(Kind, int), byte[]> cache = new();

    // 음 하나: 주파수(Hz), 시작(초), 길이(초), 배음 세기(높을수록 날카롭다)
    readonly record struct Note(double Hz, double At, double Len, double Bright = 0.25);

    static Note[] Notes(Kind k) => k switch
    {
        // 띵–동: 높은 음에서 낮은 음으로
        Kind.Janus => new[] { new Note(1318.5, 0, 0.45), new Note(1046.5, 0.2, 0.6) },
        // 톡톡톡: 짧게 세 번
        Kind.Pickup => new[] { new Note(1568, 0, 0.09), new Note(1568, 0.11, 0.09), new Note(1568, 0.22, 0.12) },
        // 띠링: 세 음이 올라간다
        Kind.Rune => new[] { new Note(1046.5, 0, 0.4), new Note(1318.5, 0.09, 0.4), new Note(1568, 0.18, 0.55) },
        // 띵 띵: 같은 음 두 번
        Kind.Buff => new[] { new Note(880, 0, 0.28), new Note(880, 0.24, 0.4) },
        // 삐빅: 높고 빠르게 두 번 (가장 급한 소리)
        _ => new[] { new Note(1975.5, 0, 0.1, 0.5), new Note(1975.5, 0.13, 0.14, 0.5) },
    };

    /// <summary>설정이 켜져 있으면 알림 소리를 낸다. 앞 소리가 나고 있으면 끊고 낸다.</summary>
    public static void Play(Kind k)
    {
        if (St is { SoundOn: false }) return;
        Play(k, St?.SoundVol ?? 60);
    }

    /// <summary>설정과 상관없이 주어진 크기로 낸다 (소리 크기를 바꿀 때 들려주기용)</summary>
    public static void Play(Kind k, int vol)
    {
        vol = Math.Clamp(vol, 0, 100);
        if (vol == 0) return;
        try
        {
            if (!cache.TryGetValue((k, vol), out var wav)) cache[(k, vol)] = wav = Make(Notes(k), vol / 100.0);
            player?.Stop();
            player?.Dispose();
            player = new SoundPlayer(new MemoryStream(wav));
            player.Play(); // 기다리지 않고 바로 돌아온다
        }
        catch (Exception ex) { Log.Write("sound", "알림음을 재생하지 못함", ex); }
    }

    static byte[] Make(Note[] notes, double vol)
    {
        double total = notes.Max(n => n.At + n.Len) + 0.05;
        int count = (int)(total * Rate);
        var mix = new double[count];
        foreach (var n in notes)
        {
            int start = (int)(n.At * Rate), len = (int)(n.Len * Rate);
            for (int i = 0; i < len && start + i < count; i++)
            {
                double t = (double)i / Rate;
                // 5ms 동안 올라오고, 그 뒤 부드럽게 줄어든다 (끝이 뚝 끊기지 않게)
                double env = Math.Min(1, t / 0.005) * Math.Exp(-t * 5.5 / n.Len) * (1 - (double)i / len);
                double w = Math.Sin(2 * Math.PI * n.Hz * t) + n.Bright * Math.Sin(4 * Math.PI * n.Hz * t);
                mix[start + i] += w * env / (1 + n.Bright);
            }
        }
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
