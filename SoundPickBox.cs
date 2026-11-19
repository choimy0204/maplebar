namespace MapleBar;

/// <summary>
/// 알림마다 소리를 고르는 창. 줄마다 알림 이름과 "&lt; 소리 이름 ▾ &gt;"가 있다.
/// 바꾸면 바로 저장하고 들려준다. 소리 이름을 누르면 목록이 펼쳐져 바로 고를 수 있다. Esc는 닫기.
/// </summary>
sealed class SoundPickBox : Form
{
    public static void Open(Form owner, Settings st)
    {
        using var f = new SoundPickBox(st);
        f.Location = new Point(owner.Left + (owner.Width - f.Width) / 2, owner.Top + (owner.Height - f.Height) / 3);
        f.ShowDialog(owner);
    }

    readonly Pen border = new(UI.Line);
    readonly List<Font> fonts = new();

    SoundPickBox(Settings st)
    {
        float sc = DeviceDpi / 96f * UI.Z;
        int S(float v) => UI.Scale(v, sc);
        Font F(float size, FontStyle style = FontStyle.Regular) { var x = new Font("맑은 고딕", size * UI.Z, style); fonts.Add(x); return x; }
        Text = "메이플 바";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = UI.Back;
        AutoScaleMode = AutoScaleMode.None;
        KeyPreview = true;
        int w = S(400), pad = S(18);

        var bar = new Panel { BackColor = UI.Accent, Location = Point.Empty, Size = new Size(w, S(5)) };
        var title = new Label { Text = "알림 소리 고르기", Font = F(11.5f, FontStyle.Bold), ForeColor = UI.Text, AutoSize = true, Location = new Point(pad, S(20)) };
        int y = title.Top + title.PreferredSize.Height + S(6);
        var sub = new Label
        {
            Text = "바꾸면 바로 들려줘요. 소리 이름을 누르면 목록에서 고를 수 있어요.", Font = F(8.5f), ForeColor = UI.Sub, AutoSize = true,
            MaximumSize = new Size(w - pad * 2, 0), Location = new Point(pad, y),
        };
        y += sub.PreferredSize.Height + S(14);
        Controls.AddRange(new Control[] { bar, title, sub });

        int Vol() => Math.Max(10, st.SoundVol);
        var values = new List<(AlertSound.Kind k, RoundLabel v)>();
        RoundLabel Step(string t, Action click)
        {
            var b = new RoundLabel
            {
                AutoSize = false, Size = new Size(S(30), S(30)), Radius = S(8), Text = t, Fill = UI.HoverBack, ForeColor = UI.Text,
                TextAlign = ContentAlignment.MiddleCenter, Font = F(10, FontStyle.Bold), Cursor = Cursors.Hand,
            };
            b.MouseEnter += (_, _) => b.Fill = ControlPaint.Dark(UI.HoverBack, 0.05f);
            b.MouseLeave += (_, _) => b.Fill = UI.HoverBack;
            b.Click += (_, _) => click();
            return b;
        }

        // 가운데 칸: 바의 '최근 검색' 칸과 같은 색 (다크: 차콜 칸에 호박색 글씨)
        Color valBack = UI.Dark ? UI.HoverBack : Color.FromArgb(255, 243, 224);
        Color valFore = UI.Dark ? UI.AccentText : Color.FromArgb(150, 80, 0);
        var menuFont = F(10);
        var menuBold = F(10, FontStyle.Bold);
        string ValText(AlertSound.Kind k) => AlertSound.Names[AlertSound.Pick(k)] + "  ▾";

        foreach (AlertSound.Kind k in Enum.GetValues<AlertSound.Kind>())
        {
            var kind = k;
            var name = new Label { Text = AlertSound.KindNames[(int)k], Font = F(10), ForeColor = UI.Text, AutoSize = true, Location = new Point(pad, y + S(6)) };
            var val = new RoundLabel
            {
                AutoSize = false, Size = new Size(S(150), S(30)), Radius = S(8), Fill = valBack, ForeColor = valFore,
                TextAlign = ContentAlignment.MiddleCenter, Font = F(9.5f, FontStyle.Bold), Cursor = Cursors.Hand,
                Text = ValText(k),
            };
            val.MouseEnter += (_, _) => val.Fill = ControlPaint.Dark(valBack, 0.05f);
            val.MouseLeave += (_, _) => val.Fill = valBack;
            var next = Step(">", () => Set(AlertSound.Pick(kind) + 1));
            var prev = Step("<", () => Set(AlertSound.Pick(kind) - 1));
            next.Location = new Point(w - pad - next.Width, y);
            val.Location = new Point(next.Left - S(6) - val.Width, y);
            prev.Location = new Point(val.Left - S(6) - prev.Width, y);
            val.Click += (_, _) => ShowList();
            Controls.AddRange(new Control[] { name, prev, val, next });
            values.Add((k, val));
            y += S(38);

            void Set(int idx)
            {
                AlertSound.SetPick(st, kind, idx);
                st.Save();
                val.Text = ValText(kind);
                AlertSound.PlaySound(AlertSound.Pick(kind), Vol());
            }

            // 소리 이름을 누르면 바로 아래에 30가지 목록을 펼친다. 지금 고른 소리는 굵게.
            void ShowList()
            {
                var cms = UI.Rounded(new ContextMenuStrip { Font = menuFont, ShowImageMargin = false });
                int cur = AlertSound.Pick(kind);
                for (int i = 0; i < AlertSound.Count; i++)
                {
                    int idx = i;
                    var mi = new ToolStripMenuItem(AlertSound.Names[i]);
                    if (i == cur) mi.Font = menuBold;
                    mi.Click += (_, _) => Set(idx);
                    cms.Items.Add(mi);
                }
                cms.Closed += (_, _) => BeginInvoke(cms.Dispose);
                cms.Show(val, new Point(0, val.Height + S(4)));
            }
        }
        y += S(10);

        // 아래 단추: 기본값(회색) · 닫기(주황)
        int gap = S(10), bw = (w - pad * 2 - gap) / 2;
        RoundLabel Btn(string t, Color back, Color fore, int x, Action click)
        {
            var b = new RoundLabel
            {
                AutoSize = false, Size = new Size(bw, S(36)), Radius = S(8), Text = t, Fill = back, ForeColor = fore,
                TextAlign = ContentAlignment.MiddleCenter, Font = F(10, FontStyle.Bold), Cursor = Cursors.Hand, Location = new Point(x, y),
            };
            b.MouseEnter += (_, _) => b.Fill = ControlPaint.Dark(back, 0.05f);
            b.MouseLeave += (_, _) => b.Fill = back;
            b.Click += (_, _) => click();
            Controls.Add(b);
            return b;
        }
        Btn("기본값", UI.HoverBack, UI.Text, pad, () =>
        {
            st.SoundPicks = null;
            st.Save();
            foreach (var (k, v) in values) v.Text = ValText(k);
        });
        Btn("닫기", UI.Accent, Color.White, pad + bw + gap, Close);
        ClientSize = new Size(w, y + S(36) + S(16));

        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { e.Handled = true; Close(); } };
        Paint += (_, e) => e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        HandleCreated += (_, _) => UI.RoundCorners(Handle);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= 0x20000; // 그림자 (CS_DROPSHADOW)
            return cp;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { border.Dispose(); foreach (var f in fonts) f.Dispose(); }
        base.Dispose(disposing);
    }
}
