using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

using static MapleBar.Native;

namespace MapleBar;

/// <summary>화면 맨 위에 항상 떠 있는 메이플 바.</summary>
public class BarForm : Form
{
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mod, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);

    const int HotkeyId = 1;
    const uint MOD_ALT = 1, MOD_CONTROL = 2;

    readonly Settings settings = Settings.Load();
    readonly FlowLayoutPanel left, right;
    readonly Panel searchBox;
    readonly TextBox search = new();
    readonly Label recentChip;
    readonly float sc;

    int S(int v) => UI.Scale(v, sc);

    // 폼이 만든 글꼴은 폼을 정리할 때 함께 푼다
    readonly List<Font> ownedFonts = new();
    Font OwnFont(Font f) { ownedFonts.Add(f); return f; }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing); // 컨트롤이 먼저 정리된 뒤에 글꼴을 푼다
        if (!disposing) return;
        searchFlash?.Dispose();
        foreach (var f in ownedFonts) f.Dispose();
        ownedFonts.Clear();
    }

    public BarForm()
    {
        // Windows 배율(DPI)에 더해 모니터 가로 해상도에 맞춰 크기와 글꼴을 같이 키우거나 줄인다
        // 메이플이 켜져 있으면 게임 화면 가로를, 없으면 모니터 가로를 기준으로 한다
        UI.Z = Game.Area() is Rectangle game ? Game.ZoomFor(game, DeviceDpi) : UI.FitZoom(Screen.PrimaryScreen!, DeviceDpi);
        sc = DeviceDpi / 96f * UI.Z;
        UI.Dark = settings.DarkMode; // 이 바와 앞으로 여는 창이 쓸 색
        Text = "메이플 바";
        FormBorderStyle = FormBorderStyle.None;
        TopMost = true;
        ShowInTaskbar = false; // 작업 표시줄·알탭에 안 나오게 (트레이 아이콘으로 연다)
        if (UI.AppIcon is { } appIcon) Icon = appIcon;
        StartPosition = FormStartPosition.Manual;
        Font = OwnFont(new Font("맑은 고딕", 10.5f * UI.Z, FontStyle.Bold));
        BackColor = UI.Line;         // 1px 테두리 색
        Padding = new Padding(1);
        Height = S(56);

        left = MakeFlow(DockStyle.Left);
        right = MakeFlow(DockStyle.Right);
        var filler = new Panel { Dock = DockStyle.Fill, BackColor = UI.Back };
        filler.MouseDown += DragMove;
        Controls.Add(left);
        Controls.Add(right);
        Controls.Add(filler);
        filler.BringToFront();

        // ── 왼쪽: 이동 손잡이, 로고, 메뉴 ──
        var grip = new Label
        {
            Text = "⠿", AutoSize = true, Cursor = Cursors.SizeAll,
            ForeColor = UI.T(Color.FromArgb(170, 170, 170)), Font = OwnFont(new Font("Segoe UI Symbol", 12f * UI.Z)),
            Margin = new Padding(0, 0, S(4), 0),
        };
        grip.MouseDown += DragMove;
        left.Controls.Add(pin = MakePin());
        left.Controls.Add(grip);

        // 저장된 순서대로 놓고, 저장 목록에 없는 메뉴는 원래 순서대로 뒤에 붙인다
        var tops = SiteMenu.Top.Where(t => t.Name != "직접입력").ToList(); // 바에서는 직접입력을 안 쓴다
        var orderedTops = settings.BarOrder
            .Select(name => tops.FirstOrDefault(t => t.Name == name))
            .Where(t => t != null).Distinct()
            .Concat(tops)
            .Distinct()
            .ToList();

        foreach (var top in orderedTops)
        {
            Label nav = null!;
            if (top!.Items == null)
            {
                nav = UI.Button(top.Name, "", () => { if (!navDragged) Navigate(WithName(top.Path!)); }, sc);
                nav.MouseEnter += (_, _) => { if (!navDragging) HideMega(); };
            }
            else
            {
                MakeMega(top);
                // 주소가 있는 메뉴(유튜브)는 글자를 누르면 바로 그 페이지를 연다. 올리면 펼친 메뉴는 그대로 나온다
                nav = UI.Button(top.Name, "", top.Path != null
                    ? () => { if (navDragged) return; HideMega(); Navigate(WithName(top.Path)); }
                    : () => { if (!navDragged) OpenMegaOf(top, nav); }, sc);
                nav.MouseEnter += (_, _) => { if (!navDragging) OpenMegaOf(top, nav); };
            }
            nav.Margin = new Padding(S(6), 0, S(6), 0);
            if (top.Name == "영상&음악") AddPlayIcon(nav);
            nav.Tag = top.Name; // 순서 저장에 쓴다
            nav.Visible = SiteMenu.ShowTop(top, settings);
            HookNavDrag(nav);
            left.Controls.Add(nav);
            navs.Add((top, nav));
        }

        // 마우스가 메뉴 글자와 펼친 메뉴 밖으로 나가면 닫는다
        megaTimer.Tick += (_, _) =>
        {
            if (openMega == null || megaNav == null) { megaTimer.Stop(); return; }
            var cur = Cursor.Position;
            var navRect = megaNav.RectangleToScreen(megaNav.ClientRectangle);
            navRect = Rectangle.FromLTRB(navRect.Left, navRect.Top, navRect.Right, openMega.Top + 2); // 바와 메뉴 사이 틈까지 이어지게
            if (!navRect.Contains(cur) && !openMega.Bounds.Contains(cur)) HideMega();
        };

        // ── 오른쪽: 뮤직 컨트롤(듣는 중일 때만), 닉네임 검색, 최근 기록, 설정/숨기기/종료 ──
        AddMusicControls();

        searchBox = new Panel { Size = new Size(S(230), S(36)), BackColor = UI.Back, Margin = new Padding(S(10), 0, S(4), 0) };
        searchBox.Paint += PaintSearchBorder;
        search.BorderStyle = BorderStyle.None;
        search.BackColor = UI.Back; // 다크 모드에서 흰 칸이 튀지 않게 바탕과 맞춘다
        search.ForeColor = UI.Text;
        search.PlaceholderText = "닉네임을 입력하세요";
        // 메이플에서 넘어오면 입력기가 꺼진 채로 남아 한/영이 안 먹을 때가 있다.
        // 칸에 들어오면 입력기를 다시 붙이고, 마지막에 쓰던 한글/영문 상태로 돌려놓는다
        search.GotFocus += (_, _) => ImeRestore(search.Handle);
        search.LostFocus += (_, _) => ImeRemember(search.Handle);
        search.Font = OwnFont(new Font("맑은 고딕", 10.5f * UI.Z));
        search.Width = searchBox.Width - S(44);
        search.Location = new Point(S(12), (searchBox.Height - search.PreferredHeight) / 2);
        search.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Search(SiteMenu.InfoPath); }
            else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; search.Clear(); }
        };
        // 🔍 글자는 파란 컬러 그림으로 나와서 튀므로, 돋보기를 글씨색으로 직접 그린다
        var searchIcon = UI.Button(" ", "캐릭터정보 검색 (Enter)", () => Search(SiteMenu.InfoPath), sc);
        searchIcon.Font = OwnFont(new Font("Segoe UI Emoji", 10f * UI.Z));
        searchIcon.Paint += (_, e) => DrawMagnifier(e.Graphics, searchIcon.ClientRectangle, UI.Sub);
        searchIcon.Padding = new Padding(S(2));
        searchIcon.Location = new Point(searchBox.Width - S(34), (searchBox.Height - searchIcon.PreferredSize.Height) / 2);
        searchBox.Controls.Add(search);
        searchBox.Controls.Add(searchIcon);
        searchBox.Click += (_, _) => search.Focus();
        search.GotFocus += (_, _) => searchBox.Invalidate();
        search.LostFocus += (_, _) => searchBox.Invalidate();
        right.Controls.Add(searchBox);


        recentChip = UI.Button("", "최근 검색한 캐릭터", ShowRecentMenu, sc,
            // 다크 모드: 누런 갈색 칸 대신 차콜 칸에 호박색 글씨
            fore: UI.Dark ? UI.AccentText : Color.FromArgb(150, 80, 0),
            back: UI.Dark ? UI.HoverBack : Color.FromArgb(255, 243, 224));
        recentChip.Margin = new Padding(S(8), 0, S(4), 0);
        right.Controls.Add(recentChip);
        UpdateRecentChip();

        Label? gear = null;
        // ⚙ 글자는 컬러 그림(꽃처럼 보임)으로 나와서, 톱니바퀴를 직접 그린다
        gear = UI.Button(" ", "설정", () => ShowSettingsMenu(gear!), sc);
        gear.Paint += (_, e) => DrawGear(e.Graphics, gear.ClientRectangle, gear.ForeColor);
        right.Controls.Add(gear);
        right.Controls.Add(UI.Button("—", "바 숨기기 (Ctrl+Alt+M으로 다시 열기)", HideAll, sc));
        right.Controls.Add(UI.Button("✕", "종료 (열린 창도 모두 닫힘)", Close, sc));
    }

    void DrawGear(Graphics g, Rectangle r, Color color)
    {
        const int n = 8;
        float cx = r.Left + r.Width / 2f, cy = r.Top + r.Height / 2f, outer = 7.5f * sc, inner = 5.6f * sc;
        double step = Math.PI * 2 / n;
        var pts = new List<PointF>();
        for (int i = 0; i < n; i++)
        {
            double a = i * step - Math.PI / 2;
            foreach (var (ang, rad) in new[] { (a - step * 0.30, inner), (a - step * 0.17, outer), (a + step * 0.17, outer), (a + step * 0.30, inner) })
                pts.Add(new PointF(cx + (float)(Math.Cos(ang) * rad), cy + (float)(Math.Sin(ang) * rad)));
        }
        using var path = new System.Drawing.Drawing2D.GraphicsPath(System.Drawing.Drawing2D.FillMode.Alternate);
        path.AddPolygon(pts.ToArray());
        float hole = 2.4f * sc;
        path.AddEllipse(cx - hole, cy - hole, hole * 2, hole * 2); // 가운데 구멍
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var b = new SolidBrush(color);
        g.FillPath(b, path);
    }

    // ── 뮤직: 메애기 창은 숨겨 두고 바에서 곡 제목·진행 바·⏮ ⏯ ⏭만 보여 준다 ──
    MusicPill? musicPill;
    MusicDock? musicDock;

    void AddMusicControls()
    {
        musicPill = new MusicPill(sc) { Visible = false, Margin = new Padding(S(4), 0, S(6), 0) };
        // 제목을 누르면: 브금은 곡 목록, 유튜브 뮤직은 그 창(재생목록 고르기)
        musicPill.TitleClick += () =>
        {
            if (WebWindow.MusicIsYt) WebWindow.ToggleMusicWindow();
            else MusicPicker.Toggle(this, settings, sc);
        };
        var menu = UI.Rounded(new ContextMenuStrip { Font = new Font("맑은 고딕", 9.5f * UI.Z), ShowImageMargin = false });
        var top100 = menu.Items.Add("인기 TOP 100", null, (_, _) => MusicPicker.Open(this, settings, sc, false));
        var search = menu.Items.Add("브금 검색", null, (_, _) => MusicPicker.Open(this, settings, sc, true));
        var winItem = menu.Items.Add("메애기 창 보기/숨기기", null, (_, _) => WebWindow.ToggleMusicWindow());
        var lyricsItem = new ToolStripMenuItem("가사 보기");
        lyricsItem.Click += (_, _) => { settings.MusicLyrics = !settings.MusicLyrics; settings.Save(); WebWindow.MusicRefresh(); UpdateLyrics(lastMusic); };
        menu.Items.Add(lyricsItem);
        // 브금 ↔ 유튜브 뮤직 바꾸기 (하나만 켜진다)
        var swap = menu.Items.Add("", null, (_, _) =>
        {
            if (WebWindow.MusicIsYt) MusicPicker.Open(this, settings, sc, false);
            else WebWindow.EnsureMusic(settings, sc, Music.YtHome, false); // 창은 숨긴 채, 칸만
        });
        var dockBack = menu.Items.Add("바에 다시 넣기", null, (_, _) => musicDock?.DockBack());
        menu.Opening += (_, _) =>
        {
            bool yt = WebWindow.MusicIsYt;
            top100.Visible = search.Visible = !yt;
            winItem.Text = yt ? "유튜브 뮤직 창 보기/숨기기" : "메애기 창 보기/숨기기";
            lyricsItem.Visible = yt;
            lyricsItem.Checked = settings.MusicLyrics;
            swap.Text = yt ? "메이플 브금으로 바꾸기" : "유튜브 뮤직으로 바꾸기";
            dockBack.Visible = musicDock?.Floating == true;
        };
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("뮤직 끄기", null, (_, _) => WebWindow.StopMusic());
        musicPill.ContextMenuStrip = menu;
        musicPill.Disposed += (_, _) => DisposeMenu(menu); // 칸은 메뉴를 대신 치우지 않으므로 칸이 정리될 때 메뉴와 글꼴을 같이 푼다
        right.Controls.Add(musicPill);
        musicDock = new MusicDock(this, right, musicPill, settings, sc, LayoutBar, () => new Rectangle(Left, Top, Width, Math.Max(S(10), VisibleBottom - Top)));
        WebWindow.MusicChanged += OnMusicChanged;
        // 칸이 들어오고 나가거나 폭·화면 배율이 바뀌면 바 폭을 다시 맞춘다 (가운데에 빈칸이 남지 않게)
        EventHandler fit = (_, _) =>
        {
            if (fitPending || !IsHandleCreated || IsDisposed) return;
            fitPending = true;
            BeginInvoke(() => { fitPending = false; if (!IsDisposed) LayoutBar(); });
        };
        left.Layout += (s, e) => fit(s, e);
        right.Layout += (s, e) => fit(s, e);
        Resize += fit;
        // 바를 다시 만들었을 때: 멈춰 있는 곡은 알림이 안 오니 지금 곡을 바로 보여 준다
        Shown += (_, _) => { if (WebWindow.CurrentMusic is { } cm) OnMusicChanged(cm); };
        Shown += (_, _) => DevInfo.Warm(); // 개발자 정보를 미리 받아 둬서 누르자마자 뜨게
    }

    void OnMusicChanged(WebWindow.MusicInfo? m)
    {
        if (IsDisposed || musicPill == null) return;
        bool show = m != null;
        if (m != null) musicPill.SetInfo(m);
        if (musicPill.Visible != show) { musicPill.Visible = show; if (musicDock?.Floating != true) LayoutBar(); }
        // 지난번에 꺼내 뒀으면 그 자리에 다시 꺼내 둔다
        if (show && settings.MusicFloat && musicDock?.Floating == false) musicDock.RestoreFloat();
        musicDock?.SetShown(show && Visible);
        lastMusic = m;
        UpdateLyrics(m);
    }

    // ── 유튜브 뮤직 가사: 켜 두면 뮤직 칸 아래에 띄운다. 가사가 없는 곡은 안 띄운다 ──
    LyricsCard? lyrics;
    WebWindow.MusicInfo? lastMusic;

    void UpdateLyrics(WebWindow.MusicInfo? m)
    {
        if (IsDisposed || musicPill == null) return;
        var host = musicPill.FindForm();
        bool want = m is { Yt: true, Lyrics: not null } && settings.MusicLyrics && musicPill.Visible &&
                    host is { Visible: true } && host.WindowState != FormWindowState.Minimized;
        if (!want) { if (lyrics?.Visible == true) lyrics.Hide(); return; }
        lyrics ??= new LyricsCard(sc, () =>
        {
            settings.MusicLyrics = false; settings.Save();
            lyrics?.Hide();
        });
        lyrics.Place(musicPill.RectangleToScreen(musicPill.ClientRectangle));
        lyrics.SetText(m!.Lyrics!, m.LyricLine, m.Time, m.Title);
        if (!lyrics.Visible) lyrics.Show();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        WebWindow.MusicChanged -= OnMusicChanged;
        base.OnHandleDestroyed(e);
    }

    FlowLayoutPanel MakeFlow(DockStyle dock)
    {
        var f = new FlowLayoutPanel
        {
            Dock = dock, AutoSize = true, WrapContents = false, BackColor = UI.Back,
            Padding = new Padding(S(8), 0, S(8), 0),
        };
        f.MouseDown += DragMove;
        return f;
    }

    // ── 메뉴 버튼 끌어서 순서 바꾸기 ──
    bool navDragging;  // 지금 끌고 있는 중
    bool navDragged;   // 이번 누름이 끌기로 끝났다 (끝난 뒤 클릭을 막는다)
    Point navDownAt;   // 누른 자리 (화면 좌표)
    bool navDown;

    void HookNavDrag(Label nav)
    {
        var normalCursor = nav.Cursor;
        nav.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            navDown = true;
            navDragged = false;
            navDownAt = Cursor.Position;
        };
        nav.MouseMove += (_, e) =>
        {
            if (!navDown) return;
            if (!navDragging)
            {
                if (Math.Abs(Cursor.Position.X - navDownAt.X) < SystemInformation.DragSize.Width) return;
                navDragging = true;
                navDragged = true;
                HideMega();
                nav.Cursor = Cursors.SizeWE;
            }
            MoveNavTo(nav, left.PointToClient(Cursor.Position).X);
        };
        nav.MouseUp += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            navDown = false;
            if (!navDragging) return;
            navDragging = false;
            nav.Cursor = normalCursor;
            settings.BarOrder = left.Controls.OfType<Label>().Where(c => c.Tag is string).Select(c => (string)c.Tag!).ToList();
            settings.Save();
        };
    }

    /// <summary>커서 x 위치에 맞게 바로 옆 칸과 자리를 바꾼다 (손잡이 앞으로는 못 간다).</summary>
    void MoveNavTo(Label nav, int x)
    {
        // 숨긴 메뉴는 건너뛰고 보이는 옆 칸과 자리를 바꾼다
        int index = left.Controls.GetChildIndex(nav);
        int n = index + 1;
        while (n < left.Controls.Count && !left.Controls[n].Visible) n++;
        if (n < left.Controls.Count && left.Controls[n] is Label next && x > next.Right - nav.Width / 2)
        {
            left.Controls.SetChildIndex(nav, n);
            return;
        }
        int p = index - 1;
        while (p >= 2 && !left.Controls[p].Visible) p--;
        if (p >= 2 && left.Controls[p] is Label prev && x < prev.Left + nav.Width / 2)
            left.Controls.SetChildIndex(nav, p);
    }

    // ── 펼침 메뉴 ──
    readonly System.Windows.Forms.Timer megaTimer = new() { Interval = 120 };
    MegaMenuForm? openMega;
    Label? megaNav;

    void OpenMega(MegaMenuForm mega, Label nav)
    {
        if (openMega != null && openMega != mega) openMega.Hide();
        if (megaNav != null && megaNav != nav) megaNav.ForeColor = UI.Text;
        openMega = mega;
        megaNav = nav;
        nav.ForeColor = UI.Accent;
        mega.ShowUnder(this);
        megaTimer.Start();
    }

    // 바의 메뉴 버튼과 그 펼침 메뉴 (고정 바 설정에서 숨기면 다시 만든다)
    readonly List<(TopMenu Top, Label Nav)> navs = new();
    readonly Dictionary<string, MegaMenuForm> megas = new();

    /// <summary>숨긴 항목을 뺀 펼침 메뉴를 만든다. 남은 항목이 없으면 만들지 않는다.</summary>
    void MakeMega(TopMenu top)
    {
        if (megas.Remove(top.Name, out var old)) old.Dispose();
        var items = SiteMenu.VisibleItems(top, settings.BarHiddenItems);
        if (items.Length > 0) megas[top.Name] = new MegaMenuForm(items, sc, p => Navigate(WithName(p))) { Owner = this };
    }

    void OpenMegaOf(TopMenu top, Label nav)
    {
        if (megas.TryGetValue(top.Name, out var mega)) OpenMega(mega, nav); else HideMega();
    }

    /// <summary>고정 바 설정을 바꾸면 바로 바에 반영한다</summary>
    void ApplyBarItems()
    {
        HideMega();
        foreach (var (top, nav) in navs)
        {
            if (top.Items != null) MakeMega(top);
            nav.Visible = SiteMenu.ShowTop(top, settings);
        }
        LayoutBar();
    }

    void HideMega()
    {
        openMega?.Hide();
        if (megaNav != null) megaNav.ForeColor = UI.Text;
        openMega = null;
        megaNav = null;
        megaTimer.Stop();
    }

    /// <summary>실행 인자: MapleBar.exe 닉네임 [--boss]</summary>
    public void RunArgs(string[] args)
    {
        var name = args.FirstOrDefault(a => !a.StartsWith("--"));
        if (name == null) return;
        search.Text = name;
        Search(args.Contains("--boss") ? SiteMenu.BossCutPath : SiteMenu.InfoPath);
    }

    // ── 동작 ──

    /// <summary>닉네임을 받는 페이지면 검색칸의 닉네임을 붙인다.</summary>
    string WithName(string path)
    {
        var name = NameOrStar();
        return SiteMenu.NamePages.Contains(path) && name.Length > 0
            ? path + "?name=" + Uri.EscapeDataString(name)
            : path;
    }

    /// <summary>검색칸의 닉네임. 비어 있으면 별표한 캐릭터.</summary>
    string NameOrStar()
    {
        var name = search.Text.Trim();
        return name.Length > 0 ? name : (settings.StarNick ?? "").Trim();
    }

    System.Windows.Forms.Timer? searchFlash; // 닉네임 없이 검색하면 검색칸 색을 잠깐 바꿨다 되돌린다

    void Search(string path)
    {
        var name = NameOrStar();
        if (name.Length == 0)
        {
            search.Focus();
            searchBox.BackColor = UI.T(Color.FromArgb(255, 240, 230));
            // 타이머 하나를 다시 쓴다. 다시 누르면 처음부터 0.4초를 센다
            if (searchFlash == null)
            {
                searchFlash = new System.Windows.Forms.Timer { Interval = 400 };
                searchFlash.Tick += (_, _) => { searchFlash.Stop(); searchBox.BackColor = UI.Back; };
            }
            searchFlash.Stop();
            searchFlash.Start();
            return;
        }
        settings.AddRecent(name);
        settings.Save();
        UpdateRecentChip();

        // 열린 캐릭터 창을 모두 이 캐릭터로 바꾸고, 누른 화면이 없으면 새 창으로 연다
        bool shown = WebWindow.ApplyName(name, path);
        if (!shown) Navigate(path + "?name=" + Uri.EscapeDataString(name));
    }

    /// <summary>링크를 항상 새 창으로 연다.</summary>
    void Navigate(string path)
    {
        HideMega();
        // 헥사 강화 계산기: 헥사코어 화면을 닉네임으로 열고 그 위에 계산기를 띄운다
        if (path.StartsWith(HexaCalc.Path)) { HexaCalc.Open(path, settings, sc); return; }
        // 계산기: 사이트가 아니라 바가 직접 띄우는 작은 창
        if (path == CalcForm.Path) { CalcForm.Open(this, settings, sc); return; }
        // 메모: 스티커 메모 같은 작은 창
        if (path == MemoForm.Path) { MemoForm.Open(this, settings, sc); return; }
        // 뮤직은 이미 열린 창에서 페이지만 바꾼다 (노래가 두 곳에서 겹쳐 나오지 않게)
        if (Music.IsMusic(path))
        {
            // 인기 차트·검색은 바의 곡 목록으로 고른다. 메애기 창은 뒤에 숨어서 재생만 한다.
            if (path == Music.Home) { MusicPicker.Open(this, settings, sc, false); return; }
            if (path == Music.Home + "/search") { MusicPicker.Open(this, settings, sc, true); return; }
            // 유튜브 뮤직은 창을 숨긴 채 열고 뮤직 칸만 띄운다 (칸을 누르면 창이 뜬다)
            // 구글 로그인이 안 된 걸 알면 로그인 화면부터 연다 (모르면 그냥 연다)
            if (Music.IsYtMusic(path) && Logins.IsIn("google") == false)
            {
                OpenLogin("https://accounts.google.com/ServiceLogin?service=youtube&continue=" + Uri.EscapeDataString(Music.YtHome));
                return;
            }
            if (Music.IsYtMusic(path)) { WebWindow.EnsureMusic(settings, sc, path, false); return; }
            WebWindow.EnsureMusic(settings, sc, path, true); // 내 목록(로그인 필요)은 창으로 연다
            return;
        }
        // 유튜브 영상·쇼츠는 재생 창 하나에서 바꿔 가며 본다
        if (YouTube.IsVideo(path)) { WebWindow.ShowYouTube(path, settings, sc); return; }
        // 경매장은 닫을 때 숨겨 두었으니 그 창을 다시 보여 준다
        if (Nexon.IsAuction(path) && WebWindow.ShowHiddenAuction()) return;
        // 넥슨 로그인이 안 된 걸 알면 로그인 화면부터 연다 (로그인 뒤 경매장으로 넘어간다)
        if (Nexon.IsAuction(path) && Logins.IsIn("nexon") == false) { OpenLogin(Nexon.LoginUrl); return; }
        try { new WebWindow(path.StartsWith("http") ? path : SiteMenu.Base + path, settings, sc).Show(); }
        catch (Exception ex)
        {
            MessageBox.Show(this, "창을 열지 못했어요.\n\n" + ex, "메이플 바", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    void OpenLogin(string url)
    {
        try { new WebWindow(url, settings, sc).Show(); }
        catch (Exception ex)
        {
            MessageBox.Show(this, "창을 열지 못했어요.\n\n" + ex, "메이플 바", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    void ShowRecentMenu()
    {
        var cms = UI.Rounded(new ContextMenuStrip { Font = new Font("맑은 고딕", 10f * UI.Z), ShowImageMargin = false });
        // 별표한 캐릭터는 기록에서 밀려나거나 지워져도 목록에 남긴다
        var names = settings.Recent.ToList();
        var star = settings.StarNick;
        if (star != null && !names.Any(x => string.Equals(x, star, StringComparison.OrdinalIgnoreCase))) names.Insert(0, star);
        if (names.Count == 0)
        {
            cms.Items.Add(new ToolStripMenuItem("검색 기록이 없어요") { Enabled = false });
        }
        // 별 칸: 글 왼쪽의 ★/☆. 누르면 메뉴를 닫지 않고 별만 바꾼다
        var tip = new ToolTip { InitialDelay = 0, ShowAlways = true };
        cms.Disposed += (_, _) => tip.Dispose();
        var items = new List<(ToolStripMenuItem mi, string n)>();
        long starAt = 0; bool tipOn = false;
        bool StarHit() => Environment.TickCount64 - starAt < 600;
        int StarW() => TextRenderer.MeasureText("★ ", cms.Font).Width + S(6);
        bool IsStarred(string n) => string.Equals(settings.StarNick, n, StringComparison.OrdinalIgnoreCase);
        void Paint() { foreach (var (m, n) in items) m.Text = (IsStarred(n) ? "★  " : "☆  ") + n; }
        cms.Closing += (_, e) => { if (StarHit() && e.CloseReason == ToolStripDropDownCloseReason.ItemClicked) e.Cancel = true; };
        foreach (var name in names)
        {
            var n = name;
            var mi = new ToolStripMenuItem(n);
            items.Add((mi, n));
            mi.MouseMove += (_, e) =>
            {
                bool on = e.X < StarW();
                if (on == tipOn) return;
                tipOn = on;
                if (on) tip.Show("별표한 캐릭터는 검색칸이 비어 있을 때\n기본으로 불러와요", cms, cms.PointToClient(Cursor.Position) + new Size(S(14), S(18)));
                else tip.Hide(cms);
            };
            mi.MouseLeave += (_, _) => { if (tipOn) { tipOn = false; tip.Hide(cms); } };
            mi.MouseDown += (_, e) =>
            {
                if (e.Button != MouseButtons.Left || e.X >= StarW()) return;
                starAt = Environment.TickCount64;
                // 다른 캐릭터를 누르면 그쪽으로 옮기고, 같은 캐릭터를 다시 누르면 끈다
                settings.StarNick = IsStarred(n) ? null : n;
                settings.Save();
                Paint();
            };
            EventHandler openInfo = (_, _) => { if (StarHit()) return; search.Text = n; Search(SiteMenu.InfoPath); };
            mi.Click += openInfo;
            var info = new ToolStripMenuItem("캐릭터정보");
            info.Click += openInfo;
            var boss = new ToolStripMenuItem("보스컷");
            boss.Click += (_, _) => { search.Text = n; Search(SiteMenu.BossCutPath); };
            mi.DropDownItems.Add(info);
            mi.DropDownItems.Add(boss);
            cms.Items.Add(mi);
        }
        Paint();
        if (settings.Recent.Count > 0)
        {
            cms.Items.Add(new ToolStripSeparator());
            var clear = new ToolStripMenuItem("기록 지우기");
            clear.Click += (_, _) => { settings.Recent.Clear(); settings.Save(); UpdateRecentChip(); };
            cms.Items.Add(clear);
        }
        ShowBelowBar(cms, recentChip);
    }

    Control? closedByAnchor;
    long closedAt;
    int menusOpen; // 바의 메뉴가 열려 있는 동안은 바를 맨 위로 올리지 않는다 (메뉴가 바 뒤로 숨지 않게)

    /// <summary>메뉴를 바 아래에 조금 띄워서 연다.</summary>
    // 바 아래 메뉴는 열 때마다 글꼴까지 새로 만드니, 닫히면 둘 다 치운다
    static void DisposeMenu(ContextMenuStrip cms) { var f = cms.Font; cms.Dispose(); f.Dispose(); }
    void ShowBelowBar(ContextMenuStrip cms, Control anchor)
    {
        // 열린 메뉴의 버튼을 다시 누르면: 누르는 순간 메뉴가 먼저 닫히므로, 여기서 다시 열지 않고 끝낸다
        if (closedByAnchor == anchor && Environment.TickCount64 - closedAt < 800)
        {
            closedByAnchor = null;
            BeginInvoke(() => DisposeMenu(cms));
            return;
        }
        cms.Opened += (_, _) => menusOpen++;
        cms.Closed += (_, e) =>
        {
            menusOpen = Math.Max(0, menusOpen - 1);
            bool onAnchor = anchor.ClientRectangle.Contains(anchor.PointToClient(Cursor.Position));
            closedByAnchor = e.CloseReason == ToolStripDropDownCloseReason.AppClicked && onAnchor ? anchor : null;
            closedAt = Environment.TickCount64;
            BeginInvoke(() => DisposeMenu(cms));
        };
        // 화면 오른쪽을 넘으면 윈도우가 알아서 안쪽으로 옮긴다
        cms.Show(new Point(anchor.PointToScreen(Point.Empty).X, Bottom + S(8)));
    }

    void UpdateRecentChip()
    {
        recentChip.Text = settings.Recent.Count > 0 ? settings.Recent[0] + " ▾" : "최근 ▾";
        if (IsHandleCreated) LayoutBar();
    }

    void ShowSettingsMenu(Control anchor)
    {
        HideMega();
        var look = new CardMenuRenderer(sc);
        var cms = UI.Rounded(new ContextMenuStrip { Font = new Font("맑은 고딕", 10f * UI.Z), ShowImageMargin = false, Renderer = look });
        cms.Padding = new Padding(S(4), S(6), S(4), S(6));
        // 켜고 끄는 항목은 프로그램 설정 창에 탭으로 나눠 두고, 메뉴에는 여는 것만 남긴다
        // 아이콘은 Segoe 아이콘 글꼴: 톱니·종·책·키보드·눈 감기·정보
        cms.Items.Add(look.Item("", "프로그램 설정", "바 · 로그인 · 창 · 화면", () => OpenProgramSettings()));
        cms.Items.Add(look.Item("", "알림 설정", "야누스 · 룬 · 버프", () => { if (janus != null) JanusForm.Open(this, settings, janus, sc); }));
        cms.Items.Add(look.Item("", "자기계발 카드", "재획하며 보는 공부 카드", () => StudyForm.Open(this, settings, sc)));
        cms.Items.Add(look.Item("", "웹툰·쇼츠 키", "넘기는 키 정하기", () => { if (janus != null) JanusForm.Open(this, settings, janus, sc, toon: true); }));
        cms.Items.Add(look.Item("", "설정 관리", "저장 · 불러오기", () => CfgForm.Open(this, settings, sc))); // 구름
        cms.Items.Add(new ToolStripSeparator());
        cms.Items.Add(look.Item("", "바 숨기기", "단축키로 다시 꺼내요", HideAll, "Ctrl+Alt+M"));
        cms.Items.Add(look.Item("", "개발자 정보", "공지사항 · 문의", () => DevInfo.Open(this)));
        cms.Closed += (_, _) => BeginInvoke(look.Dispose);
        ShowBelowBar(cms, anchor);
    }

    void OpenProgramSettings(int tab = 0) =>
        ProgramForm.Open(this, settings, sc, new ProgramForm.Hooks(
            SetPinned: SetPinned,
            SyncClock: SyncClock,
            InvenPoll: () => inven?.PollNow(),
            InvenLogin: () => Navigate(Inven.LoginUrl),
            RebuildForTheme: RebuildForTheme,
            ApplyBarItems: ApplyBarItems,
            OpenUrl: url => Navigate(url)), tab);

    /// <summary>다크 모드를 바꾸면 바와 작은 창을 새 색으로 다시 만든다 (해상도가 바뀔 때와 같은 길).
    /// 사이트 창·뮤직 창은 그대로 두고, 다시 만든 바에서 프로그램 설정 창의 화면 탭을 다시 연다.</summary>
    void RebuildForTheme()
    {
        UI.Dark = settings.DarkMode;
        Program.ReopenSettingsTab = 5; // 화면 탭
        Program.RestartRequested = true;
        Close();
    }

    // 게임에 맞춰 저절로 숨기고 보이기
    bool userHidden;  // 직접 숨겼으면 게임이 켜져도 저절로 보이지 않는다
    bool autoHidden;  // 게임이 꺼져서 숨겼다 → 게임이 켜지면 다시 보인다
    bool keepShown;   // 게임이 꺼진 채로 직접 열었으면 저절로 숨기지 않는다 (게임이 한 번 켜지면 풀린다)

    void HideAll() => HideAll(auto: false);

    void HideAll(bool auto)
    {
        HideMega();
        NoticeStrip.CloseAll(); // 떠 있던 인벤 알림과 기다리던 알림도 같이 치운다
        Hide();
        musicDock?.SetShown(false);
        lyrics?.Hide();
        JanusWatch.BarHidden = true;
        if (auto) autoHidden = true; else { userHidden = true; autoHidden = false; }
        if (tray == null) return;
        // 처음 숨길 때 한 번만, 다시 여는 방법을 알려 준다
        if (auto && !autoHintShown)
        {
            autoHintShown = true;
            tray.ShowBalloonTip(4000, "메이플스토리를 기다리는 중",
                "메이플스토리를 켜면 바가 저절로 나와요. 지금 열려면 Ctrl+Alt+M을 누르세요.", ToolTipIcon.None);
        }
        else if (!auto && !hideHintShown)
        {
            hideHintShown = true;
            tray.ShowBalloonTip(4000, "메이플 바를 숨겼어요",
                "Ctrl+Alt+M을 누르거나, 화면 오른쪽 아래 메이플 바 아이콘을 두 번 누르면 다시 나와요.", ToolTipIcon.None);
        }
    }
    bool hideHintShown, autoHintShown;

    /// <summary>게임이 켜졌는지 보고 바를 저절로 숨기거나 보인다.</summary>
    void CheckGame()
    {
        if (Game.Running())
        {
            keepShown = false;
            if (!Visible && autoHidden && !userHidden) { WindowState = FormWindowState.Normal; ShowAll(auto: true); }
        }
        else if (Visible && !keepShown && !dragging && !InUse() && MouseButtons == MouseButtons.None)
            HideAll(auto: true);
    }

    // 저절로 보일 때는 게임에서 키보드를 빼앗지 않게, 활성화하지 않고 띄운다
    bool noActivate;
    protected override bool ShowWithoutActivation => noActivate;

    // "바를 숨겨도 알림 받기" 체크 메뉴 (바 메뉴와 아이콘 메뉴에 같이 쓴다)
    ToolStripMenuItem AlertWhenHiddenItem()
    {
        var it = new ToolStripMenuItem("바를 숨겨도 알림 받기") { Checked = settings.AlertWhenHidden, CheckOnClick = true };
        it.ToolTipText = "끄면 바를 숨긴 동안 야누스·룬·버프·도핑·인벤 새 글 알림이 뜨지 않아요";
        it.CheckedChanged += (_, _) => { settings.AlertWhenHidden = it.Checked; settings.Save(); };
        return it;
    }

    // "PC 켤 때 자동 실행" 체크 메뉴 (바 메뉴와 아이콘 메뉴에 같이 쓴다)
    ToolStripMenuItem ClockItem()
    {
        var it = new ToolStripMenuItem("게임 화면에 시계 보기") { Checked = settings.ClockOn };
        it.ToolTipText = "시계는 끌어서 옮기고, 오른쪽 버튼으로 초·날짜를 고를 수 있어요";
        it.Click += (_, _) => { settings.ClockOn = !settings.ClockOn; settings.Save(); SyncClock(); };
        return it;
    }

    // 시계는 게임이 화면에 있거나 바가 보일 때 띄운다 (Ctrl+Alt+M 등으로 직접 숨기면 시계도 숨긴다)
    void SyncClock()
    {
        if (clock == null) return;
        var game = settings.ClockOn ? Game.Area() : null;
        bool want = settings.ClockOn && !userHidden && (game != null || (Visible && WindowState != FormWindowState.Minimized && Opacity > 0));
        clock.Sync(want, game);
    }

    ToolStripMenuItem AutoStartItem()
    {
        var it = new ToolStripMenuItem("PC 켤 때 자동 실행") { Checked = AutoStart.On };
        it.ToolTipText = "켜 두면 PC를 켤 때 메이플 바가 화면 오른쪽 아래 아이콘으로 시작해요";
        bool busy = false;
        it.Click += async (_, _) =>
        {
            if (busy) return;
            busy = true;
            bool want = !AutoStart.On, ok;
            // 작업 스케줄러 명령이 끝날 때까지 화면이 멈추지 않게 뒤에서 돌린다
            // 뜻밖의 오류가 나도 앱이 꺼지지 않게 기록만 남기고 "바꾸지 못했어요"로 안내한다
            try { ok = await Task.Run(() => AutoStart.Set(want)); }
            catch (Exception ex) { ok = false; Log.Write("바", "자동 실행 설정을 바꾸지 못함", ex); }
            finally { busy = false; }
            if (ok) { if (!it.IsDisposed) it.Checked = want; }
            else MessageBox.Show(want && !AutoStart.Installed ? "설치한 메이플 바에서 켜 주세요." : "자동 실행 설정을 바꾸지 못했어요. 잠시 뒤 다시 해 주세요.", "메이플 바");
        };
        return it;
    }

    void ShowAll(bool auto = false)
    {
        lastActive = DateTime.Now; // 단축키로 불러오면 펼친 채로 시작한다
        shown = 1; ApplyRegion();
        if (peek != null) peek.Visible = false;
        noActivate = auto;
        Show();
        noActivate = false;
        JanusWatch.BarHidden = false;
        musicDock?.SetShown(musicPill?.Visible == true);
        UpdateLyrics(lastMusic);
        autoHidden = false;
        if (auto) return;
        userHidden = false;
        keepShown = !Game.Running();
        Activate();
        search.Focus();
        search.SelectAll();
        if (settings.JanusChanged) BeginInvoke(JanusNotice);
    }

    /// <summary>예전 버전에서 솔 야누스 알림을 쓰던 사람에게 옵션이 바뀌었다고 한 번만 알린다.
    /// 게임 중 갑자기 뜨지 않게, 바가 보일 때(처음 켤 때 · 직접 불러올 때)만 띄운다.</summary>
    void JanusNotice()
    {
        if (!settings.JanusChanged || JanusWatch.BarHidden || !Visible || Opacity < 1) return;
        settings.JanusChanged = false;
        settings.JanusNoticeSeen = true;
        settings.Save();
        if (AskBox.Show(this, "솔 야누스 알림이 바뀌었어요",
                "지속시간 대신 알림 주기를 직접 넣어요. 5석펫 설정은 없어졌어요. 설정을 확인해 주세요.", "설정 열기", "닫기", enterYes: true)
            && janus != null)
            JanusForm.Open(this, settings, janus, sc);
    }

    // ── 창 처리 ──

    void DragMove(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        ReleaseCapture();
        dragging = true;
        SendMessage(Handle, 0xA1 /*WM_NCLBUTTONDOWN*/, (IntPtr)2 /*HTCAPTION*/, IntPtr.Zero); // 놓을 때까지 돌아오지 않는다
        dragging = false;
        guide?.Hide();
        // 게임 화면 안에 놓으면 그 자리를 게임 기준으로 기억하고 따라다닌다. 밖에 놓으면 따라다니지 않는다
        if (Game.Area() is Rectangle g)
        {
            var center = new Point(Left + Width / 2, Top + S(10));
            settings.GameFollow = g.Contains(center);
            if (settings.GameFollow)
            {
                settings.GameDX = (center.X - (g.Left + g.Width / 2)) / (double)g.Width;
                settings.GameDY = (Top - g.Top) / (double)g.Width;
            }
            settings.Save();
        }
    }

    [StructLayout(LayoutKind.Sequential)] struct MoveRect { public int L, T, R, B; }
    DragGuide? guide;

    /// <summary>끄는 동안: 게임 화면 가운데에 세로선을 보이고, 가까이 오면 가운데에 딱 붙인다.
    /// 게임 화면 밖이면(놓아도 따라다니지 않는 자리) 선을 숨긴다.</summary>
    void OnDragMoving(IntPtr lp)
    {
        var r = Marshal.PtrToStructure<MoveRect>(lp);
        // 바가 게임 창 위쪽 테두리 근처에 걸쳐 있어도 보이게 넉넉히 본다
        int mx = (r.L + r.R) / 2;
        if (Game.Area() is not Rectangle g || mx < g.Left || mx > g.Right || r.B < g.Top - S(60) || r.T > g.Bottom) { guide?.Hide(); return; }
        int gc = g.Left + g.Width / 2, cx = (r.L + r.R) / 2;
        bool on = Math.Abs(cx - gc) <= S(14);
        if (on && cx != gc) { int d = gc - cx; r.L += d; r.R += d; Marshal.StructureToPtr(r, lp, false); }
        guide ??= new DragGuide();
        guide.Place(gc, r.B + S(4), Math.Min(g.Bottom, r.B + S(70)), Math.Max(2, S(2)), on);
    }

    // ── 메이플 창 따라가기 ──
    bool dragging;
    readonly System.Windows.Forms.Timer gameTimer = new() { Interval = 500 };

    Point GameAnchor(Rectangle g)
    {
        double dx = settings.GameDX ?? 0, dy = settings.GameDY ?? 6 / 1920.0;
        int cx = g.Left + g.Width / 2 + (int)Math.Round(dx * g.Width);
        return new Point(cx - Width / 2, g.Top + (int)Math.Round(dy * g.Width));
    }

    void FollowGame()
    {
        if (dragging || !Visible || WindowState == FormWindowState.Minimized || musicDock?.Floating == true && MouseButtons != MouseButtons.None) return;
        if (Game.Area() is not Rectangle g) return;
        // 게임 해상도가 바뀌면 크기를 다시 잡기 위해 바를 새로 띄운다 (쓰는 중이면 손을 뗄 때까지 기다린다)
        if (Math.Abs(Game.ZoomFor(g, DeviceDpi) - UI.Z) > 0.03f)
        {
            if (!InUse() && MouseButtons == MouseButtons.None) { Program.RestartRequested = true; Close(); }
            return;
        }
        if (!settings.GameFollow) return;
        var p = GameAnchor(g);
        if (p != Location) Location = p;
    }

    bool fitPending;
    void LayoutBar()
    {
        int rowH = ClientSize.Height - Padding.Vertical;
        foreach (Control c in left.Controls) UI.CenterV(c, rowH);
        foreach (Control c in right.Controls) UI.CenterV(c, rowH);
        int w = left.PreferredSize.Width + right.PreferredSize.Width + S(24) + Padding.Horizontal;
        if (w == Width) return;
        // 폭이 바뀌어도 바의 가운데는 그대로 둔다
        if (IsHandleCreated) SetBounds(Left - (w - Width) / 2, Top, w, Height);
        else Width = w;
    }

    JanusWatch? janus; // 솔 야누스·룬 알림 (키 상태만 읽는다)
    PendantCheck? pendant; // 고른 버프가 도는 동안 maple.gg에서 정령의 펜던트를 확인한다
    StudyRun? study;       // 자기계발 카드를 정한 주기마다 띄운다
    ClockForm? clock; // 게임 화면 위 시계
    readonly System.Windows.Forms.Timer chipTimer = new() { Interval = 250 };

    // 타이머가 도는 동안 바 오른쪽 앞에 아이콘과 남은 시간을 띄운다
    void AddTimerChips(JanusWatch w)
    {
        var janusChip = new TimerChip(TimerChip.Kind.Janus, sc) { Key = "janus", Visible = false, Margin = new Padding(S(2), 0, S(2), 0) };
        var runeChip = new TimerChip(TimerChip.Kind.Rune, sc) { Key = "rune", Visible = false, Margin = new Padding(S(2), 0, S(6), 0) };
        // 칩을 누르면 그 타이머를 취소한다 (설정은 톱니 메뉴에서)
        janusChip.Click += (_, _) => { w.CancelJanus(); if (janusChip.SetTime(null)) LayoutBar(); };
        runeChip.Click += (_, _) => { w.CancelRune(); if (runeChip.SetTime(null)) LayoutBar(); };
        // 버프 칸마다 칩 하나
        var buffChips = new TimerChip[JanusWatch.BuffSlots];
        for (int i = 0; i < buffChips.Length; i++)
        {
            int slot = i;
            var bc = buffChips[i] = new TimerChip(TimerChip.Kind.Buff, sc) { Key = "buff" + i, Visible = false, Margin = new Padding(S(2), 0, S(2), 0) };
            bc.Click += (_, _) => { w.CancelBuff(slot); if (bc.SetTime(null)) LayoutBar(); };
        }
        // 도핑 칸마다 칩 하나 
        var dopeChips = new TimerChip[JanusWatch.DopeSlots];
        for (int i = 0; i < dopeChips.Length; i++)
        {
            int slot = i;
            var dc = dopeChips[i] = new TimerChip(TimerChip.Kind.Buff, sc) { Key = "dope" + i, Visible = false, Margin = new Padding(S(2), 0, S(2), 0), BuffTint = JanusWatch.DopeColors(i).main };
            dc.Click += (_, _) => { w.CancelDope(slot); if (dc.SetTime(null)) LayoutBar(); };
        }
        var erdaChip = new TimerChip(TimerChip.Kind.Buff, sc) { Key = "erda", Visible = false, Margin = new Padding(S(2), 0, S(2), 0), BuffTint = JanusWatch.ErdaMain };
        erdaChip.Click += (_, _) => { w.CancelErda(); if (erdaChip.SetTime(null)) LayoutBar(); };
        // 전체 알림을 꺼 두면 맨 앞에 OFF 칩 (누르면 다시 켠다)
        var offChip = new TimerChip(TimerChip.Kind.Off, sc) { Key = "off", Visible = false, Margin = new Padding(S(2), 0, S(2), 0) };
        offChip.Click += (_, _) => { settings.AlertsOn = true; settings.Save(); if (offChip.SetShown(false)) LayoutBar(); };
        foreach (var c in new[] { runeChip, janusChip, erdaChip }.Concat(buffChips).Concat(dopeChips).Append(offChip))
        {
            right.Controls.Add(c);
            right.Controls.SetChildIndex(c, 0);
        }
        // 칩을 끌어서 바에서 떼어 내거나 다시 붙인다 (떼어 둔 자리는 저장)
        // 바가 보이면 보이고, 직접 숨겼을 때는 설정에 따라 남긴다 (게임을 꺼서 저절로 숨으면 같이 숨는다)
        var dock = chipDock = new ChipDock(settings, this, sc, LayoutBar, () => Visible || (userHidden && !settings.ChipsHideWithBar));
        foreach (var c in new[] { runeChip, janusChip, erdaChip }.Concat(buffChips).Concat(dopeChips).Append(offChip)) dock.Add(c);
        dock.Restore();
        chipTimer.Tick += (_, _) =>
        {
            bool changed = janusChip.SetTime(w.Remaining);
            changed |= runeChip.SetTime(w.RuneRemaining, w.RuneReady);
            changed |= erdaChip.SetTime(w.ErdaRemaining);
            for (int i = 0; i < buffChips.Length; i++)
            {
                buffChips[i].BuffTint = JanusWatch.BuffColors(w.BuffMin(i)).main;
                changed |= buffChips[i].SetTime(w.BuffRemaining(i));
            }
            for (int i = 0; i < dopeChips.Length; i++) changed |= dopeChips[i].SetTime(w.DopeRemaining(i));
            changed |= offChip.SetShown(!settings.AlertsOn);
            if (changed) LayoutBar();
            dock.Sync();
        };
        chipTimer.Start();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        LayoutBar();
        Location = InitialLocation();
        gameTimer.Tick += (_, _) => { CheckGame(); FollowGame(); SyncClock(); };
        PrepareHiddenStart();
        AutoStart.Init();
        ListenForSecondLaunch();
        gameTimer.Start();
        janus = new JanusWatch(settings, sc);
        AddTimerChips(janus);
        pendant = new PendantCheck(settings, janus, sc);
        study = new StudyRun(settings, sc);
        clock =new ClockForm(settings, sc);
        search.Focus();
        keepTop.Tick += (_, _) => StayOnTop();
        keepTop.Start();
        MakePeek();
        foldTimer.Tick += (_, _) => FoldTick();
        foldTimer.Start();
        StartInvenWatch();
        UpdateCheck.StartWatching(); // 켜져 있는 동안 새 버전이 나오면 물어본다
        if (!Program.Rebuilt) RunArgs(Environment.GetCommandLineArgs().Skip(1).ToArray());
        if (Program.ReopenSettingsTab is int tab)
        {
            Program.ReopenSettingsTab = null;
            BeginInvoke(() => OpenProgramSettings(tab));
        }
        Logins.RefreshSoon(); // 로그인 여부를 일찍 알아 둔다
    }

    /// <summary>글자 왼쪽에 빨간 둥근 칸 속 흰 재생 세모를 그린다 (유튜브 메뉴)</summary>
    void AddPlayIcon(Label nav)
    {
        int h = (int)Math.Round(nav.Font.Height * 0.62f), w = (int)Math.Round(h * 1.42f), gap = S(5);
        var p = nav.Padding;
        int textLeft = p.Left;
        nav.Padding = new Padding(p.Left + w + gap, p.Top, p.Right, p.Bottom);
        nav.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float x = textLeft, y = p.Top + (nav.Height - p.Top - p.Bottom - h) / 2f;
            using (var path = UI.RoundRect(new RectangleF(x, y, w, h), h * 0.3f))
            using (var red = new SolidBrush(Color.FromArgb(255, 0, 0)))
                g.FillPath(red, path);
            float cx = x + w / 2f, cy = y + h / 2f, t = h * 0.42f;
            g.FillPolygon(Brushes.White, new[]
            {
                new PointF(cx - t * 0.42f, cy - t / 2f), new PointF(cx - t * 0.42f, cy + t / 2f), new PointF(cx + t * 0.5f, cy),
            });
        };
    }

    /// <summary>처음 놓을 자리: 저장된 자리, 없으면 주 화면 위 가운데. 게임 따라가기가 켜져 있으면 게임 창 기준.</summary>
    Point InitialLocation()
    {
        var wa = Screen.PrimaryScreen!.WorkingArea;
        var pos = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + S(6));
        // 가운데 좌표로 저장해 두었다가 되살린다 (예전처럼 왼쪽 끝을 저장하면 브금 칸이 붙을 때마다 조금씩 왼쪽으로 밀린다)
        if (settings.BarY is int by)
        {
            if (settings.BarCX is int cx && Screen.AllScreens.Any(s => s.WorkingArea.Contains(cx, by + 10)))
                pos = new Point(cx - Width / 2, by);
            else if (wa.Contains(wa.Left + wa.Width / 2, by + 10))
                pos = new Point(pos.X, by); // 예전 저장값은 가로를 화면 가운데로 되돌린다
        }
        if (settings.GameFollow && Game.Area() is Rectangle game) pos = GameAnchor(game);
        return pos;
    }

    void PrepareHiddenStart()
    {
        // PC를 켤 때 자동으로 실행됐으면 바를 보이지 않게 띄웠다가 곧바로 아이콘으로 넣는다
        if (Program.StartHidden) { Opacity = 0; noActivate = true; }
        Shown += (_, _) =>
        {
            if (Program.StartHidden)
            {
                Program.StartHidden = false;
                noActivate = false;
                autoHintShown = true; // PC를 켤 때는 안내 풍선을 띄우지 않는다
                HideAll(auto: true);  // 메이플스토리를 켜면 저절로 나온다
                Opacity = 1;
            }
            // 처음 켤 때 게임이 없으면 바로 숨긴다
            BeginInvoke(CheckGame);
            // 바가 그대로 보이면 예전 야누스 사용자에게 바뀐 점을 알린다
            if (settings.JanusChanged) BeginInvoke(JanusNotice);
        };
    }

    RegisteredWaitHandle? showWait;

    void ListenForSecondLaunch()
    {
        // 메이플 바를 또 실행하면 (숨어 있던) 바를 앞으로 불러온다
        if (Program.ShowEvent is { } ev)
            showWait = ThreadPool.RegisterWaitForSingleObject(ev, (_, _) =>
            {
                if (IsDisposed || !IsHandleCreated) return;
                try { BeginInvoke(() => { WindowState = FormWindowState.Normal; ShowAll(); }); }
                catch (Exception ex) { Log.Write("바", "두 번째 실행 신호로 바를 못 불러옴", ex); }
            }, null, -1, executeOnlyOnce: false);
    }

    void MakePeek()
    {
        // 접혔을 때 남는 주황 띠
        peek = new Panel { Bounds = new Rectangle(0, 0, Width, PeekH), BackColor = UI.Accent, Visible = false, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        Controls.Add(peek);
        peek.BringToFront();
    }

    void StartInvenWatch()
    {
        inven = new InvenWatcher(settings, (tag, post) =>
        {
            // 바를 숨겨도 "바를 숨겨도 알림 받기"가 켜져 있으면 띄운다 (게임 화면 위 가운데에)
            if ((!Visible || WindowState == FormWindowState.Minimized) && !settings.AlertWhenHidden) return;
            NoticeStrip.Show(this, tag == "새 글" ? 0 : 1, tag, post.Title, post.Category, () => Navigate(post.Url), sc);
        });
    }

    // 다른 '항상 위' 창(사이트 창, 다른 프로그램)이 바를 덮지 않게 계속 맨 위로 올린다
    readonly System.Windows.Forms.Timer keepTop = new() { Interval = 500 };
    InvenWatcher? inven;
    ChipDock? chipDock;

    void StayOnTop()
    {
        if (menusOpen > 0 || !Visible || WindowState == FormWindowState.Minimized || !IsHandleCreated) return;
        SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_KEEP | SWP_NOOWNERZORDER);
        musicDock?.RaiseCard();
        if (lyrics is { Visible: true, IsHandleCreated: true }) SetWindowPos(lyrics.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_KEEP | SWP_NOOWNERZORDER);
        chipDock?.RaiseAll(); // 떼어 낸 알림은 바보다 위에
    }

    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e);
        HideMega();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        ApplyRegion();
    }

    // 보이는 부분만 둥근 모양으로 남긴다 (접힐 때는 아래쪽부터 잘려서 위로 말려 올라간다)
    void ApplyRegion()
    {
        int h = Math.Max(PeekH, (int)Math.Round(Height * shown));
        // 다 접히면 가운데 짧은 손잡이만 남긴다 (나머지는 마우스가 그대로 뒤로 지나간다)
        var rgn = shown <= 0
            ? CreateRoundRectRgn(PeekRect.Left, 0, PeekRect.Right + 1, PeekH + 1, PeekH, PeekH)
            : CreateRoundRectRgn(0, 0, Width + 1, h + 1, S(14), S(14));
        if (peek != null) peek.Bounds = PeekRect;
        var oldRegion = Region;
        Region = Region.FromHrgn(rgn);
        oldRegion?.Dispose(); // 창은 새 모양을 쓰므로 지난 모양은 치운다
        DeleteObject(rgn);
    }

    // ── 자동 접기: 마우스가 3초 동안 바에 없으면 접고, 남은 띠에 마우스를 대면 펼친다 ──
    // 고정핀: 꽂혀 있으면 바를 늘 펼쳐 두고, 빼 두면 3초 뒤에 접는다
    Panel? pin;
    readonly ToolTip pinTip = new();
    void SetPinned(bool pinned)
    {
        settings.BarAutoHide = !pinned;
        settings.Save();
        lastActive = DateTime.Now;
        if (pin != null) { pin.Invalidate(); pinTip.SetToolTip(pin, PinText); }
    }
    string PinText => settings.BarAutoHide ? "고정핀: 꺼짐 (안 쓰면 3초 뒤 접혀요 · 누르면 고정)" : "고정핀: 켜짐 (늘 펼쳐 둬요 · 누르면 해제)";

    Panel MakePin()
    {
        var p = new Panel { Size = new Size(S(28), S(28)), Cursor = Cursors.Hand, BackColor = UI.Back, Margin = new Padding(0, 0, S(2), 0) };
        bool hot = false;
        p.MouseEnter += (_, _) => { hot = true; p.Invalidate(); };
        p.MouseLeave += (_, _) => { hot = false; p.Invalidate(); };
        p.Click += (_, _) => SetPinned(settings.BarAutoHide);
        pinTip.SetToolTip(p, PinText);
        p.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            bool on = !settings.BarAutoHide;
            float cx = p.Width / 2f, cy = p.Height / 2f, u = S(5);
            if (on || hot)
                using (var b = new SolidBrush(Color.FromArgb(on ? 40 : 25, UI.Accent))) g.FillEllipse(b, cx - S(12), cy - S(12), S(24), S(24));
            var col = on ? UI.Accent : UI.T(Color.FromArgb(165, 150, 135));
            // 꽂혀 있으면 똑바로, 빼 두면 비스듬히 눕힌다
            g.TranslateTransform(cx, cy);
            if (!on) g.RotateTransform(45);
            using var b2 = new SolidBrush(col);
            using var pen = new Pen(col, S(2)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.FillRectangle(b2, -u * 0.75f, -u * 1.5f, u * 1.5f, u * 0.45f);                 // 머리 윗판
            g.FillPolygon(b2, new[] { new PointF(-u * 0.5f, -u * 1.1f), new PointF(u * 0.5f, -u * 1.1f),
                                      new PointF(u * 0.95f, u * 0.2f), new PointF(-u * 0.95f, u * 0.2f) }); // 몸통
            g.DrawLine(pen, 0, u * 0.25f, 0, u * 1.55f);                                          // 바늘
            g.ResetTransform();
        };
        return p;
    }

    int PeekH => S(6);
    Rectangle PeekRect => new((Width - S(120)) / 2, 0, S(120), PeekH);
    /// <summary>지금 화면에 보이는 바의 아래쪽 (접히면 손잡이 아래). 알림이 이 아래에 붙는다.</summary>
    public int VisibleBottom => Top + Math.Max(PeekH, (int)Math.Round(Height * shown));
    // 알림이 뜰 자리: 펼친 메뉴가 있으면 그 아래, 없으면 바 아래
    public int NoticeTop => openMega is { Visible: true } m ? Math.Max(VisibleBottom, m.Bottom) : VisibleBottom;
    double shown = 1;          // 보이는 비율 (1 = 다 펼침)
    DateTime lastActive = DateTime.Now;
    readonly System.Windows.Forms.Timer foldTimer = new() { Interval = 15 };
    Panel? peek;

    [DllImport("imm32.dll")] static extern bool ImmAssociateContextEx(IntPtr hWnd, IntPtr hIMC, uint flags);
    [DllImport("imm32.dll")] static extern IntPtr ImmGetContext(IntPtr hWnd);
    [DllImport("imm32.dll")] static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);
    [DllImport("imm32.dll")] static extern bool ImmGetConversionStatus(IntPtr hIMC, out uint conv, out uint sent);
    [DllImport("imm32.dll")] static extern bool ImmSetConversionStatus(IntPtr hIMC, uint conv, uint sent);
    bool? imeHangul; // 검색칸에서 마지막으로 쓰던 상태 (모르면 건드리지 않음)

    void ImeRemember(IntPtr h)
    {
        var c = ImmGetContext(h);
        if (c == IntPtr.Zero) return;
        if (ImmGetConversionStatus(c, out uint conv, out _)) imeHangul = (conv & 1) != 0; // IME_CMODE_NATIVE
        ImmReleaseContext(h, c);
    }

    void ImeRestore(IntPtr h)
    {
        ImmAssociateContextEx(h, IntPtr.Zero, 0x10); // IACE_DEFAULT: 기본 입력기를 다시 붙인다
        if (imeHangul is not bool want) return;
        var c = ImmGetContext(h);
        if (c == IntPtr.Zero) return;
        if (ImmGetConversionStatus(c, out uint conv, out uint sent) && ((conv & 1) != 0) != want)
            ImmSetConversionStatus(c, want ? conv | 1 : conv & ~1u, sent);
        ImmReleaseContext(h, c);
    }

    bool InUse()
    {
        var cur = Cursor.Position;
        // 접혀 있으면 손잡이 근처만, 펼쳐져 있으면 바 전체 (조금 넉넉하게)
        var vis = shown <= 0
            ? new Rectangle(Left + PeekRect.Left - S(20), Top - S(8), PeekRect.Width + S(40), PeekH + S(16))
            : new Rectangle(Left, Top, Width, Math.Max(PeekH + S(4), (int)(Height * shown)));
        if (shown >= 1) vis.Inflate(S(6), S(6));
        if (vis.Contains(cur)) return true;
        if (openMega != null || menusOpen > 0) return true;
        if (ContainsFocus && search.Focused && search.TextLength > 0) return true; // 닉네임을 치는 중
        // 바에서 띄운 소리 조절기·곡 목록 위에 마우스가 있으면 쓰는 중
        foreach (Form f in Application.OpenForms)
            if (f.Visible && f is VolumeDial or MusicPicker && f.Bounds.Contains(cur)) return true;
        return false;
    }

    void FoldTick()
    {
        if (!Visible || !IsHandleCreated) return;
        double target = 1;
        if (settings.BarAutoHide)
        {
            if (InUse()) lastActive = DateTime.Now;
            else if ((DateTime.Now - lastActive).TotalSeconds >= 3) target = 0;
        }
        if (Math.Abs(shown - target) < 0.001) return;
        if (target == 0 && shown >= 1) HideMega();
        // 펼칠 때는 빠르게, 접을 때는 조금 천천히
        shown += (target - shown) * (target > shown ? 0.35 : 0.18);
        if (Math.Abs(shown - target) < 0.02) shown = target;
        if (peek != null) peek.Visible = shown <= 0;
        ApplyRegion();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= CS_DROPSHADOW; // 그림자
            cp.ExStyle |= WS_EX_TOOLWINDOW; // 알탭에 안 나오게
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        RegisterHotKey(Handle, HotkeyId, MOD_CONTROL | MOD_ALT, (uint)Keys.M);
        MakeTray();
    }

    // 작업 표시줄 오른쪽 아래(알림 영역) 아이콘: 두 번 누르면 바를 열거나 숨긴다 (Ctrl+Alt+M과 같다)
    NotifyIcon? tray;
    ToolStripMenuItem? trayAlertItem, trayAutoStartItem; // 트레이 메뉴의 체크 항목 (열 때마다 다시 맞춘다)

    void MakeTray()
    {
        if (tray != null) return;
        var menu = new ContextMenuStrip();
        menu.Items.Add("바 열기 / 숨기기 (Ctrl+Alt+M)", null, (_, _) => ToggleBar());
        trayAlertItem = AlertWhenHiddenItem();
        menu.Items.Add(trayAlertItem);
        // 바 메뉴에서 바꾼 값도 맞게 보이도록 열 때마다 체크를 다시 맞춘다
        trayAutoStartItem = AutoStartItem();
        menu.Items.Add(trayAutoStartItem);
        menu.Opening += (_, _) =>
        {
            if (trayAlertItem != null) trayAlertItem.Checked = settings.AlertWhenHidden;
            if (trayAutoStartItem != null) trayAutoStartItem.Checked = AutoStart.On;
        };
        menu.Items.Add("메이플 바 끄기", null, (_, _) => Close());
        tray = new NotifyIcon
        {
            Icon = Icon ?? SystemIcons.Application,
            Text = "메이플 바 · 두 번 누르면 열기/숨기기",
            ContextMenuStrip = menu,
            Visible = true,
        };
        tray.MouseDoubleClick += (_, e) => { if (e.Button == MouseButtons.Left) ToggleBar(); };
    }

    void ToggleBar()
    {
        if (Visible && WindowState != FormWindowState.Minimized) HideAll();
        else { WindowState = FormWindowState.Normal; ShowAll(); }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0312 /*WM_HOTKEY*/ && m.WParam == (IntPtr)HotkeyId)
        {
            ToggleBar();
            return;
        }
        if (m.Msg == 0x0216 /*WM_MOVING*/ && dragging) { OnDragMoving(m.LParam); m.Result = (IntPtr)1; return; }
        base.WndProc(ref m);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        settings.BarX = Left;
        settings.BarY = Top;
        settings.BarCX = Left + Width / 2;
        settings.Save();
        UnregisterHotKey(Handle, HotkeyId);
        if (tray != null) { tray.Visible = false; tray.Dispose(); tray = null; }
        clock?.Close(); clock = null;
        guide?.Close(); guide = null;
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        // 바를 다시 만들 때 옛 바의 일이 계속 돌지 않게 멈춘다
        showWait?.Unregister(null);
        foreach (var t in new[] { gameTimer, chipTimer, keepTop, foldTimer, megaTimer }) { t.Stop(); t.Dispose(); }
        janus?.Stop();
        pendant?.Dispose(); pendant = null;
        study?.Dispose(); study = null;
        inven?.Stop();
        lyrics?.Close(); lyrics = null;
        musicDock?.Dispose(); musicDock = null;
        // 바에 딸린 작은 창(뮤직 카드·곡 목록·소리 조절 등)은 닫는다. 사이트 창·뮤직 창은 그대로 둔다
        if (Program.RestartRequested)
        {
            NoticeStrip.CloseAll();
            foreach (var f in Application.OpenForms.Cast<Form>().ToList())
                if (f != this && f is not WebWindow && f is not UpdateWindow && f.Owner is not WebWindow) f.Close();
        }
        base.OnFormClosed(e);
    }

    static void DrawMagnifier(Graphics g, Rectangle r, Color c)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float s = Math.Min(r.Width, r.Height) * 0.62f;
        float x = r.X + (r.Width - s) / 2f, y = r.Y + (r.Height - s) / 2f;
        float d = s * 0.66f, w = Math.Max(1.6f, s / 8f);
        using var pen = new Pen(c, w) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawEllipse(pen, x, y, d, d);
        float k = d * 0.85f; // 원 오른쪽 아래에서 손잡이가 나간다
        g.DrawLine(pen, x + k, y + k, x + s, y + s);
    }

    void PaintSearchBorder(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(0, 0, searchBox.Width - 1, searchBox.Height - 1);
        int d = S(10);
        using var path = UI.RoundRect(r, d / 2f); // 지름이 d가 되는 반지름
        using var pen = new Pen(search.Focused ? UI.Accent : UI.Line, Math.Max(1, S(1)));
        g.DrawPath(pen, path);
    }
}
