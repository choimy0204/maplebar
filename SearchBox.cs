using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace MapleBar;

/// <summary>
/// 바의 닉네임 검색칸: 둥근 테두리 + 글 칸 + 돋보기 + 아래로 펼쳐지는 최근 검색 목록.
/// 목록은 사용자가 직접 한 동작(칸 누르기 · 글자 치기 · 위아래 키)으로만 열고, 닫는 길은 CloseList 하나다.
/// 포커스가 오는 것만으로는 열지 않는다: 바를 다시 누를 때(설정 버튼 등) 윈도우가 마지막 칸인
/// 검색칸에 포커스를 돌려주므로, 그걸로 열면 목록이 떴다 사라진다.
/// </summary>
sealed class SearchBox : Panel
{
    [DllImport("imm32.dll")] static extern bool ImmAssociateContextEx(IntPtr hWnd, IntPtr hIMC, uint flags);
    [DllImport("imm32.dll")] static extern IntPtr ImmGetContext(IntPtr hWnd);
    [DllImport("imm32.dll")] static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);
    [DllImport("imm32.dll")] static extern bool ImmGetConversionStatus(IntPtr hIMC, out uint conv, out uint sent);
    [DllImport("imm32.dll")] static extern bool ImmSetConversionStatus(IntPtr hIMC, uint conv, uint sent);

    readonly Form bar;
    readonly float sc;
    readonly TextBox tb = new();
    readonly RecentDrop drop;
    readonly Font textFont, iconFont;
    System.Windows.Forms.Timer? flash;
    bool settingText; // 코드가 글자를 넣는 중이면 목록을 열지 않는다
    bool? imeHangul;  // 마지막으로 쓰던 한글/영문 상태 (모르면 건드리지 않음)

    /// <summary>엔터 · 돋보기 · 목록에서 이름 고르기: 검색칸의 닉네임으로 캐릭터정보를 연다.</summary>
    public event Action? Submit;
    /// <summary>목록이 열리고 닫힐 때 (바가 메뉴 열린 동안 맨 위로 올라오지 않게 세는 데 쓴다)</summary>
    public event Action<bool>? ListVisibleChanged;

    int S(float v) => UI.Scale(v, sc);

    public SearchBox(Form bar, Settings st, float sc)
    {
        this.bar = bar;
        this.sc = sc;
        Size = new Size(S(230), S(36));
        BackColor = UI.Back;
        DoubleBuffered = true;

        textFont = new Font("맑은 고딕", 10.5f * UI.Z);
        tb.BorderStyle = BorderStyle.None;
        tb.BackColor = UI.Back; // 다크 모드에서 흰 칸이 튀지 않게 바탕과 맞춘다
        tb.ForeColor = UI.Text;
        tb.PlaceholderText = "닉네임을 입력하세요";
        tb.Font = textFont;
        tb.Width = Width - S(44);
        tb.Location = new Point(S(12), (Height - tb.PreferredHeight) / 2);

        // 🔍 글자는 파란 컬러 그림으로 나와서 튀므로, 돋보기를 글씨색으로 직접 그린다
        iconFont = new Font("Segoe UI Emoji", 10f * UI.Z);
        var icon = UI.Button(" ", "캐릭터정보 검색 (Enter)", () => { CloseList(); Submit?.Invoke(); }, sc);
        icon.Font = iconFont;
        icon.Paint += (_, e) => DrawMagnifier(e.Graphics, icon.ClientRectangle, UI.Sub);
        icon.Padding = new Padding(S(2));
        icon.Location = new Point(Width - S(34), (Height - icon.PreferredSize.Height) / 2);
        Controls.Add(tb);
        Controls.Add(icon);

        drop = new RecentDrop(bar, st, sc) { PickInfo = n => { Value = n; Submit?.Invoke(); } };
        drop.VisibleChanged += (_, _) => ListVisibleChanged?.Invoke(drop.Visible);

        // 메이플에서 넘어오면 입력기가 꺼진 채로 남아 한/영이 안 먹을 때가 있다.
        // 칸에 들어오면 입력기를 다시 붙이고, 마지막에 쓰던 한글/영문 상태로 돌려놓는다
        tb.GotFocus += (_, _) => { ImeRestore(); Invalidate(); };
        tb.LostFocus += (_, _) => { ImeRemember(); Invalidate(); BeginInvoke(CloseIfLeft); };

        // ── 여는 길: 사용자가 직접 한 동작만 ──
        tb.MouseDown += (_, _) => OpenList();
        tb.TextChanged += (_, _) => { if (!settingText && tb.Focused && Form.ActiveForm == bar) OpenList(refill: true); };
        tb.KeyDown += OnKey;

        // ── 닫는 길: 칸을 떠남 · 바가 뒤로 감 (나머지는 Esc · 고르기 · 검색 · 다른 메뉴) ──
        bar.Deactivate += (_, _) => BeginInvoke(CloseIfLeft);

        // 바는 타이머 글자 폭이 바뀌어도 가운데를 맞추며 움직이므로, 닫지 않고 목록을 따라 옮긴다
        bar.LocationChanged += (_, _) => FollowBar();
        bar.SizeChanged += (_, _) => FollowBar();
        LocationChanged += (_, _) => FollowBar();
    }

    Point DropAt => new(PointToScreen(Point.Empty).X, bar.Bottom + S(8));

    void FollowBar()
    {
        if (drop.Visible && drop.Location != DropAt) drop.Location = DropAt;
    }

    /// <summary>검색칸의 글. 코드로 넣을 때는 목록을 열지 않는다.</summary>
    public string Value
    {
        get => tb.Text;
        set { settingText = true; try { tb.Text = value; } finally { settingText = false; } }
    }

    /// <summary>글 칸에 포커스가 있고 글자가 들어 있다 (닉네임을 치는 중)</summary>
    public bool Typing => tb.Focused && tb.TextLength > 0;

    public void FocusBox(bool selectAll = false)
    {
        tb.Focus();
        if (selectAll) tb.SelectAll();
    }

    public void CloseList() => drop.Hide();

    /// <summary>닉네임 없이 검색하면 칸 색을 잠깐 바꿨다 되돌린다 (다시 누르면 처음부터 0.4초)</summary>
    public void Flash()
    {
        BackColor = UI.T(Color.FromArgb(255, 240, 230));
        if (flash == null)
        {
            flash = new System.Windows.Forms.Timer { Interval = 400 };
            flash.Tick += (_, _) => { flash.Stop(); BackColor = UI.Back; };
        }
        flash.Stop();
        flash.Start();
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        tb.Focus();
        OpenList();
    }

    /// <summary>목록을 연다. 이미 열려 있으면 글자가 바뀐 경우(refill)에만 다시 채운다.</summary>
    void OpenList(bool refill = false)
    {
        if (drop.Visible && !refill) return;
        // 글이 통째로 골라져 있으면(막 들어온 상태) 걸러 내지 않고 모두 보여 준다
        string filter = tb.SelectionLength > 0 && tb.SelectionLength == tb.TextLength ? "" : tb.Text.Trim();
        drop.Open(DropAt, Width, filter);
    }

    void CloseIfLeft()
    {
        if (tb.Focused && Form.ActiveForm == bar) return;
        CloseList();
    }

    void OnKey(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Enter:
                e.SuppressKeyPress = true;
                if (drop.Selected is string pick) Value = pick; // 위아래 키로 고른 줄만 (마우스로 가리킨 줄은 아님)
                CloseList();
                Submit?.Invoke();
                break;
            case Keys.Escape:
                e.SuppressKeyPress = true;
                if (drop.Visible) CloseList(); else Value = "";
                break;
            case Keys.Down or Keys.Up:
                e.Handled = true;
                if (!drop.Visible) OpenList(); else drop.Step(e.KeyCode == Keys.Down ? 1 : -1);
                break;
        }
    }

    void ImeRemember()
    {
        var h = tb.Handle;
        var c = ImmGetContext(h);
        if (c == IntPtr.Zero) return;
        if (ImmGetConversionStatus(c, out uint conv, out _)) imeHangul = (conv & 1) != 0; // IME_CMODE_NATIVE
        ImmReleaseContext(h, c);
    }

    void ImeRestore()
    {
        var h = tb.Handle;
        ImmAssociateContextEx(h, IntPtr.Zero, 0x10); // IACE_DEFAULT: 기본 입력기를 다시 붙인다
        if (imeHangul is not bool want) return;
        var c = ImmGetContext(h);
        if (c == IntPtr.Zero) return;
        if (ImmGetConversionStatus(c, out uint conv, out uint sent) && ((conv & 1) != 0) != want)
            ImmSetConversionStatus(c, want ? conv | 1 : conv & ~1u, sent);
        ImmReleaseContext(h, c);
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

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = UI.RoundRect(r, S(10) / 2f); // 지름이 S(10)이 되는 반지름
        using var pen = new Pen(tb.Focused ? UI.Accent : UI.Line, Math.Max(1, S(1)));
        g.DrawPath(pen, path);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing); // 컨트롤이 먼저 정리된 뒤에 글꼴을 푼다
        if (!disposing) return;
        flash?.Dispose();
        drop.Dispose();
        textFont.Dispose();
        iconFont.Dispose();
    }
}
