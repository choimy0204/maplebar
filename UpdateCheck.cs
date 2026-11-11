using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MapleBar;

/// <summary>켤 때 GitHub 최신 릴리스(와 예전 dist 폴더)에 더 새 설치 파일이 있는지 본다.
/// 있으면 업데이트해야 켤 수 있다: 앱이 새 설치 파일을 받아 조용히 설치하고 다시 켠다.
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
        var mine = Norm(typeof(UpdateCheck).Assembly.GetName().Version ?? new Version(0, 0));
        if (Norm(ver) <= mine) return true;

        var answer = MessageBox.Show(
            $"새 버전 {ver}이 나왔어요. 업데이트해야 메이플 바를 켤 수 있어요.\n\n" +
            $"지금 버전: {mine.ToString(3)}\n\n[예]를 누르면 바로 업데이트해요. 끝나면 메이플 바가 다시 켜져요.",
            "메이플 바 업데이트", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
        if (answer == DialogResult.Yes) Install(url, sha);
        return false;
    }

    /// <summary>설치 파일을 받아(받을 수 있으면 SHA256 확인) 조용히 설치하고, 끝나면 메이플 바를 다시 켠다.</summary>
    static void Install(string url, string? shaUrl)
    {
        string file = Path.Combine(Path.GetTempPath(), "MapleBar-Update", Path.GetFileName(new Uri(url).LocalPath));
        string? error = null;
        using var form = new Form
        {
            Text = "메이플 바 업데이트", FormBorderStyle = FormBorderStyle.FixedDialog, ControlBox = false,
            StartPosition = FormStartPosition.CenterScreen, ClientSize = new Size(360, 84), TopMost = true,
            Font = new Font("맑은 고딕", 9.5f), ShowInTaskbar = true,
        };
        var label = new Label { Text = "새 버전을 받는 중이에요…", Location = new Point(16, 14), AutoSize = true };
        var bar = new ProgressBar { Location = new Point(16, 44), Size = new Size(328, 18) };
        form.Controls.Add(label); form.Controls.Add(bar);
        form.Shown += async (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("MapleBar");
                using (var res = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
                {
                    res.EnsureSuccessStatusCode();
                    long total = res.Content.Headers.ContentLength ?? 0, done = 0;
                    await using var src = await res.Content.ReadAsStreamAsync();
                    await using var dst = File.Create(file);
                    var buf = new byte[81920];
                    int n;
                    while ((n = await src.ReadAsync(buf)) > 0)
                    {
                        await dst.WriteAsync(buf.AsMemory(0, n));
                        done += n;
                        if (total > 0) bar.Value = (int)Math.Min(100, done * 100 / total);
                    }
                }
                // 깃허브가 함께 올린 SHA256 값과 같아야 설치한다 (받다가 깨진 파일 방지)
                if (shaUrl != null)
                {
                    label.Text = "받은 파일을 확인하는 중이에요…";
                    var want = (await http.GetStringAsync(shaUrl)).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0];
                    string got;
                    await using (var s = File.OpenRead(file)) got = Convert.ToHexString(await SHA256.HashDataAsync(s));
                    if (!got.Equals(want, StringComparison.OrdinalIgnoreCase)) throw new Exception("받은 파일이 원본과 달라요.");
                }
            }
            catch (Exception ex) { error = ex.Message; }
            form.Close();
        };
        form.ShowDialog();

        if (error != null)
        {
            if (MessageBox.Show($"업데이트를 받지 못했어요.\n({error})\n\n브라우저로 설치 파일을 받을까요?", "메이플 바 업데이트",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
            return;
        }

        // 메이플 바가 꺼진 뒤 조용히 설치하고, 끝나면 다시 켠다 (설치 창 없이)
        static string Q(string s) => "'" + s.Replace("'", "''") + "'";
        var script = $"Start-Sleep 1; Start-Process -FilePath {Q(file)} -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-' -Wait; " +
                     $"Remove-Item -LiteralPath {Q(file)} -ErrorAction SilentlyContinue; Start-Process -FilePath {Q(Application.ExecutablePath)}";
        try
        {
            Process.Start(new ProcessStartInfo("powershell.exe",
                "-NoProfile -WindowStyle Hidden -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script)))
            { UseShellExecute = false, CreateNoWindow = true });
            MessageBox.Show("설치하고 있어요. 1분쯤 뒤에 메이플 바가 다시 켜져요.", "메이플 바 업데이트",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch
        {
            try { Process.Start(new ProcessStartInfo(file) { UseShellExecute = true }); } catch { } // 직접 설치 창으로
        }
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
