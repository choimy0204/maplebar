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

    public BarForm()
    {
        // Windows 배율(DPI)에 더해 모니터 가로 해상도에 맞춰 크기와 글꼴을 같이 키우거나 줄인다
        // 메이플이 켜져 있으면 게임 화면 가로를, 없으면 모니터 가로를 기준으로 한다
        UI.Z = Game.Area() is Rectangle game ? Game.ZoomFor(game, DeviceDpi) : UI.FitZoom(Screen.PrimaryScreen!, DeviceDpi);
        sc = DeviceDpi / 96f * UI.Z;
        Text = "메이플 바";
        FormBorderStyle = FormBorderStyle.None;
        TopMost = true;
        ShowInTaskbar = true;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        StartPosition = FormStartPosition.Manual;
        Font = new Font("맑은 고딕", 10.5f * UI.Z, FontStyle.Bold);
        BackColor = UI.Line;          // 1px 테두리 색
        Padding = new Padding(1);
        Height = S(56);

        left = MakeFlow(DockStyle.Left);
        right = MakeFlow(DockStyle.Right);
        var filler = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
        filler.MouseDown += DragMove;
        Controls.Add(left);
        Controls.Add(right);
        Controls.Add(filler);
        filler.BringToFront();

        // ── 왼쪽: 이동 손잡이, 로고, 메뉴 ──
        var grip = new Label
        {
            Text = "⠿", AutoSize = true, Cursor = Cursors.SizeAll,
            ForeColor = Color.FromArgb(170, 170, 170), Font = new Font("Segoe UI Symbol", 12f * UI.Z),
            Margin = new Padding(0, 0, S(4), 0),
        };
        grip.MouseDown += DragMove;
        left.Controls.Add(pin = MakePin());
        left.Controls.Add(grip);

        foreach (var top in SiteMenu.Top)
        {
            if (top.Name == "직접입력") continue; // 바에서는 안 쓴다

            Label nav = null!;
            if (top.Items == null)
            {
                nav = UI.Button(top.Name, "", () => Navigate(WithName(top.Path!)), sc);
                nav.MouseEnter += (_, _) => HideMega();
            }
            else
            {
                var mega = new MegaMenuForm(top.Items, sc, p => Navigate(WithName(p))) { Owner = this };
                nav = UI.Button(top.Name, "", () => OpenMega(mega, nav), sc);
                nav.MouseEnter += (_, _) => OpenMega(mega, nav);
            }
            nav.Margin = new Padding(S(6), 0, S(6), 0);
            left.Controls.Add(nav);
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

        searchBox = new Panel { Size = new Size(S(230), S(36)), BackColor = Color.White, Margin = new Padding(S(10), 0, S(4), 0) };
        searchBox.Paint += PaintSearchBorder;
        search.BorderStyle = BorderStyle.None;
        search.PlaceholderText = "닉네임을 입력하세요";
        // 메이플에서 넘어오면 입력기가 꺼진 채로 남아 한/영이 안 먹을 때가 있다.
        // 칸에 들어오면 입력기를 다시 붙이고, 마지막에 쓰던 한글/영문 상태로 돌려놓는다
        search.GotFocus += (_, _) => ImeRestore(search.Handle);
        search.LostFocus += (_, _) => ImeRemember(search.Handle);
        search.Font = new Font("맑은 고딕", 10.5f * UI.Z);
        search.Width = searchBox.Width - S(44);
        search.Location = new Point(S(12), (searchBox.Height - search.PreferredHeight) / 2);
        search.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Search(SiteMenu.InfoPath); }
            else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; search.Clear(); }
        };
        var searchIcon = UI.Button("🔍", "캐릭터정보 검색 (Enter)", () => Search(SiteMenu.InfoPath), sc);
        searchIcon.Font = new Font("Segoe UI Emoji", 10f * UI.Z);
        searchIcon.Padding = new Padding(S(2));
        searchIcon.Location = new Point(searchBox.Width - S(34), (searchBox.Height - searchIcon.PreferredSize.Height) / 2);
        searchBox.Controls.Add(search);
        searchBox.Controls.Add(searchIcon);
        searchBox.Click += (_, _) => search.Focus();
        search.GotFocus += (_, _) => searchBox.Invalidate();
        search.LostFocus += (_, _) => searchBox.Invalidate();
        right.Controls.Add(searchBox);


        recentChip = UI.Button("", "최근 검색한 캐릭터", ShowRecentMenu, sc,
            fore: Color.FromArgb(150, 80, 0), back: Color.FromArgb(255, 243, 224));
        recentChip.Margin = new Padding(S(8), 0, S(4), 0);
        right.Controls.Add(recentChip);
        UpdateRecentChip();

        Label? gear = null;
        gear = UI.Button("⚙", "설정", () => ShowSettingsMenu(gear!), sc);
        right.Controls.Add(gear);
        right.Controls.Add(UI.Button("—", "바 숨기기 (Ctrl+Alt+M으로 다시 열기)", HideAll, sc));
        right.Controls.Add(UI.Button("✕", "종료 (열린 창도 모두 닫힘)", Close, sc));
    }

    // ── 뮤직: 메애기 창은 숨겨 두고 바에서 곡 제목·진행 바·⏮ ⏯ ⏭만 보여 준다 ──
    MusicPill? musicPill;
    MusicDock? musicDock;

    void AddMusicControls()
    {
        musicPill = new MusicPill(sc) { Visible = false, Margin = new Padding(S(4), 0, S(6), 0) };
        musicPill.TitleClick += () => MusicPicker.Open(this, settings, sc, false);
        var menu = UI.Rounded(new ContextMenuStrip { Font = new Font("맑은 고딕", 9.5f * UI.Z), ShowImageMargin = false });
        menu.Items.Add("인기 TOP 100", null, (_, _) => MusicPicker.Open(this, settings, sc, false));
        menu.Items.Add("브금 검색", null, (_, _) => MusicPicker.Open(this, settings, sc, true));
        menu.Items.Add("메애기 창 보기/숨기기", null, (_, _) => WebWindow.ToggleMusicWindow());
        var dockBack = menu.Items.Add("바에 다시 넣기", null, (_, _) => musicDock?.DockBack());
        menu.Opening += (_, _) => dockBack.Visible = musicDock?.Floating == true;
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("뮤직 끄기", null, (_, _) => WebWindow.StopMusic());
        musicPill.ContextMenuStrip = menu;
        right.Controls.Add(musicPill);
        musicDock = new MusicDock(this, right, musicPill, settings, sc, LayoutBar, () => new Rectangle(Left, Top, Width, Math.Max(S(10), VisibleBottom - Top)));
        WebWindow.MusicChanged += OnMusicChanged;
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
            Dock = dock, AutoSize = true, WrapContents = false, BackColor = Color.White,
            Padding = new Padding(S(8), 0, S(8), 0),
        };
        f.MouseDown += DragMove;
        return f;
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
        var name = search.Text.Trim();
        return SiteMenu.NamePages.Contains(path) && name.Length > 0
            ? path + "?name=" + Uri.EscapeDataString(name)
            : path;
    }

    void Search(string path)
    {
        var name = search.Text.Trim();
        if (name.Length == 0)
        {
            search.Focus();
            searchBox.BackColor = Color.FromArgb(255, 240, 230);
            var t = new System.Windows.Forms.Timer { Interval = 400 };
            t.Tick += (_, _) => { searchBox.BackColor = Color.White; t.Dispose(); };
            t.Start();
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
        // 뮤직은 이미 열린 창에서 페이지만 바꾼다 (노래가 두 곳에서 겹쳐 나오지 않게)
        if (Music.IsMusic(path))
        {
            // 인기 차트·검색은 바의 곡 목록으로 고른다. 메애기 창은 뒤에 숨어서 재생만 한다.
            if (path == Music.Home) { MusicPicker.Open(this, settings, sc, false); return; }
            if (path == Music.Home + "/search") { MusicPicker.Open(this, settings, sc, true); return; }
            WebWindow.EnsureMusic(settings, sc, path, true); // 내 목록(로그인 필요)은 창으로 연다
            return;
        }
        try { new WebWindow(path.StartsWith("http") ? path : SiteMenu.Base + path, settings, sc).Show(); }
        catch (Exception ex)
        {
            MessageBox.Show(this, "창을 열지 못했어요.\n\n" + ex, "메이플 바", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    void ShowRecentMenu()
    {
        var cms = UI.Rounded(new ContextMenuStrip { Font = new Font("맑은 고딕", 10f * UI.Z), ShowImageMargin = false });
        if (settings.Recent.Count == 0)
        {
            cms.Items.Add(new ToolStripMenuItem("검색 기록이 없어요") { Enabled = false });
        }
        foreach (var name in settings.Recent)
        {
            var n = name;
            var mi = new ToolStripMenuItem(n);
            EventHandler openInfo = (_, _) => { search.Text = n; Search(SiteMenu.InfoPath); };
            mi.Click += openInfo;
            var info = new ToolStripMenuItem("캐릭터정보");
            info.Click += openInfo;
            var boss = new ToolStripMenuItem("보스컷");
            boss.Click += (_, _) => { search.Text = n; Search(SiteMenu.BossCutPath); };
            mi.DropDownItems.Add(info);
            mi.DropDownItems.Add(boss);
            cms.Items.Add(mi);
        }
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
        var cms = UI.Rounded(new ContextMenuStrip { Font = new Font("맑은 고딕", 10f * UI.Z), ShowImageMargin = false, ShowCheckMargin = true });
        var top = new ToolStripMenuItem("사이트 창 항상 위") { Checked = settings.WinTopMost };
        top.Click += (_, _) =>
        {
            settings.WinTopMost = !settings.WinTopMost;
            settings.Save();
            WebWindow.SetTopMost(settings.WinTopMost);
        };
        cms.Items.Add(top);
        var fx = new ToolStripMenuItem("곡 넘김 효과음 (레코드)") { Checked = settings.MusicFx };
        fx.Click += (_, _) =>
        {
            settings.MusicFx = !settings.MusicFx;
            settings.Save();
            WebWindow.SetMusicFx(settings.MusicFx);
        };
        cms.Items.Add(fx);
        var fold = new ToolStripMenuItem("바 고정 (끄면 3초 뒤 접힘)") { Checked = !settings.BarAutoHide };
        fold.Click += (_, _) => SetPinned(settings.BarAutoHide);
        cms.Items.Add(fold);
        var closeAll = new ToolStripMenuItem("사이트 창 모두 닫기") { Enabled = WebWindow.AnyOpen };
        closeAll.Click += (_, _) => WebWindow.CloseAll();
        cms.Items.Add(closeAll);

        // 인벤 자유게시판 알림: 체크한 목록에 새 글이 올라오면 화면 오른쪽 아래에 알려 준다
        cms.Items.Add(new ToolStripSeparator());
        cms.Items.Add(new ToolStripMenuItem("자유게시판 알림 (인벤)")
        {
            Enabled = false, Font = new Font("맑은 고딕", 9f * UI.Z, FontStyle.Bold),
        });
        const string WatchTag = "watch";
        foreach (var f in Inven.Feeds)
        {
            var key = f.Key;
            var mi = new ToolStripMenuItem(f.Label) { Checked = settings.InvenWatch.Contains(key), Tag = WatchTag };
            mi.Click += (_, _) =>
            {
                mi.Checked = !mi.Checked;
                settings.InvenWatch.Remove(key);
                if (mi.Checked) settings.InvenWatch.Add(key);
                settings.Save();
                inven?.PollNow();
            };
            cms.Items.Add(mi);
        }
        cms.Items.Add("인벤 로그인", null, (_, _) => Navigate(Inven.LoginUrl));
        cms.Items.Add(new ToolStripSeparator());
        cms.Items.Add("알림 설정 (야누스·룬·버프)…", null, (_, _) => { if (janus != null) JanusForm.Open(this, settings, janus, sc); });
        cms.Items.Add("웹툰 키 설정…", null, (_, _) => { if (janus != null) JanusForm.Open(this, settings, janus, sc, toon: true); });
        cms.Items.Add(ClockItem());
        // 알림 항목은 눌러도 메뉴를 닫지 않아서 여러 개를 바로 고를 수 있게 한다
        bool keepOpen = false;
        cms.ItemClicked += (_, e) => keepOpen = (e.ClickedItem?.Tag as string) == WatchTag;
        cms.Closing += (_, e) =>
        {
            if (keepOpen && e.CloseReason == ToolStripDropDownCloseReason.ItemClicked) e.Cancel = true;
            keepOpen = false;
        };

        cms.Items.Add(new ToolStripSeparator());
        cms.Items.Add("바 숨기기 (Ctrl+Alt+M)", null, (_, _) => HideAll());
        cms.Items.Add(AlertWhenHiddenItem());
        cms.Items.Add(AutoStartItem());
        ShowBelowBar(cms, anchor);
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

    // 시계는 게임이 화면에 있거나 바가 보일 때 띄운다 (바를 숨겨도 게임 중엔 보인다)
    void SyncClock()
    {
        if (clock == null) return;
        var game = settings.ClockOn ? Game.Area() : null;
        bool want = settings.ClockOn && (game != null || (Visible && WindowState != FormWindowState.Minimized && Opacity > 0));
        clock.Sync(want, game);
    }

    ToolStripMenuItem AutoStartItem()
    {
        var it = new ToolStripMenuItem("PC 켤 때 자동 실행") { Checked = AutoStart.On };
        it.ToolTipText = "켜 두면 PC를 켤 때 메이플 바가 화면 오른쪽 아래 아이콘으로 시작해요";
        it.Click += (_, _) =>
        {
            bool want = !AutoStart.On;
            if (AutoStart.Set(want)) it.Checked = want;
            else MessageBox.Show("자동 실행 설정을 바꾸지 못했어요. 잠시 뒤 다시 해 주세요.", "메이플 바");
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
        autoHidden = false;
        if (auto) return;
        userHidden = false;
        keepShown = !Game.Running();
        Activate();
        search.Focus();
        search.SelectAll();
    }

    // ── 창 처리 ──

    void DragMove(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        ReleaseCapture();
        dragging = true;
        SendMessage(Handle, 0xA1 /*WM_NCLBUTTONDOWN*/, (IntPtr)2 /*HTCAPTION*/, IntPtr.Zero); // 놓을 때까지 돌아오지 않는다
        dragging = false;
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
    ClockForm? clock; // 게임 화면 위 시계
    readonly System.Windows.Forms.Timer chipTimer = new() { Interval = 250 };

    // 타이머가 도는 동안 바 오른쪽 앞에 아이콘과 남은 시간을 띄운다
    void AddTimerChips(JanusWatch w)
    {
        var janusChip = new TimerChip(TimerChip.Kind.Janus, sc) { Visible = false, Margin = new Padding(S(2), 0, S(2), 0) };
        var runeChip = new TimerChip(TimerChip.Kind.Rune, sc) { Visible = false, Margin = new Padding(S(2), 0, S(6), 0) };
        // 칩을 누르면 그 타이머를 취소한다 (설정은 톱니 메뉴에서)
        janusChip.Click += (_, _) => { w.CancelJanus(); if (janusChip.SetTime(null)) LayoutBar(); };
        runeChip.Click += (_, _) => { w.CancelRune(); if (runeChip.SetTime(null)) LayoutBar(); };
        // 버프 칸마다 칩 하나
        var buffChips = new TimerChip[JanusWatch.BuffSlots];
        for (int i = 0; i < buffChips.Length; i++)
        {
            int slot = i;
            var bc = buffChips[i] = new TimerChip(TimerChip.Kind.Buff, sc) { Visible = false, Margin = new Padding(S(2), 0, S(2), 0) };
            bc.Click += (_, _) => { w.CancelBuff(slot); if (bc.SetTime(null)) LayoutBar(); };
        }
        var dopeChip = new TimerChip(TimerChip.Kind.Buff, sc) { Visible = false, Margin = new Padding(S(2), 0, S(2), 0), BuffTint = JanusWatch.DopeMain };
        dopeChip.Click += (_, _) => { w.CancelDope(); if (dopeChip.SetTime(null)) LayoutBar(); };
        foreach (var c in new[] { runeChip, janusChip }.Concat(buffChips).Append(dopeChip))
        {
            right.Controls.Add(c);
            right.Controls.SetChildIndex(c, 0);
        }
        chipTimer.Tick += (_, _) =>
        {
            bool changed = janusChip.SetTime(w.Remaining);
            changed |= runeChip.SetTime(w.RuneRemaining, w.RuneReady);
            for (int i = 0; i < buffChips.Length; i++)
            {
                buffChips[i].BuffTint = JanusWatch.BuffColors(w.BuffMin(i)).main;
                changed |= buffChips[i].SetTime(w.BuffRemaining(i));
            }
            changed |= dopeChip.SetTime(w.DopeRemaining);
            if (changed) LayoutBar();
        };
        chipTimer.Start();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        LayoutBar();
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
        Location = pos;
        gameTimer.Tick += (_, _) => { CheckGame(); FollowGame(); SyncClock(); };
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
        };
        AutoStart.Init();
        // 메이플 바를 또 실행하면 (숨어 있던) 바를 앞으로 불러온다
        if (Program.ShowEvent is { } ev)
            ThreadPool.RegisterWaitForSingleObject(ev, (_, _) =>
            {
                if (IsDisposed || !IsHandleCreated) return;
                try { BeginInvoke(() => { WindowState = FormWindowState.Normal; ShowAll(); }); } catch { }
            }, null, -1, executeOnlyOnce: false);
        gameTimer.Start();
        janus = new JanusWatch(settings, sc);
        AddTimerChips(janus);
        clock = new ClockForm(settings, sc);
        search.Focus();
        keepTop.Tick += (_, _) => StayOnTop();
        keepTop.Start();
        // 접혔을 때 남는 주황 띠
        peek = new Panel { Bounds = new Rectangle(0, 0, Width, PeekH), BackColor = UI.Accent, Visible = false, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        Controls.Add(peek);
        peek.BringToFront();
        foldTimer.Tick += (_, _) => FoldTick();
        foldTimer.Start();
        inven = new InvenWatcher(settings, (tag, post) =>
        {
            // 바를 숨겨도 "바를 숨겨도 알림 받기"가 켜져 있으면 띄운다 (게임 화면 위 가운데에)
            if ((!Visible || WindowState == FormWindowState.Minimized) && !settings.AlertWhenHidden) return;
            NoticeStrip.Show(this, tag == "새 글" ? 0 : 1, tag, post.Title, post.Category, () => Navigate(post.Url), sc);
        });
        RunArgs(Environment.GetCommandLineArgs().Skip(1).ToArray());
    }

    // 다른 '항상 위' 창(사이트 창, 다른 프로그램)이 바를 덮지 않게 계속 맨 위로 올린다
    readonly System.Windows.Forms.Timer keepTop = new() { Interval = 500 };
    InvenWatcher? inven;

    void StayOnTop()
    {
        if (menusOpen > 0 || !Visible || WindowState == FormWindowState.Minimized || !IsHandleCreated) return;
        SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_KEEP | SWP_NOOWNERZORDER);
        musicDock?.RaiseCard();
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
        var p = new Panel { Size = new Size(S(28), S(28)), Cursor = Cursors.Hand, BackColor = Color.White, Margin = new Padding(0, 0, S(2), 0) };
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
            var col = on ? UI.Accent : Color.FromArgb(165, 150, 135);
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

    void MakeTray()
    {
        if (tray != null) return;
        var menu = new ContextMenuStrip();
        menu.Items.Add("바 열기 / 숨기기 (Ctrl+Alt+M)", null, (_, _) => ToggleBar());
        menu.Items.Add(AlertWhenHiddenItem());
        // 바 메뉴에서 바꾼 값도 맞게 보이도록 열 때마다 체크를 다시 맞춘다
        menu.Items.Add(AutoStartItem());
        menu.Opening += (_, _) =>
        {
            if (menu.Items[1] is ToolStripMenuItem a) a.Checked = settings.AlertWhenHidden;
            if (menu.Items[2] is ToolStripMenuItem s) s.Checked = AutoStart.On;
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
        base.OnFormClosing(e);
    }

    void PaintSearchBorder(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(0, 0, searchBox.Width - 1, searchBox.Height - 1);
        int d = S(10);
        using var path = new GraphicsPath();
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        using var pen = new Pen(search.Focused ? UI.Accent : UI.Line, Math.Max(1, S(1)));
        g.DrawPath(pen, path);
    }
}
