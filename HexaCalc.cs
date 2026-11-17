using System.Text.Json;

namespace MapleBar;

/// <summary>헥사 코어 강화에 드는 솔 에르다·솔 에르다 조각 수.
/// 표는 0레벨(아직 안 연 코어)부터 그 레벨까지 모은 개수라, 두 레벨 사이 개수는 "목표 레벨 값 − 지금 레벨 값"이다.
/// (메이플스카우터 헥사 페이지·6차 스킬 강화 비용표와 레벨마다 같은 값)</summary>
static class Hexa
{
    /// <param name="Key">계산기 스크립트에서 쓰는 이름</param>
    public sealed record Core(string Key, string Name, int[] Sole, int[] Piece);

    public static readonly Core[] Cores =
    {
        new("skill", "스킬 코어",
            new[] { 0, 5, 6, 7, 8, 10, 12, 14, 17, 20, 30, 33, 36, 40, 44, 48, 52, 56, 60, 65, 80, 85, 90, 95, 100, 105, 111, 117, 123, 130, 150 },
            new[] { 0, 100, 130, 165, 205, 250, 300, 355, 415, 480, 680, 760, 850, 950, 1060, 1180, 1310, 1450, 1600, 1760, 2110, 2280, 2460, 2650, 2850, 3060, 3280, 3510, 3750, 4000, 4500 }),
        new("skill3", "3번째 스킬 코어",
            new[] { 0, 7, 8, 9, 10, 11, 13, 15, 17, 19, 27, 29, 31, 34, 37, 40, 43, 46, 49, 52, 64, 68, 72, 76, 80, 84, 88, 93, 98, 103, 117 },
            new[] { 0, 140, 161, 187, 217, 251, 289, 332, 379, 430, 572, 634, 703, 780, 863, 954, 1052, 1157, 1269, 1389, 1641, 1769, 1905, 2050, 2202, 2363, 2531, 2708, 2892, 3085, 3442 }),
        new("rein", "강화 코어",
            new[] { 0, 4, 5, 6, 7, 9, 11, 13, 16, 19, 27, 30, 33, 36, 39, 42, 45, 48, 51, 55, 67, 71, 75, 79, 83, 87, 92, 97, 102, 108, 123 },
            new[] { 0, 75, 98, 125, 155, 189, 227, 269, 314, 363, 513, 573, 641, 716, 799, 889, 987, 1092, 1205, 1325, 1588, 1716, 1851, 1994, 2144, 2302, 2467, 2640, 2820, 3008, 3383 }),
        new("mastery", "마스터리 코어",
            new[] { 0, 3, 4, 5, 6, 7, 8, 9, 11, 13, 18, 20, 22, 24, 26, 28, 30, 32, 34, 37, 45, 48, 51, 54, 57, 60, 63, 66, 69, 73, 83 },
            new[] { 0, 50, 65, 83, 103, 126, 151, 179, 209, 242, 342, 382, 427, 477, 532, 592, 657, 727, 802, 882, 1057, 1142, 1232, 1327, 1427, 1532, 1642, 1757, 1877, 2002, 2252 }),
        new("general", "공용 코어",
            new[] { 0, 7, 9, 11, 13, 16, 19, 22, 27, 32, 46, 51, 56, 62, 68, 74, 80, 86, 92, 99, 116, 123, 130, 137, 144, 151, 160, 169, 178, 188, 208 },
            new[] { 0, 125, 163, 207, 257, 314, 377, 446, 521, 603, 903, 1013, 1137, 1275, 1427, 1592, 1771, 1964, 2171, 2391, 2916, 3150, 3398, 3660, 3935, 4224, 4527, 4844, 5174, 5518, 6268 }),
        new("general3", "3번째 공용 코어",
            new[] { 0, 4, 5, 6, 7, 9, 11, 13, 16, 19, 28, 31, 34, 37, 40, 44, 48, 52, 56, 60, 74, 78, 83, 88, 93, 98, 103, 108, 113, 119, 137 },
            new[] { 0, 90, 115, 145, 180, 220, 265, 315, 370, 430, 610, 683, 764, 854, 952, 1059, 1174, 1298, 1430, 1571, 1886, 2037, 2197, 2367, 2546, 2735, 2933, 3141, 3358, 3585, 4035 }),
    };
}

/// <summary>헥사 강화 계산기. 환산 사이트의 헥사코어 화면을 닉네임으로 열고, 사이트가 불러온 내 코어 레벨·아이콘으로
/// 그 위에 계산기를 덮어 띄운다. 코어를 누르면 30칸 막대가 나오고, 왼쪽 클릭은 시작 레벨·오른쪽 클릭은 목표 레벨이다.
/// (사이트가 캐릭터를 검색하면 브라우저 저장소 character-store에 코어 레벨을 담아 두고, 직업별 코어 그림 표는 사이트 스크립트에 들어 있다.
///  사이트의 사람 확인 화면이 뜨면 계산기를 덮지 않는다.)</summary>
static class HexaCalc
{
    /// <summary>메뉴에서 쓰는 주소 (사이트 주소가 아니다. 바가 ?name=닉네임을 붙여 보낸다)</summary>
    public const string Path = "maplebar:hexa";

    /// <summary>캐릭터 정보를 불러오는 사이트 화면. 보스컷 화면은 경매장 → 환산 넣기가 쓰므로 겹치지 않게 헥사코어 화면을 쓴다.</summary>
    const string Page = "/ko/hexa";

    public static void Open(string path, Settings st, float sc)
    {
        int q = path.IndexOf("?name=", StringComparison.Ordinal);
        string? name = q >= 0 ? Uri.UnescapeDataString(path[(q + 6)..]) : null;
        if (string.IsNullOrWhiteSpace(name)) name = st.StarNick ?? st.MapleNick; // 검색칸이 비면 별표한 캐릭터
        var url = SiteMenu.Base + Page + (string.IsNullOrWhiteSpace(name) ? "" : "?name=" + Uri.EscapeDataString(name.Trim()));
        new WebWindow(url, st, sc, Script).Show();
    }

    static string? script;
    static string Script => script ??= Js.Replace("/*TABLES*/",
        JsonSerializer.Serialize(Hexa.Cores.ToDictionary(c => c.Key, c => new { s = c.Sole, p = c.Piece })));

    // 화면 모양은 환산 사이트의 「헥사강화 진행률」을 따른다 (M·R·S·G 색, 아이콘 오른쪽 아래 레벨)
    const string Js = """
(()=>{
if(window.__mbhx||window.top!==window)return;window.__mbhx=1; // 광고 같은 안쪽 화면에는 띄우지 않는다
const TB=/*TABLES*/;
const GROUPS=[
['M','마스터리 코어',[['masteryCore1','mastery'],['masteryCore2','mastery'],['masteryCore3','mastery'],['masteryCore4','mastery']]],
['R','강화 코어',[['reinCore1','rein'],['reinCore2','rein'],['reinCore3','rein'],['reinCore4','rein']]],
['S','스킬 코어',[['skillCore1','skill'],['skillCore2','skill'],['skillCore3','skill3']]],
['G','공용 코어',[['generalCore2','general'],['generalCore3','general3'],['generalCore1','general']]]];
const ALL=GROUPS.flatMap(g=>g[2].map(c=>({k:c[0],t:c[1],tag:g[0],gname:g[1]})));
const FIXED={generalCore1:['솔 야누스','/hexaskill/General/General_1_0.png'],generalCore2:['솔 헤카테','/hexaskill/General/General_2.png']};
const COL={M:['#a855f7','#fff','#f6efff'],R:['#3b82f6','#fff','#ebf3ff'],S:['#facc15','#1f1f1f','#fefbe6'],G:['#22c55e','#fff','#e9f9ef']};
const ERDA='/sole_erda.png',PIECE='/sole_erda_piece.png';
const CSS=`html:has(#mbhx:not([style*="none"])),html:has(#mbhx:not([style*="none"])) body{overflow:hidden!important}
#mbhx{position:fixed;inset:0;z-index:2147483647;overflow:auto;background:#f5f6f8;color:#1f1f1f;font:14px/1.45 Pretendard,"Malgun Gothic",sans-serif;padding:10px;-webkit-user-select:none;user-select:none}
#mbhx *{box-sizing:border-box;margin:0;padding:0;font:inherit;color:inherit}
#mbhx b{font-weight:700}
#mbhx .wrap{max-width:980px;margin:0 auto}
#mbhx .body{display:grid;grid-template-columns:1fr;gap:10px;align-items:start}
#mbhx .body>.card{margin:0}
@media (min-width:660px){#mbhx .body{grid-template-columns:minmax(0,1fr) minmax(0,1.15fr)}#mbhx .p{position:sticky;top:0}}
#mbhx .hd{display:flex;align-items:flex-start;gap:8px;margin-bottom:10px}
#mbhx .hd .t{flex:1;min-width:0}
#mbhx .hd .t>b{display:block;font-size:17px}
#mbhx .who{font-size:12px;color:#777;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
#mbhx button{cursor:pointer;border:1px solid #e2e2e2;background:#fff;border-radius:8px;padding:4px 10px;font-size:12px;color:#444;white-space:nowrap}
#mbhx button:hover{background:#f2f2f2}
#mbhx .card{background:#fff;border:1px solid #e2e2e2;border-radius:12px;padding:12px;margin-bottom:10px}
#mbhx .grp{display:flex;align-items:center;gap:10px}
#mbhx .grp+.grp{margin-top:6px;padding-top:6px;border-top:1px dashed #eee}
#mbhx .gl{flex:none;width:76px;display:flex;align-items:center;gap:6px;font-size:12px;color:#555}
#mbhx .tag{display:inline-flex;align-items:center;justify-content:center;width:16px;height:16px;border-radius:4px;font-size:10px;font-weight:700}
#mbhx .tiles{display:flex;flex-wrap:wrap;gap:8px}
#mbhx .tile{position:relative;width:52px;height:52px;border-radius:10px;border:1.5px solid;display:flex;align-items:center;justify-content:center;cursor:pointer}
#mbhx .tile:hover{filter:brightness(.96)}
#mbhx .tile img{width:34px;height:34px;border-radius:6px;display:block}
#mbhx .tile em{font-style:normal;font-weight:700;color:#999}
#mbhx .tile .lv{position:absolute;right:-2px;bottom:-2px;min-width:20px;height:16px;padding:0 3px;border-radius:5px;background:#111;color:#fff;font-size:11px;font-weight:700;line-height:16px;text-align:center}
#mbhx .tile.max .lv{background:#ff8a00}
#mbhx .tile.on{box-shadow:0 0 0 2px #fff,0 0 0 4px #ff8a00}
#mbhx .sum{display:flex;align-items:center;flex-wrap:wrap;gap:4px 6px;margin-top:8px;padding-top:8px;border-top:1px solid #eee;font-size:12px;color:#666}
#mbhx .sum img{width:20px;height:20px;display:block}
#mbhx .sum b{color:#1f1f1f;margin-right:6px}
#mbhx .empty{font-size:13px;color:#888;text-align:center;padding:14px 0}
#mbhx .top{display:flex;align-items:center;gap:10px;margin-bottom:8px}
#mbhx .top img{width:38px;height:38px;border-radius:8px;border:1px solid #e2e2e2;display:block}
#mbhx .nm{font-weight:700}
#mbhx .now{font-size:12px;color:#777}
#mbhx .rg{margin-left:auto;font-size:13px;color:#555;white-space:nowrap}
#mbhx .rg b{font-size:19px;color:#1f1f1f}
#mbhx .bar{display:grid;grid-template-columns:repeat(30,1fr);gap:2px;height:28px;cursor:pointer;touch-action:none}
#mbhx .bar i{border-radius:3px;background:#eceef1}
#mbhx .bar i.done{background:#a9afb9}
#mbhx .bar i.skip{background:#dcdfe4}
#mbhx .bar i.a{background:#5f6672}
#mbhx .bar i.on{background:#ffb04d}
#mbhx .bar i.b{background:#ff8a00}
#mbhx .sc{display:grid;grid-template-columns:repeat(30,1fr);font-size:10px;color:#999;margin-top:3px}
#mbhx .sc span{text-align:center}
#mbhx .hint{display:flex;align-items:center;justify-content:space-between;gap:8px;margin-top:8px;font-size:12px;color:#888}
#mbhx .hint b{color:#1f1f1f}
#mbhx .res{display:grid;grid-template-columns:1fr 1fr;gap:8px;margin-top:8px}
#mbhx .res>div{display:flex;align-items:center;gap:8px;background:#fff3e0;border-radius:10px;padding:8px 10px}
#mbhx .res img{width:30px;height:30px;display:block}
#mbhx .res small{display:block;font-size:11px;color:#8a6a3a}
#mbhx .res b{font-size:18px;color:#b45a00;margin-right:2px}
@media (prefers-color-scheme:dark){
#mbhx{background:#262a2f;color:#e8e8ea}
#mbhx .card{background:#31363c;border-color:#4c525a}
#mbhx button{background:#40464d;border-color:#4c525a;color:#e8e8ea}
#mbhx button:hover{background:#4c535b}
#mbhx .grp+.grp{border-top-color:#444a52}
#mbhx .sum{border-top-color:#444a52;color:#b2b4bc}
#mbhx .gl,#mbhx .rg{color:#c4c6cc}
#mbhx .who,#mbhx .now,#mbhx .empty,#mbhx .hint,#mbhx .sc,#mbhx .tile em{color:#a0a3ab}
#mbhx .sum b,#mbhx .rg b,#mbhx .hint b{color:#e8e8ea}
#mbhx .tile{background:#3a4047!important}
#mbhx .tile.on{box-shadow:0 0 0 2px #31363c,0 0 0 4px #f2a65a}
#mbhx .tile.max .lv{background:#f2a65a;color:#1f1f1f}
#mbhx .top img{border-color:#4c525a}
#mbhx .bar i{background:#40464d}
#mbhx .bar i.done{background:#7a808a}
#mbhx .bar i.skip{background:#4a5058}
#mbhx .bar i.a{background:#c4c8cf}
#mbhx .bar i.on{background:#c98a4a}
#mbhx .bar i.b{background:#f2a65a}
#mbhx .res>div{background:#3d3428}
#mbhx .res small{color:#cdb592}
#mbhx .res b{color:#f2b877}
}`;
const esc=s=>String(s==null?'':s).replace(/[&<>"']/g,c=>'&#'+c.charCodeAt(0)+';');
const fmt=n=>Number(n||0).toLocaleString('ko-KR');
const lvOf=v=>{v=Math.floor(Number(v)||0);return v<0?0:v>30?30:v};
const cost=(t,a,b)=>[TB[t].s[b]-TB[t].s[a],TB[t].p[b]-TB[t].p[a]];
const HINT='왼쪽 클릭: 시작 레벨 · 오른쪽 클릭: 목표 레벨 · 끌어서 옮기기';
let req=null,icons=null,scanN=-1,waited=0,raw0=null,data=null,shownKey='',shownName=null,iconsShown=false;
let root=null,grid=null,pane=null,who=null,chip=null,showBtn=null,hidden=false,sel=null,drag=null,dragBar=null;
const rng={};

// 직업별 코어 그림 표: 사이트 스크립트 안의 표를 찾는다 ({직업:{skillCore1:{title,url},…}})
function findIcons(){
 if(icons)return icons;
 const ch=self.webpackChunk_N_E;if(!ch||!ch.push)return null;
 if(!req)try{ch.push([['mbhx'+Date.now()],{},r=>{req=r}])}catch(e){}
 if(!req||!req.m)return null;
 const ids=Object.keys(req.m);if(ids.length===scanN)return null;scanN=ids.length;
 const isMap=v=>{try{return!!v&&typeof v==='object'&&Object.values(v).some(c=>c&&c.skillCore1&&typeof c.skillCore1.url==='string')}catch(e){return false}};
 const cand=[];
 for(const id of ids){let s='';try{s=Function.prototype.toString.call(req.m[id])}catch(e){continue}
  if(s.indexOf('skillCore1')<0||s.indexOf('/hexaskill/')<0)continue;cand.push([id,s.split('/hexaskill/').length])}
 cand.sort((a,b)=>b[1]-a[1]);
 for(const c of cand){try{const ex=req(c[0]);for(const k in ex)if(isMap(ex[k]))return icons=ex[k]}catch(e){}}
 return null;
}
// 사이트가 검색한 캐릭터 정보 (코어 레벨·직업)
function readData(){
 let raw='';try{raw=localStorage.getItem('character-store')||''}catch(e){}
 if(raw===raw0)return data;raw0=raw;data=null;
 try{const sr=JSON.parse(raw).state.searchResult,api=sr&&sr.userApiData;
  if(api&&api.hexaSkill){const info=api.info||{},us=sr.userStat&&sr.userStat.stat;
   data={name:info.character_name||'',cls:(us&&us.myClass)||info.character_class||'',hs:api.hexaSkill||{},hg:api.hexaSkill_general||{}};
   data.key=JSON.stringify([data.name,data.cls,data.hs,data.hg])}}catch(e){}
 return data;
}
const lvl=k=>lvOf(k.indexOf('generalCore')===0?data.hg[k]:data.hs[k]);
function meta(k){
 const m=icons&&data&&icons[data.cls],c=m&&m[k];
 if(c&&c.url)return[c.title||'',String(c.url).replace('./hexaskill','/hexaskill')];
 return FIXED[k]||['',''];
}
function build(){
 root=document.createElement('div');root.id='mbhx';
 root.innerHTML='<style>'+CSS+'</style><div class="wrap"><div class="body"><div class="card g"></div><div class="card p"></div></div></div>';
 document.documentElement.appendChild(root);
 grid=root.querySelector('.g');pane=root.querySelector('.p');
 addEventListener('resize',fit);
 root.addEventListener('click',e=>{
  const b=e.target.closest('[data-act]');
  if(b){if(b.dataset.act==='site'){hidden=true;sync('')}else if(b.dataset.act==='reset'&&sel){delete rng[sel];fillPanel()}return}
  const t=e.target.closest('.tile');if(t){sel=t.dataset.k;fillGrid();fillPanel()}});
 root.addEventListener('contextmenu',e=>{if(e.target.closest('.bar'))e.preventDefault()});
 root.addEventListener('pointerdown',e=>{const bar=e.target.closest('.bar');if(!bar||(e.button!==0&&e.button!==2))return;
  e.preventDefault();if(!sel)return;const l=at(bar,e),r=fix();
  // 왼쪽: 목표 칸을 잡으면 목표를 옮기고, 목표보다 아래면 시작을 정한다. 오른쪽: 시작보다 위면 목표를 정한다. 그 밖은 무시
  if(e.button===0){if(l===r.b)drag='b';else if(l<r.b){drag='a';r.a=l}else return}else{if(l>r.a){drag='b';r.b=l}else return}
  dragBar=bar;try{bar.setPointerCapture(e.pointerId)}catch(x){}paint()});
 root.addEventListener('pointermove',e=>{const bar=dragBar||e.target.closest('.bar');if(!bar)return;hover(at(bar,e));if(drag)setAt(e)});
 const up=()=>{drag=null;dragBar=null};root.addEventListener('pointerup',up);root.addEventListener('pointercancel',up);
 root.addEventListener('pointerout',e=>{const bar=e.target.closest('.bar');if(bar&&!dragBar&&!bar.contains(e.relatedTarget))hover(0)});
}
function fillGrid(){
 let h='',te=0,tp=0;
 if(!sel){const f=ALL.find(c=>lvl(c.k)<30)||ALL[0];sel=f.k} // 처음부터 오른쪽 칸이 비지 않게 아직 덜 올린 코어를 골라 둔다
 for(const g of GROUPS){const c=COL[g[0]];
  h+='<div class="grp"><div class="gl"><span class="tag" style="background:'+c[0]+';color:'+c[1]+'">'+esc(g[0])+'</span>'+esc(g[1])+'</div><div class="tiles">';
  for(const x of g[2]){const k=x[0],L=lvl(k),m=meta(k),r=cost(x[1],L,30);te+=r[0];tp+=r[1];
   h+='<div class="tile'+(sel===k?' on':'')+(L>=30?' max':'')+'" data-k="'+k+'" title="'+esc(m[0])+'" style="border-color:'+c[0]+';background:'+c[2]+'">'+
    (m[1]?'<img src="'+esc(m[1])+'" alt="">':'<em>'+esc(g[0])+'</em>')+'<span class="lv">'+L+'</span></div>'}
  h+='</div></div>'}
 h+='<div class="sum"><span>전부 30레벨까지 더</span><img src="'+ERDA+'" alt="솔 에르다"><b>'+fmt(te)+'</b><img src="'+PIECE+'" alt="조각"><b>'+fmt(tp)+'</b></div>';
 grid.innerHTML=h;
 fit();
}
// 창 높이 안에 다 들어오게 줄인다 (스크롤 없이 한눈에)
function fit(){
 const w=root&&root.querySelector('.wrap');if(!w||!root.clientHeight)return;
 w.style.zoom='1';
 const cs=getComputedStyle(root),avail=root.clientHeight-parseFloat(cs.paddingTop)-parseFloat(cs.paddingBottom);
 const s=Math.min(1,avail/w.scrollHeight);
 w.style.zoom=s<1?String(Math.floor(s*1000)/1000):'1';
}
function fillPanel(){
 if(!sel){pane.innerHTML='<p class="empty">코어를 누르면 필요한 개수를 볼 수 있어요</p>';return}
 const core=ALL.find(c=>c.k===sel),L=lvl(sel),m=meta(sel);
 let cells='';for(let i=1;i<=30;i++)cells+='<i data-l="'+i+'"></i>';
 pane.innerHTML='<div class="top">'+(m[1]?'<img src="'+esc(m[1])+'" alt="">':'')+'<div><div class="nm">'+esc(m[0]||core.gname)+'</div><div class="now">'+esc(core.gname)+' · 지금 '+L+'레벨</div></div><div class="rg"></div></div>'+
  '<div class="bar">'+cells+'</div><div class="sc"><span style="grid-column:1">1</span><span style="grid-column:10">10</span><span style="grid-column:20">20</span><span style="grid-column:30">30</span></div>'+
  '<div class="hint"><span class="hv">'+HINT+'</span><button data-act="reset">처음대로</button></div>'+
  '<div class="res"><div><img src="'+ERDA+'" alt=""><span><small>솔 에르다</small><b class="e"></b>개</span></div><div><img src="'+PIECE+'" alt=""><span><small>솔 에르다 조각</small><b class="pc"></b>개</span></div></div>';
 paint();fit();
}
// 처음엔 지금 레벨 → 30 (다 올린 코어는 0 → 30). 시작은 0~29, 목표는 시작보다 높게 30까지
function fix(){const L=lvl(sel);let r=rng[sel];if(!r)r=rng[sel]={a:L>=30?0:L,b:30};
 r.a=Math.max(0,Math.min(r.a,29));r.b=Math.min(30,Math.max(r.b,r.a+1));return r}
function paint(){
 const L=lvl(sel),r=fix(),core=ALL.find(c=>c.k===sel);
 pane.querySelectorAll('.bar i').forEach(el=>{const i=+el.dataset.l;
  el.className=i<=r.a?(i===r.a&&r.a!==L?'a':i<=L?'done':'skip'):i<r.b?'on':i===r.b?'b':'';el.title=i+'레벨'});
 pane.querySelector('.rg').innerHTML='<b>'+r.a+'</b> → <b>'+r.b+'</b>레벨';
 const x=cost(core.t,r.a,r.b);pane.querySelector('.e').textContent=fmt(x[0]);pane.querySelector('.pc').textContent=fmt(x[1]);
}
const at=(bar,e)=>{const b=bar.getBoundingClientRect();return Math.max(1,Math.min(30,Math.ceil((e.clientX-b.left)/b.width*30)))};
// 끄는 중에는 시작이 목표를, 목표가 시작을 넘지 못하게 끝에서 멈춘다
function setAt(e){if(!sel||!dragBar)return;const l=at(dragBar,e),r=fix();
 if(drag==='a')r.a=Math.min(l,r.b-1);else r.b=Math.max(l,r.a+1);
 paint()}
function hover(l){const h=pane&&pane.querySelector('.hv');if(h)h.innerHTML=l?'<b>'+l+'</b>레벨':HINT}
function sync(msg){
 const show=!msg&&!hidden;
 if(root){const was=root.style.display!=='none';root.style.display=show?'':'none';if(show&&!was)fit()}
 if(msg){if(!chip){chip=document.createElement('div');chip.style.cssText='position:fixed;left:50%;bottom:14px;transform:translateX(-50%);z-index:2147483647;background:rgba(31,31,31,.88);color:#fff;font:12px/1.4 "Malgun Gothic",sans-serif;padding:7px 12px;border-radius:8px;pointer-events:none;white-space:nowrap';document.documentElement.appendChild(chip)}
  chip.textContent=msg;chip.style.display=''}else if(chip)chip.style.display='none';
 if(!msg&&hidden){if(!showBtn){showBtn=document.createElement('button');showBtn.textContent='계산기 보기';
   showBtn.style.cssText='position:fixed;right:14px;bottom:14px;z-index:2147483647;background:#ff8a00;color:#fff;border:0;border-radius:8px;padding:8px 14px;font:700 13px "Malgun Gothic",sans-serif;cursor:pointer;box-shadow:0 2px 8px rgba(0,0,0,.2)';
   showBtn.onclick=()=>{hidden=false;sync('')};document.documentElement.appendChild(showBtn)}showBtn.style.display=''}
 else if(showBtn)showBtn.style.display='none';
 if(show&&document.title!=='헥사 강화 계산기')document.title='헥사 강화 계산기';
}
function tick(){
 if(!document.documentElement||!document.body)return;
 const want=(new URLSearchParams(location.search).get('name')||'').trim().toLowerCase();
 const human=/사람\s*확인/.test(document.body.innerText||''); // 화면에 보이는 글만 본다 (계산기는 body 밖이라 안 섞인다)
 const d=readData(),ic=findIcons();
 let msg='';
 if(human)msg='사이트에서 확인을 마치면 계산기가 떠요';
 else if(!d)msg=want?'캐릭터 정보를 불러오는 중이에요':'바 검색칸에 닉네임을 넣고 다시 열어 주세요';
 else if(want&&d.name&&d.name.toLowerCase()!==want)msg='캐릭터 정보를 불러오는 중이에요';
 else if(!ic&&++waited<8)msg='코어 그림을 불러오는 중이에요';
 if(!msg){if(!root)build();
  if(d.key!==shownKey||(!!ic)!==iconsShown){
   // 다른 캐릭터로 바뀌면 이전 캐릭터의 시작·목표 범위를 버린다
   if(d.name!==shownName){shownName=d.name;for(const k in rng)delete rng[k]}
   shownKey=d.key;iconsShown=!!ic;fillGrid();fillPanel()}}
 sync(msg);
}
setTimeout(tick,300);setInterval(tick,1000);
})();
""";
}
