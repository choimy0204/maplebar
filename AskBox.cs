namespace MapleBar;

/// <summary>
/// 우리 디자인으로 만든 예/아니요 질문 창 (윈도우 기본 메시지 창 대신 쓴다).
/// 위쪽 주황 줄, 굵은 질문, 작은 안내 글, 둥근 단추 두 개. Esc는 취소.
/// </summary>
sealed class AskBox : Form
{
    int pick = -1;  // 누른 단추 번호 (-1 = 취소)
    bool enterYes;  // true면 엔터로 첫 단추

    /// <summary>질문을 띄우고 확인 단추를 누르면 true.</summary>
    public static bool Show(IWin32Window? owner, string question, string note, string yesText, string noText = "취소", bool enterYes = false)
        => Choose(owner, question, note, enterYes, yesText, noText) == 0;

    /// <summary>단추 여러 개 중 하나를 고르게 한다. 누른 단추 번호, 마지막 단추나 Esc는 취소로 -1</summary>
    public static int Choose(IWin32Window? owner, string question, string note, bool enterYes, params string[] buttons)
    {
        using var f = new AskBox(question, note, buttons);
        f.enterYes = enterYes;
        if (owner is Form o) f.Location = new Point(o.Left + (o.Width - f.Width) / 2, o.Top + (o.Height - f.Height) / 3);
        else
        {
            var a = Screen.FromPoint(Cursor.Position).WorkingArea;
            f.Location = new Point(a.Left + (a.Width - f.Width) / 2, a.Top + (a.Height - f.Height) / 3);
        }
        f.ShowDialog(owner);
        return f.pick == buttons.Length - 1 ? -1 : f.pick;
    }

    readonly Pen border = new(UI.Line);
    readonly List<Font> fonts = new();

    AskBox(string question, string note, string[] buttons)
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
        int n = buttons.Length;
        int w = S(n > 2 ? 440 : 360);
        ClientSize = new Size(w, S(128));

        var bar = new Panel { BackColor = UI.Accent, Location = Point.Empty, Size = new Size(w, S(5)) };
        var title = new Label
        {
            Text = question, Font = F(11.5f, FontStyle.Bold), ForeColor = UI.Text,
            AutoSize = true, MaximumSize = new Size(w - S(36), 0), Location = new Point(S(18), S(20)),
        };
        int y = title.Top + title.PreferredSize.Height + S(6);
        Label? sub = null;
        if (!string.IsNullOrEmpty(note))
        {
            sub = new Label
            {
                Text = note, Font = F(8.5f), ForeColor = UI.Sub, AutoSize = true,
                MaximumSize = new Size(w - S(36), 0), Location = new Point(S(18), y),
            };
            y += sub.PreferredSize.Height;
        }
        y += S(16);

        // 첫 단추는 주황(권하는 것), 나머지는 회색. 마지막 단추는 취소
        int gap = S(10), bw = (w - S(36) - gap * (n - 1)) / n;
        Controls.Add(bar);
        Controls.Add(title);
        if (sub != null) Controls.Add(sub);
        for (int i = 0; i < n; i++)
        {
            int idx = i;
            Color back = i == 0 ? UI.Accent : UI.HoverBack, fore = i == 0 ? Color.White : UI.Text;
            var b = new RoundLabel
            {
                AutoSize = false, Size = new Size(bw, S(36)), Radius = S(8), Text = buttons[i], Fill = back, ForeColor = fore,
                TextAlign = ContentAlignment.MiddleCenter, Font = F(n > 2 ? 9.5f : 10, FontStyle.Bold), Cursor = Cursors.Hand,
                Location = new Point(S(18) + (bw + gap) * i, y),
            };
            b.MouseEnter += (_, _) => b.Fill = ControlPaint.Dark(back, 0.05f);
            b.MouseLeave += (_, _) => b.Fill = back;
            b.Click += (_, _) => { pick = idx; Close(); };
            Controls.Add(b);
        }
        ClientSize = new Size(w, y + S(36) + S(16));

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) { e.Handled = true; Close(); }
            else if (e.KeyCode == Keys.Enter && enterYes) { e.Handled = true; pick = 0; Close(); }
        };
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
