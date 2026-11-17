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
    /// <summary>Combo: 누를 때 조합 키(ComboVk)가 눌려 있었는지. Mods: 누를 때 눌려 있던 Shift(1)·Ctrl(2)·Alt(4)</summary>
    public readonly record struct Press(int Vk, DateTime At, uint Pid, bool Combo, int Mods = 0);

    /// <summary>Shift·Ctrl·Alt 키 코드를 Press.Mods의 칸으로 바꾼다 (그 밖의 키는 0)</summary>
    public static int ModBit(int vk) => vk switch { 16 => 1, 17 => 2, 18 => 4, _ => 0 };

    /// <summary>NumLock이 꺼져 있거나 Shift를 누른 채로 넘패드를 누르면 윈도우가 PageUp·화살표 같은 키로 바꿔 보낸다.
    /// 확장 키가 아닌 이 키들은 넘패드에서 온 것이므로 원래 넘패드 키를 돌려준다 (해당 없으면 0)</summary>
    public static int NumpadOf(int vk) => vk switch
    {
        0x21 => 0x69, 0x22 => 0x63, 0x23 => 0x61, 0x24 => 0x67,
        0x25 => 0x64, 0x26 => 0x68, 0x27 => 0x66, 0x28 => 0x62,
        0x2D => 0x60, 0x2E => 0x6E, 0x0C => 0x65,
        _ => 0,
    };

    /// <summary>조합 키 (설정 값, 0이면 없음). 알림 쪽이 바꿔 준다</summary>
    public static volatile int ComboVk;

    // Shift·Ctrl·Alt는 왼쪽·오른쪽 키로 따로 오므로 둘 중 하나라도 눌려 있으면 눌린 것으로 본다
    static bool IsDown(int vk) => vk switch
    {
        0 => false,
        16 => down[16] || down[160] || down[161],
        17 => down[17] || down[162] || down[163],
        18 => down[18] || down[164] || down[165],
        _ => vk > 0 && vk < 256 && down[vk],
    };

    /// <summary>쌓아 둘 키 (설정에 등록된 키만). 나머지 키는 눌렀는지만 보고 바로 버린다</summary>
    static volatile bool[] watched = new bool[256];

    /// <summary>쌓아 둘 키를 정한다. 바뀐 게 없으면 그대로 둔다</summary>
    public static void Watch(IEnumerable<int> keys)
    {
        var next = new bool[256];
        foreach (var k in keys)
        {
            if (k <= 0 || k >= 256) continue;
            next[k] = true;
            // 훅은 왼쪽·오른쪽 Shift·Ctrl·Alt를 따로 알려 준다
            if (k == 16) next[160] = next[161] = true;
            else if (k == 17) next[162] = next[163] = true;
            else if (k == 18) next[164] = next[165] = true;
        }
        if (!next.AsSpan().SequenceEqual(watched)) watched = next;
    }

    static readonly ConcurrentQueue<Press> queue = new();
    static readonly bool[] down = new bool[256];
    static IntPtr hook;
    static HookProc? proc; // 가비지 수집으로 사라지지 않게 잡아 둔다

    /// <summary>윈도우에서 키 알림을 받고 있는지. 아니면 예전처럼 들여다보는 방식을 쓴다.</summary>
    static volatile bool active; // 키 스레드에서 바꾸고 화면 스레드에서 읽는다
    public static bool Active { get => active; private set => active = value; }

    static Thread? starting;
    static volatile uint hookThread; // 훅 스레드 번호 (끝낼 때 그 스레드에 그만하라고 보낸다)
    static volatile bool stopping;   // 앱이 끝나는 중이면 훅을 새로 걸지 않는다
    static bool exitHooked;

    public static void Start()
    {
        if (Active || stopping) return;
        if (starting is { IsAlive: true }) return; // 앞서 켠 것이 아직 준비 중이면 하나 더 걸지 않는다
        if (!exitHooked) { exitHooked = true; AppDomain.CurrentDomain.ProcessExit += (_, _) => Stop(); } // 앱이 끝날 때 훅을 푼다
        using var ready = new ManualResetEventSlim();
        var t = starting = new Thread(() =>
        {
            hookThread = GetCurrentThreadId();
            proc = Callback;
            hook = SetWindowsHookEx(13 /* WH_KEYBOARD_LL */, proc, GetModuleHandle(null), 0);
            Active = hook != IntPtr.Zero;
            // 2초 안에 못 오면 부른 쪽은 이미 기다림을 끝내고 ready를 치웠다
            try { ready.Set(); } catch (ObjectDisposedException) { }
            if (!Active) return;
            // 알림은 이 스레드의 메시지 줄로 온다. 화면 스레드가 바빠도 여기서는 바로 받는다
            // (Stop이 그만하라는 신호를 보내면 GetMessage가 0을 돌려줘 빠져나온다)
            if (!stopping) while (GetMessage(out _, IntPtr.Zero, 0, 0) > 0) { }
            UnhookWindowsHookEx(hook);
            Active = false;
        })
        { IsBackground = true, Name = "KeyHook", Priority = ThreadPriority.AboveNormal };
        t.Start();
        ready.Wait(2000);
    }

    /// <summary>앱이 끝날 때 키 훅을 푼다. 훅 스레드가 스스로 풀게 그만하라는 신호만 보내고 잠깐 기다린다.
    /// 어떤 순서로 불려도, 여러 번 불려도 오류를 내지 않는다.</summary>
    static void Stop()
    {
        try
        {
            stopping = true;
            if (hookThread != 0) PostThreadMessage(hookThread, 0x0012 /* WM_QUIT */, IntPtr.Zero, IntPtr.Zero);
            if (starting is { IsAlive: true } t && t != Thread.CurrentThread) t.Join(300);
        }
        catch { } // 끝나는 중이라 무엇이 이미 사라졌어도 그냥 넘어간다 (풀리지 않은 훅은 윈도우가 정리한다)
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
        try // 여기서 오류가 나면 앱이 꺼지므로 무슨 일이 있어도 넘긴다
        {
            int msg = (int)wParam;
            int vk = Marshal.ReadInt32(lParam) & 0xFF;
            bool ext = (Marshal.ReadInt32(lParam, 8) & 0x01) != 0; // 확장 키 표시
            // Shift를 누른 채 넘패드를 누르면 윈도우가 Shift를 잠깐 뗐다 다시 누른 것처럼 보낸다 (확장 표시가 붙어 온다).
            // 실제로 뗀 게 아니므로 무시해야 Shift가 계속 눌린 것으로 본다
            if (ext && vk is 16 or 160 or 161) return CallNextHookEx(hook, code, wParam, lParam);
            int pad = ext ? 0 : NumpadOf(vk);
            // 넘패드로 눌렀다가 Shift를 먼저 떼면 넘패드 키로 떼어질 수 있어 둘 다 푼다
            if ((msg == 0x101 || msg == 0x105) && pad != 0) down[pad] = false;
            // 넘패드에서 온 이동 키는 그 넘패드 키를 지켜보고 있으면 넘패드 키로 센다
            if (pad != 0 && watched[pad]) vk = pad;
            if (msg == 0x100 || msg == 0x104) // 누름
            {
                // 꾹 누르고 있을 때 반복해서 오는 것은 한 번으로 센다
                if (!down[vk])
                {
                    bool combo = IsDown(ComboVk); // 이 키를 누르기 전에 조합 키가 눌려 있었는지
                    int mods = (IsDown(16) ? 1 : 0) | (IsDown(17) ? 2 : 0) | (IsDown(18) ? 4 : 0);
                    down[vk] = true;
                    if (!watched[vk]) return CallNextHookEx(hook, code, wParam, lParam); // 설정에 없는 키는 쌓지 않는다
                    GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
                    if (queue.Count < 512) queue.Enqueue(new Press(vk, DateTime.Now, pid, combo, mods));
                }
            }
            else if (msg == 0x101 || msg == 0x105) down[vk] = false; // 뗌
        }
        catch { }
        return CallNextHookEx(hook, code, wParam, lParam);
    }

    delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }

    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, HookProc fn, IntPtr mod, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] static extern bool PostThreadMessage(uint thread, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? name);
}
