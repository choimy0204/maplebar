using System.Net.Http;
using System.Text.Json;

namespace MapleBar;

/// <summary>오늘 메이플 바를 켠 사람 수를 세도록 하루에 한 번 서버에 알린다 (한국 시간 기준).
/// 보내는 건 "오늘 켰어요" 신호뿐이다 (번호·닉네임·컴퓨터 정보는 보내지 않는다).
/// 오늘 이미 보냈는지는 내 PC의 파일에 날짜로 적어 둔다. 켜 둔 채 날이 바뀌면 한 번 더 보낸다.
/// 서버 주소는 docs/dev.json의 "presence"에서 받는다. 없으면 아무것도 보내지 않는다.</summary>
static class Presence
{
    const string DevUrl = "https://raw.githubusercontent.com/choimy0204/maplebar/main/docs/dev.json";
    static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(10) };
    static readonly string dayFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MapleBar", "ping.txt");
    static System.Threading.Timer? timer;
    static int busy;

    /// <summary>켤 때 한 번 보고, 그 뒤엔 1시간마다 날이 바뀌었는지만 본다 (날이 같으면 아무것도 보내지 않는다)</summary>
    public static void Start() =>
        timer = new System.Threading.Timer(_ => _ = Check(), null, TimeSpan.Zero, TimeSpan.FromHours(1));

    public static void Stop() { timer?.Dispose(); timer = null; }

    static string Today() => DateTime.UtcNow.AddHours(9).ToString("yyyy-MM-dd");

    static async Task Check()
    {
        if (Interlocked.Exchange(ref busy, 1) == 1) return;
        try
        {
            var today = Today();
            string? sent = null;
            try { sent = File.ReadAllText(dayFile).Trim(); } catch { }
            if (sent == today) return;

            using var doc = JsonDocument.Parse(await http.GetStringAsync(DevUrl));
            if (!doc.RootElement.TryGetProperty("presence", out var p) || p.ValueKind != JsonValueKind.String) return;
            var url = p.GetString()?.TrimEnd('/');
            if (string.IsNullOrEmpty(url) || !url.StartsWith("https://", StringComparison.Ordinal)) return;

            using var res = await http.PostAsync(url + "/hit", null);
            if (!res.IsSuccessStatusCode) return; // 다음 시간에 다시 해 본다
            Directory.CreateDirectory(Path.GetDirectoryName(dayFile)!);
            File.WriteAllText(dayFile, today);
        }
        catch { } // 못 보내면 조용히 넘어간다
        finally { Interlocked.Exchange(ref busy, 0); }
    }
}
