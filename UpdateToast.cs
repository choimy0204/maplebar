using System.Drawing.Drawing2D;
using static MapleBar.Native;

namespace MapleBar;

/// <summary>켜져 있는 동안 새 버전이 나오면 화면 오른쪽 아래에 띄우는 작은 알림.
/// 왼쪽에 받기 아이콘, 굵은 제목과 작은 안내 글, 둥근 직사각형 단추 두 개 (프리텐다드 글꼴).
/// 게임 화면을 가리는 포커스는 뺏지 않는다.</summary>
sealed class UpdateToast : Form
{
    public static void Ask(Action onYes) => new UpdateToast(onYes).Show();

    readonly Pen border = new(UI.Line);
    readonly List<Font> fonts = new();

    UpdateToast(Action onYes)
    {
        float sc = DeviceDpi / 96f;
        int S(float v) => UI.Scale(v, sc);
        Font F(float size, FontStyle style = FontStyle.Regular) { var x = UI.Pretendard(size, style); fonts.Add(x); return x; }
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = UI.Back;
        AutoScaleMode = AutoScaleMode.None;
        Font = F(10f);
        int w = S(300), pad = S(16), iconSize = S(36);
        int textLeft = pad + iconSize + S(12);

        // 받기 아이콘: 연한 강조색 둥근 칸 안에 아래 화살표
        var icon = new Panel { Location = new Point(pad, pad), Size = new Size(iconSize, iconSize), BackColor = UI.Back };
        icon.Paint += (_, e) => DrawIcon(e.Graphics, icon.ClientRectangle);

        var title = new Label
        {
            Text = "새 버전이 나왔어요", Font = F(11f, FontStyle.Bold), ForeColor = UI.Text, UseMnemonic = false,
            AutoSize = true, Location = new Point(textLeft, pad - S(1)),
        };
        var sub = new Label
        {
            Text = "받으면 알아서 설치하고 다시 켜져요.", Font = F(9f), ForeColor = UI.Sub, UseMnemonic = false,
            AutoSize = true, MaximumSize = new Size(w - textLeft - pad, 0),
            Location = new Point(textLeft, title.Top + title.PreferredHeight + S(3)),
        };
        int y = Math.Max(icon.Bottom, sub.Top + sub.PreferredHeight) + S(14);

        RoundLabel Btn(string text, Color fore, Color back, Color hover, Action click)
        {
            var b = new RoundLabel
            {
                AutoSize = false, Size = new Size(S(84), S(32)), Radius = S(8), Text = text, Fill = back, ForeColor = fore,
                TextAlign = ContentAlignment.MiddleCenter, Font = F(9.5f, FontStyle.Bold), Cursor = Cursors.Hand, UseMnemonic = false,
            };
            b.MouseEnter += (_, _) => b.Fill = hover;
            b.MouseLeave += (_, _) => b.Fill = back;
            b.Click += (_, _) => click();
            return b;
        }
        // 다크 모드의 호박색 위에는 흰 글자보다 어두운 글자가 잘 보인다
        var yes = Btn("지금 받기", UI.Dark ? Color.FromArgb(38, 32, 26) : Color.White, UI.Accent,
            UI.Dark ? Color.FromArgb(248, 184, 120) : Color.FromArgb(240, 125, 0), () => { Close(); onYes(); });
        var no = Btn("나중에", UI.Text, UI.HoverBack, UI.Dark ? Color.FromArgb(76, 82, 90) : Color.FromArgb(232, 232, 232), Close);
        no.Location = new Point(w - pad - no.Width, y);
        yes.Location = new Point(no.Left - S(8) - yes.Width, y);
        ClientSize = new Size(w, y + no.Height + pad);

        Controls.Add(icon);
        Controls.Add(title);
        Controls.Add(sub);
        Controls.Add(yes);
        Controls.Add(no);

        var wa = Screen.PrimaryScreen!.WorkingArea;
        Location = new Point(wa.Right - Width - S(16), wa.Bottom - Height - S(16));
        Paint += (_, e) => e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        HandleCreated += (_, _) => UI.RoundCorners(Handle);
    }

    static void DrawIcon(Graphics g, Rectangle r)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var box = new RectangleF(0, 0, r.Width - 1, r.Height - 1);
        using (var path = UI.RoundRect(box, box.Width * 0.28f))
        using (var b = new SolidBrush(UI.Dark ? Color.FromArgb(80, 86, 94) : UI.SoftAccent))
            g.FillPath(b, path);
        float cx = r.Width / 2f, top = r.Height * 0.26f, bot = r.Height * 0.6f, hw = r.Width * 0.17f, tray = r.Height * 0.74f;
        using var pen = new Pen(UI.AccentText, Math.Max(2f, r.Width / 13f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        g.DrawLine(pen, cx, top, cx, bot);                                                       // 세로 줄
        g.DrawLines(pen, new[] { new PointF(cx - hw, bot - hw), new PointF(cx, bot), new PointF(cx + hw, bot - hw) }); // 화살촉
        g.DrawLine(pen, r.Width * 0.3f, tray, r.Width * 0.7f, tray);                              // 받침
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { border.Dispose(); foreach (var f in fonts) f.Dispose(); }
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
