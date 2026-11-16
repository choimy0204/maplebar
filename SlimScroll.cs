namespace MapleBar;

/// <summary>얇은 스크롤 칸: 기본 스크롤바 대신 오른쪽에 둥근 막대를 그린다. 내용(Content)은 높이가 저절로 늘어나는 컨트롤을 넣는다.</summary>
sealed class SlimScroll : Panel
{
    public readonly Control Content;
    readonly Bar bar;
    readonly float sc;
    int pos;
    int S(float v) => UI.Scale(v, sc);
    int Max => Math.Max(0, Content.Height - ClientSize.Height);

    public SlimScroll(Control content, float sc)
    {
        this.sc = sc;
        Content = content;
        DoubleBuffered = true;
        BackColor = UI.Back;
        SetStyle(ControlStyles.Selectable, true);
        bar = new Bar(this) { Width = S(12) };
        Controls.Add(content);
        Controls.Add(bar);
        bar.BringToFront();
        content.SizeChanged += (_, _) => ScrollTo(pos);
        Resize += (_, _) => { bar.Bounds = new Rectangle(ClientSize.Width - bar.Width, 0, bar.Width, ClientSize.Height); ScrollTo(pos); };
        MouseEnter += (_, _) => { if (FocusOnHover) Focus(); }; // 휠로 내려 보게
    }

    /// <summary>
    /// 마우스를 올리면 키보드를 가져온다 (휠이 바로 먹게). 안에 글 칸이 있으면 끈다:
    /// 쓰던 칸에서 키보드를 뺏는다. 꺼도 윈도우가 휠을 마우스 아래 칸에 보내므로 휠은 된다
    /// </summary>
    public bool FocusOnHover { get; set; } = true;

    public int Pos => pos;

    public void ScrollTo(int y)
    {
        pos = Math.Clamp(y, 0, Max);
        if (Content.Top != -pos) Content.Top = -pos;
        bar.Visible = Max > 0;
        bar.Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        ScrollTo(pos - e.Delta * S(48) / 120);
        if (e is HandledMouseEventArgs h) h.Handled = true;
    }

    /// <summary>오른쪽 막대: 평소엔 연한 회색, 올리면 진하게, 끌어서 옮길 수 있다.</summary>
    sealed class Bar : Control
    {
        readonly SlimScroll box;
        bool hot, drag;
        int grab;
        public Bar(SlimScroll box)
        {
            this.box = box;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = UI.Back;
            Cursor = Cursors.Hand;
        }

        int Pad => box.S(4);
        int Track => Height - Pad * 2;
        int ThumbH => box.Content.Height <= 0 ? Track : Math.Clamp(Track * box.ClientSize.Height / box.Content.Height, box.S(28), Track);
        int ThumbY => Pad + (box.Max == 0 ? 0 : (Track - ThumbH) * box.pos / box.Max);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            int w = hot || drag ? box.S(8) : box.S(5), x = Width - w - box.S(2);
            var c = drag ? UI.T(Color.FromArgb(150, 150, 150)) : hot ? UI.T(Color.FromArgb(185, 185, 185)) : UI.T(Color.FromArgb(215, 215, 215));
            using var b = new SolidBrush(c);
            using var p = UI.RoundRect(new Rectangle(x, ThumbY, w, ThumbH), w / 2f);
            g.FillPath(b, p);
        }

        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            int ty = ThumbY, th = ThumbH;
            if (e.Y < ty || e.Y > ty + th) { grab = th / 2; MoveTo(e.Y); } // 빈 곳을 누르면 그 자리로
            else grab = e.Y - ty;
            drag = true; Capture = true; Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e) { if (drag) MoveTo(e.Y); base.OnMouseMove(e); }
        protected override void OnMouseUp(MouseEventArgs e) { drag = false; Capture = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnMouseWheel(MouseEventArgs e) => box.OnMouseWheel(e);

        void MoveTo(int y)
        {
            int room = Track - ThumbH;
            if (room > 0) box.ScrollTo((y - grab - Pad) * box.Max / room);
        }
    }
}
