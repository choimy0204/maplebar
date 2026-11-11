using System.Runtime.InteropServices;

namespace MapleBar;

/// <summary>여러 창에서 같이 쓰는 윈도우 함수와 번호. 각 파일은 `using static MapleBar.Native;`로 이름만 불러 쓴다.</summary>
static class Native
{
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr o);

    // SetWindowPos
    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010, SWP_NOOWNERZORDER = 0x0200;
    /// <summary>크기·위치·포커스는 그대로 두고 순서만 바꾼다.</summary>
    public const uint SWP_KEEP = SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE;

    // 창 모양
    public const int CS_DROPSHADOW = 0x20000;
    public const int WS_EX_TOPMOST = 0x08, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80,
        WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x08000000;
}
