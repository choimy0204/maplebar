namespace MapleBar;

/// <summary>
/// 조용히 넘기던 실패를 파일에 적어 둔다 (%LocalAppData%\MapleBar\log.txt).
/// 같은 내용은 1분에 한 번만 적고, 파일이 커지면 log.old.txt로 넘긴다.
/// </summary>
static class Log
{
    const long MaxBytes = 256 * 1024;
    static readonly object gate = new();
    static readonly Dictionary<string, DateTime> last = new();

    public static void Write(string area, string msg, Exception? ex = null)
    {
        try
        {
            lock (gate)
            {
                string key = area + "|" + msg;
                var now = DateTime.Now;
                if (last.TryGetValue(key, out var t) && (now - t).TotalSeconds < 60) return;
                if (last.Count > 200) last.Clear();
                last[key] = now;

                string path = Settings.LogPath;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var fi = new FileInfo(path);
                if (fi.Exists && fi.Length > MaxBytes)
                {
                    string old = Path.Combine(fi.DirectoryName!, "log.old.txt");
                    File.Delete(old);
                    File.Move(path, old);
                }
                string line = $"{now:yyyy-MM-dd HH:mm:ss} [{area}] {msg}";
                if (ex != null) line += $" | {ex.GetType().Name}: {ex.Message}";
                File.AppendAllText(path, line + Environment.NewLine);
            }
        }
        catch { } // 기록을 못 남겨도 앱은 그대로 돈다
    }
}
