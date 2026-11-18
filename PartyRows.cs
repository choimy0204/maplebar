using System.Drawing.Drawing2D;
using System.Drawing.Text;

using static MapleBar.Native;

namespace MapleBar;

/// <summary>파티원마다 화면 맨 위에 쿨타임 줄(한 줄짜리 작은 창)을 쌓아 보여 준다.
/// 들어온 순서대로 아래로 쌓이고, 나가면 빈자리 없이 다시 쌓는다.</summary>
static class PartyRows
{
    static readonly List<RowWin> rows = new();
    static float sc = 1f;

    public static void Init(float scale) => sc = scale;

    /// <summary>줄을 맞추고 다시 그린다 (0.25초쯤마다 부른다)</summary>
    public static void Update(IReadOnlyList<PartyRowInfo> infos)
    {
        // 나간 사람의 줄은 닫는다
        for (int i = rows.Count - 1; i >= 0; i--)
        {
            string id = rows[i].Id;
            if (infos.Any(x => x.Id == id)) continue;
            rows[i].Dispose();
            rows.RemoveAt(i);
        }

        var area = Game.Area() ?? Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        int y = area.Y + UI.Scale(4, sc);
        int gap = UI.Scale(3, sc);

        // 들어온 순서대로 위에서부터 쌓는다
        var ordered = new List<RowWin>();
        foreach (var info in infos)
        {
            var row = rows.FirstOrDefault(r => r.Id == info.Id);
            if (row == null) row = new RowWin(info.Id, info.Me, sc);
            ordered.Add(row);
            row.Present(info, area.X + area.Width / 2, y);
            y += row.Height + gap;
        }
        rows.Clear();
        rows.AddRange(ordered);
    }

    /// <summary>모든 줄 창을 닫는다</summary>
    public static void CloseAll()
    {
        foreach (var r in rows) r.Dispose();
        rows.Clear();
    }

    /// <summary>한 사람의 줄 창. 마우스를 막지 않는 투명 창이고, 내 줄만 버튼을 누를 수 있다</summary>
    sealed class RowWin : Form
    {
        public readonly string Id;
        readonly bool mine;
        readonly float sc;
        readonly Font font, small;
        Rectangle resetRect, undoRect;
        int hover; // 0 없음, 1 초기화, 2 되돌리기
        PartyRowInfo? last;
        Point lastAt;

        public RowWin(string id, bool mine, float sc)
        {
            Id = id; this.mine = mine; this.sc = sc;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            font = new Font("맑은 고딕", 14 * sc, FontStyle.Bold, GraphicsUnit.Pixel);
            small = new Font("맑은 고딕", 11 * sc, FontStyle.Regular, GraphicsUnit.Pixel);
            MouseMove += (_, e) =>
            {
                int h = resetRect.Contains(e.Location) ? 1 : undoRect.Contains(e.Location) ? 2 : 0;
                if (h == hover) return;
                hover = h;
                Cursor = h == 0 ? Cursors.Default : Cursors.Hand;
                Redraw();
            };
            MouseLeave += (_, _) => { if (hover != 0) { hover = 0; Redraw(); } };
            MouseClick += (_, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                if (resetRect.Contains(e.Location)) PartyBoss.Reset();
                else if (undoRect.Contains(e.Location)) PartyBoss.Undo();
            };
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                if (!mine) cp.ExStyle |= WS_EX_TRANSPARENT; // 내 줄 말고는 마우스가 통과한다
                return cp;
            }
        }

        public void Present(PartyRowInfo info, int centerX, int top)
        {
            last = info;
            using var bmp = Draw(info);
            lastAt = new Point(centerX - bmp.Width / 2, top);
            Height = bmp.Height;
            if (!Visible) { base.Show(); TopMost = true; }
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_KEEP); // 바보다 위에
            PushLayered(Handle, bmp, lastAt);
        }

        void Redraw()
        {
            if (last == null || !IsHandleCreated) return;
            using var bmp = Draw(last);
            PushLayered(Handle, bmp, lastAt);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { font.Dispose(); small.Dispose(); }
            base.Dispose(disposing);
        }

        // ── 그리기 ──

        static readonly Color OriginColor = Color.FromArgb(240, 190, 50);
        static readonly Color BurstColor = Color.FromArgb(235, 70, 70);
        static readonly Color SemiColor = Color.FromArgb(70, 175, 240);
        static readonly Color AscentColor = Color.FromArgb(150, 95, 230);

        Bitmap Draw(PartyRowInfo info)
        {
            int padX = UI.Scale(9, sc), cellH = UI.Scale(26, sc), gap = UI.Scale(4, sc);
            var calm = UI.Dark ? Color.FromArgb(235, 58, 64, 71) : Color.FromArgb(240, 244, 245, 247);
            var calmText = UI.Dark ? Color.FromArgb(225, 226, 229) : Color.FromArgb(70, 72, 78);

            // 칸 글: 모르는 사람(아직 상태를 못 받음)은 "-"
            string job = info.Job == "" ? "?" : info.Job;
            string origin = info.Known ? PartyCalc.Text(info.OriginLeft, "오리진") : "오리진 -";
            string burst = info.Known ? PartyCalc.Text(info.BurstLeft, "극딜") : "극딜 -";
            string semi = info.Known ? PartyCalc.Text(info.SemiLeft, "준극") : "준극 -";
            string ascent = info.Known ? $"어센트 {info.Ascent}회" : "어센트 -";
            string? buff = info.Known && info.BuffLeft > 0 ? Math.Ceiling(info.BuffLeft) + "초" : null;

            // 칸 너비는 글이 바뀌어도 흔들리지 않게 가장 긴 모양으로 잡는다
            int wJob = Math.Max(UI.Scale(36, sc), Measure(job, font) + padX * 2);
            int wOrigin = Measure("오리진 10분", font) + Measure("15초", small) + padX * 2 + gap;
            int wBurst = Measure("극딜 ON!", font) + padX * 2;
            int wSemi = Measure("준극 ON!", font) + padX * 2;
            int wAscent = Measure("어센트 3회", font) + padX * 2;
            int wReset = Measure("초기화", font) + padX * 2, wUndo = Measure("되돌리기", font) + padX * 2;

            int total = wJob + wOrigin + wBurst + wSemi + wAscent + gap * 4 + UI.Scale(2, sc) * 2;
            if (mine) total += gap * 2 + wReset + wUndo;
            var bmp = new Bitmap(total, cellH + UI.Scale(2, sc) * 2);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;

            int x = UI.Scale(2, sc), y = UI.Scale(2, sc);
            void Cell(int w, string text, Color fill, Color fore, Color? border = null, string? extra = null)
            {
                var r = new RectangleF(x, y, w, cellH);
                using (var path = UI.RoundRect(r, cellH * 0.25f))
                {
                    using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                    if (border is Color bc)
                        using (var pen = new Pen(bc, Math.Max(1.5f, 1.8f * sc))) g.DrawPath(pen, path);
                }
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };
                using var fb = new SolidBrush(fore);
                if (extra == null)
                {
                    g.DrawString(text, font, fb, r, sf);
                }
                else
                {
                    // 글 뒤에 작은 글자(버프 남은 초)를 붙인다
                    int tw = Measure(text, font), ew = Measure(extra, small);
                    float sx = r.X + (r.Width - tw - gap - ew) / 2;
                    using var left = new StringFormat(sf) { Alignment = StringAlignment.Near };
                    g.DrawString(text, font, fb, new RectangleF(sx, r.Y, tw + 4, r.Height), left);
                    g.DrawString(extra, small, fb, new RectangleF(sx + tw + gap, r.Y, ew + 4, r.Height), left);
                }
                sf.Dispose();
                x += w + gap;
            }

            Cell(wJob, job, calm, calmText, mine ? UI.Accent : null);
            bool kn = info.Known;
            bool on0 = kn && info.OriginLeft <= 0, on1 = kn && info.BurstLeft <= 0, on2 = kn && info.SemiLeft <= 0;
            Cell(wOrigin, origin, on0 ? OriginColor : calm, on0 ? Color.FromArgb(50, 36, 0) : calmText, null, buff);
            Cell(wBurst, burst, on1 ? BurstColor : calm, on1 ? Color.White : calmText);
            Cell(wSemi, semi, on2 ? SemiColor : calm, on2 ? Color.FromArgb(10, 40, 60) : calmText);
            Cell(wAscent, ascent, calm, kn && info.Ascent > 0 ? (UI.Dark ? Color.FromArgb(200, 170, 255) : AscentColor) : calmText);

            if (mine)
            {
                x += gap;
                resetRect = new Rectangle(x, y, wReset, cellH);
                Cell(wReset, "초기화", hover == 1 ? UI.HoverBack : calm, calmText);
                undoRect = new Rectangle(x, y, wUndo, cellH);
                Cell(wUndo, "되돌리기", hover == 2 ? UI.HoverBack : calm, calmText);
            }
            return bmp;
        }

        int Measure(string text, Font f) => TextRenderer.MeasureText(text, f, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width;
    }
}
