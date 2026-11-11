namespace MapleBar;

static class Program
{
    /// <summary>게임 해상도가 바뀌어 바를 새 크기로 다시 띄워야 할 때 true.</summary>
    public static bool RestartRequested;

    /// <summary>두 번째로 실행하면 이 신호가 켜진다 → 켜져 있던 바가 앞으로 나온다.</summary>
    const string ShowEventName = "MapleBar_Show";
    public static EventWaitHandle? ShowEvent;

    /// <summary>PC를 켤 때 자동으로 실행됐으면 true → 바를 띄우지 않고 아이콘으로만 시작한다.</summary>
    public static bool StartHidden = Environment.GetCommandLineArgs().Contains(AutoStart.Arg);

    [STAThread]
    static void Main()
    {
        // 하나만 실행되게 한다
        var mutex = new Mutex(true, "MapleBar_SingleInstance", out bool created);
        if (!created)
        {
            // 이미 켜져 있으면, 숨어 있던 바를 앞으로 불러오라고 알리고 끝낸다
            try { using var ev = EventWaitHandle.OpenExisting(ShowEventName); ev.Set(); } catch { }
            return;
        }
        ShowEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);

        ApplicationConfiguration.Initialize();
        // GitHub에 더 새 설치 파일이 올라와 있으면 업데이트해야 켤 수 있다
        if (!RestartRequested && !UpdateCheck.AllowStart()) { mutex.ReleaseMutex(); mutex.Dispose(); return; }
        ToolStripManager.Renderer = new RoundMenuRenderer(); // 둥근 메뉴
        Application.Run(new BarForm());

        // 새로 띄우는 쪽이 바로 실행될 수 있게 먼저 잠금을 푼다
        ShowEvent.Dispose();
        mutex.ReleaseMutex();
        mutex.Dispose();
        if (RestartRequested)
            System.Diagnostics.Process.Start(Application.ExecutablePath);
    }
}
