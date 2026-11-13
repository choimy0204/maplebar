using Microsoft.Web.WebView2.Core;

namespace MapleBar;

/// <summary>정령의 펜던트 확인: 고른 버프가 도는 동안 maple.gg/u/닉네임 장비에서 정령의 펜던트를 찾는다.
/// 1분마다 정보 갱신을 누르고 갱신이 끝나면 바로 다시 본다. 없으면 낄 때까지 20초마다 노랑(6시 방향에서 잠깐 파랑)으로 알린다.
/// 보이지 않는 소리 끈 웹 화면으로 읽기만 한다. 게임에는 아무것도 보내지 않는다.</summary>
sealed class PendantCheck : IDisposable
{
    public static readonly Color Blue = Color.FromArgb(40, 140, 255);
    // 255,200,0은 화면 위에 겹치면 주황으로 보여서 레몬빛 노랑을 쓴다
    public static readonly Color Yellow = Color.FromArgb(255, 240, 0);
    /// <summary>설정 창 상태 줄에 보이는 글</summary>
    public static string Status { get; private set; } = Idle;
    const string Idle = "버프가 돌면 확인해요";

    static readonly TimeSpan AlertGap = TimeSpan.FromSeconds(20);    // 안 꼈을 때 알림 주기
    static readonly TimeSpan CheckGap = TimeSpan.FromMinutes(1);     // 1분마다 정보 갱신을 누르고 다시 본다
    static readonly TimeSpan SlowGap = TimeSpan.FromMinutes(3);      // 없는 닉네임·사람 확인일 때 다시 보는 주기
    static readonly TimeSpan RefreshGap = TimeSpan.FromSeconds(55);  // 정보 갱신은 이보다 자주 누르지 않는다

    readonly Settings st;
    readonly JanusWatch watch;
    readonly float sc;
    readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    Form? host;                       // 화면에 띄우지 않는 웹 화면 받침
    CoreWebView2Controller? ctl;
    bool busy, missing, disposed;
    bool quiet;                       // "착용 완료"를 눌렀다: 알림만 멈추고 확인은 계속한다 (maple.gg에 낀 걸로 보이면 풀린다)
    PendantAsk? ask;                  // 끼셨나요? 창
    /// <summary>"미착용 예정"으로 정펜 확인을 껐을 때 (설정 창 스위치를 맞춘다)</summary>
    public static event Action? TurnedOff;
    string? nick;                     // 지금 확인하고 있는 닉네임 (바뀌면 처음부터)
    DateTime nextCheck, nextAlert, lastRefresh = DateTime.MinValue;

    enum Result { Yes, No, Unknown, NoChar, Blocked }

    public PendantCheck(Settings st, JanusWatch watch, float sc)
    {
        this.st = st; this.watch = watch; this.sc = sc;
        timer.Tick += (_, _) => Tick();
        timer.Start();
    }

    string? Nick => string.IsNullOrWhiteSpace(st.MapleNick) ? null : st.MapleNick.Trim();
    int Slot => Math.Clamp(st.PendantBuff, 0, JanusWatch.BuffSlots - 1);
    bool Active => st.PendantOn && Nick != null && st.Buffs[Slot].On && watch.BuffRemaining(Slot) != null;
    // 바를 숨겼고 "바를 숨겨도 알림 받기"가 꺼져 있으면 알림만 띄우지 않는다
    bool Mute => JanusWatch.BarHidden && !st.AlertWhenHidden;

    void Tick()
    {
        if (disposed) return;
        var now = DateTime.Now;
        if (!Active || Nick != nick)
        {
            // 버프가 끝났거나 닉네임이 바뀌면 처음부터 다시 본다
            missing = false; quiet = false;
            CloseAsk();
            nick = Nick;
            nextCheck = DateTime.MinValue;
            if (!busy) Release();
            if (!Active) { Status = Idle; return; }
        }
        if (missing && !quiet && now >= nextAlert)
        {
            nextAlert = now + AlertGap;
            if (!Mute) { PlayAlert(sc); ShowAsk(); } // 끼셨나요? 창도 (이미 떠 있으면 그대로)
        }
        if (!busy && now >= nextCheck) _ = Check();
    }

    async Task Check()
    {
        busy = true;
        string who = nick!;
        try
        {
            if (Status == Idle) Status = "maple.gg에서 확인하고 있어요…";
            var r = await Look(who);
            // 1분마다 정보 갱신을 누르고, 갱신이 끝나면 바로 다시 본다
            if (r is Result.Yes or Result.No && DateTime.Now - lastRefresh >= RefreshGap && !disposed)
            {
                Status = "정보 갱신 중…";
                if (await ClickRefresh())
                {
                    lastRefresh = DateTime.Now;
                    await WaitRefresh();
                    if (disposed) return;
                    r = await Read();
                }
            }
            if (disposed || who != nick || !Active) return;
            var now = DateTime.Now;
            switch (r)
            {
                case Result.Yes:
                    missing = false; quiet = false;
                    CloseAsk();
                    Status = "정령의 펜던트를 꼈어요";
                    nextCheck = now + CheckGap;
                    break;
                case Result.No:
                    if (!missing)
                    {
                        missing = true; nextAlert = now;
                    }
                    Status = quiet ? "착용 완료로 알림만 멈췄어요 · 계속 확인해요" : "정령의 펜던트 미착용";
                    nextCheck = now + CheckGap;
                    break;
                case Result.NoChar:
                    Status = "maple.gg에 없는 닉네임이에요";
                    nextCheck = now + SlowGap;
                    break;
                case Result.Blocked:
                    Status = "maple.gg 사람 확인이 떠서 못 봤어요";
                    nextCheck = now + SlowGap;
                    break;
                default:
                    Status = "maple.gg를 못 읽었어요";
                    nextCheck = now + CheckGap;
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Write("정펜", "maple.gg 확인 실패", ex);
            Status = "maple.gg를 못 읽었어요";
            nextCheck = DateTime.Now + CheckGap;
        }
        finally { busy = false; }
    }

    // 장비 목록에서 정령의 펜던트를 찾는다. 목록이 아직 안 그려졌으면 wait
    const string ReadJs = @"(()=>{
const b=document.body; if(!b) return 'wait';
const t=b.innerText||'';
if(/Just a moment|사람인지 확인|Verify you are human/i.test(document.title+' '+t.slice(0,400))) return 'blocked';
const alts=[...document.images].map(i=>i.alt||'').filter(a=>a.trim());
if((t+'\n'+alts.join('\n')).includes('정령의 펜던트')) return 'yes';
if(/검색 ?결과가 없|존재하지 않는 캐릭터|캐릭터를 찾을 수 없/.test(t)) return 'nochar';
if(alts.length>=8 && (t.includes('펜던트')||alts.some(a=>/펜던트|목걸이/.test(a)))) return 'no';
return 'wait';
})()";

    const string RefreshJs = @"(()=>{
const b=[...document.querySelectorAll('button')].find(x=>(x.innerText||'').trim()==='정보 갱신');
if(!b||b.disabled) return false; b.click(); return true;
})()";

    async Task<Result> Look(string who)
    {
        var cw = await Web();
        var nav = new TaskCompletionSource<bool>();
        void Done(object? s, CoreWebView2NavigationCompletedEventArgs e) => nav.TrySetResult(e.IsSuccess);
        cw.NavigationCompleted += Done;
        try
        {
            cw.Navigate("https://maple.gg/u/" + Uri.EscapeDataString(who));
            await Task.WhenAny(nav.Task, Task.Delay(20000));
        }
        finally { cw.NavigationCompleted -= Done; }
        return await Read();
    }

    /// <summary>지금 열린 페이지에서 장비를 읽는다. 장비 목록은 페이지가 열린 뒤 그려지므로 최대 15초 기다린다</summary>
    async Task<Result> Read()
    {
        if (ctl == null) return Result.Unknown;
        var cw = ctl.CoreWebView2;
        for (int i = 0; i < 15 && !disposed; i++)
        {
            var s = await cw.ExecuteScriptAsync(ReadJs);
            switch (s.Trim('"'))
            {
                case "yes": return Result.Yes;
                case "no": return Result.No;
                case "nochar": return Result.NoChar;
                case "blocked": return Result.Blocked;
            }
            await Task.Delay(1000);
        }
        return Result.Unknown;
    }

    async Task<bool> ClickRefresh()
    {
        if (ctl == null) return false;
        return (await ctl.CoreWebView2.ExecuteScriptAsync(RefreshJs)) == "true";
    }

    // 정보 갱신 버튼 상태: 다시 누를 수 있게 돌아왔으면 idle, 갱신 중(없음·꺼짐·글자 바뀜)이면 busy
    const string RefreshStateJs = @"(()=>{
const b=[...document.querySelectorAll('button')].find(x=>(x.innerText||'').includes('갱신'));
return (!b||b.disabled||(b.innerText||'').trim()!=='정보 갱신')?'busy':'idle';
})()";

    /// <summary>정보 갱신이 끝날 때까지 기다린다 (버튼이 돌아오거나 페이지가 다시 열리면 끝). 최대 30초</summary>
    async Task WaitRefresh()
    {
        if (ctl == null) return;
        var cw = ctl.CoreWebView2;
        var reloaded = new TaskCompletionSource<bool>();
        void Done(object? s, CoreWebView2NavigationCompletedEventArgs e) => reloaded.TrySetResult(true);
        cw.NavigationCompleted += Done;
        try
        {
            bool sawBusy = false;
            var until = DateTime.Now.AddSeconds(30);
            while (!disposed && DateTime.Now < until && !reloaded.Task.IsCompleted)
            {
                await Task.Delay(500);
                if (reloaded.Task.IsCompleted) break;
                var s = (await cw.ExecuteScriptAsync(RefreshStateJs)).Trim('"');
                if (s == "busy") sawBusy = true;
                // 갱신 중 표시가 사라졌으면 끝. 갱신 중 표시가 3초 넘게 안 보이면 이미 끝난 것으로 본다
                else if (sawBusy || DateTime.Now > until.AddSeconds(-27)) break;
            }
        }
        finally { cw.NavigationCompleted -= Done; }
        await Task.Delay(300); // 바뀐 장비 목록이 그려질 틈
    }

    async Task<CoreWebView2> Web()
    {
        if (ctl != null) return ctl.CoreWebView2;
        host ??= new Form { ShowInTaskbar = false, FormBorderStyle = FormBorderStyle.None, Size = new Size(1280, 900) };
        var env = await WebWindow.Env;
        var c = await env.CreateCoreWebView2ControllerAsync(host.Handle);
        if (disposed) { c.Close(); throw new ObjectDisposedException(nameof(PendantCheck)); }
        c.Bounds = new Rectangle(0, 0, 1280, 900);
        c.IsVisible = true; // 받침 창은 띄우지 않아서 화면에는 보이지 않는다
        var cw = c.CoreWebView2;
        cw.IsMuted = true;  // 소리는 절대 내지 않는다
        cw.NewWindowRequested += (_, e) => e.Handled = true; // 새 창은 열지 않는다
        ctl = c;
        return cw;
    }

    /// <summary>끼셨나요? 창을 띄운다. 착용 완료 → 알림만 멈춤, 미착용 예정 → 정펜 확인 끄기</summary>
    void ShowAsk()
    {
        if (ask != null && !ask.IsDisposed) return;
        ask = new PendantAsk(
            worn: () => { quiet = true; Status = "착용 완료로 알림만 멈췄어요 · 계속 확인해요"; },
            notWear: () =>
            {
                st.PendantOn = false; st.Save();
                missing = quiet = false;
                Status = Idle;
                TurnedOff?.Invoke();
            });
        ask.FormClosed += (_, _) => ask = null;
        ask.Show();
    }

    void CloseAsk()
    {
        if (ask != null && !ask.IsDisposed) ask.Close();
        ask = null;
    }

    /// <summary>웹 화면을 닫아 메모리를 돌려준다 (버프가 끝났을 때)</summary>
    void Release()
    {
        ctl?.Close(); ctl = null;
        host?.Dispose(); host = null;
    }

    /// <summary>미착용 알림: 노랑으로 돌다가 6시 방향에서 잠깐 파랑, 다시 노랑으로 끝난다.</summary>
    public static void PlayAlert(float sc) =>
        JanusAlert.Play(sc, "정령의 펜던트 미착용", Yellow, Color.FromArgb(255, 252, 210), AlertSound.Kind.Buff,
            flash: Blue, flashLight: Color.FromArgb(205, 228, 255));

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        timer.Stop(); timer.Dispose();
        CloseAsk();
        Release();
        Status = Idle;
    }
}
