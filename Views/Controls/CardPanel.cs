using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace iFusion
{
    internal enum CardStyle
    {
        /// <summary>Rounded surface with a soft border (the simulator ".card").</summary>
        Card,
        /// <summary>Flat band with a border line at the bottom (top bar).</summary>
        TopBar,
        /// <summary>Flat band with a border line at the top (status / console strip).</summary>
        BottomBar
    }

    /// <summary>
    /// Surface used to group controls, like the ".card" / "#topbar" of the scale simulator.
    /// Paints itself from the active palette, so it follows the light/dark switch. Visual only.
    /// </summary>
    internal sealed class CardPanel : Panel
    {
        private string title = string.Empty;
        private CardStyle cardStyle = CardStyle.Card;
        private bool active;

        public CardPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        }

        /// <summary>
        /// Always the active palette's surface, so children that read "what is behind them" (buttons, badges, LCD...)
        /// follow the light/dark switch. Not serialized: a value written by the VS designer would freeze the old palette.
        /// </summary>
        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public override Color BackColor
        {
            get { return Ui.Surface; }
            set { base.BackColor = value; }
        }

        [DefaultValue("")]
        public string Title
        {
            get { return title; }
            set { title = value ?? string.Empty; Invalidate(); }
        }

        /// <summary>Highlights the card with the accent color (current step).</summary>
        [DefaultValue(false)]
        public bool Active
        {
            get { return active; }
            set { active = value; Invalidate(); }
        }

        [DefaultValue(CardStyle.Card)]
        public CardStyle Style
        {
            get { return cardStyle; }
            set { cardStyle = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color behind = cardStyle == CardStyle.Card ? UiDraw.ParentBack(this) : Ui.Surface;
            g.Clear(behind);

            if (cardStyle == CardStyle.Card)
            {
                Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
                using (GraphicsPath path = UiDraw.RoundedRect(active ? Rectangle.Inflate(r, -1, -1) : r, 10))
                using (SolidBrush fill = new SolidBrush(Ui.Surface))
                using (Pen line = new Pen(active ? Ui.Accent : Ui.Border, active ? 2f : 1f))
                {
                    g.FillPath(fill, path);
                    g.DrawPath(line, path);
                }

                if (title.Length > 0)
                {
                    using (Font f = new Font(Ui.FontFamilySemibold, 10f, FontStyle.Regular, GraphicsUnit.Point))
                    {
                        TextRenderer.DrawText(g, title, f, new Point(14, 11), Ui.Text, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                    }
                }
            }
            else
            {
                using (Pen line = new Pen(Ui.Border))
                {
                    int y = cardStyle == CardStyle.TopBar ? Height - 1 : 0;
                    g.DrawLine(line, 0, y, Width, y);
                }
            }
        }
    }
}
