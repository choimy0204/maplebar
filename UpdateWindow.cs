using System.Drawing.Drawing2D;
using System.Security.Cryptography;
using static MapleBar.Native;

namespace MapleBar;

/// <summary>업데이트 창: 새 버전을 알리고, 받는 동안 진행 막대를 보여 준다.
/// 다 받으면 File에 설치 파일 경로가 들어 있다.</summary>
sealed class UpdateWindow : Form
{
    public string? File { get; private set; }

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
        BackColor = Color.White;
        Font = new Font("맑은 고딕", 10f);
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(S(360), S(178));
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        title = new Label
        {
            Font = new Font("맑은 고딕", 13f, FontStyle.Bold), ForeColor = UI.Text, AutoSize = false,
            Location = new Point(S(22), S(20)), Size = new Size(S(230), S(30)),
        };
        sub = new Label
        {
            Text = $"{mine.ToString(3)}  →  {ver.ToString(3)}", ForeColor = Color.FromArgb(130, 130, 130), AutoSize = false,
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
            FlowDirection = FlowDirection.RightToLeft, AutoSize = false, BackColor = Color.White,
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

        Paint += (_, e) => { using var p = new Pen(UI.Line); e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1); };
        HandleCreated += (_, _) => UI.RoundCorners(Handle);
    }

    void OpenNotes()
    {
        if (notes == null || notes.IsDisposed) { notes = new PatchNotesWindow(this, mine) { Owner = this }; notes.Show(); }
        else notes.Activate();
    }

    void Buttons(params (string text, bool main, Action click)[] list)
    {
        row.Controls.Clear();
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
        string file = Path.Combine(Path.GetTempPath(), "MapleBar-Update", Path.GetFileName(new Uri(url).LocalPath));
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
                await using var dst = System.IO.File.Create(file);
                var buf = new byte[81920];
                int n;
                long shown = Environment.TickCount64 - 1000;
                while ((n = await src.ReadAsync(buf)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n));
                    done += n;
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
            // 깃허브가 함께 올린 SHA256 값과 같아야 설치한다 (받다가 깨진 파일 방지)
            if (shaUrl != null)
            {
                status.Text = "받은 파일을 확인하고 있어요…";
                var want = (await http.GetStringAsync(shaUrl)).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0];
                string got;
                await using (var s = System.IO.File.OpenRead(file)) got = Convert.ToHexString(await SHA256.HashDataAsync(s));
                if (!got.Equals(want, StringComparison.OrdinalIgnoreCase)) throw new Exception("받은 파일이 원본과 달라요.");
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
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
                Close();
            }), ("닫기", false, Close));
        }
    }

    // 테두리 없는 창이라 아무 데나 잡고 끌 수 있게 한다
    void Drag(object? s, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || s is RoundLabel) return;
        ReleaseCapture();
        SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero); // WM_NCLBUTTONDOWN, HTCAPTION
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool ReleaseCapture();
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);

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
            BackColor = Color.White;
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
