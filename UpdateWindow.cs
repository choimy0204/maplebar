using System.Drawing.Drawing2D;
using System.Security.Cryptography;
using static MapleBar.Native;

namespace MapleBar;

/// <summary>업데이트 창: 새 버전을 알리고, 받는 동안 진행 막대를 보여 준다.
/// 다 받으면 File에 설치 파일 경로가 들어 있다.</summary>
sealed class UpdateWindow : Form
{
    public string? File { get; private set; }
    /// <summary>받은 파일의 SHA256 (설치 직전에 한 번 더 맞춰 본다)</summary>
    public string? Hash { get; private set; }

    /// <summary>받을 곳: Program Files 안이라 관리자만 쓸 수 있다 (받은 뒤 다른 프로그램이 바꿔치기 못 하게)</summary>
    static string UpdateDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "MapleBar", ".update");

    readonly string url;
    readonly string? shaUrl;
    readonly float sc;
    readonly Label title, sub, status;
    readonly ProgressLine bar;
    readonly FlowLayoutPanel row;
    readonly Version mine;
    PatchNotesWindow? notes;
    int S(float v) => UI.Scale(v, sc);

    /// <param name="ask">true면 먼저 물어보고, false면 바로 받기 시작한다</param>
    public UpdateWindow(Version mine, Version ver, string url, string? shaUrl, bool ask)
    {
        this.url = url;
        this.shaUrl = shaUrl;
        this.mine = mine;
        sc = DeviceDpi / 96f;
        Text = "메이플 바 업데이트";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        TopMost = true;
        BackColor = UI.Back;
        Font = new Font("맑은 고딕", 10f);
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(S(360), S(178));
        if (UI.AppIcon is { } appIcon) Icon = appIcon;

        title = new Label
        {
            Font = new Font("맑은 고딕", 13f, FontStyle.Bold), ForeColor = UI.Text, AutoSize = false,
            Location = new Point(S(22), S(20)), Size = new Size(S(230), S(30)),
        };
        sub = new Label
        {
            Text = $"{UpdateCheck.Show(mine)}  →  {UpdateCheck.Show(ver)}", ForeColor = UI.T(Color.FromArgb(130, 130, 130)), AutoSize = false,
            Location = new Point(S(22), S(52)), Size = new Size(S(316), S(22)),
        };
        status = new Label
        {
            ForeColor = UI.Text, AutoSize = false, AutoEllipsis = true,
            Location = new Point(S(22), S(86)), Size = new Size(S(316), S(22)),
        };
        bar = new ProgressLine { Location = new Point(S(22), S(116)), Size = new Size(S(316), S(8)) };
        row = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft, AutoSize = false, BackColor = UI.Back,
            Location = new Point(S(14), S(134)), Size = new Size(S(332), S(32)),
        };
        Controls.AddRange(new Control[] { title, sub, status, bar, row });
        // 오른쪽 위: 무엇이 바뀌었는지 새 창으로 보여 준다
        var notesBtn = UI.Button("패치노트", "", OpenNotes, sc, null, UI.HoverBack);
        notesBtn.Radius = S(6);
        Controls.Add(notesBtn);
        notesBtn.Location = new Point(ClientSize.Width - S(16) - notesBtn.PreferredWidth, S(22));
        foreach (Control c in Controls) c.MouseDown += Drag;
        MouseDown += Drag;

        if (ask)
        {
            title.Text = "새 버전이 나왔어요";
            status.Text = "업데이트해야 메이플 바를 켤 수 있어요.";
            bar.Visible = false;
            Buttons(("업데이트", true, () => _ = Download()), ("나중에", false, Close));
        }
        else Shown += (_, _) => _ = Download();

        Paint += (_, e) => e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        HandleCreated += (_, _) => UI.RoundCorners(Handle);
    }

    readonly Pen border = new(UI.Line); // 테두리 색은 고정이라 재사용

    protected override void Dispose(bool disposing)
    {
        if (disposing) border.Dispose();
        base.Dispose(disposing);
    }

    void OpenNotes()
    {
        if (notes == null || notes.IsDisposed) { notes = new PatchNotesWindow(this, mine) { Owner = this }; notes.Show(); }
        else notes.Activate();
    }

    void Buttons(params (string text, bool main, Action click)[] list)
    {
        var old = row.Controls.Cast<Control>().ToList();
        row.Controls.Clear();
        // 누른 버튼이 이벤트 처리 중일 수 있어, 처리가 끝난 뒤에 해제한다
        if (IsHandleCreated) BeginInvoke(() => { foreach (var c in old) c.Dispose(); });
        else foreach (var c in old) c.Dispose();
        // 오른쪽부터 쌓이니 거꾸로 넣는다 (첫 버튼이 왼쪽)
        foreach (var (text, main, click) in list.Reverse())
        {
            var b = main ? UI.Button(text, "", click, sc, Color.White, UI.Accent) : UI.Button(text, "", click, sc, null, UI.HoverBack);
            b.Radius = S(6); // 알약 말고 둥근 네모
            row.Controls.Add(b);
        }
    }

    async Task Download()
    {
        Buttons();
        title.Text = "새 버전을 받고 있어요";
        status.Text = "받을 준비를 하고 있어요…";
        bar.Visible = true;
        bar.Value = 0;
        // 앞서 받은 파일이 아직 설치 중이라 잠겨 있을 수 있어, 받을 때마다 새 폴더에 받는다
        string file = Path.Combine(UpdateDir, Guid.NewGuid().ToString("N")[..8], Path.GetFileName(new Uri(url).LocalPath));
        try
        {
            // 깃허브 주소에서만 받는다
            if (!UpdateCheck.IsGitHub(url) || (shaUrl != null && !UpdateCheck.IsGitHub(shaUrl))) throw new Exception("받을 주소가 이상해요.");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            // MaxResponseContentBufferSize: 한 번에 받는 SHA256 값 같은 작은 응답만 제한한다 (설치 파일은 아래에서 따로 센다)
            // 넘겨 주는 주소를 하나씩 확인하려고 자동으로 따라가지 않는다
            using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(10), MaxResponseContentBufferSize = 1024 * 1024 };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MapleBar");
            using (var res = await GetGitHub(http, url, HttpCompletionOption.ResponseHeadersRead))
            {
                res.EnsureSuccessStatusCode();
                long total = res.Content.Headers.ContentLength ?? 0, done = 0;
                const long Max = 300L * 1024 * 1024; // 설치 파일은 100MB도 안 된다. 이보다 크면 이상한 파일이다
                if (total > Max) throw new Exception("받을 파일이 너무 커요.");
                await using var src = await res.Content.ReadAsStreamAsync();
                await using var dst = System.IO.File.Create(file);
                var buf = new byte[81920];
                int n;
                long shown = Environment.TickCount64 - 1000;
                while ((n = await src.ReadAsync(buf)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n));
                    done += n;
                    if (done > Max) throw new Exception("받을 파일이 너무 커요.");
                    if (Environment.TickCount64 - shown < 80 && done != total) continue;
                    shown = Environment.TickCount64;
                    if (total > 0)
                    {
                        bar.Value = (float)done / total;
                        status.Text = $"{done * 100 / total}%   ·   {done / 1048576.0:0.0} / {total / 1048576.0:0.0} MB";
                    }
                    else status.Text = $"{done / 1048576.0:0.0} MB 받았어요";
                }
            }
            // 깃허브가 함께 올린 SHA256 값과 같아야 설치한다 (받다가 깨진 파일·바꿔치기 방지). 확인 값이 없으면 설치하지 않는다
            if (shaUrl == null) throw new Exception("받은 파일을 확인할 수 없어요.");
            {
                status.Text = "받은 파일을 확인하고 있어요…";
                string text;
                using (var res = await GetGitHub(http, shaUrl, HttpCompletionOption.ResponseContentRead))
                {
                    res.EnsureSuccessStatusCode();
                    text = await res.Content.ReadAsStringAsync();
                }
                var want = FindHash(text, Path.GetFileName(file)) ?? throw new Exception("받은 파일을 확인할 수 없어요.");
                string got;
                await using (var s = System.IO.File.OpenRead(file)) got = Convert.ToHexString(await SHA256.HashDataAsync(s));
                if (!got.Equals(want, StringComparison.OrdinalIgnoreCase)) throw new Exception("받은 파일이 원본과 달라요.");
                Hash = got;
            }
            bar.Value = 1;
            title.Text = "다 받았어요";
            status.Text = "설치하고 바로 다시 켤게요.";
            await Task.Delay(700);
            File = file;
            Close();
        }
        catch (Exception ex)
        {
            title.Text = "업데이트를 받지 못했어요";
            status.Text = ex.Message;
            bar.Visible = false;
            Buttons(("브라우저로 받기", true, () =>
            {
                if (Hosts.IsWeb(url))
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
                Close();
            }), ("닫기", false, Close));
        }
    }

    /// <summary>github.com 주소를 받는다. 넘겨 주는 주소는 깃허브 것만 따라간다 (bootstrap Getter와 같은 규칙)</summary>
    static async Task<HttpResponseMessage> GetGitHub(HttpClient http, string url, HttpCompletionOption option)
    {
        var u = new Uri(url);
        for (int hop = 0; hop < 6; hop++)
        {
            var res = await http.GetAsync(u, option);
            int code = (int)res.StatusCode;
            if (code >= 300 && code < 400 && res.Headers.Location != null)
            {
                u = new Uri(u, res.Headers.Location);
                res.Dispose();
                if (!UpdateCheck.IsGitHubFile(u)) throw new Exception("받을 주소가 이상해요.");
                continue;
            }
            return res;
        }
        throw new Exception("받을 주소가 이상해요.");
    }

    /// <summary>SHA256.txt에서 이 파일 이름의 값을 찾는다 ("값  파일이름" 줄, bootstrap Getter와 같은 규칙)</summary>
    static string? FindHash(string text, string name)
    {
        foreach (var line in text.Split('\n'))
        {
            var p = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length >= 2 && p[0].Length == 64 && p[0].All(Uri.IsHexDigit) &&
                p[1].TrimStart('*').Equals(name, StringComparison.OrdinalIgnoreCase))
                return p[0];
        }
        return null;
    }

    // 테두리 없는 창이라 아무 데나 잡고 끌 수 있게 한다
    void Drag(object? s, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || s is RoundLabel) return;
        DragWindow(Handle);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= CS_DROPSHADOW;
            return cp;
        }
    }

    /// <summary>둥근 진행 막대</summary>
    sealed class ProgressLine : Control
    {
        float value;
        public float Value { get => value; set { this.value = Math.Clamp(value, 0, 1); Invalidate(); } }
        public ProgressLine()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = UI.Back;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float r = Height / 2f;
            using (var track = UI.RoundRect(new RectangleF(0, 0, Width - 1, Height - 1), r))
            using (var b = new SolidBrush(UI.HoverBack)) g.FillPath(b, track);
            float w = (Width - 1) * value;
            if (w < 1) return;
            using var fill = UI.RoundRect(new RectangleF(0, 0, Math.Max(w, Height), Height - 1), r);
            using var a = new SolidBrush(UI.Accent);
            g.FillPath(a, fill);
        }
    }
}
