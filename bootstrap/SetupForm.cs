using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MapleBarSetup;

/// <summary>진행 상황을 보여 주는 작은 창. 실패하면 [다시 시도]를 띄운다</summary>
sealed class SetupForm : Form
{
    static readonly Color Accent = Color.FromArgb(217, 98, 43);
    static readonly Color Ink = Color.FromArgb(34, 34, 34);
    static readonly Color Muted = Color.FromArgb(110, 110, 110);

    readonly Label title = new() { AutoSize = false, Location = new Point(24, 20), Size = new Size(372, 28) };
    readonly Label status = new() { AutoSize = false, Location = new Point(24, 52), Size = new Size(372, 22) };
    readonly Bar bar = new() { Location = new Point(24, 84), Size = new Size(372, 8) };
    readonly RoundButton retry = new("다시 시도", true) { Location = new Point(220, 108), Size = new Size(84, 32) };
    readonly RoundButton close = new("닫기", false) { Location = new Point(312, 108), Size = new Size(84, 32) };
    readonly CancellationTokenSource cts = new();
    bool installing;

    public SetupForm()
    {
        Text = "메이플 바 설치";
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        Font = new Font("Malgun Gothic", 9.5f);
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(420, 156);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.White;

        title.Font = new Font("Malgun Gothic", 12f, FontStyle.Bold);
        title.ForeColor = Ink;
        status.ForeColor = Muted;
        retry.Visible = close.Visible = false;
        retry.Click += (_, _) => Start();
        close.Click += (_, _) => Close();
        Controls.AddRange(new Control[] { title, status, bar, retry, close });
        Shown += (_, _) => Start();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        cts.Cancel(); // 받는 중이면 멈춘다 (설치 중이면 설치 창은 그대로 끝까지 간다)
        base.OnFormClosing(e);
    }

    void Show(string text, int pct)
    {
        status.Text = text;
        bar.Value = pct;
    }

    async void Start()
    {
        if (installing) return;
        installing = true;
        retry.Visible = close.Visible = false;
        title.Text = "메이플 바를 설치할게요";
        try
        {
            using (var d = await Getter.DownloadAsync(Show, cts.Token))
            {
                title.Text = $"메이플 바 {d.Version} 설치";
                Show("설치하고 있어요…", 100);
                int code = await Getter.InstallAsync(d);
                if (code == 2 || code == 5) throw new Fail("설치를 취소했어요.");
                if (code != 0) throw new Fail("설치하지 못했어요. 다시 시도해 주세요.");
            }
            title.Text = "다 됐어요";
            if (Getter.StartBar())
            {
                Show("메이플 바를 켤게요.", 100);
                await Task.Delay(1500);
                Close();
                return;
            }
            Show("바탕화면이나 시작 메뉴에서 메이플 바를 켜 주세요.", 100);
            close.Visible = true;
        }
        catch (Exception ex)
        {
            if (cts.IsCancellationRequested || IsDisposed) return;
            title.Text = "설치하지 못했어요";
            Show(Getter.Message(ex), 0);
            retry.Visible = close.Visible = true;
        }
        finally { installing = false; }
    }

    /// <summary>둥근 막대 진행 표시</summary>
    sealed class Bar : Control
    {
        int value;
        public int Value { get => value; set { this.value = Math.Max(0, Math.Min(100, value)); Invalidate(); } }

        public Bar() => SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Color.White);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0, 0, Width - 1, Height - 1);
            using (var p = Round(r, 3)) using (var b = new SolidBrush(Color.FromArgb(236, 236, 236))) g.FillPath(b, p);
            if (value <= 0) return;
            var f = new RectangleF(0, 0, Math.Max(Height, (Width - 1) * value / 100f), Height - 1);
            using (var p = Round(f, 3)) using (var b = new SolidBrush(Accent)) g.FillPath(b, p);
        }
    }

    /// <summary>둥근 직사각형 버튼</summary>
    sealed class RoundButton : Control
    {
        readonly bool primary;
        bool hot;

        public RoundButton(string text, bool primary)
        {
            this.primary = primary;
            Text = text;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Color.White);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 2, Height - 2);
            var fill = primary ? (hot ? ControlPaint.Dark(Accent, 0.05f) : Accent) : (hot ? Color.FromArgb(236, 236, 236) : Color.FromArgb(244, 244, 244));
            using (var p = Round(r, 6)) using (var b = new SolidBrush(fill)) g.FillPath(b, p);
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, primary ? Color.White : Ink,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }

    static GraphicsPath Round(RectangleF r, float radius)
    {
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
