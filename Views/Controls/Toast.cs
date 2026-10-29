using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace iFusion
{
    internal enum ToastKind { Success, Info, Warning, Error }

    /// <summary>
    /// In-window alerts (Alertify style): small cards that appear at the top right of the window they belong to,
    /// stack, and disappear by themselves or on click. They never take the keyboard focus (the scanner keeps it).
    /// Replaces the pop-ups that used to appear outside the application.
    /// </summary>
    internal static class Toaster
    {
        private const int Top = 66;
        private const int Gap = 8;
        private const int Margin = 16;
        private const int MaxVisible = 4;

        private static readonly ConditionalWeakTable<Form, List<ToastView>> Stacks = new ConditionalWeakTable<Form, List<ToastView>>();

        /// <summary>Shows the alert in the window in front (secondary window if one is open). False if there is no window yet.</summary>
        public static bool Show(ToastKind kind, string title, string message)
        {
            Form target = FindTarget();
            if (target == null) return false;

            if (target.InvokeRequired)
            {
                target.BeginInvoke(new Action(() => ShowOn(target, kind, title, message)));
            }
            else
            {
                ShowOn(target, kind, title, message);
            }
            return true;
        }

        private static Form FindTarget()
        {
            for (int i = Application.OpenForms.Count - 1; i >= 0; i--)
            {
                Form f = Application.OpenForms[i];
                if (f != null && !f.IsDisposed && f.Visible && f.Enabled && f.IsHandleCreated && f.WindowState != FormWindowState.Minimized)
                {
                    return f;
                }
            }
            return null;
        }

        private static void ShowOn(Form form, ToastKind kind, string title, string message)
        {
            if (form.IsDisposed) return;

            List<ToastView> stack = Stacks.GetOrCreateValue(form);
            stack.RemoveAll(t => t.IsDisposed);

            // same alert already on screen: just keep it a little longer
            ToastView same = stack.FirstOrDefault(t => t.Kind == kind && t.Title == (title ?? string.Empty) && t.Message == (message ?? string.Empty));
            if (same != null)
            {
                same.Restart();
                return;
            }

            while (stack.Count >= MaxVisible)
            {
                stack[0].Dismiss();
                stack.RemoveAt(0);
            }

            int width = Math.Max(220, Math.Min(380, form.ClientSize.Width - Margin * 2));
            ToastView view = new ToastView(kind, title, message, width);
            view.Dismissed += (s, e) =>
            {
                stack.Remove((ToastView)s);
                Reflow(form, stack);
            };

            stack.Add(view);
            form.Controls.Add(view);
            view.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            Reflow(form, stack);
            view.BringToFront();
            view.Start();
        }

        private static void Reflow(Form form, List<ToastView> stack)
        {
            int y = Top;
            foreach (ToastView view in stack)
            {
                if (view.IsDisposed) continue;
                view.Location = new Point(form.ClientSize.Width - view.Width - Margin, y);
                view.BringToFront();
                y += view.Height + Gap;
            }
        }
    }

    internal sealed class ToastView : Control
    {
        private readonly Timer timer = new Timer();
        private readonly ToastKind kind;
        private readonly string title;
        private readonly string message;
        private readonly Font titleFont = new Font(Ui.FontFamilySemibold, 9.5f, FontStyle.Regular, GraphicsUnit.Point);
        private readonly Font bodyFont = new Font(Ui.FontFamily, 9f, FontStyle.Regular, GraphicsUnit.Point);

        public event EventHandler Dismissed;

        private const int LeftPad = 52;   // room for the stripe and the icon
        private const int RightPad = 30;  // room for the close mark
        private const TextFormatFlags Wrap = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.Left | TextFormatFlags.Top;

        public ToastKind Kind { get { return kind; } }
        public string Title { get { return title; } }
        public string Message { get { return message; } }

        public ToastView(ToastKind kind, string title, string message, int width)
        {
            this.kind = kind;
            this.title = title ?? string.Empty;
            this.message = message ?? string.Empty;

            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            Cursor = Cursors.Hand;

            int textWidth = width - LeftPad - RightPad;
            int height = 14 + 14;
            if (this.title.Length > 0)
            {
                height += TextRenderer.MeasureText(this.title, titleFont, new Size(textWidth, int.MaxValue), Wrap).Height + 3;
            }
            if (this.message.Length > 0)
            {
                height += TextRenderer.MeasureText(this.message, bodyFont, new Size(textWidth, int.MaxValue), Wrap).Height;
            }
            Size = new Size(width, Math.Max(52, Math.Min(height, 220)));

            timer.Interval = Duration();
            timer.Tick += (s, e) => Dismiss();
        }

        private int Duration()
        {
            switch (kind)
            {
                case ToastKind.Error: return 8000;
                case ToastKind.Warning: return 5500;
                case ToastKind.Info: return 4000;
                default: return 3500;
            }
        }

        public void Start() { timer.Start(); }

        public void Restart() { timer.Stop(); timer.Start(); }

        public void Dismiss()
        {
            timer.Stop();
            if (IsDisposed) return;

            EventHandler handler = Dismissed;
            if (handler != null) handler(this, EventArgs.Empty);

            if (Parent != null) Parent.Controls.Remove(this);
            Dispose();
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            Dismiss();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose();
                titleFont.Dispose();
                bodyFont.Dispose();
            }
            base.Dispose(disposing);
        }

        private Color Tone()
        {
            switch (kind)
            {
                case ToastKind.Success: return Ui.SuccessText;
                case ToastKind.Warning: return Ui.Warning;
                case ToastKind.Error: return Ui.Danger;
                default: return Ui.Accent;
            }
        }

        private string Glyph()
        {
            switch (kind)
            {
                case ToastKind.Success: return "✓";
                case ToastKind.Warning: return "!";
                case ToastKind.Error: return "×";
                default: return "i";
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(UiDraw.ParentBack(this));

            Color tone = Tone();
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);

            using (GraphicsPath path = UiDraw.RoundedRect(r, 10))
            using (SolidBrush fill = new SolidBrush(Ui.Surface))
            using (Pen line = new Pen(Ui.BorderStrong))
            {
                g.FillPath(fill, path);
                g.DrawPath(line, path);

                // colored stripe on the left, clipped to the rounded card
                GraphicsState state = g.Save();
                g.SetClip(path, CombineMode.Intersect);
                using (SolidBrush stripe = new SolidBrush(tone))
                {
                    g.FillRectangle(stripe, 0, 0, 5, Height);
                }
                g.Restore(state);
            }

            // icon
            int d = 26;
            Rectangle icon = new Rectangle(15, (Height - d) / 2, d, d);
            using (SolidBrush halo = new SolidBrush(Color.FromArgb(40, tone)))
            using (SolidBrush core = new SolidBrush(tone))
            {
                g.FillEllipse(halo, icon);
                Rectangle inner = Rectangle.Inflate(icon, -4, -4);
                g.FillEllipse(core, inner);
            }
            using (Font glyphFont = new Font(Ui.FontFamilySemibold, 10f, FontStyle.Regular, GraphicsUnit.Point))
            {
                TextRenderer.DrawText(g, Glyph(), glyphFont, icon, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            }

            // text
            int textWidth = Width - LeftPad - RightPad;
            int y = 14;
            if (title.Length > 0)
            {
                Size ts = TextRenderer.MeasureText(title, titleFont, new Size(textWidth, int.MaxValue), Wrap);
                TextRenderer.DrawText(g, title, titleFont, new Rectangle(LeftPad, y, textWidth, ts.Height), Ui.Text, Wrap);
                y += ts.Height + 3;
            }
            if (message.Length > 0)
            {
                Color body = title.Length > 0 ? Ui.TextSecondary : Ui.Text;
                Rectangle area = new Rectangle(LeftPad, y, textWidth, Height - y - 8);
                TextRenderer.DrawText(g, message, bodyFont, area, body, Wrap);
            }

            // close mark
            using (Font closeFont = new Font(Ui.FontFamily, 11f, FontStyle.Regular, GraphicsUnit.Point))
            {
                TextRenderer.DrawText(g, "×", closeFont, new Rectangle(Width - 26, 4, 20, 20), Ui.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            }
        }
    }
}
