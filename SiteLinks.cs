namespace MapleBar;

public static class Webtoon
{
    public const string Home = "https://m.comic.naver.com/"; // 내 웹툰은 페이지의 MY 버튼으로 간다
    public static string LoginUrl => "https://nid.naver.com/nidlogin.login?mode=form&url=" + Uri.EscapeDataString(Home);
    /// <summary>웹툰·네이버 로그인 화면 (세로로 긴 창, 스크롤 키 대상)</summary>
    public static bool IsWebtoon(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) &&
        (u.Host.EndsWith("comic.naver.com", StringComparison.OrdinalIgnoreCase) || u.Host.Equals("nid.naver.com", StringComparison.OrdinalIgnoreCase));
    /// <summary>네이버 안의 주소 (새 창 링크를 앱 안에서 연다)</summary>
    public static bool IsNaver(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) &&
        (u.Host.Equals("naver.com", StringComparison.OrdinalIgnoreCase) || u.Host.EndsWith(".naver.com", StringComparison.OrdinalIgnoreCase));
}

public static class Music
{
    public const string Home = "https://meaegi.com/music";
    /// <summary>메애기 주소 중 코디 페이지를 뺀 나머지 (숨은 뮤직 창 하나가 맡는다)</summary>
    public static bool IsMusic(string url) => IsMeaegi(url) && !Coordi.IsCoordi(url) || IsYtMusic(url);
    public static bool IsMeaegi(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Host.EndsWith("meaegi.com", StringComparison.OrdinalIgnoreCase);

    // 유튜브 뮤직: 메이플 브금과 같은 숨은 뮤직 창 하나를 같이 쓴다 (둘 중 하나만 켜진다)
    public const string YtHome = "https://music.youtube.com/";
    public static bool IsYtMusic(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Host.Equals("music.youtube.com", StringComparison.OrdinalIgnoreCase);
}

/// <summary>유튜브: 목록 창에서 고르고, 영상·쇼츠는 재생 창 하나에서 본다</summary>
public static class YouTube
{
    public const string Home = "https://www.youtube.com/";
    public const string Shorts = "https://www.youtube.com/shorts";
    public static string LoginUrl => "https://accounts.google.com/ServiceLogin?service=youtube&continue=" + Uri.EscapeDataString(Home);
    static bool On(Uri u, string host) =>
        u.Host.Equals(host, StringComparison.OrdinalIgnoreCase) || u.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase);
    /// <summary>유튜브 주소, 또는 그 로그인 화면 (앱 안에서 연다)</summary>
    public static bool IsYouTube(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) &&
        (On(u, "youtube.com") || On(u, "youtu.be") || u.Host.Equals("accounts.google.com", StringComparison.OrdinalIgnoreCase));
    /// <summary>재생 창에서 열 주소: 영상(/watch), 쇼츠(/shorts), 짧은 주소(youtu.be)</summary>
    public static bool IsVideo(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) &&
        (On(u, "youtu.be") && u.AbsolutePath.Length > 1 ||
         On(u, "youtube.com") && (u.AbsolutePath.Equals("/watch", StringComparison.OrdinalIgnoreCase) ||
                                  u.AbsolutePath.StartsWith("/shorts", StringComparison.OrdinalIgnoreCase)));
    public static bool IsShorts(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && On(u, "youtube.com") &&
        u.AbsolutePath.StartsWith("/shorts", StringComparison.OrdinalIgnoreCase);

    // 댓글 창: 영상 페이지 주소 끝에 이 표시를 붙여 재생 창과 구분한다
    const string CommentsMark = "#mb-comments";
    public static bool IsComments(string? url) => url != null && url.EndsWith(CommentsMark, StringComparison.Ordinal) && IsVideo(url);
    public static string CommentsUrl(string id, bool shorts) =>
        (shorts ? "https://www.youtube.com/shorts/" : "https://www.youtube.com/watch?v=") + Uri.EscapeDataString(id) + CommentsMark;

    /// <summary>영상·쇼츠 주소에서 영상 번호를 꺼낸다 (없으면 null).</summary>
    public static string? VideoId(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return null;
        var p = u.AbsolutePath;
        if (On(u, "youtu.be")) { var t = p.Trim('/'); return t.Length > 0 ? t : null; }
        if (!On(u, "youtube.com")) return null;
        if (p.StartsWith("/shorts/", StringComparison.OrdinalIgnoreCase))
        {
            var id = p[8..].Split('/')[0];
            return id.Length > 0 ? id : null;
        }
        if (p.Equals("/watch", StringComparison.OrdinalIgnoreCase))
            foreach (var part in u.Query.TrimStart('?').Split('&'))
                if (part.StartsWith("v=", StringComparison.Ordinal) && part.Length > 2) return Uri.UnescapeDataString(part[2..]);
        return null;
    }
}

/// <summary>실시간 방송 (SOOP·치지직): 가로로 넓은 창에서 사이트 그대로 본다. 광고는 막지 않고, 로그인은 사용자가 직접 한다</summary>
public static class Live
{
    public const string Soop = "https://www.sooplive.co.kr/";
    public const string Chzzk = "https://chzzk.naver.com/";
    public const string Nexon = "https://live.nexon.com/";
    static bool On(Uri u, string host) =>
        u.Host.Equals(host, StringComparison.OrdinalIgnoreCase) || u.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase);
    public static bool IsLive(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) &&
        (On(u, "sooplive.co.kr") || On(u, "sooplive.com") || On(u, "afreecatv.com") || On(u, "chzzk.naver.com") || IsNexon(u));
    static bool IsNexon(Uri u) => u.Host.Equals("live.nexon.com", StringComparison.OrdinalIgnoreCase);
    /// <summary>넥슨 라이브 주소인가 (재생 창은 크기 고정, 채팅은 따로 창으로 연다)</summary>
    public static bool IsNexon(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var u) && IsNexon(u);

    static string[] Parts(Uri u) => u.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
    static bool SoopPlay(Uri u) =>
        u.Host.StartsWith("play.", StringComparison.OrdinalIgnoreCase) && (On(u, "sooplive.com") || On(u, "sooplive.co.kr") || On(u, "afreecatv.com"));
    static bool SoopVod(Uri u) =>
        u.Host.StartsWith("vod.", StringComparison.OrdinalIgnoreCase) && (On(u, "sooplive.com") || On(u, "sooplive.co.kr") || On(u, "afreecatv.com")) &&
        u.AbsolutePath.StartsWith("/player/", StringComparison.OrdinalIgnoreCase);

    /// <summary>재생 창에서 열 방송 주소: SOOP 방송·다시보기, 치지직 방송(/live/채널)·다시보기(/video/번호)</summary>
    public static bool IsPlayer(string? url)
    {
        if (IsChat(url) || !Uri.TryCreate(url, UriKind.Absolute, out var u)) return false;
        var p = Parts(u);
        if (SoopPlay(u)) return p.Length >= 1;
        if (SoopVod(u)) return true;
        if (On(u, "chzzk.naver.com"))
            return p.Length == 2 && (p[0].Equals("live", StringComparison.OrdinalIgnoreCase) || p[0].Equals("video", StringComparison.OrdinalIgnoreCase));
        if (IsNexon(u)) // 넥슨 라이브: 방송(/lives/번호)·다시보기(/replays/번호)
            return p.Length == 2 && (p[0].Equals("lives", StringComparison.OrdinalIgnoreCase) || p[0].Equals("replays", StringComparison.OrdinalIgnoreCase));
        return false;
    }

    // 채팅 창: 방송 주소 끝에 이 표시를 붙여 재생 창과 구분한다
    const string ChatMark = "#mb-chat";
    public static bool IsChat(string? url) => url != null && url.EndsWith(ChatMark, StringComparison.Ordinal) && IsLive(url);

    /// <summary>재생 창 주소로 채팅 창 주소를 만든다 (방송 주소가 아니면 null).</summary>
    public static string? ChatUrl(string? url)
    {
        if (!IsPlayer(url) || !Uri.TryCreate(url, UriKind.Absolute, out var u)) return null;
        var p = Parts(u);
        var root = u.GetLeftPart(UriPartial.Authority);
        if (SoopPlay(u)) return root + "/" + p[0] + ChatMark; // 방송 번호가 바뀌어도 같은 BJ면 같은 채팅이다
        if (On(u, "chzzk.naver.com") && p[0].Equals("live", StringComparison.OrdinalIgnoreCase))
            return root + "/live/" + p[1] + "/chat" + ChatMark; // 치지직 채팅 팝업 화면
        return u.GetLeftPart(UriPartial.Query) + ChatMark;
    }
}

/// <summary>메애기 코디 페이지: 뮤직 창과 따로, 보통 사이트 창으로 연다 (광고는 막지 않는다)</summary>
public static class Coordi
{
    public const string Base = "https://meaegi.com";
    static readonly string[] Paths = { "/dressing-room", "/analysis/coordi", "/lookbook" };
    public static bool IsCoordi(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Host.EndsWith("meaegi.com", StringComparison.OrdinalIgnoreCase) &&
        Paths.Any(p => u.AbsolutePath.StartsWith(p, StringComparison.OrdinalIgnoreCase));
}

/// <summary>넥슨 메이플 옥션(웹 경매장): 넥슨 ID 로그인·OTP는 사용자가 직접 한다 (200레벨 이상, 수령은 게임 안에서)</summary>
public static class Nexon
{
    public const string Auction = "https://auction.maplestory.nexon.com/character-select";
    // 로그인·OTP 등 넥슨 쪽 새 창은 기본 브라우저로 보내지 않고 앱 창에서 연다 (로그인이 앱 창에 남게)
    public static bool IsNexon(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) &&
        (u.Host.Equals("nexon.com", StringComparison.OrdinalIgnoreCase) || u.Host.EndsWith(".nexon.com", StringComparison.OrdinalIgnoreCase));
    /// <summary>경매장 페이지인가 (경매장에서 온 메시지만 받는다)</summary>
    public static bool IsAuction(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Host.Equals("auction.maplestory.nexon.com", StringComparison.OrdinalIgnoreCase);

    // 필터 검색은 5초에 한 번만 되는데 사이트가 막아 주지 않는다.
    // 누르면 5초 동안 버튼을 막고 남은 초를 버튼 옆에 보여 준다 (버튼 글자는 건드리지 않고 ::after로 붙인다)
    public const string AuctionScript = """
        (() => {
          if (location.hostname !== 'auction.maplestory.nexon.com') return;
          const WAIT = 5000; let until = 0, timer = 0;
          const css = document.createElement('style');
          css.textContent = 'button[data-mb-wait]{opacity:.55;cursor:not-allowed}button[data-mb-wait]::after{content:attr(data-mb-wait);margin-left:6px}';
          (document.head || document.documentElement).appendChild(css);
          // 작은 창에서도 보기 편하게: 사이트는 1100px보다 좁으면 옆으로 밀리고, 700px 이하면 스스로 모바일 화면이 된다.
          // 그 사이 폭에서는 캐릭터 띠를 한 줄로 줄이고, 필터 칸을 조금 좁히고, 결과 줄이 창 폭에 맞게 접히게 한다
          const fit = document.createElement('style');
          const row = '.isolate > div[class~="h-[84px]"]';
          fit.textContent = `@media (max-width: 1099px) {
            #scroll-root-box { min-width: 0 !important; }
            #desktop-root-header nav > div > div:first-child { height: 32px !important; }
            #desktop-root-header nav > div > div:nth-child(2) { min-height: 0 !important; padding-top: 6px; padding-bottom: 6px; }
            #desktop-root-header nav > div > div:nth-child(2) div:has(> canvas) { display: none; }
            #desktop-root-header nav [class~="left-[150px]"] { position: static; }
            #desktop-root-header nav [class~="left-[150px]"] .flex-col { flex-direction: row; flex-wrap: wrap; align-items: center; column-gap: 12px; row-gap: 2px; }
            #desktop-root-header nav [class~="min-w-[400px]"] { display: none; }
            #desktop-root-header > div:last-child button[class~="h-[44px]"] { height: 36px; min-width: 0; padding: 0 12px; }
            #main-container { gap: 12px !important; }
            #main-container > div:first-child { width: 340px !important; }
            #main-container > div:first-child p[class~="min-h-[48px]"] { display: none; }
            #main-container > div:last-child > div.sticky > div { padding-top: 10px !important; }
            #main-container > div:last-child > div.sticky .flex-1.min-w-0 > div:last-child { flex-shrink: 0; }
            ${row} { height: auto; min-height: 84px; padding: 8px 10px; }
            ${row} > div { gap: 8px; align-items: flex-start; }
            ${row} > div > div:first-child { gap: 10px; }
            ${row} > div > div:first-child > div:last-child > div:first-child { flex-wrap: wrap; row-gap: 2px; }
            ${row} > div > div:last-child { gap: 8px; }
            ${row} button[class~="min-w-[80px]"] { min-width: 0; padding: 0 10px; }
          }
          @media (max-width: 879px) {
            ${row} > div { flex-wrap: wrap; }
            ${row} > div > div:first-child { flex: 1 1 100%; }
            ${row} > div > div:last-child { flex: 1 1 100%; flex-direction: row; align-items: center; justify-content: space-between; }
          }`;
          (document.head || document.documentElement).appendChild(fit);
          const filterBtn = el => {
            const b = el && el.closest ? el.closest('button') : null;
            return b && b.textContent.replace(/\s/g, '').startsWith('필터검색') ? b : null;
          };
          const tick = () => {
            const left = Math.ceil((until - Date.now()) / 1000);
            document.querySelectorAll('button').forEach(b => {
              if (!filterBtn(b)) return;
              if (left > 0) b.setAttribute('data-mb-wait', left + '초'); else b.removeAttribute('data-mb-wait');
            });
            if (left <= 0) { clearInterval(timer); timer = 0; }
          };
          document.addEventListener('click', e => {
            if (!filterBtn(e.target)) return;
            if (Date.now() < until) { e.preventDefault(); e.stopImmediatePropagation(); return; }
            until = Date.now() + WAIT; tick();
            if (!timer) timer = setInterval(tick, 200);
          }, true);
        })();
        """;

    // 경매장 아이템을 누르면 설명창의 옵션을 읽어 아래쪽 칸에 담는다. '환산에 넣기'를 누르면 앱이 환산 보관함에 넣는다
    // 설명창 글자색: 첫 숫자=기본, 노랑=스타포스, 초록=추가옵션, 보라=주문서
    public const string PickScript = """
        (() => {
          if (location.hostname !== 'auction.maplestory.nexon.com') return;
          const KEY = { 'STR': 'str', 'DEX': 'dex', 'INT': 'int', 'LUK': 'luk', '최대 HP': 'max_hp', '최대 MP': 'max_mp',
            '공격력': 'attack_power', '마력': 'magic_power', '방어력': 'armor', '이동속도': 'speed', '점프력': 'jump',
            '보스 몬스터 공격 시 데미지': 'boss_damage', '몬스터 방어율 무시': 'ignore_monster_armor', '올스탯': 'all_stat',
            '데미지': 'damage', '착용 레벨 감소': 'equipment_level_decrease' };
          const SRC = { 'yellow-orange': 'starforceOption', 'green-mint': 'addOption', 'purple-soft': 'etcOption' };
          const SLOTS = ['무기', '보조무기', '엠블렘', '모자', '상의', '하의', '신발', '장갑', '망토', '벨트', '어깨장식', '얼굴장식',
            '눈장식', '귀고리', '반지1', '반지2', '반지3', '반지4', '펜던트', '펜던트2', '포켓 아이템', '기계 심장', '뱃지', '훈장'];
          const num = t => { const m = String(t).replace(/,/g, '').match(/[+-]?\d+/); return m ? parseInt(m[0], 10) : 0; };
          const basic = () => ({ str: '0', dex: '0', int: '0', luk: '0', max_hp: '0', max_mp: '0', attack_power: '0',
            magic_power: '0', armor: '0', speed: '0', jump: '0' });
          // '크리티컬 데미지 +8%' → '크리티컬 데미지 : +8%' (환산이 쓰는 모양)
          const potLine = t => t.includes(' : ') ? t : t.replace(/\s([+-]\d[\d.,]*\D*)$/, ' : $1');
          const guessSlot = (cat, part) => {
            if (part === '반지') return '반지1';
            if (part === '한벌옷') return '상의';
            if (SLOTS.includes(part)) return part;
            if (cat === '무기') return /방패|블레이드|화살깃|마법화살|포스실드|소울링|보조/.test(part) ? '보조무기' : '무기';
            return SLOTS.includes(cat) ? cat : '무기';
          };
          // 환산 아이템의 옵션 칸 (전부 0에서 시작)
          const blank = () => ({
            totalOption: { ...basic(), boss_damage: '0', ignore_monster_armor: '0', all_stat: '0', damage: '0',
              equipment_level_decrease: 0, max_hp_rate: '0', max_mp_rate: '0' },
            baseOption: { ...basic(), boss_damage: '0', ignore_monster_armor: '0', all_stat: '0', max_hp_rate: '0',
              max_mp_rate: '0', base_equipment_level: 0, damage: '0' },
            addOption: { ...basic(), boss_damage: '0', damage: '0', all_stat: '0', equipment_level_decrease: 0 },
            etcOption: basic(), starforceOption: basic() });
          const three = a => [0, 1, 2].map(i => (a && a[i]) || null);
          // 환산 보관함에 들어가는 아이템 모양으로 만든다
          const make = f => ({ title: f.title, slot: guessSlot(f.cat, f.part), item: {
            slot: '', part: f.part, name: f.name, iconUrl: f.iconUrl || '', starforce: String(f.starforce || 0),
            starforce_scroll_flag: '미사용', scroll_upgrade: String(f.scroll || 0), ...f.opt,
            exceptionalOption: { str: '0', dex: '0', int: '0', luk: '0', max_hp: '0', max_mp: '0', attack_power: '0', magic_power: '0', exceptional_upgrade: 0 },
            potential_grade: f.pots.main ? f.pots.main.grade : null, potential_option_1: three(f.pots.main && f.pots.main.lines),
            additional_potential_grade: f.pots.add ? f.pots.add.grade : null, additional_potential_option_1: three(f.pots.add && f.pots.add.lines),
            hasExceptional: false, soul_name: null, soul_option: null, soul_atk: null, soul_potential_grade: null,
            soul_potential_amplified_grade: null, soul_potential_option_1: [null, null, null], ring_level: f.ring || 0,
            character_name: '', class_group: f.classGroup || '', cuttable_count: String(f.cut || '255'), itemScore: '0' } });

          // 검색 결과 자료에서 만든다 (사이트가 받아 오는 자료에 옵션이 숫자로 나뉘어 들어 있다)
          const SK = { str: 'str', dex: 'dex', int: 'int', luk: 'luk', mhp: 'max_hp', mmp: 'max_mp', pad: 'attack_power',
            mad: 'magic_power', pdd: 'armor', speed: 'speed', jump: 'jump', bdr: 'boss_damage', imdr: 'ignore_monster_armor',
            all: 'all_stat', dam: 'damage', hpr: 'max_hp_rate', mpr: 'max_mp_rate', reduceReq: 'equipment_level_decrease' };
          const fill = (o, st) => {
            if (st) for (const k in SK) { const key = SK[k]; if (key in o && st[k]) o[key] = key === 'equipment_level_decrease' ? Math.abs(st[k]) : String(st[k]); }
          };
          const fromApi = (it, icon) => {
            const t = it.toolTip || {}, u = t.upgradeInfo || {}, opt = blank();
            fill(opt.totalOption, t.stat); fill(opt.baseOption, t.baseStat); fill(opt.starforceOption, t.starforceStat);
            fill(opt.etcOption, t.upgradeStat); fill(opt.addOption, t.exOptionStat);
            opt.baseOption.base_equipment_level = t.reqLevel || 0;
            const pot = p => p && Array.isArray(p.entries) && p.entries.length
              ? { grade: String(p.description || '').split(' : ').pop().trim(), lines: p.entries.map(e => potLine(String(e.text || '').trim())) } : null;
            const cats = t.categories || [], name = t.itemName || it.itemName || '';
            const scroll = u.scroll ? u.scroll.current : it.currentUpgradeCount;
            return make({ title: name + (scroll ? ' (+' + scroll + ')' : ''), cat: cats[0] || '', part: cats[1] || cats[0] || '', name,
              iconUrl: icon || (t.itemIcon && t.itemIcon.fallBackUrl), starforce: t.starforce ?? it.starforce, scroll, opt,
              pots: { main: pot(u.potential), add: pot(u.additionalPotential) }, classGroup: t.reqJob, ring: it.seedRingLevel,
              cut: ((t.tradeDesc || []).join(' ').match(/가위 사용 잔여 횟수\s*:\s*(\d+)/) || [])[1] });
          };

          // 설명창에서 읽기 (검색 결과 자료가 없을 때)
          const read = card => {
            const nameEl = card.querySelector('div[class*="text-[15px]"]');
            if (!nameEl) return null;
            const title = nameEl.textContent.trim();
            const opt = blank(), total = opt.totalOption;
            let classGroup = '', level = 0;
            card.querySelectorAll('div.shrink-0').forEach(k => {
              const label = k.textContent.trim(), v = k.nextElementSibling;
              if (!v) return;
              if (label === '착용 직업') { classGroup = v.textContent.trim(); return; }
              if (label === '요구 레벨') { level = Math.max(0, ...(v.textContent.match(/\d+/g) || []).map(Number)); return; }
              let key = KEY[label];
              if (!key) return;
              const parts = [...v.querySelectorAll('span')].filter(s => !s.children.length);
              if (!parts.length) return;
              const pct = parts[0].textContent.includes('%');
              if (pct && key === 'max_hp') key = 'max_hp_rate';
              if (pct && key === 'max_mp') key = 'max_mp_rate';
              const put = (o, n) => { if (key in o) o[key] = key === 'equipment_level_decrease' ? Math.abs(n) : String(n); };
              put(total, num(parts[0].textContent));
              if (parts.length === 1) { put(opt.baseOption, num(parts[0].textContent)); return; }
              for (const s of parts.slice(1)) {
                const t = s.textContent;
                if (!/\d/.test(t)) continue;
                const c = [...s.classList].map(x => x.replace('text-pc-item-tooltip-option-', '')).find(x => SRC[x]);
                if (c) put(opt[SRC[c]], num(t)); else if (t.trim().startsWith('(')) put(opt.baseOption, num(t));
              }
            });
            opt.baseOption.base_equipment_level = level;
            const pots = {};
            card.querySelectorAll('span').forEach(h => {
              const m = h.children.length ? null : h.textContent.trim().match(/^(에디셔널 )?잠재능력 : (.+)$/);
              const sec = m && h.parentElement && h.parentElement.parentElement;
              if (!sec) return;
              const lines = [...sec.querySelectorAll('span')].filter(s => s.classList.contains('ml-[2px]')).map(s => potLine(s.textContent.trim()));
              pots[m[1] ? 'add' : 'main'] = { grade: m[2].trim(), lines };
            });
            const text = card.textContent;
            const badges = [...card.querySelectorAll('span')].filter(s => [...s.classList].some(c => c.endsWith('gray-label'))).map(s => s.textContent.trim());
            const cat = badges[0] || '', part = badges[1] || badges[0] || '';
            const img = [...card.querySelectorAll('img')].find(i => /^https:/.test(i.src) && i.alt && i.alt !== 'MVP');
            return make({ title, cat, part, name: title.replace(/\s*\(\+\d+\)\s*$/, ''), iconUrl: img ? img.src : '',
              starforce: card.querySelectorAll('svg.text-pc-item-tooltip-option-yellow').length,
              scroll: (text.match(/주문서 강화 (\d+)회/) || [])[1], opt, pots, classGroup,
              cut: (text.match(/가위 사용 잔여 횟수\s*:\s*(\d+)/) || [])[1] });
          };
          const tipCard = () => {
            const tip = document.querySelector('[role="tooltip"]');
            const card = tip && tip.firstElementChild && tip.firstElementChild.firstElementChild;
            return card && card.textContent.includes('요구 레벨') ? card : null;
          };

          // 아래쪽 칸: 처음 설명창이 뜰 때 나타난다
          let box = null, picked = null;
          const ui = () => {
            if (box && box.isConnected) return box;
            const css = document.createElement('style');
            css.textContent = '#mbPick{position:fixed;left:50%;bottom:14px;transform:translateX(-50%);z-index:2147483646;display:flex;align-items:center;gap:8px;' +
              'background:#fff;border:1px solid #ffd9a8;border-radius:10px;box-shadow:0 4px 18px rgba(0,0,0,.18);padding:7px 9px;' +
              "font:13px 'Malgun Gothic',sans-serif;color:#222;max-width:calc(100vw - 32px)}" +
              '#mbPick img{width:28px;height:28px;object-fit:contain}#mbPick img:not([src]){display:none}' +
              '#mbPick b{max-width:230px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}#mbPick .hint{color:#888}' +
              "#mbPick select{border:1px solid #ddd;border-radius:6px;padding:3px 4px;font:13px 'Malgun Gothic',sans-serif}" +
              "#mbPick button{border:0;border-radius:6px;padding:6px 12px;font:bold 13px 'Malgun Gothic',sans-serif;cursor:pointer}" +
              '#mbPick .go{background:#ff8a00;color:#fff}#mbPick .go:disabled{background:#ffd9a8;cursor:default}#mbPick .x{background:#f1f1f1;color:#666;padding:6px 9px}';
            document.documentElement.appendChild(css);
            box = document.createElement('div');
            box.id = 'mbPick';
            box.innerHTML = '<img alt=""><b class="hint">아이템을 누르면 여기 담겨요</b><span>자리</span><select></select>' +
              '<button class="go" disabled title="환산 보관함에 넣고 효율·보스컷을 열어요">환산에 넣기</button><button class="x" title="닫기">✕</button>';
            const sel = box.querySelector('select'), go = box.querySelector('.go');
            SLOTS.forEach(s => sel.add(new Option(s, s)));
            box.querySelector('.x').onclick = () => { box.style.display = 'none'; };
            go.onclick = () => {
              if (!picked) return;
              const it = { ...picked.item, slot: sel.value, character_name: 'ItemMaker' + Date.now().toString(36), mbWho: who || undefined };
              try { chrome.webview.postMessage('mb:scouter:' + JSON.stringify(it)); } catch (_) {}
              go.textContent = '넣었어요';
              setTimeout(() => { go.textContent = '환산에 넣기'; }, 1500);
            };
            document.documentElement.appendChild(box);
            return box;
          };
          // 칸이 한 번 만들어지면 더 볼 필요가 없으니 타이머를 끈다
          const watchTip = setInterval(() => {
            if (box) { clearInterval(watchTip); return; }
            if (tipCard()) ui();
          }, 400);

          // 사이트가 받아 온 검색 결과를 최근 것 몇 묶음 기억해 둔다 (응답은 그대로 두고 복사본만 읽는다)
          const lists = [];
          const keepList = t => {
            try {
              const j = JSON.parse(t);
              if (!j || !Array.isArray(j.items) || !j.items.some(x => x && x.toolTip)) return;
              lists.unshift(j.items);
              if (lists.length > 6) lists.length = 6;
              ui();
            } catch (_) {}
          };
          // 받은 자료는 우리 경매장 화면(AuctionUiScript)에도 알린다 (주소·방식·글자 그대로)
          // 경매장에서 고른 캐릭터 이름 (착용 창은 늘 이 캐릭터로 연다). 캐릭터 모습 자료에 들어 있다
          let who = '';
          try { who = sessionStorage.getItem('mb-who') || ''; } catch (_) {}
          const got = (u, m, t) => {
            keepList(t);
            if (/\/market\/web\/inventory\/cash-equip\/appearance/.test(u)) {
              try { const n = JSON.parse(t).main.characterName; if (typeof n === 'string' && n) { who = n; sessionStorage.setItem('mb-who', n); } } catch (_) {}
            }
            try { window.dispatchEvent(new CustomEvent('mb:api', { detail: { url: u, method: String(m || 'GET').toUpperCase(), text: t } })); } catch (_) {}
          };
          const isApi = u => { try { return /(^|\.)nexon\.com$/.test(new URL(u, location.href).hostname); } catch (_) { return false; } };
          const of = window.fetch;
          window.fetch = async function (...a) {
            const r = await of.apply(this, a);
            try {
              const u = a[0] instanceof Request ? a[0].url : String(a[0]);
              const m = (a[1] && a[1].method) || (a[0] instanceof Request ? a[0].method : 'GET');
              if (isApi(u) && /json/.test(r.headers.get('content-type') || '')) r.clone().text().then(t => got(u, m, t)).catch(() => {});
            } catch (_) {}
            return r;
          };
          const oo = XMLHttpRequest.prototype.open;
          XMLHttpRequest.prototype.open = function (m, u, ...rest) {
            if (isApi(String(u))) this.addEventListener('load', () => {
              try {
                if (this.responseType === 'json') got(String(u), m, JSON.stringify(this.response));
                else if (this.responseType === '' || this.responseType === 'text') got(String(u), m, this.responseText);
              } catch (_) {}
            });
            return oo.call(this, m, u, ...rest);
          };

          // 누른 곳이 들어 있는 결과 줄 (아이템 그림과 '메소'가 든 작은 상자)
          const rowOf = el => {
            for (let n = el; n && n.nodeType === 1 && n !== document.body; n = n.parentElement) {
              const t = n.textContent || '';
              if (t.length > 300) return null;
              if (t.includes('메소') && n.querySelector('img[alt]:not([alt=""])')) return n.closest('.isolate') || n;
            }
            return null;
          };
          // '1100억', '3억 5000만 1234' → 숫자
          const money = s => {
            s = s.replace(/,/g, '');
            const g = r => { const m = s.match(r); return m ? Number(m[1]) : 0; };
            return g(/(\d+)억/) * 1e8 + g(/(\d+)만/) * 1e4 + g(/(\d+)\s*$/);
          };
          // 줄의 이름·가격과 맞는 검색 결과를 찾는다 (같은 이름이 여럿이면 줄 순서로 고른다)
          const itemOf = row => {
            const img = row.querySelector('img[alt]:not([alt=""])'), name = img ? img.alt.trim() : '';
            const pm = (row.innerText || '').match(/([\d,]+억(?:\s*[\d,]+만)?(?:\s*[\d,]+)?|[\d,]+만(?:\s*[\d,]+)?|[\d,]+)\s*메소/);
            const price = pm ? money(pm[1]) : null;
            const idx = row.parentElement ? [...row.parentElement.children].indexOf(row) : -1;
            const ok = it => it && it.toolTip && (it.itemName === name || it.toolTip.itemName === name) &&
              (price == null || Number(it.pricePerItem) === price || Number(it.price) === price);
            for (const l of lists) if (ok(l[idx])) return { it: l[idx], icon: img && img.src };
            for (const l of lists) { const f = l.find(ok); if (f) return { it: f, icon: img && img.src }; }
            return null;
          };

          const show = p => {
            picked = p;
            const b = ui();
            b.style.display = 'flex';
            if (p.item.iconUrl) b.querySelector('img').src = p.item.iconUrl; else b.querySelector('img').removeAttribute('src');
            const name = b.querySelector('b');
            name.textContent = p.title; name.className = '';
            b.querySelector('select').value = p.slot;
            b.querySelector('.go').disabled = false;
          };
          // 우리 경매장 화면에서 결과 줄을 누르면 이걸로 담는다
          window.__mbPick = (it, icon) => { try { show(fromApi(it, icon)); } catch (_) {} };
          // 우리 경매장 화면의 '착용 해보기': 고를 것 없이 맞는 자리로 바로 환산에 넣는다
          window.__mbWear = (it, icon) => {
            try {
              const p = fromApi(it, icon);
              chrome.webview.postMessage('mb:scouter:' + JSON.stringify({ ...p.item, slot: p.slot, character_name: 'ItemMaker' + Date.now().toString(36), mbWho: who || undefined }));
              return true;
            } catch (_) { return false; }
          };
          // 결과 줄 아무 곳이나 누르면 그 아이템을 담는다. 설명창이 떠 있으면 그것도 쓴다 (사이트가 누름을 쓰는 것은 막지 않는다)
          let lastRow = null;
          document.addEventListener('mousedown', e => {
            if (e.button !== 0 || (box && box.contains(e.target))) return;
            try {
              const row = rowOf(e.target), hit = row && itemOf(row);
              if (hit) {
                show(fromApi(hit.it, hit.icon));
                if (lastRow) lastRow.style.outline = '';
                row.style.outline = '2px solid #ff8a00'; row.style.outlineOffset = '1px'; row.style.borderRadius = '12px';
                lastRow = row;
                return;
              }
              const card = tipCard(), p = card && read(card);
              if (p) show(p);
            } catch (_) {}
          }, true);
        })();
        """;

    // 우리 경매장 화면: 사이트 위에 덮는 보기 쉬운 검색 화면. 사이트는 아래에 그대로 둔다.
    // 우리 칸의 값을 사이트 검색 양식에 넣고 사이트의 '필터 검색'을 누른다. 결과는 사이트가 받아 온 자료(PickScript가 알려 줌)로 그린다.
    // 구매는 '원래 화면'에서만 한다. 사이트를 못 읽으면 '원래 화면'으로 안내한다
    public const string AuctionUiScript = """
        (() => {
          if (location.hostname !== 'auction.maplestory.nexon.com') return;
          if (window.__mbUi) return;
          window.__mbUi = true;

          // ── 사이트 자료 (사이트 코드에서 옮겨 온 분류·옵션 이름) ──
          const TREE = [["armor","방어구",[["armor","방어구",["cap:모자","coat:상의","longcoat:한벌옷","pants:하의","shoes:신발","glove:장갑","shield:방패","cape:망토"]],["accessory","장신구",["face:얼굴장식","eye:눈장식","ear:귀고리","ring:반지","pendant:펜던트","belt:벨트","medal:훈장","shoulder:어깨장식","pocket:포켓아이템","badge:뱃지","emblem:엠블렘","power-source:파워소스"]],["etc","기타",["mechanic:메카닉장비","android:안드로이드","machine-heart:기계심장","dragon-cap:드래곤장비"]]]],["weapon","무기",[["one-handed-weapon","한손무기",["shining-rod:샤이닝로드","soul-shooter:소울슈터","desperado:데스페라도","energy-sword:에너지소드","sword:한손검","axe:한손도끼","mace:한손둔기","dagger:단검","cane:케인","wand:완드","staff:스태프","esp-limiter:ESP리미터","chain:체인","magic-gauntlet:매직 건틀렛","fan:부채","tuner:튜너","breath-shooter:브레스 슈터","long-sword:장검","scroll:카르타"]],["two-handed-weapon","두손무기",["sword:두손검","axe:두손도끼","mace:두손둔기","spear:창","polearm:폴암","bow:활","crossbow:석궁","throwing-glove:아대","knuckle:너클","gun:건","dual-bow:듀얼보우건","hand-cannon:핸드캐논","gauntlet-revolver:건틀렛 리볼버","ancient-bow:에인션트 보우","chakram:차크람"]],["sub-weapon","보조무기",["hero-medal:메달","rosario:로자리오","chain:쇠사슬","book:마도서","bow-master-feather:화살깃","cross-bow-thimble:활골무","shadower-sheath:단검용 검집","blade:블레이드","night-lord-pouch:부적","viper-wristband:리스트밴드","captain-sight:조준기","cannon-gun-powder:화약통","cygnus-gem:보석","aran-pendulum:무게추","evan-paper:문서","orb:오브","magic-arrow:마법화살","card:카드","eunwol-fox-orb:여우구슬","battlemage-orb:마법구슬","wild-hunter-arrow-head:화살촉","xenon-controler:컨트롤러","mechanic-magnum:매그넘","kaiser-nova:노바의 정수","angelic-buster-soul-ring:소울링","chess-piece:체스피스","charge:장약","transmitter:무기 전송장치","magic-wing:매직 윙","pass:패스","relic:렐릭","fan-stone:선추","bracelet:브레이슬릿","force-shield:포스실드","soul-shield:소울실드","weapon-belt:웨폰벨트","norigae:노리개","hex-seeker:헥스시커","sacred-jewel:여의보주","demon-mage:마법깃펜"]]]],["consume","소비",[["potion","회복 아이템",["potion:회복 아이템","cure:상태 이상 회복"]],["alchemy","연금술 아이템",["potion:회복","elixer:비약","buff:버프"]],["scroll","주문서",["armor:방어구 / 장신구 주문서","weapon:무기 주문서","enchant:장비 강화 / 잠재 주문서","white:백의 / 혼돈의 주문서","flame:환생의 불꽃","pet:펫 주문서","etc:기타 주문서"]],["recipe","전문기술 레시피",["equip:장비 레시피","acceesory:장신구 레시피","alchemy:연금술 레시피"]],"skill-book:스킬북",["etc","기타",["move-map:맵 이동 아이템","arrow:화살 / 표창","etc:기타"]]]],["cash","캐시",[["enchant","강화",["cube:큐브","scroll:주문서"]],["game","게임",["convenience:편의","shop:상점","messenger:메신저","weather-effect:기상효과"]],["beauty","뷰티",["hair:헤어","cosmetic:성형","emotion:감정표현","etc:기타"]],["pet","펫",["pet:펫","pet-equip:펫장비","pet-food:펫먹이","pet-skill:펫스킬"]],"etc:기타"]],["coordination","코디",["coupon:쿠폰","weapon:무기","cap:모자","cape:망토","long-coat:한벌옷","coat:상의","pants:하의","shoes:신발","glove:장갑","face:얼굴장식","ear:귀장식","ring:반지","eye:눈장식","effect:이펙트","shield:방패"]],["etc","기타",["chair:의자","sub-job:전문기술"]]];
          const OPT = "bossDamagePercent:보스 몬스터 데미지 %|ignoreMonsterDefense:몬스터 방어율 무시|damagePercent:데미지 %|physicalAttackPercent:공격력 %|magicAttackPercent:마력 %|allStatsPercent:올스탯 %|strPercent:STR %|dexPercent:DEX %|intPercent:INT %|lukPercent:LUK %|criticalPercent:크리티컬 확률|statusResistance:상태 이상 내성|elementalResistance:모든 속성 내성|criticalDamagePercent:크리티컬 데미지|skillCooldownReduction:스킬 재사용 대기시간 감소|itemDropPercent:아이템 획득 확률|maxHpPercent:최대 HP %|maxMpPercent:최대 MP %|physicalAttack:공격력|magicAttack:마력|hasteSkill:<쓸만한 헤이스트> 스킬|mysticDoorSkill:<쓸만한 미스틱 도어> 스킬|sharpEyesSkill:<쓸만한 샤프 아이즈> 스킬|hyperBodySkill:<쓸만한 하이퍼 바디> 스킬|combatOrdersSkill:<쓸만한 컴뱃 오더스> 스킬|advancedBlessSkill:<쓸만한 어드밴스드 블레스> 스킬|windBoosterSkill:<쓸만한 윈드 부스터> 스킬|str:STR|dex:DEX|int:INT|luk:LUK|maxHp:최대 HP|maxMp:최대 MP|allStats:올스탯|skillLevelIncrease:4차 이하 스킬 레벨|hpRecovery:공격 시 HP 회복|mpRecovery:공격 시 MP 회복|hpRecoveryRate:HP 회복 아이템 및 스킬 효율|mesosObtainedPercent:메소 획득량|strPerLevel:9레벨 당 STR|dexPerLevel:9레벨 당 DEX|intPerLevel:9레벨 당 INT|lukPerLevel:9레벨 당 LUK";
          const MAIN = "bossDamagePercent ignoreMonsterDefense damagePercent physicalAttackPercent magicAttackPercent allStatsPercent strPercent dexPercent intPercent lukPercent criticalPercent statusResistance elementalResistance criticalDamagePercent skillCooldownReduction itemDropPercent maxHpPercent maxMpPercent physicalAttack magicAttack hasteSkill mysticDoorSkill sharpEyesSkill hyperBodySkill combatOrdersSkill advancedBlessSkill windBoosterSkill str dex int luk maxHp maxMp allStats skillLevelIncrease hpRecovery mpRecovery hpRecoveryRate mesosObtainedPercent";
          const ADD = "bossDamagePercent ignoreMonsterDefense damagePercent physicalAttackPercent magicAttackPercent allStatsPercent strPercent dexPercent intPercent lukPercent criticalPercent statusResistance elementalResistance criticalDamagePercent skillCooldownReduction itemDropPercent maxHpPercent maxMpPercent physicalAttack magicAttack str dex int luk maxHp maxMp allStats skillLevelIncrease hpRecovery mpRecovery strPerLevel dexPerLevel intPerLevel lukPerLevel hpRecoveryRate mesosObtainedPercent";
          const pairOf = s => { const i = s.indexOf(':'); return [s.slice(0, i), s.slice(i + 1)]; };
          const LABEL = Object.fromEntries(OPT.split('|').map(pairOf));
          const GRADES = [['all', '전체'], ['none', '없음'], ['rare', '레어'], ['epic', '에픽'], ['unique', '유니크'], ['legendary', '레전드리']];
          const JOBS = [['all', '전체'], ['warrior', '전사'], ['mage', '마법사'], ['archer', '궁수'], ['thief', '도적'], ['pirate', '해적']];
          const SORTS = ['낮은 가격순', '개당 낮은 가격순', '전투력 증가량 높은순', '판매 종료 빠른순', '높은 가격순', '최신 등록순', '아이템명 가나다순'];
          const GCOLOR = { 1: '#3a9be0', 2: '#9b59d0', 3: '#d9a000', 4: '#2fa866' };
          const F = 'filters.';
          const K = {
            cat: 'itemCategory', pmin: 'price::min', pmax: 'price::max',
            smin: 'enhancementOption::starforceMin', smax: 'enhancementOption::starforceMax',
            pg: 'enhancementOption::potentialGrade', ag: 'enhancementOption::additionalPotentialGrade',
            psum: 'enhancementOption::potentialFilters::isAddAll', prows: 'enhancementOption::potentialFilters::optionRows',
            asum: 'enhancementOption::additionalPotentialFilters::isAddAll', arows: 'enhancementOption::additionalPotentialFilters::optionRows',
            lmin: 'basicOption::levelMin', lmax: 'basicOption::levelMax', job: 'basicOption::jobClass', world: 'isAllWorldSearchEnabled'
          };
          const OURS = new Set(Object.values(K));
          const ROWS = 3;            // 옵션 줄 기본 개수
          const GAP = 5000;          // 검색 사이 최소 간격
          const KEEP = 10 * 60000;   // 같은 검색 결과를 기억하는 시간
          const SITE = '@site';      // 사이트 화면에서 직접 찾은 결과 (조건을 모른다)

          // 사이트 프리셋(서버 모양) → 우리 칸 값. 사이트가 프리셋을 양식으로 바꾸는 규칙을 그대로 따른다
          const GNUM = ['none', 'rare', 'epic', 'unique', 'legendary'];
          const JOBNUM = { FIGHTER: 'warrior', WIZARD: 'mage', ARCHER: 'archer', THIEF: 'thief', PIRATE: 'pirate' };
          const CATFIX = {
            'weapon::one-handed-weapon': 'WEAPON_ONE_HANDED', 'weapon::two-handed-weapon': 'WEAPON_TWO_HANDED', 'weapon::sub-weapon': 'WEAPON_SUB',
            'weapon::sub-weapon::cross-bow-thimble': 'WEAPON_SUB_CROSSBOW_THIMBLE', 'weapon::sub-weapon::xenon-controler': 'WEAPON_SUB_XENON_CONTROLLER',
            'coordination::long-coat': 'CASH_COORDINATION_LONGCOAT', 'consume::recipe::acceesory': 'CONSUME_RECIPE_ACCESSORY', 'etc::sub-job': 'ETC_SUBJOB'
          };
          const apiCat = id => {
            if (/::all$/.test(id)) return apiCat(id.slice(0, -5));
            if (CATFIX[id]) return CATFIX[id];
            return id.replace(/^weapon::one-handed-weapon/, 'weapon::one-handed').replace(/^weapon::two-handed-weapon/, 'weapon::two-handed')
              .replace(/^weapon::sub-weapon/, 'weapon::sub').replace(/^coordination/, 'cash::coordination')
              .split('::').join('_').split('-').join('_').toUpperCase();
          };
          // 분류 나무를 한 줄로 편다 (사이트와 같은 순서: 위 → 묶음 → 잎)
          let NODES = null;
          const nodes = () => {
            if (NODES) return NODES;
            NODES = [];
            for (const [top, label, kids] of TREE) {
              NODES.push({ id: top, path: [top, top + '::all'], label });
              for (const ch of kids) {
                if (typeof ch === 'string') { const [id, l] = pairOf(ch); NODES.push({ id: top + '::' + id, path: [top, top + '::' + id], label: l }); continue; }
                const [gid, gl, leaves] = ch, gp = top + '::' + gid;
                NODES.push({ id: gp, path: [top, gp, gp + '::all'], label: gl });
                leaves.forEach(x => { const [id, l] = pairOf(x); NODES.push({ id: gp + '::' + id, path: [top, gp, gp + '::' + id], label: l }); });
              }
            }
            return NODES;
          };
          const catOf = detail => { const n = detail && nodes().find(n => apiCat(n.id) === detail); return n ? n.path.slice() : null; };
          const catLabel = path => { const s = JSON.stringify(path || []), n = nodes().find(n => JSON.stringify(n.path) === s); return n ? n.label : ''; };

          // ── 도움 함수 ──
          const $ = (s, r) => (r || root).querySelector(s);
          const el = (tag, cls, text) => { const e = document.createElement(tag); if (cls) e.className = cls; if (text != null) e.textContent = text; return e; };
          const store = { get: k => { try { return localStorage.getItem(k); } catch (_) { return null; } }, set: (k, v) => { try { if (v == null) localStorage.removeItem(k); else localStorage.setItem(k, v); } catch (_) {} } };
          const comma = n => Number(n).toLocaleString('ko-KR');
          const clone = x => x == null ? x : JSON.parse(JSON.stringify(x));
          // 메소 → '1,100억', '3억 5,000만'
          const won = v => {
            const n = Number(v);
            if (!isFinite(n) || n <= 0) return '0';
            const e = Math.floor(n / 1e8), m = Math.floor((n % 1e8) / 1e4), r = n % 1e4;
            if (e) return comma(e) + '억' + (m ? ' ' + comma(m) + '만' : '');
            if (m) return comma(m) + '만' + (r ? ' ' + comma(r) : '');
            return comma(n);
          };
          // 칸에 넣는 메소 글자. 끝자리까지 남겨서 다시 읽어도 같은 값이 된다
          const wonField = v => {
            const n = Number(v);
            if (!isFinite(n) || n <= 0) return '';
            const e = Math.floor(n / 1e8), m = Math.floor((n % 1e8) / 1e4), r = n % 1e4;
            return [e && comma(e) + '억', m && comma(m) + '만', r && comma(r)].filter(Boolean).join(' ');
          };
          // '10억', '3억5000만', '25000000' → 메소 글자. 빈칸은 '', 못 읽으면 null
          const meso = s => {
            s = String(s || '').replace(/[,\s]/g, '');
            if (!s) return '';
            if (/^\d+$/.test(s)) return String(Number(s));
            const m = s.match(/^(?:(\d+(?:\.\d+)?)억)?(?:(\d+(?:\.\d+)?)만)?(\d+)?$/);
            if (!m || !(m[1] || m[2] || m[3])) return null;
            return String(Math.round((Number(m[1]) || 0) * 1e8 + (Number(m[2]) || 0) * 1e4 + (Number(m[3]) || 0)));
          };
          const int = s => { s = String(s || '').trim(); return s === '' ? '' : (/^\d+$/.test(s) ? String(Number(s)) : null); };
          const two = n => String(n).padStart(2, '0');
          const when = t => { const d = new Date(Number(t)); return isNaN(d) ? '' : `${d.getMonth() + 1}월 ${d.getDate()}일 ${two(d.getHours())}:${two(d.getMinutes())}`; };
          const ago = t => { const m = Math.floor((Date.now() - t) / 60000); return m < 1 ? '방금 찾은 결과' : `${m}분 전 결과`; };
          const secs = ms => (Math.ceil(ms / 100) / 10).toFixed(1); // 남은 시간을 소수 첫째 자리까지

          // ── 사이트 쪽 (화면 아래에 그대로 있는 사이트를 움직인다) ──
          let root = null, back = null;
          const own = n => !!(n && ((root && root.contains(n)) || (back && back.contains(n)) || (n.closest && n.closest('#mbPick'))));
          const flat = n => (n.textContent || '').replace(/\s/g, '');
          const siteBtn = t => [...document.querySelectorAll('button')].find(b => !own(b) && flat(b).startsWith(t));
          const fiberOf = n => { if (!n) return null; for (const k of Object.keys(n)) if (k.startsWith('__reactFiber$')) return n[k]; return null; };
          // 사이트 필터 칸들이 쓰는 양식(react-hook-form)을 필터 검색 버튼 위쪽에서 찾는다
          const findForm = () => {
            for (let f = fiberOf(siteBtn('필터검색')); f; f = f.return) {
              const v = f.memoizedProps && f.memoizedProps.value;
              if (v && typeof v.getValues === 'function' && typeof v.setValue === 'function') {
                try { const g = v.getValues(); if (g && g.filters) return v; } catch (_) {}
              }
            }
            return null;
          };
          // 양식의 처음 값 (사이트가 '초기화'에 쓰는 값)
          const defaults = form => {
            try { const d = (form.control && form.control._defaultValues) || (form.formState && form.formState.defaultValues); return d && d.filters ? d.filters : null; } catch (_) { return null; }
          };
          const visible = n => !!(n && n.getClientRects().length && getComputedStyle(n).visibility !== 'hidden');
          const siteDialog = () => [...document.querySelectorAll('[role="dialog"],[role="alertdialog"]')].some(d => !own(d) && visible(d));
          const onSearchPage = () => !!siteBtn('필터검색');
          const clickText = (t, scope) => {
            const c = [...(scope || document).querySelectorAll('button,[role="tab"],[role="option"],[role="menuitem"],[role="radio"],li,a,span,div')]
              .filter(n => !own(n) && visible(n) && n.textContent.trim() === t);
            // 가장 안쪽 것을 누른다
            const hit = c.find(n => !c.some(o => o !== n && n.contains(o)));
            if (hit) { (hit.closest('button,[role="tab"],[role="option"],[role="menuitem"],[role="radio"],li,a') || hit).click(); return true; }
            return false;
          };
          const wait = ms => new Promise(r => setTimeout(r, ms));

          // ── 화면 만들기 ──
          const CSS = `
        #mbAuc{position:fixed;inset:0;z-index:2147483000;display:flex;flex-direction:column;background:#f3f4f6;color:#1f2329;
          font:13px 'Malgun Gothic',sans-serif;overscroll-behavior:contain}
        #mbAuc *{box-sizing:border-box}
        #mbAuc [hidden]{display:none!important}
        #mbAuc input,#mbAuc select,#mbAuc button{font:13px 'Malgun Gothic',sans-serif;color:#1f2329;letter-spacing:normal;max-width:100%}
        #mbAuc input,#mbAuc select{height:30px;border:1px solid #d9dce1;border-radius:6px;background:#fff;padding:0 8px;min-width:0}
        #mbAuc input:focus,#mbAuc select:focus{outline:2px solid #ffb35c;outline-offset:-1px}
        #mbAuc input.bad{border-color:#e5484d;background:#fff4f4}
        #mbAuc input[type=checkbox]{width:14px;height:14px;padding:0;margin:0;flex:0 0 auto}
        #mbAuc button{height:30px;border:1px solid #d9dce1;border-radius:6px;background:#fff;padding:0 12px;cursor:pointer;white-space:nowrap}
        #mbAuc button:hover{background:#f6f7f9}
        #mbAuc button:disabled{opacity:.45;cursor:default}
        #mbAuc button.main{background:#ff8a00;border-color:#ff8a00;color:#fff;font-weight:bold}
        #mbAuc button.main:hover{background:#f27f00}
        #mbAuc button.main.wait{background:#ffe2c0;border-color:#ffd09a;color:#a35600}
        #mbAuc .top{display:flex;gap:8px;align-items:center;padding:10px 12px;background:#fff;border-bottom:1px solid #e8eaed}
        #mbAuc .top .kw{flex:1 1 0;width:0;height:36px;font-size:14px;padding:0 12px}
        #mbAuc .top .go{height:36px;min-width:72px;font-variant-numeric:tabular-nums}
        #mbAuc .top .orig{height:36px;color:#555}
        #mbAuc .quota{flex:0 0 auto;display:flex;flex-direction:column;justify-content:center;gap:5px;height:36px;padding:0 10px;
          border:1px solid #e8eaed;border-radius:8px;background:#fafbfc;min-width:138px;cursor:default}
        #mbAuc .quota .qt{display:flex;justify-content:space-between;align-items:baseline;gap:10px;font-size:11px;color:#80868f;line-height:1}
        #mbAuc .quota .qt b{font-size:13px;color:#1f2329}
        #mbAuc .quota i{display:block;height:3px;border-radius:2px;background:#e6e8eb;overflow:hidden}
        #mbAuc .quota s{display:block;height:100%;width:0;background:#2fa866;transition:width .3s}
        #mbAuc .quota.low .qt b{color:#e5484d}
        #mbAuc .quota.low s{background:#e5484d}
        #mbAuc .pre{display:flex;gap:10px;align-items:flex-start;padding:8px 12px;background:#fff;border-bottom:1px solid #e8eaed}
        #mbAuc .pre .pl{flex:0 0 auto;font-size:12px;color:#80868f;line-height:28px}
        #mbAuc .pre .chips{flex:1;min-width:0;display:flex;flex-wrap:wrap;gap:6px;max-height:62px;overflow-y:auto;scrollbar-width:thin}
        #mbAuc .pre .none{font-size:12px;color:#a0a5ad;line-height:28px}
        #mbAuc .chip{height:28px;max-width:220px;overflow:hidden;text-overflow:ellipsis;padding:0 10px;font-size:12px;background:#f6f7f9;border-color:#e3e6ea;color:#333}
        #mbAuc .chip:hover{background:#fff7ee;border-color:#ffb35c}
        #mbAuc .chip.on{background:#fff2e2;border-color:#ff8a00;color:#b35f00;font-weight:bold}
        #mbAuc .chip.add{background:#fff;border-style:dashed;color:#80868f}
        #mbAuc .chip.add:hover{color:#b35f00}
        #mbAuc .body{flex:1;min-height:0;display:grid;grid-template-columns:292px minmax(0,1fr)}
        #mbAuc .fil{overflow-y:auto;overflow-x:hidden;min-width:0;padding:12px 12px 70px;border-right:1px solid #e8eaed;background:#fff}
        #mbAuc .sec{border:1px solid #eceef1;border-radius:8px;padding:10px;margin-bottom:10px}
        #mbAuc .sec>.h{display:flex;gap:8px;justify-content:space-between;align-items:center;margin-bottom:8px;font-weight:bold;font-size:12px;color:#444;white-space:nowrap}
        #mbAuc .sec>.h .r{display:flex;gap:8px;align-items:center;min-width:0}
        #mbAuc .sec>.h select{height:26px;font-size:12px;width:86px;min-width:76px;padding:0 4px}
        #mbAuc .sec>.h label{font-weight:normal;color:#666;display:flex;gap:4px;align-items:center;cursor:pointer}
        #mbAuc details.sec>summary{cursor:pointer;color:#444;font-size:12px;font-weight:bold}
        #mbAuc details.sec[open]>summary{margin-bottom:8px}
        #mbAuc .grid{display:grid;grid-template-columns:56px minmax(0,1fr);gap:6px 8px;align-items:center}
        #mbAuc .grid>*{min-width:0}
        #mbAuc .grid>label{color:#666;font-size:12px}
        #mbAuc .grid>select{width:100%}
        #mbAuc .two{display:flex;gap:4px;align-items:center;min-width:0}
        #mbAuc .two>input,#mbAuc .two>select{flex:1 1 0;width:0;min-width:0}
        #mbAuc .two>span{flex:0 0 auto;color:#a0a5ad}
        #mbAuc .two input{padding:0 6px}
        #mbAuc .two>select.c1{flex:0 0 78px;width:78px;padding:0 4px}
        #mbAuc .orow{display:flex;gap:4px;margin-top:4px}
        #mbAuc .orow select{flex:1 1 0;width:0;min-width:0}
        #mbAuc .orow input{width:58px;flex:0 0 58px}
        #mbAuc .chk{display:flex;gap:6px;align-items:center;font-size:12px;color:#444;cursor:pointer}
        #mbAuc .note{color:#a0a5ad;font-size:11px;line-height:1.5;padding:0 2px}
        #mbAuc .res{display:flex;flex-direction:column;min-height:0;min-width:0}
        #mbAuc .rt{display:flex;justify-content:space-between;align-items:center;gap:8px;padding:10px 12px 0}
        #mbAuc .tabs{display:flex;gap:2px;padding:2px;background:#e6e8eb;border-radius:8px}
        #mbAuc .tab{height:28px;border:0;border-radius:6px;background:transparent;color:#666;padding:0 12px}
        #mbAuc .tab:hover{background:rgba(255,255,255,.55)}
        #mbAuc .tab.on{background:#fff;color:#1f2329;font-weight:bold;box-shadow:0 1px 2px rgba(0,0,0,.08)}
        #mbAuc .tab .sn{color:#ff8a00;margin-left:4px}
        #mbAuc .rt .sort{height:30px;font-size:12px;width:auto;max-width:160px}
        #mbAuc .bar{display:flex;justify-content:space-between;align-items:center;gap:8px;padding:8px 12px;min-height:46px}
        #mbAuc .bar .info{display:flex;align-items:baseline;gap:8px;min-width:0;white-space:nowrap}
        #mbAuc .bar .cnt{font-weight:bold;flex:0 1 auto;min-width:0;overflow:hidden;text-overflow:ellipsis}
        #mbAuc .bar .msg{color:#80868f;font-size:12px;flex:0 1 auto;min-width:0;overflow:hidden;text-overflow:ellipsis;font-variant-numeric:tabular-nums}
        #mbAuc .rt .srcw{flex:1 1 auto;min-width:0;display:flex}
        #mbAuc .rt .src{display:block;min-width:0;max-width:100%;padding:3px 8px;border-radius:6px;background:#eef0f2;color:#80868f;font-size:11px;line-height:1.3;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;font-variant-numeric:tabular-nums}
        #mbAuc .rt .src[hidden]{display:none}
        #mbAuc .bar .acts{display:flex;gap:6px;align-items:center;flex:0 0 auto}
        #mbAuc .bar button{height:28px;padding:0 10px;font-size:12px}
        #mbAuc .pager{display:flex;gap:4px;align-items:center}
        #mbAuc .pager span{color:#666;font-size:12px;min-width:44px;text-align:center}
        #mbAuc .list{flex:1;overflow-y:auto;padding:0 12px 70px;overscroll-behavior:contain}
        #mbAuc .empty{color:#a0a5ad;text-align:center;padding:48px 16px;line-height:1.8}
        #mbAuc .row{display:flex;gap:10px;align-items:center;background:#fff;border:1px solid #e8eaed;border-radius:8px;padding:8px 10px;margin-bottom:6px}
        #mbAuc .row:hover{border-color:#ffc27a}
        #mbAuc .row .ic{position:relative;flex:0 0 40px;width:40px;height:40px}
        #mbAuc .row img{width:40px;height:40px;object-fit:contain;display:block}
        #mbAuc .row .w{position:absolute;left:-3px;top:-3px;width:15px;height:15px;border-radius:3px;background:#5cb82a;border:1px solid #3d8f17;color:#fff;font:bold 10px/13px Arial,sans-serif;text-align:center;cursor:help}
        #mbAuc .row .mid{flex:1;min-width:0}
        #mbAuc .row .nm{font-weight:bold;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
        #mbAuc .row .nm .sf{color:#e07a00;margin-left:6px;font-weight:normal;font-size:12px}
        #mbAuc .row .pl{font-size:12px;color:#444;margin-top:2px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
        #mbAuc .row .pl b{display:inline-block;font-size:11px;color:#fff;border-radius:4px;padding:0 4px;margin-right:4px;font-weight:normal}
        #mbAuc .row .pr{text-align:right;flex:0 0 auto}
        #mbAuc .row .pr .p{font-weight:bold;font-size:14px}
        #mbAuc .row .pr .s{font-size:11px;color:#888;margin-top:2px}
        #mbAuc .row .see{height:26px;font-size:12px;padding:0 8px;margin-top:4px}
        #mbAuc .srow{display:flex;gap:8px;align-items:center;background:#fff;border:1px solid #e8eaed;border-radius:8px;padding:10px 12px;margin-bottom:6px}
        #mbAuc .srow:hover{border-color:#ffc27a}
        #mbAuc .srow .mid{flex:1;min-width:0}
        #mbAuc .srow .nm{font-weight:bold;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
        #mbAuc .srow .s{font-size:12px;color:#80868f;margin-top:3px}
        #mbAuc .srow button{height:28px;font-size:12px;padding:0 10px}
        #mbAuc .srow .del{color:#80868f}
        #mbAuc .srow .del.sure{background:#e5484d;border-color:#e5484d;color:#fff}
        #mbAucBack{position:fixed;right:14px;bottom:14px;z-index:2147483645;height:34px;padding:0 14px;border:0;border-radius:8px;background:#ff8a00;color:#fff;
          font:bold 13px 'Malgun Gothic',sans-serif;box-shadow:0 4px 14px rgba(0,0,0,.2);cursor:pointer}
        @media (max-width:860px){#mbAuc .body{grid-template-columns:262px minmax(0,1fr)}#mbAuc .grid{grid-template-columns:48px minmax(0,1fr)}#mbAuc .quota{min-width:120px}}
        `;
          const opts = (sel, list) => { sel.textContent = ''; list.forEach(([v, t]) => sel.add(new Option(t, v))); };
          const addRow = box => {
            const keys = (box.dataset.k === 'p' ? MAIN : ADD).split(' ');
            const r = el('div', 'orow'), s = el('select'), n = el('input');
            s.add(new Option('옵션 고르기', 'none'));
            keys.forEach(k => s.add(new Option(LABEL[k] || k, k)));
            n.placeholder = '이상'; n.inputMode = 'numeric';
            r.append(s, n); box.appendChild(r);
          };
          const setRows = (box, n) => {
            let rs = box.querySelectorAll('.orow');
            for (let i = rs.length; i < n; i++) addRow(box);
            rs = box.querySelectorAll('.orow');
            for (let i = rs.length - 1; i >= n; i--) rs[i].remove();
          };
          const build = () => {
            const css = el('style'); css.textContent = CSS; document.documentElement.appendChild(css);
            root = el('div'); root.id = 'mbAuc';
            root.innerHTML = `
        <div class="top"><input class="kw" placeholder="아이템 이름 (비워 두면 전체)"><button class="go main" title="5초에 한 번 찾을 수 있어요">검색</button>
          <div class="quota" title="사이트가 정한 하루 검색 횟수예요"><div class="qt"><span>오늘 남은 검색</span><span><b class="qn">—</b> / <span class="ql">100</span></span></div><i><s></s></i></div>
          <button class="orig" title="사이트 화면으로 돌아가요. 구매는 여기서 해요">원래 화면</button></div>
        <div class="pre"><span class="pl">프리셋</span><div class="chips"></div></div>
        <div class="body">
          <div class="fil">
            <div class="sec"><div class="grid">
              <label>분류</label><div class="two"><select class="c1"></select><select class="c2"></select></div>
              <label>가격</label><div class="two"><input class="pmin" placeholder="최소" title="예: 10억, 3억 5000만"><span>~</span><input class="pmax" placeholder="최대"></div>
              <label>스타포스</label><div class="two"><input class="smin" placeholder="최소"><span>~</span><input class="smax" placeholder="최대"></div>
            </div></div>
            <div class="sec opt" data-k="p"><div class="h">잠재 옵션<span class="r"><select class="grade"></select>
              <label title="같은 옵션이 여러 줄이면 더해서 찾아요"><input type="checkbox" class="sum">합산</label></span></div></div>
            <div class="sec opt" data-k="a"><div class="h">에디셔널 옵션<span class="r"><select class="grade"></select>
              <label title="같은 옵션이 여러 줄이면 더해서 찾아요"><input type="checkbox" class="sum">합산</label></span></div></div>
            <details class="sec"><summary>더 보기</summary><div class="grid">
              <label>레벨</label><div class="two"><input class="lmin" placeholder="최소"><span>~</span><input class="lmax" placeholder="최대"></div>
              <label>직업</label><select class="job"></select>
              <label></label><label class="chk"><input type="checkbox" class="world">내 월드만</label>
            </div></details>
            <div class="note">추가옵션·주문서 같은 다른 조건은 '원래 화면'에서 바꾸면 같이 적용돼요.</div>
          </div>
          <div class="res">
            <div class="rt"><div class="tabs"><button class="tab on" data-v="search">검색 결과</button><button class="tab" data-v="saved">저장한 결과<span class="sn"></span></button></div>
              <div class="srcw"><span class="src" hidden></span></div>
              <select class="sort" title="정렬"></select></div>
            <div class="bar"><div class="info"><button class="tolist" hidden>← 목록</button><span class="cnt"></span><span class="msg"></span></div>
              <div class="acts"><div class="pager" hidden><button class="prev">이전</button><span class="pn"></span><button class="next">다음</button></div>
                <button class="redo" title="같은 조건으로 다시 찾아요">새로고침</button><button class="keep" title="지금 결과를 이 컴퓨터에 저장해요">결과 저장</button></div></div>
            <div class="list"></div>
          </div>
        </div>`;
            // 분류
            const c1 = $('.c1');
            TREE.forEach(([id, label]) => c1.add(new Option(label, id)));
            c1.onchange = () => fillSub(c1.value);
            // 옵션 줄
            root.querySelectorAll('.opt').forEach(box => { opts(box.querySelector('.grade'), GRADES); setRows(box, ROWS); });
            opts($('.job'), JOBS);
            opts($('.sort'), SORTS.map(s => [s, s]));
            // 버튼
            $('.go').onclick = () => search();
            $('.kw').addEventListener('keydown', e => { if (e.key === 'Enter') search(); });
            $('.orig').onclick = () => setOff(true);
            $('.sort').onchange = e => changeSort(e.target.value);
            $('.prev').onclick = () => shown && goPage(shown.page - 1);
            $('.next').onclick = () => shown && goPage(shown.page + 1);
            $('.redo').onclick = refresh;
            $('.keep').onclick = keep;
            $('.tolist').onclick = () => tab('saved');
            root.querySelectorAll('.tab').forEach(b => { b.onclick = () => tab(b.dataset.v); });
            // 칸을 고치면 빨간 표시와 프리셋 표시를 지운다
            const edited = e => { if (e.target.classList) e.target.classList.remove('bad'); if (onChip != null) { onChip = null; drawChips(); } };
            $('.fil').addEventListener('input', edited); $('.fil').addEventListener('change', edited); $('.kw').addEventListener('input', edited);
            document.documentElement.appendChild(root);

            back = el('button', null, '새 화면'); back.id = 'mbAucBack';
            back.title = '우리 경매장 화면으로 돌아가요';
            back.onclick = () => setOff(false);
            document.documentElement.appendChild(back);
            drawChips(); drawQuota(); drawTabs(); tab(view);
            if (saves == null) post({ op: 'list' });
          };
          const fillSub = (top, want) => {
            const c2 = $('.c2'); c2.textContent = '';
            const t = TREE.find(x => x[0] === top); if (!t) return;
            const add = (parent, label, path) => parent.appendChild(new Option(label, JSON.stringify(path)));
            add(c2, '전체', [top, top + '::all']);
            for (const ch of t[2]) {
              if (typeof ch === 'string') { const [id, label] = pairOf(ch); add(c2, label, [top, top + '::' + id]); continue; }
              const [gid, glabel, leaves] = ch, g = el('optgroup'); g.label = glabel;
              const gp = top + '::' + gid;
              add(g, glabel + ' 전체', [top, gp, gp + '::all']);
              leaves.forEach(l => { const [id, label] = pairOf(l); add(g, label, [top, gp, gp + '::' + id]); });
              c2.appendChild(g);
            }
            if (want) {
              const v = JSON.stringify(want);
              if (![...c2.options].some(o => o.value === v)) add(c2, '지금 값', want);
              c2.value = v;
            }
          };

          // ── 조건 값 v ↔ 우리 칸 ↔ 사이트 양식 ──
          // 사이트 양식 값 → v
          const formToV = g => {
            const f = (g && g.filters) || {}, s = x => x == null ? '' : String(x);
            const rows = x => (Array.isArray(x) ? x : []).filter(r => r && r.value && r.value !== 'none').map(r => ({ value: r.value, min: s(r.min) }));
            return {
              keyword: (g && g.keyword) || '', cat: Array.isArray(f[K.cat]) && f[K.cat].length ? f[K.cat] : null,
              pmin: s(f[K.pmin]), pmax: s(f[K.pmax]), smin: s(f[K.smin]), smax: s(f[K.smax]), lmin: s(f[K.lmin]), lmax: s(f[K.lmax]),
              job: f[K.job] || 'all', world: f[K.world] === true,
              pg: f[K.pg] || 'all', ps: f[K.psum] !== false, pr: rows(f[K.prows]),
              ag: f[K.ag] || 'all', as: f[K.asum] !== false, ar: rows(f[K.arows])
            };
          };
          // 사이트 프리셋 → v
          const presetToV = p => {
            const f = p.filter || {}, c = f.itemCategory || {}, b = f.basicOption || {}, e = f.enhancementOption || {}, pr = f.price || {};
            const s = x => x == null ? '' : String(x);
            const mh = o => Object.entries(o || {}).flatMap(([k, x]) => x === true ? [{ value: k, min: '' }] : typeof x === 'number' ? [{ value: k, min: String(x) }] : []);
            const rows = (list, sum) => {
              const r = mh(sum);
              if (r.length) return [r, true];
              const l = (Array.isArray(list) ? list : []).flatMap(x => x && typeof x.value === 'string' ? [{ value: x.value, min: s(x.min) }] : mh(x));
              return l.length ? [l, false] : [[], true];
            };
            const g = x => x == null ? 'all' : typeof x === 'number' ? (GNUM[x] || 'all') : (GRADES.some(t => t[0] === String(x).toLowerCase()) ? String(x).toLowerCase() : 'all');
            const [prs, pss] = rows(e.potentialOptions, e.potentialOptionSum), [ars, ass] = rows(e.additionalPotentialOptions, e.additionalPotentialOptionSum);
            const form = findForm(), d = form && defaults(form), dc = d && Array.isArray(d[K.cat]) && TREE.some(t => t[0] === d[K.cat][0]) ? d[K.cat] : null;
            return {
              keyword: s(f.keyword != null ? f.keyword : p.keyword),
              cat: catOf(c.itemDetailCategory) || clone(dc) || ['armor', 'armor::all'],
              pmin: s(pr.min), pmax: s(pr.max), smin: s(e.starforceMin), smax: s(e.starforceMax), lmin: s(b.levelMin), lmax: s(b.levelMax),
              job: JOBNUM[c.itemJobCategory] || 'all', world: f.myWorldOnly === true,
              pg: g(e.potentialGrade), ps: pss, pr: prs, ag: g(e.additionalPotentialGrade), as: ass, ar: ars
            };
          };
          // 프리셋에 우리 칸으로 못 넣는 조건이 들어 있는지
          const SKIP = /^(keyword|myWorldOnly|itemCategory\..*|price\..*|basicOption\.level(Min|Max)|enhancementOption\.(starforceMin|starforceMax|potentialGrade|additionalPotentialGrade|potentialOptions|additionalPotentialOptions|potentialOptionSum|additionalPotentialOptionSum)(\..*)?)$/;
          const extra = p => {
            let n = 0;
            const walk = (o, pre) => {
              for (const [k, x] of Object.entries(o || {})) {
                const key = pre + k;
                if (SKIP.test(key)) continue;
                if (x && typeof x === 'object' && !Array.isArray(x)) walk(x, key + '.');
                else if (x != null && x !== '' && x !== false && !(Array.isArray(x) && !x.length)) n++;
              }
            };
            walk(p.filter, '');
            return n > 0;
          };
          // v → 우리 칸
          const fill = v => {
            if (!root || !v) return;
            root.querySelectorAll('.bad').forEach(i => i.classList.remove('bad'));
            $('.kw').value = v.keyword || '';
            const cat = Array.isArray(v.cat) && v.cat.length ? v.cat : ['armor', 'armor::all'];
            const top = TREE.some(t => t[0] === cat[0]) ? cat[0] : 'armor';
            $('.c1').value = top; fillSub(top, top === cat[0] ? cat : null);
            $('.pmin').value = wonField(v.pmin); $('.pmax').value = wonField(v.pmax);
            $('.smin').value = v.smin || ''; $('.smax').value = v.smax || '';
            $('.lmin').value = v.lmin || ''; $('.lmax').value = v.lmax || '';
            $('.job').value = v.job || 'all'; if (!$('.job').value) $('.job').value = 'all';
            $('.world').checked = !!v.world;
            for (const k of ['p', 'a']) {
              const box = $(`.opt[data-k="${k}"]`), rows = (v[k + 'r'] || []).filter(r => r && r.value && r.value !== 'none');
              box.querySelector('.grade').value = v[k + 'g'] || 'all';
              box.querySelector('.sum').checked = v[k + 's'] !== false;
              setRows(box, Math.max(ROWS, rows.length));
              box.querySelectorAll('.orow').forEach((r, i) => {
                const s = r.querySelector('select'), n = r.querySelector('input'), d = rows[i];
                if (d && ![...s.options].some(o => o.value === d.value)) s.add(new Option(LABEL[d.value] || d.value, d.value));
                s.value = d ? d.value : 'none'; n.value = d ? (d.min || '') : '';
              });
            }
          };
          const pull = () => {
            const form = findForm(); if (!form || !root) return false;
            let g; try { g = form.getValues(); } catch (_) { return false; }
            fill(formToV(g));
            return true;
          };
          // 우리 칸 → v. 잘못 쓴 칸이 있으면 null
          const read = () => {
            let ok = true;
            const chk = (cls, fn) => { const i = $(cls), v = fn(i.value); if (v == null) { i.classList.add('bad'); ok = false; } return v; };
            const v = {
              keyword: $('.kw').value.trim(),
              cat: JSON.parse($('.c2').value || '["armor","armor::all"]'),
              pmin: chk('.pmin', meso), pmax: chk('.pmax', meso),
              smin: chk('.smin', int), smax: chk('.smax', int),
              lmin: chk('.lmin', int), lmax: chk('.lmax', int),
              job: $('.job').value, world: $('.world').checked
            };
            for (const k of ['p', 'a']) {
              const box = $(`.opt[data-k="${k}"]`);
              v[k + 'g'] = box.querySelector('.grade').value;
              v[k + 's'] = box.querySelector('.sum').checked;
              const rows = [];
              box.querySelectorAll('.orow').forEach(r => {
                const s = r.querySelector('select').value, n = r.querySelector('input'), m = int(n.value);
                if (m == null) { n.classList.add('bad'); ok = false; return; }
                if (s !== 'none') rows.push({ value: s, min: m });
              });
              v[k + 'r'] = rows;
            }
            const pair = (a, b, ca, cb) => { if (v[a] !== '' && v[b] !== '' && Number(v[a]) > Number(v[b])) { $(ca).classList.add('bad'); $(cb).classList.add('bad'); ok = false; } };
            pair('pmin', 'pmax', '.pmin', '.pmax'); pair('smin', 'smax', '.smin', '.smax'); pair('lmin', 'lmax', '.lmin', '.lmax');
            return ok ? v : null;
          };
          const blank = rows => rows.length ? rows : [{ value: 'none', min: '' }];
          const push = (form, v) => {
            const set = (k, x) => form.setValue(k, x, { shouldDirty: true });
            set('keyword', v.keyword);
            set(F + K.cat, v.cat);
            // 가격은 사이트가 양식 값을 그대로 보내므로 숫자로 넣는다 (사이트 프리셋도 숫자로 들어온다)
            set(F + K.pmin, v.pmin === '' ? '' : Number(v.pmin)); set(F + K.pmax, v.pmax === '' ? '' : Number(v.pmax));
            set(F + K.smin, v.smin); set(F + K.smax, v.smax);
            set(F + K.lmin, v.lmin); set(F + K.lmax, v.lmax);
            set(F + K.job, v.job); set(F + K.world, v.world);
            set(F + K.pg, v.pg); set(F + K.psum, v.ps); set(F + K.prows, blank(v.pr));
            set(F + K.ag, v.ag); set(F + K.asum, v.as); set(F + K.arows, blank(v.ar));
          };
          // 우리 칸에 없는 사이트 조건(추가옵션 등)은 처음 값으로 돌린다. 프리셋을 넣을 때만 쓴다
          const resetOthers = form => {
            const d = defaults(form); if (!d) return;
            for (const [k, x] of Object.entries(d)) if (!OURS.has(k)) { try { form.setValue(F + k, clone(x), { shouldDirty: true }); } catch (_) {} }
          };
          // 같은 검색인지 가르는 열쇠: 우리 조건 + 사이트에서만 고르는 나머지 조건
          const baseOf = (form, v) => {
            let rest = '';
            try { const f = form.getValues().filters || {}, o = {}; Object.keys(f).sort().forEach(k => { if (!OURS.has(k)) o[k] = f[k]; }); rest = JSON.stringify(o); } catch (_) {}
            return JSON.stringify(v) + '|' + rest;
          };

          // ── 검색 (5초 간격, 10분 기억) ──
          let shown = null;          // 지금 보여 주는 결과 {v, base, sort, page, totalPages, total, items, at}
          let want = null;           // 사이트에 보낸 요청 (결과가 오면 이것과 짝짓는다)
          let siteKey = null, siteSortL = null, siteP = 1; // 사이트 화면이 지금 보여 주는 검색
          let coolUntil = 0, waiting = 0, tipOnce = '', searchMsg = '', shownCached = false;
          const cache = new Map();
          const ck = (base, sort, page) => base + '|' + sort + '|' + page;
          const fresh = (base, sort, page) => {
            const k = ck(base, sort, page), e = cache.get(k);
            if (!e) return null;
            if (Date.now() - e.at > KEEP) { cache.delete(k); return null; }
            return e;
          };
          const remember = e => {
            const now = Date.now();
            for (const [k, x] of cache) if (now - x.at > KEEP) cache.delete(k);
            cache.set(ck(e.base, e.sort, e.page), e);
          };
          const forget = base => { for (const k of [...cache.keys()]) if (k.startsWith(base + '|')) cache.delete(k); };
          // 남은 초 표시는 AuctionScript가 사이트 필터 검색 버튼에만 붙이므로, 0.1초마다 모든 버튼 글자를 읽지 않고 표시로 바로 찾는다
          const siteWait = () => { const b = document.querySelector('button[data-mb-wait]'), w = b && b.getAttribute('data-mb-wait'); return w ? (parseInt(w, 10) || 1) * 1000 : 0; };
          const coolLeft = () => Math.max(coolUntil - Date.now(), siteWait(), 0);
          const sortBtn = () => [...document.querySelectorAll('button,[role="combobox"]')].find(n => !own(n) && visible(n) && SORTS.includes(n.textContent.trim()));
          const siteSortNow = () => { const b = sortBtn(); return b ? b.textContent.trim() : null; };
          const say = t => { searchMsg = t || ''; if (root && view === 'search') $('.msg').textContent = searchMsg; };
          const expect = () => {
            const my = ++waiting;
            say('찾는 중…');
            setTimeout(() => { if (waiting === my) say("결과가 아직 안 왔어요. '원래 화면'에서 확인해 주세요"); }, 8000);
          };
          // o: {v, force(새로고침), real(기억한 결과 말고 사이트에서), then(결과가 오면 할 일)}
          function search(o = {}) {
            const form = findForm(), b = siteBtn('필터검색');
            if (!form || !b) { tab('search'); say("사이트 검색 칸을 못 찾았어요. '원래 화면'에서 검색해 주세요"); return; }
            const v = o.v || read();
            if (!v) { tab('search'); say('빨간 칸을 확인해 주세요'); return; }
            const base = baseOf(form, v), sort = siteSortNow() || $('.sort').value;
            if (o.force) forget(base);
            else if (!o.real) { const c = fresh(base, sort, 1); if (c) { showEntry(c, true, true); return; } }
            const l = coolLeft();
            if (l > 0) { tab('search'); say(`${secs(l)}초 뒤에 다시 눌러 주세요`); return; } // 쿨타임 중에는 미뤄 두지 않고 막기만 한다
            try { push(form, v); } catch (_) { tab('search'); say("조건을 넣지 못했어요. '원래 화면'에서 검색해 주세요"); return; }
            want = { v, base, sort, page: 1, at: Date.now(), then: o.then || null };
            coolUntil = Date.now() + GAP;
            tab('search'); expect();
            setTimeout(() => b.click(), 30);
          }
          function refresh() {
            const s = shown;
            if (s && s.v) fill(s.v);
            const v = read(); if (!v) { say('빨간 칸을 확인해 주세요'); return; }
            const page = s ? s.page : 1, sort = s ? s.sort : null;
            tipOnce = '';
            // 보던 정렬·쪽까지 그대로 다시 가져온다
            const after = () => {
              if (sort && shown && shown.sort !== sort) changeSort(sort, page > 1 ? () => goPage(page) : null);
              else if (page > 1) goPage(page);
            };
            search({ v, force: true, then: after });
          }
          function sitePage(n) {
            const btn = [...document.querySelectorAll('button')].find(b => !own(b) && b.textContent.trim() === String(n) && visible(b) &&
              [...b.parentElement.querySelectorAll('button')].filter(x => /^\d+$/.test(x.textContent.trim())).length >= 1);
            if (btn) { btn.click(); return true; }
            if (Math.abs(n - siteP) !== 1) return false;
            const arrow = [...document.querySelectorAll('button')].find(b => !own(b) && visible(b) &&
              new RegExp(n > siteP ? '다음' : '이전').test(b.getAttribute('aria-label') || b.title || ''));
            if (arrow) { arrow.click(); return true; }
            return false;
          }
          function goPage(n) {
            const s = shown;
            if (!s || n < 1 || n > s.totalPages || n === s.page) return;
            if (s.base !== SITE) { const c = fresh(s.base, s.sort, n); if (c) { showEntry(c, true, true); return; } }
            if (siteKey === s.base) {
              // 사이트 정렬이 다르면 먼저 맞추고 넘긴다
              if (siteSortL !== s.sort) { changeSort(s.sort, () => goPage(n)); return; }
              want = { v: s.v, base: s.base, sort: s.sort, page: n, at: Date.now(), then: null };
              if (sitePage(n)) { expect(); return; }
              want = null; say("다른 쪽은 '원래 화면'에서 넘겨 주세요"); return;
            }
            // 사이트가 다른 검색을 보여 주고 있으면 같은 조건으로 다시 찾은 뒤 넘긴다
            if (!s.v) { say("다른 쪽은 '원래 화면'에서 넘겨 주세요"); return; }
            fill(s.v);
            search({ v: s.v, real: true, then: () => goPage(n) });
          }
          async function siteSort(label) {
            const cur = sortBtn();
            if (!cur) return false;
            if (cur.textContent.trim() === label) return 'same';
            cur.click(); await wait(150);
            if (clickText(label)) return true;
            document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
            return false;
          }
          // then: 정렬을 사이트에 맞춘 뒤 할 일 (쪽 넘기기·새로고침이 쓴다)
          async function changeSort(label, then) {
            const s = shown;
            const no = () => { if (s && SORTS.includes(s.sort)) $('.sort').value = s.sort; say("정렬은 '원래 화면'에서 바꿔 주세요"); };
            if (!s) { if (await siteSort(label) === false) no(); return; }
            if (label === s.sort && !then) return;
            if (!then && s.base !== SITE) { const c = fresh(s.base, label, 1); if (c) { showEntry(c, true, true); return; } }
            if (siteKey === s.base) {
              if (then && siteSortL === label) { then(); return; }
              want = { v: s.v, base: s.base, sort: label, page: 1, at: Date.now(), then: then || null };
              const r = await siteSort(label);
              if (r === true) { expect(); return; }
              want = null;
              if (r === 'same' && s.v) { fill(s.v); search({ v: s.v, real: true, then }); return; }
              no(); return;
            }
            if (!s.v) { no(); return; }
            fill(s.v);
            search({ v: s.v, real: true, then: () => changeSort(label, then) });
          }
          // 사이트에서 결과가 왔다
          function onResult(j) {
            waiting++;
            const items = Array.isArray(j.items) ? j.items : [];
            const page = Number(j.page) || 1, totalPages = Math.max(1, Number(j.totalPages) || 1), total = Number(j.total) || items.length;
            const w = want && Date.now() - want.at < 20000 ? want : null;
            want = null; siteP = page;
            let v = w ? w.v : null;
            if (!v) { const form = findForm(); try { if (form) v = formToV(form.getValues()); } catch (_) {} }
            const e = { v, base: w ? w.base : SITE, sort: siteSortNow() || (w && w.sort) || SORTS[0], page, totalPages, total, items, at: Date.now() };
            siteKey = e.base; siteSortL = e.sort;
            if (e.base !== SITE) remember(e);
            showEntry(e, false, !!w);
            if (w && w.then) setTimeout(w.then, 400);
          }
          function showEntry(e, cached, jump) {
            shown = e; shownCached = cached; // 기억해 둔 결과면 위 줄에 몇 분 전인지 보여 준다
            say(tipOnce); tipOnce = '';
            if (!root) return;
            if (jump && view !== 'search') tab('search'); else if (view === 'search') drawSearch();
            drawSrc();
          }
          // 결과가 어디서 온 것인지 작게 보여 준다 (새로 찾은 결과면 비워 둔다)
          function drawSrc() {
            if (!root) return;
            let t = '';
            if (view === 'one' && one) t = '저장한 결과 · ' + String(one.name || '검색 결과');
            else if (view === 'search' && shown && shownCached) t = ago(shown.at);
            const b = $('.src');
            if (b.textContent !== t) { b.textContent = t; b.title = t; }
            b.hidden = !t;
          }
          // 남은 시간 표시만 자주 고친다 (0.1초마다)
          const showCool = () => {
            if (!root) return;
            const l = coolLeft(), go = $('.go');
            if (l > 0) {
              go.textContent = secs(l) + '초'; go.classList.add('wait');
            } else {
              go.textContent = '검색'; go.classList.remove('wait');
            }
          };

          // ── 프리셋 (사이트에 저장한 검색 프리셋을 우리 칸에 바로 넣는다) ──
          let presets = null, onChip = null;
          function drawChips() {
            if (!root) return;
            const box = $('.chips'); box.textContent = '';
            if (presets && presets.length) presets.forEach(p => {
              const id = p.id || p.name, b = el('button', 'chip' + (onChip != null && onChip === id ? ' on' : ''), p.name || '이름 없음');
              b.title = p.name || ''; b.onclick = () => applyPreset(p);
              box.appendChild(b);
            });
            else box.appendChild(el('span', 'none', presets ? '저장한 프리셋이 없어요' : '사이트에 저장한 프리셋이 여기 나와요'));
            const add = el('button', 'chip add', '＋ 프리셋 추가');
            add.title = '지금 조건을 사이트 프리셋으로 저장해요';
            add.onclick = saveFilter;
            box.appendChild(add);
          }
          function applyPreset(p) {
            const form = findForm();
            if (!form) { tab('search'); say("사이트 검색 칸을 못 찾았어요. '원래 화면'에서 골라 주세요"); return; }
            const v = presetToV(p);
            resetOthers(form);
            fill(v);
            onChip = p.id || p.name; drawChips();
            tipOnce = extra(p) ? '이 프리셋의 일부 조건은 여기서 못 넣었어요' : '';
            search();
          }
          let pendingPull = null;
          function saveFilter() {
            const form = findForm(), b = siteBtn('필터저장');
            if (!form || !b) { say("프리셋 저장은 '원래 화면'에서 해 주세요"); return; }
            const v = read(); if (!v) { tab('search'); say('빨간 칸을 확인해 주세요'); return; }
            try { push(form, v); } catch (_) {}
            pendingPull = '';
            setTimeout(() => b.click(), 30);
          }
          let left = null;
          function drawQuota() {
            if (!root) return;
            const q = $('.quota');
            if (!left) { $('.qn').textContent = '—'; return; }
            const r = Number(left.remaining), l = Number(left.limit) || 100;
            $('.qn').textContent = isFinite(r) ? comma(r) : '—';
            $('.ql').textContent = comma(l);
            $('.quota s').style.width = (isFinite(r) ? Math.max(0, Math.min(100, r / l * 100)) : 0) + '%';
            q.classList.toggle('low', isFinite(r) && r <= 10);
          }

          // ── 결과 그리기 ──
          let view = 'search';       // search | saved | one
          const empty = (list, text) => {
            list.textContent = '';
            const d = el('div', 'empty');
            text.split('\n').forEach((t, i) => { if (i) d.appendChild(el('br')); d.appendChild(document.createTextNode(t)); });
            list.appendChild(d);
          };
          const potLine = (tag, p) => {
            if (!p || !Array.isArray(p.entries) || !p.entries.length) return null;
            const d = el('div', 'pl'), b = el('b', null, tag);
            b.style.background = GCOLOR[p.grade] || '#999';
            d.appendChild(b);
            d.appendChild(document.createTextNode(p.entries.map(e => String(e.text || '').replace(/\s+/g, ' ').trim()).join(' · ')));
            return d;
          };
          function drawItems(list, items, none) {
            list.textContent = ''; list.scrollTop = 0;
            if (!items.length) { empty(list, none); return; }
            items.forEach(it => {
              if (!it || typeof it !== 'object') return;
              const t = it.toolTip || {}, u = t.upgradeInfo || {};
              const row = el('div', 'row'), img = el('img');
              const icon = (it.itemIcon && it.itemIcon.fallBackUrl) || (t.itemIcon && t.itemIcon.fallBackUrl) || '';
              if (/^https:\/\//.test(icon)) img.src = icon;
              img.alt = '';
              const mid = el('div', 'mid'), nm = el('div', 'nm', it.itemName || t.itemName || '');
              const sf = Number(it.starforce || t.starforce || 0);
              if (sf) nm.appendChild(el('span', 'sf', '★' + sf));
              mid.appendChild(nm);
              [potLine('잠재', u.potential), potLine('에디', u.additionalPotential)].forEach(x => x && mid.appendChild(x));
              const pr = el('div', 'pr'), q = Number(it.quantity) || 1;
              pr.appendChild(el('div', 'p', won(it.price) + ' 메소'));
              if (q > 1) pr.appendChild(el('div', 's', `${comma(q)}개 · 개당 ${won(it.pricePerItem)}`));
              if (Number(it.attackPowerDiff) > 0) pr.appendChild(el('div', 's', '전투력 +' + won(it.attackPowerDiff)));
              const wear = el('button', 'see', '착용 해보기');
              wear.title = '이 아이템을 환산에 껴 보고 효율·보스컷을 열어요';
              wear.onclick = () => {
                const ok = window.__mbWear && window.__mbWear(it, icon);
                wear.textContent = ok ? '넣었어요' : '못 넣었어요';
                setTimeout(() => { wear.textContent = '착용 해보기'; }, 1500);
              };
              pr.appendChild(wear);
              const ic = el('div', 'ic'); ic.appendChild(img);
              if (it.isMyWorld === false) { const w = el('span', 'w', 'W'); w.title = '다른 월드'; ic.appendChild(w); }
              row.append(ic, mid, pr);
              list.appendChild(row);
            });
          }
          function tab(v) {
            view = v;
            if (!root) return;
            root.querySelectorAll('.tab').forEach(b => b.classList.toggle('on', b.dataset.v === (v === 'one' ? 'saved' : v)));
            const s = v === 'search';
            $('.sort').hidden = !s; $('.redo').hidden = !s; $('.keep').hidden = !s; $('.tolist').hidden = v !== 'one';
            $('.pager').hidden = true;
            if (s) drawSearch(); else if (v === 'saved') drawSaved(); else drawOne();
            drawSrc();
          }
          function drawSearch() {
            $('.msg').textContent = searchMsg;
            const list = $('.list'), e = shown;
            if (!e) {
              $('.cnt').textContent = '검색 결과';
              $('.redo').disabled = true; $('.keep').disabled = true;
              empty(list, '조건을 고르고 검색을 눌러 주세요.\n같은 조건은 10분 동안 다시 찾지 않고 바로 보여 줘요.');
              return;
            }
            $('.cnt').textContent = `검색 결과 ${comma(e.total)}개`;
            $('.redo').disabled = false; $('.keep').disabled = !e.items.length || keeping;
            $('.pager').hidden = e.totalPages <= 1;
            $('.pn').textContent = `${e.page} / ${e.totalPages}`;
            $('.prev').disabled = e.page <= 1; $('.next').disabled = e.page >= e.totalPages;
            if (SORTS.includes(e.sort)) $('.sort').value = e.sort;
            drawItems(list, e.items, '조건에 맞는 아이템이 없어요.');
          }

          // ── 저장한 결과 (이 컴퓨터에 보관. 앱이 파일로 저장한다) ──
          let saves = null, one = null, keeping = false, savedMsg = '';
          const post = o => { try { window.chrome.webview.postMessage('mb:auc:' + JSON.stringify(o)); return true; } catch (_) { return false; } };
          window.__mbAucSaves = r => {
            if (!r || typeof r !== 'object') return;
            if (Array.isArray(r.list)) saves = r.list.filter(x => x && typeof x === 'object');
            if (r.op === 'saved') { keeping = false; say(`'${r.name}' 저장했어요`); if (root && view === 'search') drawSearch(); }
            else if (r.op === 'load' && r.data && typeof r.data === 'object') { one = r; savedMsg = ''; tab('one'); }
            else if (r.op === 'err') {
              if (keeping) { keeping = false; say('저장하지 못했어요'); if (root && view === 'search') drawSearch(); }
              else { savedMsg = '열지 못했어요. 지우고 다시 저장해 주세요'; if (root && view === 'saved') drawSaved(); }
            }
            else if (r.op === 'list') { savedMsg = ''; if (root && view === 'saved') drawSaved(); }
            drawTabs();
          };
          function drawTabs() { if (root) $('.sn').textContent = saves && saves.length ? String(saves.length) : ''; }
          const nameOf = s => {
            const v = s.v || {}, parts = [v.keyword || catLabel(v.cat) || '검색 결과'];
            const g = GRADES.find(x => x[0] === v.pg); if (g && v.pg !== 'all') parts.push(g[1]);
            const r = (v.pr || []).find(x => x && x.value && x.value !== 'none'); if (r) parts.push((LABEL[r.value] || r.value) + (r.min ? ' ' + r.min : ''));
            if (v.smin) parts.push('★' + v.smin);
            if (s.page > 1) parts.push(s.page + '쪽');
            return parts.join(' · ');
          };
          function keep() {
            const s = shown;
            if (!s || !s.items.length || keeping) return;
            keeping = true;
            const ok = post({ op: 'save', name: nameOf(s), data: { items: s.items, total: s.total, page: s.page, totalPages: s.totalPages, sort: s.sort, v: s.v } });
            if (!ok) { keeping = false; say('이 창에서는 저장할 수 없어요'); return; }
            say('저장하는 중…'); $('.keep').disabled = true;
          }
          function drawSaved() {
            const list = $('.list'), st = list.scrollTop;
            $('.cnt').textContent = '저장한 결과';
            $('.msg').textContent = savedMsg || '이 컴퓨터에만 보관해요';
            if (saves == null) { empty(list, post({ op: 'list' }) ? '불러오는 중…' : '이 창에서는 저장한 결과를 볼 수 없어요.'); return; }
            if (!saves.length) { empty(list, "저장한 결과가 없어요.\n검색 결과 위의 '결과 저장'을 누르면 여기에 모여요."); return; }
            list.textContent = '';
            saves.forEach(x => {
              const row = el('div', 'srow'), mid = el('div', 'mid');
              mid.append(el('div', 'nm', String(x.name || '검색 결과')), el('div', 's', `아이템 ${comma(Number(x.count) || 0)}개 · ${when(x.at)}`));
              const see = el('button', null, '보기');
              see.onclick = () => { savedMsg = '여는 중…'; $('.msg').textContent = savedMsg; post({ op: 'load', id: x.id }); };
              const del = el('button', 'del', '지우기');
              del.onclick = () => {
                if (del.classList.contains('sure')) { del.disabled = true; post({ op: 'del', id: x.id }); return; }
                del.classList.add('sure'); del.textContent = '정말 지울까요?';
                setTimeout(() => { del.classList.remove('sure'); del.textContent = '지우기'; }, 3000);
              };
              row.append(mid, see, del);
              list.appendChild(row);
            });
            list.scrollTop = st;
          }
          function drawOne() {
            const o = one;
            if (!o) { tab('saved'); return; }
            const items = Array.isArray(o.data.items) ? o.data.items : [];
            $('.cnt').textContent = String(o.name || '검색 결과');
            $('.msg').textContent = `${when(o.at)} 저장 · 아이템 ${comma(items.length)}개`;
            drawItems($('.list'), items, '아이템이 없어요.');
          }

          // ── 사이트 자료 받기 (PickScript가 알려 준다) ──
          addEventListener('mb:api', e => {
            const d = e.detail || {}; let j;
            try { j = JSON.parse(String(d.text || '').replace(/^﻿/, '')); } catch (_) { return; }
            const path = (() => { try { return new URL(d.url, location.href).pathname; } catch (_) { return ''; } })();
            if (/\/daily-limit$/.test(path)) { if (j && j.search) { left = j.search; drawQuota(); } }
            else if (/\/filter-preset$/.test(path) && d.method === 'GET') { if (Array.isArray(j)) { presets = j; drawChips(); } }
            else if (/\/items\/searches/.test(path) && !/sold/i.test(path)) { if (j && Array.isArray(j.items)) onResult(j); }
          });

          // ── 켜고 끄기 ──
          let off = store.get('mb-auc-off') === '1', aside = false;
          function setOff(v) {
            off = v; store.set('mb-auc-off', v ? '1' : null);
            if (!v) pull();
            sync();
          }
          function sync() {
            if (!root) return;
            const page = onSearchPage(), dlg = siteDialog();
            if (aside && !dlg) {
              aside = false;
              if (pendingPull != null) { pull(); if (pendingPull) say(pendingPull); pendingPull = null; }
            }
            if (dlg) aside = true;
            root.style.display = page && !off && !dlg ? 'flex' : 'none';
            const pk = document.getElementById('mbPick'); // 원래 화면용 담기 칸은 새 화면 위에서는 숨긴다
            if (pk && root.style.display === 'flex') pk.style.display = 'none';
            back.style.display = page && off ? 'block' : 'none';
          }
          let pulled = false;
          const loop = () => {
            if (!document.body) return;
            const onPage = onSearchPage();
            if (!root) { if (!onPage) return; build(); }
            if (!pulled && onPage && pull()) pulled = true;
            sync();
          };
          setInterval(loop, 300);
          setInterval(() => { showCool(); drawSrc(); }, 100);
        })();
        """;

    /// <summary>환산(maplescouter) 보관함(아이템메이커가 쓰는 곳)에 아이템 하나를 더하는 스크립트. 창 하나에서 한 번만 돈다</summary>
    public static string AddBookmarkScript(string itemJson, string id) => $$"""
        (() => { try {
          if (window !== top || !location.hostname.endsWith('maplescouter.com')) return;
          if (sessionStorage.getItem('mb-add-{{id}}')) return;
          sessionStorage.setItem('mb-add-{{id}}', '1');
          const k = 'equipBookmarkList', it = {{itemJson}};
          // 다시 읽기 전에 멈춰 둔 창이면 막아 둔 저장 대신 원래 저장을 쓴다
          const set = window.__mbSet || ((a, b) => localStorage.setItem(a, b));
          let s = null;
          try { s = JSON.parse(localStorage.getItem(k)); } catch (_) {}
          if (!s || typeof s !== 'object') s = { state: { bookmarkList: [] }, version: 0 };
          if (!s.state || typeof s.state !== 'object') s.state = {};
          // 전에 '착용 해보기'로 만든 아이템(mb 표시)만 지우고, 그때 잠시 뺀 내 아이템은 다시 낀다. 내가 만든 아이템은 이름이 같아도 그대로 둔다
          const l = (Array.isArray(s.state.bookmarkList) ? s.state.bookmarkList : []).filter(o => o && !o.mb);
          for (const o of l) if (o.mbWas) { o.isEquipped = true; delete o.mbWas; }
          // 같은 자리에 낀 내 아이템은 잠시 빼 둔다 (착용 창을 닫으면 되돌린다)
          for (const o of l) if (o.slot === it.slot && o.isEquipped) { o.isEquipped = false; o.mbWas = 1; }
          it.isEquipped = true;
          it.mb = 1;
          l.push(it);
          s.state.bookmarkList = l;
          set(k, JSON.stringify(s));
          // 결과 화면은 캐릭터를 불러올 때 착용 표시를 모두 풀므로, 실제 착용은 WearViewScript가 화면이 뜬 뒤에 한다
          // 아이템을 통째로 넘긴다: 열려 있던 창이 다시 읽기 전에 옛 목록으로 덮어써도 그쪽에서 다시 넣을 수 있게
          set('mb-wear-it', JSON.stringify(it));
          set('mb-wear-at', String(Date.now())); // 효율·보스컷 창을 배율표만 보이게
        } catch (_) {} })();
        """;

    /// <summary>새 아이템을 넣기 직전, 곧 다시 읽을 환산 창을 멈춘다.
    /// 화면에 남은 옛 목록(이전 착용 아이템)을 사이트가 다시 저장해서 새 목록을 덮어쓰지 않게 한다 (다시 읽으면 풀린다)</summary>
    public const string FreezeListScript = """
        (() => { try {
          if (window !== top || !location.hostname.endsWith('maplescouter.com') || window.__mbFrozen) return;
          window.__mbFrozen = 1;
          const o = Storage.prototype.setItem;
          window.__mbSet = (a, b) => o.call(localStorage, a, b);
          Storage.prototype.setItem = function (a, b) { if (this === localStorage && a === 'equipBookmarkList') return; return o.call(this, a, b); };
        } catch (_) {} })();
        """;

    /// <summary>착용 창을 닫을 때 다른 환산 창에서 돌린다: '착용 해보기'로 만든 아이템(mb 표시)만 지우고 잠시 뺀 내 아이템은 다시 낀다</summary>
    public const string ClearWearScript = """
        (() => { try {
          if (window !== top || !location.hostname.endsWith('maplescouter.com')) return;
          localStorage.removeItem('mb-wear-live');
          const k = 'equipBookmarkList', s = JSON.parse(localStorage.getItem(k) || 'null');
          const l = s && s.state && Array.isArray(s.state.bookmarkList) ? s.state.bookmarkList : null;
          if (!l || !l.some(o => o && (o.mb || o.mbWas))) return;
          s.state.bookmarkList = l.filter(o => o && !o.mb).map(o => { if (o.mbWas) { o.isEquipped = true; delete o.mbWas; } return o; });
          localStorage.setItem(k, JSON.stringify(s));
        } catch (_) {} })();
        """;

    /// <summary>'착용 해보기'로 연 효율·보스컷 창: 시뮬레이터와 아이템 적용을 켜고, 보스 배율표(9×3)와 계산 결과만 보여 준다.
    /// 사람 확인 화면이 뜨면 아무것도 가리지 않는다</summary>
    public const string WearViewScript = """
        (() => {
          if (window !== top || !location.hostname.endsWith('maplescouter.com')) return;
          const onPage = () => location.pathname.replace(/\/$/, '').endsWith('/result');
          // 착용 창은 앱이 따로 표시한다(__mbWearWin). 다른 환산 창은 착용 아이템을 받지 않는다
          const mine = !!window.__mbWearWin;
          try {
            if (mine) {
              const t = localStorage.getItem('mb-wear-it');
              if (t && onPage()) { sessionStorage.setItem('mb-wear', '1'); sessionStorage.setItem('mb-wear-it', t); }
              // 착용 창이 살아 있다고 알린다 (닫히면 다른 창이 남은 착용 아이템을 지운다)
              const beat = () => { try { localStorage.setItem('mb-wear-live', String(Date.now())); } catch (_) {} };
              beat(); setInterval(beat, 2000);
            } else {
              const live = +localStorage.getItem('mb-wear-live') || 0, at = +localStorage.getItem('mb-wear-at') || 0;
              if (Date.now() - live > 8000 && Date.now() - at > 60000) {
                // 착용 창이 없으면 '착용 해보기'로 만든 아이템(mb 표시)만 지우고 잠시 뺀 내 아이템은 다시 낀다
                const k = 'equipBookmarkList', s = JSON.parse(localStorage.getItem(k) || 'null');
                const l = s && s.state && Array.isArray(s.state.bookmarkList) ? s.state.bookmarkList : null;
                if (l && l.some(o => o && (o.mb || o.mbWas))) {
                  s.state.bookmarkList = l.filter(o => o && !o.mb).map(o => { if (o.mbWas) { o.isEquipped = true; delete o.mbWas; } return o; });
                  localStorage.setItem(k, JSON.stringify(s));
                }
              }
            }
          } catch (_) {}
          const W = 680;
          let full = false, onAt = 0, retry = 0, stores = null, findTry = 0, findAt = 0, holding = false, why = '';
          // 가볍게 돌리려고 기억해 두는 것들 (0.25초마다 페이지 전체를 다시 훑지 않게)
          let itCache = null, bgColor = '', resCache = null, lastToggle = 0, needToggle = false, applies = 0, okSeen = false;
          const startAt = Date.now();
          // 사이트의 장비 목록(원래 장비, 보관함)을 찾는다. 무거운 일이라 1초에 한 번, 몇 번만 해 본다
          const getStores = () => {
            if (stores || findTry >= 8 || Date.now() - findAt < 1000) return stores;
            findTry++; findAt = Date.now();
            let req = null;
            try { self.webpackChunk_N_E.push([[Math.random()], {}, r => { req = r; }]); } catch (_) {}
            if (!req || !req.m) return null;
            const find = (name) => {
              for (const id in req.m) {
                try {
                  if (!String(req.m[id]).includes('name:"' + name + '"')) continue;
                  const ex = req(id);
                  for (const k in ex) if (ex[k] && typeof ex[k].getState === 'function') return ex[k];
                } catch (_) {}
              }
              return null;
            };
            const c = find('customEquipList'), b = find('equipBookmarkList');
            if (c && b) stores = { c, b };
            return stores;
          };
          // 넣은 아이템을 착용하고 같은 자리의 원래 장비는 뺀다 (사이트의 장착 버튼과 같은 방식). 바꾼 게 있으면 true
          const wear = () => {
            if (!itCache) { try { itCache = JSON.parse(sessionStorage.getItem('mb-wear-it') || 'null'); } catch (_) {} }
            const it = itCache;
            if (!it) { why = '넣을 아이템 정보가 없어요'; return false; }
            const s = getStores();
            if (!s) { why = '사이트 장비 목록을 못 찾았어요'; return false; }
            why = '';
            const same = (o) => o && o.name === it.name && o.character_name === it.character_name && o.itemScore === it.itemScore && o.slot === it.slot;
            let changed = false;
            const bl = s.b.getState().bookmarkList || [];
            if (!bl.some(same)) {
              // 보관함에서 빠졌으면 다시 넣는다 (아이템 정보가 통째로 있을 때만)
              if (!it.totalOption) { why = '보관함에 아이템이 없어요'; return false; }
              s.b.getState().setBookmarkList([...bl.map(o => o && o.isEquipped && o.slot === it.slot ? { ...o, isEquipped: false, ...(o.mb ? {} : { mbWas: 1 }) } : o), { ...it, isEquipped: true }]);
              changed = true;
            } else if (!bl.some(o => same(o) && o.isEquipped)) {
              s.b.getState().setBookmarkList(bl.map(o => same(o) ? { ...o, isEquipped: true } : (o && o.isEquipped && o.slot === it.slot ? { ...o, isEquipped: false, ...(o.mb ? {} : { mbWas: 1 }) } : o)));
              changed = true;
            }
            const cl = s.c.getState().customEquipList || [];
            if (cl.some(o => o && o.slot === it.slot && !o.disabled)) {
              s.c.getState().setCustomEquipList(cl.map(o => o && o.slot === it.slot && !o.disabled ? { ...o, disabled: true } : o));
              changed = true;
            }
            return changed;
          };
          const reToggle = (ti) => { ti.click(); setTimeout(() => { if (ti.getAttribute('aria-checked') !== 'true') ti.click(); }, 400); };
          const css = `
            #mbwBg{position:fixed;inset:0;z-index:2147483000;display:flex;align-items:center;justify-content:center;font:14px sans-serif;color:#888}
            [data-mbw=grid]{position:fixed!important;left:8px!important;top:8px!important;min-width:${W}px;z-index:2147483001!important;margin:0!important}
            [data-mbw=grid]>div:nth-child(n+28){display:none!important}
            [data-mbw=res]{position:fixed!important;left:8px!important;right:8px!important;width:auto!important;z-index:2147483001!important;margin:0!important;flex-direction:row!important}
            #mbwBtn{position:fixed;right:8px;bottom:8px;z-index:2147483002;pointer-events:auto!important;padding:4px 10px;border-radius:6px;border:1px solid #bbb;background:#fff;color:#333;font:12px sans-serif;cursor:pointer}
            #mbwHold{position:fixed;left:8px;right:96px;overflow:hidden;z-index:2147483002;display:flex;align-items:center;gap:10px;font:12px sans-serif;color:#888}
            #mbwHold button{flex:none;padding:6px 12px;border-radius:6px;border:1px solid #bbb;background:#fff;color:#333;font:12px sans-serif;cursor:pointer;user-select:none;touch-action:none}
            #mbwHold button[data-on]{background:#ff8a3d;border-color:#ff8a3d;color:#fff}
            html.mbw body{overflow:hidden!important}`;
          const setOn = (sw) => { if (sw && sw.getAttribute('aria-checked') !== 'true' && !sw.disabled) sw.click(); };
          const tick = () => {
            try {
              const btn = document.getElementById('mbwBtn');
              if (window.__mbFrozen) return; // 새 아이템으로 다시 여는 중: 옛 아이템을 다시 끼지 않는다
              if (sessionStorage.getItem('mb-wear') !== '1' || !onPage() || !document.body) {
                document.documentElement.classList.remove('mbw');
                document.getElementById('mbwBg')?.remove(); document.getElementById('mbwCss')?.remove(); btn?.remove();
                document.getElementById('mbwHold')?.remove(); holding = false;
                return;
              }
              // 사람 확인 화면은 절대 가리거나 누르지 않는다
              // (페이지 전체 글자를 읽으면 무거워서, 그 글자가 든 곳만 찾고 보이는지 확인한다)
              const seen = (e) => !!e && (typeof e.checkVisibility !== 'function' || e.checkVisibility({ visibilityProperty: true, checkVisibilityCSS: true }));
              const hs = document.evaluate("//body//text()[contains(., '사람 확인')][not(ancestor::script)][not(ancestor::style)]", document, null, XPathResult.ORDERED_NODE_SNAPSHOT_TYPE, null);
              let human = false;
              for (let i = 0; i < hs.snapshotLength && !human; i++) human = seen(hs.snapshotItem(i).parentElement);
              // 글자가 여러 조각으로 나뉘어 있어도 놓치지 않게, 화면 위에 떠 있는 창은 통째 글자로도 본다
              if (!human) for (const e of document.querySelectorAll('[class*="fixed"],[role=dialog],[role=alertdialog]')) if (/사람\s*확인/.test(e.textContent) && seen(e)) { human = true; break; }
              // 켜기: 추가 스펙 시뮬레이터 → 아이템 적용하기
              const h4 = [...document.querySelectorAll('h4')].find(h => h.textContent.trim() === '추가 스펙 시뮬레이터');
              let card = null;
              if (h4) {
                let n = h4.parentElement, sw = null;
                for (let i = 0; i < 4 && n && !sw; i++, n = n.parentElement) sw = [...n.querySelectorAll('button[role=switch]')].find(b => b.id !== 'toggleItem');
                card = h4.closest('[class*="rounded-2xl"]');
                // 캐릭터 정보가 다 불러와져(보스표가 뜬 뒤) 켜야 사이트가 계산한다
                const ready = !!document.querySelector('img[alt="boss"]');
                const ti = document.getElementById('toggleItem');
                const on = sw && ti && sw.getAttribute('aria-checked') === 'true' && ti.getAttribute('aria-checked') === 'true';
                // 착용은 캐릭터를 다 불러온 뒤에 한다 (불러올 때 사이트가 착용을 풀기 때문). 켜진 뒤에 바뀌면 다시 계산하게 한다
                // '원래 아이템 장착하기'를 누르고 있는 동안은 아이템 적용하기를 꺼 둔다
                // 사이트가 계속 착용을 풀면 끝없이 다시 계산하지 않게 몇 번만 다시 낀다
                const changed = !holding && !human && ready && applies < 8 && wear();
                if (changed) { applies++; if (on) needToggle = true; }
                else if (applies >= 8 && !why) why = '사이트가 계속 착용을 풀어요';
                if (!holding && !human && ready) { setOn(sw); setOn(ti); }
                if (holding) { onAt = 0; okSeen = false; }
                // 다시 계산은 1.2초에 한 번까지만 (연달아 껐다 켜면 사이트 계산이 겹쳐 버벅인다)
                else if (needToggle && on) { if (Date.now() - lastToggle > 1200) { needToggle = false; okSeen = false; lastToggle = onAt = Date.now(); reToggle(ti); } }
                else if (!on) { onAt = 0; okSeen = false; }
                else if (!onAt) onAt = Date.now();
                // 켰는데 결과가 0이면 아이템 적용하기를 껐다 켜서 다시 계산하게 한다 (몇 번만). 결과가 나온 뒤에는 더 보지 않는다
                else if (!okSeen && Date.now() - onAt > 2500 && retry < 3 && card) {
                  if (/0\.000%/.test(card.textContent)) { retry++; lastToggle = onAt = Date.now(); reToggle(ti); }
                  else okSeen = true;
                }
              }
              // 표시할 곳 찾기
              const img = document.querySelector('img[alt="boss"]');
              const grid = img && img.closest('.flex-wrap');
              if (grid && grid.dataset.mbw !== 'grid') grid.dataset.mbw = 'grid';
              // 표 너비: 보스 카드 9칸이 딱 들어가게 실제 크기로 잰다
              if (grid && grid.firstElementChild) {
                // 줄이기 전에 한 번만 잰다 (줄인 뒤에 재면 크기가 같이 줄어든다)
                if (!grid.dataset.mbwCw) { grid.style.zoom = ''; grid.dataset.mbwCw = String(grid.firstElementChild.getBoundingClientRect().width || ''); }
                const cw = +grid.dataset.mbwCw;
                if (cw > 0) {
                  const gw = Math.ceil(cw * 9 + 4 * 8), w = gw + 'px';
                  if (grid.style.width !== w) grid.style.width = w;
                  // 창이 좁으면 표를 줄여서 끝 칸이 잘리지 않게 한다
                  const z = String(Math.min(1, (window.innerWidth - 16) / gw).toFixed(3));
                  if (grid.style.zoom !== z) grid.style.zoom = z;
                }
              }
              // 결과 칸: 한 번 찾으면 그대로 쓰고, 사이트가 새로 그려 없어졌을 때만 다시 찾는다
              let res = card && resCache && resCache.isConnected && card.contains(resCache) ? resCache : null;
              if (card && !res) {
                const tw = document.createTreeWalker(card, NodeFilter.SHOW_TEXT);
                let lab = null;
                for (let t = tw.nextNode(); t; t = tw.nextNode()) if (/^\s*보스300/.test(t.nodeValue)) { lab = t.parentElement; break; }
                for (let n = lab; n && n !== card; n = n.parentElement) { const t = n.textContent; if (t.includes('보스380') && t.includes('최종뎀')) { res = n; break; } }
                if (res && res.parentElement && res.parentElement !== card && res.parentElement.textContent.includes('전투력') && !res.parentElement.contains(h4)) res = res.parentElement;
                resCache = res;
              }
              document.querySelectorAll('[data-mbw=res]').forEach(e => { if (e !== res) delete e.dataset.mbw; });
              if (res && res.dataset.mbw !== 'res') res.dataset.mbw = 'res';
              // 버튼
              let b = btn;
              if (!b) { b = document.createElement('button'); b.id = 'mbwBtn'; b.onclick = () => { full = !full; tick(); }; document.body.appendChild(b); }
              const small = !full && !human && grid;
              // 보스표가 뜨기 전에는 원래 화면 대신 '불러오는 중'으로 가린다 (오래 걸리면 그냥 보여 준다)
              const loading = !full && !human && !grid && Date.now() - startAt < 15000;
              // 같은 값을 다시 쓰지 않는다 (쓸 때마다 사이트가 화면 배치를 다시 계산한다)
              const bt = full ? '배율만 보기' : '전체 보기';
              if (b.textContent !== bt) b.textContent = bt;
              const bd = human ? 'none' : '';
              if (b.style.display !== bd) b.style.display = bd;
              if (!small && !loading) {
                document.documentElement.classList.remove('mbw');
                document.getElementById('mbwBg')?.remove(); document.getElementById('mbwCss')?.remove();
                document.getElementById('mbwHold')?.remove(); holding = false;
                return;
              }
              // 보스컷 안내 창은 닫는다
              const dlg = document.querySelector('[role=dialog]');
              if (dlg && dlg.textContent.includes('보스컷 안내')) document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
              if (!document.getElementById('mbwCss')) { const s = document.createElement('style'); s.id = 'mbwCss'; s.textContent = css; document.head.appendChild(s); }
              let bg = document.getElementById('mbwBg');
              if (!bg) {
                if (!bgColor) { bgColor = getComputedStyle(document.body).backgroundColor || ''; if (!bgColor || /rgba\(0, 0, 0, 0\)|transparent/.test(bgColor)) bgColor = '#fff'; }
                bg = document.createElement('div'); bg.id = 'mbwBg'; bg.style.background = bgColor; document.body.appendChild(bg);
              }
              const msg = loading ? '보스 배율표 불러오는 중…' : '';
              if (bg.textContent !== msg) bg.textContent = msg;
              if (!document.documentElement.classList.contains('mbw')) document.documentElement.classList.add('mbw');
              if (res && grid) { const rt = (8 + grid.getBoundingClientRect().height + 10) + 'px'; if (res.style.top !== rt) res.style.top = rt; }
              // 결과 아래: 누를 때마다 원래 아이템 ↔ 고른 아이템으로 바꿔 계산 + 주의문
              let hb = document.getElementById('mbwHold');
              if (!hb) {
                hb = document.createElement('div'); hb.id = 'mbwHold';
                const hbtn = document.createElement('button'); hbtn.type = 'button'; hbtn.textContent = '원래 아이템 장착하기';
                const note = document.createElement('span');
                note.textContent = '실제 보스 세팅 기준으로 아이템을 장착하기에 경매장 전투력과 차이가 존재할 수 있습니다.';
                hb.append(hbtn, note);
                hbtn.addEventListener('click', () => {
                  const ti = document.getElementById('toggleItem');
                  if (!holding) {
                    holding = true; hbtn.dataset.on = '1'; hbtn.textContent = '고른 아이템 다시 장착하기';
                    if (ti && ti.getAttribute('aria-checked') === 'true') ti.click();
                  } else {
                    holding = false; delete hbtn.dataset.on; hbtn.textContent = '원래 아이템 장착하기'; onAt = 0;
                    setOn(ti);
                  }
                });
                document.body.appendChild(hb);
              }
              const hd = res && grid && !loading ? '' : 'none';
              if (hb.style.display !== hd) hb.style.display = hd;
              // 여러 번 다시 해 봐도 0이면 알려 준다
              const failed = !holding && retry >= 3 && onAt && Date.now() - onAt > 2500 && card && /0\.000%/.test(card.textContent);
              const noteText = failed
                ? '아이템이 계산에 안 들어갔어요. 착용 해보기를 다시 눌러 주세요.' + (why ? ' (' + why + ')' : '')
                : '실제 보스 세팅 기준으로 아이템을 장착하기에 경매장 전투력과 차이가 존재할 수 있습니다.';
              if (hb.lastChild.textContent !== noteText) hb.lastChild.textContent = noteText;
              const nc = failed ? 'rgb(229, 72, 77)' : '';
              if (hb.lastChild.style.color !== nc) hb.lastChild.style.color = nc;
              if (res && grid) { const ht = (res.getBoundingClientRect().bottom + 12) + 'px'; if (hb.style.top !== ht) hb.style.top = ht; }
            } catch (_) {}
          };
          setInterval(tick, 250);
        })();
        """;

    // [임시] 우리 필터·결과 창을 만들려고 경매장 구조를 저장한다 (%TEMP%\MapleBarAuction, 이 컴퓨터 안에만). 다 만들면 지운다
    // - 저절로: 검색 조건 이름표가 든 사이트 프로그램 파일, 사이트가 받아 오는 검색 자료(주소마다 처음 몇 번만)
    // - F9: 지금 화면 구조
    public const string DumpScript = """
        (() => {
          if (location.hostname !== 'auction.maplestory.nexon.com') return;
          const send = (u, t) => { try { chrome.webview.postMessage('mb:dump:' + u + '\n' + t); } catch (_) {} };
          const count = k => { try { const n = Number(sessionStorage.getItem('mb-dump:' + k) || 0); sessionStorage.setItem('mb-dump:' + k, String(n + 1)); return n; } catch (_) { return 99; } };
          const scan = async () => {
            const srcs = new Set([...document.scripts].map(s => s.src));
            performance.getEntriesByType('resource').forEach(r => { if (/\.js(\?|$)/.test(r.name)) srcs.add(r.name); });
            for (const s of srcs) {
              if (!s || !s.startsWith(location.origin) || count(s) > 0) continue;
              try { const t = await (await fetch(s)).text(); if (/criticalDamagePercent|potentialGrade|itemCategory|starforceMin/.test(t)) send(s, t); } catch (_) {}
            }
          };
          addEventListener('load', () => {
            setTimeout(scan, 3000); setInterval(scan, 15000);
            try { if (self.__next_f && count('next:' + location.pathname) < 2) send(location.href + '#next_f', JSON.stringify(self.__next_f)); } catch (_) {}
          });
          const skip = /login|logout|auth|token|otp|session|member|account|password|analytics|collect/i;
          const keep = (u, t, type) => {
            try {
              const url = new URL(u, location.href);
              if (!/nexon\.com$/.test(url.hostname) || skip.test(url.pathname) || !t) return;
              if (count('api:' + url.hostname + url.pathname) < (type.includes('component') ? 4 : 3)) send(url.href, t);
            } catch (_) {}
          };
          const of = window.fetch;
          window.fetch = async function (...a) {
            const r = await of.apply(this, a);
            try {
              const u = typeof a[0] === 'string' ? a[0] : (a[0] && a[0].url) || r.url;
              const c = r.headers.get('content-type') || '';
              if (/json|x-component/.test(c)) r.clone().text().then(t => keep(u, t, c)).catch(() => {});
            } catch (_) {}
            return r;
          };
          const oo = XMLHttpRequest.prototype.open;
          XMLHttpRequest.prototype.open = function (m, u, ...rest) {
            this.addEventListener('load', () => {
              try {
                const c = this.getResponseHeader('content-type') || '';
                if (!/json/.test(c)) return;
                keep(u, this.responseType === 'json' ? JSON.stringify(this.response) : (this.responseType === '' || this.responseType === 'text') ? this.responseText : '', c);
              } catch (_) {}
            });
            return oo.call(this, m, u, ...rest);
          };
          addEventListener('keydown', e => {
            if (e.key !== 'F9') return;
            document.querySelectorAll('input,select,textarea').forEach(i => {
              i.setAttribute('data-mb-value', i.type === 'checkbox' || i.type === 'radio' ? String(i.checked) : i.value);
            });
            const c = document.documentElement.cloneNode(true);
            c.querySelectorAll('script,style,link,noscript,path,iframe').forEach(n => n.remove());
            send(location.href, c.outerHTML);
          }, true);
        })();
        """;
}

/// <summary>AI 대화 (ChatGPT·Claude): 사이트 그대로 작은 창에서 쓴다. 로그인은 사용자가 직접 한다</summary>
public static class Ai
{
    public const string ChatGpt = "https://chatgpt.com/";
    public const string Claude = "https://claude.ai/new";
}
