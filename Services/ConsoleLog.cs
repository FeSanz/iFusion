using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

namespace iFusion
{
    /// <summary>Tipo de mensaje de la consola: Tx = lo que la app le pide a Oracle, Rx = lo que Oracle contesta.</summary>
    internal enum LogKind { Info, Rx, Tx, Ok, Warn, Error }

    /// <summary>
    /// Consola de la aplicación (mismo estilo que la de WOW Scales 3): hora, tipo en palabras y mensaje, con el dato técnico atenuado.
    /// Puede mostrarse en varias ventanas a la vez (principal y Receipt). Las entradas se conservan para repintar todo con los colores
    /// correctos al cambiar el tema claro/oscuro.
    /// </summary>
    internal static class ConsoleLog
    {
        private sealed class Entry
        {
            public DateTime Time;
            public LogKind Kind;
            public string Text;
            public string Detail;
        }

        private static readonly List<RichTextBox> Outputs = new List<RichTextBox>();
        private static readonly Dictionary<RichTextBox, DateTime> PaintedDay = new Dictionary<RichTextBox, DateTime>();
        private static readonly List<Entry> Entries = new List<Entry>();
        private static readonly CultureInfo Spanish = new CultureInfo("es-MX");

        private const int MaxEntries = 1500;
        private const int TrimBlock = 300;
        private const int KindWidth = 8;
        private const int MaxDetail = 1500;

        public static bool AutoScroll = true;
        public static string LastMessage = string.Empty;
        public static LogKind LastKind = LogKind.Info;

        /// <summary>Se dispara (en el hilo de la interfaz) cada vez que llega una entrada.</summary>
        public static event Action Changed;

        #region Public API

        public static void Attach(RichTextBox box)
        {
            lock (Outputs) { if (!Outputs.Contains(box)) Outputs.Add(box); }
            box.Disposed += (s, e) => Detach(box);
            Render(box);
        }

        public static void Detach(RichTextBox box)
        {
            lock (Outputs) { Outputs.Remove(box); PaintedDay.Remove(box); }
        }

        /// <param name="detail">Dato técnico (consulta o respuesta tal cual); se muestra atenuado después del mensaje.</param>
        public static void Log(LogKind kind, string text, string detail = null)
        {
            Entry entry = new Entry { Time = DateTime.Now, Kind = kind, Text = text ?? string.Empty, Detail = Trim(detail) };
            List<RichTextBox> boxes;
            lock (Outputs) boxes = new List<RichTextBox>(Outputs);

            Action add = () => AddEntry(entry);
            RichTextBox host = boxes.Find(b => !b.IsDisposed && b.IsHandleCreated);
            if (host != null && host.InvokeRequired) host.BeginInvoke(add);
            else add();
        }

        public static void Clear()
        {
            lock (Entries) Entries.Clear();
            LastMessage = string.Empty;
            foreach (RichTextBox box in Snapshot()) { PaintedDay.Remove(box); box.Clear(); }
            Action changed = Changed; if (changed != null) changed();
        }

        /// <summary>Repinta toda la consola (tema nuevo, o después de recortar).</summary>
        public static void Rerender()
        {
            foreach (RichTextBox box in Snapshot()) Render(box);
        }

        /// <summary>Texto plano para el botón Copiar.</summary>
        public static string PlainText()
        {
            StringBuilder sb = new StringBuilder();
            DateTime day = DateTime.MinValue;
            lock (Entries)
            {
                foreach (Entry e in Entries)
                {
                    if (e.Time.Date != day) { day = e.Time.Date; sb.AppendLine(DayTitle(day)); }
                    sb.Append(e.Time.ToString("HH:mm:ss", CultureInfo.InvariantCulture)).Append("  ")
                      .Append(KindText(e.Kind).PadRight(KindWidth)).Append("  ").Append(Visible(e.Text));
                    if (!string.IsNullOrEmpty(e.Detail)) sb.Append("  (").Append(Visible(e.Detail)).Append(")");
                    sb.AppendLine();
                }
            }
            return sb.ToString();
        }

        #endregion

        #region Painting

        private static List<RichTextBox> Snapshot()
        {
            lock (Outputs) return Outputs.FindAll(b => !b.IsDisposed);
        }

        private static string Trim(string detail)
        {
            if (detail == null) return null;
            detail = detail.Replace("\r", " ").Replace("\n", " ");
            return detail.Length > MaxDetail ? detail.Substring(0, MaxDetail) + "…" : detail;
        }

        private static void AddEntry(Entry entry)
        {
            bool trimmed = false;
            lock (Entries)
            {
                Entries.Add(entry);
                if (Entries.Count > MaxEntries) { Entries.RemoveRange(0, TrimBlock); trimmed = true; }
            }
            LastMessage = Visible(entry.Text);
            LastKind = entry.Kind;

            foreach (RichTextBox box in Snapshot())
            {
                if (trimmed) Render(box);
                else { Paint(box, entry); ScrollIfNeeded(box); }
            }
            Action changed = Changed; if (changed != null) changed();
        }

        private static void Render(RichTextBox box)
        {
            if (box.IsDisposed) return;
            box.SuspendLayout();
            box.Clear();
            PaintedDay[box] = DateTime.MinValue;
            lock (Entries) foreach (Entry entry in Entries) Paint(box, entry);
            box.ResumeLayout();
            ScrollIfNeeded(box);
        }

        private static void ScrollIfNeeded(RichTextBox box)
        {
            if (!AutoScroll) return;
            box.SelectionStart = box.TextLength;
            box.SelectionLength = 0;
            box.ScrollToCaret();
        }

        private static string KindText(LogKind kind)
        {
            switch (kind)
            {
                case LogKind.Rx: return "RECIBIDO";
                case LogKind.Tx: return "ENVIADO";
                case LogKind.Ok: return "OK";
                case LogKind.Warn: return "AVISO";
                case LogKind.Error: return "ERROR";
                default: return "INFO";
            }
        }

        public static Color KindColor(LogKind kind)
        {
            switch (kind)
            {
                case LogKind.Rx: return Ui.SuccessText;
                case LogKind.Tx: return Ui.Accent;
                case LogKind.Ok: return Ui.SuccessText;
                case LogKind.Warn: return Ui.Warning;
                case LogKind.Error: return Ui.Danger;
                default: return Ui.Muted;
            }
        }

        private static Color TextColor(LogKind kind)
        {
            switch (kind)
            {
                case LogKind.Rx:
                case LogKind.Tx: return Ui.Text;
                case LogKind.Ok: return Ui.SuccessText;
                case LogKind.Warn: return Ui.Warning;
                case LogKind.Error: return Ui.Danger;
                default: return Ui.TextSecondary;
            }
        }

        private static string DayTitle(DateTime day)
        {
            string text = day.ToString("dddd d 'de' MMMM 'de' yyyy", Spanish);
            return char.ToUpper(text[0], Spanish) + text.Substring(1);
        }

        private static string Visible(string text)
        {
            return (text ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n");
        }

        private static void Paint(RichTextBox box, Entry entry)
        {
            Font regular = box.Font;
            DateTime painted;
            if (!PaintedDay.TryGetValue(box, out painted)) painted = DateTime.MinValue;
            using (Font bold = new Font(regular, FontStyle.Bold))
            {
                if (entry.Time.Date != painted)
                {
                    if (painted != DateTime.MinValue) Append(box, "\n", Ui.Muted, regular);
                    Append(box, DayTitle(entry.Time.Date) + "\n", Ui.TextSecondary, bold);
                    PaintedDay[box] = entry.Time.Date;
                }

                Append(box, entry.Time.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "  ", Ui.Muted, regular);
                Append(box, KindText(entry.Kind).PadRight(KindWidth) + "  ", KindColor(entry.Kind), bold);
                Append(box, Visible(entry.Text), TextColor(entry.Kind), regular);
                if (!string.IsNullOrEmpty(entry.Detail))
                {
                    Append(box, "  (", Ui.Muted, regular);
                    Append(box, Visible(entry.Detail), Ui.Muted, regular);
                    Append(box, ")", Ui.Muted, regular);
                }
                Append(box, "\n", TextColor(entry.Kind), regular);
            }
        }

        private static void Append(RichTextBox box, string text, Color color, Font font)
        {
            box.SelectionStart = box.TextLength;
            box.SelectionLength = 0;
            box.SelectionColor = color;
            box.SelectionFont = font;
            box.AppendText(text);
        }

        #endregion
    }
}
