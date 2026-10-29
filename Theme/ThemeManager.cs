using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace iFusion
{
    /// <summary>
    /// Applies the NotiLink-style look to a form and all of its controls, right after InitializeComponent().
    /// It only touches colors, fonts, borders and painting: never text, sizes, positions, events or data,
    /// so form behavior is exactly the one written in the code-behind.
    /// Any failure here is swallowed on purpose: theming must never be able to stop a form from opening.
    /// </summary>
    internal static class ThemeManager
    {
        #region Public API

        /// <summary>Styles controls created after the form was themed (sections built at run time).</summary>
        public static void ApplyTo(Control root)
        {
            try { Walk(root.Controls, Ui.Surface, false); }
            catch (Exception) { }
        }

        public static void Apply(Form form)
        {
            Apply(form, false);
        }

        /// <param name="withThemeToggle">Main windows get the floating light/dark switch.</param>
        public static void Apply(Form form, bool withThemeToggle)
        {
            try
            {
                ApplyCore(form, withThemeToggle);
            }
            catch (Exception)
            {
                // cosmetic only
            }
        }

        #endregion

        #region Form

        private static void ApplyCore(Form form, bool withThemeToggle)
        {
            bool wasDark = IsSlate(form.BackColor);
            bool white = Same(form.BackColor, Color.White);
            Color back = white ? Ui.Surface : Ui.Bg;

            form.BackColor = back;
            form.ForeColor = Ui.Text;

            Walk(form.Controls, back, wasDark);
            StyleContextMenus(form);
            ApplyAppIcon(form);

            if (withThemeToggle)
            {
                AddThemeToggle(form);
            }

            if (form.FormBorderStyle == FormBorderStyle.None)
            {
                form.Paint += BorderlessFormPaint;
            }

            form.HandleCreated += (s, e) => ApplyWindowChrome((Form)s);
            if (form.IsHandleCreated)
            {
                ApplyWindowChrome(form);
            }
        }

        private static void BorderlessFormPaint(object sender, PaintEventArgs e)
        {
            Control form = (Control)sender;
            using (Pen border = new Pen(Ui.BorderStrong))
            using (SolidBrush accent = new SolidBrush(Ui.Accent))
            {
                e.Graphics.FillRectangle(accent, 0, 0, form.Width, 3);
                e.Graphics.DrawRectangle(border, 0, 0, form.Width - 1, form.Height - 1);
            }
        }

        #endregion

        #region Window chrome (Windows 11 only, silently ignored elsewhere)

        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWA_BORDER_COLOR = 34;
        private const int DWMWA_CAPTION_COLOR = 35;
        private const int DWMWA_TEXT_COLOR = 36;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWCP_ROUND = 2;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        private static void ApplyWindowChrome(Form form)
        {
            try
            {
                IntPtr handle = form.Handle;
                int round = DWMWCP_ROUND;
                DwmSetWindowAttribute(handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));

                int dark = Ui.IsDark ? 1 : 0;
                DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));

                int caption = ColorRef(Ui.Bg);
                DwmSetWindowAttribute(handle, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));

                int text = ColorRef(Ui.Text);
                DwmSetWindowAttribute(handle, DWMWA_TEXT_COLOR, ref text, sizeof(int));

                int border = ColorRef(Ui.BorderStrong);
                DwmSetWindowAttribute(handle, DWMWA_BORDER_COLOR, ref border, sizeof(int));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        private static int ColorRef(Color c)
        {
            return c.R | (c.G << 8) | (c.B << 16);
        }

        #endregion

        #region Control tree

        private static void Walk(Control.ControlCollection controls, Color surface, bool dark)
        {
            foreach (Control c in controls)
            {
                Color childSurface = surface;

                if (c is Label) StyleLabel((Label)c, surface, dark);
                else if (c is GroupBox) { StyleGroupBox((GroupBox)c); childSurface = Ui.Surface; }
                else if (c is Button) StyleButton((Button)c);
                else if (c is RichTextBox) StyleRichText((RichTextBox)c, surface);
                else if (c is TextBox) StyleTextBox((TextBox)c);
                else if (c is ComboBox) StyleComboBox((ComboBox)c);
                else if (c is DataGridView) StyleGrid((DataGridView)c);
                else if (c is RadioButton || c is CheckBox) StyleToggle((ButtonBase)c, dark);
                else StyleContainer(c, surface);

                ThemeScrollbars(c);

                if (c.HasChildren && !(c is DataGridView))
                {
                    Walk(c.Controls, childSurface, dark);
                }
            }
        }

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string subAppName, string subIdList);

        /// <summary>Barras de desplazamiento nativas en oscuro (Windows 10/11); sin esto quedan blancas sobre el tema oscuro.</summary>
        private static void ThemeScrollbars(Control c)
        {
            bool scrolls = c is DataGridView || c is RichTextBox || c is ListBox || (c is ScrollableControl && ((ScrollableControl)c).AutoScroll);
            if (!scrolls) return;
            Action apply = () =>
            {
                try { SetWindowTheme(c.Handle, Ui.IsDark ? "DarkMode_Explorer" : "Explorer", null); }
                catch (Exception) { }
            };
            if (c.IsHandleCreated) apply();
            else c.HandleCreated += (s, e) => apply();
        }

        private static void StyleContainer(Control c, Color surface)
        {
            if (c is PictureBox)
            {
                PictureBox pb = (PictureBox)c;
                if (pb.BorderStyle == BorderStyle.Fixed3D) pb.BorderStyle = BorderStyle.FixedSingle;
                SetPictureIcon(pb);
            }
            else
            {
                c.Font = Ui.Typeface(c.Font);
            }

            SetBack(c, MapBack(c.BackColor, surface));
        }

        private static void StyleLabel(Label l, Color surface, bool dark)
        {
            l.Font = Ui.Typeface(l.Font);
            SetFore(l, MapFore(l.ForeColor, dark));

            if (l.BorderStyle != BorderStyle.None)
            {
                // read-only "value box": flat field with a soft rounded outline
                l.BorderStyle = BorderStyle.None;
                l.BackColor = Same(surface, Ui.Surface) ? Ui.Field : Ui.Surface;
                l.Padding = FieldPadding(l.TextAlign);
                DoubleBuffer(l);
                l.Paint += FieldPaint;
            }
            else
            {
                SetBack(l, MapBack(l.BackColor, surface));
            }
        }

        private static Padding FieldPadding(ContentAlignment align)
        {
            switch (align)
            {
                case ContentAlignment.TopLeft:
                case ContentAlignment.MiddleLeft:
                case ContentAlignment.BottomLeft:
                    return new Padding(4, 0, 0, 0);
                case ContentAlignment.TopRight:
                case ContentAlignment.MiddleRight:
                case ContentAlignment.BottomRight:
                    return new Padding(0, 0, 4, 0);
                default:
                    return Padding.Empty;
            }
        }

        private static void StyleToggle(ButtonBase b, bool dark)
        {
            b.Font = Ui.Typeface(b.Font);
            SetFore(b, MapFore(b.ForeColor, dark));
            b.BackColor = Color.Transparent;
        }

        private static void StyleTextBox(TextBox t)
        {
            t.Font = Ui.Typeface(t.Font);

            Color fore = MapFore(t.ForeColor, false);
            t.ForeColor = Same(fore, Ui.Accent) ? Ui.Text : fore;

            if (t.BorderStyle == BorderStyle.Fixed3D) t.BorderStyle = BorderStyle.FixedSingle;

            Color back = t.BackColor;
            if (Same(back, Color.White) || Same(back, SystemColors.Window)) t.BackColor = Ui.Input;
            else if (Same(back, SystemColors.Control)) t.BackColor = Ui.Field;
        }

        private static void StyleRichText(RichTextBox r, Color surface)
        {
            r.Font = Ui.Typeface(r.Font);
            r.ForeColor = Ui.Text;
            if (Same(r.BackColor, SystemColors.Control) || Same(r.BackColor, SystemColors.Window) || Same(r.BackColor, Color.White))
            {
                r.BackColor = surface;
            }
        }

        private static void StyleComboBox(ComboBox c)
        {
            c.Font = Ui.Typeface(c.Font);
            c.FlatStyle = FlatStyle.Flat;
            c.BackColor = Ui.Input;
            c.ForeColor = Ui.Text;
        }

        #endregion

        #region Group box (card)

        private static void StyleGroupBox(GroupBox g)
        {
            g.Font = Ui.Typeface(g.Font);
            g.BackColor = Ui.Surface;
            g.ForeColor = Ui.Text;
            DoubleBuffer(g);
            g.Paint += GroupBoxPaint;
        }

        private static void GroupBoxPaint(object sender, PaintEventArgs e)
        {
            GroupBox g = (GroupBox)sender;
            Graphics gfx = e.Graphics;
            gfx.SmoothingMode = SmoothingMode.AntiAlias;

            Color parent = ParentBack(g);
            int titleHeight = string.IsNullOrEmpty(g.Text) ? 0 : g.Font.Height;
            int top = titleHeight / 2;

            gfx.Clear(parent);

            Rectangle card = new Rectangle(0, top, g.Width - 1, g.Height - 1 - top);
            using (GraphicsPath path = RoundedRect(card, Ui.Radius + 2))
            using (SolidBrush fill = new SolidBrush(Ui.Surface))
            using (Pen line = new Pen(Ui.Border))
            {
                gfx.FillPath(fill, path);
                gfx.DrawPath(line, path);
            }

            if (titleHeight > 0)
            {
                Size size = TextRenderer.MeasureText(gfx, g.Text, g.Font, new Size(int.MaxValue, titleHeight), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                Rectangle chip = new Rectangle(10, 0, size.Width + 8, titleHeight);
                using (SolidBrush chipFill = new SolidBrush(Ui.Surface))
                {
                    gfx.FillRectangle(chipFill, chip);
                }
                TextRenderer.DrawText(gfx, g.Text, g.Font, new Point(14, 0), g.ForeColor, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            }
        }

        #endregion

        #region Value fields (labels that used to be 3D bordered)

        private static void FieldPaint(object sender, PaintEventArgs e)
        {
            Control c = (Control)sender;
            if (c.Width < 4 || c.Height < 4) return;

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (GraphicsPath path = RoundedRect(new Rectangle(0, 0, c.Width - 1, c.Height - 1), Ui.Radius))
            {
                GraphicsState state = g.Save();
                g.SetClip(path, CombineMode.Exclude);
                using (SolidBrush corner = new SolidBrush(ParentBack(c)))
                {
                    g.FillRectangle(corner, c.ClientRectangle);
                }
                g.Restore(state);

                using (Pen line = new Pen(Ui.Border))
                {
                    g.DrawPath(line, path);
                }
            }
        }

        #endregion

        #region Buttons

        private sealed class ButtonState
        {
            public bool Hover;
            public bool Pressed;
        }

        private static readonly ConditionalWeakTable<Button, ButtonState> ButtonStates = new ConditionalWeakTable<Button, ButtonState>();

        private static void StyleButton(Button b)
        {
            b.Font = Ui.Typeface(b.Font);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.Cursor = Cursors.Hand;

            bool iconOnly = b.BackgroundImage != null || b.Image != null || string.IsNullOrEmpty(b.Text);
            if (iconOnly)
            {
                b.FlatAppearance.MouseOverBackColor = Ui.AccentTint;
                b.FlatAppearance.MouseDownBackColor = Ui.AccentSoft;
                SetButtonIcon(b);
                return;
            }

            b.UseVisualStyleBackColor = false;
            b.BackColor = MapButtonBack(b.BackColor);
            if (Same(b.ForeColor, SystemColors.Control) || Same(b.ForeColor, SystemColors.ControlText)) b.ForeColor = Color.White;

            ButtonState state = ButtonStates.GetOrCreateValue(b);
            b.MouseEnter += (s, e) => { state.Hover = true; b.Invalidate(); };
            b.MouseLeave += (s, e) => { state.Hover = false; state.Pressed = false; b.Invalidate(); };
            b.MouseDown += (s, e) => { state.Pressed = true; b.Invalidate(); };
            b.MouseUp += (s, e) => { state.Pressed = false; b.Invalidate(); };
            b.Paint += ButtonPaint;
        }

        private static void ButtonPaint(object sender, PaintEventArgs e)
        {
            Button b = (Button)sender;
            if (b.Width < 4 || b.Height < 4) return;

            ButtonState state;
            ButtonStates.TryGetValue(b, out state);
            state = state ?? new ButtonState();

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color parent = ParentBack(b);
            Color fill = b.BackColor;
            if (!b.Enabled) fill = Blend(fill, parent, 0.35f);
            else if (state.Pressed) fill = Blend(fill, Color.Black, 0.18f);
            else if (state.Hover) fill = Blend(fill, Color.Black, 0.08f);

            g.Clear(parent);
            using (GraphicsPath path = RoundedRect(new Rectangle(0, 0, b.Width - 1, b.Height - 1), b.Height / 2))
            using (SolidBrush brush = new SolidBrush(fill))
            {
                g.FillPath(brush, path);
            }

            TextRenderer.DrawText(g, b.Text, b.Font, b.ClientRectangle, b.ForeColor, TextFlags(b.TextAlign));
        }

        private static TextFormatFlags TextFlags(ContentAlignment align)
        {
            TextFormatFlags flags = TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            switch (align)
            {
                case ContentAlignment.TopLeft: return flags | TextFormatFlags.Left | TextFormatFlags.Top;
                case ContentAlignment.TopCenter: return flags | TextFormatFlags.HorizontalCenter | TextFormatFlags.Top;
                case ContentAlignment.TopRight: return flags | TextFormatFlags.Right | TextFormatFlags.Top;
                case ContentAlignment.MiddleLeft: return flags | TextFormatFlags.Left | TextFormatFlags.VerticalCenter;
                case ContentAlignment.MiddleRight: return flags | TextFormatFlags.Right | TextFormatFlags.VerticalCenter;
                case ContentAlignment.BottomLeft: return flags | TextFormatFlags.Left | TextFormatFlags.Bottom;
                case ContentAlignment.BottomCenter: return flags | TextFormatFlags.HorizontalCenter | TextFormatFlags.Bottom;
                case ContentAlignment.BottomRight: return flags | TextFormatFlags.Right | TextFormatFlags.Bottom;
                default: return flags | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter;
            }
        }

        #endregion

        #region Data grids

        private static void StyleGrid(DataGridView grid)
        {
            DoubleBuffer(grid);

            grid.BackgroundColor = Ui.Surface;
            grid.BorderStyle = BorderStyle.None;
            grid.GridColor = Ui.Border;
            grid.EnableHeadersVisualStyles = false;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            grid.RowHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;

            DataGridViewCellStyle head = grid.ColumnHeadersDefaultCellStyle;
            head.BackColor = Ui.HeaderBack;
            head.ForeColor = Ui.TextSecondary;
            head.SelectionBackColor = Ui.HeaderBack;
            head.SelectionForeColor = Ui.TextSecondary;
            head.Font = Ui.Typeface(new Font(head.Font ?? grid.Font, FontStyle.Bold));

            DataGridViewCellStyle cells = grid.DefaultCellStyle;
            cells.BackColor = Ui.Surface;
            cells.ForeColor = Ui.Text;
            cells.SelectionBackColor = Ui.AccentTint;
            cells.SelectionForeColor = Ui.Text;
            cells.Font = Ui.Typeface(cells.Font ?? grid.Font);

            DataGridViewCellStyle rowHeads = grid.RowHeadersDefaultCellStyle;
            rowHeads.BackColor = Ui.HeaderBack;
            rowHeads.ForeColor = Ui.TextSecondary;
            rowHeads.SelectionBackColor = Ui.HeaderBack;
            rowHeads.SelectionForeColor = Ui.TextSecondary;

            grid.AlternatingRowsDefaultCellStyle.BackColor = Ui.Surface;
        }

        #endregion

        #region Context menus

        private static void StyleContextMenus(Form form)
        {
            FieldInfo field = form.GetType().GetField("components", BindingFlags.Instance | BindingFlags.NonPublic);
            IContainer components = field == null ? null : field.GetValue(form) as IContainer;
            if (components == null) return;

            foreach (Component component in components.Components)
            {
                ContextMenuStrip menu = component as ContextMenuStrip;
                if (menu == null) continue;

                menu.Renderer = new ToolStripProfessionalRenderer(new ThemeColors()) { RoundedEdges = false };
                menu.Font = Ui.Typeface(menu.Font);
                menu.BackColor = Ui.Surface;
                menu.ForeColor = Ui.Text;
            }
        }

        private sealed class ThemeColors : ProfessionalColorTable
        {
            public ThemeColors() { UseSystemColors = false; }
            public override Color ToolStripDropDownBackground { get { return Ui.Surface; } }
            public override Color ImageMarginGradientBegin { get { return Ui.Surface; } }
            public override Color ImageMarginGradientMiddle { get { return Ui.Surface; } }
            public override Color ImageMarginGradientEnd { get { return Ui.Surface; } }
            public override Color MenuBorder { get { return Ui.Border; } }
            public override Color MenuItemBorder { get { return Ui.AccentSoft; } }
            public override Color MenuItemSelected { get { return Ui.AccentTint; } }
            public override Color MenuItemSelectedGradientBegin { get { return Ui.AccentTint; } }
            public override Color MenuItemSelectedGradientEnd { get { return Ui.AccentTint; } }
            public override Color MenuItemPressedGradientBegin { get { return Ui.AccentTint; } }
            public override Color MenuItemPressedGradientEnd { get { return Ui.AccentTint; } }
            public override Color SeparatorDark { get { return Ui.Border; } }
            public override Color SeparatorLight { get { return Ui.Surface; } }
        }

        #endregion

        #region Color mapping (legacy design colors -> tokens)

        private static bool Same(Color a, Color b)
        {
            return a.ToArgb() == b.ToArgb();
        }

        private static bool IsSlate(Color c)
        {
            return Same(c, Color.FromArgb(49, 66, 82));
        }

        private static bool IsBrandOrange(Color c)
        {
            return Same(c, Color.DarkOrange) || Same(c, Color.FromArgb(255, 128, 0));
        }

        private static Color MapFore(Color c, bool dark)
        {
            if (Same(c, SystemColors.ControlDarkDark) || Same(c, Color.DimGray)) return Ui.TextSecondary;
            if (Same(c, Color.Black) || Same(c, SystemColors.ControlText) || Same(c, SystemColors.WindowText)) return Ui.Text;
            if (dark && (Same(c, Color.White) || Same(c, SystemColors.Control))) return Ui.Text;
            if (IsBrandOrange(c) || Same(c, Color.MediumBlue)) return Ui.Accent;
            if (Same(c, Color.ForestGreen) || Same(c, Color.Green) || Same(c, Color.DarkOliveGreen)) return Ui.SuccessText;
            if (Same(c, Color.Firebrick) || Same(c, Color.Red)) return Ui.Danger;
            if (Same(c, Color.SaddleBrown) || Same(c, Color.DarkSlateGray)) return Ui.Text;
            if (Same(c, Color.DarkGray) || Same(c, Color.Gray)) return Ui.Muted;
            if (Same(c, Color.Yellow)) return Ui.Warning;
            return c;
        }

        private static Color MapBack(Color c, Color surface)
        {
            if (c.A == 0) return c; // Transparent
            if (Same(c, SystemColors.Control) || Same(c, Color.WhiteSmoke)) return surface;
            if (IsSlate(c)) return Ui.Bg;
            if (IsBrandOrange(c)) return Ui.Accent;
            return c;
        }

        private static Color MapButtonBack(Color c)
        {
            if (IsBrandOrange(c)) return Ui.Accent;
            if (Same(c, Color.LimeGreen) || Same(c, Color.Green)) return Ui.Success;
            if (Same(c, Color.IndianRed) || Same(c, Color.Red)) return Ui.Danger;
            if (Same(c, Color.Gray)) return Ui.Disabled;
            return c;
        }

        // Assign only when the value really changes, so ambient (inherited) colors stay inherited.
        private static void SetBack(Control c, Color value)
        {
            if (!Same(c.BackColor, value)) c.BackColor = value;
        }

        private static void SetFore(Control c, Color value)
        {
            if (!Same(c.ForeColor, value)) c.ForeColor = value;
        }

        private static Color ParentBack(Control c)
        {
            for (Control p = c.Parent; p != null; p = p.Parent)
            {
                Color k = p.BackColor;
                if (k.A == 255) return k;
            }
            return Ui.Bg;
        }

        private static Color Blend(Color from, Color to, float amount)
        {
            return Color.FromArgb(
                (int)(from.R + (to.R - from.R) * amount),
                (int)(from.G + (to.G - from.G) * amount),
                (int)(from.B + (to.B - from.B) * amount));
        }

        #endregion

        #region App icon and vector icons

        private static Icon appIcon;

        /// <summary>Icono de la app desde el recurso incrustado (conserva todos los tamaños del .ico: barra de título, barra de tareas, Alt+Tab).</summary>
        public static Icon LoadAppIcon()
        {
            if (appIcon == null)
            {
                try
                {
                    using (System.IO.Stream s = typeof(ThemeManager).Assembly.GetManifestResourceStream("iFusion.app.ico"))
                        if (s != null) appIcon = new Icon(s);
                }
                catch (Exception) { }
                if (appIcon == null)
                {
                    try { appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
                    catch (Exception) { }
                }
            }
            return appIcon;
        }

        private static void ApplyAppIcon(Form form)
        {
            Icon icon = LoadAppIcon();
            if (icon != null) form.Icon = icon;
        }

        // Icon-only buttons are identified by their designer name (same names in the three apps).
        private static readonly Dictionary<string, string> ButtonIconKinds = new Dictionary<string, string>
        {
            { "btnSettings", "lock" },
            { "btnReprint", "printer" },
            { "btnCloseFrm", "close" },
            { "btnCloseWeighing", "close" },
            { "btnCloseReprint", "close" },
            { "btnEndProcess", "logout" },
            { "btnSwapMode", "swap" },
            { "btnReloadCore", "refresh" },
            { "btnReloadTare", "refresh" },
            { "btnReset", "refresh" },
            { "btnTareBag", "bag" },
            { "btnWeighing", "scale" }
        };

        private static readonly ConditionalWeakTable<Control, string> IconKinds = new ConditionalWeakTable<Control, string>();

        private static void SetButtonIcon(Button b)
        {
            string kind;
            if (!ButtonIconKinds.TryGetValue(b.Name, out kind)) return;

            IconKinds.Remove(b);
            IconKinds.Add(b, kind);
            b.BackgroundImageLayout = ImageLayout.Zoom;
            RefreshIcon(b);
        }

        private static void SetPictureIcon(PictureBox pb)
        {
            string kind = pb.Name == "pictureBox2" ? "user" : pb.Name == "pictureBox3" ? "lock" : null;
            if (kind == null || pb.Width > 32 || pb.Image == null) return;

            IconKinds.Remove(pb);
            IconKinds.Add(pb, kind);
            RefreshIcon(pb);
        }

        private static void RefreshIcon(Control c)
        {
            string kind;
            if (!IconKinds.TryGetValue(c, out kind)) return;

            int size = Math.Max(c.Width, c.Height) * 2;
            bool neutral = c is Button && kind != "scale" && kind != "bag";
            Bitmap bmp = Icons.Render(kind, size, neutral ? Ui.TextSecondary : Ui.Text);
            if (c is Button) ((Button)c).BackgroundImage = bmp;
            else if (c is PictureBox) ((PictureBox)c).Image = bmp;
        }

        #endregion

        #region Theme toggle (floating switch, like NotiLink's theme button)

        private const string ToggleName = "wowThemeToggle";
        private static readonly ToolTip ToggleTip = new ToolTip();

        private sealed class ThemeToggle : Control
        {
            private bool hover;

            public ThemeToggle()
            {
                Name = ToggleName;
                Size = new Size(22, 22);
                Cursor = Cursors.Hand;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                TabStop = false;
            }

            protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

            protected override void OnClick(EventArgs e)
            {
                base.OnClick(e);
                SetDark(!Ui.IsDark);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(ParentBack(this));

                Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
                using (SolidBrush fill = new SolidBrush(hover ? Ui.AccentTint : Ui.Surface))
                using (Pen line = new Pen(hover ? Ui.AccentSoft : Ui.Border))
                {
                    g.FillEllipse(fill, r);
                    g.DrawEllipse(line, r);
                }

                using (Bitmap icon = Icons.Render(Ui.IsDark ? "sun" : "moon", 32, Ui.IsDark ? Ui.Warning : Ui.Accent))
                {
                    g.DrawImage(icon, new Rectangle(4, 4, Width - 8, Height - 8));
                }
            }
        }

        private static void AddThemeToggle(Form form)
        {
            ThemeToggle toggle = new ThemeToggle();

            // Make room for the switch at the end of the console strip (the bottom RichTextBox), if there is one.
            RichTextBox console = null;
            foreach (Control c in form.Controls)
            {
                RichTextBox r = c as RichTextBox;
                if (r != null && (console == null || r.Bottom > console.Bottom)) console = r;
            }

            if (console != null)
            {
                console.Width -= toggle.Width + 12;
                toggle.Location = new Point(console.Right + 6, console.Top + (console.Height - toggle.Height) / 2);
            }
            else
            {
                toggle.Location = new Point(form.ClientSize.Width - toggle.Width - 8, form.ClientSize.Height - toggle.Height - 8);
            }

            toggle.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            form.Controls.Add(toggle);
            toggle.BringToFront();
            ToggleTip.SetToolTip(toggle, "Cambiar tema claro / oscuro");
        }

        #endregion

        #region Live theme switch

        public static void SetDark(bool dark)
        {
            Palette oldP = Ui.Current;
            Palette newP = dark ? Ui.Dark : Ui.Light;
            if (ReferenceEquals(oldP, newP)) return;

            Ui.Current = newP;
            Ui.Save(dark);

            List<Form> forms = new List<Form>();
            foreach (Form f in Application.OpenForms) forms.Add(f);

            foreach (Form f in forms)
            {
                Form target = f;
                if (target.InvokeRequired) target.BeginInvoke(new Action(() => Retheme(target, oldP, newP)));
                else Retheme(target, oldP, newP);
            }
        }

        private static void Retheme(Form form, Palette oldP, Palette newP)
        {
            try
            {
                form.SuspendLayout();
                RemapBack(form, oldP, newP);
                RemapFore(form, oldP, newP);
                RethemeTree(form.Controls, oldP, newP);
                StyleContextMenus(form);
                ApplyWindowChrome(form);
                form.ResumeLayout(true);
                form.Invalidate(true);
                ConsoleLog.Rerender(); // los colores de cada línea de la consola dependen del tema
            }
            catch (Exception)
            {
                // cosmetic only
            }
        }

        private static void RethemeTree(Control.ControlCollection controls, Palette oldP, Palette newP)
        {
            foreach (Control c in controls)
            {
                if (c is DataGridView) RethemeGrid((DataGridView)c, oldP, newP);
                else if (c is RichTextBox) RethemeRichText((RichTextBox)c, oldP, newP);
                else if (c is Button) RethemeButton((Button)c, oldP, newP);
                else if (c is ComboBox)
                {
                    c.BackColor = Ui.Input;
                    c.ForeColor = Ui.Text;
                }
                else
                {
                    RemapBack(c, oldP, newP);
                    RemapFore(c, oldP, newP);
                    if (c is PictureBox) RefreshIcon(c);
                }

                ThemeScrollbars(c);
                c.Invalidate();

                if (c.HasChildren && !(c is DataGridView))
                {
                    RethemeTree(c.Controls, oldP, newP);
                }
            }
        }

        private static void RethemeButton(Button b, Palette oldP, Palette newP)
        {
            RemapBack(b, oldP, newP);
            b.FlatAppearance.MouseOverBackColor = Ui.AccentTint;
            b.FlatAppearance.MouseDownBackColor = Ui.AccentSoft;
            RefreshIcon(b);
        }

        private static void RethemeGrid(DataGridView grid, Palette oldP, Palette newP)
        {
            StyleGrid(grid);
            foreach (DataGridViewRow row in grid.Rows)
            {
                DataGridViewCellStyle style = row.DefaultCellStyle;
                if (!style.BackColor.IsEmpty) style.BackColor = oldP.Translate(style.BackColor, newP);
                if (!style.ForeColor.IsEmpty) style.ForeColor = oldP.Translate(style.ForeColor, newP);
            }
        }

        private static void RethemeRichText(RichTextBox r, Palette oldP, Palette newP)
        {
            RemapBack(r, oldP, newP);
            RemapFore(r, oldP, newP);

            // earlier console lines keep the color they were written with: translate each line
            int selStart = r.SelectionStart, selLength = r.SelectionLength;
            string[] lines = r.Lines;
            for (int i = 0; i < lines.Length; i++)
            {
                int start = r.GetFirstCharIndexFromLine(i);
                if (start < 0 || lines[i].Length == 0) continue;

                r.Select(start, lines[i].Length);
                Color current = r.SelectionColor;
                if (current.IsEmpty) continue;

                Color mapped = oldP.Translate(current, newP);
                if (mapped.ToArgb() != current.ToArgb()) r.SelectionColor = mapped;
            }
            r.Select(selStart, selLength);
        }

        private static void RemapBack(Control c, Palette oldP, Palette newP)
        {
            Color mapped = oldP.Translate(c.BackColor, newP);
            if (mapped.ToArgb() != c.BackColor.ToArgb()) c.BackColor = mapped;
        }

        private static void RemapFore(Control c, Palette oldP, Palette newP)
        {
            Color mapped = oldP.Translate(c.ForeColor, newP);
            if (mapped.ToArgb() != c.ForeColor.ToArgb()) c.ForeColor = mapped;
        }

        #endregion

        #region Helpers

        private static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0)
            {
                path.AddRectangle(r);
                return path;
            }

            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static void DoubleBuffer(Control c)
        {
            try
            {
                typeof(Control)
                    .GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(c, true, null);
            }
            catch (Exception)
            {
                // cosmetic only
            }
        }

        #endregion
    }
}
