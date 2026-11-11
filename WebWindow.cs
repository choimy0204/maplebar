using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

using static MapleBar.Native;

namespace MapleBar;

/// <summary>
/// 링크 하나를 여는 독립된 창. 윈도우 제목줄 없이 얇은 손잡이만 있고,
/// 손잡이를 끌면 옮겨지고 가장자리를 끌면 크기가 바뀐다.
/// </summary>
public class WebWindow : Form
{
    // 모든 창이 같은 WebView2 환경(쿠키·로그인·사람 확인)을 함께 쓴다
    static Task<CoreWebView2Environment>? sharedEnv;
    static Task<CoreWebView2Environment> Env =>
        sharedEnv ??= CoreWebView2Environment.CreateAsync(null, Settings.WebDataDir,
            // 바에서 고른 곡이 숨은 창에서 바로 재생되게 한다
            new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required" + (Environment.GetEnvironmentVariable("MB_DEBUG") == "1" ? " --remote-debugging-port=9333" : "")));

    static readonly List<WebWindow> open = new();

    readonly WebView2 web = new() { Dock = DockStyle.Fill };
    readonly Settings settings;
    readonly TitleStrip grip;
    string startUrl = "";

    /// <summary>"아델 | 캐릭터정보 - 환산주스탯"을 닉네임·페이지로 나눠 그리는 제목 줄.</summary>
    sealed class TitleStrip : Panel
    {
        readonly WebWindow owner;
        readonly Font bold = new("맑은 고딕", 9.5f, FontStyle.Bold);
        readonly Font normal = new("맑은 고딕", 9f);
        public TitleStrip(WebWindow owner)
        {
            this.owner = owner;
            DoubleBuffered = true; ResizeRedraw = true; BackColor = Color.White;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            int cy = Height / 2, d = owner.S(8), x = owner.S(6);
            using (var b = new SolidBrush(UI.Accent)) g.FillEllipse(b, x, cy - d / 2f, d, d);
            x += d + owner.S(8);

            var t = owner.Text;
            int dash = t.LastIndexOf(" - 환산주스탯", StringComparison.Ordinal);
            if (dash > 0) t = t[..dash];
            string main = t, sub = "";
            int bar = t.IndexOf(" | ", StringComparison.Ordinal);
            if (bar > 0) { main = t[..bar]; sub = t[(bar + 3)..]; }

            // 오른쪽 버튼·슬라이드 바 앞에서 글자를 끊는다
            int right = Width - Padding.Right - owner.S(30);
            foreach (Control c in Controls) if (c.Visible && c.Width > 0 && c.BackColor != Color.Transparent) right = Math.Min(right, c.Left - owner.S(6));
            // 페이지 부가 설명(예: '메이플 코디·믹스 염색…')은 창을 펼쳤을 때만 보인다
            if (!owner.Expanded) sub = "";
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
            var r1 = new Rectangle(x, 0, Math.Max(0, right - x), Height);
            TextRenderer.DrawText(g, main, bold, r1, UI.Text, flags);
            if (sub.Length == 0) return;
            x += TextRenderer.MeasureText(g, main, bold, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width + owner.S(8);
            if (x < right)
                TextRenderer.DrawText(g, sub, normal, new Rectangle(x, 0, right - x, Height), Color.FromArgb(140, 140, 140), flags);
        }
    }
    readonly float sc;
    readonly int edge; // 크기 조절용 테두리 두께

    // ── 뮤직: 메애기 창은 숨겨 두고 노래만 재생한다. 바의 작은 컨트롤이 이 창을 조종한다. ──
    public record MusicInfo(string Title, string Sub, string Time, bool Playing, double Volume, string Img,
        bool Repeat, bool Shuffle, bool Fx);

    // 설정의 효과음 켜짐/꺼짐을 페이지에 알려 준다 (새로 열린 페이지에도 적용)
    static string FxScript(bool on) => $"window.mbSetFx&&window.mbSetFx({(on ? "true" : "false")});";
    string? fxScriptId;

    /// <summary>설정에서 곡 넘김 효과음을 켜고 끄면 뮤직 창에 바로 적용한다.</summary>
    public static async void SetMusicFx(bool on)
    {
        var w = MusicWin;
        var cw = w?.web.CoreWebView2;
        if (w == null || cw == null) return;
        try
        {
            if (w.fxScriptId != null) cw.RemoveScriptToExecuteOnDocumentCreated(w.fxScriptId);
            w.fxScriptId = await cw.AddScriptToExecuteOnDocumentCreatedAsync(FxScript(on));
            await cw.ExecuteScriptAsync(FxScript(on));
        }
        catch { }
    }

    /// <summary>"rep" 반복 재생, "shuf" 랜덤 재생을 켜고 끈다.</summary>
    public static void MusicMode(string m)
    {
        var w = MusicWin;
        if (w?.web.CoreWebView2 == null) return;
        _ = w.web.CoreWebView2.ExecuteScriptAsync($"window.mbMode&&window.mbMode('{(m == "rep" ? "rep" : "shuf")}')");
        _ = w.PollSoon();
    }

    /// <summary>소리 크기(0~1)를 바꾼다.</summary>
    public static void MusicVolume(double v) =>
        _ = MusicWin?.web.CoreWebView2?.ExecuteScriptAsync($"window.mbVol&&window.mbVol({v.ToString(System.Globalization.CultureInfo.InvariantCulture)})");
    /// <summary>곡 정보가 바뀌면 알린다. null이면 뮤직 창이 닫힌 것.</summary>
    public static event Action<MusicInfo?>? MusicChanged;
    bool reallyClose;
    readonly System.Windows.Forms.Timer musicTimer = new() { Interval = 1000 };
    string lastTime = "";
    MusicInfo? lastInfo;

    // 재생 바에서 곡 정보를 읽고 버튼(0 이전, 1 재생/일시정지, 2 다음)을 누르는 함수
    const string MusicScript =
        "(()=>{const P='div.fixed.left-0.bottom-0.w-full';" +
        // 소리 크기는 바에서 정한 값을 계속 쓴다 (0~1, 이 사이트에만 저장)
        "let V=parseFloat(localStorage.getItem('mbVol'));if(isNaN(V))V=1;" +
        // 반복(REP): 지금 곡을 계속 다시 튼다. 랜덤(SHUF): 곡이 끝나거나 다음을 누르면 지금 목록에서 아무 곡이나 튼다.
        "let REP=localStorage.getItem('mbRep')==='1',SHUF=localStorage.getItem('mbShuf')==='1';" +
        // 곡이 바뀔 때 옛 축음기 소리 (FX). 새 곡은 바늘이 닿은 뒤 서서히 키운다
        "let FX=true,lastSrc='',AC=null,fade=null,fadeWait=null,fxAt=0;const FXLEN=2.55;" +
        "const vinyl=()=>{try{AC=AC||new AudioContext();if(AC.state==='suspended')AC.resume();const sr=AC.sampleRate,t=AC.currentTime+0.02;" +
        "const M=AC.createGain();M.gain.value=0.7*Math.max(0.15,V);const W=AC.createBiquadFilter();W.type='lowpass';W.frequency.value=2600;const H=AC.createBiquadFilter();H.type='highpass';H.frequency.value=220;M.connect(H);H.connect(W);W.connect(AC.destination);" +
        "const noise=s=>{const b=AC.createBuffer(1,Math.max(1,Math.floor(sr*s)),sr),d=b.getChannelData(0);for(let i=0;i<d.length;i++)d[i]=Math.random()*2-1;return b};" +
        // 잡음을 거르는 필터 주파수를 움직여 '쉭' 같은 소리를 만든다
        "const nz=(t0,dur,type,f0,f1,q,gain,att)=>{const s=AC.createBufferSource();s.buffer=noise(dur+0.05);const f=AC.createBiquadFilter();f.type=type;f.Q.value=q;" +
        "f.frequency.setValueAtTime(f0,t0);f.frequency.exponentialRampToValueAtTime(f1,t0+dur);const g=AC.createGain();g.gain.setValueAtTime(0.0001,t0);" +
        "g.gain.exponentialRampToValueAtTime(gain,t0+att);g.gain.exponentialRampToValueAtTime(0.0001,t0+dur);s.connect(f);f.connect(g);g.connect(M);s.start(t0);s.stop(t0+dur+0.05)};" +
        // 낮은 음이 떨어지며 '쿵/툭'
        "const thud=(t0,f0,f1,dur,gain)=>{const o=AC.createOscillator();o.type='sine';o.frequency.setValueAtTime(f0,t0);o.frequency.exponentialRampToValueAtTime(f1,t0+dur);" +
        "const g=AC.createGain();g.gain.setValueAtTime(gain,t0);g.gain.exponentialRampToValueAtTime(0.0001,t0+dur);o.connect(g);g.connect(M);o.start(t0);o.stop(t0+dur+0.02)};" +
        // LP 교체: 슬리브에서 꺼내는 '스윽' → 판 올리는 뭉툭한 '툭' → 바늘팔 내려오는 '톡' → 판이 돌며 따뜻한 지지직 (새 곡이 커지면서 사라진다)
        "nz(t,0.42,'lowpass',1100,450,0.7,0.12,0.14);" +                                                         // 슬리브에서 꺼내는 '스윽'
        "thud(t+0.48,110,52,0.20,0.60);nz(t+0.48,0.07,'lowpass',700,280,0.7,0.26,0.004);" +                     // 판 올리는 '툭'
        "thud(t+0.60,150,85,0.05,0.18);" +                                                                       // 판이 자리 잡는 작은 소리
        "{const o=AC.createOscillator();o.type='triangle';o.frequency.setValueAtTime(38,t+0.62);o.frequency.linearRampToValueAtTime(55,t+1.2);" +
        "const g=AC.createGain();g.gain.setValueAtTime(0.0001,t+0.62);g.gain.exponentialRampToValueAtTime(0.06,t+0.9);g.gain.exponentialRampToValueAtTime(0.0001,t+1.5);" +
        "o.connect(g);g.connect(M);o.start(t+0.62);o.stop(t+1.55)}" +                                           // 판이 돌기 시작하는 낮은 울림
        "thud(t+0.88,85,48,0.10,0.38);nz(t+0.88,0.03,'lowpass',1400,600,0.7,0.12,0.003);" +                     // 바늘 내려앉는 '톡'
        // 판이 돌며 나는 지지직: 아주 작은 쉬— 소리 위로 낮게 튀는 '틱·톡' (33회전 리듬으로 살짝 출렁인다)
        "const SL=1.4,cl=AC.createBuffer(1,Math.floor(sr*SL),sr),cd=cl.getChannelData(0);let lp=0,p=0;for(let i=0;i<cd.length;i++){const tt=i/sr;" +
        "if(Math.random()<0.0007)p+=(Math.random()<0.5?-1:1)*(0.25+Math.random()*0.55);p*=0.9;" +
        "const v=(Math.random()*2-1)*0.022*(0.8+0.2*Math.sin(2*Math.PI*0.55*tt))+p;lp+=(v-lp)*0.28;" +
        "cd[i]=lp*Math.min(1,tt/0.04)*Math.min(1,(cd.length-i)/(sr*0.5))}" +
        "const cs=AC.createBufferSource();cs.buffer=cl;cs.connect(M);cs.start(t+0.92)}catch(e){}};" +
        "const fadeIn=(a,wait)=>{clearInterval(fade);clearTimeout(fadeWait);let k=0;a.volume=0;" +
        "fadeWait=setTimeout(()=>{try{a.currentTime=0}catch(e){}fade=setInterval(()=>{k=Math.min(1,k+0.08);try{a.volume=V*k}catch(e){}if(k>=1)clearInterval(fade)},70)},wait*1000)};" +
        "const op=HTMLMediaElement.prototype.play;HTMLMediaElement.prototype.play=function(){window.__mbA=this;" +
        "const src=this.currentSrc||this.src||'';const isNew=src&&src!==lastSrc;if(src)lastSrc=src;" +
        // 레코드 소리는 5초 쿨타임: 쿨타임 중에 또 넘기면 소리 없이 바로 틀고 쿨타임을 다시 5초부터 센다
        "try{this.loop=REP;if(isNew&&FX){const now=Date.now(),cool=now-fxAt<5000;fxAt=now;" +
        "if(cool){clearInterval(fade);clearTimeout(fadeWait);this.volume=V}else{vinyl();fadeIn(this,FXLEN)}}else this.volume=V}catch(e){}return op.apply(this,arguments)};" +
        "window.mbSetFx=v=>{FX=!!v};" +
        "window.mbMode=m=>{if(m==='rep'){REP=!REP;localStorage.setItem('mbRep',REP?'1':'0');const a=window.__mbA;if(a)a.loop=REP}else{SHUF=!SHUF;localStorage.setItem('mbShuf',SHUF?'1':'0')}};" +
        "const rnd=()=>{const r=R();if(!r.length)return false;const st=window.mbState&&window.mbState();const cur=st?st.t:'';let i=0,n=0;" +
        "do{i=Math.floor(Math.random()*r.length);n++}while(n<10&&r.length>1&&((r[i].querySelector('div.text-sm')||{}).textContent||'').trim()===cur);return window.mbPlay(i)};" +
        // 곡이 끝났을 때 사이트의 '다음 곡'보다 먼저 받아서 랜덤 곡을 튼다
        "window.addEventListener('ended',e=>{if(SHUF&&!REP&&rnd())e.stopImmediatePropagation()},true);" +
        "window.mbVol=v=>{V=Math.min(1,Math.max(0,v));try{localStorage.setItem('mbVol',V)}catch(e){}const a=window.__mbA;if(a)a.volume=V;return V};" +
        "window.mbState=()=>{const p=document.querySelector(P);if(!p)return null;const sp=p.querySelectorAll('span.cursor-pointer');" +
        "const tm=[...p.querySelectorAll('span')].find(s=>/\\d+:\\d+ \\/ \\d+:\\d+/.test(s.textContent));const a=window.__mbA;" +
        "return {t:(sp[0]&&sp[0].textContent.trim())||'',s:(sp[1]&&sp[1].textContent.trim())||'',time:tm?tm.textContent.trim():'',playing:!!(a&&!a.paused&&!a.ended),hasA:!!a,vol:V,rep:REP,shuf:SHUF,fx:FX,img:(p.querySelector('img')||{}).src||''}};" +
        "window.mbCmd=i=>{if(i===2&&SHUF&&rnd())return;const p=document.querySelector(P);const f=p&&p.querySelector('svg');if(!f)return;" +
        "const b=f.parentElement.querySelectorAll(':scope>svg')[i];if(b)b.dispatchEvent(new MouseEvent('click',{bubbles:true}))};" +
        // 곡 목록 (첫 화면이면 인기 차트 TOP 100만), 곡 재생, 검색, 사이트 안 이동
        "const here=()=>location.pathname.replace(/\\/$/,'');" +
        "const R=()=>{let r=[...document.querySelectorAll('div.group.flex.items-center.gap-2')].filter(x=>x.querySelector(':scope>div.relative.cursor-pointer img'));return here()==='/music'?r.slice(0,100):r};" +
        "window.mbList=()=>R().map(x=>{const a=x.querySelector('div.text-sm'),b=x.querySelector('div.text-xs');const im=x.querySelector('img');return {t:a?a.textContent.trim():'',s:b?b.textContent.trim():'',img:im?(im.currentSrc||im.src||''):''}});" +
        "window.mbPlay=i=>{const r=R()[i];const b=r&&r.querySelector(':scope>div.relative.cursor-pointer');if(!b)return false;b.dispatchEvent(new MouseEvent('click',{bubbles:true}));return true};" +
        "window.mbSearch=q=>{const inp=[...document.querySelectorAll('input')].find(i=>i.placeholder==='노래 검색');if(!inp)return false;" +
        "Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(inp,q);inp.dispatchEvent(new Event('input',{bubbles:true}));return true};" +
        "window.mbGo=p=>{if(here()===p)return 0;const a=[...document.querySelectorAll('a[href]')].find(a=>{try{return new URL(a.href).pathname.replace(/\\/$/,'')===p}catch(e){return false}});" +
        "if(a){a.click();return 1}location.href=p;return 2};" +
        "window.mbHere=()=>here();})();";

    /// <summary>뮤직 창을 찾거나 새로 연다. show가 false면 화면에 띄우지 않고 뒤에서만 연다.</summary>
    public static WebWindow EnsureMusic(Settings st, float scale, string url, bool show)
    {
        var w = MusicWin;
        if (w == null)
        {
            // 처음부터 화면 밖에 투명하게 띄워 둔다 (잠깐 보이지 않게)
            w = new WebWindow(url, st, scale);
            w.Park();
            w.Show();
        }
        else if (show) { ShowMusic(url); return w; }
        if (show) { w.Unpark(); w.Activate(); }
        return w;
    }

    // 뮤직 창은 숨기지 않고 화면 밖으로 치워 둔다. 숨기면 웹 화면이 멈춰 노래가 안 나올 수 있다.
    bool parked;
    Rectangle parkedFrom;
    void Park()
    {
        if (parked) return;
        if (WindowState != FormWindowState.Normal) WindowState = FormWindowState.Normal;
        parkedFrom = Bounds;
        parked = true;
        Opacity = 0;
        Location = new Point(-32000, -32000);
    }
    void Unpark()
    {
        if (parked) { parked = false; Bounds = parkedFrom; Opacity = 1; }
        if (!Visible) Show();
    }

    async Task<string?> Eval(string js)
    {
        for (int i = 0; i < 100 && web.CoreWebView2 == null && !IsDisposed; i++) await Task.Delay(100);
        if (web.CoreWebView2 == null || IsDisposed) return null;
        try { return await web.CoreWebView2.ExecuteScriptAsync(js); } catch { return null; }
    }

    public record Song(string Title, string Sub, string Img);

    /// <summary>뒤에 숨은 뮤직 창에서 path 페이지(인기 차트, 검색)의 곡 목록을 읽는다. 노래는 끊기지 않는다.</summary>
    public static async Task<List<Song>> MusicListAsync(Settings st, float scale, string path, string? query = null)
    {
        var w = EnsureMusic(st, scale, Music.Home.Replace("/music", "") + path, false);
        string p = System.Text.Json.JsonSerializer.Serialize(path);
        // 페이지가 준비될 때까지 기다리며 옮긴다
        for (int i = 0; i < 60; i++)
        {
            var r = await w.Eval($"window.mbGo?(window.mbGo({p}),window.mbHere()):''");
            if (r == p) break;
            await Task.Delay(250);
        }
        if (query != null)
        {
            for (int i = 0; i < 40; i++)
            {
                if (await w.Eval($"window.mbSearch?window.mbSearch({System.Text.Json.JsonSerializer.Serialize(query)}):false") == "true") break;
                await Task.Delay(250);
            }
            await Task.Delay(900); // 결과가 다시 그려질 때까지
        }
        for (int i = 0; i < 40; i++)
        {
            var json = await w.Eval("window.mbList?JSON.stringify(window.mbList()):'[]'");
            var list = ParseSongs(json);
            if (list.Count > 0 || (query != null && i >= 4)) return list;
            await Task.Delay(250);
        }
        return new();
    }

    static List<Song> ParseSongs(string? json)
    {
        var list = new List<Song>();
        try
        {
            if (json == null) return list;
            // ExecuteScriptAsync는 JSON.stringify 결과를 다시 문자열로 감싸서 준다
            var inner = System.Text.Json.JsonSerializer.Deserialize<string>(json) ?? "[]";
            using var doc = System.Text.Json.JsonDocument.Parse(inner);
            foreach (var e in doc.RootElement.EnumerateArray())
                list.Add(new Song(e.GetProperty("t").GetString() ?? "", e.GetProperty("s").GetString() ?? "",
                    e.TryGetProperty("img", out var im) ? im.GetString() ?? "" : ""));
        }
        catch { }
        return list;
    }

    /// <summary>지금 읽어 둔 목록의 i번째 곡을 재생한다.</summary>
    public static async Task MusicPlayAsync(int i)
    {
        var w = MusicWin;
        if (w == null) return;
        await w.Eval($"window.mbPlay&&window.mbPlay({i})");
        // 곡은 바뀌었는데 멈춰 있으면 한 번 더 재생을 건다
        await Task.Delay(1500);
        await w.Eval("(()=>{const a=window.__mbA;if(a&&a.paused&&!a.ended)a.play().catch(()=>{})})()");
        _ = w.PollSoon();
    }

    static WebWindow? MusicWin => open.FirstOrDefault(x => Music.IsMusic(x.startUrl) && !x.IsDisposed);
    public static bool MusicOpen => MusicWin != null;

    public static void MusicCmd(int i)
    {
        var w = MusicWin;
        if (w?.web.CoreWebView2 == null) return;
        _ = w.web.CoreWebView2.ExecuteScriptAsync($"window.mbCmd&&window.mbCmd({i})");
        _ = w.PollSoon();
    }

    /// <summary>뮤직 창이 보이면 숨기고, 숨어 있으면 띄운다.</summary>
    public static void ToggleMusicWindow()
    {
        var w = MusicWin;
        if (w == null) return;
        if (!w.parked && w.Visible && w.WindowState != FormWindowState.Minimized) { w.Park(); return; }
        if (w.WindowState == FormWindowState.Minimized) w.WindowState = FormWindowState.Normal;
        w.Unpark();
        w.Activate();
    }

    public static void StopMusic()
    {
        var w = MusicWin;
        if (w == null) return;
        w.reallyClose = true;
        w.Close();
    }

    async Task PollSoon() { await Task.Delay(300); await PollMusic(); }

    async Task PollMusic()
    {
        var cw = web.CoreWebView2;
        if (cw == null || IsDisposed) return;
        try
        {
            var r = await cw.ExecuteScriptAsync("window.mbState?window.mbState():null");
            if (string.IsNullOrEmpty(r) || r == "null") return;
            using var doc = System.Text.Json.JsonDocument.Parse(r);
            var js = doc.RootElement;
            string t = js.GetProperty("t").GetString() ?? "", s = js.GetProperty("s").GetString() ?? "";
            string time = js.GetProperty("time").GetString() ?? "";
            bool playing = js.GetProperty("hasA").GetBoolean()
                ? js.GetProperty("playing").GetBoolean()
                : (lastTime != "" && time != lastTime); // 오디오를 못 잡으면 시간이 흐르는지로 판단
            lastTime = time;
            if (t == "") return;
            double vol = js.TryGetProperty("vol", out var ve) && ve.ValueKind == System.Text.Json.JsonValueKind.Number ? ve.GetDouble() : 1;
            string img = js.TryGetProperty("img", out var ie) ? ie.GetString() ?? "" : "";
            bool Flag(string k) => js.TryGetProperty(k, out var f) && f.ValueKind == System.Text.Json.JsonValueKind.True;
            var info = new MusicInfo(t, s, time, playing, vol, img, Flag("rep"), Flag("shuf"), Flag("fx"));
            if (info == lastInfo) return;
            lastInfo = info;
            MusicChanged?.Invoke(info);
        }
        catch { }
    }
    int S(int v) => UI.Scale(v, sc);

    public WebWindow(string url, Settings settings, float scale)
    {
        this.settings = settings;
        sc = scale;
        edge = S(5);
        Text = "메이플 바";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = !Music.IsMusic(url); // 뮤직 창은 화면 밖에 있으므로 작업 표시줄에 안 보이게
        StartPosition = FormStartPosition.Manual;
        MinimumSize = new Size(S(300), S(240));
        BackColor = Color.White; // 테두리(크기 조절 영역)가 따로 보이지 않게 흰색
        Padding = new Padding(edge);
        TopMost = settings.WinTopMost || Webtoon.IsWebtoon(url); // 웹툰은 늘 맨 위
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        // 위쪽 제목 줄: 주황 점 + 닉네임(굵게) + 페이지 이름(회색), 오른쪽에 주황 동그라미 ✕
        grip = new TitleStrip(this) { Dock = DockStyle.Top, Height = S(32), Padding = new Padding(0, S(5), S(2), S(5)) };
        var close = new RoundLabel
        {
            Dock = DockStyle.Right, AutoSize = false, Width = S(22), Text = "", Cursor = Cursors.Hand,
            ForeColor = Color.White, Fill = UI.Accent, Radius = S(6), UseMnemonic = false,
        };
        // 글자 대신 직접 그려서 정확히 가운데에 둔다
        close.Paint += (_, e) =>
        {
            var g = e.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            float cx = close.Width / 2f, cy = close.Height / 2f, d = S(4);
            using var p = new Pen(Color.White, Math.Max(1.6f, S(2) * 0.85f)) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
            g.DrawLine(p, cx - d, cy - d, cx + d, cy + d);
            g.DrawLine(p, cx - d, cy + d, cx + d, cy - d);
        };
        close.MouseEnter += (_, _) => close.Fill = ControlPaint.Dark(UI.Accent, 0.05f);
        close.MouseLeave += (_, _) => close.Fill = UI.Accent;
        close.Click += (_, _) => Close();
        grip.Controls.Add(close);
        // ✕ 왼쪽 펼치기 버튼: 누르면 크게(BigSize), 다시 누르면 원래 크기로
        var gap = new Panel { Dock = DockStyle.Right, Width = S(6), BackColor = Color.Transparent };
        grip.Controls.Add(gap); gap.BringToFront();
        var grow = new RoundLabel
        {
            Dock = DockStyle.Right, AutoSize = false, Width = S(22), Text = "", Cursor = Cursors.Hand,
            ForeColor = UI.Accent, Fill = UI.SoftAccent, Radius = S(6), UseMnemonic = false,
        };
        // 작을 때: 큰 네모(펼치기) / 클 때: 작은 네모(접기)
        grow.Paint += (_, e) =>
        {
            var g = e.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            float cx = grow.Width / 2f, cy = grow.Height / 2f, d = smallBounds != null ? S(3) : S(5);
            using var p = new Pen(UI.Accent, Math.Max(1.5f, S(2) * 0.8f));
            using var box = UI.RoundRect(new RectangleF(cx - d, cy - d, d * 2, d * 2), S(2));
            g.DrawPath(p, box);
        };
        var growTip = new ToolTip();
        growTip.SetToolTip(grow, "크게 보기");
        grow.MouseEnter += (_, _) => grow.Fill = Color.FromArgb(255, 226, 196);
        grow.MouseLeave += (_, _) => grow.Fill = UI.SoftAccent;
        grow.Click += (_, _) =>
        {
            ToggleBig();
            grow.Invalidate(); grip.Invalidate();
            growTip.SetToolTip(grow, smallBounds != null ? "원래 크기로" : "크게 보기");
        };
        grip.Controls.Add(grow); grow.BringToFront();
        // 펼치기 버튼 왼쪽: 투명도 슬라이드 바
        var gap2 = new Panel { Dock = DockStyle.Right, Width = S(8), BackColor = Color.Transparent };
        grip.Controls.Add(gap2); gap2.BringToFront();
        alphaBar = new AlphaSlider { Dock = DockStyle.Right, Width = S(70), Value = Music.IsMusic(url) ? 100 : Math.Clamp(settings.WinOpacity, AlphaSlider.Min, 100) };
        alphaBar.Changed += v => SetAlpha(v);
        new ToolTip().SetToolTip(alphaBar, "투명도");
        grip.Controls.Add(alphaBar); alphaBar.BringToFront();
        if (Music.IsMusic(url))
        {
            // 뮤직 창: 곡 정보를 1초마다 읽어 바에 알린다
            musicTimer.Tick += async (_, _) => await PollMusic();
            musicTimer.Start();
        }

        var menu = UI.Rounded(new ContextMenuStrip { Font = new Font("맑은 고딕", 9.5f * UI.Z), ShowImageMargin = false });
        menu.Items.Add("새로고침 (F5)", null, (_, _) => web.CoreWebView2?.Reload());
        menu.Items.Add("뒤로 (Alt+←)", null, (_, _) => { if (web.CanGoBack) web.GoBack(); });
        menu.Items.Add("브라우저로 열기", null, (_, _) => { if (web.Source != null) OpenExternal(web.Source.ToString()); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("닫기 (Ctrl+W)", null, (_, _) => Close());
        // 투명도: 제목 줄의 슬라이드 바(또는 제목 줄에서 휠). 마지막 값은 새 창에도 쓴다
        grip.MouseWheel += (_, e) => SetAlpha((int)Math.Round(Opacity * 100) + (e.Delta > 0 ? 5 : -5));

        grip.ContextMenuStrip = menu;
        grip.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left && e.Clicks == 1) DragMove(); };
        grip.DoubleClick += (_, _) => ToggleMax();

        Controls.Add(web);
        Controls.Add(grip);
        web.BringToFront();

        Bounds = NextBounds(url);
        if (!Music.IsMusic(url)) Opacity = Math.Clamp(settings.WinOpacity, 30, 100) / 100.0;
        startUrl = url;
        open.Add(this);
        _ = InitAsync(url);
        if (Webtoon.IsWebtoon(url)) scrollTimer.Start();
    }

    /// <summary>작은 창(약 420×560)으로, 이미 열린 창과 겹치지 않게 조금씩 비켜서 연다.</summary>
    Rectangle NextBounds(string url)
    {
        // 바가 있는 화면에 연다
        var bar = Application.OpenForms.OfType<BarForm>().FirstOrDefault();
        var wa = (bar != null ? Screen.FromControl(bar) : Screen.FromPoint(Cursor.Position)).WorkingArea;
        // 웹툰은 세로로 길게 본다: 폭은 조금 넓게, 높이는 화면 거의 전부
        bool tall = Webtoon.IsWebtoon(url);
        // 사이트 글자는 해상도와 상관없이 같은 크기로 나오므로, 창은 90% 아래로는 줄이지 않는다 (좁으면 내용이 답답하다)
        float ws = Math.Max(sc, DeviceDpi / 96f * 0.9f);
        int W(int v) => (int)Math.Round(v * ws);
        int w = Math.Min(W(tall ? 460 : 420), wa.Width);
        int h = tall ? Math.Max(Math.Min(W(560), wa.Height), wa.Height - S(40)) : Math.Min(W(560), wa.Height);

        Point p;
        if (open.Count > 0)
        {
            var last = open[^1].Bounds;
            p = new Point(last.Left - S(30), last.Top + S(30));
        }
        else if (settings.WinX is int x && settings.WinY is int y)
            p = new Point(x, y);
        else
            p = new Point(wa.Right - w - S(40), wa.Top + S(80));

        // 화면 밖으로 나가지 않게
        if (p.X < wa.Left || p.Y + h > wa.Bottom) p = new Point(wa.Right - w - S(40), wa.Top + S(80));
        p = new Point(Math.Max(wa.Left, Math.Min(p.X, wa.Right - w)), Math.Max(wa.Top, Math.Min(p.Y, wa.Bottom - h)));
        return new Rectangle(p, new Size(w, h));
    }

    async Task InitAsync(string url)
    {
        try
        {
            var env = await Env;
            await web.EnsureCoreWebView2Async(env);
            var cw = web.CoreWebView2;
            // 페이지(또는 그 안의 iframe)에 포커스가 있을 때도 브라우저 단계에서 Ctrl+W를 받는다
            try
            {
                var ctl = typeof(Microsoft.Web.WebView2.WinForms.WebView2)
                    .GetField("_coreWebView2Controller", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    ?.GetValue(web) as Microsoft.Web.WebView2.Core.CoreWebView2Controller;
                if (ctl != null)
                    ctl.AcceleratorKeyPressed += (_, e) =>
                    {
                        if (e.KeyEventKind == Microsoft.Web.WebView2.Core.CoreWebView2KeyEventKind.KeyDown &&
                            e.VirtualKey == (uint)Keys.W && (ModifierKeys & (Keys.Control | Keys.Alt | Keys.Shift)) == Keys.Control)
                        {
                            e.Handled = true;
                            if (!IsDisposed) BeginInvoke(() => { if (!IsDisposed) Close(); });
                        }
                    };
            }
            catch { }
            // 페이지 안에 포커스가 있어도 Ctrl+W로 창을 닫는다 (한글 입력 중에도 되도록 키 위치로 본다)
            await cw.AddScriptToExecuteOnDocumentCreatedAsync(
                "addEventListener('keydown',e=>{if(e.ctrlKey&&!e.altKey&&!e.shiftKey&&e.code==='KeyW'){e.preventDefault();e.stopImmediatePropagation();" +
                "try{chrome.webview.postMessage('mb:close')}catch(_){}}},true);" +
                // Esc도 창을 닫는다. 페이지가 먼저 쓴 Esc(팝업 닫기 등)는 건드리지 않는다
                "addEventListener('keydown',e=>{if(e.key==='Escape'&&!e.defaultPrevented&&!e.isComposing&&!document.fullscreenElement){" +
                "try{chrome.webview.postMessage('mb:close')}catch(_){}}});");
            cw.WebMessageReceived += (_, e) =>
            {
                string? m = null; try { m = e.TryGetWebMessageAsString(); } catch { }
                if (m == "mb:close" && !IsDisposed) BeginInvoke(() => { if (!IsDisposed) Close(); });
            };
            // 숨은 뮤직 창은 화면에 안 보이니 광고를 건드리지 않는다
            if (Webtoon.IsWebtoon(startUrl)) await cw.AddScriptToExecuteOnDocumentCreatedAsync(ScrollScript);
            else if (Coordi.IsCoordi(startUrl)) // 코디 창: 광고(사용자 요청)와 아래쪽 뮤직 플레이어 바를 숨긴다 (노래는 바의 뮤직이 맡는다)
            {
                await BlockAds(cw, env);
                await cw.AddScriptToExecuteOnDocumentCreatedAsync(
                    "(()=>{const add=()=>{if(document.getElementById('mbNoPlayer'))return;const s=document.createElement('style');s.id='mbNoPlayer';" +
                    "s.textContent='div.fixed.left-0.bottom-0.w-full{display:none!important}';(document.head||document.documentElement).appendChild(s)};" +
                    "if(document.documentElement)add();document.addEventListener('DOMContentLoaded',add);" +
                    // '새 드레스룸 v2도 써 보세요!' 안내 상자: 그 글과 'v2 써보기' 버튼을 함께 감싼 가장 작은 상자를 숨긴다
                    // (글이 들어 있는 가장 안쪽 요소에서 시작하고, 글이 400자 넘는 큰 상자는 건드리지 않는다)
                    "const R=/새\\s*드레스룸\\s*v2도\\s*써\\s*보세요/;" +
                    "const promo=()=>{try{for(const el of document.querySelectorAll('h1,h2,h3,h4,p,span,div,strong,b')){" +
                    "if(el.dataset.mbPromo||!R.test(el.textContent||'')||[...el.children].some(c=>R.test(c.textContent||'')))continue;let n=el;" +
                    "for(let i=0;i<8&&n&&n!==document.body&&(n.textContent||'').length<400;i++,n=n.parentElement){if(/v2\\s*써보기/.test(n.textContent||'')){" +
                    "n.style.setProperty('display','none','important');el.dataset.mbPromo='1';break}}}}catch(e){}};" +
                    "setInterval(promo,700)})();");
            }
            else if (!Music.IsMusic(startUrl)) await BlockAds(cw, env); // 숨김 스크립트 등록이 끝난 뒤에 페이지를 연다
            else
            {
                await cw.AddScriptToExecuteOnDocumentCreatedAsync(MusicScript);
                fxScriptId = await cw.AddScriptToExecuteOnDocumentCreatedAsync(FxScript(settings.MusicFx));
            }
            // 인벤은 작은 창에 맞는 모바일 화면으로 보이게 한다
            if (Inven.IsInven(startUrl) || Webtoon.IsWebtoon(startUrl))
                cw.Settings.UserAgent = Inven.MobileUA;
            // 새 창 링크: 사이트 안이면 또 새 창으로, 바깥이면 기본 브라우저로
            cw.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                // 화면 밖에 숨은 뮤직 창은 사용자가 누를 수 없으니, 페이지가 스스로 연 새 창이다.
                // 메애기 창을 화면에 꺼내거나 다른 창을 띄우지 않는다 (메애기 주소면 숨은 채로 그 안에서 옮긴다)
                if (parked)
                {
                    if (Music.IsMusic(e.Uri) && Uri.TryCreate(e.Uri, UriKind.Absolute, out var pu))
                        _ = cw.ExecuteScriptAsync($"window.mbGo&&window.mbGo({System.Text.Json.JsonSerializer.Serialize(pu.AbsolutePath.TrimEnd('/'))})");
                    return;
                }
                if (Music.IsMusic(e.Uri) && ShowMusic(e.Uri)) return;
                if (IsSite(e.Uri) || Inven.IsInven(e.Uri) || Music.IsMeaegi(e.Uri) || Webtoon.IsNaver(e.Uri)) new WebWindow(e.Uri, settings, sc).Show();
                else OpenExternal(e.Uri);
            };
            cw.DocumentTitleChanged += (_, _) => { Text = cw.DocumentTitle; grip.Invalidate(); };
            cw.Navigate(startUrl);
        }
        catch (Exception ex)
        {
            sharedEnv = null;
            MessageBox.Show(this, "사이트 화면을 띄우지 못했어요.\nMicrosoft Edge WebView2 런타임이 필요해요.\n\n" + ex.Message,
                "메이플 바", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // 광고 서버 요청을 막고, 남는 광고 칸은 숨긴다
    static readonly string[] AdHosts =
    {
        "googlesyndication.com", "doubleclick.net", "googleadservices.com", "adservice.google.",
        "adtrafficquality.google", "fundingchoicesmessages.google.com", "amazon-adsystem.com",
        "criteo.com", "taboola.com", "outbrain.com", "adnxs.com", "kakaoad", "adfit",
        "mobon.net", "dable.io",
        // 이 사이트가 쓰는 광고 회사(Venatus)와 연결된 곳들
        "vntsm.com", "vntsm.io", "venatusmedia.com", "rapidedge.io", "rubiconproject.com",
        "affec.tv", "hyth.io", "script.ac", "ad-delivery.net", "clean.gg", "ccgateway.net",
        "pubmatic.com", "openx.net", "adsrvr.org", "casalemedia.com", "33across.com",
        "sharethrough.com", "smartadserver.com", "teads.tv", "media.net", "id5-sync.com",
        // 인벤 자체 광고 서버
        "zicf.inven.co.kr", "ads-partners.coupang.com", "coupangad",
    };

    // 사이트가 직접 올리는 배너 이미지 폴더
    static bool IsAdPath(Uri u) =>
        u.Host.EndsWith("maplescouter.com") && u.AbsolutePath.Contains("/ads/", StringComparison.OrdinalIgnoreCase);

    const string AdCss =
        "ins.adsbygoogle,iframe[id^='google_ads'],iframe[id^='aswift'],div[id^='google_ads']," +
        "[id^='div-gpt-ad'],.adsbygoogle,.kakao_ad_area,[data-ad-client],[data-ad-slot]," +
        ".horizontal-sticky,[id*='google_ads_iframe'],a:has(img[src*='/ads/']),img[alt='banner ad']," +
        "#desktop_takeover,[id^='slot-'],[data-gamera-placement-container]," +
        // 썬데이메이플 이벤트 둥근 버튼
        "button:has(> img[src*='fab_sunday']),img[src*='fab_sunday']," +
        // 인벤 모바일 광고 칸 (아래 고정 배너, 글 사이·떠 있는 광고)
        "[id^='mobileTailAd'],.madfoh50,[class*='mobileDaNs'],[class*='mobileDaWide'],[class^='ibt'],iframe[src*='zicf.inven']," +
        "[id^='comAdM'],.ad-bbs-view-bottom,.mobileTopAd,iframe[src*='coupang']";
    // 환산주스탯 머리글(로고·로그인·닉네임 입력)과 맨 아래 안내: 바가 대신하므로 숨긴다. 인벤 같은 다른 사이트에는 넣지 않는다.
    const string MapleOnlyCss = ",div:has(> main) > div:first-child,div:has(> main) > footer";
    const string HideRule = "{display:none!important;height:0!important;min-height:0!important}";

    static async Task BlockAds(CoreWebView2 cw, CoreWebView2Environment env)
    {
        cw.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        cw.WebResourceRequested += (_, e) =>
        {
            if (!Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var u)) return;
            var host = u.Host;
            if (IsAdPath(u) || AdHosts.Any(h => host.Contains(h, StringComparison.OrdinalIgnoreCase)))
                e.Response = env.CreateWebResourceResponse(null, 403, "Blocked", "");
        };
        // 사이트가 화면을 다시 그리며 head를 바꿔도 숨김 스타일이 남도록 계속 확인한다
        await cw.AddScriptToExecuteOnDocumentCreatedAsync(
            "(()=>{const mp=location.hostname.endsWith('maplescouter.com');" +
            "const add=()=>{try{if(document.getElementById('mb-noad'))return;" +
            "const s=document.createElement('style');s.id='mb-noad';s.textContent=mp?\"" + AdCss + MapleOnlyCss + HideRule + "\":\"" + AdCss + HideRule + "\";" +
            "(document.head||document.documentElement).appendChild(s)}catch(x){}};" +
            "add();document.addEventListener('DOMContentLoaded',add);setInterval(add,700);" +
            // 화면 구석에 떠 있는 둥근 'TOP'(맨 위로) 버튼을 숨긴다: 글자가 TOP뿐이고 고정 위치인 작은 요소
            "const top=()=>{try{document.querySelectorAll('button,a,[role=button],[class*=top i],[id*=top i]').forEach(el=>{" +
            "if(el.dataset.mbTop||!/^\\W*top\\W*$/i.test((el.textContent||'').trim()))return;let n=el;" +
            "for(let i=0;i<4&&n&&n!==document.body;i++,n=n.parentElement){const p=getComputedStyle(n).position;if(p==='fixed'||p==='sticky'){" +
            "const r=n.getBoundingClientRect();if(r.width<160&&r.height<160){n.style.setProperty('display','none','important');el.dataset.mbTop='1'}return}}})}catch(e){}};" +
            "top();setInterval(top,700);" +
            // '다시 보지 않기'가 달린 공지 팝업(예: 보스컷 안내)은 체크하고 바로 닫는다. 사람 확인 창은 건드리지 않는다.
            "const noti=()=>{try{document.querySelectorAll(\"[role=dialog][data-state=open]\").forEach(d=>{" +
            "if(!/다시 보지 않기/.test(d.textContent))return;const x=d.querySelector('[data-slot=dialog-close]');if(!x)return;" +
            "d.style.opacity='0';const c=d.querySelector('[role=checkbox][data-state=unchecked]');if(c)c.click();setTimeout(()=>x.click(),60)})}catch(e){}};" +
            "if(mp)setInterval(noti,250);})();");
    }

    // ── 설정 메뉴에서 쓰는 기능 ──
    public static void SetTopMost(bool on)
    {
        foreach (var w in open.ToList()) w.TopMost = on || Webtoon.IsWebtoon(w.startUrl);
        RaiseWebtoons();
    }

    public static void CloseAll()
    {
        foreach (var w in open.ToList()) w.Close();
    }

    public static bool AnyOpen => open.Count > 0;

    /// <summary>
    /// 열린 창 중 캐릭터 페이지를 모두 새 닉네임으로 바꾼다.
    /// 지금 path를 보여주는 창이 하나라도 있으면 true.
    /// </summary>
    public static bool ApplyName(string name, string path)
    {
        bool showsPath = false;
        foreach (var w in open.ToList())
        {
            // 아직 불러오는 중인 창은 처음 열려던 주소를 기준으로 본다
            var src = w.web.Source;
            if (src == null || !IsSite(src.ToString())) Uri.TryCreate(w.startUrl, UriKind.Absolute, out src);
            if (src == null || !IsSite(src.ToString())) continue;
            var q = System.Web.HttpUtility.ParseQueryString(src.Query);
            bool hasName = !string.IsNullOrEmpty(q["name"]);
            var p = src.AbsolutePath.TrimEnd('/');
            if (!hasName && !SiteMenu.NamePages.Contains(p)) continue;

            if (string.Equals(p, path, StringComparison.OrdinalIgnoreCase)) showsPath = true;
            if (string.Equals(q["name"], name, StringComparison.OrdinalIgnoreCase)) continue;
            q["name"] = name;
            var url = new UriBuilder(src) { Query = q.ToString(), Fragment = "" }.Uri.ToString();
            w.startUrl = url;
            if (w.web.CoreWebView2 != null) w.web.CoreWebView2.Navigate(url);
            // 아직 준비 중이면 InitAsync가 startUrl로 연다
        }
        return showsPath;
    }

    /// <summary>열린 뮤직 창이 있으면 그 창에서 url을 열고 앞으로 가져온다.</summary>
    public static bool ShowMusic(string url)
    {
        var w = open.FirstOrDefault(x => Music.IsMusic(x.startUrl));
        if (w == null) return false;
        w.startUrl = url;
        // 페이지를 새로 읽으면 노래가 끊기므로 사이트 안 링크로 옮긴다
        if (w.web.CoreWebView2 != null && Uri.TryCreate(url, UriKind.Absolute, out var u))
            _ = w.web.CoreWebView2.ExecuteScriptAsync($"window.mbGo?window.mbGo({System.Text.Json.JsonSerializer.Serialize(u.AbsolutePath.TrimEnd('/'))}):location.href={System.Text.Json.JsonSerializer.Serialize(url)}");
        if (w.WindowState == FormWindowState.Minimized) w.WindowState = FormWindowState.Normal;
        w.Unpark(); w.Activate();
        return true;
    }

    static bool IsSite(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Host.EndsWith("maplescouter.com");

    static void OpenExternal(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    // ── 제목줄 없는 창 옮기기·크기 조절 ──
    // ── 웹툰: 정한 키를 누르고 있는 동안 부드럽게 스크롤 ──
    // 키 상태만 읽는다(GetAsyncKeyState). 게임에는 아무것도 보내지 않는다.
    // 대상: 웹툰 창에 포커스가 있으면 그 창, 메이플이 앞에 있으면 마지막으로 본 웹툰 창
    const string ScrollScript =
        "(()=>{let dir=0,speed=900,raf=0,last=0,start=0,el=null,pos=0,keys=[];" +
        "const typing=()=>{const a=document.activeElement;return a&&(a.isContentEditable||/^(INPUT|TEXTAREA|SELECT)$/.test(a.tagName))};" +
        "const pick=()=>{const se=document.scrollingElement||document.documentElement;if(se.scrollHeight>se.clientHeight+5)return se;" +
        "let b=se,bh=0;for(const e of document.querySelectorAll('div,main,section,article')){const s=getComputedStyle(e);" +
        "if(/(auto|scroll)/.test(s.overflowY)&&e.scrollHeight>e.clientHeight+5&&e.clientHeight>bh){b=e;bh=e.clientHeight}}return b};" +
        "const step=t=>{if(!dir||!el){raf=0;return}const dt=Math.min(50,t-last);last=t;" +
        "if(Math.abs(el.scrollTop-pos)>3)pos=el.scrollTop;" + // 사용자가 직접 굴렸으면 거기서 이어 간다
        "const ramp=Math.min(1,(t-start)/180);pos+=dir*speed*ramp*dt/1000;" +
        "pos=Math.max(0,Math.min(pos,el.scrollHeight-el.clientHeight));el.scrollTop=pos;raf=requestAnimationFrame(step)};" +
        "window.__mbScroll=(d,sp,k)=>{if(sp)speed=sp;if(k)keys=k;if(d&&document.hasFocus()&&typing())d=0;" +
        "if(d===dir)return;dir=d;if(dir&&!raf){el=pick();pos=el.scrollTop;last=start=performance.now();raf=requestAnimationFrame(step)}};" +
        // 창에 포커스가 있을 때 같은 키로 페이지가 따로 움직이지 않게 막는다 (입력 칸은 그대로)
        "addEventListener('keydown',e=>{if(keys.includes(e.keyCode)&&!e.ctrlKey&&!e.altKey&&!typing())e.preventDefault()},true);" +
        // 다음 화/이전 화: 페이지의 버튼을 누르고, 없으면 주소의 회차 번호(no)를 하나 옮긴다
        "window.__mbEp=d=>{if(document.hasFocus()&&typing())return;const re=d>0?/다음\\s*화/:/이전\\s*화/;" +
        "const el=[...document.querySelectorAll('a,button,[role=button]')].find(e=>{const t=(e.textContent||'').trim();" +
        "return re.test(e.getAttribute('aria-label')||'')||(t.length<12&&re.test(t))});" +
        "if(el){el.click();return}try{const u=new URL(location.href),n=parseInt(u.searchParams.get('no'));" +
        "if(n>0&&n+d>0){u.searchParams.set('no',n+d);location.href=u.href}}catch(_){}};})();";

    static readonly System.Windows.Forms.Timer scrollTimer = CreateScrollTimer();
    static WebWindow? lastWebtoon, scrollWin;
    static int scrollDir;
    static bool nextWas = true, prevWas = true; // 켜자마자 눌려 있던 키는 무시한다

    static System.Windows.Forms.Timer CreateScrollTimer()
    {
        var t = new System.Windows.Forms.Timer { Interval = 30 };
        t.Tick += (_, _) => ScrollTick();
        return t;
    }

    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
    static bool KeyIsDown(int vk) => vk != 0 && (GetAsyncKeyState(vk) & 0x8000) != 0;

    static readonly List<WebWindow> scrollWins = new();

    static void ScrollTick()
    {
        // 30ms마다 돌아서 LINQ 대신 그냥 반복문으로 고른다 (고르는 순서와 결과는 같다)
        bool anyToon = false;
        foreach (var x in open) if (!x.IsDisposed && Webtoon.IsWebtoon(x.startUrl)) { anyToon = true; break; }
        if (!anyToon) { scrollTimer.Stop(); scrollWin = null; scrollDir = 0; return; }
        var wins = scrollWins;
        wins.Clear();
        foreach (var x in open)
            if (!x.IsDisposed && x.Visible && x.WindowState != FormWindowState.Minimized && Webtoon.IsWebtoon(x.startUrl)) wins.Add(x);
        var fg = GetForegroundWindow();
        WebWindow? target = null;
        foreach (var x in wins) if (x.Handle == fg) { target = x; break; }
        if (target == null && wins.Count > 0 && Game.Foreground())
            target = lastWebtoon != null && wins.Contains(lastWebtoon) ? lastWebtoon : wins[^1];
        int dir = 0;
        if (target != null)
        {
            var s = target.settings;
            dir = (KeyIsDown(s.WebtoonDownKey) ? 1 : 0) - (KeyIsDown(s.WebtoonUpKey) ? 1 : 0);
            // 다음 화/이전 화는 누르는 순간 한 번만
            bool next = KeyIsDown(s.WebtoonNextKey), prev = KeyIsDown(s.WebtoonPrevKey);
            if (next && !nextWas) target.SendEpisode(1);
            if (prev && !prevWas) target.SendEpisode(-1);
            nextWas = next; prevWas = prev;
        }
        else nextWas = prevWas = false;
        if (target == scrollWin && dir == scrollDir) return;
        if (scrollWin != null && scrollWin != target && !scrollWin.IsDisposed) scrollWin.SendScroll(0);
        scrollWin = target; scrollDir = dir;
        target?.SendScroll(dir);
    }

    void SendScroll(int dir)
    {
        try
        {
            var keys = $"[{settings.WebtoonUpKey},{settings.WebtoonDownKey},{settings.WebtoonNextKey},{settings.WebtoonPrevKey}]";
            _ = web.CoreWebView2?.ExecuteScriptAsync($"window.__mbScroll&&window.__mbScroll({dir},{Math.Clamp(settings.WebtoonSpeed, 100, 5000)},{keys})");
        }
        catch { }
    }


    /// <summary>웹툰 창들을 포커스는 그대로 둔 채 맨 위로 올린다.</summary>
    static void RaiseWebtoons()
    {
        foreach (var w in open.ToList())
            if (!w.IsDisposed && w.IsHandleCreated && w.Visible && Webtoon.IsWebtoon(w.startUrl))
                SetWindowPos(w.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_KEEP);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (!Webtoon.IsWebtoon(startUrl)) BeginInvoke(RaiseWebtoons);
    }

    void SendEpisode(int d)
    {
        try { _ = web.CoreWebView2?.ExecuteScriptAsync($"window.__mbEp&&window.__mbEp({d})"); } catch { }
    }

    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);

    void DragMove()
    {
        if (WindowState == FormWindowState.Maximized) return;
        ReleaseCapture();
        SendMessage(Handle, 0xA1 /*WM_NCLBUTTONDOWN*/, (IntPtr)2 /*HTCAPTION*/, IntPtr.Zero);
    }

    void ToggleMax()
    {
        if (WindowState == FormWindowState.Maximized) { WindowState = FormWindowState.Normal; Padding = new Padding(edge); }
        else
        {
            MaximizedBounds = Screen.FromControl(this).WorkingArea;
            Padding = Padding.Empty;
            WindowState = FormWindowState.Maximized;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int round = 2; // 윈도우 11 둥근 모서리
        int noBorder = unchecked((int)0xFFFFFFFE); // 윈도우가 그리는 회색 테두리 끄기
        try
        {
            DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int));
            DwmSetWindowAttribute(Handle, 34, ref noBorder, sizeof(int));
        }
        catch { }
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.Style |= 0x20000;       // 작업 표시줄 클릭으로 최소화
            cp.ClassStyle |= CS_DROPSHADOW;  // 그림자
            if (Music.IsMusic(startUrl)) cp.ExStyle |= WS_EX_TOOLWINDOW; // 뮤직 창은 Alt+Tab에도 안 나오게
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg != 0x84 /*WM_NCHITTEST*/ || (int)m.Result != 1 /*HTCLIENT*/ || WindowState == FormWindowState.Maximized) return;
        var p = PointToClient(new Point((short)((long)m.LParam & 0xFFFF), (short)(((long)m.LParam >> 16) & 0xFFFF)));
        int g = edge + S(3);
        bool l = p.X < g, r = p.X >= ClientSize.Width - g, t = p.Y < edge, b = p.Y >= ClientSize.Height - g;
        int hit = t && l ? 13 : t && r ? 14 : b && l ? 16 : b && r ? 17 : l ? 10 : r ? 11 : t ? 12 : b ? 15 : 0;
        if (hit != 0) m.Result = (IntPtr)hit;
    }

    // 펼치기: 크게 볼 때의 크기. 화면 밖으로 나가지 않게 맞추고, 접으면 펼치기 전 자리·크기로 돌아간다
    static readonly Size BigSize = new(1040, 565);
    readonly ToolTip alphaTip = new();
    readonly AlphaSlider alphaBar;
    /// <summary>창 투명도(%)를 바꾼다. 너무 흐려져 못 찾는 일이 없게 30% 아래로는 내리지 않는다.</summary>
    void SetAlpha(int pct)
    {
        pct = Math.Clamp(pct, AlphaSlider.Min, 100);
        Opacity = pct / 100.0;
        alphaBar.Value = pct;
        settings.WinOpacity = pct;
        settings.Save();
        alphaTip.Show($"투명도 {pct}%", grip, grip.PointToClient(Cursor.Position) + new Size(S(12), S(12)), 900);
    }

    Rectangle? smallBounds;
    /// <summary>크게 보기(펼치기)나 최대화 상태</summary>
    bool Expanded => smallBounds != null || WindowState == FormWindowState.Maximized;
    void ToggleBig()
    {
        if (WindowState != FormWindowState.Normal) WindowState = FormWindowState.Normal;
        if (smallBounds is Rectangle r) { Bounds = r; smallBounds = null; return; }
        smallBounds = Bounds;
        var wa = Screen.FromControl(this).WorkingArea;
        int w = Math.Min(BigSize.Width, wa.Width), h = Math.Min(BigSize.Height, wa.Height);
        int x = Math.Max(wa.Left, Math.Min(Left, wa.Right - w)), y = Math.Max(wa.Top, Math.Min(Top, wa.Bottom - h));
        Bounds = new Rectangle(x, y, w, h);
    }

    // 창이 앞으로 오면 페이지에 키보드 포커스를 줘서 Ctrl+W 등이 바로 먹게 한다
    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        if (Webtoon.IsWebtoon(startUrl)) lastWebtoon = this;
        else RaiseWebtoons(); // 다른 창이 앞으로 와도 웹툰 창이 그 위에 있게
        if (!web.ContainsFocus) web.Focus();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.F5) { web.CoreWebView2?.Reload(); return true; }
        if (keyData == (Keys.Alt | Keys.Left)) { if (web.CanGoBack) web.GoBack(); return true; }
        if (keyData == (Keys.Control | Keys.W) || keyData == Keys.Escape) { Close(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // 옮기면 다음 첫 창 위치로 기억한다
    protected override void OnResizeEnd(EventArgs e)
    {
        base.OnResizeEnd(e);
        if (WindowState != FormWindowState.Normal) return;
        settings.WinX = Left; settings.WinY = Top;
        settings.Save();
    }

    // 뮤직 창은 ✕를 눌러도 숨기기만 한다 (노래는 계속). 바의 '뮤직 끄기'로 닫는다.
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (Music.IsMusic(startUrl) && !reallyClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Park();
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (Music.IsMusic(startUrl)) { musicTimer.Dispose(); MusicChanged?.Invoke(null); }
        open.Remove(this);
        base.OnFormClosed(e);
    }
}
