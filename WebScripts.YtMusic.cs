namespace MapleBar;

static partial class WebScripts
{
    /// <summary>
    /// 유튜브 뮤직 창 조종: 메애기 브금 창과 같은 이름(mbState·mbCmd·mbVol·mbMode)을 만들어 바가 똑같이 다룬다.
    /// 플레이어 바의 버튼을 눌러 주고, 곡 정보는 플레이어 바와 video 요소에서 읽는다. 광고는 건드리지 않는다.
    /// mbState(true)면 가사 탭을 열어 가사 글을 같이 돌려준다 (가사가 없는 곡은 빈 글).
    /// </summary>
    public const string YtMusicScript = """
(()=>{if(window.mbState)return;
const Q=s=>document.querySelector(s),bar=()=>Q('ytmusic-player-bar'),V=()=>Q('video'),P=()=>Q('#movie_player');
const tx=e=>e?(e.textContent||'').trim():'';
const f=s=>{s=Math.max(0,Math.floor(s||0));return Math.floor(s/60)+':'+String(s%60).padStart(2,'0')};
const btn=c=>{const b=bar(),e=b&&b.querySelector(c);if(e){e.click();return true}return false};
let REP=false,SHUF=false;
window.mbCmd=i=>{const p=P(),v=V();
 if(i===1){if(!btn('#play-pause-button')&&v){v.paused?v.play().catch(()=>{}):v.pause()}}
 else if(i===0){if(!btn('.previous-button')&&p&&p.previousVideo)p.previousVideo()}
 else if(i===2){if(!btn('.next-button')&&p&&p.nextVideo)p.nextVideo()}};
window.mbVol=x=>{x=Math.max(0,Math.min(1,+x||0));const p=P();
 try{if(p&&p.setVolume){p.setVolume(Math.round(x*100));if(x>0&&p.unMute)p.unMute()}else{const v=V();if(v)v.volume=x}}catch(e){}};
window.mbMode=m=>{if(m==='rep'){REP=!REP;const v=V();if(v)v.loop=REP}else{SHUF=!SHUF;btn('.shuffle')}};
window.mbSetFx=()=>{};
window.mbGo=()=>{};
// 가사: 곡이 바뀌면 이전 글과 다른 글이 나올 때까지 기다린다.
// 시간이 맞춰진 가사(일부 곡)는 줄마다 따로 된 요소로 나오고, 페이지가 지금 줄만 다르게 꾸민다 → 그 줄 번호를 같이 준다
let LK='',LT='',LP='',tries=0,opens=0,BOX=null,CUR=-1;
const lines=el=>[...el.children].filter(c=>(c.textContent||'').trim());
const findBox=root=>{let best=null,bc=0;
 for(const el of root.querySelectorAll('*')){const k=el.children;if(k.length<4||k.length<=bc||el.offsetParent===null)continue;let ok=0;
  for(const c of k){const s=(c.textContent||'').trim();if(s.length>0&&s.length<160&&!c.querySelector('img,button,a'))ok++}
  if(ok>=k.length*0.7&&ok>bc){best=el;bc=ok}}
 return best};
const sig=c=>{const a=getComputedStyle(c),e=c.firstElementChild,b=e?getComputedStyle(e):a;
 return a.color+a.opacity+a.fontWeight+a.transform+'|'+b.color+b.opacity+b.fontWeight};
const curOf=ks=>{let i=ks.findIndex(c=>/current|active|highlight|selected/i.test((c.className||'')+' '+(c.getAttribute('aria-current')||'')));
 if(i>=0)return i;const sg=ks.map(sig),m=new Map();sg.forEach(s=>m.set(s,(m.get(s)||0)+1));
 const odd=[];sg.forEach((s,j)=>{if(m.get(s)===1)odd.push(j)});return odd.length===1?odd[0]:-1};
const vis=e=>!!e&&e.offsetParent!==null&&e.getClientRects().length>0;
const isSel=x=>x.matches('[aria-selected=true],.iron-selected,[selected]')||!!x.querySelector('[aria-selected=true]')||!!x.closest('[aria-selected=true]');
const isOff=x=>x.matches('[disabled],[aria-disabled=true]')||!!x.closest('[disabled],[aria-disabled=true]');
const lyr=()=>{CUR=-1;const t=tx(bar()&&bar().querySelector('.title'));if(!t)return '';
 if(LK!==t){LK=t;LP=LT;LT='';tries=0;opens=0;BOX=null}
 // 재생 화면이 접혀 있으면 가사 탭이 안 그려진다 → 펼친다
 const lay=Q('ytmusic-app-layout');
 if(lay&&!lay.hasAttribute('player-page-open')&&opens<4){opens++;
  const b=bar(),o=b&&(b.querySelector('.toggle-player-page-button')||b.querySelector('.middle-controls .thumbnail-image-wrapper')||b.querySelector('.middle-controls'));
  if(o)o.click();return LT}
 // 탭 모양이 바뀌어도 찾게: 글자가 '가사'/'Lyrics'인 탭
 const tabs=[...document.querySelectorAll('tp-yt-paper-tab,[role=tab],yt-tab-shape')];
 const tab=tabs.find(x=>/^\s*(가사|lyrics)\s*$/i.test(tx(x)))||tabs.find(x=>/가사|lyrics/i.test(tx(x)));
 if(!tab||isOff(tab))return '';
 if(!isSel(tab)&&tries<6){tries++;(tab.querySelector('[role=tab]')||tab).click();return LT}
 const root=Q('ytmusic-player-page')||document.body;
 // 그냥 글로 된 가사
 const shs=[...root.querySelectorAll('ytmusic-description-shelf-renderer')];
 const sh=shs.find(vis)||shs.find(x=>tx(x));
 if(sh){const d=sh.querySelector('.description')||sh;const x=(d.textContent||'').split('\n').map(s=>s.trim()).join('\n').replace(/\n{3,}/g,'\n\n').trim();if(x&&x!==LP)LT=x;return LT}
 // 시간 맞춘 가사
 if(!BOX||!BOX.isConnected)BOX=findBox(root);
 if(!BOX)return LT;
 const ks=lines(BOX),x=ks.map(c=>(c.textContent||'').trim().replace(/\s*\n\s*/g,' ')).join('\n');
 if(x&&x!==LP){LT=x;CUR=curOf(ks)}
 return LT};
window.mbState=L=>{const b=bar(),v=V();if(!b)return null;
 const t=tx(b.querySelector('.title'));
 const s=tx(b.querySelector('.byline')).split('•')[0].trim();
 let img='';for(const im of b.querySelectorAll('img')){if(/^https?:/.test(im.src||'')){img=im.src;break}}
 if(v&&v.loop!==REP)v.loop=REP;
 const d=v&&isFinite(v.duration)?v.duration:0;
 let vol=1;try{const p=P();vol=p&&p.getVolume?p.getVolume()/100:(v?v.volume:1)}catch(e){}
 return {t,s,time:v&&d?f(v.currentTime)+' / '+f(d):'',playing:!!(v&&!v.paused&&!v.ended),hasA:!!v,vol,img,
  rep:REP,shuf:SHUF,fx:false,lyr:L?lyr():null,cur:CUR}};
})();
""";
}
