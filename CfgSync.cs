using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MapleBar;

/// <summary>설정 옮기기: 알림 설정과 알림 프리셋 목록을 이름·비밀번호로 서버에 저장하고 다른 PC에서 불러온다.
/// 세던 시각과 메이플 닉네임은 담지 않는다. 묶음은 JSON을 gzip으로 줄여 base64 글자로 보낸다.
/// 서버 주소는 '켰어요' 신호와 같은 docs/dev.json의 "presence"를 쓴다.
/// 저장·불러오기는 각각 30분에 한 번, 하루 3번까지 (서버도 같은 제한을 건다. 여기 기록은 버튼을 잠가 보여 주는 용도).</summary>
static class CfgSync
{
    public const int PerDay = 3;
    public static readonly TimeSpan Wait = TimeSpan.FromMinutes(30);
    public const int NameMax = 20, PwMin = 4, PwMax = 32;
    const int DataMax = 14 * 1024;      // 서버 한도와 같다
    const int MaxPresets = 30;
    const int UnzipMax = 512 * 1024;    // 불러온 묶음을 풀 때 이보다 크면 버린다

    const string DevUrl = "https://raw.githubusercontent.com/choimy0204/maplebar/main/docs/dev.json";
    static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(15) };
    static string? baseUrl;

    static readonly JsonSerializerOptions packOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, // null은 "없음"이라 빼도 같다 (기본값이 있는 칸은 빼지 않는다)
        MaxDepth = 16,
    };

    /// <summary>서버에 올리는 묶음. V는 묶음 모양 버전 (칸 수가 달라도 ApplyTo가 맞춰 넣는다)</summary>
    public sealed class Pack
    {
        public int V { get; set; } = 1;
        public AlertPreset? Alert { get; set; }
        public List<AlertPreset>? Presets { get; set; }
        public string? Sel { get; set; }
    }

    // ── 묶기 · 풀기 ──

    /// <summary>지금 설정을 묶는다. 너무 크면 null</summary>
    public static string? Build(Settings st)
    {
        var p = new Pack
        {
            Alert = AlertPreset.From(st, ""),
            Presets = st.AlertPresets.Take(MaxPresets).Select(Copy).ToList(),
            Sel = st.AlertPresetName,
        };
        p.Alert.MapleNick = null;
        foreach (var x in p.Presets) x.MapleNick = null;
        var json = JsonSerializer.SerializeToUtf8Bytes(p, packOpts);
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.SmallestSize, true)) gz.Write(json);
        var b64 = Convert.ToBase64String(ms.ToArray());
        return b64.Length > DataMax ? null : b64;
    }

    static AlertPreset Copy(AlertPreset p) =>
        JsonSerializer.Deserialize<AlertPreset>(JsonSerializer.Serialize(p)) ?? new();

    /// <summary>불러온 묶음을 풀고 값을 안전한 범위로 맞춘다. 깨졌으면 null</summary>
    public static Pack? Parse(string? b64)
    {
        try
        {
            if (string.IsNullOrEmpty(b64) || b64.Length > DataMax) return null;
            var raw = Convert.FromBase64String(b64);
            using var gz = new GZipStream(new MemoryStream(raw), CompressionMode.Decompress);
            using var outMs = new MemoryStream();
            var buf = new byte[8192];
            int n;
            while ((n = gz.Read(buf, 0, buf.Length)) > 0)
            {
                outMs.Write(buf, 0, n);
                if (outMs.Length > UnzipMax) return null;
            }
            var p = JsonSerializer.Deserialize<Pack>(outMs.ToArray(), packOpts);
            if (p?.Alert == null) return null;
            p.Alert = Clean(p.Alert, "");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<AlertPreset>();
            foreach (var x in (p.Presets ?? new()).Take(MaxPresets))
            {
                var name = Txt(x?.Name, NameMax)?.Trim();
                if (x == null || string.IsNullOrEmpty(name) || !names.Add(name)) continue;
                list.Add(Clean(x, name));
            }
            p.Presets = list;
            p.Sel = Txt(p.Sel, NameMax);
            return p;
        }
        catch (Exception ex)
        {
            Log.Write("설정 관리", "불러온 설정 풀기 실패", ex);
            return null;
        }
    }

    static int K(int v) => v is >= 0 and <= 255 ? v : 0;

    /// <summary>글자: 줄바꿈 같은 조절 문자를 빼고 길이를 자른다</summary>
    static string? Txt(string? s, int max = 30)
    {
        if (s == null) return null;
        var t = new string(s.Where(c => !char.IsControl(c)).ToArray());
        return t.Length > max ? t[..max] : t;
    }

    static List<T> Fix<T>(List<T>? src, int max, Action<T> fix) where T : new()
    {
        var l = (src ?? new()).Take(max).Select(x => x ?? new T()).ToList();
        foreach (var x in l) fix(x);
        return l;
    }

    /// <summary>불러온 값은 믿지 않는다: 칸 수·글자 길이·숫자 범위(초·음량·키 0~255)를 맞추고, 세던 시각과 닉네임은 버린다</summary>
    static AlertPreset Clean(AlertPreset p, string name)
    {
        p.Name = name;
        p.SoundVol = Math.Clamp(p.SoundVol, 0, 100);
        // 알림마다 고른 소리: 알림 수만큼만 두고, 없는 소리 번호는 기본 소리로 (AlertSound.Pick이 읽을 때도 거른다)
        p.SoundPicks = p.SoundPicks?.Take(AlertSound.KindNames.Length).Select(i => i >= 0 && i < AlertSound.Count ? i : -1).ToList();
        p.ComboKey = K(p.ComboKey); p.JanusKey = K(p.JanusKey); p.JanusMod = K(p.JanusMod);
        p.NpcKey = K(p.NpcKey); p.ErdaKey = K(p.ErdaKey);
        if (p.JanusCycleSec is int c) p.JanusCycleSec = Math.Clamp(c, 0, 3600);
        p.JanusRealertSec = Math.Clamp(p.JanusRealertSec, 5, 60);
        p.RuneMin = Math.Clamp(p.RuneMin, 1, 60);
        p.PendantBuff = Math.Clamp(p.PendantBuff, 0, JanusWatch.BuffSlots - 1);
        p.MapleNick = null;
        p.Buffs = Fix(p.Buffs, JanusWatch.BuffSlots, b =>
        {
            b.Key = K(b.Key); b.Min = Math.Clamp(b.Min, 1, 600); b.Name = Txt(b.Name);
            b.Start = null; b.KeyAt = null; b.EnterAt = null;
        });
        p.Dopes = Fix(p.Dopes, JanusWatch.DopeSlots, d =>
        {
            d.Key = K(d.Key); d.Sec = Math.Clamp(d.Sec, 1, 3600); d.DelaySec = Math.Clamp(d.DelaySec, 0, 60);
            d.Name = Txt(d.Name); d.Start = null;
        });
        p.Repeats = Fix(p.Repeats, JanusWatch.RepeatSlots, r =>
        {
            r.Key = K(r.Key); r.Sec = Math.Clamp(r.Sec, 1, 86400); r.UntilSec = Math.Clamp(r.UntilSec, 0, 86400);
            r.Name = Txt(r.Name); r.Start = null;
        });
        if (p.Onces != null)
            p.Onces = Fix(p.Onces, JanusWatch.OnceSlots, o =>
            {
                o.Key = K(o.Key); o.Sec = Math.Clamp(o.Sec, 1, 86400); o.Name = Txt(o.Name); o.Start = null;
            });
        // 알림을 띄울 모니터: 이 PC에 없는 모니터는 빼고, 남는 게 없으면 전체
        var have = Screen.AllScreens.Select(s => s.DeviceName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var scr = (p.AlertScreens ?? new()).Where(s => s == "ALL" || (s != null && have.Contains(s))).Distinct().Take(8).ToList();
        p.AlertScreens = scr.Count == 0 || scr.Contains("ALL") ? new() { "ALL" } : scr;
        return p;
    }

    /// <summary>불러온 묶음으로 지금 알림 설정과 프리셋 목록을 덮어쓴다. 메이플 닉네임은 이 PC 것을 그대로 둔다</summary>
    public static void Apply(Settings st, Pack p)
    {
        string? nick = st.MapleNick;
        p.Alert!.ApplyTo(st);
        st.MapleNick = nick;
        var list = p.Presets ?? new();
        foreach (var x in list) x.MapleNick = nick;
        st.AlertPresets = list;
        st.AlertPresetName = p.Sel != null && list.Any(x => string.Equals(x.Name, p.Sel, StringComparison.OrdinalIgnoreCase)) ? p.Sel : null;
        st.Save();
    }

    // ── 서버 ──

    public sealed record Quota(int SaveLeft, int SaveWait, int LoadLeft, int LoadWait);

    /// <summary>서버 답. Err: busy·wait·daily·taken·wrong·locked·tries·bad</summary>
    public sealed record Reply(bool Ok, string? Err, int Sec, string? Data, long Expires, Quota? Q, bool FullSave, bool FullLoad);

    /// <summary>서버에 보낸다. 연결이 안 되거나 서버가 아직 이 기능을 모르면 null</summary>
    public static async Task<Reply?> Call(string op, object body)
    {
        try
        {
            if (baseUrl == null)
            {
                using var doc = JsonDocument.Parse(await http.GetStringAsync(DevUrl));
                if (!doc.RootElement.TryGetProperty("presence", out var p) || p.ValueKind != JsonValueKind.String) return null;
                var url = p.GetString()?.TrimEnd('/');
                if (string.IsNullOrEmpty(url) || !url.StartsWith("https://", StringComparison.Ordinal)) return null;
                baseUrl = url;
            }
            using var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            using var res = await http.PostAsync(baseUrl + "/cfg/" + op, content);
            if (!res.IsSuccessStatusCode) return null;
            var text = await res.Content.ReadAsStringAsync();
            if (text.Length > 64 * 1024 || JsonNode.Parse(text) is not JsonObject o) return null;

            Quota? q = null;
            if (o["q"] is JsonObject qo)
                q = new Quota(Int(qo["save"]?["left"]), Int(qo["save"]?["wait"]), Int(qo["load"]?["left"]), Int(qo["load"]?["wait"]));
            return new Reply(
                o["ok"]?.GetValueKind() == JsonValueKind.True,
                o["err"]?.GetValueKind() == JsonValueKind.String ? o["err"]!.GetValue<string>() : null,
                Int(o["sec"]),
                o["data"]?.GetValueKind() == JsonValueKind.String ? o["data"]!.GetValue<string>() : null,
                o["expires"]?.GetValueKind() == JsonValueKind.Number ? (long)o["expires"]!.GetValue<double>() : 0,
                q,
                o["full"]?["save"]?.GetValueKind() == JsonValueKind.True,
                o["full"]?["load"]?.GetValueKind() == JsonValueKind.True);
        }
        catch (Exception ex)
        {
            Log.Write("설정 관리", "서버 연결 실패 (" + op + ")", ex);
            return null;
        }
    }

    static int Int(JsonNode? n) =>
        n?.GetValueKind() == JsonValueKind.Number ? (int)Math.Clamp(n.GetValue<double>(), 0, 1_000_000) : 0;

    /// <summary>서버 답을 쉬운 말로</summary>
    public static string Message(Reply r) => r.Err switch
    {
        "busy" => BusyText(r.FullSave, r.FullLoad) ?? "금일은 이용자 수가 많아 더 이상 조회나 저장이 불가능합니다.",
        "wait" => $"{Left(TimeSpan.FromSeconds(r.Sec))} 뒤에 다시 할 수 있어요.",
        "daily" => "오늘은 3번 다 썼어요. 내일 다시 해 주세요.",
        "taken" => "이미 있는 이름이에요.",
        "wrong" => "이름이나 비밀번호가 맞지 않아요.",
        "locked" => $"비밀번호를 여러 번 틀려 잠겼어요. {Left(TimeSpan.FromSeconds(r.Sec))} 뒤에 다시 해 주세요.",
        "tries" => "오늘은 비밀번호를 너무 많이 틀렸어요. 내일 다시 해 주세요.",
        _ => "보낸 내용이 맞지 않아요. 이름과 비밀번호를 확인해 주세요.",
    };

    /// <summary>서버 전체 하루 한도를 넘었을 때 문구 (안 넘었으면 null)</summary>
    public static string? BusyText(bool save, bool load) => (save, load) switch
    {
        (true, true) => "금일은 이용자 수가 많아 더 이상 조회나 저장이 불가능합니다.",
        (false, true) => "금일은 이용자 수가 많아 조회가 불가능합니다.",
        (true, false) => "금일은 이용자 수가 많아 저장이 불가능합니다.",
        _ => null,
    };

    /// <summary>남은 시간을 "12분", "40초"처럼</summary>
    public static string Left(TimeSpan t) =>
        t.TotalSeconds >= 60 ? $"{(int)Math.Ceiling(t.TotalMinutes)}분" : $"{Math.Max(1, (int)Math.Ceiling(t.TotalSeconds))}초";

    // ── 이 PC의 사용 기록 (버튼 잠금 표시용) ──

    sealed class Use { public string Day { get; set; } = ""; public int N { get; set; } public DateTime Last { get; set; } }
    sealed class Uses
    {
        public Use Save { get; set; } = new(); public Use Load { get; set; } = new();
        public Slot? Saved { get; set; }    // 이 PC에서 마지막으로 저장·연장한 설정
        public Slot? Loaded { get; set; }   // 지금 쓰는 설정을 불러온 곳
    }

    /// <summary>서버에 있는 설정 하나: 이름과 지워지는 때(유닉스 ms)</summary>
    public sealed class Slot { public string Name { get; set; } = ""; public long Expires { get; set; } }

    public static Slot? Saved => U.Saved;
    public static Slot? Loaded => U.Loaded;

    /// <summary>저장·연장(saved=true) 또는 불러오기 결과를 기억한다. 같은 이름이면 다른 쪽의 날짜도 맞춘다</summary>
    public static void Remember(bool saved, string name, long expires)
    {
        var s = new Slot { Name = name, Expires = expires };
        if (saved) U.Saved = s; else U.Loaded = s;
        var other = saved ? U.Loaded : U.Saved;
        if (other != null && string.Equals(other.Name, name, StringComparison.OrdinalIgnoreCase)) other.Expires = expires;
        WriteUses();
    }

    /// <summary>지워지기까지 남은 시간 (모르면 null)</summary>
    public static TimeSpan? Remain(Slot? s) =>
        s == null || s.Expires <= 0 ? null : DateTimeOffset.FromUnixTimeMilliseconds(s.Expires) - DateTimeOffset.UtcNow;

    static readonly string useFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MapleBar", "cfgsync.json");
    static Uses? uses;
    static Uses U => uses ??= ReadUses();

    static Uses ReadUses()
    {
        try { if (File.Exists(useFile)) return JsonSerializer.Deserialize<Uses>(File.ReadAllText(useFile)) ?? new(); }
        catch (Exception ex) { Log.Write("설정 관리", "사용 기록 읽기 실패", ex); }
        return new();
    }

    static void WriteUses()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(useFile)!);
            File.WriteAllText(useFile, JsonSerializer.Serialize(U));
        }
        catch (Exception ex) { Log.Write("설정 관리", "사용 기록 저장 실패", ex); }
    }

    /// <summary>한국 시간 오늘 날짜</summary>
    static string Today() => DateTime.UtcNow.AddHours(9).ToString("yyyy-MM-dd");

    /// <summary>횟수가 다시 채워지는 한국 시간 자정까지 남은 시간</summary>
    public static TimeSpan ToReset()
    {
        var kst = DateTime.UtcNow.AddHours(9);
        return kst.Date.AddDays(1) - kst;
    }

    /// <summary>5시간 12분 3초</summary>
    public static string Hms(TimeSpan t)
    {
        int s = Math.Max(0, (int)Math.Ceiling(t.TotalSeconds));
        int h = s / 3600, m = s / 60 % 60; s %= 60;
        return h > 0 ? $"{h}시간 {m}분 {s}초" : m > 0 ? $"{m}분 {s}초" : $"{s}초";
    }

    /// <summary>저장(true)·불러오기(false)를 지금 할 수 있는지: 남은 대기, 오늘 다 썼는지, 오늘 남은 횟수</summary>
    public static (TimeSpan wait, bool daily, int left) State(bool save)
    {
        var u = save ? U.Save : U.Load;
        int n = u.Day == Today() ? u.N : 0;
        var wait = u.Last + Wait - DateTime.UtcNow;
        if (wait < TimeSpan.Zero || wait > Wait) wait = wait < TimeSpan.Zero ? TimeSpan.Zero : Wait;
        return (wait, n >= PerDay, Math.Max(0, PerDay - n));
    }

    /// <summary>한 번 했다고 적는다</summary>
    public static void Mark(bool save)
    {
        var u = save ? U.Save : U.Load;
        var today = Today();
        if (u.Day != today) { u.Day = today; u.N = 0; }
        u.N++;
        u.Last = DateTime.UtcNow;
        WriteUses();
    }

    /// <summary>서버가 알려 준 남은 횟수·대기가 더 빡빡하면 그쪽에 맞춘다 (기록을 지워도 서버 기준으로 다시 잠긴다)</summary>
    public static void Sync(Quota? q)
    {
        if (q == null) return;
        bool changed = false;
        void One(Use u, int left, int wait)
        {
            var today = Today();
            int used = Math.Clamp(PerDay - left, 0, PerDay);
            int mine = u.Day == today ? u.N : 0;
            if (used > mine) { u.Day = today; u.N = used; changed = true; }
            var serverLast = DateTime.UtcNow + TimeSpan.FromSeconds(Math.Min(wait, (int)Wait.TotalSeconds)) - Wait;
            if (wait > 0 && serverLast > u.Last) { u.Last = serverLast; changed = true; }
        }
        One(U.Save, q.SaveLeft, q.SaveWait);
        One(U.Load, q.LoadLeft, q.LoadWait);
        if (changed) WriteUses();
    }
}
