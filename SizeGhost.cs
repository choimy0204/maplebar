namespace MapleBar;

/// <summary>바 크기를 바꾸려고 끄는 동안, 놓으면 될 크기를 테두리와 배율 글자로 보여 준다.
/// 창을 빼앗지 않고 마우스도 받지 않는다.</summary>
sealed class SizeGhost : Form
{
    string text = "";
    readonly Font font = new("맑은 고딕", 11f, FontStyle.Bold);

    public SizeGhost()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = UI.Back;
        Opacity = 0.85;
        DoubleBuffered = true;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x08000000 /* WS_EX_NOACTIVATE */ | 0x80 /* WS_EX_TOOLWINDOW */ | 0x20 /* WS_EX_TRANSPARENT */;
            return cp;
        }
    }

    public void Place(Rectangle r, int percent)
    {
        string t = percent + "%";
        if (r == Bounds && t == text && Visible) return;
        text = t;
        Bounds = r;
        if (!Visible) Show();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        using var pen = new Pen(UI.Accent, 2);
        e.Graphics.DrawRectangle(pen, 1, 1, Width - 2, Height - 2);
        TextRenderer.DrawText(e.Graphics, text, font, ClientRectangle, UI.Accent,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) font.Dispose();
    }
}
