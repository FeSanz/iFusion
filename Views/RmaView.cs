using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using iFusion.Models;
using iFusion.Services;
using Newtonsoft.Json.Linq;

namespace iFusion.Views
{
    /// <summary>
    /// Devoluciones de clientes (RMA): busca órdenes de devolución en Order Management (salesOrdersForOrderHub),
    /// muestra sus líneas con lo ya recibido y registra la recepción física (receivingReceiptRequests).
    /// </summary>
    internal class RmaView : Panel
    {
        private class Choice
        {
            public readonly string Text, Value;
            public Choice(string text, string value) { Text = text; Value = value; }
            public override string ToString() { return Text; }
        }

        private class Section
        {
            public CardPanel Card;
            public FlowLayoutPanel Flow;
            public Control Below;
        }

        private readonly TextBox txtOrder = new TextBox(), txtType = new TextBox(), txtCustomer = new TextBox();
        private readonly ComboBox cmbStatus = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly Button btnSearch = UiKit.Btn("Buscar", true, 130), btnClear = UiKit.Btn("Limpiar", false, 90);
        private readonly DataGridView gridOrders = UiKit.Grid(false);
        private readonly FlowLayoutPanel detail = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        private readonly Button btnReceive = UiKit.Btn("Recibir devolución (0)", true, 220);
        private readonly ToolTip tip = new ToolTip();
        private readonly List<Section> sections = new List<Section>();

        private List<JToken> orders = new List<JToken>();
        private JToken currentOrder;
        private DataGridView gridLines;
        private int version;

        public Action<string> Status = s => { };

        public RmaView()
        {
            Dock = DockStyle.Fill;
            Padding = new Padding(12, 8, 12, 8);

            CardPanel left = new CardPanel { Dock = DockStyle.Left, Width = 320 };
            Label title = UiKit.Lbl("Devoluciones de clientes (RMA)", true, 11f); title.Location = new Point(16, 14);
            int y = 46;
            Action<string, Control> field = (cap, ctl) =>
            {
                Label l = UiKit.Lbl(cap, false, 8.5f, Ui.TextSecondary); l.Location = new Point(16, y);
                ctl.SetBounds(16, y + 18, 288, 26);
                left.Controls.Add(l); left.Controls.Add(ctl);
                y += 56;
            };
            field("Número de orden RMA", txtOrder);
            field("El tipo de orden empieza con", txtType);
            field("Estatus", cmbStatus);
            field("Cliente contiene (sobre los resultados)", txtCustomer);
            txtType.Text = "DEV";
            cmbStatus.Items.Add(new Choice("Abiertas (no cerradas ni canceladas)", "open"));
            cmbStatus.Items.Add(new Choice("Todas", "all"));
            cmbStatus.Items.Add(new Choice("Cerradas", "closed"));
            cmbStatus.SelectedIndex = 0;
            btnSearch.Location = new Point(16, y); btnClear.Location = new Point(158, y);
            gridOrders.SetBounds(16, y + 48, 288, 300);
            gridOrders.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            UiKit.Col(gridOrders, "Orden", 0.8f); UiKit.Col(gridOrders, "Cliente", 1.6f); UiKit.Col(gridOrders, "Estatus", 0.8f);
            left.Controls.AddRange(new Control[] { title, btnSearch, btnClear, gridOrders });
            left.Resize += (s, e) => gridOrders.Height = Math.Max(80, left.Height - gridOrders.Top - 14);

            Panel gap = new Panel { Dock = DockStyle.Left, Width = 10 };
            Controls.Add(detail);
            Controls.Add(gap);
            Controls.Add(left);

            btnSearch.Click += async (s, e) => await SearchAsync();
            btnClear.Click += (s, e) => { txtOrder.Clear(); txtCustomer.Clear(); txtType.Text = "DEV"; cmbStatus.SelectedIndex = 0; orders.Clear(); gridOrders.Rows.Clear(); ClearDetail(); };
            txtOrder.KeyDown += async (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await SearchAsync(); } };
            txtCustomer.TextChanged += (s, e) => FillOrders();
            gridOrders.CellClick += async (s, e) => { if (e.RowIndex >= 0) await ShowOrderAsync(gridOrders.Rows[e.RowIndex].Tag as JToken); };
            gridOrders.KeyDown += async (s, e) =>
            {
                if (e.KeyCode == Keys.Enter && gridOrders.CurrentRow != null) { e.SuppressKeyPress = true; await ShowOrderAsync(gridOrders.CurrentRow.Tag as JToken); }
            };
            detail.SizeChanged += (s, e) => RelayoutAll();
            btnReceive.Click += async (s, e) => await ReceiveAsync();
            btnReceive.Enabled = false;
        }

        public void Reset()
        {
            orders.Clear();
            gridOrders.Rows.Clear();
            ClearDetail();
        }

        #region Búsqueda

        private const string HeaderFields =
            "HeaderId,OrderNumber,SourceTransactionSystem,BusinessUnitName,BuyingPartyName,BuyingPartyNumber,TransactionTypeCode,StatusCode,Status,SubmittedDate," +
            "RequestedFulfillmentOrganizationCode,TransactionalCurrencyCode,CustomerPONumber";

        private const string LineFields =
            "LineId,DisplayLineNumber,SourceTransactionLineNumber,TransactionCategoryCode,ProductNumber,ProductDescription,OrderedQuantity,OrderedUOMCode,RMADeliveredQuantity," +
            "FulfilledQuantity,ReturnReason,ReturnReasonCode,StatusCode,Status,RequestedFulfillmentOrganizationCode,TransactionLineType";

        private static string Enc(string expr) { return Uri.EscapeDataString(expr).Replace("%27", "'").Replace("%3D", "=").Replace("%3C", "<").Replace("%3E", ">"); }

        private async Task SearchAsync()
        {
            if (InstanceStore.Active == null) { UiKit.Error("No hay una instancia activa."); return; }
            List<string> q = new List<string>();
            string number = txtOrder.Text.Trim(), type = txtType.Text.Trim();
            if (number.Length > 0) q.Add("OrderNumber='" + number.Replace("'", "''") + "'");
            else
            {
                if (type.Length > 0) q.Add("TransactionTypeCode like '" + type.Replace("'", "''") + "%'");
                string st = ((Choice)cmbStatus.SelectedItem).Value;
                if (st == "open") { q.Add("StatusCode<>'CLOSED'"); q.Add("StatusCode<>'CANCELED'"); }
                else if (st == "closed") q.Add("StatusCode='CLOSED'");
            }
            if (q.Count == 0) { UiKit.Error("Escribe un número de orden o el inicio del tipo de orden de devolución."); return; }

            btnSearch.Enabled = false;
            try
            {
                Status("Buscando órdenes de devolución…");
                JObject res = await FusionClient.GetAsync("/salesOrdersForOrderHub?limit=200&totalResults=true&onlyData=true&orderBy=SubmittedDate:desc&fields=" + HeaderFields + "&q=" + Enc(string.Join(" and ", q)));
                orders = FusionClient.Items(res).ToList();
                FillOrders();
                string total = UiKit.S(res, "totalResults");
                Status(orders.Count + " orden(es)" + (total.Length > 0 && total != orders.Count.ToString() ? " de " + total : string.Empty) + ".");
                if (orders.Count == 1) await ShowOrderAsync(orders[0]);
            }
            catch (Exception ex) { UiKit.Error(ex.Message); }
            finally { btnSearch.Enabled = true; }
        }

        private void FillOrders()
        {
            string f = txtCustomer.Text.Trim();
            gridOrders.Rows.Clear();
            foreach (JToken o in orders.Where(o => f.Length == 0 || UiKit.S(o, "BuyingPartyName").IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0))
                gridOrders.Rows[gridOrders.Rows.Add(UiKit.S(o, "OrderNumber"), UiKit.S(o, "BuyingPartyName"), UiKit.S(o, "Status"))].Tag = o;
            gridOrders.ClearSelection();
        }

        #endregion

        #region Detalle de la orden

        private void ClearDetail()
        {
            version++;
            currentOrder = null; gridLines = null;
            sections.Clear();
            detail.Controls.Clear();
            btnReceive.Enabled = false;
            btnReceive.Text = "Recibir devolución (0)";
        }

        private async Task ShowOrderAsync(JToken order)
        {
            if (order == null) return;
            int ver = ++version;
            currentOrder = order;
            sections.Clear();
            detail.Controls.Clear();
            gridLines = null;
            btnReceive.Enabled = false;
            detail.Controls.Add(UiKit.Lbl("Consultando las líneas de la orden " + UiKit.S(order, "OrderNumber") + "…", false, 9f, Ui.TextSecondary));
            try
            {
                JObject res = await FusionClient.GetAsync("/salesOrdersForOrderHub/" + UiKit.S(order, "HeaderId") + "/child/lines?limit=200&onlyData=true&fields=" + LineFields);
                if (ver != version) return;
                List<JToken> lines = FusionClient.Items(res).Where(l => UiKit.S(l, "TransactionCategoryCode") == "RETURN").ToList();
                BuildDetail(order, lines);
                Status("RMA " + UiKit.S(order, "OrderNumber") + ": " + lines.Count + " línea(s) de devolución.");
            }
            catch (Exception ex)
            {
                if (ver != version) return;
                detail.Controls.Clear();
                detail.Controls.Add(UiKit.Lbl("No se pudieron consultar las líneas de la orden.", false, 9f, Ui.Danger));
                UiKit.Error(ex.Message);
            }
        }

        private static decimal Dec(JToken t, string key)
        {
            decimal d;
            return decimal.TryParse(UiKit.S(t, key), NumberStyles.Any, CultureInfo.InvariantCulture, out d) ? d : 0m;
        }

        private static decimal Pending(JToken line) { return Math.Max(0m, Dec(line, "OrderedQuantity") - Dec(line, "RMADeliveredQuantity")); }

        private void BuildDetail(JToken order, List<JToken> lines)
        {
            detail.SuspendLayout();
            detail.Controls.Clear();
            sections.Clear();

            CardPanel head = new CardPanel { Height = 78, Margin = new Padding(0, 0, 0, 8), Tag = "wide" };
            Label num = UiKit.Lbl("RMA " + UiKit.S(order, "OrderNumber"), true, 13f, Ui.Accent); num.Location = new Point(14, 8);
            Label desc = UiKit.Lbl(UiKit.S(order, "BuyingPartyName") + "   ·   " + UiKit.S(order, "Status"), false, 9.5f); desc.Location = new Point(14, 38);
            head.Controls.AddRange(new Control[] { num, desc, btnReceive });
            head.Resize += (s, e) => btnReceive.Location = new Point(head.Width - btnReceive.Width - 14, 8);
            detail.Controls.Add(head);

            AddSection("Orden de devolución", new[]
            {
                new[] { "Orden", V(order, "OrderNumber") }, new[] { "Cliente", V(order, "BuyingPartyName") }, new[] { "Número de cliente", V(order, "BuyingPartyNumber") },
                new[] { "Tipo de orden", V(order, "TransactionTypeCode") }, new[] { "Estatus", V(order, "Status") }, new[] { "Fecha de captura", DateOnly(order, "SubmittedDate") },
                new[] { "Unidad de negocio", V(order, "BusinessUnitName") }, new[] { "Organización de recepción", V(order, "RequestedFulfillmentOrganizationCode") },
                new[] { "Sistema origen", V(order, "SourceTransactionSystem") }, new[] { "Moneda", V(order, "TransactionalCurrencyCode") }, new[] { "OC del cliente", V(order, "CustomerPONumber") }
            }, null);

            DataGridView g = UiKit.Grid(false);
            g.ReadOnly = false;
            g.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "", FillWeight = 22, MinimumWidth = 30, Resizable = DataGridViewTriState.False });
            string[] heads = { "Línea", "Artículo", "Descripción", "Ordenado", "UOM", "Recibido", "Pendiente", "Motivo", "Estatus" };
            foreach (string h in heads)
            {
                DataGridViewColumn c = UiKit.Col(g, h, h == "Descripción" ? 2.2f : h == "Línea" || h == "UOM" ? 0.5f : 1f, h == "Ordenado" || h == "Recibido" || h == "Pendiente");
                c.ReadOnly = true;
            }
            UiKit.ThemeGridControls(g);
            foreach (JToken l in lines)
            {
                decimal pend = Pending(l);
                int idx = g.Rows.Add(false, V(l, "DisplayLineNumber"), V(l, "ProductNumber"), V(l, "ProductDescription"), StockService.Fmt(Dec(l, "OrderedQuantity")), V(l, "OrderedUOMCode"),
                    StockService.Fmt(Dec(l, "RMADeliveredQuantity")), StockService.Fmt(pend), V(l, "ReturnReason"), V(l, "Status"));
                g.Rows[idx].Tag = l;
                if (pend <= 0) g.Rows[idx].Cells[0].ReadOnly = true; // nada pendiente de recibir
            }
            g.CurrentCellDirtyStateChanged += (s, e) => { if (g.IsCurrentCellDirty) g.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            g.CellValueChanged += (s, e) => UpdateReceiveButton();
            gridLines = g;
            Control below = lines.Count == 0 ? (Control)UiKit.Lbl("Esta orden no tiene líneas de devolución.", false, 9f, Ui.Warning) : g;
            AddSection("Líneas de devolución · marca las que vas a recibir", new[]
            {
                new[] { "Líneas", lines.Count.ToString() }, new[] { "Con pendiente", lines.Count(l => Pending(l) > 0).ToString() }
            }, below);

            ThemeManager.ApplyTo(detail);
            detail.ResumeLayout(true);
            RelayoutAll();
            UpdateReceiveButton();
        }

        private List<JToken> SelectedLines()
        {
            if (gridLines == null) return new List<JToken>();
            return gridLines.Rows.Cast<DataGridViewRow>().Where(r => r.Cells[0].Value is bool && (bool)r.Cells[0].Value).Select(r => (JToken)r.Tag).ToList();
        }

        private void UpdateReceiveButton()
        {
            int n = SelectedLines().Count;
            btnReceive.Text = "Recibir devolución (" + n + ")";
            btnReceive.Enabled = n > 0;
        }

        private async Task ReceiveAsync()
        {
            List<JToken> sel = SelectedLines();
            if (currentOrder == null || sel.Count == 0) return;
            string number = UiKit.S(currentOrder, "OrderNumber");
            using (frmRmaReceive f = new frmRmaReceive(currentOrder, sel)) f.ShowDialog(FindForm());
            await ShowOrderAsync(currentOrder); // la cantidad recibida pudo cambiar
            Status("RMA " + number + " actualizada.");
        }

        #endregion

        #region Presentación

        private static string V(JToken t, string key)
        {
            JToken v = t == null ? null : t[key];
            if (v == null || v.Type == JTokenType.Null) return "—";
            string s = v.Type == JTokenType.Date ? v.Value<DateTime>().ToString("dd/MM/yyyy HH:mm") : (string)v;
            return string.IsNullOrEmpty(s) ? "—" : s;
        }

        private static string DateOnly(JToken t, string key)
        {
            JToken v = t == null ? null : t[key];
            return v != null && v.Type == JTokenType.Date ? v.Value<DateTime>().ToString("dd/MM/yyyy") : V(t, key);
        }

        private void AddSection(string title, string[][] cells, Control below)
        {
            CardPanel card = new CardPanel { Height = 120, Margin = new Padding(0, 0, 0, 8) };
            Label t = UiKit.Lbl(title, true, 10f, Ui.Accent); t.Location = new Point(14, 10);
            FlowLayoutPanel flow = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = true, AutoSize = false, Location = new Point(10, 36) };
            foreach (string[] c in cells) flow.Controls.Add(Cell(c[0], c[1]));
            card.Controls.Add(t); card.Controls.Add(flow);
            if (below != null) card.Controls.Add(below);
            sections.Add(new Section { Card = card, Flow = flow, Below = below });
            detail.Controls.Add(card);
        }

        private Control Cell(string caption, string value)
        {
            Panel p = new Panel { Width = 210, Height = 42, Margin = new Padding(4, 0, 4, 2) };
            Label cap = UiKit.Lbl(caption, false, 8f, Ui.TextSecondary); cap.Location = new Point(0, 0);
            Label val = UiKit.Lbl(value, true, 9f); val.AutoSize = false; val.Size = new Size(200, 18); val.AutoEllipsis = true; val.Location = new Point(0, 17);
            tip.SetToolTip(val, value);
            p.Controls.Add(cap); p.Controls.Add(val);
            return p;
        }

        private void RelayoutAll()
        {
            if (detail.Controls.Count == 0) return;
            int width = Math.Max(300, detail.ClientSize.Width - 4);
            foreach (Control c in detail.Controls.OfType<CardPanel>().Where(c => (c.Tag as string) == "wide")) c.Width = width;
            foreach (Section s in sections)
            {
                s.Card.Width = width;
                s.Flow.Width = width - 20;
                s.Flow.Height = s.Flow.GetPreferredSize(new Size(s.Flow.Width, 0)).Height;
                int bottom = s.Flow.Bottom;
                if (s.Below != null)
                {
                    s.Below.Left = 14;
                    s.Below.Top = bottom + 6;
                    DataGridView g = s.Below as DataGridView;
                    if (g != null)
                    {
                        g.Width = width - 28;
                        g.Height = 34 + Math.Min(Math.Max(1, g.Rows.Count), 14) * 28;
                    }
                    bottom = s.Below.Bottom;
                }
                s.Card.Height = bottom + 12;
            }
        }

        #endregion
    }
}
