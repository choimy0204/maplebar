using static MapleBar.Native;

namespace MapleBar;

/// <summary>정령의 펜던트 미착용일 때 게임 화면 위쪽 가운데에 띄우는 작은 창.
/// "착용 완료"는 알림만 멈추고(확인은 계속), "미착용 예정"은 정펜 확인을 끈다. 게임 포커스는 뺏지 않는다.</summary>
sealed class PendantAsk : Form
{
    public PendantAsk(Action worn, Action notWear)
    {
        float sc = DeviceDpi / 96f * UI.Z;
        int S(float v) => UI.Scale(v, sc);
        Font F(float size, FontStyle style = FontStyle.Regular) => new("맑은 고딕", size * UI.Z, style);
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.White;
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(S(360), S(128));

        var bar = new Panel { BackColor = PendantCheck.Yellow, Location = Point.Empty, Size = new Size(ClientSize.Width, S(5)) };
        var title = new Label
        {
            Text = "정령의 펜던트를 끼셨나요?", Font = F(11.5f, FontStyle.Bold), ForeColor = UI.Text,
            AutoSize = true, Location = new Point(S(18), S(18)),
        };
        var sub = new Label
        {
            Text = "maple.gg는 늦게 바뀔 수 있어요. 꼈다면 '착용 완료'를 눌러 주세요.",
            Font = F(8.5f), ForeColor = Color.FromArgb(130, 130, 136), AutoSize = true, Location = new Point(S(18), S(46)),
            MaximumSize = new Size(S(360) - S(36), 0), // 창 폭을 넘으면 줄을 바꾼다
        };
        RoundLabel Btn(string text, Color fore, Color back, Action act)
        {
            var b = new RoundLabel
            {
                AutoSize = false, Size = new Size(S(156), S(36)), Radius = S(8), Text = text, Fill = back, ForeColor = fore,
                TextAlign = ContentAlignment.MiddleCenter, Font = F(10, FontStyle.Bold), Cursor = Cursors.Hand,
            };
            b.MouseEnter += (_, _) => b.Fill = ControlPaint.Dark(back, 0.05f);
            b.MouseLeave += (_, _) => b.Fill = back;
            b.Click += (_, _) => { Close(); act(); };
            return b;
        }
        var yes = Btn("착용 완료", Color.White, UI.Accent, worn);
        var no = Btn("미착용 예정", UI.Text, UI.HoverBack, notWear);
        // 안내 글이 두 줄이 되면 버튼과 창을 그만큼 내린다
        int by = Math.Max(S(76), sub.Top + sub.PreferredSize.Height + S(12));
        yes.Location = new Point(S(18), by);
        no.Location = new Point(ClientSize.Width - S(18) - no.Width, by);
        ClientSize = new Size(ClientSize.Width, by + yes.Height + S(16));
        Controls.AddRange(new Control[] { bar, title, sub, yes, no });

        // 게임 화면 위쪽 가운데 (게임이 없으면 주 모니터)
        var area = Game.Area() ?? Screen.PrimaryScreen!.WorkingArea;
        Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + area.Height / 6);
        Paint += (_, e) => e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        HandleCreated += (_, _) => UI.RoundCorners(Handle);
    }

    readonly Pen border = new(UI.Line);

    protected override void Dispose(bool disposing)
    {
        if (disposing) border.Dispose();
        base.Dispose(disposing);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST; // 작업 표시줄에 안 나오고 포커스를 뺏지 않게
            cp.ClassStyle |= CS_DROPSHADOW;
            return cp;
        }
    }
}
