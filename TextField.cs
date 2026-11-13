using System.Diagnostics.CodeAnalysis;
using System.Drawing.Drawing2D;

namespace MapleBar;

/// <summary>둥근 네모 글 칸. 평소엔 옅은 회색, 누르면 흰 바탕에 파란 테두리.
/// 안쪽은 테두리 없는 진짜 글 칸이라 커서·선택·붙여 넣기가 그대로 된다.</summary>
public sealed class TextField : Control
{
    readonly TextBox tb = new() { BorderStyle = BorderStyle.None, ShortcutsEnabled = true };
    public int Radius { get; set; } = 8;
    public Color Fill { get; set; } = UI.HoverBack;

    [AllowNull]
    public override string Text { get => tb.Text; set => tb.Text = value ?? ""; }
    public string PlaceholderText { get => tb.PlaceholderText; set => tb.PlaceholderText = value; }
    public int MaxLength { get => tb.MaxLength; set => tb.MaxLength = value; }
    public void SelectAll() => tb.SelectAll();

    public TextField()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.IBeam;
        tb.BackColor = Fill;
        Controls.Add(tb);
        tb.TextChanged += (_, e) => OnTextChanged(e);
        tb.Enter += (_, _) => Repaint();
        tb.Leave += (_, _) => Repaint();
        FitHeight();
    }

    void Repaint()
    {
        tb.BackColor = tb.Focused ? Color.White : Fill;
        Invalidate();
    }

    void FitHeight() => Height = tb.PreferredHeight + (int)Math.Round(12 * UI.Z);

    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); tb.Font = Font; FitHeight(); PerformLayout(); }
    protected override void OnForeColorChanged(EventArgs e) { base.OnForeColorChanged(e); tb.ForeColor = ForeColor; }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        int pad = (int)Math.Round(10 * UI.Z);
        tb.Width = Math.Max(8, Width - pad * 2);
        tb.Location = new Point(pad, (Height - tb.Height) / 2);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        bool focus = tb.Focused;
        using var path = UI.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Radius);
        using (var b = new SolidBrush(focus ? Color.White : Fill)) g.FillPath(b, path);
        if (focus) { using var pen = new Pen(UI.Accent, 1.5f); g.DrawPath(pen, path); }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        tb.Focus();
        tb.SelectionStart = e.X < tb.Left ? 0 : tb.TextLength;
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); tb.Focus(); }
}
