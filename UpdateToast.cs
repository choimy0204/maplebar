using static MapleBar.Native;

namespace MapleBar;

/// <summary>켜져 있는 동안 새 버전이 나오면 화면 오른쪽 아래에 띄우는 작은 알림.
/// 게임 화면을 가리는 포커스는 뺏지 않는다.</summary>
sealed class UpdateToast : Form
{
    public static void Ask(Action onYes) => new UpdateToast(onYes).Show();

    UpdateToast(Action onYes)
    {
        float sc = DeviceDpi / 96f;
        int S(float v) => UI.Scale(v, sc);
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.White;
        Font = new Font("맑은 고딕", 10f);
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(S(280), S(104));

        var msg = new Label
        {
            Text = "새 버전이 올라왔어요.\n업데이트하시겠어요?",
            ForeColor = UI.Text, AutoSize = false,
            Location = new Point(S(16), S(12)), Size = new Size(S(250), S(44)),
        };
        var yes = UI.Button("Yes", "", () => { Close(); onYes(); }, sc, Color.White, UI.Accent);
        var no = UI.Button("No", "", Close, sc, null, UI.HoverBack);
        var row = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft, AutoSize = false,
            Location = new Point(S(12), S(62)), Size = new Size(S(256), S(32)), BackColor = Color.White,
        };
        row.Controls.Add(no);
        row.Controls.Add(yes);
        Controls.Add(msg);
        Controls.Add(row);

        var wa = Screen.PrimaryScreen!.WorkingArea;
        Location = new Point(wa.Right - Width - S(16), wa.Bottom - Height - S(16));
        Paint += (_, e) => e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        HandleCreated += (_, _) => UI.RoundCorners(Handle);
    }

    readonly Pen border = new(UI.Line); // 테두리 색은 고정이라 재사용

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
