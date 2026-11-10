using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using iFusion.Models;
using iFusion.Services;
using Newtonsoft.Json.Linq;

namespace iFusion.Views
{
    /// <summary>Consulta de órdenes de trabajo (procesos y discreta) y acceso a stock / Miscellaneous Receipt.</summary>
    internal class frmMain : Form
    {
        private static readonly string[][] Statuses =
        {
            new[] { "ORA_UNRELEASED", "No liberada" }, new[] { "ORA_RELEASED", "Liberada" }, new[] { "ORA_ON_HOLD", "En espera" },
            new[] { "ORA_COMPLETED", "Completada" }, new[] { "ORA_CLOSED", "Cerrada" }, new[] { "ORA_CANCELED", "Cancelada" }
        };

        private readonly ComboBox cmbInstance = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230 };
        private readonly ComboBox cmbOrg = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly ComboBox cmbWc = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly TextBox txtWo = new TextBox(), txtItem = new TextBox();
        private readonly StatusChecks lstStatus = new StatusChecks();
        private readonly Button btnSearch = UiKit.Btn("Buscar", true, 130), btnClear = UiKit.Btn("Limpiar", false, 90), btnInstances = UiKit.Btn("Instancias", false, 110);
        private readonly Button tabProcess = UiKit.Btn("Manufactura por procesos", false, 200), tabDiscrete = UiKit.Btn("Manufactura discreta", false, 190);
        private readonly Button btnReceipt = UiKit.Btn("Incrementar stock (0)", true, 190);
        private readonly Button btnNewWo = UiKit.Btn("Nueva OT liberada", false, 160);
        private readonly Button btnConsole = UiKit.Btn("Consola ▲", false, 110);
        private Panel pnlConsole;
        private CardPanel filters;
        private Panel content;
        private readonly InventoryView inventory = new InventoryView();
        private readonly Button secMfg = UiKit.Btn("Manufactura", false, 130), secInv = UiKit.Btn("Inventarios", false, 130);
        private bool inventorySection, countSub;
        private readonly CycleCountView cycle = new CycleCountView();
        private readonly Button subStock = UiKit.Btn("Existencias", false, 120), subCount = UiKit.Btn("Conteos cíclicos", false, 150);
        private readonly DataGridView gridWo = UiKit.Grid(false);
        private readonly FlowLayoutPanel detail = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(0, 6, 0, 0) };
        private readonly Label lblStatus = UiKit.Lbl("Selecciona una instancia y realiza una búsqueda.", false, 9f, Ui.TextSecondary);

        private bool process = true, loadingInstances;
        private List<JToken> orgs = new List<JToken>();
        private JArray workOrders = new JArray();
        private readonly List<DataGridView> matGrids = new List<DataGridView>();
        private readonly List<Control> wideControls = new List<Control>();

        public frmMain()
        {
            Text = "iFusion · Órdenes de Trabajo";
            ClientSize = new Size(1380, 820);
            MinimumSize = new Size(1100, 680);
            StartPosition = FormStartPosition.CenterScreen;
            WindowState = FormWindowState.Maximized; // ocupa toda el área de trabajo al iniciar, con minimizar / restaurar / cerrar a la vista
            Font = new Font(Ui.FontFamily, 9f);
            Icon = ThemeManager.LoadAppIcon();

            BuildLayout();
            ThemeManager.Apply(this, true);
            MarkTabs();
            Load += async (s, e) => { FillInstances(); await LoadOrganizationsAsync(); };
        }

        #region Layout

        private void BuildLayout()
        {
            // Barra superior
            CardPanel top = new CardPanel { Dock = DockStyle.Top, Height = 52, Style = CardStyle.TopBar };
            Label brand = UiKit.Lbl("iFusion", true, 14f, Ui.Accent); brand.Location = new Point(16, 13);
            Label brandSub = UiKit.Lbl("Oracle Fusion Cloud", false, 9f, Ui.TextSecondary); brandSub.Location = new Point(92, 19);
            Label lblInst = UiKit.Lbl("Instancia activa", false, 9f, Ui.TextSecondary);
            secMfg.Location = new Point(250, 11); secInv.Location = new Point(388, 11);
            secMfg.Click += (s, e) => SetSection(false);
            secInv.Click += async (s, e) => { SetSection(true); await EnterInventorySubAsync(); };
            inventory.Status = text => lblStatus.Text = text;
            cycle.Status = text => lblStatus.Text = text;
            subStock.Location = new Point(560, 11); subCount.Location = new Point(688, 11);
            subStock.Visible = subCount.Visible = false;
            subStock.Click += async (s, e) => { countSub = false; SetSection(true); await EnterInventorySubAsync(); };
            subCount.Click += async (s, e) => { countSub = true; SetSection(true); await EnterInventorySubAsync(); };
            top.Controls.AddRange(new Control[] { brand, brandSub, secMfg, secInv, subStock, subCount, lblInst, cmbInstance, btnInstances });
            top.Resize += (s, e) =>
            {
                btnInstances.Location = new Point(top.Width - btnInstances.Width - 16, 11);
                cmbInstance.Location = new Point(btnInstances.Left - cmbInstance.Width - 10, 14);
                lblInst.Location = new Point(cmbInstance.Left - lblInst.Width - 8, 19);
            };

            // Barra inferior (estado + botón de la consola; a la derecha queda libre el interruptor de tema)
            CardPanel bottom = new CardPanel { Dock = DockStyle.Bottom, Height = 32, Style = CardStyle.BottomBar };
            lblStatus.Location = new Point(16, 8);
            btnConsole.Height = 24;
            bottom.Controls.Add(lblStatus); bottom.Controls.Add(btnConsole);
            bottom.Resize += (s, e) => btnConsole.Location = new Point(bottom.Width - btnConsole.Width - 44, 4);

            // Consola (oculta hasta pulsar el botón), encima de la barra inferior
            pnlConsole = new Panel { Dock = DockStyle.Bottom, Height = 250, Padding = new Padding(12, 0, 12, 8), Visible = false };
            pnlConsole.Controls.Add(new ConsolePanel { Dock = DockStyle.Fill });
            btnConsole.Click += (s, e) =>
            {
                pnlConsole.Visible = !pnlConsole.Visible;
                btnConsole.Text = pnlConsole.Visible ? "Consola ▼" : "Consola ▲";
            };

            // Filtros
            filters = new CardPanel { Dock = DockStyle.Left, Width = 300, Margin = new Padding(0) };
            int y = 14;
            Action<string, Control, int> field = (cap, ctl, h) =>
            {
                Label l = UiKit.Lbl(cap, false, 8.5f, Ui.TextSecondary); l.Location = new Point(16, y);
                ctl.SetBounds(16, y + 18, 268, h);
                filters.Controls.Add(l); filters.Controls.Add(ctl);
                y += 18 + h + 12;
            };
            Label title = UiKit.Lbl("Búsqueda de OT", true, 11f); title.Location = new Point(16, y); filters.Controls.Add(title); y += 30;
            field("Organización de inventario", cmbOrg, 26);
            field("Número de OT (ignora organización y estatus)", txtWo, 26);
            field("Estatus", lstStatus, 138);
            field("Artículo (Item Number)", txtItem, 26);
            field("Centro de trabajo", cmbWc, 26);
            btnSearch.Location = new Point(16, y); btnClear.Location = new Point(158, y);
            filters.Controls.Add(btnSearch); filters.Controls.Add(btnClear);
            foreach (string[] s in Statuses) lstStatus.Add(s[1]);

            // Contenido
            content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 8, 12, 8) };
            Panel tabs = new Panel { Dock = DockStyle.Top, Height = 40 };
            tabProcess.Location = new Point(0, 4); tabDiscrete.Location = new Point(210, 4);
            btnReceipt.Enabled = false;
            tabs.Controls.AddRange(new Control[] { tabProcess, tabDiscrete, btnNewWo, btnReceipt });
            tabs.Resize += (s, e) => { btnReceipt.Left = tabs.Width - btnReceipt.Width; btnNewWo.Left = btnReceipt.Left - btnNewWo.Width - 8; };

            gridWo.Dock = DockStyle.Top; gridWo.Height = 190;
            UiKit.Col(gridWo, "Orden", 1.1f); UiKit.Col(gridWo, "Org.", 0.9f); UiKit.Col(gridWo, "Estatus", 1f); UiKit.Col(gridWo, "Artículo", 1.2f); UiKit.Col(gridWo, "Descripción", 2.2f);
            UiKit.Col(gridWo, "Cantidad", 0.9f, true); UiKit.Col(gridWo, "Completado", 0.9f, true); UiKit.Col(gridWo, "Inicio plan.", 1f); UiKit.Col(gridWo, "Fin plan.", 1f);
            gridWo.SelectionChanged += (s, e) => ShowSelectedWorkOrder();
            Panel gap = new Panel { Dock = DockStyle.Top, Height = 6 };

            content.Controls.Add(detail);
            content.Controls.Add(gap);
            content.Controls.Add(gridWo);
            content.Controls.Add(tabs);

            Controls.Add(content);
            inventory.Visible = false;
            cycle.Visible = false;
            Controls.Add(inventory);
            Controls.Add(cycle);
            Controls.Add(filters);
            Controls.Add(top);
            Controls.Add(pnlConsole);
            Controls.Add(bottom);

            // Eventos
            cmbInstance.SelectedIndexChanged += async (s, e) => await OnInstanceChangedAsync();
            btnInstances.Click += async (s, e) => await OpenInstancesAsync();
            cmbOrg.SelectedIndexChanged += async (s, e) => await LoadWorkCentersAsync();
            btnSearch.Click += async (s, e) => await SearchAsync();
            btnClear.Click += (s, e) => { txtWo.Clear(); txtItem.Clear(); cmbWc.SelectedIndex = 0; for (int i = 0; i < lstStatus.Count; i++) lstStatus.SetItemChecked(i, false); };
            txtWo.KeyDown += async (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await SearchAsync(); } };
            txtItem.KeyDown += async (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await SearchAsync(); } };
            tabProcess.Click += async (s, e) => await SwitchKindAsync(true);
            tabDiscrete.Click += async (s, e) => await SwitchKindAsync(false);
            btnReceipt.Click += (s, e) => OpenReceipt();
            btnNewWo.Click += async (s, e) => await OpenCreateWoAsync();
            detail.SizeChanged += (s, e) => ResizeWide();
        }

        private string OrgCodeSelected() { Item o = cmbOrg.SelectedItem as Item; return o == null ? null : (string)o.Value; }

        /// <summary>Alterna entre las secciones Manufactura (OT) e Inventarios.</summary>
        private void SetSection(bool inventories)
        {
            inventorySection = inventories;
            filters.Visible = content.Visible = !inventories;
            inventory.Visible = inventories && !countSub;
            cycle.Visible = inventories && countSub;
            subStock.Visible = subCount.Visible = inventories;
            MarkSections();
            Text = inventories ? "iFusion · Inventarios" : "iFusion · Órdenes de Trabajo";
        }

        /// <summary>Carga lo que necesita la subsección activa de Inventarios (existencias o conteos cíclicos).</summary>
        private async System.Threading.Tasks.Task EnterInventorySubAsync()
        {
            if (countSub) await cycle.EnsureLoadedAsync();
            else await inventory.EnsureOrgsAsync(OrgCodeSelected());
        }

        private void MarkSections()
        {
            UiKit.Style(secMfg, !inventorySection);
            UiKit.Style(secInv, inventorySection);
            UiKit.Style(subStock, inventorySection && !countSub);
            UiKit.Style(subCount, inventorySection && countSub);
            secMfg.Font = new Font(inventorySection ? Ui.FontFamily : Ui.FontFamilySemibold, 9f);
            secInv.Font = new Font(inventorySection ? Ui.FontFamilySemibold : Ui.FontFamily, 9f);
        }

        private void MarkTabs()
        {
            MarkSections();
            tabProcess.Text = "Manufactura por procesos";
            tabDiscrete.Text = "Manufactura discreta";
            UiKit.Style(tabProcess, process);
            UiKit.Style(tabDiscrete, !process);
            tabProcess.Font = new Font(process ? Ui.FontFamilySemibold : Ui.FontFamily, 9f);
            tabDiscrete.Font = new Font(process ? Ui.FontFamily : Ui.FontFamilySemibold, 9f);
        }

        #endregion

        #region Instancias, organizaciones y centros de trabajo

        private void FillInstances()
        {
            loadingInstances = true;
            cmbInstance.Items.Clear();
            foreach (Instance i in InstanceStore.All) cmbInstance.Items.Add(i);
            cmbInstance.SelectedItem = InstanceStore.Active;
            loadingInstances = false;
        }

        private async System.Threading.Tasks.Task OpenInstancesAsync()
        {
            using (frmInstances f = new frmInstances()) f.ShowDialog(this);
            FillInstances();
            await OnInstanceChangedAsync(true);
            if (InstanceStore.All.Count == 0) lblStatus.Text = "Configura una instancia de Oracle Cloud.";
        }

        private async System.Threading.Tasks.Task OnInstanceChangedAsync(bool force = false)
        {
            if (loadingInstances && !force) return;
            Instance sel = cmbInstance.SelectedItem as Instance;
            if (sel != null) InstanceStore.Active = sel;
            ClearResults();
            inventory.Reset();
            cycle.Reset();
            await LoadOrganizationsAsync();
            if (inventorySection) await EnterInventorySubAsync();
        }

        private async System.Threading.Tasks.Task LoadOrganizationsAsync()
        {
            cmbOrg.Items.Clear(); cmbWc.Items.Clear(); cmbWc.Items.Add(new Item("— Todos —", null));
            cmbWc.SelectedIndex = 0; orgs.Clear();
            ApplyManufacturing();
            if (InstanceStore.Active == null)
            {
                lblStatus.Text = "No hay instancias configuradas.";
                if (InstanceStore.All.Count == 0 && !IsDisposed) BeginInvoke(new Action(async () => await OpenInstancesAsync()));
                return;
            }
            try
            {
                SetBusy("Cargando organizaciones…");
                JObject res = await FusionClient.GetAsync(EndPoints.InventoryOrganizations);
                // solo organizaciones de manufactura (planta)
                orgs = ((JArray)res["items"] ?? new JArray()).Where(o => UiKit.S(o, "InventoryFlag") != "False" && UiKit.S(o, "ManufacturingPlantFlag") == "True")
                    .OrderBy(o => UiKit.S(o, "OrganizationCode")).ToList();
                foreach (JToken o in orgs) cmbOrg.Items.Add(new Item(UiKit.S(o, "OrganizationCode") + " · " + UiKit.S(o, "OrganizationName"), UiKit.S(o, "OrganizationCode")));
                if (cmbOrg.Items.Count > 0) cmbOrg.SelectedIndex = 0;
                lblStatus.Text = "Instancia \"" + InstanceStore.Active.Name + "\" lista.";
            }
            catch (Exception ex) { lblStatus.Text = "Error al cargar organizaciones."; UiKit.Error(ex.Message); }
        }

        private async System.Threading.Tasks.Task LoadWorkCentersAsync()
        {
            cmbWc.Items.Clear(); cmbWc.Items.Add(new Item("— Todos —", null)); cmbWc.SelectedIndex = 0;
            Item org = cmbOrg.SelectedItem as Item;
            if (org == null) return;
            try
            {
                JObject res = await FusionClient.GetAsync(EndPoints.WorkCentersPath((string)org.Value));
                foreach (JToken w in ((JArray)res["items"] ?? new JArray()).OrderBy(w => UiKit.S(w, "WorkCenterCode")))
                    cmbWc.Items.Add(new Item(UiKit.S(w, "WorkCenterCode") + " · " + UiKit.S(w, "WorkCenterName"), UiKit.S(w, "WorkCenterId")));
            }
            catch (Exception ex) { UiKit.Error(ex.Message); }
        }

        #endregion

        #region Búsqueda

        private async System.Threading.Tasks.Task SwitchKindAsync(bool isProcess)
        {
            if (process == isProcess) return;
            process = isProcess; MarkTabs(); ClearResults();
            if (cmbOrg.SelectedItem != null) await SearchAsync();
        }

        private void ClearResults()
        {
            workOrders = new JArray(); gridWo.Rows.Clear(); ClearDetail(); UpdateReceiptButton();
        }

        private async System.Threading.Tasks.Task SearchAsync()
        {
            Item org = cmbOrg.SelectedItem as Item;
            string number = txtWo.Text.Trim();
            // Con número de OT se busca en todas las organizaciones (y sin estatus ni centro de trabajo): la OT indica su organización.
            if (org == null && number.Length == 0) { UiKit.Error("Selecciona una organización."); return; }
            EndPoints.WorkOrderFilter f = new EndPoints.WorkOrderFilter
            {
                OrgCode = number.Length > 0 ? null : (string)org.Value,
                WorkOrderNumber = number,
                ItemNumber = txtItem.Text.Trim()
            };
            Item wc = cmbWc.SelectedItem as Item;
            if (number.Length == 0 && wc != null && wc.Value != null) f.WorkCenterId = long.Parse((string)wc.Value);
            if (number.Length == 0)
                for (int i = 0; i < Statuses.Length; i++) if (lstStatus.GetItemChecked(i)) f.Statuses.Add(Statuses[i][0]);

            btnSearch.Enabled = false;
            try
            {
                SetBusy("Consultando órdenes de trabajo…");
                JObject res = await FusionClient.GetAsync(EndPoints.WorkOrderUrl(process, f));
                // OT no encontrada en este tipo de manufactura: se prueba con el otro si la instancia lo usa
                Instance inst = InstanceStore.Active;
                bool otherAllowed = inst != null && (process ? inst.AllowsDiscrete : inst.AllowsProcess);
                if (number.Length > 0 && FusionClient.Items(res["items"]).Count == 0 && otherAllowed)
                {
                    ConsoleLog.Log(LogKind.Info, "OT " + number + " no está en manufactura " + (process ? "por procesos" : "discreta") + "; se busca en " + (process ? "discreta" : "por procesos"));
                    process = !process; MarkTabs();
                    res = await FusionClient.GetAsync(EndPoints.WorkOrderUrl(process, f));
                }
                bool hasMore = (bool?)res["hasMore"] == true;
                ClearResults();
                workOrders = FusionClient.Items(res["items"]);
                gridWo.SuspendLayout();
                foreach (JToken wo in workOrders)
                {
                    int i = gridWo.Rows.Add(UiKit.S(wo, "WorkOrderNumber"), UiKit.S(wo, "OrganizationCode"), StatusName(UiKit.S(wo, "WorkOrderStatusCode")), UiKit.S(wo, "ItemNumber"), UiKit.S(wo, "Description"),
                        UiKit.Num(wo, process ? "PrimaryProductQuantity" : "PlannedStartQuantity"), UiKit.Num(wo, "CompletedQuantity"),
                        UiKit.Day(wo, "PlannedStartDate"), UiKit.Day(wo, "PlannedCompletionDate"));
                    gridWo.Rows[i].Tag = wo;
                }
                gridWo.ResumeLayout();
                lblStatus.Text = workOrders.Count + " orden(es) encontrada(s)" + (hasMore ? " (hay más resultados; afina los filtros)." : ".");
                ConsoleLog.Log(workOrders.Count == 0 ? LogKind.Warn : LogKind.Ok, lblStatus.Text);
                if (gridWo.Rows.Count > 0) { gridWo.ClearSelection(); gridWo.Rows[0].Selected = true; ShowSelectedWorkOrder(); }
            }
            catch (Exception ex) { lblStatus.Text = "Error en la consulta."; UiKit.Error(ex.Message); }
            finally { btnSearch.Enabled = true; }
        }

        /// <summary>Selecciona en el filtro la organización de la OT mostrada (stock y Receipt usan la organización seleccionada).</summary>
        private void SyncOrganization(JToken wo)
        {
            string code = UiKit.S(wo, "OrganizationCode");
            Item current = cmbOrg.SelectedItem as Item;
            if (code.Length == 0 || (current != null && (string)current.Value == code)) return;
            foreach (Item it in cmbOrg.Items)
                if ((string)it.Value == code)
                {
                    cmbOrg.SelectedItem = it;
                    lblStatus.Text = "OT " + UiKit.S(wo, "WorkOrderNumber") + " · organización " + code + " seleccionada automáticamente.";
                    ConsoleLog.Log(LogKind.Info, lblStatus.Text);
                    return;
                }
            ConsoleLog.Log(LogKind.Warn, "La organización " + code + " de la OT " + UiKit.S(wo, "WorkOrderNumber") + " no está en la lista de organizaciones de inventario.");
        }

        /// <summary>Muestra solo los tipos de manufactura que usa la instancia activa (se configura en Instancias).</summary>
        private void ApplyManufacturing()
        {
            Instance inst = InstanceStore.Active;
            bool allowProcess = inst == null || inst.AllowsProcess, allowDiscrete = inst == null || inst.AllowsDiscrete;
            tabProcess.Visible = allowProcess;
            tabDiscrete.Visible = allowDiscrete;
            tabDiscrete.Location = new Point(allowProcess ? 210 : 0, 4);
            bool wanted = process;
            if (process && !allowProcess) process = false;
            else if (!process && !allowDiscrete) process = true;
            MarkTabs();
            if (wanted != process) ClearResults();
        }

        private static string StatusName(string code)
        {
            string[] hit = Statuses.FirstOrDefault(s => s[0] == code);
            return hit != null ? hit[1] : code;
        }

        private void SetBusy(string text) { lblStatus.Text = text; }

        #endregion

        #region Detalle de la OT (por operación)

        private void ClearDetail()
        {
            stockVersion++; matGrids.Clear(); wideControls.Clear();
            foreach (Control c in detail.Controls.Cast<Control>().ToList()) { detail.Controls.Remove(c); c.Dispose(); }
        }

        private void ShowSelectedWorkOrder()
        {
            if (gridWo.SelectedRows.Count == 0) return;
            JToken wo = gridWo.SelectedRows[0].Tag as JToken;
            if (wo == null) return;
            if (ReferenceEquals(wo, shownWo) && detail.Controls.Count > 0) return; // la selección se dispara dos veces al buscar: evita repetir el stock
            shownWo = wo;
            SyncOrganization(wo);

            detail.SuspendLayout();
            ClearDetail();
            detail.Controls.Add(BuildHeader(wo));

            // framework 4: cada colección hija llega como { items: [...], count, … }
            JArray ops = FusionClient.Items(wo[process ? "Operation" : "WorkOrderOperation"]);
            JArray mats = FusionClient.Items(wo[process ? "ProcessWorkOrderMaterial" : "WorkOrderMaterial"]);
            JArray ress = FusionClient.Items(wo[process ? "ProcessWorkOrderResource" : "WorkOrderResource"]);
            JArray outs = process ? FusionClient.Items(wo["ProcessWorkOrderOutput"]) : new JArray();
            Func<JToken, int> seq = t => (int)UiKit.D(t, "OperationSequenceNumber");
            IEnumerable<int> seqs = ops.Select(seq).Concat(mats.Select(seq)).Concat(ress.Select(seq)).Concat(outs.Select(seq)).Distinct().OrderBy(x => x);
            foreach (int s in seqs)
            {
                JToken op = ops.FirstOrDefault(o => seq(o) == s);
                detail.Controls.Add(BuildOperation(wo, s, op, mats.Where(m => seq(m) == s), ress.Where(r => seq(r) == s), outs.Where(o => seq(o) == s)));
            }
            if (!seqs.Any()) detail.Controls.Add(UiKit.Lbl("Esta orden no tiene operaciones.", false, 9f, Ui.Muted));
            ThemeManager.ApplyTo(detail);
            detail.ResumeLayout(true);
            ResizeWide();
            UpdateReceiptButton();
            var ignored = LoadStocksAsync();
        }

        private int stockVersion;
        private JToken shownWo;

        /// <summary>Llena Disponible / Existencia de cada material con el stock total del artículo en la organización (un batch por tanda).</summary>
        private async System.Threading.Tasks.Task LoadStocksAsync()
        {
            int ver = ++stockVersion;
            Item org = cmbOrg.SelectedItem as Item;
            List<DataGridView> grids = matGrids.ToList();
            if (org == null || grids.Count == 0) return;
            List<string> items = grids.SelectMany(g => g.Rows.Cast<DataGridViewRow>()).Select(r => UiKit.S((JToken)r.Tag, "ItemNumber")).Distinct().ToList();
            Dictionary<string, JArray> stock = null;
            Dictionary<string, bool> lots = null;
            try { stock = await StockService.LoadAsync(items, (string)org.Value); }
            catch (Exception ex) { if (ver == stockVersion) UiKit.Error("No se pudo consultar el stock de los materiales: " + ex.Message); }
            if (ver != stockVersion) return; // se seleccionó otra OT mientras se consultaba
            try { lots = await StockService.LoadLotsAsync(items, (string)org.Value); }
            catch (Exception ex) { if (ver == stockVersion) UiKit.Error("No se pudo consultar el control de lote: " + ex.Message); }
            if (ver != stockVersion) return;
            foreach (DataGridView g in grids)
            {
                foreach (DataGridViewRow r in g.Rows)
                {
                    string item = UiKit.S((JToken)r.Tag, "ItemNumber");
                    JArray rows = null;
                    bool ok = stock != null && stock.TryGetValue(item, out rows);
                    // Solo el subinventario de suministro del material (si no tiene, el total de la organización).
                    string sub = UiKit.S((JToken)r.Tag, "SupplySubinventory");
                    if (sub.Length == 0) sub = null;
                    r.Cells["avail"].Tag = ok ? (object)StockService.Total(rows, "AvailableToTransact", sub) : null;
                    r.Cells["avail"].Value = ok ? StockService.Fmt(StockService.Total(rows, "AvailableToTransact", sub)) : "n/d";
                    r.Cells["onhand"].Value = ok ? StockService.Fmt(StockService.Total(rows, "PrimaryOnhandQuantity", sub)) : "n/d";
                    bool lot;
                    r.Cells["lot"].Value = lots != null && lots.TryGetValue(item, out lot) ? (lot ? "✔" : "—") : "n/d";
                }
            }
            foreach (DataGridView g in grids) g.Invalidate();
        }

        /// <summary>Indica si el producto terminado lleva control de lote (misma consulta con caché que los materiales).</summary>
        private async System.Threading.Tasks.Task ShowFinishedLotAsync(Label target, string item, string orgCode)
        {
            try
            {
                Dictionary<string, bool> r = await StockService.LoadLotsAsync(new[] { item }, orgCode);
                if (target.IsDisposed) return;
                bool lot = r.TryGetValue(item, out lot) && lot;
                target.Text = lot ? "✔ Con lote" : "Sin lote";
                target.ForeColor = lot ? Ui.SuccessText : Ui.Text;
            }
            catch { if (!target.IsDisposed) target.Text = "n/d"; }
        }

        private Control BuildHeader(JToken wo)
        {
            CardPanel card = new CardPanel { Height = 92, Margin = new Padding(0, 0, 0, 8) };
            Label num = UiKit.Lbl("OT " + UiKit.S(wo, "WorkOrderNumber"), true, 13f, Ui.Accent); num.Location = new Point(14, 8);
            Badge badge = new Badge { Text = StatusName(UiKit.S(wo, "WorkOrderStatusCode")), Kind = BadgeKind.Info, Location = new Point(190, 12) };
            card.Controls.Add(num); card.Controls.Add(badge);

            string qtyKey = process ? "PrimaryProductQuantity" : "PlannedStartQuantity";
            string uom = UiKit.S(wo, process ? "PrimaryProductUOMCode" : "UOMCode");
            // En procesos la terminación está en la salida principal; en discreta, en la propia orden.
            JToken finished = wo;
            if (process)
            {
                List<JToken> outs = FusionClient.Items(wo["ProcessWorkOrderOutput"]).ToList();
                finished = outs.FirstOrDefault(o => UiKit.S(o, "PrimaryFlag").Equals("true", StringComparison.OrdinalIgnoreCase)) ?? outs.FirstOrDefault();
            }
            string[][] kv =
            {
                new[] { "Artículo", UiKit.S(wo, "ItemNumber") }, new[] { "Descripción", UiKit.S(wo, "Description") },
                new[] { "Cantidad", UiKit.Num(wo, qtyKey) + " " + uom }, new[] { "Completado", UiKit.Num(wo, "CompletedQuantity") },
                new[] { "Desechado", UiKit.Num(wo, "ScrappedQuantity") }, new[] { "Rechazado", UiKit.Num(wo, "RejectedQuantity") },
                new[] { "Inicio plan.", UiKit.Day(wo, "PlannedStartDate") }, new[] { "Fin plan.", UiKit.Day(wo, "PlannedCompletionDate") },
                new[] { process ? "Pedido de venta" : "Organización", process ? UiKit.S(FusionClient.Items(wo["ProcessWorkOrderDFF"]).FirstOrDefault(), "pedidoDeVenta") : UiKit.S(wo, "OrganizationCode") },
                new[] { "Últ. actualización", UiKit.Day(wo, "LastUpdateDate") },
                // tercera fila: tipo, definición, control de lote del producto terminado, terminación y suministro
                new[] { "Tipo de OT", UiKit.S(wo, "WorkOrderType") }, new[] { "Definición", UiKit.S(wo, "WorkDefinitionCode") },
                new[] { "PT con lote", "…" },
                new[] { "Subinv. terminación", UiKit.S(finished, process ? "ComplSubinventoryCode" : "CompletionSubinventoryCode") },
                new[] { "Localizador terminación", UiKit.S(finished, "CompletionLocator") },
                new[] { "Tipo de suministro", UiKit.S(wo, "SupplyTypeDescription") }
            };
            int[] xs = { 0, 130, 400, 520, 640 };
            Label lotVal = null;
            for (int i = 0; i < kv.Length; i++)
            {
                int row = i < 5 ? 0 : i < 10 ? 1 : 2, cx = 14 + (row == 0 ? xs[i] : 130 * (i - (row == 1 ? 5 : 10)));
                Label cap = UiKit.Lbl(kv[i][0], false, 8f, Ui.TextSecondary);
                Label val = UiKit.Lbl(string.IsNullOrEmpty(kv[i][1]) ? "—" : kv[i][1], true, 9f);
                val.AutoSize = false; val.Width = (row == 0 && i == 1 ? 260 : 118) - 8; val.Height = 18; val.AutoEllipsis = true;
                cap.Location = new Point(cx, 40 + row * 38); val.Location = new Point(cx, 56 + row * 38);
                if (i == 12) lotVal = val;
                card.Controls.Add(cap); card.Controls.Add(val);
            }
            card.Height = 156;
            if (lotVal != null) { var lotTask = ShowFinishedLotAsync(lotVal, UiKit.S(wo, "ItemNumber"), UiKit.S(wo, "OrganizationCode")); }
            wideControls.Add(card);
            return card;
        }

        private Control BuildOperation(JToken wo, int seq, JToken op, IEnumerable<JToken> mats, IEnumerable<JToken> ress, IEnumerable<JToken> outs)
        {
            FlowLayoutPanel stack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0) };
            CardPanel card = new CardPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(12, 10, 12, 10), Margin = new Padding(0, 0, 0, 10) };
            card.Controls.Add(stack);

            string opInfo = op == null ? string.Empty : "   ·   Lista " + UiKit.Num(op, "ReadyQuantity") + "  ·  Compl. " + UiKit.Num(op, "CompletedQuantity") + "  ·  Desech. " + UiKit.Num(op, "ScrappedQuantity") + "  ·  Rech. " + UiKit.Num(op, "RejectedQuantity") + " " + UiKit.S(op, "UnitOfMeasure");
            Label head = UiKit.Lbl("Operación " + seq + " · " + (op != null ? UiKit.S(op, "OperationName") : "") + (op != null && UiKit.S(op, "WorkCenterName").Length > 0 ? "  (" + UiKit.S(op, "WorkCenterName") + ")" : "") + opInfo, true, 10.5f);
            head.Margin = new Padding(0, 0, 0, 6);
            stack.Controls.Add(head);

            // Materiales
            List<JToken> ml = mats.ToList();
            stack.Controls.Add(Section("Materiales (" + ml.Count + ")", Ui.Accent));
            if (ml.Count == 0) stack.Controls.Add(Empty());
            else
            {
                DataGridView g = UiKit.Grid(true); g.ReadOnly = false; g.Tag = wo;
                UiKit.ThemeGridControls(g);
                g.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "", FillWeight = 22, MinimumWidth = 30, Resizable = DataGridViewTriState.False });
                string[] heads = process
                    ? new[] { "Sec.", "Artículo", "Descripción", "Tipo", "Subinv.", "Requerida", "Emitida", "UOM", "Suministro", "Disponible", "Existencia", "Lote" }
                    : new[] { "Sec.", "Artículo", "Descripción", "Subinv.", "Requerida", "Emitida", "UOM", "Suministro", "Disponible", "Existencia", "Lote" };
                foreach (string h in heads)
                {
                    var c = UiKit.Col(g, h, h == "Descripción" ? 2.4f : h == "Sec." ? 0.5f : h == "Lote" ? 0.6f : 1f, h == "Requerida" || h == "Emitida" || h == "Disponible" || h == "Existencia");
                    c.ReadOnly = true;
                    if (h == "Disponible") c.Name = "avail"; else if (h == "Existencia") c.Name = "onhand";
                    else if (h == "Lote")
                    {
                        c.Name = "lot";
                        c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                        c.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
                    }
                }
                UiKit.StockHighlight(g);
                g.Columns.Add(new DataGridViewButtonColumn { HeaderText = "", Text = "Ver Stock", UseColumnTextForButtonValue = true, FillWeight = 85, MinimumWidth = 80, FlatStyle = FlatStyle.Flat });
                foreach (JToken m in ml)
                {
                    object[] v = process
                        ? new object[] { false, UiKit.S(m, "MaterialSequenceNumber"), UiKit.S(m, "ItemNumber"), UiKit.S(m, "ItemDescription"), UiKit.S(m, "MaterialType"), UiKit.S(m, "SupplySubinventory"), UiKit.Num(m, "Quantity"), UiKit.Num(m, "IssuedQuantity"), UiKit.S(m, "UOMCode"), UiKit.S(m, "SupplyTypeDescription").Length > 0 ? UiKit.S(m, "SupplyTypeDescription") : UiKit.S(m, "SupplyType"), "…", "…", "…", null }
                        : new object[] { false, UiKit.S(m, "MaterialSequenceNumber"), UiKit.S(m, "ItemNumber"), UiKit.S(m, "ItemDescription"), UiKit.S(m, "SupplySubinventory"), UiKit.Num(m, "Quantity"), UiKit.Num(m, "IssuedQuantity"), UiKit.S(m, "UOMCode"), UiKit.S(m, "SupplyTypeDescription"), "…", "…", "…", null };
                    g.Rows[g.Rows.Add(v)].Tag = m;
                }
                g.CurrentCellDirtyStateChanged += (s, e) => { if (g.IsCurrentCellDirty) g.CommitEdit(DataGridViewDataErrorContexts.Commit); };
                g.CellValueChanged += (s, e) => UpdateReceiptButton();
                g.CellContentClick += (s, e) => OnMaterialCellClick(g, e);
                matGrids.Add(g);
                stack.Controls.Add(Inline(g));
            }

            // Recursos
            List<JToken> rl = ress.ToList();
            stack.Controls.Add(Section("Recursos (" + rl.Count + ")", Ui.Warning));
            if (rl.Count == 0) stack.Controls.Add(Empty());
            else
            {
                DataGridView g = UiKit.Grid(true);
                string[] heads = process ? new[] { "Sec.", "Código", "Recurso", "Tipo", "Uso requerido", "Uso real", "UOM", "Cargo" } : new[] { "Sec.", "Código", "Recurso", "Tipo", "Uso requerido", "Uso real", "UOM" };
                foreach (string h in heads) UiKit.Col(g, h, h == "Recurso" ? 2f : h == "Sec." ? 0.5f : 1f, h.StartsWith("Uso "));
                foreach (JToken r in rl)
                {
                    List<object> v = new List<object> { UiKit.S(r, "ResourceSequenceNumber"), UiKit.S(r, "ResourceCode"), UiKit.S(r, "ResourceName"), UiKit.S(r, "ResourceType"), UiKit.Num(r, "RequiredUsage"), UiKit.Num(r, "ActualResourceUsage"), UiKit.S(r, "UOMCode") };
                    if (process) v.Add(UiKit.S(r, "ChargeType"));
                    g.Rows.Add(v.ToArray());
                }
                stack.Controls.Add(Inline(g));
            }

            // Salidas / co-productos / subproductos (solo procesos)
            if (process)
            {
                List<JToken> ol = outs.ToList();
                stack.Controls.Add(Section("Salidas · Co-productos · Subproductos (" + ol.Count + ")", Ui.SuccessText));
                if (ol.Count == 0) stack.Controls.Add(Empty());
                else
                {
                    DataGridView g = UiKit.Grid(true);
                    foreach (string h in new[] { "Sec.", "Artículo", "Descripción", "Tipo", "Principal", "Cant. salida", "Completado", "UOM", "Subinv. compl.", "Compleción" })
                        UiKit.Col(g, h, h == "Descripción" ? 2f : h == "Sec." ? 0.5f : 1f, h.StartsWith("Cant.") || h == "Completado");
                    foreach (JToken o in ol)
                        g.Rows.Add(UiKit.S(o, "OutputSequenceNumber"), UiKit.S(o, "ItemNumber"), UiKit.S(o, "ItemDescription"), UiKit.S(o, "OutputType"),
                            UiKit.S(o, "PrimaryFlag").ToLowerInvariant() == "true" ? "✔" : "", UiKit.Num(o, "OutputQuantity"), UiKit.Num(o, "CompletedQuantity"),
                            UiKit.S(o, "UOMCode"), UiKit.S(o, "ComplSubinventoryCode"), UiKit.S(o, "CompletionType"));
                    stack.Controls.Add(Inline(g));
                }
            }
            wideControls.Add(card);
            return card;
        }

        private Label Section(string text, Color color)
        {
            Label l = UiKit.Lbl(text.ToUpperInvariant(), true, 8.5f, color); l.Margin = new Padding(0, 8, 0, 3); return l;
        }

        private Label Empty()
        {
            Label l = UiKit.Lbl("Sin registros.", false, 9f, Ui.Muted); l.Margin = new Padding(0, 0, 0, 4); return l;
        }

        private Control Inline(DataGridView g)
        {
            g.Height = UiKit.GridHeight(g);
            g.Margin = new Padding(0, 0, 0, 2);
            wideControls.Add(g);
            return g;
        }

        /// <summary>Hace que tarjetas y tablas ocupen el ancho disponible del panel (sin barra horizontal).</summary>
        private void ResizeWide()
        {
            int w = Math.Max(600, detail.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4);
            detail.SuspendLayout();
            foreach (Control c in wideControls)
            {
                if (c is DataGridView) c.Width = w - 26;
                else c.Width = w;
            }
            // las tarjetas AutoSize toman el ancho de su contenido: fijar el del contenedor interno
            foreach (CardPanel cp in detail.Controls.OfType<CardPanel>())
                foreach (FlowLayoutPanel f in cp.Controls.OfType<FlowLayoutPanel>()) f.MinimumSize = new Size(w - 24, 0);
            detail.ResumeLayout(true);
        }

        private void OnMaterialCellClick(DataGridView g, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || !(g.Columns[e.ColumnIndex] is DataGridViewButtonColumn)) return;
            JToken mat = g.Rows[e.RowIndex].Tag as JToken;
            Item org = cmbOrg.SelectedItem as Item;
            if (mat == null || org == null) return;
            using (frmStock f = new frmStock(UiKit.S(mat, "ItemNumber"), (string)org.Value)) f.ShowDialog(this);
        }

        #endregion

        #region Miscellaneous Receipt

        private List<KeyValuePair<JToken, JToken>> SelectedMaterials()
        {
            List<KeyValuePair<JToken, JToken>> sel = new List<KeyValuePair<JToken, JToken>>();
            foreach (DataGridView g in matGrids)
                foreach (DataGridViewRow r in g.Rows)
                    if (r.Cells[0].Value is bool && (bool)r.Cells[0].Value) sel.Add(new KeyValuePair<JToken, JToken>((JToken)r.Tag, (JToken)g.Tag));
            return sel;
        }

        private void UpdateReceiptButton()
        {
            int n = SelectedMaterials().Count;
            btnReceipt.Text = "Incrementar stock (" + n + ")";
            btnReceipt.Enabled = n > 0;
        }

        private async System.Threading.Tasks.Task OpenCreateWoAsync()
        {
            if (InstanceStore.Active != null && !InstanceStore.Active.AllowsDiscrete)
            { UiKit.Info("Esta instancia está configurada solo para manufactura por procesos; la creación de OT usa /workOrders (manufactura discreta)."); return; }
            if (orgs == null || orgs.Count == 0) { UiKit.Error("No hay organizaciones de manufactura cargadas."); return; }
            Item sel = cmbOrg.SelectedItem as Item;
            string created;
            using (frmCreateWo f = new frmCreateWo(orgs, sel == null ? null : (string)sel.Value))
            {
                f.ShowDialog(this);
                created = f.CreatedNumber;
            }
            if (string.IsNullOrEmpty(created)) return;
            // se muestra la OT recién creada
            if (process) await SwitchKindAsync(false);
            txtWo.Text = created;
            await SearchAsync();
        }

        private void OpenReceipt()
        {
            List<KeyValuePair<JToken, JToken>> sel = SelectedMaterials();
            Item org = cmbOrg.SelectedItem as Item;
            if (sel.Count == 0 || org == null) return;
            JToken orgData = orgs.First(o => UiKit.S(o, "OrganizationCode") == (string)org.Value);
            using (frmReceipt f = new frmReceipt(orgData, sel)) f.ShowDialog(this);
            // al volver, el stock pudo cambiar: se consulta de nuevo para actualizar Disponible / Existencia
            var refreshed = LoadStocksAsync();
        }

        #endregion

        /// <summary>Lista de estatus como casillas normales: así el tema claro/oscuro las pinta igual que el resto (un CheckedListBox no).</summary>
        private class StatusChecks : FlowLayoutPanel
        {
            public StatusChecks()
            {
                FlowDirection = FlowDirection.TopDown;
                WrapContents = false;
                Padding = new Padding(2, 2, 0, 0);
            }

            public int Count { get { return Controls.Count; } }

            public void Add(string text)
            {
                Controls.Add(new CheckBox { Text = text, AutoSize = true, Margin = new Padding(0, 0, 0, 3), Cursor = Cursors.Hand });
            }

            public bool GetItemChecked(int i) { return ((CheckBox)Controls[i]).Checked; }

            public void SetItemChecked(int i, bool value) { ((CheckBox)Controls[i]).Checked = value; }
        }

        private class Item
        {
            public readonly string Text; public readonly object Value;
            public Item(string text, object value) { Text = text; Value = value; }
            public override string ToString() { return Text; }
        }
    }
}
