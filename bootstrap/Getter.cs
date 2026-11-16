using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace MapleBarSetup;

/// <summary>사람에게 그대로 보여 줄 실패 문구</summary>
sealed class Fail(string message) : Exception(message);

/// <summary>받아서 확인까지 끝난 설치 파일. 설치가 끝날 때까지 파일을 잠가 두어 바꿔치기하지 못하게 한다</summary>
sealed class Downloaded(string version, string file, string dir, FileStream lockStream) : IDisposable
{
    public string Version { get; } = version;
    public string File { get; } = file;

    public void Dispose()
    {
        lockStream.Dispose();
        try { Directory.Delete(dir, true); } catch { }
    }
}

/// <summary>메이플 바 최신 설치 파일을 깃허브에서 받아 확인하고 설치한다.
/// 메이플 바 안의 자동 업데이트(UpdateCheck.cs)와 같은 규칙을 쓴다: 깃허브에서만 받고, SHA256이 맞아야 설치한다.</summary>
static class Getter
{
    const string Repo = "https://github.com/choimy0204/maplebar";
    const long Max = 300L * 1024 * 1024; // 설치 파일은 100MB도 안 된다. 이보다 크면 이상한 파일이다
    static readonly Regex TagVer = new(@"/tag/v?(\d+(?:\.\d+){1,3})$", RegexOptions.IgnoreCase);
    static readonly Regex Hex64 = new("^[0-9A-Fa-f]{64}$");

    /// <summary>처음 묻는 주소: 깃허브(github.com) https만</summary>
    static bool IsGitHub(Uri u) =>
        u.Scheme == Uri.UriSchemeHttps && u.IsDefaultPort && u.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase);

    /// <summary>넘겨 주는 주소: 깃허브가 파일을 두는 곳(objects.githubusercontent.com 등)까지만</summary>
    static bool IsGitHubFile(Uri u) =>
        IsGitHub(u) || (u.Scheme == Uri.UriSchemeHttps && u.IsDefaultPort &&
                        u.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase));

    static HttpClient NewHttp()
    {
        // 넘겨 주는 주소를 하나씩 확인하려고 자동으로 따라가지 않는다
        var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MapleBar-Setup");
        return http;
    }

    /// <summary>최신 릴리스 주소가 어느 태그로 넘어가는지 보고 버전을 알아낸다</summary>
    static async Task<string> LatestAsync(HttpClient http, CancellationToken ct)
    {
        var start = new Uri(Repo + "/releases/latest");
        using var res = await http.SendAsync(new HttpRequestMessage(HttpMethod.Head, start), ct);
        var loc = res.Headers.Location;
        if (loc == null || !IsGitHub(new Uri(start, loc))) throw new Fail("최신 버전을 찾지 못했어요.");
        var m = TagVer.Match(new Uri(start, loc).AbsolutePath);
        if (!m.Success) throw new Fail("최신 버전을 찾지 못했어요.");
        return m.Groups[1].Value;
    }

    /// <summary>github.com 주소를 받는다. 넘겨 주는 주소는 깃허브 것만 따라간다</summary>
    static async Task<HttpResponseMessage> GetAsync(HttpClient http, Uri url, CancellationToken ct)
    {
        if (!IsGitHub(url)) throw new Fail("받을 주소가 이상해요.");
        for (int hop = 0; hop < 6; hop++)
        {
            var res = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            int code = (int)res.StatusCode;
            if (code >= 300 && code < 400 && res.Headers.Location != null)
            {
                url = new Uri(url, res.Headers.Location);
                res.Dispose();
                if (!IsGitHubFile(url)) throw new Fail("받을 주소가 이상해요.");
                continue;
            }
            if (!res.IsSuccessStatusCode)
            {
                res.Dispose();
                throw new Fail("설치 파일을 찾지 못했어요. 잠시 뒤에 다시 해 주세요.");
            }
            return res;
        }
        throw new Fail("받을 주소가 이상해요.");
    }

    /// <summary>SHA256.txt에서 이 파일 이름의 값을 찾는다 ("값  파일이름" 줄)</summary>
    static string? FindHash(string text, string name)
    {
        foreach (var line in text.Split('\n'))
        {
            var p = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length >= 2 && Hex64.IsMatch(p[0]) && p[1].TrimStart('*').Equals(name, StringComparison.OrdinalIgnoreCase))
                return p[0];
        }
        return null;
    }

    /// <summary>최신 설치 파일과 SHA256 값을 받아 맞춰 본다. report(문구, 퍼센트)로 진행 상황을 알린다</summary>
    public static async Task<Downloaded> DownloadAsync(Action<string, int>? report, CancellationToken ct)
    {
        using var http = NewHttp();
        report?.Invoke("최신 버전을 찾고 있어요…", 0);
        var ver = await LatestAsync(http, ct);
        var name = $"MapleBar-Setup-{ver}.exe";
        var baseUrl = $"{Repo}/releases/download/v{ver}/";

        // 확인 값을 먼저 받는다 (작은 파일만)
        string want;
        using (var res = await GetAsync(http, new Uri(baseUrl + "SHA256.txt"), ct))
        {
            if (res.Content.Headers.ContentLength > 64 * 1024) throw new Fail("받은 파일을 확인할 수 없어요.");
            using var s = await res.Content.ReadAsStreamAsync();
            var buf = new byte[64 * 1024];
            int len = 0, n;
            while (len < buf.Length && (n = await s.ReadAsync(buf, len, buf.Length - len, ct)) > 0) len += n;
            want = FindHash(Encoding.ASCII.GetString(buf, 0, len), name) ?? throw new Fail("받은 파일을 확인할 수 없어요.");
        }

        // 받을 때마다 새 폴더에 받는다
        var dir = Path.Combine(Path.GetTempPath(), "MapleBar-Setup", Guid.NewGuid().ToString("N").Substring(0, 8));
        var file = Path.Combine(dir, name);
        FileStream? lck = null;
        try
        {
            Directory.CreateDirectory(dir);
            using (var res = await GetAsync(http, new Uri(baseUrl + name), ct))
            {
                long total = res.Content.Headers.ContentLength ?? 0, done = 0;
                if (total > Max) throw new Fail("받을 파일이 너무 커요.");
                using var src = await res.Content.ReadAsStreamAsync();
                using var dst = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
                var buf = new byte[81920];
                int n, shown = -1;
                while ((n = await src.ReadAsync(buf, 0, buf.Length, ct)) > 0)
                {
                    await dst.WriteAsync(buf, 0, n, ct);
                    done += n;
                    if (done > Max) throw new Fail("받을 파일이 너무 커요.");
                    int pct = total > 0 ? (int)(done * 100 / total) : 0;
                    if (pct != shown) { shown = pct; report?.Invoke($"메이플 바 {ver} 받는 중… {pct}%", pct); }
                }
            }

            // 깃허브가 함께 올린 SHA256 값과 같아야 설치한다. 확인한 뒤 설치가 끝날 때까지 쓰기를 막아 둔다
            report?.Invoke("받은 파일을 확인하고 있어요…", 100);
            lck = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            var l = lck;
            var got = await Task.Run(() => { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(l)).Replace("-", ""); }, ct);
            if (!got.Equals(want, StringComparison.OrdinalIgnoreCase)) throw new Fail("받은 파일이 원본과 달라요. 다시 시도해 주세요.");
            return new Downloaded(ver, file, dir, lck);
        }
        catch
        {
            lck?.Dispose();
            try { Directory.Delete(dir, true); } catch { }
            throw;
        }
    }

    /// <summary>받은 설치 파일을 진행 창만 보이게 실행하고 끝날 때까지 기다린다. 끝난 코드를 돌려준다 (0이면 성공)</summary>
    public static async Task<int> InstallAsync(Downloaded d)
    {
        Process? p;
        try
        {
            p = Process.Start(new ProcessStartInfo(d.File, "/SP- /SILENT /SUPPRESSMSGBOXES /NORESTART") { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) // 관리자 권한 창에서 [아니요]
        {
            throw new Fail("설치를 취소했어요.");
        }
        if (p == null) throw new Fail("설치를 시작하지 못했어요.");
        using (p)
        {
            await Task.Run(() => p.WaitForExit());
            return p.ExitCode;
        }
    }

    /// <summary>설치된 메이플 바를 켠다. 관리자만 고칠 수 있는 HKLM에 적힌 설치 위치만 본다</summary>
    public static bool StartBar()
    {
        const string key = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{6F3C2A9E-4B7D-4E21-9A55-8C1D0E7B3F42}_is1";
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var k = root.OpenSubKey(key);
                if (k?.GetValue("InstallLocation") is not string dir || dir.Length == 0) continue;
                var exe = Path.Combine(dir, "MapleBar.exe");
                if (!System.IO.File.Exists(exe)) continue;
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = dir })?.Dispose();
                return true;
            }
            catch { }
        }
        return false;
    }

    /// <summary>실패를 쉬운 문구로</summary>
    public static string Message(Exception ex) => ex switch
    {
        Fail f => f.Message,
        HttpRequestException or TaskCanceledException or System.Net.WebException => "인터넷 연결을 확인해 주세요.",
        IOException or UnauthorizedAccessException => "받은 파일을 저장하지 못했어요.",
        _ => "설치 파일을 받지 못했어요.",
    };
}
