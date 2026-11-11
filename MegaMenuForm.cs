using static MapleBar.Native;

namespace MapleBar;

/// <summary>메뉴에 마우스를 올리면 바 아래로 넓게 펼쳐지는 메뉴 (사이트의 드롭다운과 같은 모양).</summary>
public class MegaMenuForm : Form
{
    const int MaxRows = 6; // 구역 안에서 한 칸에 넣는 최대 항목 수 (넘치면 옆 칸으로)
    readonly float sc;
    readonly FlowLayoutPanel groups;
    int S(int v) => UI.Scale(v, sc);

    public MegaMenuForm(MenuEntry[] items, float scale, Action<string> onPick)
    {
        sc = scale;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = UI.Line;               // 1px 테두리
        Padding = new Padding(1);

        groups = new FlowLayoutPanel
        {
            AutoSize = true, WrapContents = false, BackColor = Color.White,
            Padding = new Padding(S(28), S(16), S(28), S(18)), Location = new Point(1, 1),
        };

        // 구역 제목(Path == null)을 기준으로 묶는다
        var sections = new List<(string Title, List<MenuEntry> Items)>();
        foreach (var it in items)
        {
            if (it.Path == null || sections.Count == 0) sections.Add((it.Path == null ? it.Title : "", new()));
            if (it.Path != null) sections[^1].Items.Add(it);
        }

        for (int i = 0; i < sections.Count; i++)
        {
            if (i > 0)
                groups.Controls.Add(new Panel
                {
                    Width = 1, BackColor = UI.Line,
                    Margin = new Padding(S(18), 0, S(18), 0),
                });
            groups.Controls.Add(BuildSection(sections[i].Title, sections[i].Items, onPick));
        }

        Controls.Add(groups);
        groups.PerformLayout();
        // 구분선을 가장 긴 구역 높이에 맞춘다
        int h = groups.Controls.OfType<Control>().Where(c => c.Width > 1).Max(c => c.PreferredSize.Height);
        foreach (Control c in groups.Controls) if (c.Width == 1) c.Height = h;
    }

    Control BuildSection(string title, List<MenuEntry> items, Action<string> onPick)
    {
        var box = new FlowLayoutPanel
        {
            AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            BackColor = Color.White, Margin = Padding.Empty,
        };
        box.Controls.Add(new Label
        {
            Text = title, AutoSize = true, UseMnemonic = false,
            Font = new Font("맑은 고딕", 9.5f * UI.Z, FontStyle.Bold), ForeColor = Color.FromArgb(120, 120, 120),
            Margin = new Padding(0, 0, 0, S(12)),
        });

        var cols = new FlowLayoutPanel
        {
            AutoSize = true, WrapContents = false, BackColor = Color.White, Margin = Padding.Empty,
        };
        for (int start = 0; start < items.Count; start += MaxRows)
        {
            var col = new FlowLayoutPanel
            {
                AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false,
                BackColor = Color.White, Margin = new Padding(0, 0, start + MaxRows < items.Count ? S(22) : 0, 0),
            };
            foreach (var it in items.Skip(start).Take(MaxRows))
                col.Controls.Add(MakeItem(it, onPick));
            cols.Controls.Add(col);
        }
        box.Controls.Add(cols);
        return box;
    }

    Label MakeItem(MenuEntry it, Action<string> onPick)
    {
        var lbl = new Label
        {
            Text = it.Title, AutoSize = true, UseMnemonic = false, Cursor = Cursors.Hand,
            Font = new Font("맑은 고딕", 10.5f * UI.Z, FontStyle.Bold), ForeColor = UI.Text,
            Margin = new Padding(0, 0, 0, S(9)),
        };
        lbl.MouseEnter += (_, _) => lbl.ForeColor = UI.Accent;
        lbl.MouseLeave += (_, _) => lbl.ForeColor = UI.Text;
        lbl.Click += (_, _) => { Hide(); onPick(it.Path!); };
        return lbl;
    }

    public Size ContentSize => new(groups.PreferredSize.Width + Padding.Horizontal,
                                   groups.PreferredSize.Height + Padding.Vertical);

    /// <summary>바 바로 아래, 바 너비 이상으로 펼친다. 처음 나타날 때는 위에서부터 부드럽게 내려온다.</summary>
    public void ShowUnder(Form bar)
    {
        var size = ContentSize;
        int w = Math.Max(size.Width, bar.Width);
        var wa = Screen.FromControl(bar).WorkingArea;
        int x = Math.Max(wa.Left, Math.Min(bar.Left, wa.Right - w));
        var target = new Rectangle(x, bar.Bottom + S(8), w, size.Height); // 바에서 조금 띄운다
        groups.Width = w - Padding.Horizontal;
        fullH = target.Height;
        if (!Visible)
        {
            Opacity = 0;
            Bounds = new Rectangle(target.X, target.Y, target.Width, StartH);
            Show(bar);
            clock.Restart();
            anim.Start();
        }
        else if (anim.Enabled) Bounds = new Rectangle(target.X, target.Y, target.Width, Height);
        else Bounds = target;
        BringToFront();
    }

    // 펼침 애니메이션: 높이가 아래로 늘어나며 서서히 나타난다
    const int AnimMs = 190;
    int StartH => S(12);
    int fullH;
    readonly System.Diagnostics.Stopwatch clock = new();
    readonly System.Windows.Forms.Timer anim = new() { Interval = 15 };

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        anim.Tick += (_, _) =>
        {
            double p = Math.Min(1, clock.Elapsed.TotalMilliseconds / AnimMs);
            double k = 1 - Math.Pow(1 - p, 3); // 처음엔 빠르게, 끝에서 천천히
            Height = (int)Math.Round(StartH + (fullH - StartH) * k);
            Opacity = Math.Min(1, k * 1.3);
            if (p >= 1) { Height = fullH; Opacity = 1; anim.Stop(); }
        };
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!Visible) anim.Stop();
    }

    protected override bool ShowWithoutActivation => true;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UI.RoundCorners(Handle);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= CS_DROPSHADOW; // 그림자
            return cp;
        }
    }
}
