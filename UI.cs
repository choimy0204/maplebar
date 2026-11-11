using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace MapleBar;

/// <summary>바와 사이트 창에서 같이 쓰는 작은 UI 도우미.</summary>
public static class UI
{
    /// <summary>모니터 해상도에 맞춘 배율. 가로 1920(배율 100% 기준)이면 1, 더 좁거나 넓으면 그만큼 줄이거나 키운다.
    /// 크기(S)와 글꼴 크기에 같이 곱한다.</summary>
    public static float Z = 1f;

    public static float FitZoom(Screen s, int dpi)
    {
        float logical = s.WorkingArea.Width / (dpi / 96f);
        return Balance(logical / 1920f);
    }

    /// <summary>
    /// 화면 폭 비율(1920 = 1)을 바·창 크기로 바꾼다. 1920보다 크면 그대로 키우고,
    /// 작으면 덜 줄인다: 메이플 UI는 해상도를 낮춰도 크기가 그대로라, 바만 정비례로 줄면 글자가 너무 작아진다.
    /// 1366 → 0.84 · 1280 → 0.82 · 1024 → 0.74 (예전엔 0.71 · 0.67 · 0.53)
    /// </summary>
    public static float Balance(float ratio) =>
        ratio >= 1f ? Math.Min(ratio, 1.5f) : Math.Max(0.7f, 0.45f + 0.55f * ratio);

    public static readonly Color Accent = Color.FromArgb(255, 138, 0);
    public static readonly Color Text = Color.FromArgb(31, 31, 31);
    public static readonly Color Line = Color.FromArgb(226, 226, 226);
    public static readonly Color HoverBack = Color.FromArgb(242, 242, 242);
    public static readonly Color SoftAccent = Color.FromArgb(255, 243, 224);

    static readonly ToolTip tip = new();

    /// <summary>둥근 알약 모양 버튼. 마우스를 올리면 배경이 바뀐다.</summary>
    public static RoundLabel Button(string text, string tooltip, Action onClick, float sc,
        Color? fore = null, Color? back = null)
    {
        var normal = back ?? Color.Transparent;
        var lbl = new RoundLabel
        {
            Text = text,
            AutoSize = true,
            UseMnemonic = false, // '&'를 그대로 보이게
            Cursor = Cursors.Hand,
            ForeColor = fore ?? Text,
            Fill = normal,
            Padding = new Padding((int)(10 * sc), (int)(5 * sc), (int)(10 * sc), (int)(5 * sc)),
            Margin = new Padding((int)(2 * sc), 0, (int)(2 * sc), 0),
        };
        lbl.MouseEnter += (_, _) => lbl.Fill = back.HasValue ? ControlPaint.Dark(back.Value, 0.05f) : HoverBack;
        lbl.MouseLeave += (_, _) => lbl.Fill = normal;
        lbl.Click += (_, _) => onClick();
        if (!string.IsNullOrEmpty(tooltip)) tip.SetToolTip(lbl, tooltip);
        return lbl;
    }

    /// <summary>FlowLayoutPanel 안의 컨트롤을 세로 가운데에 놓는다.</summary>
    public static void CenterV(Control c, int rowHeight)
    {
        var m = c.Margin;
        int h = c.AutoSize ? c.PreferredSize.Height : c.Height;
        c.Margin = new Padding(m.Left, Math.Max(0, (rowHeight - h) / 2), m.Right, 0);
    }

    public static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var p = new GraphicsPath();
        if (d <= 0) { p.AddRectangle(r); return p; }
        p.AddArc(r.Left, r.Top, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    // ── 윈도우 11 둥근 모서리 (창·펼침 메뉴) ──
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);

    public static void RoundCorners(IntPtr handle, bool small = false)
    {
        int pref = small ? 3 : 2; // 3 = 작게 둥글게, 2 = 둥글게
        try { DwmSetWindowAttribute(handle, 33, ref pref, sizeof(int)); } catch { }
    }

    /// <summary>펼침 메뉴(하위 메뉴 포함)를 둥근 모양으로 만든다.</summary>
    public static T Rounded<T>(T menu) where T : ToolStripDropDown
    {
        void Hook(ToolStripDropDown d)
        {
            if (d.Tag as string == "round") return;
            d.Tag = "round";
            d.Padding = new Padding(4, 6, 4, 6);
            if (d.IsHandleCreated) RoundCorners(d.Handle);
            d.HandleCreated += (_, _) => RoundCorners(d.Handle);
            d.Opening += (_, _) =>
            {
                foreach (var it in d.Items.OfType<ToolStripMenuItem>())
                {
                    it.Padding = new Padding(2, 3, 2, 3);
                    if (it.HasDropDownItems) Hook(it.DropDown);
                }
            };
        }
        Hook(menu);
        return menu;
    }
}

/// <summary>배경을 둥글게 칠하는 글자 버튼.</summary>
public class RoundLabel : Label
{
    Color fill = Color.Transparent;
    public Color Fill { get => fill; set { fill = value; Invalidate(); } }
    /// <summary>0이면 높이의 절반(알약 모양).</summary>
    public int Radius { get; set; }

    public RoundLabel()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        if (fill.A == 0) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = UI.RoundRect(new RectangleF(0, 0, Width - 0.5f, Height - 0.5f), Radius > 0 ? Radius : Height / 2f);
        using var b = new SolidBrush(fill);
        e.Graphics.FillPath(b, path);
    }
}

/// <summary>흰 바탕, 둥근 강조 칸, 주황 체크를 쓰는 메뉴 그리기.</summary>
public class RoundMenuRenderer : ToolStripProfessionalRenderer
{
    public RoundMenuRenderer() { RoundedEdges = true; }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) =>
        e.Graphics.Clear(Color.White);

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { } // 테두리는 윈도우가 둥글게 그린다

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(3, 1, e.Item.Width - 6, e.Item.Height - 2);
        using var path = UI.RoundRect(r, 7);
        using var b = new SolidBrush(UI.SoftAccent);
        e.Graphics.FillPath(b, path);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        if (e.Item.Enabled) e.TextColor = e.Item.Selected ? Color.FromArgb(200, 95, 0) : UI.Text;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        int y = e.Item.Height / 2;
        using var pen = new Pen(UI.Line);
        e.Graphics.DrawLine(pen, 10, y, e.Item.Width - 10, y);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var r = e.ImageRectangle;
        using var pen = new Pen(UI.Accent, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        e.Graphics.DrawLines(pen, new[]
        {
            new PointF(r.Left + r.Width * 0.2f, r.Top + r.Height * 0.52f),
            new PointF(r.Left + r.Width * 0.42f, r.Top + r.Height * 0.74f),
            new PointF(r.Left + r.Width * 0.82f, r.Top + r.Height * 0.3f),
        });
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = Color.FromArgb(140, 140, 140);
        base.OnRenderArrow(e);
    }
}
