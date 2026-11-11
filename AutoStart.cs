using System.Diagnostics;
using System.Security;
using System.Security.Principal;
using System.Text;

namespace MapleBar;

/// <summary>
/// PC를 켤 때(로그인할 때) 메이플 바를 아이콘으로만 띄운다.
/// 메이플 바는 관리자 권한으로 돌아서, 시작 프로그램 폴더에 넣으면 윈도우가 막는다.
/// 그래서 작업 스케줄러에 "로그인하면 관리자 권한으로 실행" 작업을 등록한다.
/// </summary>
static class AutoStart
{
    const string TaskName = "MapleBar_AutoStart";
    public const string Arg = "--startup";

    /// <summary>등록돼 있는지 (켤 때 한 번 확인하고, 바꿀 때마다 맞춘다)</summary>
    static volatile bool on; // 백그라운드 스레드에서 바꾸고 화면 스레드에서 읽는다
    public static bool On { get => on; private set => on = value; }

    static string Exe => Environment.ProcessPath ?? Application.ExecutablePath;

    // 예전 설치 파일이 만들던 시작 프로그램 바로가기 (관리자 권한 프로그램이라 실제로는 실행이 막혔다)
    static string OldShortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "메이플 바.lnk");

    /// <summary>켤 때 뒤에서 한 번: 상태를 읽고, 프로그램 위치가 바뀌었으면 고쳐 등록한다.</summary>
    public static void Init()
    {
        Task.Run(() =>
        {
            try
            {
                var (code, xml) = Run("/Query", "/TN", TaskName, "/XML");
                On = code == 0;
                bool oldLink = File.Exists(OldShortcut);
                // 예전 바로가기로 자동 실행을 골랐던 사람은 작업으로 옮겨 준다
                if (oldLink && !On) On = Register();
                else if (On && !xml.Contains(SecurityElement.Escape(Exe), StringComparison.OrdinalIgnoreCase)) Register();
                if (oldLink && On) File.Delete(OldShortcut);
            }
            catch { }
        });
    }

    /// <summary>켜거나 끈다. 실패하면 false.</summary>
    public static bool Set(bool on)
    {
        try
        {
            bool ok = on ? Register() : Run("/Delete", "/TN", TaskName, "/F").code == 0 || !Exists();
            if (ok)
            {
                On = on;
                if (File.Exists(OldShortcut)) File.Delete(OldShortcut);
            }
            return ok;
        }
        catch { return false; }
    }

    static bool Exists() => Run("/Query", "/TN", TaskName).code == 0;

    static bool Register()
    {
        string user;
        using (var me = WindowsIdentity.GetCurrent()) user = me.Name;
        string xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>PC를 켜면 메이플 바를 화면 오른쪽 아래 아이콘으로 켭니다</Description></RegistrationInfo>
              <Triggers>
                <LogonTrigger><Enabled>true</Enabled><UserId>{SecurityElement.Escape(user)}</UserId><Delay>PT5S</Delay></LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{SecurityElement.Escape(user)}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>4</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec><Command>{SecurityElement.Escape(Exe)}</Command><Arguments>{Arg}</Arguments></Exec>
              </Actions>
            </Task>
            """;
        string file = Path.Combine(Path.GetTempPath(), "MapleBar_AutoStart.xml");
        try
        {
            File.WriteAllText(file, xml, Encoding.Unicode);
            return Run("/Create", "/TN", TaskName, "/XML", file, "/F").code == 0;
        }
        finally { try { File.Delete(file); } catch { } }
    }

    static (int code, string output) Run(params string[] args)
    {
        var psi = new ProcessStartInfo("schtasks.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var errTask = p.StandardError.ReadToEndAsync();
        // 출력도 따로 읽어서, schtasks가 멈춰도 10초 뒤에는 돌아온다
        var outTask = p.StandardOutput.ReadToEndAsync();
        if (!p.WaitForExit(10000)) { try { p.Kill(); } catch { } outTask.Wait(1000); return (-1, Done(outTask)); }
        outTask.Wait(5000);
        errTask.Wait(5000);
        return (p.ExitCode, Done(outTask));
        static string Done(Task<string> t) => t.IsCompletedSuccessfully ? t.Result : "";
    }
}
