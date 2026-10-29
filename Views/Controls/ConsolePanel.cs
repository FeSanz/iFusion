using System;
using System.Drawing;
using System.Windows.Forms;

namespace iFusion
{
    /// <summary>Consola reutilizable: título, Auto-scroll, Copiar, Limpiar y el área de mensajes (ver <see cref="ConsoleLog"/>).</summary>
    internal class ConsolePanel : Panel
    {
        private readonly CardPanel card = new CardPanel { Dock = DockStyle.Fill };
        private readonly Label title = new Label { Text = "Consola", AutoSize = true, BackColor = Color.Transparent };
        private readonly CheckBox chkAuto = new CheckBox { Text = "Auto-scroll", AutoSize = true, Checked = true, TabStop = false };
        private readonly Button btnCopy = new Button { Text = "Copiar", Width = 80, Height = 26, TabStop = false };
        private readonly Button btnClear = new Button { Text = "Limpiar", Width = 80, Height = 26, TabStop = false };
        private readonly RichTextBox box = new RichTextBox { ReadOnly = true, BorderStyle = BorderStyle.None, WordWrap = true, DetectUrls = false, TabStop = false };

        public ConsolePanel()
        {
            title.Font = new Font(Ui.FontFamilySemibold, 10f);
            box.Font = new Font(UiDraw.MonoFamily, 9f);
            box.BackColor = Ui.Bg;
            box.ForeColor = Ui.Text;
            iFusion.Views.UiKit.Style(btnCopy, false);
            iFusion.Views.UiKit.Style(btnClear, false);
            btnCopy.FlatStyle = btnClear.FlatStyle = FlatStyle.Flat;

            card.Controls.AddRange(new Control[] { title, chkAuto, btnCopy, btnClear, box });
            Controls.Add(card);
            card.Resize += (s, e) => LayoutParts();

            chkAuto.CheckedChanged += (s, e) =>
            {
                ConsoleLog.AutoScroll = chkAuto.Checked;
                if (chkAuto.Checked) { box.SelectionStart = box.TextLength; box.ScrollToCaret(); }
            };
            btnCopy.Click += (s, e) =>
            {
                string text = ConsoleLog.PlainText();
                if (text.Length > 0) try { Clipboard.SetText(text); } catch (Exception) { }
            };
            btnClear.Click += (s, e) => ConsoleLog.Clear();

            HandleCreated += (s, e) => ConsoleLog.Attach(box);
            // Al mostrarse (la consola principal nace oculta) se repinta todo: pintar con tamaño 0 deja el desplazamiento mal calculado.
            box.VisibleChanged += (s, e) => { if (box.Visible) { ConsoleLog.Rerender(); } };
            box.SizeChanged += (s, e) => { if (box.Visible && ConsoleLog.AutoScroll) { box.SelectionStart = box.TextLength; box.ScrollToCaret(); } };
            LayoutParts();
        }

        private void LayoutParts()
        {
            int w = card.Width, h = card.Height;
            title.Location = new Point(14, 9);
            btnClear.Location = new Point(w - btnClear.Width - 12, 6);
            btnCopy.Location = new Point(btnClear.Left - btnCopy.Width - 8, 6);
            chkAuto.Location = new Point(btnCopy.Left - chkAuto.Width - 14, 9);
            box.SetBounds(10, 38, Math.Max(10, w - 20), Math.Max(10, h - 46));
        }
    }
}
