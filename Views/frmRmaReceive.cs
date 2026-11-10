using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using iFusion.Models;
using iFusion.Services;
using Newtonsoft.Json.Linq;

namespace iFusion.Views
{
    /// <summary>Recepción física de una devolución de cliente contra las líneas de una RMA: POST /receivingReceiptRequests.</summary>
    internal class frmRmaReceive : Form
    {
        private class Line
        {
            public JToken Src;
            public bool Lot;
            public TextBox Qty, Sub, LotNo;
            public ComboBox Loc;
        }

        private readonly JToken order;
        private readonly List<Line> lines = new List<Line>();
        private readonly string orgCode;
        private readonly TextBox txtEmployee = new TextBox(), txtComments = new TextBox();
        private readonly FlowLayoutPanel flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(12) };
        private readonly Button btnSend = UiKit.Btn("Registrar recepción", true, 190), btnClose = UiKit.Btn("Cerrar");
        private readonly ConsolePanel console = new ConsolePanel { Dock = DockStyle.Bottom, Height = 170, Padding = new Padding(12, 0, 12, 6) };
        private readonly Label lblStatus = UiKit.Lbl("Verificando control de lote…", false, 9f, Ui.TextSecondary);
        private readonly Dictionary<string, List<string>> locators = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        private static string EmployeeFile
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "iFusion", "receiver.txt"); }
        }

        public frmRmaReceive(JToken rmaOrder, IList<JToken> rmaLines)
        {
            order = rmaOrder;
            orgCode = UiKit.S(rmaLines[0], "RequestedFulfillmentOrganizationCode");
            if (orgCode.Length == 0) orgCode = UiKit.S(order, "RequestedFulfillmentOrganizationCode");
            Text = "Recibir devolución · RMA " + UiKit.S(order, "OrderNumber") + " · " + orgCode;
            ClientSize = new Size(860, 680);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font(Ui.FontFamily, 9f);

            Panel top = new Panel { Dock = DockStyle.Top, Height = 78 };
            Label le = UiKit.Lbl("Empleado que recibe (nombre en Oracle)", false, 8.5f, Ui.TextSecondary); le.Location = new Point(16, 8);
            txtEmployee.SetBounds(16, 28, 330, 26);
            Label lc = UiKit.Lbl("Comentarios (opcional)", false, 8.5f, Ui.TextSecondary); lc.Location = new Point(362, 8);
            txtComments.SetBounds(362, 28, 482, 26);
            Label lo = UiKit.Lbl("Cliente: " + UiKit.S(order, "BuyingPartyName") + "   ·   Organización de recepción: " + orgCode, false, 8.5f, Ui.Muted); lo.Location = new Point(16, 58);
            try { if (File.Exists(EmployeeFile)) txtEmployee.Text = File.ReadAllText(EmployeeFile).Trim(); } catch (Exception) { }
            top.Controls.AddRange(new Control[] { le, txtEmployee, lc, txtComments, lo });

            Panel bar = new Panel { Dock = DockStyle.Bottom, Height = 52 };
            lblStatus.Location = new Point(16, 18);
            bar.Resize += (s, e) =>
            {
                btnClose.Location = new Point(bar.Width - btnClose.Width - 16, 11);
                btnSend.Location = new Point(btnClose.Left - btnSend.Width - 8, 11);
            };
            btnSend.Enabled = false;
            btnSend.Click += async (s, e) => await SendAsync();
            btnClose.Click += (s, e) => Close();
            bar.Controls.AddRange(new Control[] { lblStatus, btnSend, btnClose });

            Controls.Add(flow); Controls.Add(top); Controls.Add(console); Controls.Add(bar);
            ThemeManager.Apply(this);
            Shown += async (s, e) => await BuildAsync(rmaLines);
        }

        private async System.Threading.Tasks.Task BuildAsync(IList<JToken> rmaLines)
        {
            Dictionary<string, bool> lots = new Dictionary<string, bool>();
            try { lots = await StockService.LoadLotsAsync(rmaLines.Select(l => UiKit.S(l, "ProductNumber")), orgCode); }
            catch (Exception ex) { UiKit.Error("No se pudo verificar el control de lote: " + ex.Message); }
            flow.SuspendLayout();
            foreach (JToken l in rmaLines)
            {
                bool lot; lots.TryGetValue(UiKit.S(l, "ProductNumber"), out lot);
                flow.Controls.Add(BuildLine(l, lot));
            }
            flow.ResumeLayout();
            ThemeManager.ApplyTo(flow);
            lblStatus.Text = lines.Count + " línea(s). Indica subinventario y, si lo exige, localizador y lote.";
            btnSend.Enabled = true;
        }

        private Control BuildLine(JToken src, bool lot)
        {
            Line ln = new Line { Src = src, Lot = lot };
            decimal pend = Math.Max(0m, Dec(src, "OrderedQuantity") - Dec(src, "RMADeliveredQuantity"));
            CardPanel card = new CardPanel { Width = 810, Height = lot ? 150 : 122, Margin = new Padding(0, 0, 0, 10) };
            Label head = UiKit.Lbl("Línea " + UiKit.S(src, "DisplayLineNumber") + "  ·  " + UiKit.S(src, "ProductNumber") + "  ·  " + UiKit.S(src, "ProductDescription"), true, 10f);
            head.Location = new Point(14, 10);
            Label sub = UiKit.Lbl("Ordenado " + StockService.Fmt(Dec(src, "OrderedQuantity")) + " " + UiKit.S(src, "OrderedUOMCode") + "   ·   Recibido " +
                StockService.Fmt(Dec(src, "RMADeliveredQuantity")) + "   ·   Pendiente " + StockService.Fmt(pend) + "   ·   " + UiKit.S(src, "ReturnReason") + (lot ? "   ·   CON LOTE" : string.Empty),
                false, 8.5f, lot ? Ui.Warning : Ui.TextSecondary);
            sub.Location = new Point(14, 32);

            ln.Qty = new TextBox { Width = 110, Text = pend.ToString(CultureInfo.CurrentCulture) };
            ln.Sub = new TextBox { Width = 170 };
            ln.Loc = new ComboBox { Width = 230, DropDownStyle = ComboBoxStyle.DropDown };
            Add(card, "Cantidad a recibir", ln.Qty, 14, 58, 110);
            Add(card, "Subinventario destino", ln.Sub, 138, 58, 170);
            Add(card, "Localizador (si lo exige)", ln.Loc, 322, 58, 230);
            if (lot)
            {
                ln.LotNo = new TextBox { Width = 230 };
                Add(card, "Lote del producto devuelto", ln.LotNo, 566, 58, 230);
            }
            // Los localizadores válidos son los que ya existen en el subinventario: se ofrecen al capturarlo, sin elegir ninguno por el usuario.
            ln.Sub.Leave += async (s, e) => await FillLocatorsAsync(ln);
            card.Controls.Add(head); card.Controls.Add(sub);
            lines.Add(ln);
            return card;
        }

        private static void Add(Control parent, string caption, Control box, int x, int y, int w)
        {
            Label l = UiKit.Lbl(caption, false, 8.5f, Ui.TextSecondary);
            l.Location = new Point(x, y);
            box.SetBounds(x, y + 18, w, 26);
            parent.Controls.Add(l); parent.Controls.Add(box);
        }

        private static decimal Dec(JToken t, string key)
        {
            decimal d;
            return decimal.TryParse(UiKit.S(t, key), NumberStyles.Any, CultureInfo.InvariantCulture, out d) ? d : 0m;
        }

        private async System.Threading.Tasks.Task FillLocatorsAsync(Line ln)
        {
            string sub = ln.Sub.Text.Trim();
            if (sub.Length == 0) return;
            try
            {
                List<string> names;
                if (!locators.TryGetValue(sub, out names))
                {
                    JObject r = await FusionClient.GetAsync("/itemStockingInquiries?onlyData=true&limit=500&fields=Locator,LocatorId&q=" +
                        Uri.EscapeDataString("OrganizationCode='" + orgCode.Replace("'", "''") + "' and SubinventoryCode='" + sub.Replace("'", "''") + "'").Replace("%27", "'"));
                    names = FusionClient.Items(r).Select(t => UiKit.S(t, "Locator")).Where(n => n.Length > 0).Distinct().ToList();
                    locators[sub] = names;
                }
                string keep = ln.Loc.Text;
                ln.Loc.Items.Clear();
                foreach (string n in names) ln.Loc.Items.Add(n);
                ln.Loc.Text = keep;
            }
            catch (Exception ex) { ConsoleLog.Log(LogKind.Warn, "No se pudieron consultar los localizadores de " + sub + ": " + ex.Message); }
        }

        private JObject BuildPayload()
        {
            string employee = txtEmployee.Text.Trim();
            if (employee.Length == 0) throw new InvalidOperationException("Captura el nombre del empleado que recibe (Oracle lo exige en la recepción).");
            JArray lineArr = new JArray();
            foreach (Line ln in lines)
            {
                string item = UiKit.S(ln.Src, "ProductNumber");
                decimal qty;
                if (!decimal.TryParse(ln.Qty.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out qty) || qty <= 0) throw new InvalidOperationException(item + ": la cantidad debe ser mayor a cero.");
                decimal pend = Math.Max(0m, Dec(ln.Src, "OrderedQuantity") - Dec(ln.Src, "RMADeliveredQuantity"));
                if (qty > pend && MessageBox.Show(item + ": recibes " + qty.ToString("#,##0.####") + " y solo hay " + pend.ToString("#,##0.####") + " pendientes en la RMA.\n\n¿Continuar de todos modos?",
                        "Cantidad mayor a la pendiente", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) throw new InvalidOperationException("Recepción cancelada.");
                if (ln.Sub.Text.Trim().Length == 0) throw new InvalidOperationException(item + ": captura el subinventario destino.");
                if (ln.Lot && ln.LotNo.Text.Trim().Length == 0) throw new InvalidOperationException(item + ": el artículo lleva lote; captura el lote del producto devuelto.");

                JObject line = new JObject
                {
                    ["ReceiptSourceCode"] = "CUSTOMER",
                    ["SourceDocumentCode"] = "RMA",
                    ["TransactionType"] = "RECEIVE",
                    ["RMANumber"] = UiKit.S(order, "OrderNumber"),
                    ["RMALineNumber"] = UiKit.S(ln.Src, "DisplayLineNumber"),
                    ["OrganizationCode"] = orgCode,
                    ["ItemNumber"] = item,
                    ["Quantity"] = qty,
                    ["UOMCode"] = UiKit.S(ln.Src, "OrderedUOMCode"),
                    ["Subinventory"] = ln.Sub.Text.Trim()
                };
                if (ln.Loc.Text.Trim().Length > 0) line["Locator"] = ln.Loc.Text.Trim();
                if (ln.Lot)
                    line["lotItemLots"] = new JArray { new JObject { ["LotNumber"] = ln.LotNo.Text.Trim(), ["TransactionQuantity"] = qty } };
                lineArr.Add(line);
            }

            JObject header = new JObject
            {
                ["ReceiptSourceCode"] = "CUSTOMER",
                ["CustomerPartyName"] = UiKit.S(order, "BuyingPartyName"),
                ["CustomerPartyNumber"] = UiKit.S(order, "BuyingPartyNumber"),
                ["RMASourceSystemName"] = UiKit.S(order, "SourceTransactionSystem"),
                ["OrganizationCode"] = orgCode,
                ["EmployeeName"] = employee,
                ["TransactionDate"] = DateTimeOffset.Now.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture),
                ["InsertAndProcessFlag"] = true,
                ["lines"] = lineArr
            };
            if (UiKit.S(order, "BusinessUnitName").Length > 0) header["BusinessUnit"] = UiKit.S(order, "BusinessUnitName");
            if (txtComments.Text.Trim().Length > 0) header["Comments"] = txtComments.Text.Trim();
            return header;
        }

        private async System.Threading.Tasks.Task SendAsync()
        {
            JObject payload;
            try { payload = BuildPayload(); }
            catch (InvalidOperationException ex) { UiKit.Error(ex.Message); return; }

            if (MessageBox.Show("Se registrará la recepción de " + lines.Count + " línea(s) de la RMA " + UiKit.S(order, "OrderNumber") + " en \"" + InstanceStore.Active.Name + "\".\n\n¿Continuar?",
                    "Confirmar recepción", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            btnSend.Enabled = false; // evita duplicar la recepción con un doble clic
            try { Directory.CreateDirectory(Path.GetDirectoryName(EmployeeFile)); File.WriteAllText(EmployeeFile, txtEmployee.Text.Trim()); } catch (Exception) { }
            lblStatus.Text = "Enviando la recepción…";
            try
            {
                JObject res = await FusionClient.PostAsync("/receivingReceiptRequests", payload);
                string id = UiKit.S(res, "HeaderInterfaceId");
                ConsoleLog.Log(LogKind.Info, "Recepción enviada · HeaderInterfaceId " + id + " · estatus " + UiKit.S(res, "ProcessingStatusCode") + "; confirmando…");
                lblStatus.Text = "Enviada. Confirmando el procesamiento…";
                await VerifyAsync(id);
            }
            catch (Exception ex)
            {
                btnSend.Enabled = true;
                lblStatus.Text = "No se registró la recepción.";
                UiKit.Error(ex.Message);
            }
        }

        /// <summary>Revisa el estatus de la solicitud de recepción: PENDING = aún en proceso, ERROR = rechazada por Oracle.</summary>
        private async System.Threading.Tasks.Task VerifyAsync(string headerInterfaceId)
        {
            if (headerInterfaceId.Length == 0) { lblStatus.Text = "Enviada (Oracle no devolvió el ID de la solicitud)."; return; }
            string status = string.Empty, receipt = string.Empty;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                await System.Threading.Tasks.Task.Delay(3000);
                try
                {
                    JObject r = await FusionClient.GetAsync("/receivingReceiptRequests?onlyData=true&limit=1&q=HeaderInterfaceId=" + Uri.EscapeDataString(headerInterfaceId));
                    JToken h = FusionClient.Items(r).FirstOrDefault();
                    status = UiKit.S(h, "ProcessingStatusCode"); receipt = UiKit.S(h, "ReceiptNumber");
                }
                catch (Exception) { }
                if (status.Length > 0 && !status.Equals("PENDING", StringComparison.OrdinalIgnoreCase) && !status.Equals("RUNNING", StringComparison.OrdinalIgnoreCase)) break;
            }
            if (status.Equals("ERROR", StringComparison.OrdinalIgnoreCase) || status.Equals("REJECTED", StringComparison.OrdinalIgnoreCase))
            {
                ConsoleLog.Log(LogKind.Error, "La solicitud " + headerInterfaceId + " quedó en " + status + ". Revisa el error en Receiving (Gestionar transacciones de recepción).");
                lblStatus.Text = "Oracle rechazó la recepción (" + status + "). Revisa la consola.";
                UiKit.Error("Oracle dejó la solicitud " + headerInterfaceId + " en estado " + status + ". Revisa el detalle del error en Oracle antes de reintentar.");
                btnSend.Enabled = true;
                return;
            }
            string text = "Recepción registrada · solicitud " + headerInterfaceId + (receipt.Length > 0 ? " · recibo " + receipt : string.Empty) + (status.Length > 0 ? " · " + status : string.Empty);
            ConsoleLog.Log(LogKind.Ok, text);
            lblStatus.Text = text;
            UiKit.Info(text + (status.Equals("PENDING", StringComparison.OrdinalIgnoreCase) ? "\n\nSigue en proceso en Oracle; recarga la RMA en unos minutos para ver lo recibido." : string.Empty));
        }
    }
}
