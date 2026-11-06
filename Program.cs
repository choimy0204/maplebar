namespace MapleBar;

static class Program
{
    [STAThread]
    static void Main()
    {
        // 하나만 실행되게 한다
        using var mutex = new Mutex(true, "MapleBar_SingleInstance", out bool created);
        if (!created) return;

        ApplicationConfiguration.Initialize();
        ToolStripManager.Renderer = new RoundMenuRenderer(); // 둥근 메뉴
        Application.Run(new BarForm());
    }
}
