using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using iFusion.Models;
using iFusion.Services;
using Newtonsoft.Json.Linq;

namespace iFusion.Views
{
    /// <summary>Incremento libre de stock: Miscellaneous Receipt en batch (POST /fscmRestApi/resources/latest).</summary>
    internal class frmReceipt : Form
    {
        private class Line
        {
            public JToken Mat, Wo;
            public bool Lot;
            public TextBox Qty, Sub, Uom, Ref;
            public ComboBox Loc;
            public Dictionary<string, string> LocIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public DataGridView Lots;
            public Label Stock;
        }

        private Dictionary<string, JArray> stock;

        private readonly JToken org;
        private readonly List<Line> lines = new List<Line>();
        private readonly FlowLayoutPanel flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(12) };
        private readonly Button btnSend = UiKit.Btn("Enviar a Oracle", true, 150), btnCancel = UiKit.Btn("Cerrar");
        private readonly ConsolePanel console = new ConsolePanel { Dock = DockStyle.Bottom, Height = 210, Padding = new Padding(12, 0, 12, 6) };
        private readonly Label lblStatus = UiKit.Lbl("Verificando control de lote…", false, 9f, Ui.TextSecondary);
        private IList<KeyValuePair<JToken, JToken>> source;

        public frmReceipt(JToken organization, IList<KeyValuePair<JToken, JToken>> materialsAndWo)
        {
            org = organization; source = materialsAndWo;
            Text = "Incrementar stock · Miscellaneous Receipt · " + UiKit.S(org, "OrganizationCode");
            ClientSize = new Size(860, 640);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font(Ui.FontFamily, 9f);

            Panel bar = new Panel { Dock = DockStyle.Bottom, Height = 52 };
            lblStatus.Location = new Point(16, 18);
            bar.Resize += (s, e) =>
            {
                btnCancel.Location = new Point(bar.Width - btnCancel.Width - 16, 11);
                btnSend.Location = new Point(btnCancel.Left - btnSend.Width - 8, 11);
            };
            btnSend.Enabled = false;
            btnSend.Click += async (s, e) => await SendAsync();
            btnCancel.Click += (s, e) => Close();
            bar.Controls.AddRange(new Control[] { lblStatus, btnSend, btnCancel });

            Controls.Add(flow); Controls.Add(console); Controls.Add(bar);
            ThemeManager.Apply(this);
            Shown += async (s, e) => await BuildAsync();
        }

        private async System.Threading.Tasks.Task BuildAsync()
        {
            string code = UiKit.S(org, "OrganizationCode");
            bool[] lot = new bool[source.Count];
            try
            {
                // Una sola llamada batch para saber qué ítems llevan lote
                Dictionary<string, bool> lots = await StockService.LoadLotsAsync(source.Select(m => UiKit.S(m.Key, "ItemNumber")), code);
                for (int i = 0; i < lot.Length; i++) { bool v; lot[i] = lots.TryGetValue(UiKit.S(source[i].Key, "ItemNumber"), out v) && v; }
            }
            catch (Exception ex) { UiKit.Error("No se pudo verificar el control de lote: " + ex.Message); }

            try { stock = await StockService.LoadAsync(source.Select(m => UiKit.S(m.Key, "ItemNumber")), code); }
            catch (Exception ex) { UiKit.Error("No se pudo consultar el stock: " + ex.Message); }

            flow.SuspendLayout();
            for (int i = 0; i < source.Count; i++) flow.Controls.Add(BuildLine(source[i].Key, source[i].Value, lot[i], i));
            flow.ResumeLayout();
            ThemeManager.ApplyTo(flow);
            lblStatus.Text = lines.Count + " material(es). La fecha de transacción es hoy.";
            btnSend.Enabled = true;
        }

        private Control BuildLine(JToken mat, JToken wo, bool lot, int index)
        {
            Line ln = new Line { Mat = mat, Wo = wo, Lot = lot };
            CardPanel card = new CardPanel { Width = 800, Height = lot ? 268 : 142, Margin = new Padding(0, 0, 0, 10) };

            Label head = UiKit.Lbl(UiKit.S(mat, "ItemNumber") + "  ·  " + UiKit.S(mat, "ItemDescription"), true, 10f);
            head.Location = new Point(14, 10);
            Label sub = UiKit.Lbl("OT " + UiKit.S(wo, "WorkOrderNumber") + (lot ? "   ·   CON LOTE" : "   ·   sin lote"), false, 8.5f, lot ? Ui.Warning : Ui.TextSecondary);
            sub.Location = new Point(14, 30);

            ln.Qty = new TextBox { Width = 100 };
            ln.Sub = new TextBox { Width = 150, Text = UiKit.S(mat, "SupplySubinventory") };
            ln.Loc = new ComboBox { Width = 190, DropDownStyle = ComboBoxStyle.DropDown };
            ln.Uom = new TextBox { Width = 80, Text = UiKit.S(mat, "UOMCode") };
            ln.Ref = new TextBox { Width = 194, Text = "Ingreso manual (" + Epoch() + ")" };
            AddField(card, "Cantidad", ln.Qty, 14, 56);
            AddField(card, "Subinventario", ln.Sub, 128, 56);
            AddField(card, "Localizador (si lo exige)", ln.Loc, 292, 56);
            AddField(card, "UOM (código)", ln.Uom, 496, 56);
            AddField(card, "Referencia (opcional)", ln.Ref, 590, 56);

            ln.Stock = UiKit.Lbl(string.Empty, true, 9f, Ui.Accent);
            ln.Stock.Location = new Point(14, 108);
            card.Controls.Add(ln.Stock);
            ln.Sub.TextChanged += (s, e) => { ShowStock(ln); RefreshLocators(ln); };
            ShowStock(ln);
            RefreshLocators(ln);

            if (lot)
            {
                ln.Qty.ReadOnly = true;
                Label l = UiKit.Lbl("Lotes (cantidad parcial por lote; la cantidad total es la suma). Usa la última fila para agregar.", false, 8.5f, Ui.TextSecondary);
                l.Location = new Point(14, 134);
                ln.Lots = UiKit.Grid(false);
                ln.Lots.ReadOnly = false;
                ln.Lots.AllowUserToAddRows = true;
                ln.Lots.AllowUserToDeleteRows = true;
                UiKit.Col(ln.Lots, "Lote", 2f);
                UiKit.Col(ln.Lots, "Cantidad", 1f, true);
                ln.Lots.SetBounds(14, 156, 770, 100);
                Line captured = ln;
                // Lote automático LOT + epoch (segundos); editable. Cada fila nueva toma uno distinto.
                ln.Lots.DefaultValuesNeeded += (s, e) => e.Row.Cells[0].Value = NextLotName(captured);
                ln.Lots.Rows.Add(NextLotName(ln), null);
                ln.Lots.CellEndEdit += (s, e) => SumLots(captured);
                ln.Lots.RowsRemoved += (s, e) => SumLots(captured);
                card.Controls.Add(l); card.Controls.Add(ln.Lots);
            }
            card.Controls.Add(head); card.Controls.Add(sub);
            lines.Add(ln);
            return card;
        }

        private static long Epoch() { return DateTimeOffset.UtcNow.ToUnixTimeSeconds(); }

        private static string NextLotName(Line ln)
        {
            long t = Epoch();
            while (ln.Lots.Rows.Cast<DataGridViewRow>().Any(r => Convert.ToString(r.Cells[0].Value) == "LOT" + t)) t++;
            return "LOT" + t;
        }

        /// <summary>Disponible / Existencia del artículo: total en la organización y, si hay filas, el del subinventario capturado.</summary>
        private void ShowStock(Line ln)
        {
            JArray rows;
            if (stock == null || !stock.TryGetValue(UiKit.S(ln.Mat, "ItemNumber"), out rows)) { ln.Stock.Text = "Stock: n/d"; return; }
            string sub = ln.Sub.Text.Trim();
            string text = "Disponible: " + StockService.Fmt(StockService.Total(rows, "AvailableToTransact")) +
                "   ·   Existencia: " + StockService.Fmt(StockService.Total(rows, "PrimaryOnhandQuantity")) + "   (total de la organización)";
            if (sub.Length > 0)
                text += "      " + sub + ": " + StockService.Fmt(StockService.Total(rows, "AvailableToTransact", sub)) +
                    " disp. / " + StockService.Fmt(StockService.Total(rows, "PrimaryOnhandQuantity", sub)) + " exist.";
            ln.Stock.Text = text;
        }

        /// <summary>
        /// Localizadores que ya tienen stock del artículo en el subinventario capturado. Si el subinventario exige localizador y no se envía,
        /// Oracle rechaza la transacción (INV_INT_LOCSEGCODE · FLEX-NO DYNAMIC INSERTS), por eso se propone el primero.
        /// </summary>
        private void RefreshLocators(Line ln)
        {
            JArray rows;
            ln.LocIds.Clear();
            string previous = ln.Loc.Text;
            ln.Loc.Items.Clear();
            string sub = ln.Sub.Text.Trim();
            if (stock != null && sub.Length > 0 && stock.TryGetValue(UiKit.S(ln.Mat, "ItemNumber"), out rows))
            {
                foreach (JToken r in rows)
                {
                    string name = UiKit.S(r, "Locator");
                    if (name.Length == 0 || !string.Equals(UiKit.S(r, "SubinventoryCode"), sub, StringComparison.OrdinalIgnoreCase) || ln.LocIds.ContainsKey(name)) continue;
                    ln.LocIds[name] = UiKit.S(r, "LocatorId");
                    ln.Loc.Items.Add(name);
                }
            }
            // Nunca se asigna un localizador por cuenta propia: solo se ofrecen y el usuario elige (se conserva lo que ya había capturado).
            ln.Loc.Text = previous;
            if (ln.Loc.Items.Count == 0 && sub.Length > 0) FillSubLocatorsAsync(ln, sub);
        }

        private readonly Dictionary<string, List<string>> subLocators = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// El artículo aún no tiene stock en el subinventario (por eso no hay localizadores propios): se toman los localizadores que ya existen ahí
        /// para otros artículos. Un localizador inexistente se rechaza (INV_INT_LOCSEGCODE · FLEX-NO DYNAMIC INSERTS).
        /// </summary>
        private async void FillSubLocatorsAsync(Line ln, string sub)
        {
            try
            {
                List<string> names;
                if (!subLocators.TryGetValue(sub, out names))
                {
                    JObject r = await FusionClient.GetAsync("/itemStockingInquiries?onlyData=true&limit=500&fields=Locator,LocatorId&q=" +
                        Uri.EscapeDataString("OrganizationCode='" + UiKit.S(org, "OrganizationCode").Replace("'", "''") + "' and SubinventoryCode='" + sub.Replace("'", "''") + "'").Replace("%27", "'"));
                    names = FusionClient.Items(r).Select(t => UiKit.S(t, "Locator")).Where(n => n.Length > 0).Distinct().ToList();
                    subLocators[sub] = names;
                }
                if (ln.Sub.Text.Trim() != sub || ln.Loc.Items.Count > 0) return;
                foreach (string n in names) ln.Loc.Items.Add(n);
            }
            catch (Exception ex) { ConsoleLog.Log(LogKind.Warn, "No se pudieron consultar los localizadores de " + sub + ": " + ex.Message); }
        }

        private static void AddField(Control parent, string caption, Control box, int x, int y)
        {
            Label l = UiKit.Lbl(caption, false, 8.5f, Ui.TextSecondary);
            l.Location = new Point(x, y);
            box.Location = new Point(x, y + 18);
            parent.Controls.Add(l); parent.Controls.Add(box);
        }

        private static decimal ParseQty(object v)
        {
            decimal d;
            return decimal.TryParse(Convert.ToString(v, CultureInfo.CurrentCulture), NumberStyles.Any, CultureInfo.CurrentCulture, out d) ? d : 0m;
        }

        private static void SumLots(Line ln)
        {
            decimal total = 0;
            foreach (DataGridViewRow r in ln.Lots.Rows) if (!r.IsNewRow) total += ParseQty(r.Cells[1].Value);
            ln.Qty.Text = total > 0 ? total.ToString(CultureInfo.CurrentCulture) : string.Empty;
        }

        private JArray BuildParts()
        {
            JArray parts = new JArray();
            string today = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            for (int i = 0; i < lines.Count; i++)
            {
                Line ln = lines[i];
                string name = UiKit.S(ln.Mat, "ItemNumber");
                if (lines[i].Lot) SumLots(ln);
                if (ParseQty(ln.Qty.Text) <= 0) throw new InvalidOperationException(name + ": la cantidad debe ser mayor a cero.");
                if (ln.Sub.Text.Trim().Length == 0) throw new InvalidOperationException(name + ": captura el subinventario.");
                if (ln.Uom.Text.Trim().Length == 0) throw new InvalidOperationException(name + ": captura la unidad de medida.");

                JObject payload = new JObject
                {
                    ["SourceCode"] = "SERVICE",
                    ["SourceHeaderId"] = 1,
                    ["SourceLineId"] = (i + 1).ToString(),
                    ["TransactionMode"] = "1",
                    ["OrganizationId"] = UiKit.S(org, "OrganizationId"),
                    ["SubinventoryCode"] = ln.Sub.Text.Trim(),
                    ["ItemNumber"] = name,
                    ["TransactionTypeName"] = "Miscellaneous Receipt",
                    ["TransactionTypeId"] = 42,
                    ["TransactionQuantity"] = ParseQty(ln.Qty.Text),
                    // Código de la unidad (KG, pza…). TransactionUnitOfMeasure es el NOMBRE ("Kilogramo") y con un código falla al procesar (INV_INUOM_INVALID_UOM).
                    ["TransactionUOM"] = ln.Uom.Text.Trim(),
                    ["TransactionDate"] = today,
                    ["UseCurrentCostFlag"] = "true"
                };
                if (ln.Ref.Text.Trim().Length > 0) payload["TransactionReference"] = ln.Ref.Text.Trim();
                string loc = ln.Loc.Text.Trim();
                if (loc.Length == 0 && ln.Loc.Items.Count > 0)
                    throw new InvalidOperationException(name + ": " + ln.Sub.Text.Trim() + " tiene localizadores (por ejemplo " + ln.Loc.Items[0] + "). Elige uno en \"Localizador\" antes de enviar.");
                if (loc.Length > 0)
                {
                    payload["LocatorName"] = loc; // por nombre ("RESV MTEX.1."): enviar LocatorId hizo que Oracle rechazara la línea sin detalle
                }
                if (ln.Lot)
                {
                    JArray lots = new JArray();
                    foreach (DataGridViewRow r in ln.Lots.Rows)
                    {
                        if (r.IsNewRow) continue;
                        string lotNo = Convert.ToString(r.Cells[0].Value).Trim();
                        decimal q = ParseQty(r.Cells[1].Value);
                        if (lotNo.Length == 0 || q <= 0) throw new InvalidOperationException(name + ": cada lote necesita número y cantidad mayor a cero.");
                        lots.Add(new JObject { ["LotNumber"] = lotNo, ["TransactionQuantity"] = q.ToString(CultureInfo.InvariantCulture) });
                    }
                    if (lots.Count == 0) throw new InvalidOperationException(name + ": captura al menos un lote.");
                    payload["lots"] = lots;
                }
                parts.Add(new JObject { ["id"] = (i + 1).ToString(), ["path"] = EndPoints.InventoryStagedTransactions, ["operation"] = "create", ["payload"] = payload });
            }
            return parts;
        }

        /// <summary>
        /// Oracle procesa el Miscellaneous Receipt al recibirlo y, si lo acepta, responde con la parte vacía (sin error ni TransactionInterfaceId).
        /// Por eso el resultado se confirma contra el stock: la existencia del subinventario debe subir en la cantidad enviada. Si Oracle lo rechazó,
        /// el motivo queda en la cola de staging con ProcessStatus 3 y se muestra en la consola.
        /// </summary>
        private async System.Threading.Tasks.Task<bool> VerifyAppliedAsync(Dictionary<int, decimal> before, DateTime sentUtc)
        {
            string orgId = UiKit.S(org, "OrganizationId"), code = UiKit.S(org, "OrganizationCode");
            List<int> pending = before.Keys.ToList();
            // varias líneas del mismo artículo y subinventario se esperan sumadas
            Func<int, string> key = i => UiKit.S(lines[i].Mat, "ItemNumber") + "|" + lines[i].Sub.Text.Trim().ToUpperInvariant();
            Dictionary<string, decimal> expected = new Dictionary<string, decimal>();
            foreach (int i in pending) { string k = key(i); expected[k] = (expected.ContainsKey(k) ? expected[k] : 0m) + ParseQty(lines[i].Qty.Text); }
            Dictionary<string, decimal> baseline = new Dictionary<string, decimal>();
            foreach (int i in pending) baseline[key(i)] = before[i];

            bool allOk = true;
            for (int attempt = 0; attempt < 6 && pending.Count > 0; attempt++)
            {
                await System.Threading.Tasks.Task.Delay(3000);
                Dictionary<string, JArray> now = await StockService.LoadAsync(pending.Select(i => UiKit.S(lines[i].Mat, "ItemNumber")), code);
                JObject rejected = await FusionClient.GetAsync(EndPoints.StagedRejectsPath(orgId));
                List<int> still = new List<int>();
                foreach (int i in pending)
                {
                    string item = UiKit.S(lines[i].Mat, "ItemNumber"), sub = lines[i].Sub.Text.Trim(), k = key(i);
                    decimal after = StockService.Total(now.ContainsKey(item) ? now[item] : null, "PrimaryOnhandQuantity", sub);
                    JToken bad = ((rejected["items"] as JArray) ?? new JArray()).FirstOrDefault(r =>
                        UiKit.S(r, "ItemNumber") == item && UiKit.S(r, "SubinventoryCode").Equals(sub, StringComparison.OrdinalIgnoreCase) && CreatedSince(r, sentUtc));
                    if (after - baseline[k] >= expected[k] - 0.0001m)
                    {
                        ConsoleLog.Log(LogKind.Ok, item + " · aplicado en " + sub + ": existencia " + StockService.Fmt(baseline[k]) + " → " + StockService.Fmt(after));
                        stock[item] = now[item];
                    }
                    else if (bad != null)
                    {
                        allOk = false;
                        ConsoleLog.Log(LogKind.Error, item + " · RECHAZADA por Oracle: " + UiKit.S(bad, "ErrorCode") + " · " + UiKit.S(bad, "ErrorExplanation"));
                    }
                    else still.Add(i);
                }
                pending = still;
            }
            foreach (int i in pending)
            {
                allOk = false;
                ConsoleLog.Log(LogKind.Warn, UiKit.S(lines[i].Mat, "ItemNumber") + " · no se pudo confirmar que el stock subiera; revisa la existencia en Fusion antes de reintentar");
            }
            foreach (Line ln in lines) { ShowStock(ln); RefreshLocators(ln); }
            return allOk;
        }

        private static bool CreatedSince(JToken row, DateTime sentUtc)
        {
            // Newtonsoft ya convirtió la fecha ISO a DateTime (hora local): se compara como instante, no como texto.
            JToken v = row["CreationDate"];
            if (v == null) return false;
            if (v.Type == JTokenType.Date) return v.Value<DateTime>().ToUniversalTime() >= sentUtc.AddSeconds(-30);
            DateTimeOffset created;
            return DateTimeOffset.TryParse((string)v, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out created)
                && created.UtcDateTime >= sentUtc.AddSeconds(-30);
        }

        private async System.Threading.Tasks.Task SendAsync()
        {
            JArray parts;
            try { parts = BuildParts(); }
            catch (InvalidOperationException ex) { UiKit.Error(ex.Message); return; }

            if (MessageBox.Show("Se enviarán " + parts.Count + " Miscellaneous Receipt a la instancia \"" + InstanceStore.Active.Name + "\".\n\n¿Continuar?",
                    "Confirmar envío", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            btnSend.Enabled = false;
            try
            {
                // existencia del subinventario antes de enviar: con ella se confirma después que Oracle sí aplicó la transacción
                Dictionary<int, decimal> before = new Dictionary<int, decimal>();
                for (int i = 0; i < lines.Count; i++)
                {
                    JArray rows;
                    stock.TryGetValue(UiKit.S(lines[i].Mat, "ItemNumber"), out rows);
                    before[i] = StockService.Total(rows, "PrimaryOnhandQuantity", lines[i].Sub.Text.Trim());
                }

                DateTime sentUtc = DateTime.UtcNow;
                JArray result = await FusionClient.BatchAsync(parts);
                bool allOk = true;
                Dictionary<int, decimal> accepted = new Dictionary<int, decimal>();
                foreach (JToken p in result)
                {
                    int index = Math.Max(0, int.Parse(UiKit.S(p, "id")) - 1);
                    string status = UiKit.S(p, "status");
                    JToken payload = p["payload"];
                    JObject po = payload as JObject;
                    // Aceptada: sin error. Oracle devuelve la parte con payload vacío cuando la transacción se procesó al recibirla.
                    bool ok = status.Length > 0 ? status.StartsWith("2") : (po == null || (po["title"] == null && po["o:errorDetails"] == null));
                    string item = UiKit.S(lines[index].Mat, "ItemNumber");
                    if (ok) { accepted[index] = before[index]; ConsoleLog.Log(LogKind.Info, "[" + (index + 1) + "] " + item + " · aceptada por Oracle; confirmando contra el stock…"); }
                    else
                    {
                        allOk = false;
                        string why = UiKit.S(po, "detail").Length > 0 ? UiKit.S(po, "detail") : UiKit.S(po, "title");
                        ConsoleLog.Log(LogKind.Error, "[" + (index + 1) + "] " + item + " · rechazada" + (status.Length > 0 ? " (HTTP " + status + ")" : string.Empty) + ": " + why);
                    }
                }
                // No se permite reenviar el mismo batch: las líneas correctas se duplicarían (el stock sube en cada envío).
                if (accepted.Count > 0)
                {
                    lblStatus.Text = "Aceptada por Oracle. Confirmando contra el stock…";
                    allOk &= await VerifyAppliedAsync(accepted, sentUtc);
                }
                lblStatus.Text = allOk ? "Receipt aplicado: el stock subió." : "Revisa la consola. No reenvíes las líneas aplicadas: duplicarían el stock.";
                if (allOk) UiKit.Info("Miscellaneous Receipt aplicado.");
            }
            catch (Exception ex) { UiKit.Error(ex.Message); btnSend.Enabled = true; }
        }
    }
}
