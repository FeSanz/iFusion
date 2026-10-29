using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace iFusion
{
    /// <summary>
    /// Pill-shaped button with a small status dot, like the scale tabs of the scale simulator.
    /// It never takes the keyboard focus, so the scanner input keeps it. Visual only.
    /// </summary>
    internal sealed class ChipButton : Control
    {
        private bool hover;
        private bool pressed;
        private bool showDot = true;
        private string iconKind = string.Empty;

        public ChipButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.StandardClick, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            Cursor = Cursors.Hand;
            AutoSize = true;
            Height = 34;
        }

        /// <summary>Name of a vector icon (see Icons): when set it replaces the status dot, e.g. "refresh".</summary>
        [DefaultValue("")]
        public string IconKind
        {
            get { return iconKind; }
            set { iconKind = value ?? string.Empty; if (AutoSize) Size = GetPreferredSize(Size.Empty); Invalidate(); }
        }

        [DefaultValue(true)]
        public bool ShowDot
        {
            get { return showDot; }
            set { showDot = value; if (AutoSize) Size = GetPreferredSize(Size.Empty); Invalidate(); }
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            if (AutoSize) Size = GetPreferredSize(Size.Empty);
            Invalidate();
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            using (Font f = ChipFont())
            {
                Size text = TextRenderer.MeasureText(Text, f, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                return new Size(text.Width + (iconKind.Length > 0 ? 52 : showDot ? 44 : 28), 34);
            }
        }

        private static Font ChipFont()
        {
            return new Font(Ui.FontFamilySemibold, 9.5f, FontStyle.Regular, GraphicsUnit.Point);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(UiDraw.ParentBack(this));

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fill = pressed ? Ui.AccentSoft : hover ? Ui.AccentTint : Ui.Field;
            Color line = hover || pressed ? Ui.Accent : Ui.Border;

            using (GraphicsPath path = UiDraw.RoundedRect(r, 8))
            using (SolidBrush brush = new SolidBrush(fill))
            using (Pen pen = new Pen(line))
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
            }

            int textLeft = 14;
            if (iconKind.Length > 0)
            {
                int s = 18;
                using (Bitmap icon = Icons.Render(iconKind, 36, hover || pressed ? Ui.AccentHover : Ui.Accent))
                {
                    g.DrawImage(icon, new Rectangle(12, (Height - s) / 2, s, s));
                }
                textLeft = 12 + s + 8;
            }
            else if (showDot)
            {
                int d = 9;
                Rectangle dot = new Rectangle(14, (Height - d) / 2, d, d);
                using (SolidBrush halo = new SolidBrush(Color.FromArgb(46, Ui.Success)))
                using (SolidBrush core = new SolidBrush(Ui.Success))
                {
                    g.FillEllipse(halo, Rectangle.Inflate(dot, 3, 3));
                    g.FillEllipse(core, dot);
                }
                textLeft = 14 + d + 8;
            }

            using (Font f = ChipFont())
            {
                Rectangle textRect = new Rectangle(textLeft, 0, Width - textLeft - 10, Height);
                TextRenderer.DrawText(g, Text, f, textRect, Ui.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            }
        }
    }
}
