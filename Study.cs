namespace MapleBar;

/// <summary>공부 묶음 하나: 칸 이름 3개와 카드 목록 (카드 한 장 = 칸 3개)</summary>
public class StudyDeck
{
    public string Name { get; set; } = "";
    public string[] Cols { get; set; } = { "단어", "의미", "첨언" };
    public List<string[]> Items { get; set; } = new();
    public bool On { get; set; } = true;
    /// <summary>즐겨찾기한 카드의 첫 칸</summary>
    public List<string> Stars { get; set; } = new();

    public bool Starred(string[] item) => Stars.Contains(item[0]);
    public void Star(string[] item, bool on)
    {
        Stars.Remove(item[0]);
        if (on) Stars.Add(item[0]);
    }
}

/// <summary>자기계발 카드 양식 읽기 · AI에게 줄 양식 만들기.
/// 양식: 한 줄에 카드 하나, 칸은 | 로 나눈다. "# 이름: …", "# 칸: a|b|c" 줄로 묶음 이름과 칸 이름을 정한다.</summary>
static class Study
{
    public static readonly string[] WordCols = { "단어", "의미", "첨언" };
    public static readonly string[] SentenceCols = { "문장", "해석", "첨언" };

    /// <summary>글을 읽어 묶음을 만든다. 카드가 하나도 없으면 null</summary>
    public static StudyDeck? Parse(string text, string fallbackName)
    {
        var deck = new StudyDeck { Name = fallbackName, Cols = (string[])WordCols.Clone() };
        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("```")) continue;
            if (line.StartsWith("#"))
            {
                var body = line.TrimStart('#').Trim();
                if (Head(body, "이름") is { Length: > 0 } n) deck.Name = n;
                else if (Head(body, "칸") is { Length: > 0 } c)
                {
                    var cols = Cells(c);
                    if (cols.Count >= 2) deck.Cols = new[] { cols[0], cols[1], cols.Count > 2 ? cols[2] : "첨언" };
                }
                continue;
            }
            // 표 모양(| a | b |)과 구분 줄(---|---)도 받아 준다
            if (line.Trim('|', '-', ':', ' ').Length == 0) continue;
            var cells = Cells(line);
            if (cells.Count < 2 || cells[0].Length == 0) continue;
            var card = new[] { cells[0], cells[1], string.Join(" | ", cells.Skip(2).Where(s => s.Length > 0)) };
            // 칸 이름 줄(용어|의미|첨언)은 카드가 아니다
            if (card[0] == deck.Cols[0] && card[1] == deck.Cols[1]) continue;
            deck.Items.Add(card);
        }
        if (deck.Name.Length > 30) deck.Name = deck.Name[..30];
        return deck.Items.Count > 0 ? deck : null;
    }

    static string? Head(string body, string key)
    {
        foreach (var sep in new[] { ':', '：' })
        {
            int i = body.IndexOf(sep);
            if (i > 0 && body[..i].Trim() == key) return body[(i + 1)..].Trim();
        }
        return null;
    }

    static List<string> Cells(string line)
    {
        var t = line.Trim();
        if (t.StartsWith("|")) t = t[1..];
        if (t.EndsWith("|")) t = t[..^1];
        return t.Split('|').Select(s => s.Trim()).ToList();
    }

    /// <summary>묶음을 더한다. 같은 이름이 있으면 겹치지 않는 카드만 이어 붙인다. 더한 카드 수를 돌려준다</summary>
    public static int Merge(Settings st, StudyDeck deck, out bool appended)
    {
        var old = st.Decks.FirstOrDefault(d => d.Name == deck.Name);
        appended = old != null;
        if (old == null) { st.Decks.Add(deck); Version++; return deck.Items.Count; }
        var seen = new HashSet<string>(old.Items.Select(x => x[0]));
        int n = 0;
        foreach (var c in deck.Items)
            if (seen.Add(c[0])) { old.Items.Add(c); n++; }
        Version++;
        return n;
    }

    /// <summary>묶음이 바뀔 때마다 오른다 (카드 순서를 새로 섞는다)</summary>
    public static int Version;

    /// <summary>AI에게 줄 글: 할 일, 규칙, 예시</summary>
    public static string Prompt(string topic, int count, string[] cols)
    {
        bool sentence = cols[0].Contains("문장");
        bool note = cols[2] == "첨언";
        // 셋째 칸이 '발음'뿐이면 발음만, '발음 및 적용예시'처럼 다른 것도 있으면 발음과 함께 나머지도 쓰게 한다
        bool sound = cols[2].Replace(" ", "") is "발음" or "발음기호";
        bool soundMix = !sound && cols[2].Contains("발음");
        // 예시는 정한 칸 이름을 그대로 쓰고, 셋째 칸 내용도 칸 이름에 맞춘다
        (string a, string b, string c)[] rows = sentence
            ? new[]
            {
                ("It was way easier than I thought.", "그건 생각보다 훨씬 쉬웠어.", "way = 훨씬 (비교급 강조)"),
                ("I'm running late.", "나 좀 늦을 것 같아.", "약속에 늦을 때 자주 써요"),
                ("Let me sleep on it.", "하룻밤 생각해 볼게.", "바로 결정하기 어려울 때"),
            }
            : new[]
            {
                ("ubiquitous", "어디에나 있는", "Smartphones are ubiquitous."),
                ("reluctant", "꺼리는, 마지못해 하는", "be reluctant to ~ 하기를 꺼리다"),
                ("meticulous", "꼼꼼한, 세심한", "careful보다 더 철저한 느낌"),
            };
        if (sound || soundMix)
        {
            string[] s = sentence
                ? new[] { "잇 워즈 웨이 이지어 댄 아이 쏘트", "아임 러닝 레잇", "렛 미 슬립 온 잇" }
                : new[] { "/juːˈbɪkwɪtəs/ 유비쿼터스", "/rɪˈlʌktənt/ 릴럭턴트", "/məˈtɪkjələs/ 머티큘러스" };
            string[] use = sentence
                ? new[] { "비교할 때 '훨씬'을 강조", "약속에 늦을 때", "바로 결정하기 어려울 때" }
                : new[] { "Smartphones are ubiquitous.", "She was reluctant to leave.", "He is meticulous about details." };
            for (int i = 0; i < rows.Length; i++) rows[i].c = soundMix ? $"{s[i]} / {use[i]}" : s[i];
        }
        else if (!note) for (int i = 0; i < rows.Length; i++) rows[i].c = $"({cols[2]})";
        string ex = $"# 이름: {(sentence ? "영어 회화 문장" : "영어 단어")}\n# 칸: {cols[0]}|{cols[1]}|{cols[2]}\n" +
                    string.Join("\n", rows.Select(r => $"{r.a}|{r.b}|{r.c}"));
        string rule4 = note
            ? $"{cols[2]}{Josa(cols[2], "은", "는")} 한 줄로 짧게 써 주세요 (예문, 외우는 요령, 헷갈리는 점 등)."
            : sound
                ? $"{cols[2]} 칸에는 {cols[0]}의 발음을 적어 주세요 (발음 기호와 한글 발음)."
            : soundMix
                ? $"{cols[2]} 칸에는 {cols[0]}의 발음(발음 기호와 한글 발음)을 먼저 쓰고, / 뒤에 나머지 내용을 한 줄로 짧게 적어 주세요."
                : $"{cols[2]} 칸에는 {cols[2]}{Josa(cols[2], "을", "를")} 한 줄로 짧게 적어 주세요.";
        return
$@"아래 규칙에 맞춰 자기계발 카드를 만들어 주세요.

주제: {topic}
개수: {count}개

[규칙]
1. 맨 위 두 줄은 이렇게 적어 주세요.
   # 이름: (주제를 짧게, 15자 안쪽)
   # 칸: {cols[0]}|{cols[1]}|{cols[2]}
2. 그 다음 줄부터 한 줄에 카드 하나씩, 칸을 | 로 나눠 적어 주세요.
   {cols[0]}|{cols[1]}|{cols[2]}
3. 칸 안에는 | 기호와 줄바꿈을 쓰지 마세요.
4. {rule4} 없으면 비워 두되 | 는 남겨 주세요.
5. 겹치는 카드 없이, 쉬운 것부터 어려운 것 순서로 만들어 주세요.
6. 표, 번호, 머리말·맺음말, 코드 블록 없이 양식 내용만 답해 주세요.

[예시]
{ex}
".Replace("\r\n", "\n").Replace("\n", "\r\n"); // 윈도우 줄바꿈으로 맞춰야 어디에 붙여 넣어도 줄이 안 붙는다
    }

    /// <summary>받침이 있으면 a, 없으면 b (은/는, 을/를)</summary>
    static string Josa(string word, string a, string b)
    {
        if (word.Length == 0) return a;
        char ch = word[^1];
        return ch is >= '가' and <= '힣' && (ch - '가') % 28 == 0 ? b : a;
    }
}

/// <summary>카드 띄우기를 켜 두는 동안 카드 한 장을 계속 띄워 두고, 정한 시간마다 다음 카드로 넘긴다.
/// 켜 둔 묶음을 섞어서 보여 주고, 한 바퀴 돌 때까지 같은 카드는 다시 나오지 않는다.</summary>
sealed class StudyRun : IDisposable
{
    readonly Settings st;
    readonly System.Windows.Forms.Timer timer = new() { Interval = 500 };
    readonly Queue<string[]> order = new();
    readonly Dictionary<string[], StudyDeck> owner = new();
    int version = -1;
    StudyCard? card;

    public static StudyRun? Current { get; private set; }
    /// <summary>카드의 ✕로 카드 띄우기를 껐을 때 (공부 창 스위치를 맞춘다)</summary>
    public static event Action? TurnedOff;

    public StudyRun(Settings st, float sc)
    {
        this.st = st;
        Current = this;
        timer.Tick += (_, _) => Tick();
        timer.Start();
    }

    bool Open => card != null && !card.IsDisposed;

    /// <summary>지금 볼 수 있는 카드인지 (켜 둔 묶음, 즐겨찾기만 보기면 즐겨찾기한 것)</summary>
    public static bool Usable(Settings st, StudyDeck d, string[] c) => d.On && st.Decks.Contains(d) && (!st.StudyStarOnly || d.Starred(c));

    /// <summary>지금 볼 수 있는 카드 수</summary>
    public static int Count(Settings st) => st.Decks.Sum(d => d.Items.Count(c => Usable(st, d, c)));

    void Tick()
    {
        // 바를 숨겼고 "바를 숨겨도 알림 받기"가 꺼져 있으면 잠깐 내린다
        bool want = st.StudyOn && !(JanusWatch.BarHidden && !st.AlertWhenHidden) && Count(st) > 0;
        if (want && !Open)
        {
            card = new StudyCard(st, Pick, TurnOff);
            if (card.Empty) { card.Dispose(); card = null; return; }
            if (!card.IsDisposed) card.Show();
        }
        else if (!want && Open) card!.Close();
    }

    /// <summary>바로 다음 카드로 넘긴다. 카드가 떠 있지 않으면 false</summary>
    public bool ShowNext()
    {
        if (!Open) return false;
        card!.Next();
        return true;
    }

    void TurnOff()
    {
        st.StudyOn = false; st.Save();
        if (Open) card!.Close();
        TurnedOff?.Invoke();
    }

    (StudyDeck, string[])? Pick()
    {
        if (version != Study.Version) { order.Clear(); version = Study.Version; }
        for (int round = 0; round < 2; round++)
        {
            if (order.Count == 0)
            {
                owner.Clear();
                var all = new List<string[]>();
                foreach (var d in st.Decks)
                    foreach (var c in d.Items)
                        if (Usable(st, d, c)) { all.Add(c); owner[c] = d; }
                foreach (var c in all.OrderBy(_ => Random.Shared.Next())) order.Enqueue(c);
            }
            // 섞어 둔 뒤 즐겨찾기를 풀었거나 묶음을 껐으면 건너뛴다
            while (order.Count > 0)
            {
                var item = order.Dequeue();
                if (owner.TryGetValue(item, out var d) && Usable(st, d, item)) return (d, item);
            }
        }
        return null;
    }

    public void Dispose()
    {
        timer.Stop(); timer.Dispose();
        if (card != null && !card.IsDisposed) card.Close();
        if (Current == this) Current = null;
    }
}
