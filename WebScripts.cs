namespace MapleBar;

/// <summary>웹 창에 넣는 자바스크립트·CSS 글자. WebWindow는 `using static`으로 이름만 불러 쓴다.</summary>
static partial class WebScripts
{
    // 재생 바에서 곡 정보를 읽고 버튼(0 이전, 1 재생/일시정지, 2 다음)을 누르는 함수
    public const string MusicScript =
        // 메애기 화면에서 찾는 것들. 사이트 화면이 바뀌면 여기만 고친다
        // bar 아래 재생 바, row 곡 한 줄, btn 줄 안의 재생 칸(썸네일), title·sub 줄 안의 제목·지역, search 검색 칸의 안내 글
        "(()=>{const S={bar:'div.fixed.left-0.bottom-0.w-full',row:'div.group.flex.items-center.gap-2',btn:':scope>div.relative.cursor-pointer'," +
        "title:'div.text-sm',sub:'div.text-xs',search:'노래 검색'};const P=S.bar;" +
        "const T=(x,q)=>{const e=x.querySelector(q);return e?e.textContent.trim():''};" +
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
        // 서서히 키우기는 타이머 대신 곡의 재생 시각 알림(timeupdate)에 맞춰 한다.
        // 숨은 창에서는 타이머가 1분씩 밀려 소리가 0인 채로 멈춘 것처럼 들릴 수 있다
        // 오디오가 페이지에 붙어 있지 않을 수 있어 알림은 오디오에 직접 걸고, 앱이 1초마다 부르는 mbState에서도 한 번 더 맞춘다
        "const fadeStep=()=>{const f=fade;if(!f)return;const a=f.a,n=Date.now();if(n<f.at){a.volume=0;return}" +
        "if(f.reset){f.reset=false;try{a.currentTime=0}catch(_){}}const k=Math.min(1,(n-f.at)/900);try{a.volume=V*k}catch(_){}if(k>=1)fade=null};" +
        "const fadeIn=(a,wait)=>{a.volume=0;fade={a,at:Date.now()+wait*1000,reset:true};" +
        "if(!a.__mbF){a.__mbF=1;a.addEventListener('timeupdate',fadeStep)}setTimeout(fadeStep,wait*1000+50)};" +
        "const op=HTMLMediaElement.prototype.play;HTMLMediaElement.prototype.play=function(){window.__mbA=this;" +
        "const src=this.currentSrc||this.src||'';const isNew=src&&src!==lastSrc;if(src)lastSrc=src;" +
        // 레코드 소리는 5초 쿨타임: 쿨타임 중에 또 넘기면 소리 없이 바로 틀고 쿨타임을 다시 5초부터 센다
        "try{this.loop=REP;if(isNew&&FX){const now=Date.now(),cool=now-fxAt<5000;fxAt=now;" +
        "if(cool){fade=null;this.volume=V}else{vinyl();fadeIn(this,FXLEN)}}else this.volume=V}catch(e){}return op.apply(this,arguments)};" +
        "window.mbSetFx=v=>{FX=!!v};" +
        "window.mbMode=m=>{if(m==='rep'){REP=!REP;localStorage.setItem('mbRep',REP?'1':'0');const a=window.__mbA;if(a)a.loop=REP}else{SHUF=!SHUF;localStorage.setItem('mbShuf',SHUF?'1':'0')}};" +
        // 랜덤으로 고를 곡 묶음 (POOL): 사용자가 마지막으로 곡을 고른 목록. 없으면 지금 화면의 목록에서 고른다.
        // 고른 곡이 지금 화면에 없으면 앱에 알려서 찾아 틀게 한다 (mb:play)
        "let POOL=null;window.mbPool=a=>{POOL=Array.isArray(a)&&a.length?a:null};" +
        "const rnd=()=>{const st=window.mbState&&window.mbState();const cur=st?st.t:'';" +
        "const pool=POOL||R().map(x=>({t:T(x,S.title),s:T(x,S.sub)}));if(!pool.length)return false;let c,n=0;" +
        "do{c=pool[Math.floor(Math.random()*pool.length)];n++}while(n<10&&pool.length>1&&c.t===cur);" +
        "if(window.mbPlay(c.t,c.s))return true;if(!POOL)return false;" +
        "try{chrome.webview.postMessage('mb:play:'+JSON.stringify(c));return true}catch(e){return false}};" +
        // 곡이 끝났을 때 사이트의 '다음 곡'보다 먼저 받아서 랜덤 곡을 튼다
        "window.addEventListener('ended',e=>{if(SHUF&&!REP&&rnd())e.stopImmediatePropagation()},true);" +
        "window.mbVol=v=>{V=Math.min(1,Math.max(0,v));try{localStorage.setItem('mbVol',V)}catch(e){}const a=window.__mbA;if(a)a.volume=V;return V};" +
        "window.mbState=()=>{fadeStep();const p=document.querySelector(P);if(!p)return null;const sp=p.querySelectorAll('span.cursor-pointer');" +
        "const tm=[...p.querySelectorAll('span')].find(s=>/\\d+:\\d+ \\/ \\d+:\\d+/.test(s.textContent));const a=window.__mbA;" +
        "return {t:(sp[0]&&sp[0].textContent.trim())||'',s:(sp[1]&&sp[1].textContent.trim())||'',time:tm?tm.textContent.trim():'',playing:!!(a&&!a.paused&&!a.ended),hasA:!!a,vol:V,rep:REP,shuf:SHUF,fx:FX,img:(p.querySelector('img')||{}).src||''}};" +
        // 사이트의 버튼을 그대로 누른다 (0 이전, 1 재생/일시정지, 2 다음)
        "const press=i=>{const p=document.querySelector(P);const f=p&&p.querySelector('svg');if(!f)return;" +
        "const b=f.parentElement.querySelectorAll(':scope>svg')[i];if(b)b.dispatchEvent(new MouseEvent('click',{bubbles:true}))};" +
        "window.mbCmd=i=>{if(i===2&&SHUF&&rnd())return;press(i)};" +
        // 랜덤 곡을 앱이 못 틀었을 때: 사이트의 다음 곡으로 넘긴다
        "window.mbNextRaw=()=>press(2);" +
        // 곡 목록 (첫 화면이면 인기 차트 TOP 100만), 곡 재생, 검색, 사이트 안 이동
        "const here=()=>location.pathname.replace(/\\/$/,'');" +
        // SEEN: 이 주소에서 곡 줄을 한 번이라도 봤는지 (곡 자료가 다 실렸는지 가리는 데 쓴다)
        "const SEEN={};" +
        "const R=()=>{let r=[...document.querySelectorAll(S.row)].filter(x=>x.querySelector(S.btn+' img'));if(r.length)SEEN[here()]=1;return here()==='/music'?r.slice(0,100):r};" +
        "window.mbSeen=()=>{R();return !!SEEN[here()]};" +
        "window.mbList=()=>R().map(x=>{const im=x.querySelector('img');return {t:T(x,S.title),s:T(x,S.sub),img:im?(im.currentSrc||im.src||''):''}});" +
        // 순번이 아니라 제목·지역이 같은 줄을 찾아 누른다 (화면의 순서가 바뀌어도 다른 곡이 나오지 않게)
        "window.mbPlay=(t,s)=>{const r=R().find(x=>T(x,S.title)===t&&T(x,S.sub)===s);const b=r&&r.querySelector(S.btn);if(!b)return false;" +
        "b.dispatchEvent(new MouseEvent('click',{bubbles:true}));return true};" +
        "const box=()=>[...document.querySelectorAll('input')].find(i=>i.placeholder===S.search);" +
        // 검색 칸의 글이 q가 되게 한다 (이미 q면 건드리지 않는다)
        "window.mbSearch=q=>{const inp=box();if(!inp)return false;if(inp.value===q)return true;" +
        "Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(inp,q);inp.dispatchEvent(new Event('input',{bubbles:true}));return true};" +
        // 화면에서 무엇을 찾았는지 (못 읽었을 때 까닭을 가린다)
        "window.mbCheck=()=>({path:here(),ready:document.readyState,bar:!!document.querySelector(S.bar),rows:R().length,search:!!box(),seen:!!SEEN[here()]});" +
        "window.mbGo=p=>{if(here()===p)return 0;const a=[...document.querySelectorAll('a[href]')].find(a=>{try{return new URL(a.href).pathname.replace(/\\/$/,'')===p}catch(e){return false}});" +
        "if(a){a.click();return 1}location.href=p;return 2};" +
        "window.mbHere=()=>here();})();";

    // 광고 칸 (보통 사이트 창에 숨김 CSS로 넣는다)
    public const string AdCss =
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
    public const string MapleOnlyCss = ",div:has(> main) > div:first-child,div:has(> main) > footer";
    public const string HideRule = "{display:none!important;height:0!important;min-height:0!important}";

    // 웹툰 스크롤·회차 이동 (WebWindow.Webtoon.cs의 SendScroll·SendEpisode가 부른다)
    public const string ScrollScript =
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
}
