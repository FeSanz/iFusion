using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace iFusion
{
    internal enum BadgeKind { Neutral, Ok, Warn, Err, Info }

    /// <summary>Small pill with an outline, like the ".badge" of the scale simulator. Visual only.</summary>
    internal sealed class Badge : Control
    {
        private BadgeKind kind = BadgeKind.Neutral;

        public Badge()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            AutoSize = true;
            Height = 22;
            TabStop = false;
        }

        [DefaultValue(BadgeKind.Neutral)]
        public BadgeKind Kind
        {
            get { return kind; }
            set { kind = value; Invalidate(); }
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            if (AutoSize) Size = GetPreferredSize(Size.Empty);
            Invalidate();
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            using (Font f = BadgeFont())
            {
                Size text = TextRenderer.MeasureText(Text, f, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                return new Size(text.Width + 20, Math.Max(22, text.Height + 8));
            }
        }

        private static Font BadgeFont()
        {
            return new Font(Ui.FontFamilySemibold, 8f, FontStyle.Regular, GraphicsUnit.Point);
        }

        private Color Tone()
        {
            switch (kind)
            {
                case BadgeKind.Ok: return Ui.SuccessText;
                case BadgeKind.Warn: return Ui.Warning;
                case BadgeKind.Err: return Ui.Danger;
                case BadgeKind.Info: return Ui.Accent;
                default: return Ui.TextSecondary;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(UiDraw.ParentBack(this));

            Color tone = Tone();
            Color outline = kind == BadgeKind.Neutral ? Ui.Border : Color.FromArgb(120, tone);

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = UiDraw.RoundedRect(r, Height / 2))
            using (Pen line = new Pen(outline))
            {
                g.DrawPath(line, path);
            }

            using (Font f = BadgeFont())
            {
                TextRenderer.DrawText(g, Text, f, ClientRectangle, tone,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            }
        }
    }
}
