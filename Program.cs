namespace MapleBar;

static class Program
{
    /// <summary>게임 해상도가 바뀌어 바를 새 크기로 다시 띄워야 할 때 true.</summary>
    public static bool RestartRequested;
    /// <summary>해상도 때문에 바를 한 번 이상 다시 만들었으면 true (처음 켤 때만 할 일을 건너뛴다).</summary>
    public static bool Rebuilt;

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
            try { using var ev = EventWaitHandle.OpenExisting(ShowEventName); ev.Set(); }
            catch (Exception ex) { Log.Write("시작", "켜져 있는 바에 신호를 못 보냄", ex); }
            return;
        }
        ShowEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);

        ApplicationConfiguration.Initialize();
        // GitHub에 더 새 설치 파일이 올라와 있으면 업데이트해야 켤 수 있다
        if (!RestartRequested && !UpdateCheck.AllowStart()) { mutex.ReleaseMutex(); mutex.Dispose(); return; }
        ToolStripManager.Renderer = new RoundMenuRenderer(); // 둥근 메뉴
        // 해상도가 바뀌면 바 창만 새 크기로 다시 만든다. 앱은 계속 켜져 있어 음악·열린 창이 그대로 남는다
        // (Application.Run을 끝냈다 다시 부르면 그때 다른 창이 모두 없어지므로, 실행은 한 번만 하고 바만 갈아 끼운다)
        Presence.Start(); // 하루 사용자 세기 (하루 한 번 신호만 보낸다)
        Application.Run(new BarContext());
        Presence.Stop();

        ShowEvent.Dispose();
        mutex.ReleaseMutex();
        mutex.Dispose();
    }

    /// <summary>바가 닫힐 때 다시 만들라는 신호가 있으면 새 바로 갈아 끼우고, 아니면 앱을 끝낸다.</summary>
    sealed class BarContext : ApplicationContext
    {
        public BarContext() : base(new BarForm()) { }

        protected override void OnMainFormClosed(object? sender, EventArgs e)
        {
            if (!RestartRequested) { base.OnMainFormClosed(sender, e); return; }
            RestartRequested = false;
            Rebuilt = true;
            var bar = new BarForm();
            MainForm = bar;
            bar.Show();
        }
    }
}
