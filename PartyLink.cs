using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MapleBar;

/// <summary>
/// 파티 서버 연결 (바 화면 쪽). 파티 만들기·들어가기(HTTP)와 실시간 연결(WebSocket)을 맡는다.
/// 받은 것은 UI 스레드에서 PartyHub로 알리고, PartyHub.SendState를 받아 서버로 보낸다.
/// 서버 주소는 docs/dev.json의 "presence" (환경 변수 MAPLEBAR_PARTY_URL이 있으면 그것).
/// </summary>
static class PartyLink
{
    const string DevUrl = "https://raw.githubusercontent.com/choimy0204/maplebar/main/docs/dev.json";
    public const int PwMin = 4, PwMax = 20;

    /// <summary>한 번의 파티 연결. 나가거나 해체되면 버린다</summary>
    sealed class Session
    {
        public required string Room { get; init; }
        public required string Ticket { get; init; }
        public readonly CancellationTokenSource Cts = new();
        public readonly SemaphoreSlim SendLock = new(1, 1);
        public ClientWebSocket? Ws;
        public bool Stop; // 나갔거나 해체됐다: 다시 붙지 않는다
        public bool Leaving; // 해체를 보내는 중: 서버가 보내는 bye는 무시한다
    }

    static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(15) };
    static string? baseUrl;
    static Form? ui;
    static Session? session;
    static bool busy; // 만들기·들어가기가 진행 중
    const int SendWaitMs = 3000; // 나가기·해체·모집 끝을 보낼 때 연결이 열리기를 기다리는 시간

    /// <summary>모집 중이면 입장 코드, 아니면 null</summary>
    public static string? Code { get; private set; }

    /// <summary>파티에 들어와 있다 (잠깐 끊겨 다시 붙는 중이어도 true)</summary>
    public static bool Active => session != null;

    /// <summary>내가 방장이다</summary>
    public static bool IsOwner => PartyHub.MyId is string me && PartyHub.Members.Any(m => m.Id == me && m.Owner);

    /// <summary>인원·코드가 바뀌었다 (UI 스레드)</summary>
    public static event Action? Changed;

    /// <summary>파티가 끝났다 (UI 스레드). "disband" = 방장이 해체, "lost" = 연결이 안 돼 포기</summary>
    public static event Action<string>? Ended;

    /// <summary>바가 켜질 때 한 번</summary>
    public static void Init(Form owner)
    {
        ui = owner;
        PartyHub.SendState -= OnSendState;
        PartyHub.SendState += OnSendState;
    }

    /// <summary>바를 끌 때: 연결만 닫는다</summary>
    public static void Shutdown()
    {
        PartyHub.SendState -= OnSendState;
        var s = session;
        session = null;
        if (s == null) return;
        s.Stop = true;
        // 바를 끄면 파티에서 나간 것으로 본다: 열려 있으면 leave를 짧게(최대 1초) 보낸다
        try
        {
            if (s.Ws is { State: WebSocketState.Open } ws)
            {
                using var timeout = new CancellationTokenSource(1000);
                Task.Run(async () =>
                {
                    await s.SendLock.WaitAsync(timeout.Token);
                    try { await ws.SendAsync(Encoding.UTF8.GetBytes("{\"t\":\"leave\"}"), WebSocketMessageType.Text, true, timeout.Token); }
                    finally { s.SendLock.Release(); }
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", timeout.Token);
                }).Wait(1000);
            }
        }
        catch (Exception ex) { Log.Write("파티", "바 끌 때 나가기 보내기 실패", ex); }
        finally
        {
            try { s.Cts.Cancel(); s.Ws?.Abort(); }
            catch (Exception ex) { Log.Write("파티", "연결 닫기 실패", ex); }
        }
    }

    // ── 만들기·들어가기 ──

    /// <summary>파티를 만든다. 성공하면 null, 실패하면 보여 줄 문구</summary>
    public static async Task<string?> CreateAsync(string pw)
    {
        if (session != null) return "이미 파티에 있어요";
        if (busy) return Busy;
        busy = true;
        try
        {
            var o = await Post("/party/create", new JsonObject { ["pw"] = pw });
            if (o == null) return ConnectFail;
            if (!IsOk(o)) return ErrText(Str(o["err"]));
            string? room = Str(o["room"]), ticket = Str(o["ticket"]);
            if (room == null || ticket == null) return ConnectFail;
            Code = Str(o["code"]);
            Begin(room, ticket);
            return null;
        }
        finally { busy = false; }
    }

    /// <summary>입장 코드와 비밀번호로 들어간다. 성공하면 null, 실패하면 보여 줄 문구</summary>
    public static async Task<string?> JoinAsync(string code, string pw)
    {
        if (session != null) return "이미 파티에 있어요";
        if (busy) return Busy;
        busy = true;
        try
        {
            code = new string(code.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
            var o = await Post("/party/join", new JsonObject { ["code"] = code, ["pw"] = pw });
            if (o == null) return ConnectFail;
            if (!IsOk(o)) return ErrText(Str(o["err"]));
            string? room = Str(o["room"]), ticket = Str(o["ticket"]);
            if (room == null || ticket == null) return ConnectFail;
            Code = null;
            Begin(room, ticket);
            return null;
        }
        finally { busy = false; }
    }

    const string ConnectFail = "연결이 안 돼요";
    const string Busy = "처리 중이에요";

    static string ErrText(string? err) => err switch
    {
        "wrong" => "비밀번호가 달라요",
        "gone" => "없는 코드이거나 모집이 끝났어요",
        "full" => "자리가 다 찼어요 (6명)",
        "tries" or "busy" => "잠시 뒤에 다시 해 주세요",
        "bad" => "코드와 비밀번호를 다시 확인해 주세요",
        _ => ConnectFail,
    };

    static async Task<string?> BaseUrl()
    {
        if (baseUrl != null) return baseUrl;
        var env = Environment.GetEnvironmentVariable("MAPLEBAR_PARTY_URL")?.Trim().TrimEnd('/');
        if (!string.IsNullOrEmpty(env)) return baseUrl = env;
        using var doc = JsonDocument.Parse(await http.GetStringAsync(DevUrl));
        if (!doc.RootElement.TryGetProperty("presence", out var p) || p.ValueKind != JsonValueKind.String) return null;
        var url = p.GetString()?.TrimEnd('/');
        if (string.IsNullOrEmpty(url) || !url.StartsWith("https://", StringComparison.Ordinal)) return null;
        return baseUrl = url;
    }

    /// <summary>서버에 보내고 답(JSON 객체)을 받는다. 연결이 안 되면 null</summary>
    static async Task<JsonObject?> Post(string path, JsonObject body)
    {
        try
        {
            var url = await BaseUrl();
            if (url == null) return null;
            using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            using var res = await http.PostAsync(url + path, content);
            var text = await res.Content.ReadAsStringAsync();
            if (text.Length > 64 * 1024) return null;
            return JsonNode.Parse(text) as JsonObject;
        }
        catch (Exception ex)
        {
            Log.Write("파티", "서버 연결 실패 (" + path + ")", ex);
            return null;
        }
    }

    static bool IsOk(JsonObject o) => o["ok"] is JsonValue v && v.TryGetValue<bool>(out var ok) && ok;

    static string? Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    // ── 나가기·모집 끝·해체 ──

    /// <summary>나간다. 화면은 바로 파티 밖으로 돌리고, 서버에는 연결이 열리면(최대 3초 기다림) 알린다</summary>
    public static void Leave()
    {
        var s = session;
        if (s == null) return;
        LeaveScreen();
        _ = Task.Run(async () =>
        {
            await SendWaitAsync(s, "{\"t\":\"leave\"}");
            await CloseSocket(s);
        });
    }

    /// <summary>해체한다 (방장만). 모두 내보낸다. 서버에 못 보내면 연결을 잃은 것으로 끝낸다</summary>
    public static void Disband()
    {
        var s = session;
        if (s == null) return;
        s.Leaving = true;
        _ = Task.Run(async () =>
        {
            if (!await SendWaitAsync(s, "{\"t\":\"disband\"}"))
            {
                Post(() => { s.Leaving = false; if (session == s) End(s, "lost"); });
                return;
            }
            Post(() => { if (session == s) LeaveScreen(); });
            await CloseSocket(s);
        });
    }

    /// <summary>모집을 마친다 (방장만). 코드가 사라지고 인원은 그대로. 못 보내면 화면만 다시 그린다</summary>
    public static void CloseRecruit()
    {
        var s = session;
        if (s == null) return;
        _ = Task.Run(async () =>
        {
            if (!await SendWaitAsync(s, "{\"t\":\"close\"}")) Post(() => Changed?.Invoke());
        });
    }

    /// <summary>내 직업 줄임말을 서버에 다시 알린다</summary>
    public static void SendHello()
    {
        var s = session;
        if (s != null) _ = SendAsync(s, HelloJson());
    }

    static string HelloJson() =>
        new JsonObject { ["t"] = "hello", ["job"] = PartyHub.St?.Party?.Job ?? "" }.ToJsonString();

    /// <summary>화면은 파티 밖으로 돌린다 (연결은 그대로 둔다)</summary>
    static void LeaveScreen()
    {
        session = null;
        Code = null;
        PartyHub.RaiseLeft();
        Changed?.Invoke();
    }

    /// <summary>연결을 닫고 다시 붙지 않게 한다</summary>
    static async Task CloseSocket(Session s)
    {
        try
        {
            s.Stop = true;
            if (s.Ws is { State: WebSocketState.Open } ws)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", timeout.Token);
            }
        }
        catch (Exception ex) { Log.Write("파티", "연결 닫기 실패", ex); }
        finally { try { s.Cts.Cancel(); s.Ws?.Abort(); } catch { } }
    }

    /// <summary>연결이 열리기를 잠깐(최대 3초) 기다렸다 보낸다. 못 보내면 false</summary>
    static async Task<bool> SendWaitAsync(Session s, string text)
    {
        var until = DateTime.UtcNow.AddMilliseconds(SendWaitMs);
        while (s.Ws is not { State: WebSocketState.Open })
        {
            if (DateTime.UtcNow >= until) return false;
            await Task.Delay(100);
        }
        return await SendAsync(s, text);
    }

    // ── 실시간 연결 ──

    static void Begin(string room, string ticket)
    {
        var s = new Session { Room = room, Ticket = ticket };
        session = s;
        _ = Task.Run(() => RunAsync(s));
        Changed?.Invoke();
    }

    /// <summary>화면 스레드에서 실행한다</summary>
    static void Post(Action a)
    {
        var f = ui;
        if (f == null || f.IsDisposed || !f.IsHandleCreated) return;
        try { f.BeginInvoke(a); }
        catch (InvalidOperationException) { } // 바가 닫히는 중
    }

    /// <summary>붙어서 받는다. 끊기면 2·5·10초 뒤 같은 방·표로 다시 붙고, 그래도 안 되면 포기한다</summary>
    static async Task RunAsync(Session s)
    {
        int[] waits = { 2, 5, 10 };
        int failed = 0;
        while (!s.Stop)
        {
            CancellationTokenSource? conn = null;
            try
            {
                var url = await BaseUrl();
                if (url == null) throw new InvalidOperationException("서버 주소를 모름");
                var wsUrl = (url.StartsWith("https://", StringComparison.Ordinal) ? "wss://" + url[8..] : "ws://" + url[(url.IndexOf("://", StringComparison.Ordinal) + 3)..])
                    + "/party/ws?room=" + Uri.EscapeDataString(s.Room) + "&ticket=" + Uri.EscapeDataString(s.Ticket);
                using var ws = new ClientWebSocket();
                await ws.ConnectAsync(new Uri(wsUrl), s.Cts.Token);
                s.Ws = ws;
                failed = 0;
                conn = CancellationTokenSource.CreateLinkedTokenSource(s.Cts.Token);
                _ = PingLoop(s, ws, conn.Token);
                await SendAsync(s, HelloJson());
                Post(() => SendCurrentState(s));
                await ReceiveLoop(s, ws);
            }
            catch (Exception ex) when (!s.Stop)
            {
                Log.Write("파티", "연결 끊김", ex);
            }
            catch (Exception) { break; }
            finally
            {
                conn?.Cancel();
                s.Ws = null;
            }
            if (s.Stop) break;
            if (failed >= waits.Length)
            {
                Post(() => { if (session == s) End(s, "lost"); });
                return;
            }
            try { await Task.Delay(waits[failed++] * 1000, s.Cts.Token); }
            catch (OperationCanceledException) { break; }
        }
    }

    static async Task PingLoop(Session s, ClientWebSocket ws, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                await Task.Delay(25000, token);
                await SendAsync(s, "{\"t\":\"ping\"}");
            }
        }
        catch (OperationCanceledException) { }
    }

    static async Task ReceiveLoop(Session s, ClientWebSocket ws)
    {
        var buf = new byte[8192];
        using var ms = new MemoryStream();
        while (ws.State == WebSocketState.Open && !s.Stop)
        {
            ms.SetLength(0);
            WebSocketReceiveResult r;
            do
            {
                r = await ws.ReceiveAsync(buf, s.Cts.Token);
                if (r.MessageType == WebSocketMessageType.Close) return;
                ms.Write(buf, 0, r.Count);
                if (ms.Length > 64 * 1024) return;
            } while (!r.EndOfMessage);
            var text = Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
            Post(() => Handle(s, text));
        }
    }

    /// <summary>보냈으면 true</summary>
    static async Task<bool> SendAsync(Session s, string text)
    {
        var ws = s.Ws;
        if (ws == null || ws.State != WebSocketState.Open) return false;
        try
        {
            await s.SendLock.WaitAsync();
            try { await ws.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, s.Cts.Token); }
            finally { s.SendLock.Release(); }
            return true;
        }
        catch (Exception ex)
        {
            Log.Write("파티", "보내기 실패", ex);
            return false;
        }
    }

    /// <summary>붙은 직후 내 지금 상태를 한 번 보낸다</summary>
    static void SendCurrentState(Session s)
    {
        if (session != s || PartyHub.CurrentState == null) return;
        try { Send(s, PartyHub.CurrentState(), null); }
        catch (Exception ex) { Log.Write("파티", "내 상태 만들기 실패", ex); }
    }

    static void OnSendState(JsonObject state, string? use)
    {
        var s = session;
        if (s != null) Send(s, state, use);
    }

    static void Send(Session s, JsonObject state, string? use)
    {
        // 상태 객체가 다른 곳에 붙어 있어도 되도록 복사해서 싣는다
        if (JsonNode.Parse(state.ToJsonString()) is not JsonObject copy) return;
        var msg = new JsonObject { ["t"] = "state", ["s"] = copy, ["use"] = use };
        _ = SendAsync(s, msg.ToJsonString());
    }

    // ── 받은 메시지 (UI 스레드) ──

    static void Handle(Session s, string text)
    {
        if (session != s) return;
        JsonObject? o;
        try { o = JsonNode.Parse(text) as JsonObject; }
        catch (JsonException) { return; }
        if (o == null) return;
        switch (Str(o["t"]))
        {
            case "welcome":
                if (Str(o["id"]) is string id) PartyHub.RaiseJoined(id);
                break;
            case "members":
                HandleMembers(o);
                break;
            case "state":
                HandleState(o);
                break;
            case "bye":
                if (s.Leaving) break; // 내가 해체하는 중
                End(s, "disband");
                break;
        }
    }

    static void HandleMembers(JsonObject o)
    {
        var list = new List<PartyMember>();
        if (o["list"] is JsonArray arr)
        {
            foreach (var n in arr)
            {
                if (n is not JsonObject m || Str(m["id"]) is not string mid) continue;
                bool owner = m["owner"] is JsonValue ov && ov.TryGetValue<bool>(out var b) && b;
                list.Add(new PartyMember(mid, Str(m["job"]) ?? "", owner));
            }
        }
        Code = Str(o["code"]);
        PartyHub.RaiseMembers(list);
        Changed?.Invoke();
    }

    static void HandleState(JsonObject o)
    {
        if (Str(o["id"]) is not string id) return;
        if (o["s"] == null || JsonNode.Parse(o["s"]!.ToJsonString()) is not JsonObject s) return;
        string? use = Str(o["use"]);
        double age = o["age"] is JsonValue av && av.TryGetValue<double>(out var a) ? a : 0;
        foreach (var key in new[] { "origin", "burst", "semi", "buff" })
        {
            if (s[key] is JsonValue v && v.TryGetValue<double>(out var left))
                s[key] = Math.Max(0, left - age / 1000.0);
        }
        PartyHub.RaiseState(id, s, use);
    }

    /// <summary>파티가 끝났다 (해체당했거나 연결을 포기)</summary>
    static void End(Session s, string why)
    {
        s.Stop = true;
        session = null;
        Code = null;
        try { s.Cts.Cancel(); s.Ws?.Abort(); }
        catch (Exception ex) { Log.Write("파티", "연결 닫기 실패", ex); }
        PartyHub.RaiseLeft();
        Changed?.Invoke();
        Ended?.Invoke(why);
    }
}
