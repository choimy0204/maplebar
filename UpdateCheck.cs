using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MapleBar;

/// <summary>켤 때 GitHub 최신 릴리스(와 예전 dist 폴더)에 더 새 설치 파일이 있는지 본다.
/// 있으면 업데이트해야 켤 수 있다. 확인에 실패하면(인터넷 끊김 등) 막지 않고 그냥 켠다.</summary>
static class UpdateCheck
{
    const string ReleaseUrl = "https://api.github.com/repos/choimy0204/maplebar/releases/latest";
    const string ListUrl ="https://api.github.com/repos/choimy0204/maplebar/contents/dist";
    static readonly Regex SetupName = new(@"^MapleBar-Setup-(\d+(?:\.\d+){1,3})\.exe$", RegexOptions.IgnoreCase);

    /// <summary>켜도 되면 true. 새 버전이 있으면 안내하고 false.</summary>
    public static bool AllowStart()
    {
        var latest = FindLatest();
        if (latest == null) return true;
        var (ver, url) = latest.Value;
        var mine = Norm(typeof(UpdateCheck).Assembly.GetName().Version ?? new Version(0, 0));
        if (Norm(ver) <= mine) return true;

        var answer = MessageBox.Show(
            $"새 버전 {ver}이 나왔어요. 업데이트해야 메이플 바를 켤 수 있어요.\n\n" +
            $"지금 버전: {mine.ToString(3)}\n\n[예]를 누르면 새 설치 파일을 내려받아요. 받은 파일을 실행해서 설치해 주세요.",
            "메이플 바 업데이트", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
        if (answer == DialogResult.Yes)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }
        return false;
    }

    static (Version ver, string url)? FindLatest()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MapleBar");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            (Version ver, string url)? best = null;
            void Take(string? name, string? url)
            {
                if (name == null || url == null) return;
                var m = SetupName.Match(name);
                if (!m.Success || !Version.TryParse(m.Groups[1].Value, out var v)) return;
                if (best == null || Norm(v) > Norm(best.Value.ver)) best = (v, url);
            }
            // 깃허브가 직접 만든 최신 릴리스의 설치 파일
            try
            {
                using var rel = JsonDocument.Parse(http.GetStringAsync(ReleaseUrl).GetAwaiter().GetResult());
                if (rel.RootElement.TryGetProperty("assets", out var assets))
                    foreach (var a in assets.EnumerateArray())
                        Take(a.TryGetProperty("name", out var n) ? n.GetString() : null,
                             a.TryGetProperty("browser_download_url", out var d) ? d.GetString() : null);
            }
            catch { }
            // 예전 방식: 저장소 dist 폴더
            try
            {
                using var doc = JsonDocument.Parse(http.GetStringAsync(ListUrl).GetAwaiter().GetResult());
                foreach (var f in doc.RootElement.EnumerateArray())
                    Take(f.TryGetProperty("name", out var n) ? n.GetString() : null,
                         f.TryGetProperty("download_url", out var d) ? d.GetString() : null);
            }
            catch { }
            return best;
        }
        catch { return null; } // 인터넷 끊김, GitHub 응답 없음, 요청 한도 초과 등: 막지 않는다
    }

    // 1.0.4 와 1.0.4.0 을 같게 본다
    static Version Norm(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));
}
