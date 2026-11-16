using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MapleBarSetup;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // --check: 창 없이 최신 설치 파일을 받아 확인만 하고 지운다 (설치하지 않는다). 0이면 성공
        if (args.Length == 1 && args[0] == "--check")
        {
            try
            {
                using var d = Task.Run(() => Getter.DownloadAsync(null, CancellationToken.None)).GetAwaiter().GetResult();
                Console.WriteLine("OK " + d.Version);
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL " + Getter.Message(ex) + " (" + ex.GetType().Name + ")");
                return 1;
            }
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new SetupForm());
        return 0;
    }
}
