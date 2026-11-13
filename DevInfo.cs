using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using static MapleBar.Native;

namespace MapleBar;

/// <summary>개발자 정보 창. 왼쪽은 개발자 캐릭터(코디·닉네임·서버·서버 마크)와 문의 방법, 오른쪽은 공지사항.
/// 닉네임·서버·공지는 깃허브의 docs/dev.json에서 받고, 그 닉네임으로 maple.gg/u/닉네임을 읽어 코디와 서버 마크를 가져온다.
/// 보이지 않는 소리 끈 웹 화면으로 읽기만 하고, 한 번 불러온 캐릭터는 바를 끌 때까지 다시 쓴다.</summary>
sealed class DevInfo : Form
{
    const string DevUrl = "https://raw.githubusercontent.com/choimy0204/maplebar/main/docs/dev.json";
    // raw 주소는 깃허브가 5분 동안 예전 파일을 돌려줘서, 바로 바뀌는 API 주소에서 먼저 받는다 (실패하면 raw)
    const string DevApiUrl = "https://api.github.com/repos/choimy0204/maplebar/contents/docs/dev.json?ref=main";

    static async Task<string> GetDevJson()
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, DevApiUrl);
            req.Headers.TryAddWithoutValidation("User-Agent", "MapleBar");
            req.Headers.TryAddWithoutValidation("Accept", "application/vnd.github.raw");
            using var res = await http.SendAsync(req);
            if (res.IsSuccessStatusCode) return await res.Content.ReadAsStringAsync();
        }
        catch (Exception ex) { Log.Write("개발자 정보", "깃허브 API로 받기 실패", ex); }
        return await http.GetStringAsync(DevUrl + "?t=" + DateTime.UtcNow.Ticks);
    }
    // dev.json을 못 받았을 때 쓰는 값
    const string DefaultNick = "블신";
    const string DefaultWorld = "엘리시움";

    record Notice(string Date, string Title, string Body);
    record Info(string Name, string World, Image? Avatar, Image? Icon);
    static readonly Dictionary<string, Info> cache = new();
    static DevInfo? open;
    static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public static void Open(IWin32Window owner)
    {
        if (open != null && !open.IsDisposed) { open.Activate(); return; }
        open = new DevInfo();
        open.Show(owner);
    }

    readonly float sc;
    readonly PictureBox avatar, icon;
    readonly Label name, world, state, ask;
    readonly Panel notes;
    readonly Pen border = new(UI.Line);
    bool closed;

    int S(float v) => UI.Scale(v, sc);
    Font F(float size, FontStyle style = FontStyle.Regular) => new("맑은 고딕", size * UI.Z, style);
    static readonly Color Gray = Color.FromArgb(130, 130, 136);

    DevInfo()
    {
        sc = DeviceDpi / 96f * UI.Z;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.White;
        AutoScaleMode = AutoScaleMode.None;

        int leftW = S(264), mid = S(18) + leftW + S(18), rightX = mid + S(18), rightW = S(370); // 공지 제목이 한 줄에 들어가게 넉넉히
        ClientSize = new Size(rightX + rightW + S(18), S(290));

        var bar = new Panel { BackColor = UI.Accent, Location = Point.Empty, Size = new Size(ClientSize.Width, S(5)) };
        var x = new Label
        {
            Text = "✕", Font = F(10), ForeColor = Gray, AutoSize = false, Size = new Size(S(28), S(28)),
            TextAlign = ContentAlignment.MiddleCenter, Location = new Point(ClientSize.Width - S(40), S(14)), Cursor = Cursors.Hand,
        };
        x.Click += (_, _) => Close();

        // ── 왼쪽: 개발자 정보
        var title = new Label { Text = "개발자 정보", Font = F(11.5f, FontStyle.Bold), ForeColor = UI.Text, AutoSize = true, Location = new Point(S(18), S(18)) };
        // 코디: 배경 없이 캐릭터만 잘라서 크게 보여 준다
        avatar = new PictureBox { Location = new Point(S(18), S(50)), Size = new Size(S(116), S(160)), SizeMode = PictureBoxSizeMode.CenterImage, BackColor = Color.White };
        var card = avatar;

        int tx = card.Right + S(14);
        // 글자 높이에 딱 맞추면 '블'의 윗부분이 잘려 '믈'처럼 보여서, 칸을 넉넉히 잡고 가운데에 둔다
        name = new Label
        {
            Text = DefaultNick, Font = F(13, FontStyle.Bold), ForeColor = UI.Text, AutoSize = false, UseMnemonic = false,
            Location = new Point(tx - S(3), S(80)), Size = new Size(S(18) + leftW - tx + S(3), S(40)), TextAlign = ContentAlignment.MiddleLeft,
        };
        icon = new PictureBox { Size = new Size(S(16), S(16)), SizeMode = PictureBoxSizeMode.Zoom, Location = new Point(tx + S(2), S(128)), Visible = false };
        world = new Label { Text = DefaultWorld, Font = F(10), ForeColor = UI.Text, AutoSize = true, Location = new Point(tx, S(125)) };
        state = new Label { Text = "불러오는 중…", Font = F(8), ForeColor = Gray, AutoSize = true, Location = new Point(tx, S(156)) };

        ask = new Label
        {
            Font = F(9.5f), ForeColor = UI.Text, BackColor = Color.FromArgb(247, 247, 249), AutoSize = false,
            Location = new Point(S(18), card.Bottom + S(12)), Size = new Size(leftW, S(56)),
            Padding = new Padding(S(12), 0, S(12), 0), TextAlign = ContentAlignment.MiddleLeft,
        };
        ask.HandleCreated += (_, _) => UI.RoundCorners(ask.Handle, small: true);
        SetAsk(DefaultNick, DefaultWorld);

        // ── 가운데 줄
        var line = new Panel { BackColor = UI.Line, Location = new Point(mid, S(18)), Size = new Size(Math.Max(1, S(1)), ClientSize.Height - S(36)) };

        // ── 오른쪽: 공지사항
        var ntitle = new Label { Text = "공지사항", Font = F(11.5f, FontStyle.Bold), ForeColor = UI.Text, AutoSize = true, Location = new Point(rightX, S(18)) };
        notes = new Panel { Location = new Point(rightX, S(54)), Size = new Size(rightW, ClientSize.Height - S(54) - S(18)), AutoScroll = true };
        NoteText("공지사항을 불러오는 중…");

        // 제목 옆에 지금 쓰는 프로그램 버전을 작게 적는다
        var ver = typeof(DevInfo).Assembly.GetName().Version;
        var verLabel = new Label
        {
            Text = ver == null ? "" : $"v{ver.Major}.{ver.Minor}.{Math.Max(0, ver.Build)}",
            Font = F(8.5f), ForeColor = Gray, AutoSize = true,
        };
        verLabel.Location = new Point(title.Left + title.PreferredWidth + S(6), title.Top + title.PreferredHeight - verLabel.PreferredHeight - S(1));

        Controls.AddRange(new Control[] { x, bar, title, verLabel, card, name, icon, world, state, ask, line, ntitle, notes });

        // 빈 곳을 끌어서 옮길 수 있게
        foreach (Control c in new Control[] { this, title, ntitle, bar })
            c.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero); } };
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };

        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 3);
        Paint += (_, e) => e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        HandleCreated += (_, _) => UI.RoundCorners(Handle);
        Shown += async (_, _) => await Start();
        FormClosed += (_, _) => { closed = true; if (open == this) open = null; };
    }

    void SetAsk(string nick, string world) =>
        ask.Text = $"기능 문의는 {world} '{nick}'에게\n귓속말이나 쪽지로 보내 주세요.";

    // ── 미리 받아 두기: 바가 켜질 때 한 번 받고, 지난번 받은 것은 디스크에 저장해 둔다 ──
    static string? sNick, sWorld;
    static List<Notice>? sNotes;
    static Task? warming;
    static string CacheDir => Path.Combine(Path.GetDirectoryName(Settings.LogPath)!, "dev");

    /// <summary>바가 켜질 때 부른다. 저장해 둔 것을 먼저 읽고, 뒤에서 새로 받는다</summary>
    public static void Warm()
    {
        try { LoadDisk(); } catch (Exception ex) { Log.Write("개발자 정보", "저장한 정보 읽기 실패", ex); }
        warming ??= Prefetch();
    }

    static async Task Prefetch()
    {
        string guess = sNick ?? DefaultNick;
        var fastT = FetchFast(guess); // dev.json을 기다리지 않고 같이 받는다
        var (nick, wname, list) = await GetDev();
        sNick = nick; sWorld = wname;
        if (list != null) sNotes = list;
        var info = nick == guess ? await fastT : await FetchFast(nick);
        if (info != null) cache[nick] = info;
        try { SaveDisk(info); } catch (Exception ex) { Log.Write("개발자 정보", "정보 저장 실패", ex); }
    }

    static void LoadDisk()
    {
        string f = Path.Combine(CacheDir, "info.json");
        if (!File.Exists(f)) return;
        using var doc = JsonDocument.Parse(File.ReadAllText(f));
        var r = doc.RootElement;
        string P(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
        sNick = P(r, "nick") is { Length: > 0 } n ? n : null;
        sWorld = P(r, "world") is { Length: > 0 } w ? w : null;
        if (r.TryGetProperty("notices", out var arr) && arr.ValueKind == JsonValueKind.Array)
            sNotes = arr.EnumerateArray().Select(it => new Notice(P(it, "date"), P(it, "title"), P(it, "body"))).ToList();
        Image? Img(string name)
        {
            string p = Path.Combine(CacheDir, name);
            return File.Exists(p) ? new Bitmap(new MemoryStream(File.ReadAllBytes(p))) : null;
        }
        if (sNick != null && P(r, "name") is { Length: > 0 } cname && Img("avatar.png") is { } av)
            cache[sNick] = new Info(cname, P(r, "cworld") is { Length: > 0 } cw ? cw : sWorld ?? DefaultWorld, av, Img("icon.png"));
    }

    static void SaveDisk(Info? info)
    {
        Directory.CreateDirectory(CacheDir);
        if (info?.Avatar != null) info.Avatar.Save(Path.Combine(CacheDir, "avatar.png"), System.Drawing.Imaging.ImageFormat.Png);
        if (info?.Icon != null) info.Icon.Save(Path.Combine(CacheDir, "icon.png"), System.Drawing.Imaging.ImageFormat.Png);
        var keep = sNick != null && cache.TryGetValue(sNick, out var c) ? c : null;
        File.WriteAllText(Path.Combine(CacheDir, "info.json"), JsonSerializer.Serialize(new
        {
            nick = sNick, world = sWorld, name = keep?.Name, cworld = keep?.World,
            notices = sNotes?.Select(n => new { date = n.Date, title = n.Title, body = n.Body }),
        }));
    }

    /// <summary>지금 가진 정보로 창을 채운다 (기다리지 않음)</summary>
    void Apply()
    {
        string nick = sNick ?? DefaultNick, wname = sWorld ?? DefaultWorld;
        name.Text = nick; world.Text = wname; SetAsk(nick, wname);
        if (sNotes != null) ShowNotices(sNotes);
        if (!cache.TryGetValue(nick, out var info)) return;
        name.Text = info.Name; world.Text = info.World; SetAsk(info.Name, info.World);
        var old = avatar.Image;
        avatar.Image = info.Avatar == null ? null : Fit(info.Avatar, avatar.Width, avatar.Height, 1.4);
        old?.Dispose();
        icon.Image = info.Icon;
        icon.Visible = info.Icon != null;
        world.Left = icon.Visible ? icon.Right + S(5) : icon.Left - S(2);
        state.Visible = false;
    }

    async Task Start()
    {
        // 저장해 둔 것을 바로 보여 주고, 새로 받은 게 오면 다시 채운다
        Apply();
        if (warming == null || warming.IsCompleted) warming = Prefetch();
        await warming;
        if (closed) return;
        Apply();
        if (sNotes == null) ShowNotices(null);
        string nick = sNick ?? DefaultNick;
        if (cache.ContainsKey(nick)) return;
        // 빠른 길이 막혔을 때만 느린 길(보이지 않는 웹 화면)로 읽는다
        var info = await Fetch(nick);
        if (closed) return;
        if (info == null) { state.Text = "maple.gg를 못 불러왔어요"; return; }
        Apply();
    }

    static async Task<(string nick, string world, List<Notice>? list)> GetDev()
    {
        // 깃허브에서 닉네임·서버·공지를 받는다
        string nick = DefaultNick, wname = DefaultWorld;
        List<Notice>? list = null;
        try
        {
            var json = await GetDevJson();
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.TryGetProperty("nick", out var n) && n.GetString() is { Length: > 0 } ns) nick = ns.Trim();
            if (r.TryGetProperty("world", out var w) && w.GetString() is { Length: > 0 } ws) wname = ws.Trim();
            list = new();
            if (r.TryGetProperty("notices", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var it in arr.EnumerateArray())
                {
                    string P(string k) => it.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()!.Trim() : "";
                    if (P("title").Length > 0 || P("body").Length > 0) list.Add(new Notice(P("date"), P("title"), P("body")));
                }
        }
        catch (Exception ex) { Log.Write("개발자 정보", "dev.json 받기 실패", ex); }
        return (nick, wname, list);
    }

    void NoteText(string text)
    {
        notes.Controls.Clear();
        notes.Controls.Add(new Label { Text = text, Font = F(9), ForeColor = Gray, AutoSize = true, Location = Point.Empty });
    }

    void ShowNotices(List<Notice>? list)
    {
        if (list == null) { NoteText("공지사항을 못 불러왔어요"); return; }
        if (list.Count == 0) { NoteText("아직 공지사항이 없어요"); return; }
        notes.SuspendLayout();
        notes.Controls.Clear();
        int w = notes.Width - SystemInformation.VerticalScrollBarWidth - S(4), y = 0;
        for (int i = 0; i < list.Count; i++)
        {
            var nt = list[i];
            if (i > 0)
            {
                notes.Controls.Add(new Panel { BackColor = UI.Line, Location = new Point(0, y), Size = new Size(w, 1) });
                y += S(12);
            }
            if (nt.Date.Length > 0)
            {
                var d = new Label { Text = nt.Date, Font = F(8), ForeColor = UI.Accent, AutoSize = true, Location = new Point(0, y) };
                notes.Controls.Add(d); y += d.PreferredHeight + S(2);
            }
            if (nt.Title.Length > 0)
            {
                var t = new Label { Text = nt.Title, Font = F(10, FontStyle.Bold), ForeColor = UI.Text, AutoSize = true, MaximumSize = new Size(w, 0), Location = new Point(0, y) };
                notes.Controls.Add(t); y += t.PreferredSize.Height + S(4);
            }
            if (nt.Body.Length > 0)
            {
                var b = new Label { Text = nt.Body, Font = F(9), ForeColor = Color.FromArgb(80, 80, 86), AutoSize = true, MaximumSize = new Size(w, 0), Location = new Point(0, y) };
                notes.Controls.Add(b); y += b.PreferredSize.Height;
            }
            y += S(12);
        }
        notes.ResumeLayout();
    }

    // 코디 그림이 그려질 때까지 기다렸다가 닉네임·서버·그림 주소를 읽는다
    const string ReadJs = @"(()=>{
const b=document.body; if(!b) return '';
const t=document.title+' '+(b.innerText||'').slice(0,400);
if(/Just a moment|사람인지 확인|Verify you are human/i.test(t)) return 'blocked';
if(/검색 ?결과가 없|존재하지 않는 캐릭터|캐릭터를 찾을 수 없/.test(b.innerText||'')) return 'blocked';
const a=[...document.querySelectorAll('img.character-image')].find(i=>/maplestory|nexon/.test(i.currentSrc||i.src));
const w=document.querySelector('.world');
if(!a||!w||!a.complete) return '';
const wi=w.querySelector('img');
return JSON.stringify({name:a.alt||'', avatar:a.currentSrc||a.src, world:(w.innerText||'').trim(), icon:wi?(wi.currentSrc||wi.src):''});
})()";

    async Task<Info?> Fetch(string nick)
    {
        // 빠른 길(FetchFast)이 막혔을 때만 온다: 보이지 않는 웹 화면으로 읽는다(느림)
        Form? host = null;
        CoreWebView2Controller? ctl = null;
        try
        {
            host = new Form { ShowInTaskbar = false, FormBorderStyle = FormBorderStyle.None, Size = new Size(1280, 900) };
            var env = await WebWindow.Env;
            ctl = await env.CreateCoreWebView2ControllerAsync(host.Handle);
            ctl.Bounds = new Rectangle(0, 0, 1280, 900);
            ctl.IsVisible = true; // 받침 창은 띄우지 않아서 화면에는 보이지 않는다
            var cw = ctl.CoreWebView2;
            cw.IsMuted = true;    // 소리는 내지 않는다
            cw.NewWindowRequested += (_, e) => e.Handled = true;
            cw.Navigate("https://maple.gg/u/" + Uri.EscapeDataString(nick));

            string? json = null;
            for (int i = 0; i < 25 && !closed; i++)
            {
                await Task.Delay(800);
                var raw = JsonSerializer.Deserialize<string>(await cw.ExecuteScriptAsync(ReadJs)) ?? "";
                if (raw == "blocked") break;
                if (raw.Length > 0) { json = raw; break; }
            }
            if (json == null) return null;

            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            string Str(string k) => r.GetProperty(k).GetString() ?? "";
            var info = new Info(
                Str("name") is { Length: > 0 } n ? n : nick,
                Str("world") is { Length: > 0 } w ? w : DefaultWorld,
                await Pic(Str("avatar")),
                await Pic(Str("icon")));
            if (info.Avatar != null) cache[nick] = info; // 그림까지 받았을 때만 다음에 다시 쓴다
            return info;
        }
        catch (Exception ex)
        {
            Log.Write("개발자 정보", "maple.gg 불러오기 실패", ex);
            return null;
        }
        finally
        {
            ctl?.Close();
            host?.Dispose();
        }
    }

    /// <summary>코디 그림의 빈(투명) 테두리를 잘라 내고, 칸에 맞게 키운다. 정수 배로 키울 수 있으면 픽셀을 또렷하게 둔다</summary>
    static Bitmap Fit(Image src, int boxW, int boxH, double size)
    {
        using var b = new Bitmap(src);
        int l = b.Width, t = b.Height, r = -1, btm = -1;
        for (int y = 0; y < b.Height; y++)
            for (int x = 0; x < b.Width; x++)
                if (b.GetPixel(x, y).A > 16) { l = Math.Min(l, x); r = Math.Max(r, x); t = Math.Min(t, y); btm = Math.Max(btm, y); }
        var crop = r < 0 ? new Rectangle(0, 0, b.Width, b.Height) : Rectangle.FromLTRB(l, t, r + 1, btm + 1);
        double k = Math.Min((double)boxW / crop.Width, (double)boxH / crop.Height);
        k = Math.Min(k, size); // 원본의 1.4배로 보여 주고, 늘어나는 픽셀은 업스케일링으로 매끈하게 채운다
        int w = Math.Max(1, (int)(crop.Width * k)), h = Math.Max(1, (int)(crop.Height * k));
        // 업스케일링: 도트 그림용 방식(Scale2x)으로 4배까지 계단 없이 키운 뒤, 원하는 크기로 부드럽게 줄인다.
        // 그냥 늘리면 흐릿해지고, 이렇게 하면 윤곽은 또렷하고 대각선은 매끈하다
        using var part = b.Clone(crop, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var x2 = Scale2x(part);
        using var x4 = Scale2x(x2);
        var outp = new Bitmap(w, h);
        using var g = Graphics.FromImage(outp);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
        g.DrawImage(x4, new Rectangle(0, 0, w, h), new Rectangle(0, 0, x4.Width, x4.Height), GraphicsUnit.Pixel);
        return outp;
    }

    /// <summary>도트 그림을 2배로 키우는 Scale2x(EPX). 이웃 픽셀을 보고 대각선 계단을 메운다</summary>
    static Bitmap Scale2x(Bitmap src)
    {
        int w = src.Width, h = src.Height;
        var fmt = System.Drawing.Imaging.PixelFormat.Format32bppArgb;
        var sd = src.LockBits(new Rectangle(0, 0, w, h), System.Drawing.Imaging.ImageLockMode.ReadOnly, fmt);
        var s = new int[w * h];
        for (int y = 0; y < h; y++) System.Runtime.InteropServices.Marshal.Copy(sd.Scan0 + y * sd.Stride, s, y * w, w);
        src.UnlockBits(sd);
        int W = w * 2, H = h * 2;
        var d = new int[W * H];
        int P(int x, int y) => s[Math.Clamp(y, 0, h - 1) * w + Math.Clamp(x, 0, w - 1)];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int e = P(x, y), a = P(x, y - 1), bb = P(x + 1, y), c = P(x - 1, y), dd = P(x, y + 1);
                int e0 = e, e1 = e, e2 = e, e3 = e;
                if (a != dd && c != bb)
                {
                    if (c == a) e0 = a;
                    if (a == bb) e1 = bb;
                    if (c == dd) e2 = c;
                    if (dd == bb) e3 = bb;
                }
                int o = y * 2 * W + x * 2;
                d[o] = e0; d[o + 1] = e1; d[o + W] = e2; d[o + W + 1] = e3;
            }
        var dst = new Bitmap(W, H, fmt);
        var dd2 = dst.LockBits(new Rectangle(0, 0, W, H), System.Drawing.Imaging.ImageLockMode.WriteOnly, fmt);
        for (int y = 0; y < H; y++) System.Runtime.InteropServices.Marshal.Copy(d, y * W, dd2.Scan0 + y * dd2.Stride, W);
        dst.UnlockBits(dd2);
        return dst;
    }

    /// <summary>빠른 길: maple.gg 페이지 글을 바로 받아 코디·서버를 찾는다 (1초 안쪽). 못 찾으면 null</summary>
    static async Task<Info?> FetchFast(string nick)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://maple.gg/u/" + Uri.EscapeDataString(nick));
            req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36");
            req.Headers.TryAddWithoutValidation("Accept-Language", "ko-KR,ko;q=0.9");
            using var res = await http.SendAsync(req);
            if (!res.IsSuccessStatusCode) return null;
            var html = await res.Content.ReadAsStringAsync();
            var av = System.Text.RegularExpressions.Regex.Match(html, @"https://avatar\.maplestory\.nexon\.com/Character/[^""'\s\\<>]+");
            var wm = System.Text.RegularExpressions.Regex.Match(html, @"class=""world""><img src=""([^""]+)""[^>]*>\s*<span>([^<]+)</span>");
            if (!av.Success) return null;
            var avatarImg = Pic(System.Net.WebUtility.HtmlDecode(av.Value));
            var iconImg = wm.Success ? Pic(System.Net.WebUtility.HtmlDecode(wm.Groups[1].Value)) : Task.FromResult<Image?>(null);
            await Task.WhenAll(avatarImg, iconImg);
            if (avatarImg.Result == null) return null;
            return new Info(nick, wm.Success ? System.Net.WebUtility.HtmlDecode(wm.Groups[2].Value).Trim() : DefaultWorld, avatarImg.Result, iconImg.Result);
        }
        catch (Exception ex)
        {
            Log.Write("개발자 정보", "maple.gg 바로 받기 실패", ex);
            return null;
        }
    }

    static async Task<Image?> Pic(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != "https") return null;
        try
        {
            var bytes = await http.GetByteArrayAsync(u);
            using var ms = new MemoryStream(bytes);
            return new Bitmap(Image.FromStream(ms));
        }
        catch { return null; }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            border.Dispose();
            // 잘라 키운 코디만 버리고, 불러온 원본 그림은 다음에 다시 쓰므로 남긴다
            avatar.Image?.Dispose(); avatar.Image = null; icon.Image = null;
        }
        base.Dispose(disposing);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool ReleaseCapture();
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW;
            cp.ClassStyle |= CS_DROPSHADOW;
            return cp;
        }
    }
}
