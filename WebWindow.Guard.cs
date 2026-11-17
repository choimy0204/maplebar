using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MapleBar;

/// <summary>
/// 가짜 로그인 화면(피싱) 막기.
/// 주소 칸이 없는 창은 제목 줄에 지금 사이트 주소를 늘 보여 주고 (제목은 페이지가 마음대로 정할 수 있다),
/// 로그인 창·로그인 팝업은 그 로그인 서비스의 도메인 밖으로 가지 못하게 한다.
/// </summary>
public partial class WebWindow
{
    Font? hostFont;
    string? guardNote;                       // 막았을 때 잠깐 보여 줄 말
    System.Windows.Forms.Timer? guardNoteTimer;

    /// <summary>제목 줄에 보여 줄 사이트 주소 (예: nxlogin.nexon.com). 웹 주소가 아니면 null</summary>
    static string? HostLabel(Uri? u)
    {
        if (u == null || (u.Scheme != Uri.UriSchemeHttps && u.Scheme != Uri.UriSchemeHttp) || u.Host.Length == 0) return null;
        var h = u.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? u.Host[4..] : u.Host;
        return u.Scheme == Uri.UriSchemeHttp ? "보안 연결 아님 · " + h : h;
    }

    /// <summary>제목 줄 오른쪽(버튼 앞)에 지금 사이트 주소를 그린다. 제목이 쓸 수 있는 오른쪽 끝을 돌려준다.
    /// 주소 칸이 있는 브라우저 창은 이미 주소가 보이므로 그리지 않는다.</summary>
    int DrawHost(Graphics g, int left, int right, int height)
    {
        string? text = guardNote ?? (browser ? null : HostLabel(web.Source));
        if (string.IsNullOrEmpty(text) || right <= left) return right;
        hostFont ??= Own(new Font("맑은 고딕", 8.5f));
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.Right | TextFormatFlags.PathEllipsis;
        int w = TextRenderer.MeasureText(g, text, hostFont, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width + S(2);
        // 주소가 우선: 자리가 모자라면 제목을 줄인다 (그래도 모자라면 주소 앞부분을 줄인다)
        w = Math.Min(w, right - left);
        var col = guardNote != null || text.StartsWith("보안", StringComparison.Ordinal) ? UI.Accent : UI.Sub;
        TextRenderer.DrawText(g, text, hostFont, new Rectangle(right - w, 0, w, height), col, flags);
        return right - w - S(10);
    }

    T Own<T>(T d) where T : IDisposable { owned.Add(d); return d; }

    /// <summary>InitAsync에서 처음 주소로 가기 전에 한 번 부른다.</summary>
    void GuardNavigation(CoreWebView2 cw)
    {
        web.SourceChanged += (_, _) => grip.Invalidate(); // 주소가 바뀌면 제목 줄의 주소도 바꾼다
        cw.NavigationStarting += (_, e) =>
        {
            // 웹 주소가 아닌 곳(다른 프로그램을 여는 주소, 파일 등)으로는 어느 창에서도 가지 않는다
            if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var u)) return;
            if (u.Scheme != Uri.UriSchemeHttps && u.Scheme != Uri.UriSchemeHttp && u.Scheme != "about" && u.Scheme != "data" && u.Scheme != "blob")
            {
                e.Cancel = true;
                Log.Write("web", "웹 주소가 아닌 이동을 막음: " + u.Scheme);
                return;
            }
            // 앱이 로그인 화면 주소로 연 창만 지킨다 (창을 다른 주소로 다시 쓰면 그때 주소로 다시 본다)
            var stay = Logins.StayDomainsFor(startUrl);
            if (stay == null) return;
            if (u.Scheme == "about" || Logins.MayStay(u, stay)) return;
            e.Cancel = true;
            Blocked(u, e.IsUserInitiated);
        };
    }

    /// <summary>개발자 도구(F12)는 테스트 빌드에서만 연다 (사이트 창·로그인 팝업 공통)</summary>
    static void NoDevTools(CoreWebView2 cw)
    {
#if !DEBUG
        cw.Settings.AreDevToolsEnabled = false;
#endif
    }

    /// <summary>사이트 창에서는 파일을 받지 않는다. 메이플 바는 관리자 권한으로 돌아서, 여기서 받은 파일을 바로 열면
    /// 그 파일도 관리자 권한으로 실행될 수 있다. 웹 주소면 평소 쓰는 브라우저로 넘겨 거기서 받게 한다.</summary>
    static void NoDownloads(CoreWebView2 cw)
    {
        cw.DownloadStarting += (_, e) =>
        {
            e.Cancel = true;
            string? url = null;
            try { url = e.DownloadOperation.Uri; } catch { }
            Log.Write("web", "사이트 창에서 파일 받기를 막음");
            if (url != null && Hosts.IsWeb(url)) OpenExternal(url);
        };
    }

    /// <summary>로그인 서비스 밖으로 가는 이동을 막았을 때: 사용자가 누른 링크면 주소가 보이는 기본 브라우저로 연다</summary>
    void Blocked(Uri u, bool userClicked)
    {
        Log.Write("web", "로그인 창에서 다른 사이트로 가는 걸 막음: " + u.Host);
        bool opened = userClicked && Hosts.IsWeb(u.AbsoluteUri);
        if (opened) OpenExternal(u.AbsoluteUri);
        ShowGuardNote(opened ? "브라우저로 열었어요" : "다른 사이트로는 못 가요");
    }

    void ShowGuardNote(string note)
    {
        if (IsDisposed) return;
        guardNote = note;
        if (guardNoteTimer == null)
        {
            guardNoteTimer = Own(new System.Windows.Forms.Timer { Interval = 3000 });
            guardNoteTimer.Tick += (_, _) => { guardNoteTimer.Stop(); guardNote = null; if (!IsDisposed) grip.Invalidate(); };
        }
        guardNoteTimer.Stop(); guardNoteTimer.Start();
        grip.Invalidate();
    }

    /// <summary>구글·애플 로그인 팝업 창을 만든다. 위에 지금 사이트 주소를 늘 보여 주고,
    /// 로그인 사이트와 이 팝업을 연 사이트 밖으로는 가지 못하게 한다.</summary>
    async Task<CoreWebView2> OpenLoginPopup(Uri? opener)
    {
        var pop = new Form { Text = "로그인", Size = new Size(S(480), S(640)), StartPosition = FormStartPosition.CenterScreen, ShowInTaskbar = false, BackColor = UI.Back };
        var font = new Font("맑은 고딕", 9f);
        pop.FormClosed += (_, _) => font.Dispose();
        string? note = null;
        var strip = new Panel { Dock = DockStyle.Top, Height = S(26), BackColor = UI.Back };
        var pw = new WebView2 { Dock = DockStyle.Fill };
        strip.Paint += (_, e) =>
        {
            var text = note ?? HostLabel(pw.Source) ?? "";
            var col = note != null || text.StartsWith("보안", StringComparison.Ordinal) ? UI.Accent : UI.Sub;
            TextRenderer.DrawText(e.Graphics, text, font, new Rectangle(S(10), 0, strip.Width - S(20), strip.Height), col,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.PathEllipsis);
        };
        pop.Controls.Add(pw);
        pop.Controls.Add(strip);
        pop.Show();
        CoreWebView2 cw;
        try
        {
            await pw.EnsureCoreWebView2Async(await Env);
            cw = pw.CoreWebView2 ?? throw new InvalidOperationException("로그인 팝업 웹뷰가 없음");
        }
        catch (Exception ex)
        {
            // 빈 창이 남지 않게 닫는다. 부른 쪽(새 창 요청)은 지금처럼 실패로 처리한다
            Log.Write("web", "로그인 팝업을 띄우지 못함", ex);
            if (!pop.IsDisposed) pop.Close();
            throw;
        }
        cw.WindowCloseRequested += (_, _) => pop.Close();
        pw.SourceChanged += (_, _) => strip.Invalidate();

        // 머물러도 되는 곳: 로그인 사이트 + 팝업을 연 사이트 주소 그대로 (로그인이 끝나면 그 사이트로 돌아간다)
        // (뒷부분만 맞추면 같은 끝을 쓰는 남의 사이트까지 열려 버린다)
        var stay = new List<string>(Logins.PopupDomains);
        if (opener != null && (opener.Scheme == Uri.UriSchemeHttps || opener.Scheme == Uri.UriSchemeHttp) && opener.Host.Length > 0) stay.Add(opener.Host);
        NoDevTools(cw);
        NoDownloads(cw);
        var noteTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        noteTimer.Tick += (_, _) => { noteTimer.Stop(); note = null; if (!strip.IsDisposed) strip.Invalidate(); };
        pop.FormClosed += (_, _) => noteTimer.Dispose();
        cw.NavigationStarting += (_, e) =>
        {
            if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var u) || u.Scheme == "about" || Logins.MayStay(u, stay)) return;
            e.Cancel = true;
            Log.Write("web", "로그인 팝업에서 다른 사이트로 가는 걸 막음: " + u.Host);
            note = "다른 사이트로는 못 가요";
            noteTimer.Stop(); noteTimer.Start();
            strip.Invalidate();
        };
        return cw;
    }

}
