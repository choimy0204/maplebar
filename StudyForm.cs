using System.Runtime.InteropServices;
using System.Text;

using static MapleBar.Native;

namespace MapleBar;

/// <summary>공부 카드 창: 띄우는 주기, 공부 묶음 목록, 불러오기, AI에게 줄 양식 받기.</summary>
sealed class StudyForm : Form
{
    static StudyForm? open;

    public static void Open(Form owner, Settings st, float sc)
    {
        if (open == null || open.IsDisposed) open = new StudyForm(st, sc) { Owner = owner };
        var wa = Screen.FromControl(owner).WorkingArea;
        open.Location = new Point(
            Math.Clamp(owner.Left + (owner.Width - open.Width) / 2, wa.Left, Math.Max(wa.Left, wa.Right - open.Width)),
            Math.Clamp(owner.Bottom + (int)(12 * sc), wa.Top, Math.Max(wa.Top, wa.Bottom - open.Height)));
        if (!open.Visible) open.Show();
        open.Activate();
    }

    static readonly Color Gray = Color.FromArgb(135, 135, 135);
    static readonly Color Deep = Color.FromArgb(200, 95, 0);
    static readonly Color Red = Color.FromArgb(225, 60, 60);

    readonly Settings st;
    readonly float sc;
    readonly Panel list;
    readonly Label msg;
    int rowH, rowGap;

    int S(float v) => UI.Scale(v, sc);
    static Font F(float size, FontStyle style = FontStyle.Regular) => new("맑은 고딕", size * UI.Z, style);

    StudyForm(Settings st, float sc)
    {
        this.st = st; this.sc = sc;
        Text = "공부 카드";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.White;
        ForeColor = UI.Text;
        Font = F(10);
        DoubleBuffered = true;
        MouseDown += (_, e) => Drag(this, e);

        int pad = S(18), W = S(500), inner = W - pad * 2, y = S(14);
        var title = new Label { Text = "공부 카드", Font = F(13, FontStyle.Bold), AutoSize = true, Location = new Point(pad, y) };
        title.MouseDown += (_, e) => Drag(this, e);
        var close = UI.Button("✕", "닫기", Close, sc);
        close.Font = F(10);
        Controls.Add(title);
        Controls.Add(close);
        close.Location = new Point(W - pad - close.PreferredSize.Width + S(6), y + S(2));
        y += S(34);
        y += Add(new Label
        {
            Text = "게임 화면 위에 카드를 띄워 두고, 정한 시간마다 다음 카드로 내려가요. 누르면 바로 다음 카드, 끌면 자리 옮기기, ☆는 즐겨찾기, ✕는 끄기예요. 마우스를 올려 두면 멈추고, 다른 알림이 뜨면 1초 늦게 넘어가요.",
            Font = F(9), ForeColor = Gray, AutoSize = true, MaximumSize = new Size(inner, 0), Location = new Point(pad, y),
        }) + S(14);

        // ── 띄우기 ──
        int rh = S(32), cx = pad + S(130);
        Label RowLabel(string t, int ry) => new() { Text = t, AutoSize = true, Font = F(10), Location = new Point(pad, ry + (rh - S(20)) / 2) };
        var sw = new JanusForm.Toggle { On = st.StudyOn, Size = new Size(S(40), S(22)) };
        sw.Location = new Point(cx, y + (rh - sw.Height) / 2);
        sw.Changed += v => { st.StudyOn = v; st.Save(); };
        // 카드의 ✕로 끄면 여기 스위치도 맞춘다
        Action synced = () => { sw.On = false; sw.Invalidate(); };
        StudyRun.TurnedOff += synced;
        FormClosed += (_, _) => StudyRun.TurnedOff -= synced;
        Controls.Add(RowLabel("카드 띄우기", y));
        Controls.Add(sw);
        var now = Btn("다음 카드", UI.Text, UI.HoverBack, () =>
        {
            if (StudyRun.Current?.ShowNext() == true) return;
            if (!st.StudyOn) Say("카드 띄우기를 켜 주세요.", Red);
            else if (StudyRun.Count(st) == 0) Say(st.StudyStarOnly ? "켜 둔 묶음에 즐겨찾기한 카드가 없어요." : "켜 둔 묶음에 카드가 없어요.", Red);
            else Say("곧 카드가 떠요.", Gray);
        });
        now.Location = new Point(W - pad - now.Width, y + (rh - now.Height) / 2);
        Controls.Add(now);
        y += rh + S(6);

        NumRow("카드 넘기는 시간", "초마다", st.StudySec, 3, 600, v => st.StudySec = v);
        void NumRow(string label, string unit, int value, int min, int max, Action<int> set)
        {
            var nb = new NumBox { Minimum = min, Maximum = max, Value = value, Size = new Size(S(70), S(30)), Radius = S(8), Font = F(10) };
            nb.Location = new Point(cx, y + (rh - nb.Height) / 2);
            nb.ValueChanged += (_, _) => { set(nb.Value); st.Save(); };
            Controls.Add(RowLabel(label, y));
            Controls.Add(nb);
            Controls.Add(new Label { Text = unit, AutoSize = true, Font = F(10), ForeColor = Gray, Location = new Point(nb.Right + S(8), y + (rh - S(20)) / 2) });
            y += rh + S(6);
        }

        // 즐겨찾기만 보기: 카드의 ☆를 누른 카드만 돌린다
        var star = new JanusForm.Toggle { On = st.StudyStarOnly, Size = new Size(S(40), S(22)) };
        star.Location = new Point(cx, y + (rh - star.Height) / 2);
        star.Changed += v =>
        {
            st.StudyStarOnly = v; Study.Version++; st.Save();
            if (v && StudyRun.Count(st) == 0) Say("아직 즐겨찾기한 카드가 없어요. 카드의 ☆를 눌러 주세요.", Red);
            else Say("");
        };
        Controls.Add(RowLabel("즐겨찾기만 보기", y));
        Controls.Add(star);
        Controls.Add(new Label { Text = "카드의 ☆를 누르면 즐겨찾기돼요", AutoSize = true, Font = F(9), ForeColor = Gray, Location = new Point(star.Right + S(10), y + (rh - S(18)) / 2) });
        y += rh + S(6);
        // 카드에서 ☆를 누르면 묶음 목록의 즐겨찾기 수를 맞춘다
        Action starred = () => { if (!IsDisposed) Fill(); };
        StudyCard.Starred += starred;
        FormClosed += (_, _) => StudyCard.Starred -= starred;
        y += S(10);
        Controls.Add(new Panel { BackColor = UI.Line, Bounds = new Rectangle(pad, y, inner, Math.Max(1, S(1))) });
        y += S(14);

        // ── 공부 묶음 ──
        Controls.Add(new Label { Text = "공부 묶음", Font = F(11, FontStyle.Bold), AutoSize = true, Location = new Point(pad, y) });
        y += S(30);
        int bx = pad;
        foreach (var b in new[]
        {
            Btn("AI용 양식 받기", Deep, UI.SoftAccent, AskAi),
            Btn("파일 불러오기", UI.Text, UI.HoverBack, LoadFiles),
            Btn("붙여 넣기", UI.Text, UI.HoverBack, PasteBox),
        })
        {
            b.Location = new Point(bx, y);
            Controls.Add(b);
            bx = b.Right + S(8);
        }
        y += S(36) + S(10);

        rowH = S(50); rowGap = S(6);
        list = new Panel { AutoScroll = true, Location = new Point(pad, y), Size = new Size(inner, rowH * 5 + rowGap * 4) };
        Controls.Add(list);
        y += list.Height + S(8);
        msg = new Label { AutoSize = true, Font = F(9), ForeColor = Deep, MaximumSize = new Size(inner, 0), Location = new Point(pad, y) };
        Controls.Add(msg);
        y += S(22);
        y += Add(new Label
        {
            Text = "양식: 한 줄에 카드 하나, 칸은 | 로 나눠요. 예) ubiquitous|어디에나 있는|Smartphones are ubiquitous.\n" +
                   "첫 줄에 '# 칸: 문장|해석|첨언'처럼 쓰면 칸 이름이 바뀌어요. 같은 이름의 묶음을 또 넣으면 겹치지 않는 카드만 이어 붙여요.",
            Font = F(8.5f), ForeColor = Gray, AutoSize = true, MaximumSize = new Size(inner, 0), Location = new Point(pad, y),
        }) + S(14);

        ClientSize = new Size(W, y);
        Fill();
    }

    int Add(Control c) { Controls.Add(c); return c.PreferredSize.Height; }

    void Say(string text, Color? color = null) { msg.Text = text; msg.ForeColor = color ?? Deep; }

    RoundLabel Btn(string text, Color fore, Color back, Action act)
    {
        var b = new RoundLabel
        {
            Text = text, AutoSize = false, Radius = S(8), Fill = back, ForeColor = fore, Cursor = Cursors.Hand, UseMnemonic = false,
            TextAlign = ContentAlignment.MiddleCenter, Font = F(9.5f, FontStyle.Bold),
        };
        b.Size = new Size(TextRenderer.MeasureText(text, b.Font).Width + S(28), S(34));
        b.MouseEnter += (_, _) => b.Fill = ControlPaint.Dark(back, 0.05f);
        b.MouseLeave += (_, _) => b.Fill = back;
        b.Click += (_, _) => act();
        return b;
    }

    /// <summary>묶음 목록을 다시 그린다. 줄마다 켜기 · 이름 · 카드 수 · 지우기(두 번 눌러야 지운다)</summary>
    void Fill()
    {
        list.SuspendLayout();
        foreach (Control c in list.Controls.Cast<Control>().ToList()) c.Dispose();
        list.Controls.Clear();
        if (st.Decks.Count == 0)
        {
            list.Controls.Add(new Label
            {
                Text = "아직 묶음이 없어요.\n'AI용 양식 받기'로 양식을 받아 AI에게 주고, 받은 답을 '붙여 넣기' 하세요.",
                Font = F(9.5f), ForeColor = Gray, AutoSize = false, TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill,
            });
            list.ResumeLayout();
            return;
        }
        int w = list.Width - (st.Decks.Count > 5 ? SystemInformation.VerticalScrollBarWidth + S(4) : 0);
        for (int i = 0; i < st.Decks.Count; i++)
        {
            var d = st.Decks[i];
            var row = new RoundLabel { AutoSize = false, Radius = S(8), Fill = UI.HoverBack, Bounds = new Rectangle(0, i * (rowH + rowGap), w, rowH) };
            var sw = new JanusForm.Toggle { On = d.On, Size = new Size(S(36), S(20)), BackColor = UI.HoverBack };
            sw.Location = new Point(S(12), (rowH - sw.Height) / 2);
            sw.Changed += v => { d.On = v; Study.Version++; st.Save(); };
            var name = new Label
            {
                Text = d.Name, Font = F(10, FontStyle.Bold), AutoSize = false, AutoEllipsis = true, UseMnemonic = false,
                Location = new Point(sw.Right + S(12), S(7)), Size = new Size(w - sw.Right - S(70), S(20)),
            };
            var info = new Label
            {
                Text = $"{d.Items.Count}장{(d.Items.Count(d.Starred) is > 0 and var n ? $" · ★ {n}" : "")} · {string.Join(" | ", d.Cols)}", Font = F(8.5f), ForeColor = Gray, AutoSize = false, AutoEllipsis = true,
                UseMnemonic = false, Location = new Point(name.Left, S(27)), Size = name.Size,
            };
            var del = new RoundLabel
            {
                Text = "✕", AutoSize = false, Radius = S(6), Fill = Color.Transparent, ForeColor = Gray, Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter, Font = F(9), Size = new Size(S(46), S(28)),
            };
            del.Location = new Point(w - del.Width - S(8), (rowH - del.Height) / 2);
            bool sure = false;
            del.Click += (_, _) =>
            {
                if (!sure) { sure = true; del.Text = "지우기"; del.ForeColor = Color.White; del.Fill = Red; return; }
                st.Decks.Remove(d); Study.Version++; st.Save();
                Say($"'{d.Name}' 묶음을 지웠어요.");
                BeginInvoke(Fill);
            };
            del.MouseLeave += (_, _) => { if (sure) { sure = false; del.Text = "✕"; del.ForeColor = Gray; del.Fill = Color.Transparent; } };
            row.Controls.AddRange(new Control[] { sw, name, info, del });
            list.Controls.Add(row);
        }
        list.ResumeLayout();
    }

    /// <summary>읽은 글을 묶음으로 넣고 결과를 알려 준다</summary>
    bool Take(string text, string fallbackName)
    {
        var deck = Study.Parse(text, fallbackName);
        if (deck == null) { Say("카드를 못 찾았어요. 한 줄에 '단어|의미|첨언'처럼 | 로 나눠 주세요.", Red); return false; }
        int n = Study.Merge(st, deck, out bool appended);
        st.Save();
        Say(appended ? $"'{deck.Name}' 묶음에 {n}장을 더했어요." : $"'{deck.Name}' 묶음을 넣었어요 ({n}장).");
        Fill();
        return true;
    }

    void LoadFiles()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "공부 묶음 불러오기", Multiselect = true,
            Filter = "글 파일 (*.txt;*.tsv;*.csv;*.md)|*.txt;*.tsv;*.csv;*.md|모든 파일|*.*",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        foreach (var f in dlg.FileNames)
        {
            try { Take(ReadText(f), Path.GetFileNameWithoutExtension(f)); }
            catch (Exception ex) { Log.Write("공부", "파일을 못 읽음", ex); Say("파일을 못 읽었어요.", Red); }
        }
    }

    /// <summary>UTF-8로 읽고, 글자가 깨지면 한국어 메모장 형식(CP949)으로 다시 읽는다</summary>
    static string ReadText(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var text = new UTF8Encoding(false, false).GetString(bytes).TrimStart('﻿');
        if (!text.Contains('�')) return text;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(949).GetString(bytes);
    }

    // ── 작은 창 (붙여 넣기 · AI용 양식) ──

    Form Dialog(string title, int w, out int y)
    {
        var f = new Form
        {
            FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, ShowInTaskbar = false, TopMost = true,
            BackColor = Color.White, ForeColor = UI.Text, Font = F(10), AutoScaleMode = AutoScaleMode.None, KeyPreview = true,
        };
        f.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) f.Close(); };
        f.MouseDown += (_, e) => Drag(f, e);
        f.HandleCreated += (_, _) => UI.RoundCorners(f.Handle);
        int pad = S(18);
        y = S(14);
        var t = new Label { Text = title, Font = F(12, FontStyle.Bold), AutoSize = true, Location = new Point(pad, y) };
        t.MouseDown += (_, e) => Drag(f, e);
        var close = UI.Button("✕", "닫기", f.Close, sc);
        close.Font = F(10);
        f.Controls.Add(t);
        f.Controls.Add(close);
        close.Location = new Point(w - pad - close.PreferredSize.Width + S(6), y);
        y += S(36);
        f.ClientSize = new Size(w, S(100));
        return f;
    }

    void ShowDialogOver(Form f)
    {
        f.Location = new Point(Left + (Width - f.Width) / 2, Top + Math.Max(0, (Height - f.Height) / 3));
        f.ShowDialog(this);
        f.Dispose();
    }

    /// <summary>둥근 칸 안에 테두리 없는 입력 칸</summary>
    TextBox Field(Control parent, Rectangle r, string placeholder, bool multi = false)
    {
        var box = new RoundLabel { AutoSize = false, Radius = S(8), Fill = UI.HoverBack, Bounds = r };
        var tb = new TextBox
        {
            BorderStyle = BorderStyle.None, BackColor = UI.HoverBack, Font = F(10), PlaceholderText = placeholder,
            Multiline = multi, ScrollBars = multi ? ScrollBars.Vertical : ScrollBars.None, AcceptsReturn = multi,
            // 둥근 칸(Label)은 입력기를 끄는 게 기본이라, 그대로 두면 한글이 안 써진다
            ImeMode = ImeMode.NoControl,
        };
        tb.HandleCreated += (_, _) => ImmAssociateContextEx(tb.Handle, IntPtr.Zero, 0x10); // IACE_DEFAULT: 기본 입력기를 붙인다
        int px = S(10);
        if (multi) tb.Bounds = new Rectangle(px, S(8), r.Width - px - S(4), r.Height - S(16));
        else tb.Bounds = new Rectangle(px, (r.Height - tb.PreferredHeight) / 2, r.Width - px * 2, tb.PreferredHeight);
        box.Click += (_, _) => tb.Focus();
        box.Controls.Add(tb);
        parent.Controls.Add(box);
        return tb;
    }

    void PasteBox()
    {
        int w = S(520), pad = S(18), inner = w - pad * 2;
        var f = Dialog("붙여 넣기", w, out int y);
        f.Controls.Add(new Label
        {
            Text = "AI가 만든 답을 그대로 붙여 넣으세요. 이름을 비우면 답 안의 '# 이름:'을 써요.",
            Font = F(9), ForeColor = Gray, AutoSize = true, MaximumSize = new Size(inner, 0), Location = new Point(pad, y),
        });
        y += S(28);
        var name = Field(f, new Rectangle(pad, y, inner, S(34)), "묶음 이름 (예: 토익 단어)");
        y += S(42);
        var body = Field(f, new Rectangle(pad, y, inner, S(260)), "ubiquitous|어디에나 있는|Smartphones are ubiquitous.", multi: true);
        try { if (Clipboard.ContainsText() && Clipboard.GetText().Contains('|')) body.Text = Clipboard.GetText(); } catch { }
        y += S(268);
        var err = new Label { AutoSize = true, Font = F(9), ForeColor = Red, Location = new Point(pad, y + S(8)) };
        var ok = Btn("불러오기", Color.White, UI.Accent, () =>
        {
            var text = body.Text;
            if (name.Text.Trim().Length > 0) text = "# 이름: " + name.Text.Trim() + "\n" + RemoveName(text);
            if (Take(text, "새 묶음")) f.Close();
            else err.Text = "카드를 못 찾았어요. 한 줄에 '단어|의미|첨언'처럼 | 로 나눠 주세요.";
        });
        ok.Location = new Point(w - pad - ok.Width, y);
        f.Controls.Add(err);
        f.Controls.Add(ok);
        f.ClientSize = new Size(w, y + ok.Height + S(16));
        ShowDialogOver(f);
    }

    static string RemoveName(string text) =>
        string.Join("\n", text.Replace("\r", "").Split('\n').Where(l => !l.TrimStart('#', ' ').StartsWith("이름")));

    void AskAi()
    {
        int w = S(520), pad = S(18), inner = w - pad * 2;
        var f = Dialog("AI용 양식 받기", w, out int y);
        f.Controls.Add(new Label
        {
            Text = "주제를 적고 저장하거나 복사해서 ChatGPT·Claude 같은 AI에게 주세요. AI가 답한 내용을 '붙여 넣기'로 넣으면 돼요.",
            Font = F(9), ForeColor = Gray, AutoSize = true, MaximumSize = new Size(inner, 0), Location = new Point(pad, y),
        });
        y += S(44);
        int lw = S(80), rh = S(34);
        Label L(string t, int ly) => new() { Text = t, AutoSize = true, Font = F(10), Location = new Point(pad, ly + (rh - S(20)) / 2) };

        f.Controls.Add(L("주제", y));
        var topic = Field(f, new Rectangle(pad + lw, y, inner - lw, rh), "예: 토익 빈출 단어, 정보처리기사 용어, 일본어 회화");
        y += rh + S(10);

        f.Controls.Add(L("개수", y));
        var count = new NumBox { Minimum = 5, Maximum = 300, Value = 50, Size = new Size(S(80), S(32)), Radius = S(8), Font = F(10), Location = new Point(pad + lw, y) };
        f.Controls.Add(count);
        f.Controls.Add(new Label { Text = "장", AutoSize = true, ForeColor = Gray, Location = new Point(count.Right + S(8), y + (rh - S(20)) / 2) });
        y += rh + S(10);

        f.Controls.Add(L("칸 이름", y));
        int cw = (inner - lw - S(16)) / 3;
        var cols = new TextBox[3];
        for (int i = 0; i < 3; i++)
        {
            cols[i] = Field(f, new Rectangle(pad + lw + i * (cw + S(8)), y, cw, rh), "");
            cols[i].Text = Study.WordCols[i];
        }
        y += rh + S(8);
        int qx = pad + lw;
        foreach (var (label, set) in new[] { ("단어 공부", Study.WordCols), ("문장 공부", Study.SentenceCols) })
        {
            var q = Btn(label, UI.Text, UI.HoverBack, () => { for (int i = 0; i < 3; i++) cols[i].Text = set[i]; });
            q.Font = F(8.5f); q.Size = new Size(q.Width - S(6), S(26));
            q.Location = new Point(qx, y);
            f.Controls.Add(q);
            qx = q.Right + S(6);
        }
        f.Controls.Add(new Label
        {
            Text = "칸 이름은 바꿔도 돼요 (예: 사건|연도|설명)", Font = F(8.5f), ForeColor = Gray, AutoSize = true,
            MaximumSize = new Size(w - pad - qx - S(4), 0), Location = new Point(qx + S(4), y + S(4)),
        });
        y += S(26) + S(16);

        var info = new Label { AutoSize = true, Font = F(9), ForeColor = Deep, MaximumSize = new Size(inner, 0), Location = new Point(pad, y + S(8)) };
        string Make()
        {
            var c = cols.Select((t, i) => t.Text.Replace("|", "").Trim() is { Length: > 0 } s ? s : Study.WordCols[i]).ToArray();
            var t = topic.Text.Trim();
            return Study.Prompt(t.Length > 0 ? t : "(여기에 공부할 주제를 적어 주세요)", count.Value, c);
        }
        var save = Btn("파일로 저장", Color.White, UI.Accent, () =>
        {
            var t = string.Concat(topic.Text.Trim().Split(Path.GetInvalidFileNameChars()));
            using var dlg = new SaveFileDialog
            {
                Title = "AI용 양식 저장", Filter = "글 파일 (*.txt)|*.txt",
                FileName = t.Length > 0 ? $"공부 양식 - {t}.txt" : "공부 양식.txt",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            };
            if (dlg.ShowDialog(f) != DialogResult.OK) return;
            try { File.WriteAllText(dlg.FileName, Make(), new UTF8Encoding(true)); info.ForeColor = Deep; info.Text = "저장했어요. 이 파일을 AI에게 주세요."; }
            catch (Exception ex) { Log.Write("공부", "양식을 저장하지 못함", ex); info.ForeColor = Red; info.Text = "저장하지 못했어요."; }
        });
        var copy = Btn("복사하기", UI.Text, UI.HoverBack, () =>
        {
            try { Clipboard.SetText(Make()); info.ForeColor = Deep; info.Text = "복사했어요. AI 채팅창에 붙여 넣으세요."; }
            catch { info.ForeColor = Red; info.Text = "복사하지 못했어요. 다시 눌러 주세요."; }
        });
        save.Location = new Point(w - pad - save.Width, y);
        copy.Location = new Point(save.Left - S(8) - copy.Width, y);
        f.Controls.AddRange(new Control[] { info, copy, save });
        f.ClientSize = new Size(w, y + save.Height + S(16));
        f.Shown += (_, _) => topic.Focus();
        ShowDialogOver(f);
    }

    [DllImport("imm32.dll")] static extern bool ImmAssociateContextEx(IntPtr hWnd, IntPtr hIMC, uint flags);
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);

    static void Drag(Form f, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        ReleaseCapture();
        SendMessage(f.Handle, 0xA1, (IntPtr)2, IntPtr.Zero); // 제목줄을 잡은 것처럼 끈다
    }

    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ClassStyle |= CS_DROPSHADOW; return cp; }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UI.RoundCorners(Handle);
    }
}
