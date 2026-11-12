using System.Net;
using System.Text.RegularExpressions;
using static MapleBar.Native;

namespace MapleBar;

/// <summary>패치노트 창: 깃허브에 올린 패치노트 페이지(docs/index.html)를 읽어 버전별 목록으로 보여 준다.</summary>
sealed class PatchNotesWindow : Form
{
    const string Source = "https://raw.githubusercontent.com/choimy0204/maplebar/main/docs/index.html";

    record Group(string Tag, List<string> Items);
    record Note(string Ver, string Date, string Title, List<Group> Groups);
    static List<Note>? cache;

    readonly float sc;
    readonly Version mine;
    readonly Panel body;
    readonly FlowLayoutPanel list;
    int S(float v) => UI.Scale(v, sc);
    int TextW => body.ClientSize.Width - S(40);

    public PatchNotesWindow(Form near, Version mine)
    {
        this.mine = mine;
        sc = DeviceDpi / 96f;
        Text = "메이플 바 패치노트";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.White;
        Font = new Font("맑은 고딕", 9.5f);
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(S(400), S(480));
        // 업데이트 창 오른쪽에 붙여 띄운다 (자리가 없으면 왼쪽)
        var wa = Screen.FromControl(near).WorkingArea;
        int x = near.Right + S(8);
        if (x + Width > wa.Right) x = near.Left - S(8) - Width;
        Location = new Point(Math.Max(wa.Left, x), Math.Clamp(near.Top, wa.Top, Math.Max(wa.Top, wa.Bottom - Height)));

        var title = new Label
        {
            Text = "패치노트", Font = new Font("맑은 고딕", 13f, FontStyle.Bold), ForeColor = UI.Text, AutoSize = false,
            Location = new Point(S(22), S(18)), Size = new Size(S(250), S(30)),
        };
        var close = UI.Button("닫기", "", Close, sc, null, UI.HoverBack);
        close.Radius = S(6);
        Controls.Add(close);
        close.Location = new Point(ClientSize.Width - S(16) - close.PreferredWidth, S(20));
        body = new Panel
        {
            AutoScroll = true, BackColor = Color.White,
            Location = new Point(S(8), S(58)), Size = new Size(ClientSize.Width - S(10), ClientSize.Height - S(66)),
        };
        list = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.White, Location = new Point(S(14), 0),
        };
        body.Controls.Add(list);
        Controls.AddRange(new Control[] { title, body });
        title.MouseDown += Drag;
        MouseDown += Drag;
        body.MouseEnter += (_, _) => body.Focus(); // 휠로 내려 보게

        Line("불러오고 있어요…", Color.FromArgb(130, 130, 130));
        Shown += async (_, _) => Fill(await Load());
        Paint += (_, e) => { using var p = new Pen(UI.Line); e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1); };
        HandleCreated += (_, _) => UI.RoundCorners(Handle);
    }

    static async Task<List<Note>?> Load()
    {
        if (cache != null) return cache;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MapleBar");
            var notes = Parse(await http.GetStringAsync(Source));
            return notes.Count > 0 ? cache = notes : null;
        }
        catch { return null; }
    }

    static readonly RegexOptions O = RegexOptions.Singleline | RegexOptions.IgnoreCase;

    static List<Note> Parse(string html)
    {
        static string Clean(string s) => WebUtility.HtmlDecode(Regex.Replace(s, "<[^>]+>", "")).Trim();
        static string Find(string s, string cls) => Regex.Match(s, $"class=\"{cls}\"[^>]*>(.*?)</", O) is { Success: true } m ? Clean(m.Groups[1].Value) : "";
        var notes = new List<Note>();
        foreach (Match a in Regex.Matches(html, "<article class=\"release\"[^>]*>(.*?)</article>", O))
        {
            var s = a.Groups[1].Value;
            var groups = new List<Group>();
            foreach (Match g in Regex.Matches(s, "<span class=\"tag[^\"]*\">(.*?)</span>\\s*<ul[^>]*>(.*?)</ul>", O))
                groups.Add(new Group(Clean(g.Groups[1].Value),
                    Regex.Matches(g.Groups[2].Value, "<li>(.*?)</li>", O).Select(m => Clean(m.Groups[1].Value)).ToList()));
            var ver = Find(s, "ver");
            if (ver != "") notes.Add(new Note(ver, Find(s, "date"), Find(s, "title"), groups));
        }
        return notes;
    }

    void Fill(List<Note>? notes)
    {
        if (IsDisposed) return;
        list.SuspendLayout();
        list.Controls.Clear();
        if (notes == null) Line("패치노트를 불러오지 못했어요. 인터넷 연결을 확인해 주세요.", Color.FromArgb(130, 130, 130));
        else
            foreach (var n in notes)
            {
                bool isNew = Version.TryParse(n.Ver, out var v) && v > mine;
                // 버전 줄: 1.1.2  2026.11.12  (새 버전이면 주황)
                var head = Line(n.Ver + (isNew ? "  새 버전" : ""), isNew ? UI.Accent : UI.Text, 11.5f, true);
                head.Margin = new Padding(0, list.Controls.Count == 1 ? S(2) : S(14), 0, 0);
                var sub = string.Join("  ·  ", new[] { n.Date, n.Title }.Where(t => t != ""));
                if (sub != "") Line(sub, Color.FromArgb(130, 130, 130));
                foreach (var g in n.Groups)
                {
                    var tag = new RoundLabel
                    {
                        Text = g.Tag, AutoSize = true, Fill = UI.SoftAccent, ForeColor = Color.FromArgb(200, 95, 0),
                        Font = new Font("맑은 고딕", 8.5f, FontStyle.Bold), Radius = S(6),
                        Padding = new Padding(S(6), S(2), S(6), S(2)), Margin = new Padding(0, S(8), 0, S(2)),
                    };
                    list.Controls.Add(tag);
                    foreach (var it in g.Items) Line("·  " + it, UI.Text).Margin = new Padding(S(2), S(2), 0, 0);
                }
            }
        list.ResumeLayout();
    }

    Label Line(string text, Color color, float size = 9.5f, bool bold = false)
    {
        var l = new Label
        {
            Text = text, ForeColor = color, AutoSize = true, UseMnemonic = false,
            Font = new Font("맑은 고딕", size, bold ? FontStyle.Bold : FontStyle.Regular),
            MaximumSize = new Size(TextW, 0), Margin = new Padding(0, S(2), 0, 0),
        };
        l.MouseEnter += (_, _) => body.Focus();
        list.Controls.Add(l);
        return l;
    }

    void Drag(object? s, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
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
}
