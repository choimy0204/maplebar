namespace MapleBar;

/// <summary>바를 끄는 동안 게임 화면 가운데에 보이는 얇은 세로선.
/// 바가 가운데에 붙으면 주황, 아니면 연한 회색이다. 마우스를 막지 않고 포커스도 가져가지 않는다.</summary>
sealed class DragGuide : Form
{
    public DragGuide()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        BackColor = UI.Accent;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x08 /*TOPMOST*/ | 0x80 /*TOOLWINDOW*/ | 0x08000000 /*NOACTIVATE*/;
            return cp;
        }
    }

    /// <summary>x 줄에 top~bottom 길이로 선을 띄운다. on이면 가운데에 붙은 것</summary>
    public void Place(int x, int top, int bottom, int width, bool on)
    {
        var c = on ? UI.Accent : Color.FromArgb(160, 160, 170);
        if (BackColor != c) BackColor = c;
        var b = new Rectangle(x - width / 2, top, width, Math.Max(1, bottom - top));
        if (!Visible) { Bounds = b; Show(); }
        // 바보다 위에 보이게 맨 앞으로 (포커스는 가져가지 않는다)
        Native.SetWindowPos(Handle, new IntPtr(-1) /*HWND_TOPMOST*/, b.X, b.Y, b.Width, b.Height, 0x0010 /*NOACTIVATE*/ | 0x0040 /*SHOWWINDOW*/);
    }
}
