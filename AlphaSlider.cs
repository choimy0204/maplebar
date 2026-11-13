using System.Drawing.Drawing2D;

namespace MapleBar;

/// <summary>사이트 창 제목 줄의 작은 투명도 슬라이드 바 (Min~100%). 끌거나 누르거나 휠로 바꾼다.</summary>
sealed class AlphaSlider : Control
{
    public const int Min = 30;
    int value = 100;
    bool drag;
    // 색이 고정인 붓은 한 번만 만든다
    readonly SolidBrush softBrush = new(UI.SoftAccent);
    readonly SolidBrush accentBrush = new(UI.Accent);
    readonly SolidBrush whiteBrush = new(Color.White);
    public event Action<int>? Changed;

    public AlphaSlider()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Color.White;
        Cursor = Cursors.Hand;
    }

    public int Value
    {
        get => value;
        set { value = Math.Clamp(value, Min, 100); if (this.value == value) return; this.value = value; Invalidate(); }
    }

    int Pad => Height / 3;
    float KnobX => Pad + (Width - Pad * 2) * (value - Min) / (float)(100 - Min);

    void SetFrom(int x)
    {
        int v = Min + (int)Math.Round((x - Pad) / (float)Math.Max(1, Width - Pad * 2) * (100 - Min));
        v = Math.Clamp(v, Min, 100);
        if (v == value) return;
        Value = v;
        Changed?.Invoke(v);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        drag = true; Capture = true; SetFrom(e.X);
    }
    protected override void OnMouseMove(MouseEventArgs e) { if (drag) SetFrom(e.X); }
    protected override void OnMouseUp(MouseEventArgs e) { drag = false; Capture = false; }
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        int v = Math.Clamp(value + (e.Delta > 0 ? 5 : -5), Min, 100);
        if (v == value) return;
        Value = v; Changed?.Invoke(v);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float cy = Height / 2f, th = Math.Max(3f, Height / 7f), kx = KnobX, kr = Height / 4.2f;
        float x0 = Pad, x1 = Width - Pad;
        g.FillRectangle(softBrush, x0, cy - th / 2, x1 - x0, th);
        g.FillRectangle(accentBrush, x0, cy - th / 2, kx - x0, th);
        g.FillEllipse(whiteBrush, kx - kr, cy - kr, kr * 2, kr * 2);
        using (var p = new Pen(UI.Accent, Math.Max(1.5f, th * 0.6f))) g.DrawEllipse(p, kx - kr, cy - kr, kr * 2, kr * 2);
    }

    // 마우스 캡처를 잃으면(다른 창이 가져가는 등) 끌기도 끝낸다
    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        if (!Capture) drag = false;
        base.OnMouseCaptureChanged(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { softBrush.Dispose(); accentBrush.Dispose(); whiteBrush.Dispose(); }
        base.Dispose(disposing);
    }
}
