using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Web.WebView2.Core;

namespace MapleBar;

/// <summary>
/// 경매장 검색 결과를 이 컴퓨터에 보관한다 (%LocalAppData%\MapleBar\auction-saves).
/// 화면이 'mb:auc:{op:...}'로 부르면 일을 하고 window.__mbAucSaves(...)로 답한다.
/// </summary>
static class AuctionSaves
{
    static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MapleBar", "auction-saves");
    static string IndexPath => Path.Combine(Dir, "index.json");
    static readonly Regex IdRx = new(@"^\d{8}-\d{6}-\d{3}$");
    const int MaxCount = 200;
    static readonly object gate = new();

    sealed class Entry
    {
        public string id { get; set; } = "";
        public string name { get; set; } = "";
        public int count { get; set; }
        public long at { get; set; }
    }

    public static async void Handle(Control owner, CoreWebView2 cw, string json)
    {
        string reply;
        try { reply = await Task.Run(() => Work(json)); }
        catch (Exception ex)
        {
            Log.Write("auction", "검색 결과 보관 실패", ex);
            reply = JsonSerializer.Serialize(new { op = "err" });
        }
        try { if (!owner.IsDisposed) await cw.ExecuteScriptAsync("window.__mbAucSaves&&window.__mbAucSaves(" + reply + ")"); }
        catch { }
    }

    static string Work(string json)
    {
        var req = JsonNode.Parse(json) as JsonObject;
        string op = req?["op"]?.GetValue<string>() ?? "";
        lock (gate)
        {
            switch (op)
            {
                case "list":
                    return JsonSerializer.Serialize(new { op = "list", list = Load() });
                case "save":
                {
                    if (req!["data"] is not JsonObject data) return Err();
                    string name = (req["name"]?.GetValue<string>() ?? "").Trim();
                    if (name.Length > 60) name = name[..60];
                    if (name == "") name = "검색 결과";
                    int count = data["items"] is JsonArray a ? a.Count : 0;
                    Directory.CreateDirectory(Dir);
                    var now = DateTime.Now;
                    string id;
                    do { id = now.ToString("yyyyMMdd-HHmmss-fff"); now = now.AddMilliseconds(1); }
                    while (File.Exists(Path.Combine(Dir, id + ".json")));
                    File.WriteAllText(Path.Combine(Dir, id + ".json"), data.ToJsonString());
                    var list = Load();
                    list.Insert(0, new Entry { id = id, name = name, count = count, at = DateTimeOffset.Now.ToUnixTimeMilliseconds() });
                    while (list.Count > MaxCount) { var old = list[^1]; list.RemoveAt(list.Count - 1); TryDelete(old.id); }
                    SaveIndex(list);
                    return JsonSerializer.Serialize(new { op = "saved", id, name, list });
                }
                case "load":
                {
                    string id = req!["id"]?.GetValue<string>() ?? "";
                    var e = IdRx.IsMatch(id) ? Load().Find(x => x.id == id) : null;
                    string path = Path.Combine(Dir, id + ".json");
                    if (e == null || !File.Exists(path)) return Err();
                    var data = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
                    if (data == null) return Err();
                    return JsonSerializer.Serialize(new { op = "load", e.id, e.name, e.at, data });
                }
                case "del":
                {
                    string id = req!["id"]?.GetValue<string>() ?? "";
                    var list = Load();
                    if (IdRx.IsMatch(id) && list.RemoveAll(x => x.id == id) > 0) { SaveIndex(list); TryDelete(id); }
                    return JsonSerializer.Serialize(new { op = "list", list });
                }
                default:
                    return Err();
            }
        }
    }

    static string Err() => JsonSerializer.Serialize(new { op = "err" });

    static List<Entry> Load()
    {
        try
        {
            if (!File.Exists(IndexPath)) return new();
            var list = JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(IndexPath)) ?? new();
            list.RemoveAll(x => x == null || !IdRx.IsMatch(x.id ?? ""));
            return list;
        }
        catch (Exception ex) { Log.Write("auction", "보관 목록 읽기 실패", ex); return new(); }
    }

    static void SaveIndex(List<Entry> list)
    {
        Directory.CreateDirectory(Dir);
        string tmp = IndexPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(list));
        File.Move(tmp, IndexPath, true);
    }

    static void TryDelete(string id)
    {
        try { if (IdRx.IsMatch(id)) File.Delete(Path.Combine(Dir, id + ".json")); }
        catch (Exception ex) { Log.Write("auction", "보관 결과 지우기 실패", ex); }
    }
}
