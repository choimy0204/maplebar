namespace MapleBar;

/// <summary>
/// 계산기 안의 넓은 [스크립트] 화면. 왼쪽은 스크립트 목록, 오른쪽은 고른 스크립트의 이름·식·값 칸·결과.
/// 식에 쓴 이름마다 값 칸이 생기고, 값을 넣는 즉시 결과가 나온다. 넣은 값도 스크립트마다 남는다.
/// </summary>
sealed partial class CalcForm
{
    bool scriptView;                 // 스크립트 화면을 보는 중
    int? leftBefore;                 // 넓히다 화면 밖으로 나가 창을 옮겼으면 원래 왼쪽 자리
    Panel scriptPanel = null!;
    Panel listInner = null!, editInner = null!;
    SlimScroll listScroll = null!, editScroll = null!;

    CalcScript? script;              // 고른 스크립트 (null이면 고르지 않음)
    Panel? varPanel;                 // 변수 값 칸들
    RoundLabel? resultCard;
    Label? resultL, resultUnit, scriptNoteL, selNameL, selInfoL;
    RoundLabel? copyBtn, sendBtn;
    TextBox? exprBox;
    List<string> varNames = new();   // 지금 값 칸을 만든 변수 이름들
    readonly List<TextBox> varBoxes = new();
    decimal? scriptValue;            // 지금 결과 (식이 틀렸거나 값이 빠졌으면 null)

    int ScriptW => S(660);
    int ListW => S(200);

    static string ScriptName(CalcScript s) => s.Title.Trim().Length > 0 ? s.Title.Trim() : "이름 없는 스크립트";

    /// <summary>생성자에서 한 번: 스크립트 화면의 틀을 만든다 (처음엔 숨김)</summary>
    void BuildScriptPanel()
    {
        scriptPanel = new Panel { Location = new Point(0, bodyTop), Size = new Size(ScriptW, bodyH), BackColor = UI.Back, Visible = false };

        int lw = ListW - S(12);
        var add = new RoundLabel
        {
            Text = "+ 새 스크립트", UseMnemonic = false, AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Cursor = Cursors.Hand,
            Font = F(9.5f, FontStyle.Bold), Radius = S(8), Size = new Size(lw, S(34)), Location = new Point(pad, 0),
            Fill = UI.AmberSoft, ForeColor = UI.AmberText,
        };
        var addHover = UI.Dark ? ControlPaint.Light(UI.AmberSoft, 0.15f) : ControlPaint.Dark(UI.AmberSoft, 0.04f);
        add.MouseEnter += (_, _) => add.Fill = addHover;
        add.MouseLeave += (_, _) => add.Fill = UI.AmberSoft;
        add.Click += (_, _) => NewScript();
        scriptPanel.Controls.Add(add);

        listInner = new Panel { Location = Point.Empty, Size = new Size(lw, 1), BackColor = UI.Back };
        listScroll = new SlimScroll(listInner, sc) { Location = new Point(pad, S(42)), Size = new Size(ListW, bodyH - S(42)), FocusOnHover = false };
        scriptPanel.Controls.Add(listScroll);

        int lineX = pad + ListW + S(4);
        scriptPanel.Controls.Add(new Panel { Location = new Point(lineX, 0), Size = new Size(1, bodyH), BackColor = UI.Line });

        int rx = lineX + S(14);
        int rw = ScriptW - rx - S(8);
        editInner = new Panel { Location = Point.Empty, Size = new Size(rw - S(12), 1), BackColor = UI.Back };
        editScroll = new SlimScroll(editInner, sc) { Location = new Point(rx, 0), Size = new Size(rw, bodyH), FocusOnHover = false };
        scriptPanel.Controls.Add(editScroll);

        Controls.Add(scriptPanel);
    }

    /// <summary>계산기 ↔ 스크립트 화면. 스크립트 화면은 넓게, 돌아오면 원래 크기로</summary>
    void SetScriptView(bool on)
    {
        if (ActiveControl is TextBox) Unfocus(); // 쓰던 칸을 먼저 닫아 저장한다
        scriptView = on;
        if (st.CalcScriptView != on) { st.CalcScriptView = on; st.Save(); }
        SuspendLayout();
        foreach (var c in calcParts) c.Visible = !on;
        scriptPanel.Visible = on;
        title.Text = on ? "스크립트" : "계산기";
        scriptBtn.Text = on ? "‹ 계산기" : "스크립트";
        tips.SetToolTip(scriptBtn, on ? "계산기로 돌아가기" : "식을 저장해 두고 값만 넣어 계산하는 화면");
        if (on)
        {
            ShowScripts();
            ClientSize = new Size(ScriptW, bottom);
            close.Location = new Point(ScriptW - pad - close.PreferredSize.Width + S(6), S(12));
            scriptBtn.Location = new Point(close.Left - scriptBtn.PreferredSize.Width - S(2), S(12));
            if (IsHandleCreated)
            {
                var wa = Screen.FromControl(this).WorkingArea;
                if (Right > wa.Right) { leftBefore = Left; Left = Math.Max(wa.Left, wa.Right - Width); }
            }
        }
        else
        {
            SetHistoryOpen(st.CalcHistoryOpen); // 기록 칸도 원래대로 숨기거나 보인다
            if (leftBefore is int l) { Left = l; leftBefore = null; }
        }
        ResumeLayout();
    }

    /// <summary>목록과 오른쪽 칸을 다시 그린다. 고른 것이 없으면 첫 스크립트를 고른다</summary>
    void ShowScripts()
    {
        if (st.CalcScriptOpen < 0 || st.CalcScriptOpen >= st.CalcScripts.Count)
            st.CalcScriptOpen = st.CalcScripts.Count > 0 ? 0 : -1;
        int i = st.CalcScriptOpen;
        script = i >= 0 ? st.CalcScripts[i] : null;
        BuildScriptList();
        BuildScriptEditor();
    }

    /// <summary>안에 든 것에 맞춰 내려 볼 길이를 정한다</summary>
    void FitScroll(SlimScroll box)
    {
        int h = 1;
        foreach (Control c in box.Content.Controls) if (!c.IsDisposed) h = Math.Max(h, c.Bottom + S(2));
        box.Content.Height = h;
        box.ScrollTo(box.Pos);
    }

    static void Clear(Panel p)
    {
        foreach (var c in p.Controls.Cast<Control>().ToList()) c.Dispose();
    }

    // ── 왼쪽 목록 ──

    void BuildScriptList()
    {
        listInner.SuspendLayout();
        Clear(listInner);
        selNameL = selInfoL = null;
        int w = listInner.Width;
        if (st.CalcScripts.Count == 0)
        {
            var e = Empty("아직 스크립트가 없어요", w);
            e.Height = S(48);
            listInner.Controls.Add(e);
        }
        int y = 0;
        for (int k = 0; k < st.CalcScripts.Count; k++)
        {
            var s = st.CalcScripts[k];
            int idx = k;
            bool sel = s == script;
            var row = new RoundLabel
            {
                AutoSize = false, Size = new Size(w, S(48)), Location = new Point(0, y), Radius = S(8), Cursor = Cursors.Hand,
                Fill = sel ? UI.AmberSoft : Color.Transparent,
            };
            var name = new Label
            {
                Text = ScriptName(s), AutoSize = false, Size = new Size(w - S(36), S(22)), Location = new Point(S(6), S(3)), Font = F(9.5f, FontStyle.Bold),
                ForeColor = sel ? UI.AmberText : UI.Text,
                TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false, AutoEllipsis = true, BackColor = Color.Transparent, Cursor = Cursors.Hand,
            };
            var info = new Label
            {
                Text = ExprPreview(s), AutoSize = false, Size = new Size(w - S(12), S(18)), Location = new Point(S(6), S(26)),
                Font = F(8.5f), ForeColor = UI.Sub, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false, AutoEllipsis = true,
                BackColor = Color.Transparent, Cursor = Cursors.Hand,
            };
            var del = new Label
            {
                Text = "✕", AutoSize = false, Size = new Size(S(24), S(22)), Location = new Point(w - S(28), S(3)), Font = F(9.5f),
                ForeColor = UI.Sub, TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.Transparent, Cursor = Cursors.Hand,
            };
            tips.SetToolTip(del, "지우기");
            row.Controls.AddRange(new Control[] { name, info, del });
            if (!sel) Hover(row, name, info, del); // 고른 줄은 칠한 그대로 둔다
            else { selNameL = name; selInfoL = info; }
            foreach (Control c in new Control[] { row, name, info }) c.Click += (_, _) => OpenScript(idx);
            del.Click += (_, _) => DeleteScript(s);
            listInner.Controls.Add(row);
            y += S(48) + S(2);
        }
        listInner.ResumeLayout();
        FitScroll(listScroll);
    }

    static string ExprPreview(CalcScript s) => s.Expr.Trim().Length > 0 ? s.Expr.Trim() : "식이 비어 있어요";

    void NewScript()
    {
        int n = 1;
        while (st.CalcScripts.Any(s => s.Title.Trim() == $"스크립트 {n}")) n++;
        st.CalcScripts.Add(new CalcScript { Title = $"스크립트 {n}" });
        OpenScript(st.CalcScripts.Count - 1);
        listScroll.ScrollTo(int.MaxValue); // 새 줄이 보이게
        BeginInvoke(() => exprBox?.Focus());
    }

    void OpenScript(int i)
    {
        if (ActiveControl is TextBox) Unfocus();
        st.CalcScriptOpen = i;
        st.Save();
        ShowScripts();
    }

    void DeleteScript(CalcScript s)
    {
        if (!AskBox.Show(this, $"'{ScriptName(s)}' 스크립트를 지울까요?", "지우면 되돌릴 수 없어요.", "지우기")) return;
        if (ActiveControl is TextBox) Unfocus();
        int at = st.CalcScripts.IndexOf(s);
        st.CalcScripts.Remove(s);
        if (s == script) st.CalcScriptOpen = Math.Min(at, st.CalcScripts.Count - 1);
        else st.CalcScriptOpen = script == null ? -1 : st.CalcScripts.IndexOf(script);
        st.Save();
        BeginInvoke((Action)ShowScripts); // 누른 ✕를 그 일 안에서 없애지 않게
    }

    // ── 오른쪽: 고른 스크립트 ──

    void BuildScriptEditor()
    {
        editInner.SuspendLayout();
        Clear(editInner);
        editScroll.ScrollTo(0);
        varPanel = null; resultCard = null; resultL = resultUnit = scriptNoteL = null; copyBtn = sendBtn = null; exprBox = null;
        varNames = new(); varBoxes.Clear(); scriptValue = null;
        int w = editInner.Width;
        var s = script;
        if (s == null)
        {
            var e = Empty("왼쪽에서 스크립트를 고르거나 새로 만들어요", w);
            e.Top = S(40); e.Height = S(48);
            editInner.Controls.Add(e);
            editInner.ResumeLayout();
            FitScroll(editScroll);
            return;
        }

        var del = UI.Button("지우기", "이 스크립트 지우기", () => DeleteScript(s), sc, UI.Sub);
        del.Font = F(9f);
        int nameH = S(36);
        del.Location = new Point(w - del.PreferredSize.Width, (nameH - del.PreferredSize.Height) / 2);
        editInner.Controls.Add(del);

        var (nameBack, nameTb) = Field(new Rectangle(0, 0, del.Left - S(8), nameH), "스크립트 이름", F(11f, FontStyle.Bold), false);
        nameTb.MaxLength = 40;
        nameTb.Text = s.Title;
        nameTb.TextChanged += (_, _) => { s.Title = nameTb.Text; if (selNameL != null) selNameL.Text = ScriptName(s); };
        editInner.Controls.Add(nameBack);
        int y = nameH + S(12);

        editInner.Controls.Add(Caption("식", y, w));
        y += S(22);
        var (exprBack, exprTb) = Field(new Rectangle(0, y, w, S(68)), "식 쓰기", F(11f), true);
        exprBox = exprTb;
        exprTb.MaxLength = 500;
        exprTb.Text = s.Expr;
        exprTb.TextChanged += (_, _) =>
        {
            if (exprTb.Text.Contains('\n') || exprTb.Text.Contains('\r'))
            {
                int at = exprTb.SelectionStart;
                exprTb.Text = exprTb.Text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
                exprTb.SelectionStart = Math.Min(at, exprTb.Text.Length);
                return; // 위에서 Text를 바꿔 이 일이 다시 불린다
            }
            s.Expr = exprTb.Text;
            if (selInfoL != null) selInfoL.Text = ExprPreview(s);
            RefreshVars(s, w);
        };
        tips.SetToolTip(exprTb, "+ − × ÷ ^ 괄호를 쓸 수 있어요. 글자로 쓴 이름마다 값 칸이 생겨요");
        editInner.Controls.Add(exprBack);
        y += S(68) + S(14);

        editInner.Controls.Add(Caption("값", y, w));
        y += S(22);
        varPanel = new Panel { Location = new Point(0, y), Width = w, Height = 0, BackColor = UI.Back };
        editInner.Controls.Add(varPanel);

        resultCard = new RoundLabel { AutoSize = false, Size = new Size(w, S(66)), Radius = S(8), Fill = UI.HoverBack, Cursor = Cursors.Hand };
        resultL = new Label
        {
            AutoSize = false, Size = new Size(w - S(20), S(34)), Location = new Point(S(10), S(4)), Font = F(16f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleRight, UseMnemonic = false, AutoEllipsis = true, BackColor = Color.Transparent, Cursor = Cursors.Hand,
        };
        resultUnit = new Label
        {
            AutoSize = false, Size = new Size(w - S(20), S(22)), Location = new Point(S(10), S(38)), Font = F(9.5f, FontStyle.Bold),
            ForeColor = UI.AmberText, TextAlign = ContentAlignment.MiddleRight, UseMnemonic = false, AutoEllipsis = true, BackColor = Color.Transparent, Cursor = Cursors.Hand,
        };
        resultCard.Controls.Add(resultL);
        resultCard.Controls.Add(resultUnit);
        foreach (Control c in new Control[] { resultCard, resultL, resultUnit }) { c.Click += (_, _) => CopyScript(); tips.SetToolTip(c, "눌러서 결과 복사"); }
        editInner.Controls.Add(resultCard);

        sendBtn = UI.Button("계산기로", "결과를 계산기에 넣고 이어서 계산", () => SendScript(s), sc, UI.AmberText, UI.AmberSoft);
        sendBtn.Font = F(9.5f, FontStyle.Bold);
        copyBtn = UI.Button("복사", "결과 복사", CopyScript, sc, UI.Sub);
        copyBtn.Font = F(9.5f);
        scriptNoteL = new Label { AutoSize = false, ForeColor = UI.Sub, Font = F(9f), TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false };
        editInner.Controls.Add(sendBtn);
        editInner.Controls.Add(copyBtn);
        editInner.Controls.Add(scriptNoteL);

        RefreshVars(s, w);
        editInner.ResumeLayout();
    }

    Label Caption(string text, int y, int w) => new()
    {
        Text = text, AutoSize = false, Size = new Size(w, S(20)), Location = new Point(S(2), y), Font = F(9f, FontStyle.Bold),
        ForeColor = UI.Sub, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false,
    };

    /// <summary>글 칸: 둥근 칸 안에 테두리 없는 글 칸. 바탕이 Label이라 한글 입력이 꺼지지 않게 ImeMode를 따로 준다</summary>
    (RoundLabel back, TextBox tb) Field(Rectangle r, string placeholder, Font font, bool multi)
    {
        var back = new RoundLabel { AutoSize = false, Bounds = r, Radius = S(8), Fill = UI.HoverBack, Cursor = Cursors.IBeam };
        var tb = new TextBox
        {
            BorderStyle = BorderStyle.None, BackColor = UI.HoverBack, ForeColor = UI.Text, Font = font,
            PlaceholderText = placeholder, ImeMode = ImeMode.NoControl, Multiline = multi, WordWrap = multi, TabStop = true,
        };
        tb.Width = r.Width - S(20);
        if (multi) { tb.Height = r.Height - S(14); tb.Location = new Point(S(10), S(7)); }
        else tb.Location = new Point(S(10), (r.Height - tb.Height) / 2);
        back.Controls.Add(tb);
        back.Click += (_, _) => tb.Focus();
        tb.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; Unfocus(); }
            else if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; NextBox(tb); }
        };
        tb.Leave += (_, _) => st.Save();
        return (back, tb);
    }

    /// <summary>Enter: 다음 값 칸으로. 마지막 칸이면 칸에서 나온다</summary>
    void NextBox(TextBox tb)
    {
        int i = varBoxes.IndexOf(tb);
        var next = i < 0 ? varBoxes.FirstOrDefault() : i + 1 < varBoxes.Count ? varBoxes[i + 1] : null;
        if (next != null) { next.Focus(); next.SelectAll(); }
        else Unfocus();
    }

    /// <summary>식에 쓴 이름이 바뀌었으면 값 칸을 다시 만들고, 결과를 다시 계산한다. 값 칸이 많으면 두 줄로 놓는다</summary>
    void RefreshVars(CalcScript s, int w)
    {
        if (varPanel == null) return;
        var names = ScriptMath.Names(s.Expr);
        if (!names.SequenceEqual(varNames) || varPanel.Controls.Count == 0)
        {
            varNames = names;
            varPanel.SuspendLayout();
            foreach (var c in varPanel.Controls.Cast<Control>().ToList()) c.Dispose();
            varBoxes.Clear();
            int rowH = S(34), gap = S(14);
            int cols = names.Count >= 4 ? 2 : 1;
            int colW = (w - gap * (cols - 1)) / cols;
            int nameW = Math.Min(S(100), colW * 2 / 5);
            int h = 0;
            if (names.Count == 0)
            {
                varPanel.Controls.Add(new Label
                {
                    Text = "식에 글자로 이름을 쓰면 여기에 값 칸이 생겨요", AutoSize = false, Size = new Size(w, S(36)), Location = new Point(0, 0),
                    ForeColor = UI.Sub, Font = F(9f), TextAlign = ContentAlignment.MiddleCenter,
                });
                h = S(36);
            }
            for (int k = 0; k < names.Count; k++)
            {
                string n = names[k], key = ScriptMath.Key(n);
                int x = (k % cols) * (colW + gap), y = (k / cols) * (rowH + S(6));
                varPanel.Controls.Add(new Label
                {
                    Text = n, AutoSize = false, Size = new Size(nameW, rowH), Location = new Point(x + S(2), y), Font = F(10f, FontStyle.Bold),
                    TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false, AutoEllipsis = true,
                });
                var (vb, tb) = Field(new Rectangle(x + nameW + S(6), y, colW - nameW - S(6), rowH), "값", F(10.5f), false);
                tb.MaxLength = 60;
                tb.TextAlign = HorizontalAlignment.Right;
                tb.Text = s.Values.TryGetValue(key, out var old) ? old : "";
                tb.TextChanged += (_, _) => { s.Values[key] = tb.Text; Recalc(s); };
                varBoxes.Add(tb);
                varPanel.Controls.Add(vb);
                h = y + rowH;
            }
            varPanel.Height = h;
            varPanel.ResumeLayout();
            if (resultCard != null && copyBtn != null && sendBtn != null && scriptNoteL != null)
            {
                resultCard.Top = varPanel.Bottom + S(14);
                int by = resultCard.Bottom + S(8);
                sendBtn.Location = new Point(w - sendBtn.PreferredSize.Width, by);
                copyBtn.Location = new Point(sendBtn.Left - copyBtn.PreferredSize.Width - S(4), by);
                scriptNoteL.Bounds = new Rectangle(S(2), by, Math.Max(1, copyBtn.Left - S(8)), sendBtn.PreferredSize.Height);
                FitScroll(editScroll); // 값 칸 수가 바뀌면 내려 볼 길이도 바뀐다
            }
        }
        Recalc(s);
    }

    /// <summary>값을 모아 식을 계산해 결과 칸에 보인다. 틀렸거나 값이 빠졌으면 그 까닭을 짧게 보인다</summary>
    void Recalc(CalcScript s)
    {
        if (resultL == null || resultUnit == null) return;
        string? err = ScriptMath.Check(s.Expr);
        decimal v = 0;
        if (err == null)
        {
            var vals = new Dictionary<string, decimal>();
            var missing = new List<string>();
            foreach (var n in varNames)
            {
                string key = ScriptMath.Key(n);
                string text = s.Values.TryGetValue(key, out var t) ? t.Trim() : "";
                if (text.Length == 0) { missing.Add(n); continue; }
                try { vals[key] = ScriptMath.Eval(text, _ => throw new ScriptError("")); }
                catch (Exception) { err ??= $"{n} 값이 숫자가 아니에요"; }
            }
            if (err == null && missing.Count > 0) err = $"{string.Join(", ", missing)} 값을 넣어 주세요";
            if (err == null)
            {
                try { v = Math.Round(ScriptMath.Eval(s.Expr, n => vals[ScriptMath.Key(n)]), 10); }
                catch (ScriptError e) { err = e.Message; }
                catch (DivideByZeroException) { err = "0으로 나눌 수 없어요"; }
                catch (OverflowException) { err = "수가 너무 커요"; }
                catch (Exception ex) { Log.Write("calc", "스크립트를 계산하지 못함", ex); err = "식이 틀렸어요"; }
            }
        }
        scriptValue = err == null ? v : null;
        resultL.Text = err ?? Fmt(v);
        resultL.Font = err == null ? F(16f, FontStyle.Bold) : F(10f);
        resultL.ForeColor = err == null ? UI.Text : UI.Sub;
        resultUnit.Text = err == null ? Korean(v) : "";
    }

    void CopyScript()
    {
        if (scriptValue is not decimal v) { Note("결과가 아직 없어요"); Render(); return; }
        try { Clipboard.SetText(Plain(v)); Note("복사했어요"); }
        catch (Exception ex) { Log.Write("calc", "스크립트 결과를 복사하지 못함", ex); Note("복사하지 못했어요"); }
        Render();
    }

    /// <summary>결과를 계산기에 넣고 계산기 화면으로 돌아간다. 기록에도 "스크립트 이름 =" 줄로 남는다</summary>
    void SendScript(CalcScript s)
    {
        if (scriptValue is not decimal v) { Note("결과가 아직 없어요"); Render(); return; }
        if (ActiveControl is TextBox) Unfocus();
        string expr = ScriptName(s) + " =";
        parts.Clear();
        cur = Plain(v);
        shown = v;
        lastExpr = expr;
        justDone = true;
        AddHistory(expr, v);
        SetScriptView(false);
        Note("계산기로 보냈어요");
        Render();
    }
}
