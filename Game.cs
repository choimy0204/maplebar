using System.Diagnostics;
using System.Runtime.InteropServices;

using static MapleBar.Native;

namespace MapleBar;

/// <summary>메이플스토리 게임 창의 화면 영역(테두리·제목줄을 뺀 게임 화면)을 찾는다.
/// 게임 프로세스를 열거나 건드리지 않고, 창 위치와 크기만 읽는다.</summary>
static class Game
{
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }

    /// <summary>지금 켜져 있는 메이플 프로세스 번호들.</summary>
    static HashSet<uint> MaplePids()
    {
        var set = new HashSet<uint>();
        foreach (var p in Process.GetProcessesByName("MapleStory")) { set.Add((uint)p.Id); p.Dispose(); }
        return set;
    }

    /// <summary>게임 화면 영역. 게임이 꺼져 있거나 최소화되어 있으면 null.</summary>
    public static Rectangle? Area()
    {
        var pids = MaplePids();
        if (pids.Count == 0) return null;

        Rectangle? best = null;
        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out uint pid);
            if (!pids.Contains(pid) || !IsWindowVisible(h) || IsIconic(h)) return true;
            if (!GetClientRect(h, out var r)) return true;
            var pt = new POINT();
            if (!ClientToScreen(h, ref pt)) return true;
            var rect = new Rectangle(pt.X, pt.Y, r.R - r.L, r.B - r.T);
            // 로딩 창 같은 작은 창은 건너뛰고 가장 큰 창을 게임 화면으로 본다
            if (rect.Width >= 640 && rect.Height >= 400 && (best == null || rect.Width * rect.Height > best.Value.Width * best.Value.Height))
                best = rect;
            return true;
        }, IntPtr.Zero);
        return best;
    }

    static HashSet<uint> fgPids = new();
    static DateTime fgPidsAt;

    /// <summary>지금 메이플 창을 쓰고 있는지 (다른 창에서 친 글자는 세지 않으려고).</summary>
    public static bool Foreground()
    {
        if ((DateTime.Now - fgPidsAt).TotalSeconds > 2)
        {
            fgPidsAt = DateTime.Now;
            fgPids = MaplePids();
        }
        if (fgPids.Count == 0) return false;
        GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
        return fgPids.Contains(pid);
    }

    /// <summary>이 프로세스가 메이플인지. 모르는 번호면(게임을 막 다시 켰을 수 있다) 목록을 바로 새로 읽는다.</summary>
    public static bool IsGame(uint pid)
    {
        if (pid == 0) return false;
        if (fgPids.Contains(pid)) return true;
        if ((DateTime.Now - fgPidsAt).TotalSeconds < 0.5) return false;
        fgPidsAt = DateTime.Now;
        var set = MaplePids();
        fgPids = set;
        return set.Contains(pid);
    }

    static bool running;
    static DateTime runningAt;

    /// <summary>메이플스토리 프로세스가 있는지 (최소화돼 있어도 켜진 것으로 본다). 1초마다 다시 본다.</summary>
    public static bool Running()
    {
        if ((DateTime.Now - runningAt).TotalSeconds >= 1)
        {
            runningAt = DateTime.Now;
            var ps = Process.GetProcessesByName("MapleStory");
            running = ps.Length > 0;
            foreach (var p in ps) p.Dispose();
        }
        return running;
    }

    /// <summary>게임 가로 1920일 때 1배. 바 크기와 글꼴에 곱한다.</summary>
    public static float ZoomFor(Rectangle game, int dpi) =>
        UI.Balance(game.Width / 1920f / (dpi / 96f));
}
