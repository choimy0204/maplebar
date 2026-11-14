using Microsoft.Web.WebView2.Core;

namespace MapleBar;

public partial class WebWindow
{
    // ── 실시간 방송 (SOOP·치지직): 목록 창에서 방송을 누르면 재생 창 하나에서 영상만 본다 ──
    // 채팅은 재생 창 제목 줄의 '채팅' 버튼으로 따로 연다. 광고는 막지 않고, 로그인은 사용자가 직접 한다.

    // 목록 창: 방송 링크를 누르면 페이지가 열지 않고 메이플 바에 알린다 (치지직은 주소만 바꾸며 옮겨 다닌다)
    const string LiveCatchScript =
        "(()=>{const ok=u=>/^play\\.(sooplive\\.(com|co\\.kr)|afreecatv\\.com)$/.test(u.hostname)&&u.pathname.length>1||" +
        "/^vod\\.(sooplive\\.(com|co\\.kr)|afreecatv\\.com)$/.test(u.hostname)&&u.pathname.startsWith('/player/')||" +
        "u.hostname==='chzzk.naver.com'&&/^\\/(live|video)\\/[^/]+\\/?$/.test(u.pathname)||" +
        "u.hostname==='live.nexon.com'&&/^\\/(lives|replays)\\/[^/]+\\/?$/.test(u.pathname);" +
        "addEventListener('click',e=>{if(e.button!==0||e.ctrlKey||e.shiftKey||e.metaKey||e.altKey)return;" +
        "const a=e.composedPath().find(n=>n&&n.tagName==='A'&&n.href);if(!a)return;let u;try{u=new URL(a.href,location.href)}catch(_){return}" +
        "if(!ok(u))return;e.preventDefault();e.stopImmediatePropagation();try{chrome.webview.postMessage('mb:live:'+u.href)}catch(_){}},true)})();";

    // 재생 창: 가장 큰 영상과 그 조작 버튼이 든 상자만 창에 꽉 채운다 (SOOP은 #player)
    const string LiveVideoOnlyScript = """
        (()=>{const css=`html.mblv,html.mblv body{overflow:hidden!important}
        html.mblv [data-mbbox]{position:fixed!important;left:0!important;top:0!important;right:0!important;bottom:0!important;width:auto!important;height:auto!important;max-width:none!important;max-height:none!important;margin:0!important;z-index:2147483646!important;background:#000!important;transform:none!important;border-radius:0!important}
        html.mblv [data-mbfill]{width:100%!important;height:100%!important;max-width:none!important;max-height:none!important;left:0!important;top:0!important;margin:0!important}
        html.mblv video[data-mbmain]{object-fit:contain!important}
        html.mblv [data-mbhide]{display:none!important}
        html.mblv.mbside [data-mbbox]{right:340px!important}
        html.mblv [data-mbside]{position:fixed!important;top:0!important;right:0!important;bottom:0!important;left:auto!important;width:340px!important;height:auto!important;max-width:none!important;max-height:none!important;margin:0!important;z-index:2147483646!important;transform:none!important;display:block!important}
        html.mblv [data-mbside] #chatbox{position:absolute!important;inset:0!important;width:auto!important;height:auto!important;max-height:none!important}
        html.mblv [data-mbside] .chatting-item-wrap{flex:1 1 auto!important;height:auto!important;max-height:none!important;min-height:0!important}
        html.mblv.mbnxp [data-mbbox]{top:32px!important}
        html.mblv.mbnxp video[data-mbmain]{height:calc(100% - 36px)!important}
        html.mblv.mbnxp [class*="MiniController_root"]{opacity:1!important;visibility:visible!important}
        html.mblv.mbnxp [class*="MiniController_root"]:not([class*="MiniController_entered"]) [class*="MiniController_middle"]{opacity:0!important;pointer-events:none!important}
        html.mblv.mbnxp [class*="MiniController_top"]{top:auto!important;bottom:7px!important;left:auto!important;right:10px!important;width:auto!important;height:22px!important;padding:0!important;background:none!important;justify-content:flex-start!important;opacity:1!important;visibility:visible!important;transform:none!important;pointer-events:auto!important;z-index:3!important}
        html.mblv.mbnxp [class*="MiniController_bottom"]{top:auto!important;bottom:0!important;height:36px!important;opacity:1!important;visibility:visible!important;transform:none!important;background:#111!important;pointer-events:auto!important}
        html.mblv.mbnxp [class*="MiniController_bottom"] [class*="ProgressBar_root"]{position:absolute!important;left:10px!important;right:206px!important;width:auto!important;top:15px!important;margin:0!important}
        html.mblv.mbnxp [class*="MiniController_actions"]{display:none!important}
        html.mblv.mbnxp [class*="SheetPortal_root"]:has([class*="TextButtonGroup_root"]){display:block!important;position:fixed!important;left:auto!important;right:10px!important;top:auto!important;bottom:44px!important;width:240px!important;height:auto!important;z-index:20!important;background:none!important}
        html.mblv.mbnxp [class*="SheetPortal_root"] [class*="BottomSheet_root"]:has([class*="ChatBottomSheet"],[class*="ReplayChatPanel"]){display:none!important}
        html.mblv.mbnxp [class*="SheetPortal_root"] [class*="SheetPortal_inner"],html.mblv.mbnxp [class*="SheetPortal_root"] [class*="SheetPortal_slot"],html.mblv.mbnxp [class*="SheetPortal_root"] [class*="BottomSheetSlot_root"],html.mblv.mbnxp [class*="SheetPortal_root"] [class*="BottomSheet_root"]{position:static!important;width:auto!important;height:auto!important;transform:none!important;inset:auto!important;max-height:none!important}
        html.mblv.mbnoch [data-mbside]{display:none!important}
        html.mblv.mbnoch [data-mbbox]{right:0!important}
        html:not(.mblv):not(.mbshow){background:#000!important}
        html:not(.mblv):not(.mbshow) body{visibility:hidden!important}`;
        const add=()=>{if(document.getElementById('mbLv'))return;const s=document.createElement('style');s.id='mbLv';s.textContent=css;(document.head||document.documentElement).appendChild(s)};
        const area=e=>{const r=e.getBoundingClientRect();return r.width*r.height};
        // 치지직은 영상이 따로 감싼 칸(shadow) 안에 있어서 그 안까지 찾는다
        const deep=sel=>{const out=[];const walk=n=>{out.push(...n.querySelectorAll(sel));for(const e of n.querySelectorAll('*'))if(e.shadowRoot)walk(e.shadowRoot)};walk(document);return out};
        const up=e=>e.parentElement||(e.parentNode instanceof ShadowRoot?e.parentNode.host:null);
        const pick=()=>{let best=null,a=10000;for(const v of deep('video')){if(v.id==='pipMedia')continue;const s=area(v);if(s>a){a=s;best=v}}return best};
        // 치지직 '확장 프로그램 설치' 안내: 설치 없이 일반 화질로 본다
        const skip=()=>{if(location.hostname!=='chzzk.naver.com')return;for(const b of deep('button'))if((b.textContent||'').replace(/\s/g,'').includes('설치없이')){b.click();break}};
        let main=null,box=null;
        const unmark=()=>deep('[data-mbbox],[data-mbfill],[data-mbmain],[data-mbhide]').forEach(e=>{for(const k of ['data-mbbox','data-mbfill','data-mbmain','data-mbhide'])e.removeAttribute(k)});
        const tick=()=>{const r=document.documentElement;if(!r)return;add();skip();        if(main&&main.isConnected&&box&&box.isConnected&&area(main)>10000){}else{
        r.classList.remove('mblv');unmark();main=pick();box=null;if(!main)return;
        const sp=document.getElementById('player');
        // 넥슨 라이브는 처음에 영상이 페이지만큼 커서 크기로 찾으면 페이지 전체를 잡는다. 영상 칸을 이름으로 찾는다
        const nb=location.hostname==='live.nexon.com'?main.closest('[class*="Content_root"]'):null;
        if(sp&&sp.contains(main))box=sp;else if(nb)box=nb;else{const va=area(main);box=main;for(let e=up(main);e&&e!==document.body;e=up(e)){if(area(e)>va*1.6+4000)break;box=e}}
        while(box.getRootNode() instanceof ShadowRoot)box=box.getRootNode().host;
        for(let e=main;e&&e!==box;e=up(e)){e.setAttribute('data-mbfill','');const rn=e.getRootNode();if(rn instanceof ShadowRoot&&!rn.getElementById('mbLv')){const s=document.createElement('style');s.id='mbLv';s.textContent='[data-mbfill]{width:100%!important;height:100%!important;max-width:none!important;max-height:none!important;margin:0!important}video[data-mbmain]{object-fit:contain!important}';rn.appendChild(s)}}
        main.setAttribute('data-mbmain','');box.setAttribute('data-mbbox','');
        setTimeout(()=>dispatchEvent(new Event('resize')),300);setTimeout(()=>dispatchEvent(new Event('resize')),1500)}
        r.classList.add('mblv');
        // 넥슨 라이브는 볼륨 줄이 영상 위쪽에 있어서, 마우스를 올리면 뜨는 제목 줄에 가리지 않게 한 줄 내린다
        if(location.hostname==='live.nexon.com')r.classList.add('mbnxp');
        // SOOP은 채팅도 같은 창 오른쪽에 둔다 (채팅 창을 따로 열면 재생 창이 멈춘다)
        const soop=/(^|\.)(sooplive\.(com|co\.kr)|afreecatv\.com)$/.test(location.hostname);
        const ch=soop?document.getElementById('chatting_area'):null;
        if(ch){if(!ch.hasAttribute('data-mbside')){document.querySelectorAll('[data-mbside]').forEach(e=>e.removeAttribute('data-mbside'));ch.setAttribute('data-mbside','');setTimeout(()=>dispatchEvent(new Event('resize')),300)}
        for(let e=ch;e;e=e.parentElement)e.removeAttribute('data-mbhide');r.classList.add('mbside');
        // 화면 모드면 채팅이 영상 위에 겹쳐 나오므로 풀고, 채팅이 닫혀 있으면 연다 (3초에 한 번, 최대 6번)
        const now=Date.now(),wp=document.getElementById('webplayer');
        if(!(window.__mbsc>now)&&(window.__mbsn|0)<6){let btn=null;
        if(document.body.classList.contains('screen_mode'))btn=document.querySelector('.btn_screen_mode');
        else if(wp&&!wp.classList.contains('chat_open')&&!r.classList.contains('mbnoch'))btn=document.querySelector('.view_ctrl li.btn_chat');
        if(btn){btn.click();window.__mbsc=now+3000;window.__mbsn=(window.__mbsn|0)+1}}}else r.classList.remove('mbside');
        // 영상 상자 둘레의 다른 칸(위쪽 메뉴·나중에 뜨는 안내 창)은 숨긴다
        const keep=s=>ch&&s.contains(ch);
        for(const t of ch?[box,ch]:[box])for(let e=t;e.parentElement&&e!==document.body;e=e.parentElement)for(const s of e.parentElement.children)if(s!==e&&!keep(s)&&!s.contains(box)&&!s.hasAttribute('data-mbhide')&&!/^(SCRIPT|STYLE|LINK)$/.test(s.tagName))s.setAttribute('data-mbhide','');
        for(const p of ch?[box.parentElement,ch.parentElement]:[box.parentElement])for(let e=p;e&&e!==document.body;e=e.parentElement){const t=e.style;t.setProperty('transform','none','important');t.setProperty('filter','none','important');t.setProperty('contain','none','important');t.setProperty('will-change','auto','important');t.setProperty('perspective','none','important')}};
        // 영상만 남기기 전에는 페이지를 검게 가린다 (10초 안에 못 찾으면 그냥 보여 준다)
        setTimeout(()=>document.documentElement.classList.add('mbshow'),10000);
        let n=0;const loop=()=>{tick();setTimeout(loop,n++<50?200:700)};
        loop();document.addEventListener('DOMContentLoaded',tick)})();
        """;

    // 채팅 창: 영상은 소리 없이 멈춰 두고, 채팅 칸만 창에 꽉 채운다 (그 둘레의 다른 칸은 숨긴다)
    const string LiveChatScript = """
        (()=>{const SEL=['#chatting_area','[class*="live_chatting_container"]','[class*="vod_chatting"]','aside[class*="chatting"]'];
        const css=`html.mbch,html.mbch body{overflow:hidden!important}
        html.mbch [data-mbhide]{display:none!important}
        html.mbch [data-mbchat]{position:fixed!important;left:0!important;top:0!important;right:0!important;bottom:0!important;width:auto!important;height:auto!important;max-width:none!important;max-height:none!important;margin:0!important;z-index:2147483646!important;transform:none!important}
        html.mbch #chatting_area #chatbox{position:absolute!important;inset:0!important;width:auto!important;height:auto!important;max-height:none!important}
        html.mbch #chatting_area .chatting-item-wrap{flex:1 1 auto!important;height:auto!important;max-height:none!important;min-height:0!important}
        html.mbch [data-mbchat] [class*="SheetPortal_inner"],html.mbch [data-mbchat] [class*="SheetPortal_slot"],html.mbch [data-mbchat] [class*="BottomSheetSlot_root"],html.mbch [data-mbchat] [class*="BottomSheet_root"],html.mbch [data-mbchat] [class*="BottomSheet_inner"]{position:absolute!important;inset:0!important;width:auto!important;height:auto!important;max-height:none!important;transform:none!important;margin:0!important;border-radius:0!important}
        html.mbch [data-mbchat] [class*="BottomSheet_header__close"]{display:none!important}`;
        const add=()=>{if(document.getElementById('mbCh'))return;const s=document.createElement('style');s.id='mbCh';s.textContent=css;(document.head||document.documentElement).appendChild(s)};
        const stop=()=>document.querySelectorAll('video,audio').forEach(v=>{try{v.muted=true;if(!v.paused)v.pause()}catch(e){}});
        document.addEventListener('play',e=>{try{e.target.muted=true;e.target.pause()}catch(_){}},true);
        // 치지직: 위쪽 머리줄(채팅 · 100% 확대)과 주간 후원 랭킹 칸은 숨긴다.
        // 글자로 찾아서 채팅 목록·입력 칸이 들어 있지 않은 가장 큰 칸까지 올라가 숨긴다
        const zz=()=>{if(location.hostname!=='chzzk.naver.com'||!document.body)return;const root=document.body;
        if((root.textContent||'').length<300)return; // 채팅이 아직 안 들어왔으면 기다린다
        const inp=root.querySelector('textarea,[contenteditable="true"],[contenteditable=""],input[type="text"]');
        const tw=document.createTreeWalker(root,NodeFilter.SHOW_TEXT);const hits=[];let n;
        while(n=tw.nextNode()){const v=n.nodeValue.trim();const pe=n.parentElement;if(v==='주간 후원 랭킹'||/^\d{2,3}%$/.test(v)&&pe&&pe.parentElement&&pe.parentElement.querySelector('button'))hits.push(pe)}
        for(let e of hits){if(!e||e.closest('[data-mbhide]'))continue;
        while(e.parentElement&&e.parentElement!==root){const p=e.parentElement;if(inp&&p.contains(inp))break;if((p.textContent||'').length>(e.textContent||'').length+150)break;e=p}
        if(!(inp&&e.contains(inp)))e.setAttribute('data-mbhide','')}};
        let cur=null;const tick=()=>{const r=document.documentElement;if(!r)return;add();stop();zz();if(location.hostname==='chzzk.naver.com')r.classList.add('mbch');
        // 넥슨 라이브: 좁은 창에서는 채팅이 아래에서 올라오는 창에 들어 있다. 닫혀 있으면 '채팅 다시보기'로 연다 (2초에 한 번)
        // 다시보기 채팅은 영상 시간에 맞춰 나오므로, 재생 창의 시간(__mbt)으로 멈춘 영상의 자리만 옮긴다
        let el=null;if(location.hostname==='live.nexon.com'){
        if(typeof window.__mbt==='number')for(const v of document.querySelectorAll('video'))if(Math.abs(v.currentTime-window.__mbt)>2)try{v.currentTime=window.__mbt}catch(e){}
        el=document.querySelector('[class*="SheetPortal_root"]');const now=Date.now();
        if(el&&!el.querySelector('[class*="ChatBottomSheet"]')&&!(window.__mbjc>now)){const b=document.querySelector('[class*="JoinChat_trigger"]');if(b){b.click();window.__mbjc=now+2000}}}
        else for(const s of SEL){el=document.querySelector(s);if(el)break}
        if(!el){if(location.hostname!=='chzzk.naver.com')r.classList.remove('mbch');return}
        if(el!==cur){cur=el;document.querySelectorAll('[data-mbhide],[data-mbchat]').forEach(e=>{e.removeAttribute('data-mbhide');e.removeAttribute('data-mbchat')});
        el.setAttribute('data-mbchat','');
        for(let e=el;e.parentElement&&e!==document.body;e=e.parentElement)for(const s of e.parentElement.children)if(s!==e&&!/^(SCRIPT|STYLE|LINK)$/.test(s.tagName))s.setAttribute('data-mbhide','');
        setTimeout(()=>dispatchEvent(new Event('resize')),300)}
        r.classList.add('mbch');
        for(let e=el;e.parentElement&&e!==document.body;e=e.parentElement)for(const s of e.parentElement.children)if(s!==e&&!s.hasAttribute('data-mbhide')&&!/^(SCRIPT|STYLE|LINK)$/.test(s.tagName))s.setAttribute('data-mbhide','');
        for(let e=el.parentElement;e&&e!==document.body;e=e.parentElement){const t=e.style;t.setProperty('transform','none','important');t.setProperty('filter','none','important');t.setProperty('contain','none','important')}};
        tick();document.addEventListener('DOMContentLoaded',tick);setInterval(tick,500)})();
        """;

    static WebWindow? ChatWindow() => FindOpen(SiteKind.LiveChat);

    /// <summary>재생 창의 방송이 바뀌면 채팅 창도 그 방송의 채팅으로 바꾼다.</summary>
    void SyncChat()
    {
        var c = ChatWindow();
        if (c == null || IsDisposed) return;
        var url = Live.ChatUrl(web.Source?.ToString());
        if (url == null || url == c.startUrl) return;
        c.startUrl = url; // 아직 준비 중이면 InitAsync가 이 주소로 연다
        try { c.web.CoreWebView2?.Navigate(url); } catch { }
    }

    async Task InitLive(CoreWebView2 cw)
    {
        if (kind == SiteKind.LivePlayer)
        {
            await cw.AddScriptToExecuteOnDocumentCreatedAsync(LiveVideoOnlyScript);
            cw.SourceChanged += (_, _) => SyncChat();
            // 넥슨 라이브 다시보기: 채팅 창이 영상과 같은 시간의 채팅을 보이게 1초마다 재생 시간을 넘긴다
            if (IsDisposed) return; // 초기화 중에 창이 닫혔으면 타이머를 만들지 않는다
            var sync = new System.Windows.Forms.Timer { Interval = 1000 };
            sync.Tick += async (_, _) =>
            {
                try
                {
                    if (ChatWindow() is not { } c || c.web.CoreWebView2 == null) return;
                    var src = web.Source;
                    if (src == null || !Live.IsNexon(src.ToString()) || !src.AbsolutePath.StartsWith("/replays/", StringComparison.OrdinalIgnoreCase)) return;
                    var t = await cw.ExecuteScriptAsync("(()=>{const v=document.querySelector('video[data-mbmain]')||document.querySelector('video');return v?Math.floor(v.currentTime):-1})()");
                    if (double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s) && s >= 0)
                        await c.web.CoreWebView2.ExecuteScriptAsync("window.__mbt=" + s.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
                catch { } // 1초마다 도는 틱이고, 닫히는 중의 접근 실패는 정상이다
            };
            sync.Start();
            FormClosed += (_, _) => sync.Dispose();
            return;
        }
        if (kind == SiteKind.LiveChat)
        {
            cw.IsMuted = true; // 채팅 창에서는 소리가 나지 않게
            await cw.AddScriptToExecuteOnDocumentCreatedAsync(LiveChatScript);
            return;
        }
        await cw.AddScriptToExecuteOnDocumentCreatedAsync(LiveCatchScript);
        cw.WebMessageReceived += (_, e) =>
        {
            string? m = null; try { m = e.TryGetWebMessageAsString(); } catch { }
            if (m == null || !m.StartsWith("mb:live:", StringComparison.Ordinal)) return;
            var u = m[8..];
            if (Live.IsPlayer(u) && !IsDisposed) BeginInvoke(() => ShowLive(u, settings, sc));
        };
        // 놓친 경우: 목록 창이 방송 화면으로 가면 재생 창으로 넘기고 목록으로 돌아온다
        cw.SourceChanged += (_, _) =>
        {
            var s = cw.Source;
            if (!Live.IsPlayer(s)) return;
            try { _ = cw.ExecuteScriptAsync("document.querySelectorAll('video').forEach(v=>{try{v.muted=true;v.pause()}catch(e){}})"); } catch { }
            ShowLive(s, settings, sc);
            if (cw.CanGoBack) cw.GoBack(); else cw.Navigate(Live.IsLive(startUrl) ? startUrl : Live.Soop);
        };
    }

    /// <summary>방송 재생 창에서 url을 연다. 창이 있으면 방송만 바꾸고, 없으면 새로 띄운다.</summary>
    public static void ShowLive(string url, Settings st, float scale)
    {
        var w = FindOpen(SiteKind.LivePlayer);
        if (w == null) { new WebWindow(url, st, scale).Show(); return; }
        w.startUrl = url; // 아직 준비 중이면 InitAsync가 이 주소로 연다
        try { w.web.CoreWebView2?.Navigate(url); } catch { }
        if (w.WindowState == FormWindowState.Minimized) w.WindowState = FormWindowState.Normal;
        w.Activate();
    }
}
