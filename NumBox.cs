using System.Drawing.Drawing2D;

namespace MapleBar;

/// <summary>둥근 네모 숫자 칸. 오른쪽 ˄ ˅ 를 누르거나(누르고 있으면 계속), 휠을 굴리거나, 칸에 숫자를 쳐서 바꾼다.
/// 숫자 부분은 진짜 글 칸이라 깜빡이는 커서, 드래그 선택, ←→, Delete, 붙여 넣기가 보통 칸처럼 된다.</summary>
public sealed class NumBox : Control
{
    int value, min, max = 99;
    public int Minimum { get => min; set { min = value; Value = Value; Sync(); } }
    public int Maximum { get => max; set { max = value; tb.MaxLength = Math.Max(1, max.ToString().Length); Value = Value; Sync(); } }
    public int Value
    {
        get => value;
        set
        {
            int v = Math.Clamp(value, min, max);
            if (!typing) Sync(v);
            if (v == this.value) return;
            this.value = v;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public event EventHandler? ValueChanged;
    public int Radius { get; set; } = 8;
    public Color Fill { get; set; } = UI.HoverBack;

    readonly TextBox tb = new()
    {
        BorderStyle = BorderStyle.None, TextAlign = HorizontalAlignment.Center, ImeMode = ImeMode.Disable,
        ShortcutsEnabled = true, Text = "0", MaxLength = 2,
    };
    readonly System.Windows.Forms.Timer hold = new() { Interval = 400 };
    int holdDir, hover;           // hover: 1 = 위, -1 = 아래, 0 = 없음
    bool typing;                  // 글 칸에서 치는 중이면 글자를 덮어쓰지 않는다

    public NumBox()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.IBeam;
        tb.BackColor = Fill;
        Controls.Add(tb);
        hold.Tick += (_, _) => { hold.Interval = 60; Value += holdDir; };

        // 숫자와 지우기·이동 같은 조작 키만 받는다
        tb.KeyPress += (_, e) => { if (!char.IsDigit(e.KeyChar) && !char.IsControl(e.KeyChar)) e.Handled = true; };
        tb.KeyDown += (_, e) =>
        {
            switch (e.KeyCode)
            {
                case Keys.Up: Commit(); Value++; tb.SelectAll(); break;
                case Keys.Down: Commit(); Value--; tb.SelectAll(); break;
                case Keys.Enter: Commit(); tb.SelectAll(); break;
                case Keys.Escape: Sync(); tb.SelectAll(); break;
                default: return;
            }
            e.Handled = e.SuppressKeyPress = true;
        };
        // 치는 동안 범위 안의 숫자면 바로 반영한다 (범위 밖이면 칸을 떠날 때 맞춘다)
        tb.TextChanged += (_, _) =>
        {
            if (!tb.Focused) return;
            var digits = new string(tb.Text.Where(char.IsDigit).ToArray());
            if (digits != tb.Text) { int c = tb.SelectionStart; tb.Text = digits; tb.SelectionStart = Math.Min(c, digits.Length); return; }
            if (int.TryParse(digits, out int v) && v >= min && v <= max) { typing = true; Value = v; typing = false; }
        };
        // Tab으로 들어오면 전체 선택, 마우스로 누르면 누른 자리에 커서
        tb.Enter += (_, _) => { Paint2(); if (MouseButtons == MouseButtons.None) BeginInvoke(tb.SelectAll); };
        tb.Leave += (_, _) => { Commit(); hold.Stop(); Paint2(); };
        tb.MouseWheel += (_, e) => Wheel(e);
        Sync();
    }

    int ArrowW => Height * 2 / 3;
    int Dir(Point p) => p.X < Width - ArrowW ? 0 : p.Y < Height / 2 ? 1 : -1;

    /// <summary>글 칸의 글자를 지금 값으로 맞춘다</summary>
    void Sync(int? v = null)
    {
        var s = (v ?? value).ToString();
        if (tb.Text != s) tb.Text = s;
    }

    /// <summary>친 글자를 값으로 확정한다. 비었거나 범위 밖이면 범위 안으로 맞춘다</summary>
    void Commit()
    {
        if (int.TryParse(tb.Text, out int v)) Value = v;
        Sync();
    }

    void Paint2()
    {
        tb.BackColor = tb.Focused ? Color.White : Fill;
        Invalidate();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        int left = Height / 6, w = Width - ArrowW + Height / 8 - left * 2;
        tb.Width = Math.Max(8, w);
        tb.Location = new Point(left, (Height - tb.Height) / 2);
    }

    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); tb.Font = Font; PerformLayout(); }
    protected override void OnForeColorChanged(EventArgs e) { base.OnForeColorChanged(e); tb.ForeColor = ForeColor; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        bool focus = tb.Focused;
        using (var path = UI.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Radius))
        {
            using var b = new SolidBrush(focus ? Color.White : Fill);
            g.FillPath(b, path);
            if (focus) { using var pen = new Pen(UI.Accent, 1.5f); g.DrawPath(pen, path); }
        }
        // ˄ ˅
        int ax = Width - ArrowW, cx = ax + ArrowW / 2 - 2, s = Math.Max(3, Height / 9);
        for (int k = 0; k < 2; k++)
        {
            int dir = k == 0 ? 1 : -1;
            bool can = dir > 0 ? value < max : value > min;
            var c = !can ? Color.FromArgb(200, 200, 206) : hover == dir ? UI.Accent : Color.FromArgb(120, 120, 128);
            int cy = k == 0 ? Height / 2 - s - 1 : Height / 2 + s + 1;
            var pts = dir > 0
                ? new[] { new Point(cx - s, cy + s / 2), new Point(cx, cy - s / 2), new Point(cx + s, cy + s / 2) }
                : new[] { new Point(cx - s, cy - s / 2), new Point(cx, cy + s / 2), new Point(cx + s, cy - s / 2) };
            using var pen = new Pen(c, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            g.DrawLines(pen, pts);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int d = Dir(e.Location);
        Cursor = d == 0 ? Cursors.IBeam : Cursors.Hand;
        if (d != hover) { hover = d; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = 0; Invalidate(); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        int d = Dir(e.Location);
        if (d == 0)
        {
            // 숫자 둘레 여백을 눌러도 글 칸으로
            tb.Focus();
            tb.SelectionStart = e.X < tb.Left ? 0 : tb.TextLength;
            return;
        }
        if (!tb.Focused) tb.Focus();
        Commit();
        Value += d;
        holdDir = d; hold.Interval = 400; hold.Start();
    }

    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); hold.Stop(); }

    protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); Wheel(e); }

    void Wheel(MouseEventArgs e)
    {
        Commit();
        Value += Math.Sign(e.Delta);
        if (tb.Focused) tb.SelectAll();
        if (e is HandledMouseEventArgs h) h.Handled = true; // 창이 같이 스크롤되지 않게
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); tb.Focus(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing) hold.Dispose();
        base.Dispose(disposing);
    }
}
