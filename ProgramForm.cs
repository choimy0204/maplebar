using static MapleBar.Native;

namespace MapleBar;

/// <summary>
/// 프로그램 설정 창. 톱니 메뉴에 길게 있던 켜고 끄는 항목을 탭(일반 · 로그인 · 사이트 창 · 고정 바 · 웹 & 일반 설정 · 화면)으로 나눠 둔다.
/// </summary>
sealed class ProgramForm : Form
{
    /// <summary>바가 해 줘야 하는 일 (바 안의 상태를 바꾸는 것들)</summary>
    public sealed record Hooks(Action<bool> SetPinned, Action SyncClock, Action InvenPoll, Action InvenLogin, Action RebuildForTheme, Action ApplyBarItems, Action<string> OpenUrl);

    static ProgramForm? open;

    public static void Open(Form owner, Settings st, float sc, Hooks hooks, int tab = 0)
    {
        if (open == null || open.IsDisposed) open = new ProgramForm(st, sc, hooks) { Owner = owner };
        open.ShowTab(tab);
        open.Location = WinPlace.Where(st, "program", open, owner, sc);
        if (!open.Visible) open.Show();
        open.Activate();
    }

    readonly Settings st;
    readonly float sc;
    readonly Hooks hooks;
    readonly List<(RoundLabel tab, Panel page)> tabs = new();

    int S(float v) => UI.Scale(v, sc);
    static Font F(float size, FontStyle style = FontStyle.Regular) => new("맑은 고딕", size * UI.Z, style);

    ProgramForm(Settings st, float sc, Hooks hooks)
    {
        this.st = st; this.sc = sc; this.hooks = hooks;
        Text = "프로그램 설정";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = UI.Back;
        ForeColor = UI.Text;
        Font = F(10);
        DoubleBuffered = true;

        // 탭 글자가 줄바꿈되지 않게, 가장 긴 탭 이름에 맞춘 같은 너비로 둔다 (모자라면 창을 넓힌다)
        var names = new[] { "일반", "로그인", "사이트 창", "고정 바", "웹 & 일반 설정", "화면" };
        int pad = S(18), gap = S(8), y = S(14);
        using var tabFont = F(9.5f, FontStyle.Bold);
        int tabW = names.Max(n => TextRenderer.MeasureText(n, tabFont, Size.Empty, TextFormatFlags.NoPrefix).Width) + S(20);
        int W = Math.Max(S(440), pad * 2 + tabW * names.Length + gap * (names.Length - 1));
        tabW = (W - pad * 2 - gap * (names.Length - 1)) / names.Length;

        // 머리: 제목과 닫기. 빈 곳을 끌면 창이 움직인다
        var title = new Label { Text = "프로그램 설정", Font = F(13, FontStyle.Bold), AutoSize = true, Location = new Point(pad, y) };
        title.MouseDown += (_, e) => Drag(e);
        MouseDown += (_, e) => Drag(e);
        var close = UI.Button("✕", "닫기", Close, sc);
        close.Font = F(10);
        Controls.Add(title);
        Controls.Add(close);
        close.Location = new Point(W - pad - close.PreferredSize.Width + S(6), y + S(2));
        y += S(42);

        // 탭 줄
        int tx = pad;
        int top = y + S(30) + S(12);
        foreach (var name in names)
        {
            var t = new RoundLabel
            {
                Text = name, UseMnemonic = false, AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Cursor = Cursors.Hand,
                Font = F(9.5f, FontStyle.Bold), Radius = S(8), Size = new Size(tabW, S(30)), Location = new Point(tx, y),
            };
            int idx = tabs.Count;
            t.Click += (_, _) => ShowTab(idx);
            Controls.Add(t);
            var page = new Panel { Location = new Point(pad, top), Width = W - pad * 2, BackColor = UI.Back, Visible = false };
            page.MouseDown += (_, e) => Drag(e);
            Controls.Add(page);
            tabs.Add((t, page));
            tx += t.Width + gap;
        }

        BuildGeneral(tabs[0].page);
        BuildLogins(tabs[1].page);
        BuildSites(tabs[2].page);
        BuildBarCustom(tabs[3].page);
        BuildInven(tabs[4].page);
        BuildLook(tabs[5].page);

        Logins.Changed += UpdateLogins;
        Activated += (_, _) => Logins.RefreshSoon();

        int h = tabs.Max(p => p.page.Height);
        foreach (var (_, page) in tabs) page.Height = h;
        ClientSize = new Size(W, top + h + S(16));
        ShowTab(0);
    }

    void ShowTab(int i)
    {
        i = Math.Clamp(i, 0, tabs.Count - 1);
        for (int k = 0; k < tabs.Count; k++)
        {
            var (t, page) = tabs[k];
            bool on = k == i;
            t.Fill = on ? UI.SoftAccent : UI.HoverBack;
            t.ForeColor = on ? UI.Accent : UI.Text;
            page.Visible = on;
        }
        if (i == 1) Logins.RefreshSoon();
    }

    // ── 탭 내용 ──

    void BuildGeneral(Panel p)
    {
        int y = 0;
        Row(p, ref y, "바 고정", "끄면 바를 안 쓸 때 3초 뒤 접혀요", !st.BarAutoHide, v => hooks.SetPinned(v));
        Row(p, ref y, "바를 숨겨도 알림 받기", "끄면 바를 숨긴 동안 알림이 뜨지 않아요", st.AlertWhenHidden,
            v => { st.AlertWhenHidden = v; st.Save(); });
        // 자동 실행은 작업 스케줄러에 등록하므로 뒤에서 돌리고, 실패하면 스위치를 되돌린다
        bool busy = false;
        JanusForm.Toggle? auto = null;
        auto = Row(p, ref y, "PC 켤 때 자동 실행", "PC를 켜면 화면 오른쪽 아래 아이콘으로 시작해요", AutoStart.On, async v =>
        {
            if (busy) { auto!.On = !v; return; }
            busy = true;
            bool ok;
            try { ok = await Task.Run(() => AutoStart.Set(v)); }
            finally { busy = false; }
            if (!ok)
            {
                if (!auto!.IsDisposed) auto.On = AutoStart.On;
                MessageBox.Show(v && !AutoStart.Installed ? "설치한 메이플 바에서 켜 주세요." : "자동 실행 설정을 바꾸지 못했어요. 잠시 뒤 다시 해 주세요.", "메이플 바");
            }
        });
        p.Height = y;
    }

    void BuildLogins(Panel p)
    {
        int y = 0;
        var tip = new Label
        {
            Text = "로그인은 열린 창에서 직접 해요. 마치면 여기에 바로 보여요.",
            Font = F(9), ForeColor = UI.Sub, AutoSize = true, MaximumSize = new Size(p.Width, 0), Location = new Point(0, y),
        };
        p.Controls.Add(tip);
        y += tip.PreferredSize.Height + S(8);
        foreach (var s in Logins.Services)
        {
            var service = s;
            int w = p.Width;
            var name = new Label { Text = service.Name, AutoSize = true, Font = F(10, FontStyle.Bold), Location = new Point(0, y + S(8)) };
            var hint = new Label { Text = service.Hint, AutoSize = true, Font = F(8.5f), ForeColor = UI.Sub, Location = new Point(0, name.Bottom + S(2)) };
            p.Controls.Add(name);
            p.Controls.Add(hint);
            int rowH = hint.Bottom - y + S(8);

            // 로그인돼 있으면 로그아웃 단추, 아니면 로그인 단추
            var btn = UI.Button("로그인", "", async () =>
            {
                if (Logins.IsIn(service.Key) != true) { hooks.OpenUrl(service.LoginUrl); return; }
                if (AskBox.Show(this, service.Name + "에서 정말 로그아웃 하시겠어요?", "메이플 바 안에서만 로그아웃돼요.", "로그아웃"))
                    await Logins.LogoutAsync(service.Key);
            }, sc, null, UI.HoverBack);
            btn.Radius = S(8);
            btn.Location = new Point(w - btn.PreferredSize.Width, y + (rowH - btn.PreferredSize.Height) / 2);
            p.Controls.Add(btn);

            var status = new Label { AutoSize = true, Font = F(9, FontStyle.Bold), TextAlign = ContentAlignment.MiddleRight };
            p.Controls.Add(status);
            loginRows.Add((service.Key, status, btn, rowH, y, w));

            y += rowH;
            var line = new Panel { BackColor = UI.Line, Location = new Point(0, y), Size = new Size(w, Math.Max(1, S(1))) };
            p.Controls.Add(line);
            y += line.Height;
        }
        p.Height = y;
        UpdateLogins();
    }

    readonly List<(string key, Label status, RoundLabel btn, int rowH, int y, int w)> loginRows = new();

    /// <summary>로그인 칸의 글자와 단추를 지금 상태에 맞춘다.</summary>
    void UpdateLogins()
    {
        if (IsDisposed) return;
        foreach (var (key, status, btn, rowH, y, w) in loginRows)
        {
            bool? isIn = Logins.IsIn(key);
            status.Text = isIn == null ? "확인 중…" : isIn == true ? "로그인됨" : "로그인 안 됨";
            status.ForeColor = isIn == true ? UI.T(Color.FromArgb(40, 160, 90)) : UI.Sub;
            btn.Text = isIn == true ? "로그아웃" : "로그인";
            btn.Location = new Point(w - btn.PreferredSize.Width, y + (rowH - btn.PreferredSize.Height) / 2);
            status.Location = new Point(btn.Left - S(12) - status.PreferredSize.Width, y + (rowH - status.PreferredSize.Height) / 2);
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Logins.Changed -= UpdateLogins;
        base.OnFormClosed(e);
    }

    void BuildSites(Panel p)
    {
        int y = 0;
        Row(p, ref y, "사이트 창 항상 위", "사이트 창을 게임 위에 띄워 둬요", st.WinTopMost,
            v => { st.WinTopMost = v; st.Save(); WebWindow.SetTopMost(v); });
        Row(p, ref y, "곡 넘김 효과음", "뮤직에서 곡을 넘길 때 레코드 소리를 내요", st.MusicFx,
            v => { st.MusicFx = v; st.Save(); WebWindow.SetMusicFx(v); });
        y += S(6);
        var closeAll = UI.Button("사이트 창 모두 닫기", "", () => WebWindow.CloseAll(), sc, null, UI.HoverBack);
        closeAll.Radius = S(8);
        closeAll.Location = new Point(0, y);
        p.Controls.Add(closeAll);
        y += closeAll.PreferredSize.Height + S(4);
        p.Height = y;
    }

    /// <summary>고정 바 탭: 바의 메뉴와 펼침 메뉴 항목을 눌러 보이기/숨기기 (회색 = 숨김). 누르면 바로 바에 반영한다.</summary>
    void BuildBarCustom(Panel p)
    {
        var tip = new Label
        {
            Text = "누르면 바로 바에 반영돼요. 회색은 숨긴 항목이에요.",
            Font = F(9), ForeColor = UI.Sub, AutoSize = true, MaximumSize = new Size(p.Width, 0), Location = new Point(0, 0),
        };
        p.Controls.Add(tip);
        int y = tip.PreferredSize.Height + S(10);

        static void Flip(List<string> list, string key) { if (!list.Remove(key)) list.Add(key); }

        // 바에 있는 순서대로 (직접입력은 바에 없다)
        var tops = SiteMenu.Top.Where(t => t.Name != "직접입력")
            .OrderBy(t => { int i = st.BarOrder.IndexOf(t.Name); return i < 0 ? int.MaxValue : i; }).ToList();

        // 눌러서 켜고 끄는 작은 칸 (켜짐 = 주황, 꺼짐 = 회색)
        RoundLabel Chip(string text, bool bold = false) => new()
        {
            Text = text, UseMnemonic = false, AutoSize = true, Cursor = Cursors.Hand, Radius = S(8),
            Font = F(9, bold ? FontStyle.Bold : FontStyle.Regular),
            Padding = new Padding(S(10), S(5), S(10), S(5)), Margin = new Padding(0, 0, S(6), S(6)),
        };
        FlowLayoutPanel Flow(Panel body, int top) => new()
        {
            Location = new Point(0, top), Width = body.Width, AutoSize = true, MaximumSize = new Size(body.Width, 0),
            WrapContents = true, BackColor = UI.Back, Margin = Padding.Empty, Padding = Padding.Empty,
        };

        // 1) 바에 보일 메뉴
        Group(p, ref y, "바에 보일 메뉴", body =>
        {
            var flow = Flow(body, 0);
            body.Controls.Add(flow);
            foreach (var t in tops)
            {
                var name = t.Name;
                var chip = Chip(name);
                void Paint() { bool on = !st.BarHiddenTops.Contains(name); chip.Fill = on ? UI.SoftAccent : UI.HoverBack; chip.ForeColor = on ? UI.AccentText : UI.Sub; }
                Paint();
                chip.Click += (_, _) => { Flip(st.BarHiddenTops, name); st.Save(); hooks.ApplyBarItems(); Paint(); };
                flow.Controls.Add(chip);
            }
            // 순서: 바에서 버튼을 끌어 바꾸고, 여기서 처음 순서로 되돌린다
            int oy = flow.PreferredSize.Height + S(6);
            var note = new Label { Text = "순서는 바에서 버튼을 끌어서 바꿔요", AutoSize = true, Font = F(8.5f), ForeColor = UI.Sub };
            var reset = UI.Button("처음 순서로", "", () =>
            {
                st.BarOrder.Clear();
                st.Save();
                note.Text = "다음에 바를 켜면 처음 순서로 돌아가요";
            }, sc, null, UI.HoverBack);
            reset.Radius = S(8);
            body.Controls.Add(note);
            body.Controls.Add(reset);
            int rh = reset.PreferredSize.Height;
            reset.Location = new Point(body.Width - S(2) - reset.PreferredSize.Width, oy);
            note.Location = new Point(0, oy + (rh - note.PreferredSize.Height) / 2);
            return oy + rh;
        });

        // 2) 펼침 메뉴: 위에서 메뉴를 고르면 아래에 그 메뉴 항목을 스위치로 보여 준다
        var menus = tops.Where(t => t.Items != null).ToList();
        if (menus.Count > 0) Group(p, ref y, "펼침 메뉴 항목", body =>
        {
            var pick = Flow(body, 0);
            body.Controls.Add(pick);
            var list = new Panel { Width = body.Width, Height = S(250), AutoScroll = true, BackColor = UI.Back };
            body.Controls.Add(list);
            var chips = new List<RoundLabel>();
            int sel = 0;

            void Show(int i)
            {
                sel = i;
                for (int k = 0; k < chips.Count; k++)
                {
                    chips[k].Fill = k == sel ? UI.SoftAccent : UI.HoverBack;
                    chips[k].ForeColor = k == sel ? UI.Accent : UI.Text;
                }
                list.SuspendLayout();
                list.AutoScrollPosition = Point.Empty;
                foreach (var c in list.Controls.Cast<Control>().ToList()) c.Dispose();
                var t = menus[sel];
                int innerW = list.Width - SystemInformation.VerticalScrollBarWidth - S(6);
                int colGap = S(16), colW = (innerW - colGap) / 2, rowH = S(34);
                int ly = 0, col = 0;
                void NewLine() { if (col != 0) { ly += rowH; col = 0; } }
                foreach (var it in t.Items!)
                {
                    if (it.Path == null)
                    {
                        // 묶음 제목
                        NewLine();
                        var h = new Label { Text = it.Title, UseMnemonic = false, AutoSize = true, Font = F(8.5f, FontStyle.Bold), ForeColor = UI.Sub, Location = new Point(0, ly + (ly > 0 ? S(10) : S(2))) };
                        list.Controls.Add(h);
                        ly = h.Top + h.PreferredSize.Height + S(2);
                        continue;
                    }
                    string key = SiteMenu.ItemKey(t, it);
                    int x = col * (colW + colGap);
                    var sw = new JanusForm.Toggle { On = !st.BarHiddenItems.Contains(key), Size = new Size(S(34), S(18)) };
                    sw.Location = new Point(x + colW - sw.Width, ly + (rowH - sw.Height) / 2);
                    sw.Changed += _ => { Flip(st.BarHiddenItems, key); st.Save(); hooks.ApplyBarItems(); };
                    var name = new Label
                    {
                        Text = it.Title, UseMnemonic = false, AutoSize = false, AutoEllipsis = true, Font = F(9), TextAlign = ContentAlignment.MiddleLeft,
                        Location = new Point(x, ly), Size = new Size(colW - sw.Width - S(6), rowH),
                    };
                    list.Controls.Add(name); list.Controls.Add(sw);
                    if (++col == 2) { ly += rowH; col = 0; }
                }
                NewLine();
                list.Controls.Add(new Panel { Location = new Point(0, ly), Size = new Size(1, S(6)), BackColor = UI.Back }); // 끝 여백
                list.ResumeLayout();
            }

            for (int i = 0; i < menus.Count; i++)
            {
                int idx = i;
                var c = Chip(menus[i].Name, bold: true);
                c.Click += (_, _) => { if (sel != idx) Show(idx); };
                chips.Add(c);
                pick.Controls.Add(c);
            }
            list.Top = pick.PreferredSize.Height + S(6);
            Show(0);
            return list.Bottom;
        });
        p.Height = y - S(12);
    }

    /// <summary>웹 & 일반 설정 탭: '웹'(브라우저 배율)과 '인벤 알림'을 칸으로 나눠 둔다</summary>
    void BuildInven(Panel p)
    {
        int y = 0;
        Group(p, ref y, "웹", BuildZoom);
        Group(p, ref y, "인벤 알림", body =>
        {
            int by = 0;
            var tip = new Label
            {
                Text = "켜 둔 게시판에 새 글이 올라오면 화면 오른쪽 아래에 알려 드려요.",
                Font = F(9), ForeColor = UI.Sub, AutoSize = true, MaximumSize = new Size(body.Width, 0), Location = new Point(0, by),
            };
            body.Controls.Add(tip);
            by += tip.PreferredSize.Height + S(4);
            foreach (var f in Inven.Feeds)
            {
                var key = f.Key;
                Row(body, ref by, f.Label, null, st.InvenWatch.Contains(key), v =>
                {
                    st.InvenWatch.Remove(key);
                    if (v) st.InvenWatch.Add(key);
                    st.Save();
                    hooks.InvenPoll();
                });
            }
            by += S(10);
            var login = UI.Button("인벤 로그인", "", hooks.InvenLogin, sc, null, UI.HoverBack);
            login.Radius = S(8);
            login.Location = new Point(0, by);
            body.Controls.Add(login);
            return by + login.PreferredSize.Height;
        });
        p.Height = y;
    }

    /// <summary>브라우저 배율: − 100% + 로 사이트 창 화면 크기를 고른다. 바로 열린 창에 적용된다</summary>
    int BuildZoom(Panel body)
    {
        int w = body.Width;
        var name = new Label { Text = "브라우저 배율", AutoSize = true, Font = F(10, FontStyle.Bold), Location = new Point(0, S(2)) };
        var hint = new Label
        {
            Text = "사이트 창 화면 크기예요. Ctrl+휠로는 안 바뀌어요", AutoSize = true, Font = F(8.5f), ForeColor = UI.Sub,
            Location = new Point(0, name.Bottom + S(2)),
        };
        body.Controls.Add(name);
        body.Controls.Add(hint);
        int rowH = hint.Bottom + S(2);

        var value = new Label { AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Font = F(10, FontStyle.Bold), Size = new Size(S(56), S(28)) };
        void Show() => value.Text = st.WebZoom + "%";
        void Step(int d)
        {
            int v = Math.Clamp(st.WebZoom + d, WebWindow.ZoomMin, WebWindow.ZoomMax);
            if (v == st.WebZoom) return;
            st.WebZoom = v; st.Save(); Show(); WebWindow.SetZoom();
        }
        var minus = UI.Button("−", "작게", () => Step(-10), sc, null, UI.HoverBack);
        var plus = UI.Button("+", "크게", () => Step(10), sc, null, UI.HoverBack);
        var reset = UI.Button("새로고침", "100%로 되돌려요", () => Step(100 - st.WebZoom), sc, null, UI.HoverBack);
        foreach (var b in new[] { minus, plus, reset }) { b.Radius = S(8); body.Controls.Add(b); }
        body.Controls.Add(value);
        Show();

        int x = w - S(2) - reset.PreferredSize.Width;
        reset.Location = new Point(x, (rowH - reset.PreferredSize.Height) / 2);
        x -= S(10) + plus.PreferredSize.Width;
        plus.Location = new Point(x, (rowH - plus.PreferredSize.Height) / 2);
        x -= value.Width;
        value.Location = new Point(x, (rowH - value.Height) / 2);
        x -= minus.PreferredSize.Width;
        minus.Location = new Point(x, (rowH - minus.PreferredSize.Height) / 2);
        return rowH;
    }

    /// <summary>둥근 테두리 칸에 제목을 달고, 안쪽은 fill이 채운다 (fill은 쓴 높이를 돌려준다)</summary>
    void Group(Panel p, ref int y, string title, Func<Panel, int> fill)
    {
        int inset = S(14);
        var card = new GroupCard { Radius = S(10), Location = new Point(0, y), Width = p.Width };
        var head = new Label { Text = title, UseMnemonic = false, AutoSize = true, Font = F(10.5f, FontStyle.Bold), ForeColor = UI.Accent, Location = new Point(inset, S(10)) };
        card.Controls.Add(head);
        var body = new Panel { Location = new Point(inset, head.Bottom + S(4)), Width = card.Width - inset * 2, BackColor = UI.Back };
        body.MouseDown += (_, e) => Drag(e);
        card.MouseDown += (_, e) => Drag(e);
        card.Controls.Add(body);
        // 먼저 창에 붙여 둔다: 붙기 전에 재면 기본 글꼴로 재서 버튼 너비가 작게 나와 오른쪽이 잘린다
        p.Controls.Add(card);
        body.Height = fill(body);
        card.Height = body.Bottom + S(12);
        y = card.Bottom + S(12);
    }

    sealed class GroupCard : Panel
    {
        public int Radius { get; set; } = 10;
        public GroupCard() { DoubleBuffered = true; ResizeRedraw = true; BackColor = UI.Back; }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var pen = new Pen(UI.Line);
            using var path = UI.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Radius);
            e.Graphics.DrawPath(pen, path);
        }
    }

    void BuildLook(Panel p)
    {
        int y = 0;
        Row(p, ref y, "다크 모드", "바꾸면 바와 설정 창을 새 색으로 다시 그려요", st.DarkMode, v =>
        {
            st.DarkMode = v; st.Save();
            BeginInvoke(hooks.RebuildForTheme); // 스위치 그리기가 끝난 뒤에 바를 다시 만든다
        });
        Row(p, ref y, "게임 화면에 시계 보기", "시계는 끌어서 옮기고, 오른쪽 버튼으로 초·날짜를 골라요", st.ClockOn,
            v => { st.ClockOn = v; st.Save(); hooks.SyncClock(); });
        p.Height = y;
    }

    /// <summary>이름 · 설명 · 스위치 한 줄. 아래에 옅은 줄을 긋는다.</summary>
    JanusForm.Toggle Row(Panel p, ref int y, string label, string? hint, bool on, Action<bool> set)
    {
        int w = p.Width;
        var sw = new JanusForm.Toggle { On = on, Size = new Size(S(40), S(22)) };
        var name = new Label { Text = label, AutoSize = true, Font = F(10, FontStyle.Bold), Location = new Point(0, y + S(8)) };
        p.Controls.Add(name);
        int bottom = name.Bottom;
        if (hint != null)
        {
            var h = new Label
            {
                Text = hint, AutoSize = true, Font = F(8.5f), ForeColor = UI.Sub,
                MaximumSize = new Size(w - sw.Width - S(16), 0), Location = new Point(0, name.Bottom + S(2)),
            };
            p.Controls.Add(h);
            bottom = h.Bottom;
        }
        int rowH = bottom - y + S(8);
        sw.Location = new Point(w - sw.Width, y + (rowH - sw.Height) / 2);
        sw.Changed += set;
        p.Controls.Add(sw);
        y += rowH;
        var line = new Panel { BackColor = UI.Line, Location = new Point(0, y), Size = new Size(w, Math.Max(1, S(1))) };
        p.Controls.Add(line);
        y += line.Height;
        return sw;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UI.RoundCorners(Handle);
    }

    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ClassStyle |= 0x20000; return cp; } // 창 그림자
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { Close(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    void Drag(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        DragWindow(Handle);
        WinPlace.Remember(st, "program", this);
    }
}
