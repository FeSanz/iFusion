using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;

namespace iFusion.Views
{
    /// <summary>Utilidades para construir pantallas en código y leer valores JSON de Fusion.</summary>
    internal static class UiKit
    {
        public static string S(JToken t, string key)
        {
            JObject o = t as JObject;
            JToken v = o == null ? null : o[key];
            return v == null || v.Type == JTokenType.Null ? string.Empty : v.ToString();
        }

        public static decimal D(JToken t, string key)
        {
            decimal d;
            return decimal.TryParse(S(t, key), NumberStyles.Any, CultureInfo.InvariantCulture, out d) ? d : 0m;
        }

        public static string Num(JToken t, string key)
        {
            string s = S(t, key);
            decimal d;
            return s.Length == 0 ? string.Empty
                : decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out d) ? d.ToString("#,##0.####", CultureInfo.CurrentCulture) : s;
        }

        public static string Day(JToken t, string key)
        {
            string s = S(t, key);
            return s.Length >= 10 ? s.Substring(0, 10) : s;
        }

        public static Label Lbl(string text, bool bold = false, float size = 9f, Color? color = null)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Font = new Font(bold ? Ui.FontFamilySemibold : Ui.FontFamily, size),
                ForeColor = color ?? Ui.Text,
                BackColor = Color.Transparent
            };
        }

        public static Button Btn(string text, bool primary = false, int width = 110)
        {
            Button b = new Button { Text = text, Width = width, Height = 30, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            Style(b, primary);
            return b;
        }

        /// <summary>Botón principal (acento) o secundario (tinte del acento); ThemeManager los dibuja redondeados.</summary>
        public static void Style(Button b, bool primary)
        {
            b.BackColor = primary ? Ui.Accent : Ui.AccentTint;
            b.ForeColor = primary ? Color.White : Ui.Accent;
        }

        public static DataGridView Grid(bool inline)
        {
            DataGridView g = new DataGridView
            {
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = 28,
                RowTemplate = { Height = 26 },
                ReadOnly = true,
                BorderStyle = BorderStyle.None
            };
            if (inline) g.ScrollBars = ScrollBars.None;
            return g;
        }

        public static DataGridViewTextBoxColumn Col(DataGridView g, string header, float weight = 1f, bool right = false, int minWidth = 40)
        {
            DataGridViewTextBoxColumn c = new DataGridViewTextBoxColumn { HeaderText = header, FillWeight = weight * 100, MinimumWidth = minWidth, SortMode = DataGridViewColumnSortMode.NotSortable };
            if (right) { c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight; c.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight; }
            g.Columns.Add(c);
            return c;
        }

        /// <summary>
        /// Pinta las columnas de botón y de casilla con los colores del tema vigente en cada repintado
        /// (las nativas quedan blancas en el tema oscuro y no siguen el cambio en vivo). Mismo tamaño que las nativas: los clics siguen igual.
        /// </summary>
        public static void ThemeGridControls(DataGridView g)
        {
            g.CellPainting += (s, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
                DataGridViewColumn col = g.Columns[e.ColumnIndex];
                bool button = col is DataGridViewButtonColumn, check = col is DataGridViewCheckBoxColumn;
                if (!button && !check) return;

                e.Paint(e.CellBounds, DataGridViewPaintParts.Background | DataGridViewPaintParts.Border | DataGridViewPaintParts.SelectionBackground);
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                if (button)
                {
                    Rectangle r = Rectangle.Inflate(e.CellBounds, -4, -3);
                    using (System.Drawing.Drawing2D.GraphicsPath path = UiDraw.RoundedRect(r, 7))
                    using (SolidBrush fill = new SolidBrush(Ui.AccentTint))
                    using (Pen line = new Pen(Ui.AccentSoft))
                    {
                        e.Graphics.FillPath(fill, path);
                        e.Graphics.DrawPath(line, path);
                    }
                    TextRenderer.DrawText(e.Graphics, Convert.ToString(e.FormattedValue), e.CellStyle.Font, r, Ui.Accent,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                }
                else
                {
                    bool on = e.Value is bool && (bool)e.Value;
                    Rectangle box = new Rectangle(e.CellBounds.X + (e.CellBounds.Width - 15) / 2, e.CellBounds.Y + (e.CellBounds.Height - 15) / 2, 15, 15);
                    using (System.Drawing.Drawing2D.GraphicsPath path = UiDraw.RoundedRect(box, 3))
                    using (SolidBrush fill = new SolidBrush(on ? Ui.Accent : Ui.Input))
                    using (Pen line = new Pen(on ? Ui.Accent : Ui.BorderStrong, 1.2f))
                    {
                        e.Graphics.FillPath(fill, path);
                        e.Graphics.DrawPath(line, path);
                    }
                    if (on)
                        using (Pen tick = new Pen(Color.White, 2f) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round })
                        {
                            e.Graphics.DrawLines(tick, new[] { new Point(box.X + 3, box.Y + 8), new Point(box.X + 6, box.Y + 11), new Point(box.X + 12, box.Y + 4) });
                        }
                }
                e.Handled = true;
            };
        }

        /// <summary>
        /// Semáforo de la columna "avail": rojo si Disponible es 0, amarillo si es menor a lo requerido (Quantity del material);
        /// el check de la columna "lot" va en verde. Se resuelve al pintar, así sigue el tema claro/oscuro en vivo.
        /// Disponible debe guardar su valor numérico en Cell.Tag.
        /// </summary>
        private static readonly System.Collections.Generic.Dictionary<Font, Font> BoldFonts = new System.Collections.Generic.Dictionary<Font, Font>();

        private static Font Bold(Font f)
        {
            Font b;
            if (!BoldFonts.TryGetValue(f, out b)) BoldFonts[f] = b = new Font(f, FontStyle.Bold);
            return b;
        }

        public static void StockHighlight(DataGridView g)
        {
            g.CellFormatting += (s, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
                DataGridViewRow row = g.Rows[e.RowIndex];
                string col = g.Columns[e.ColumnIndex].Name;
                if (col == "avail" && row.Cells[e.ColumnIndex].Tag is decimal)
                {
                    decimal avail = (decimal)row.Cells[e.ColumnIndex].Tag, required = D(row.Tag as JToken, "Quantity");
                    Color back = avail <= 0 ? Ui.RowDanger : avail < required ? Ui.RowWarning : Color.Empty;
                    if (!back.IsEmpty)
                    {
                        e.CellStyle.BackColor = e.CellStyle.SelectionBackColor = back;
                        e.CellStyle.ForeColor = e.CellStyle.SelectionForeColor = Ui.Text;
                        e.CellStyle.Font = Bold(e.CellStyle.Font);
                    }
                }
                else if (col == "lot" && Convert.ToString(e.Value).StartsWith("✔"))
                {
                    e.CellStyle.ForeColor = e.CellStyle.SelectionForeColor = Ui.SuccessText;
                    e.CellStyle.Font = Bold(e.CellStyle.Font);
                }
            };
        }

        public static int GridHeight(DataGridView g)
        {
            return g.ColumnHeadersHeight + g.Rows.Count * g.RowTemplate.Height + 2;
        }

        public static void Error(string message)
        {
            ConsoleLog.Log(LogKind.Error, message);
            if (!Toaster.Show(ToastKind.Error, "Error", message)) MessageBox.Show(message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        public static void Info(string message)
        {
            ConsoleLog.Log(LogKind.Ok, message);
            if (!Toaster.Show(ToastKind.Success, "Listo", message)) MessageBox.Show(message, "iFusion", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
