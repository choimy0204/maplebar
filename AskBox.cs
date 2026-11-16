namespace MapleBar;

/// <summary>
/// 우리 디자인으로 만든 예/아니요 질문 창 (윈도우 기본 메시지 창 대신 쓴다).
/// 위쪽 주황 줄, 굵은 질문, 작은 안내 글, 둥근 단추 두 개. Esc는 취소.
/// </summary>
sealed class AskBox : Form
{
    bool yes;

    /// <summary>질문을 띄우고 확인 단추를 누르면 true.</summary>
    public static bool Show(IWin32Window? owner, string question, string note, string yesText, string noText = "취소")
    {
        using var f = new AskBox(question, note, yesText, noText);
        if (owner is Form o) f.Location = new Point(o.Left + (o.Width - f.Width) / 2, o.Top + (o.Height - f.Height) / 3);
        else
        {
            var a = Screen.FromPoint(Cursor.Position).WorkingArea;
            f.Location = new Point(a.Left + (a.Width - f.Width) / 2, a.Top + (a.Height - f.Height) / 3);
        }
        f.ShowDialog(owner);
        return f.yes;
    }

    readonly Pen border = new(UI.Line);
    readonly List<Font> fonts = new();

    AskBox(string question, string note, string yesText, string noText)
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
        int w = S(360);
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

        RoundLabel Btn(string text, Color fore, Color back, bool result)
        {
            var b = new RoundLabel
            {
                AutoSize = false, Size = new Size((w - S(36) - S(10)) / 2, S(36)), Radius = S(8), Text = text, Fill = back, ForeColor = fore,
                TextAlign = ContentAlignment.MiddleCenter, Font = F(10, FontStyle.Bold), Cursor = Cursors.Hand,
            };
            b.MouseEnter += (_, _) => b.Fill = ControlPaint.Dark(back, 0.05f);
            b.MouseLeave += (_, _) => b.Fill = back;
            b.Click += (_, _) => { yes = result; Close(); };
            return b;
        }
        var ok = Btn(yesText, Color.White, UI.Accent, true);
        var no = Btn(noText, UI.Text, UI.HoverBack, false);
        ok.Location = new Point(S(18), y);
        no.Location = new Point(w - S(18) - no.Width, y);
        ClientSize = new Size(w, y + ok.Height + S(16));
        Controls.Add(bar);
        Controls.Add(title);
        if (sub != null) Controls.Add(sub);
        Controls.Add(ok);
        Controls.Add(no);

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
