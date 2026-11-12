using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MapleBar;

/// <summary>켤 때 GitHub 최신 릴리스(와 예전 dist 폴더)에 더 새 설치 파일이 있는지 본다.
/// 있으면 업데이트해야 켤 수 있다: 업데이트 창에서 받고, 앱이 꺼진 뒤 조용히 설치해 바로 다시 켠다.
/// 확인에 실패하면(인터넷 끊김 등) 막지 않고 그냥 켠다.</summary>
static class UpdateCheck
{
    const string ReleaseUrl = "https://api.github.com/repos/choimy0204/maplebar/releases/latest";
    const string ListUrl ="https://api.github.com/repos/choimy0204/maplebar/contents/dist";
    static readonly Regex SetupName = new(@"^MapleBar-Setup-(\d+(?:\.\d+){1,3})\.exe$", RegexOptions.IgnoreCase);

    /// <summary>켜도 되면 true. 새 버전이 있으면 업데이트를 하고 false.</summary>
    public static bool AllowStart()
    {
        var latest = FindLatest();
        if (latest == null) return true;
        var (ver, url, sha) = latest.Value;
        var mine = Mine();
        if (Norm(ver) <= mine) return true;

        using var w = new UpdateWindow(mine, ver, url, sha, ask: true);
        w.ShowDialog();
        if (w.File != null) StartInstall(w.File);
        return false;
    }

    static System.Windows.Forms.Timer? watchTimer;
    static Version? asked; // 이번 실행에서 이미 물어본 버전 (아니요를 누르면 다음에 켤 때 업데이트한다)

    /// <summary>켜져 있는 동안 1분마다 새 버전을 확인해, 있으면 오른쪽 아래에 물어본다.</summary>
    public static void StartWatching()
    {
        if (watchTimer != null) return; // 바를 다시 만들어도 한 번만 돈다
        watchTimer = new System.Windows.Forms.Timer { Interval = 60 * 1000 };
        bool busy = false;
        watchTimer.Tick += async (_, _) =>
        {
            if (asked != null || busy) return;
            busy = true;
            var latest = await Task.Run(LatestTag);
            busy = false;
            var mine = Mine();
            if (latest == null || Norm(latest.Value.ver) <= mine || asked != null) return;
            asked = latest.Value.ver;
            var (ver, url, sha) = latest.Value;
            UpdateToast.Ask(() =>
            {
                using var w = new UpdateWindow(mine, ver, url, sha, ask: false);
                w.ShowDialog();
                if (w.File != null && StartInstall(w.File)) Application.Exit(); // 꺼져야 설치가 시작된다
            });
        };
        watchTimer.Start();
    }

    static Version Mine() => Norm(typeof(UpdateCheck).Assembly.GetName().Version ?? new Version(0, 0));

    /// <summary>메이플 바가 완전히 꺼지기를 기다렸다가 조용히 설치하고, 끝나면 바로 다시 켠다 (설치 창 없이).
    /// 켜져 있는 채로 설치하면 파일을 못 바꿔 옛 버전이 다시 켜지므로 반드시 꺼진 뒤에 설치한다.</summary>
    static bool StartInstall(string file)
    {
        static string Q(string s) => "'" + s.Replace("'", "''") + "'";
        int pid = Environment.ProcessId;
        var script = $"$p = Get-Process -Id {pid} -ErrorAction SilentlyContinue; if ($p) {{ $null = $p.WaitForExit(30000) }}; " +
                     $"Start-Process -FilePath {Q(file)} -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-' -Wait; " +
                     $"Remove-Item -LiteralPath {Q(file)} -ErrorAction SilentlyContinue; " +
                     // 설치된 곳의 새 버전을 켠다 (다른 곳에서 켠 옛 파일을 다시 켜면 업데이트 창이 또 뜬다)
                     "$k = 'Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\{6F3C2A9E-4B7D-4E21-9A55-8C1D0E7B3F42}_is1'; " +
                     "$d = @(('HKCU:\\' + $k), ('HKLM:\\' + $k), ('HKLM:\\Software\\WOW6432Node\\' + $k.Substring(9))) | ForEach-Object { (Get-ItemProperty -LiteralPath $_ -ErrorAction SilentlyContinue).InstallLocation } | Where-Object { $_ } | Select-Object -First 1; " +
                     $"$e = if ($d -and (Test-Path -LiteralPath (Join-Path $d 'MapleBar.exe'))) {{ Join-Path $d 'MapleBar.exe' }} else {{ {Q(Application.ExecutablePath)} }}; " +
                     "Start-Process -FilePath $e";
        try
        {
            Process.Start(new ProcessStartInfo("powershell.exe",
                "-NoProfile -WindowStyle Hidden -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script)))
            { UseShellExecute = false, CreateNoWindow = true });
            return true;
        }
        catch
        {
            try { Process.Start(new ProcessStartInfo(file) { UseShellExecute = true }); } catch { } // 직접 설치 창으로
            return false;
        }
    }

    const string Repo = "https://github.com/choimy0204/maplebar";
    static readonly Regex TagVer = new(@"/tag/v?(\d+(?:\.\d+){1,3})$", RegexOptions.IgnoreCase);

    /// <summary>1분마다 쓰는 가벼운 확인: 최신 릴리스 주소가 어느 태그로 넘어가는지만 본다.
    /// (GitHub API는 한 시간에 60번까지라 1분마다 부르면 막힌다)</summary>
    static (Version ver, string url, string? sha)? LatestTag()
    {
        try
        {
            using var h = new HttpClientHandler { AllowAutoRedirect = false };
            using var http = new HttpClient(h) { Timeout = TimeSpan.FromSeconds(5) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MapleBar");
            using var res = http.Send(new HttpRequestMessage(HttpMethod.Head, Repo + "/releases/latest"));
            var m = TagVer.Match(res.Headers.Location?.ToString() ?? "");
            if (!m.Success || !Version.TryParse(m.Groups[1].Value, out var v)) return null;
            var t = m.Groups[1].Value;
            return (v, $"{Repo}/releases/download/v{t}/MapleBar-Setup-{t}.exe", $"{Repo}/releases/download/v{t}/SHA256.txt");
        }
        catch { return null; }
    }

    static (Version ver, string url, string? sha)? FindLatest()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MapleBar");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            (Version ver, string url, string? sha)? best = null;
            void Take(string? name, string? url, string? sha)
            {
                if (name == null || url == null) return;
                var m = SetupName.Match(name);
                if (!m.Success || !Version.TryParse(m.Groups[1].Value, out var v)) return;
                if (best == null || Norm(v) > Norm(best.Value.ver)) best = (v, url, sha);
            }
            // 깃허브가 직접 만든 최신 릴리스의 설치 파일 (SHA256.txt가 함께 있다)
            try
            {
                using var rel = JsonDocument.Parse(http.GetStringAsync(ReleaseUrl).GetAwaiter().GetResult());
                if (rel.RootElement.TryGetProperty("assets", out var assets))
                {
                    var list = assets.EnumerateArray().Select(a => (
                        name: a.TryGetProperty("name", out var n) ? n.GetString() : null,
                        url: a.TryGetProperty("browser_download_url", out var d) ? d.GetString() : null)).ToList();
                    var sha = list.FirstOrDefault(a => string.Equals(a.name, "SHA256.txt", StringComparison.OrdinalIgnoreCase)).url;
                    foreach (var a in list) Take(a.name, a.url, sha);
                }
            }
            catch { }
            // 예전 방식: 저장소 dist 폴더
            try
            {
                using var doc = JsonDocument.Parse(http.GetStringAsync(ListUrl).GetAwaiter().GetResult());
                foreach (var f in doc.RootElement.EnumerateArray())
                    Take(f.TryGetProperty("name", out var n) ? n.GetString() : null,
                         f.TryGetProperty("download_url", out var d) ? d.GetString() : null, null);
            }
            catch { }
            return best;
        }
        catch { return null; } // 인터넷 끊김, GitHub 응답 없음, 요청 한도 초과 등: 막지 않는다
    }

    // 1.0.4 와 1.0.4.0 을 같게 본다
    static Version Norm(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));
}
