using System.Collections.Concurrent;
using System.Runtime.InteropServices;

using static MapleBar.Native;

namespace MapleBar;

/// <summary>
/// 키를 누를 때마다 윈도우가 알려 주는 것을 받아 차례대로 쌓아 둔다.
/// 몇 번씩 "지금 눌려 있나"를 들여다보는 방식은 프로그램이 잠깐 바쁘면 짧은 누름을 놓친다.
/// 읽기만 한다: 키를 막거나 바꾸거나 게임에 보내지 않는다.
/// </summary>
static class KeyHook
{
    public readonly record struct Press(int Vk, DateTime At, uint Pid);

    static readonly ConcurrentQueue<Press> queue = new();
    static readonly bool[] down = new bool[256];
    static IntPtr hook;
    static HookProc? proc; // 가비지 수집으로 사라지지 않게 잡아 둔다

    /// <summary>윈도우에서 키 알림을 받고 있는지. 아니면 예전처럼 들여다보는 방식을 쓴다.</summary>
    public static bool Active { get; private set; }

    public static void Start()
    {
        if (Active) return;
        using var ready = new ManualResetEventSlim();
        var t = new Thread(() =>
        {
            proc = Callback;
            hook = SetWindowsHookEx(13 /* WH_KEYBOARD_LL */, proc, GetModuleHandle(null), 0);
            Active = hook != IntPtr.Zero;
            ready.Set();
            if (!Active) return;
            // 알림은 이 스레드의 메시지 줄로 온다. 화면 스레드가 바빠도 여기서는 바로 받는다
            while (GetMessage(out _, IntPtr.Zero, 0, 0) > 0) { }
            UnhookWindowsHookEx(hook);
            Active = false;
        })
        { IsBackground = true, Name = "KeyHook", Priority = ThreadPriority.AboveNormal };
        t.Start();
        ready.Wait(2000);
    }

    /// <summary>쌓인 누름을 순서대로 꺼낸다.</summary>
    public static List<Press> Drain()
    {
        var list = new List<Press>();
        while (queue.TryDequeue(out var p)) list.Add(p);
        return list;
    }

    static IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            int msg = (int)wParam;
            int vk = Marshal.ReadInt32(lParam) & 0xFF;
            if (msg == 0x100 || msg == 0x104) // 누름
            {
                // 꾹 누르고 있을 때 반복해서 오는 것은 한 번으로 센다
                if (!down[vk])
                {
                    down[vk] = true;
                    GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
                    if (queue.Count < 512) queue.Enqueue(new Press(vk, DateTime.Now, pid));
                }
            }
            else if (msg == 0x101 || msg == 0x105) down[vk] = false; // 뗌
        }
        return CallNextHookEx(hook, code, wParam, lParam);
    }

    delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }

    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, HookProc fn, IntPtr mod, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? name);
}
