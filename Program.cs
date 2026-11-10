namespace MapleBar;

static class Program
{
    /// <summary>게임 해상도가 바뀌어 바를 새 크기로 다시 띄워야 할 때 true.</summary>
    public static bool RestartRequested;

    [STAThread]
    static void Main()
    {
        // 하나만 실행되게 한다
        var mutex = new Mutex(true, "MapleBar_SingleInstance", out bool created);
        if (!created) return;

        ApplicationConfiguration.Initialize();
        // GitHub에 더 새 설치 파일이 올라와 있으면 업데이트해야 켤 수 있다
        if (!RestartRequested && !UpdateCheck.AllowStart()) { mutex.ReleaseMutex(); mutex.Dispose(); return; }
        ToolStripManager.Renderer = new RoundMenuRenderer(); // 둥근 메뉴
        Application.Run(new BarForm());

        // 새로 띄우는 쪽이 바로 실행될 수 있게 먼저 잠금을 푼다
        mutex.ReleaseMutex();
        mutex.Dispose();
        if (RestartRequested)
            System.Diagnostics.Process.Start(Application.ExecutablePath);
    }
}
