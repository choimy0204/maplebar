namespace MapleBar;

/// <summary>설정 창들이 마지막으로 옮겨 둔 자리를 기억했다가 다음에 그 자리에 연다</summary>
static class WinPlace
{
    /// <summary>기억한 자리 (화면 밖이면 그 화면 안으로 당긴다). 없으면 바 아래 가운데</summary>
    public static Point Where(Settings st, string key, Form f, Form owner, float sc)
    {
        if (st.WinPos.TryGetValue(key, out var p) && p.Length == 2)
        {
            var r = new Rectangle(p[0], p[1], f.Width, f.Height);
            var wa = Screen.FromRectangle(r).WorkingArea;
            if (wa.IntersectsWith(r))
                return new Point(Math.Clamp(p[0], wa.Left, Math.Max(wa.Left, wa.Right - f.Width)),
                                 Math.Clamp(p[1], wa.Top, Math.Max(wa.Top, wa.Bottom - f.Height)));
        }
        var a = Screen.FromControl(owner).WorkingArea;
        return new Point(
            Math.Clamp(owner.Left + (owner.Width - f.Width) / 2, a.Left, Math.Max(a.Left, a.Right - f.Width)),
            Math.Clamp(owner.Bottom + (int)(12 * sc), a.Top, Math.Max(a.Top, a.Bottom - f.Height)));
    }

    /// <summary>지금 자리를 기억한다</summary>
    public static void Remember(Settings st, string key, Form f)
    {
        if (f.WindowState != FormWindowState.Normal) return;
        if (st.WinPos.TryGetValue(key, out var p) && p.Length == 2 && p[0] == f.Left && p[1] == f.Top) return;
        st.WinPos[key] = new[] { f.Left, f.Top };
        st.Save();
    }
}
