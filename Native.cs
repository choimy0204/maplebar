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

    // ── 제목줄 없는 창 끌기 ──
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    const int WM_NCLBUTTONDOWN = 0xA1, HTCAPTION = 2;

    /// <summary>제목줄을 잡은 것처럼 창을 끈다. 마우스를 놓을 때까지 돌아오지 않는다.</summary>
    public static void DragWindow(IntPtr hwnd)
    {
        ReleaseCapture();
        SendMessage(hwnd, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
    }

    // ── 반투명 가장자리까지 그대로 올리는 창 (WS_EX_LAYERED) ──
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int x, y; }
    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
    [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
    [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, IntPtr psize, IntPtr hdcSrc, IntPtr pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
    [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, IntPtr pptDst, IntPtr psize, IntPtr hdcSrc, IntPtr pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr o);

    static BLENDFUNCTION Blend(byte alpha) => new() { BlendOp = 0, SourceConstantAlpha = alpha, AlphaFormat = 1 };

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }
    [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);

    /// <summary>겹창에 자주 올리는 그림판: 윈도우 그림(DIB)을 한 번 만들어 두고 그 위에 바로 그린다.
    /// PushLayered처럼 올릴 때마다 그림을 새로 만들어 복사하지 않아 빠르다.</summary>
    public sealed class LayerSurface : IDisposable
    {
        readonly IntPtr dc, hb, old;
        public readonly Bitmap Bmp;
        public int Width => Bmp.Width;
        public int Height => Bmp.Height;

        public LayerSurface(int w, int h)
        {
            var bi = new BITMAPINFOHEADER { biSize = Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
            dc = CreateCompatibleDC(IntPtr.Zero);
            hb = CreateDIBSection(dc, ref bi, 0, out var bits, IntPtr.Zero, 0);
            old = SelectObject(dc, hb);
            Bmp = new Bitmap(w, h, w * 4, System.Drawing.Imaging.PixelFormat.Format32bppPArgb, bits);
        }

        public void Push(IntPtr hwnd, Point at)
        {
            var size = new SIZE { cx = Width, cy = Height };
            var src = new POINT();
            var dst = new POINT { x = at.X, y = at.Y };
            var blend = Blend(255);
            UpdateLayeredWindow(hwnd, IntPtr.Zero, ref dst, ref size, dc, ref src, 0, ref blend, 2);
        }

        public void Dispose()
        {
            Bmp.Dispose();
            SelectObject(dc, old);
            DeleteObject(hb);
            DeleteDC(dc);
        }
    }

    /// <summary>그림을 창에 올리고 at 자리에 놓는다.</summary>
    public static void PushLayered(IntPtr hwnd, Bitmap bmp, Point at, byte alpha = 255)
    {
        IntPtr screen = GetDC(IntPtr.Zero), mem = CreateCompatibleDC(screen), hb = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
            hb = bmp.GetHbitmap(Color.FromArgb(0));
            old = SelectObject(mem, hb);
            var size = new SIZE { cx = bmp.Width, cy = bmp.Height };
            var src = new POINT();
            var dst = new POINT { x = at.X, y = at.Y };
            var blend = Blend(alpha);
            UpdateLayeredWindow(hwnd, screen, ref dst, ref size, mem, ref src, 0, ref blend, 2);
        }
        finally
        {
            if (old != IntPtr.Zero) SelectObject(mem, old);
            if (hb != IntPtr.Zero) DeleteObject(hb);
            DeleteDC(mem);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    /// <summary>그림은 그대로 두고 자리만 옮긴다.</summary>
    public static void MoveLayered(IntPtr hwnd, Point at)
    {
        var dst = new POINT { x = at.X, y = at.Y };
        var blend = Blend(255);
        UpdateLayeredWindow(hwnd, IntPtr.Zero, ref dst, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, ref blend, 2);
    }

    /// <summary>그림은 그대로 두고 창 전체 투명도만 바꾼다.</summary>
    public static void SetLayeredAlpha(IntPtr hwnd, byte alpha)
    {
        var blend = Blend(alpha);
        UpdateLayeredWindow(hwnd, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, ref blend, 2);
    }
}
