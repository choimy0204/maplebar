namespace MapleBar;

/// <summary>Path가 null이면 하위 메뉴 안의 구역 제목이다.</summary>
public record MenuEntry(string Title, string? Path);

public record TopMenu(string Name, string? Path, MenuEntry[]? Items);

/// <summary>maplescouter.com(환산주스탯) 상단 메뉴 구조 (2026-11 기준).</summary>
public static class SiteMenu
{
    public const string Base = "https://maplescouter.com";
    public const string InfoPath = "/ko/info";      // 캐릭터정보: ?name=닉네임
    public const string BossCutPath = "/ko/result"; // 효율∙보스컷: ?name=닉네임

    // 닉네임(?name=)을 붙여 여는 페이지: 캐릭터정보 + 분석&최적화 전체
    static HashSet<string>? namePages;
    public static HashSet<string> NamePages => namePages ??= BuildNamePages(); // Top보다 먼저 초기화되지 않게 늦게 만든다

    static HashSet<string> BuildNamePages()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { InfoPath, BossCutPath };
        foreach (var it in Top.First(t => t.Name == "분석&최적화").Items!)
            if (it.Path != null) set.Add(it.Path);
        return set;
    }

    static MenuEntry H(string t) => new(t, null);
    static MenuEntry I(string t, string p) => new(t, p);

    public static readonly TopMenu[] Top =
    {
        new("직접입력", "/ko/manual/input", null),
        new("캐릭터정보", InfoPath, null),
        new("분석&최적화", null, new[]
        {
            H("보스 / 사냥 분석"),
            I("효율∙보스컷", BossCutPath), I("파티 보스컷", "/ko/multi-result"),
            I("보스세팅 최적화", "/ko/optimizer"), I("주간 보스 정산", "/ko/boss-income"),
            I("사냥컷 분석", "/ko/huntresult"), I("사냥세팅 최적화", "/ko/hunt/optimizer"),
            H("효율 / 시뮬레이터"),
            I("스펙업 순서", "/ko/spec-order"), I("내실메이커", "/ko/base"),
            I("아이템메이커", "/ko/item"), I("헥사코어 순서", "/ko/hexa"),
            I("스타포스 효율", "/ko/starforce"), I("잠재∙추가옵션 효율", "/ko/cube-fire"),
            I("어빌리티 효율", "/ko/ability"), I("심볼 효율", "/ko/tax"),
            H("경험치 분석"),
            I("종합 레벨 시뮬레이터", "/ko/exp/total"), I("사냥 경험치", "/ko/exp/monster"),
            I("일일컨텐츠 경험치", "/ko/exp/daily-quest"), I("주간컨텐츠 경험치", "/ko/exp/weekly-quest"),
            I("소비아이템 경험치", "/ko/exp/item"), I("잠수맵 경험치", "/ko/exp/event"),
            I("트레저 헌터", "/ko/exp/treasure-hunter"),
            H("유니온"),
            I("유니온 캐릭터", "/ko/union-character"), I("유니온 챔피언", "/ko/union-champion"),
            H("해방 시뮬레이터"),
            I("제네시스 해방", "/ko/genesis"), I("데스티니 해방", "/ko/destiny"),
        }),
        // 메이플스토리 인벤 (주소 전체를 그대로 연다)
        new("인벤", null, new[]
        {
            H("자유게시판"),
            I("전체 글", Inven.Board), I("10추 글", Inven.Board + "?my=chu"),
            I("30추 글", Inven.Board + "?my=chuchu"), I("인증 30추 글", Inven.Board + "?my=chuver"),
            H("계정"),
            I("인벤 로그인", Inven.LoginUrl),
        }),
        // 네이버 웹툰 (작은 창에 맞는 모바일 화면, 로그인은 사용자가 직접 한다)
        new("웹툰", null, new[]
        {
            H("네이버 웹툰"),
            I("웹툰 홈", Webtoon.Home),
            H("계정"),
            I("네이버 로그인", Webtoon.LoginUrl),
        }),
        // 메애기 메이플 뮤직 (사이트의 플레이어로 듣는다. 창 하나를 같이 쓴다)
        new("🎵 뮤직", null, new[]
        {
            H("메애기 메이플 뮤직"),
            I("인기 차트 TOP 100", Music.Home), I("브금 검색", Music.Home + "/search"),
            H("내 목록"),
            I("현재 재생목록", Music.Home + "/current"), I("좋아요", Music.Home + "/favorite"),
            I("재생목록", Music.Home + "/my/playlists"),
        }),
    };
}

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
    public static bool IsMusic(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Host.EndsWith("meaegi.com", StringComparison.OrdinalIgnoreCase);
}
