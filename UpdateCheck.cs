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
    static readonly Regex SetupName = new(@"^MapleBar-Setup-(\d+(?:\.\d+){1,3})\.exe$", RegexOptions.IgnoreCase);

    /// <summary>켜도 되면 true. 새 버전이 있으면 업데이트를 하고 false.</summary>
    public static bool AllowStart()
    {
        var latest = FindLatest();
        if (latest == null) return true;
        var (ver, url, sha) = latest.Value;
        var mine = Mine();
        if (Key(ver) <= Key(mine)) return true;

        using var w = new UpdateWindow(mine, ver, url, sha, ask: true);
        w.ShowDialog();
        if (w.File != null && w.Hash != null) StartInstall(w.File, w.Hash);
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
            if (latest == null || Key(latest.Value.ver) <= Key(mine) || asked != null) return;
            asked = latest.Value.ver;
            watchTimer?.Stop(); // 한 번 물어본 뒤로는 더 확인하지 않으므로 타이머도 멈춘다
            var (ver, url, sha) = latest.Value;
            UpdateToast.Ask(() =>
            {
                using var w = new UpdateWindow(mine, ver, url, sha, ask: false);
                w.ShowDialog();
                if (w.File != null && w.Hash != null && StartInstall(w.File, w.Hash)) ExitForUpdate(); // 꺼져야 설치가 시작된다
            });
        };
        watchTimer.Start();
    }

    /// <summary>업데이트하려고 앱을 끈다.
    /// Application.Exit는 열린 창 목록을 돌면서 닫는데, 바가 닫힐 때 시계 창 등을 같이 닫아 목록이 바뀌면
    /// "Collection was modified" 오류가 난다. 그래서 목록을 복사해 하나씩 닫고, 바는 맨 나중에 닫는다.</summary>
    static void ExitForUpdate()
    {
        try
        {
            var forms = Application.OpenForms.Cast<Form>().ToList();
            foreach (var f in forms) if (f is not BarForm && !f.IsDisposed) f.Close();
            foreach (var f in forms) if (f is BarForm && !f.IsDisposed) f.Close(); // 바가 닫히면 앱이 끝난다
        }
        catch (Exception ex) { Log.Write("업데이트", "창 닫기 실패", ex); }
        Application.ExitThread(); // 혹시 남은 창이 있어도 끝낸다
    }

    static Version Mine() => Norm(typeof(UpdateCheck).Assembly.GetName().Version ?? new Version(0, 0));

    /// <summary>메이플 바가 완전히 꺼지기를 기다렸다가 조용히 설치하고, 끝나면 바로 다시 켠다 (설치 창 없이).
    /// 켜져 있는 채로 설치하면 파일을 못 바꿔 옛 버전이 다시 켜지므로 반드시 꺼진 뒤에 설치한다.</summary>
    static bool StartInstall(string file, string hash)
    {
        static string Q(string s) => "'" + s.Replace("'", "''") + "'";
        int pid = Environment.ProcessId;
        var script = $"$p = Get-Process -Id {pid} -ErrorAction SilentlyContinue; if ($p) {{ $null = $p.WaitForExit(30000) }}; " +
                     // 설치 직전에 한 번 더 맞춰 본다. 다르면 지우고 설치하지 않는다
                     $"if ((Get-FileHash -LiteralPath {Q(file)} -Algorithm SHA256).Hash -ne {Q(hash)}) {{ Remove-Item -LiteralPath {Q(file)} -ErrorAction SilentlyContinue; exit }}; " +
                     $"Start-Process -FilePath {Q(file)} -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-' -Wait; " +
                     $"Remove-Item -LiteralPath {Q(Path.GetDirectoryName(file)!)} -Recurse -ErrorAction SilentlyContinue; " +
                     // 설치된 곳의 새 버전을 켠다. 관리자만 고칠 수 있는 HKLM만 본다 (예전 내 계정 설치에서 옮겨 오면 자리가 바뀐다)
                     "$k = 'SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\{6F3C2A9E-4B7D-4E21-9A55-8C1D0E7B3F42}_is1'; " +
                     "$d = @(('HKLM:\\' + $k), ('HKLM:\\SOFTWARE\\WOW6432Node\\' + $k.Substring(9))) | ForEach-Object { (Get-ItemProperty -LiteralPath $_ -ErrorAction SilentlyContinue).InstallLocation } | Where-Object { $_ } | Select-Object -First 1; " +
                     $"$e = if ($d -and (Test-Path -LiteralPath (Join-Path $d 'MapleBar.exe'))) {{ Join-Path $d 'MapleBar.exe' }} else {{ {Q(Environment.ProcessPath ?? Application.ExecutablePath)} }}; " +
                     "Start-Process -FilePath $e";
        try
        {
            Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
                "-NoProfile -WindowStyle Hidden -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script)))
            { UseShellExecute = false, CreateNoWindow = true })?.Dispose();
            return true;
        }
        catch
        {
            try { Process.Start(new ProcessStartInfo(file) { UseShellExecute = true })?.Dispose(); } catch { } // 직접 설치 창으로
            return false;
        }
    }

    const string Repo = "https://github.com/choimy0204/maplebar";
    static readonly Regex TagVer = new(@"/tag/v?(\d+(?:\.\d+){1,3})$", RegexOptions.IgnoreCase);

    /// <summary>1분마다 쓰는 가벼운 확인: 최신 릴리스 주소가 어느 태그로 넘어가는지만 본다.
    /// (GitHub API는 한 시간에 60번까지라 1분마다 부르면 막힌다)</summary>
    // 1분마다 새로 만들지 않고 하나를 재사용한다 (리디렉션을 따라가지 않는다)
    static readonly HttpClient TagHttp = CreateTagHttp();

    static HttpClient CreateTagHttp()
    {
        var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MapleBar");
        return http;
    }

    static (Version ver, string url, string? sha)? LatestTag()
    {
        try
        {
            using var res = TagHttp.Send(new HttpRequestMessage(HttpMethod.Head, Repo + "/releases/latest"));
            var m = TagVer.Match(res.Headers.Location?.ToString() ?? "");
            if (!m.Success || !Version.TryParse(m.Groups[1].Value, out var v)) return null;
            var t = m.Groups[1].Value;
            return (v, $"{Repo}/releases/download/v{t}/MapleBar-Setup-{t}.exe", $"{Repo}/releases/download/v{t}/SHA256.txt");
        }
        catch { return null; }
    }

    /// <summary>깃허브(github.com) https 주소인지. 업데이트 파일은 여기서만 받는다</summary>
    public static bool IsGitHub([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps &&
        u.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) && u.IsDefaultPort;

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
                if (name == null || !IsGitHub(url) || (sha != null && !IsGitHub(sha))) return;
                var m = SetupName.Match(name);
                if (!m.Success || !Version.TryParse(m.Groups[1].Value, out var v)) return;
                if (best == null || Key(v) > Key(best.Value.ver)) best = (v, url, sha);
            }
            // 두 주소를 한꺼번에 묻는다 (차례로 물으면 응답이 없을 때 켜지는 데 두 배로 걸린다). 결과는 아래에서 예전 순서대로 본다
            var relTask = Task.Run(() => http.GetStringAsync(ReleaseUrl));
            // 깃허브가 직접 만든 최신 릴리스의 설치 파일 (SHA256.txt가 함께 있다)
            try
            {
                using var rel = JsonDocument.Parse(relTask.GetAwaiter().GetResult());
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
            // 저장소 dist 폴더(옛 방식)는 SHA256 확인이 없어 보지 않는다
            return best;
        }
        catch { return null; } // 인터넷 끊김, GitHub 응답 없음, 요청 한도 초과 등: 막지 않는다
    }

    // 1.0.4 와 1.0.4.0 을 같게 본다
    static Version Norm(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));

    /// <summary>비교용 값: 셋째 자리는 소수처럼 읽는다 (1.1.41 = 1.1.4.1 &lt; 1.1.5 = 1.1.50 &lt; 1.1.51 &lt; 1.1.6).
    /// 작은 수정은 끝에 한 자리를 붙이고, 새 기능은 셋째 자리를 올리는 번호 규칙에 맞춘 것.</summary>
    public static Version Key(Version v)
    {
        var b = Math.Max(0, v.Build).ToString();
        b = b.Length >= 4 ? b[..4] : b.PadRight(4, '0');
        return new(v.Major, v.Minor, int.Parse(b), Math.Max(0, v.Revision));
    }

    /// <summary>보여 줄 버전: 1.1.50은 1.1.5로 줄여 보여 준다</summary>
    public static string Show(Version v)
    {
        var b = Math.Max(0, v.Build).ToString();
        if (b.Length > 1) b = b.TrimEnd('0');
        return $"{v.Major}.{v.Minor}.{b}";
    }
}
