using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using iFusion.Models;
using iFusion.Services;
using Newtonsoft.Json.Linq;

namespace iFusion.Views
{
    /// <summary>
    /// Sección de Inventarios: busca un artículo y muestra sus datos maestros, atributos físicos y existencias
    /// (subinventario, localizador, lote; existente, reservado y transaccionable). Desde aquí se puede incrementar el stock.
    /// </summary>
    internal class InventoryView : Panel
    {
        private class Section
        {
            public CardPanel Card;
            public FlowLayoutPanel Flow;
            public Control Below;
        }

        private readonly TextBox txtItem = new TextBox();
        private readonly Button btnSearch = UiKit.Btn("Buscar", true, 130), btnClear = UiKit.Btn("Limpiar", false, 90);
        private readonly DataGridView gridItems = UiKit.Grid(false), gridOrgs = UiKit.Grid(false);
        private readonly Label lblOrgs = UiKit.Lbl("Organizaciones que lo tienen", false, 8.5f, Ui.TextSecondary);
        private readonly FlowLayoutPanel detail = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(0, 0, 0, 0) };
        private readonly Button btnGeneral = UiKit.Btn("Incrementar stock (general)", true, 220), btnRow = UiKit.Btn("Incrementar en la fila elegida", false, 220);
        private readonly ToolTip tip = new ToolTip();
        private readonly List<Section> sections = new List<Section>();

        private List<JToken> orgs = new List<JToken>();
        private List<JToken> found = new List<JToken>();   // filas de itemsV2: un artículo por organización
        private string orgsFor;
        private bool filling;
        private int version;
        private string currentItem, currentDescription, currentOrg;
        private JArray stockRows = new JArray();
        private JToken shelfItem; // datos maestros del artículo mostrado (vida útil para comparar con sus lotes)
        private DataGridView gridStock;

        /// <summary>Mensajes de estado para la barra inferior de la ventana principal.</summary>
        public Action<string> Status = s => { };

        public InventoryView()
        {
            Dock = DockStyle.Fill;
            Padding = new Padding(12, 8, 12, 8);

            CardPanel filters = new CardPanel { Dock = DockStyle.Left, Width = 300 };
            Label title = UiKit.Lbl("Búsqueda de artículo", true, 11f); title.Location = new Point(16, 14);
            Label li = UiKit.Lbl("Número de artículo (3 o más caracteres del inicio)", false, 8.5f, Ui.TextSecondary); li.Location = new Point(16, 46);
            txtItem.SetBounds(16, 64, 268, 26);
            btnSearch.Location = new Point(16, 102); btnClear.Location = new Point(158, 102);
            gridItems.SetBounds(16, 150, 268, 200);
            UiKit.Col(gridItems, "Artículo", 1.3f); UiKit.Col(gridItems, "Descripción", 2f);
            lblOrgs.Location = new Point(16, 362);
            gridOrgs.SetBounds(16, 382, 268, 150);
            UiKit.Col(gridOrgs, "Org.", 1f); UiKit.Col(gridOrgs, "Nombre", 2f);
            filters.Controls.AddRange(new Control[] { title, li, txtItem, btnSearch, btnClear, gridItems, lblOrgs, gridOrgs });
            // la lista de artículos y la de organizaciones se reparten el alto disponible
            filters.Resize += (s, e) => LayoutLeft();
            leftPanel = filters;

            Panel gap = new Panel { Dock = DockStyle.Left, Width = 10 };
            Controls.Add(detail);
            Controls.Add(gap);
            Controls.Add(filters);

            btnSearch.Click += async (s, e) => await SearchAsync();
            btnClear.Click += (s, e) => { txtItem.Clear(); gridItems.Rows.Clear(); gridOrgs.Rows.Clear(); found.Clear(); ClearDetail(); };
            txtItem.KeyDown += async (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await SearchAsync(); } };
            gridItems.SelectionChanged += async (s, e) =>
            {
                if (filling || gridItems.CurrentRow == null) return;
                await FillOrgsAsync(Convert.ToString(gridItems.CurrentRow.Cells[0].Value));
            };
            // la organización se elige con un clic (o Enter): no se abre sola cuando hay varias
            gridOrgs.CellClick += async (s, e) => { if (e.RowIndex >= 0) await OpenSelectedOrgAsync(e.RowIndex); };
            gridOrgs.KeyDown += async (s, e) => { if (e.KeyCode == Keys.Enter && gridOrgs.CurrentRow != null) { e.SuppressKeyPress = true; await OpenSelectedOrgAsync(gridOrgs.CurrentRow.Index); } };
            detail.SizeChanged += (s, e) => RelayoutAll();
            btnGeneral.Click += async (s, e) => await OpenReceiptAsync(null);
            btnRow.Click += async (s, e) => await OpenReceiptAsync(SelectedStockRow());
            btnRow.Enabled = false;
        }

        private CardPanel leftPanel;

        /// <summary>Reparte el alto del panel izquierdo entre la lista de artículos y la de organizaciones.</summary>
        private void LayoutLeft()
        {
            if (leftPanel == null) return;
            int space = Math.Max(240, leftPanel.Height - 150 - 14 - 38);
            gridItems.Height = Math.Max(90, Math.Min(34 + Math.Max(1, gridItems.Rows.Count) * 28, space * 55 / 100));
            lblOrgs.Top = gridItems.Bottom + 12;
            gridOrgs.Top = lblOrgs.Bottom + 4;
            gridOrgs.Height = Math.Max(60, leftPanel.Height - gridOrgs.Top - 14);
        }

        private async Task OpenSelectedOrgAsync(int row)
        {
            if (gridItems.CurrentRow == null || row < 0 || row >= gridOrgs.Rows.Count) return;
            await ShowItemAsync(Convert.ToString(gridItems.CurrentRow.Cells[0].Value), Convert.ToString(gridOrgs.Rows[row].Cells[0].Value));
        }

        #region Organizaciones y búsqueda

        /// <summary>Carga las organizaciones de la instancia activa (una vez por instancia): se usan para mostrar nombres y para el receipt.</summary>
        public async Task EnsureOrgsAsync(string preferredOrg = null)
        {
            Instance inst = InstanceStore.Active;
            if (inst == null) { Status("No hay instancias configuradas."); return; }
            if (orgsFor == inst.Id && orgs.Count > 0) return;
            try
            {
                Status("Cargando organizaciones…");
                JObject res = await FusionClient.GetAsync(EndPoints.InventoryOrganizations);
                orgs = FusionClient.Items(res).OrderBy(o => UiKit.S(o, "OrganizationCode")).ToList();
                orgsFor = inst.Id;
                Status("Inventarios: escribe un número de artículo y busca.");
            }
            catch (Exception ex) { Status("Error al cargar organizaciones."); UiKit.Error(ex.Message); }
        }

        /// <summary>Se limpia al cambiar de instancia.</summary>
        public void Reset()
        {
            orgsFor = null;
            orgs = new List<JToken>();
            found.Clear();
            gridItems.Rows.Clear();
            gridOrgs.Rows.Clear();
            ClearDetail();
        }

        private string OrgName(string code)
        {
            JToken o = orgs.FirstOrDefault(x => UiKit.S(x, "OrganizationCode") == code);
            return o == null ? "—" : UiKit.S(o, "OrganizationName");
        }

        private async Task SearchAsync()
        {
            string prefix = txtItem.Text.Trim();
            if (prefix.Length < 3) { UiKit.Error("Escribe al menos 3 caracteres del inicio del número de artículo (Oracle no permite buscar con menos)."); return; }
            btnSearch.Enabled = false;
            try
            {
                await EnsureOrgsAsync();
                Status("Buscando artículos…");
                JObject res = await FusionClient.GetAsync(EndPoints.ItemSearchAllOrgsPath(prefix));
                found = FusionClient.Items(res).ToList();
                List<string> numbers = found.Select(i => UiKit.S(i, "ItemNumber")).Distinct().OrderBy(n => n).ToList();
                filling = true;
                gridItems.Rows.Clear();
                gridOrgs.Rows.Clear();
                foreach (string n in numbers) gridItems.Rows.Add(n, UiKit.S(found.First(i => UiKit.S(i, "ItemNumber") == n), "ItemDescription"));
                filling = false;
                LayoutLeft();
                Status(numbers.Count + " artículo(s) · " + found.Count + " asignación(es) a organizaciones" + (found.Count >= 500 ? " (límite de 500; afina la búsqueda)" : string.Empty) + ".");
                if (numbers.Count == 0) { ClearDetail(); return; }
                gridItems.ClearSelection();
                gridItems.CurrentCell = gridItems.Rows[0].Cells[0];
                gridItems.Rows[0].Selected = true;
                await FillOrgsAsync(numbers[0]);
            }
            catch (Exception ex) { filling = false; UiKit.Error(ex.Message); }
            finally { btnSearch.Enabled = true; }
        }

        /// <summary>Organizaciones que tienen el artículo; el usuario elige de cuál quiere ver el detalle (con una sola se abre sola).</summary>
        private async Task FillOrgsAsync(string itemNumber)
        {
            List<string> codes = found.Where(i => UiKit.S(i, "ItemNumber") == itemNumber).Select(i => UiKit.S(i, "OrganizationCode")).Distinct().OrderBy(c => c).ToList();
            filling = true;
            gridOrgs.Rows.Clear();
            foreach (string c in codes) gridOrgs.Rows.Add(c, OrgName(c));
            filling = false;
            ClearDetail();
            if (codes.Count == 1)
            {
                gridOrgs.CurrentCell = gridOrgs.Rows[0].Cells[0];
                await ShowItemAsync(itemNumber, codes[0]);
            }
            else
            {
                gridOrgs.CurrentCell = null;
                gridOrgs.ClearSelection();
                detail.Controls.Add(UiKit.Lbl(itemNumber + " está en " + codes.Count + " organizaciones: elige una de la lista de la izquierda.", false, 9f, Ui.TextSecondary));
            }
        }

        #endregion

        #region Detalle del artículo

        private void ClearDetail()
        {
            version++;
            currentItem = null;
            sections.Clear();
            detail.Controls.Clear();
            stockRows = new JArray();
        }

        private async Task ShowItemAsync(string itemNumber, string org)
        {
            if (string.IsNullOrEmpty(itemNumber) || string.IsNullOrEmpty(org)) return;
            int ver = ++version;
            currentItem = itemNumber; currentOrg = org;
            sections.Clear();
            detail.Controls.Clear();
            detail.Controls.Add(UiKit.Lbl("Consultando " + itemNumber + " en " + org + "…", false, 9f, Ui.TextSecondary));
            try
            {
                Task<JObject> itemTask = FusionClient.GetAsync(EndPoints.ItemDetailPath(org, itemNumber));
                Task<Dictionary<string, JArray>> stockTask = StockService.LoadAsync(new[] { itemNumber }, org);
                Task<JObject> onhandTask = LoadOnhandAsync(org, itemNumber), lotsTask = LoadLotsInfoAsync(org, itemNumber);
                await Task.WhenAll(itemTask, stockTask, onhandTask, lotsTask);
                if (ver != version) return; // se eligió otro artículo mientras tanto

                JToken item = FusionClient.Items(itemTask.Result).FirstOrDefault();
                JArray rows;
                stockTask.Result.TryGetValue(itemNumber, out rows);
                BuildDetail(item, rows ?? new JArray(), onhandTask.Result, lotsTask.Result, org, itemNumber);
                Status(itemNumber + " · " + org + ": " + (rows == null ? 0 : rows.Count) + " fila(s) de existencia.");
            }
            catch (Exception ex)
            {
                if (ver != version) return;
                detail.Controls.Clear();
                detail.Controls.Add(UiKit.Lbl("No se pudo consultar el artículo.", false, 9f, Ui.Danger));
                UiKit.Error(ex.Message);
            }
        }

        /// <summary>Cantidades reservadas por nivel; si la consulta falla el resto de la pantalla sigue funcionando (Reservado = n/d).</summary>
        private static async Task<JObject> LoadOnhandAsync(string org, string item)
        {
            try { return await FusionClient.GetAsync(EndPoints.OnhandDetailsPath(org, item)); }
            catch (Exception) { return null; }
        }

        /// <summary>Lotes con fechas de origen y caducidad; si falla, la pantalla sigue sin esa sección.</summary>
        private static async Task<JObject> LoadLotsInfoAsync(string org, string item)
        {
            try { return await FusionClient.GetAsync(EndPoints.ItemLotsInfoPath(org, item)); }
            catch (Exception) { return null; }
        }

        private void BuildDetail(JToken item, JArray rows, JObject onhand, JObject lotsInfo, string org, string itemNumber)
        {
            detail.SuspendLayout();
            detail.Controls.Clear();
            sections.Clear();
            currentDescription = UiKit.S(item, "ItemDescription");

            if (item == null)
            {
                detail.Controls.Add(UiKit.Lbl("El artículo " + itemNumber + " no existe en la organización " + org + ".", false, 9f, Ui.Warning));
                detail.ResumeLayout(true);
                return;
            }

            // Encabezado con las acciones de incremento
            CardPanel head = new CardPanel { Height = 78, Margin = new Padding(0, 0, 0, 8) };
            Label num = UiKit.Lbl(itemNumber, true, 13f, Ui.Accent); num.Location = new Point(14, 8);
            Label desc = UiKit.Lbl(currentDescription + "   ·   " + org, false, 9.5f); desc.Location = new Point(14, 38);
            btnGeneral.Location = new Point(14, 0); btnRow.Location = new Point(0, 0);
            head.Controls.AddRange(new Control[] { num, desc, btnGeneral, btnRow });
            head.Resize += (s, e) =>
            {
                btnRow.Location = new Point(head.Width - btnRow.Width - 14, 8);
                btnGeneral.Location = new Point(btnRow.Left - btnGeneral.Width - 8, 8);
            };
            head.Tag = "wide";
            detail.Controls.Add(head);
            btnRow.Enabled = false;

            // Datos del artículo
            AddSection("Datos del artículo", new[]
            {
                P("ID de artículo", item, "ItemId"), P("Clase", item, "ItemClass"), P("Tipo de artículo", item, "UserItemTypeValue"), P("Estatus", item, "ItemStatusValue"),
                P("Aprobación", item, "ApprovalStatusValue"), P("Fase de ciclo de vida", item, "LifecyclePhaseValue"), P("UOM primaria", item, "PrimaryUOMValue"), P("Reservable", item, "ReservableFlag"),
                P("Control de lote", item, "LotControlValue"), P("Prefijo de lote inicial", item, "StartingLotPrefix"), P("Número de lote inicial", item, "StartingLotNumber"),
                P("Control de caducidad", item, "LotExpirationControlValue"), P("Vida útil (días)", item, "ShelfLifeDays"),
                P("Generación de serie", item, "SerialGenerationValue"), P("Prefijo de serie", item, "SerialStartingPrefix"), P("Conteo cíclico", item, "CycleCountEnabledFlag"),
                P("Control de localizador", item, "StockLocatorControlValue"), P("Restringe subinventarios", item, "RestrictSubinventoriesFlag"), P("Restringe localizadores", item, "RestrictLocatorsFlag"),
                P("Almacenable", item, "StockEnabledFlag"), P("Transacciones de material", item, "MaterialTransactionEnabledFlag"), P("Costeable", item, "CostingEnabledFlag"),
                P("Fabricar / Comprar", item, "PlanningMakeBuyValue"), P("Planificador", item, "Planner"), P("Se puede enviar", item, "ShippableFlag"), P("Se puede comprar", item, "PurchasableFlag"),
                P("Creado", item, "CreationDate"), P("Últ. actualización", item, "LastUpdateDate")
            }, UiKit.S(item, "LongDescription").Length > 0 ? UiKit.Lbl("Descripción larga: " + UiKit.S(item, "LongDescription"), false, 9f, Ui.TextSecondary) : null);

            // Atributos físicos
            AddSection("Atributos físicos", new[]
            {
                new[] { "Peso unitario", Pair(item, "UnitWeightQuantity", "WeightUOMValue") }, new[] { "Volumen unitario", Pair(item, "UnitVolumeQuantity", "VolumeUOMValue") },
                new[] { "Largo", Pair(item, "UnitLengthQuantity", "DimensionUOMValue") }, new[] { "Ancho", Pair(item, "UnitWidthQuantity", "DimensionUOMValue") }, new[] { "Alto", Pair(item, "UnitHeightQuantity", "DimensionUOMValue") },
                new[] { "Carga máxima", Pair(item, "MaximumLoadWeight", "WeightUOMValue") }, new[] { "Volumen interno", Pair(item, "InternalVolume", "VolumeUOMValue") }, P("Llenado mínimo (%)", item, "MinimumFillPercent"),
                P("Es contenedor", item, "ContainerItemFlag"), P("Tipo de contenedor", item, "ContainerTypeValue"), P("Cadena de empaque", item, "PackagingStringValue"), P("Tipo de empaque", item, "PackTypeValue"),
                P("Vehículo", item, "VehicleItemFlag"), P("Equipo de almacén", item, "WarehouseEquipmentFlag")
            }, UiKit.Lbl("Tarimas (pallets) y cajas por tarima no vienen en la respuesta de itemsV2; solo se muestran los atributos de peso, volumen, dimensiones, contenedor y empaque.", false, 8.5f, Ui.Muted));

            // Reglas de rotación: lo que el artículo define para FIFO / FEFO y control de lotes
            AddSection("Reglas de rotación y control de lotes (FIFO / LIFO / FEFO)", new[]
            {
                P("Control de lote", item, "LotControlValue"), P("Control de caducidad", item, "LotExpirationControlValue"), P("Vida útil (días)", item, "ShelfLifeDays"),
                P("Base de fecha de caducidad", item, "ExpirationDateCalculationBasisValue"), P("Acción al caducar", item, "ExpirationActionValue"), P("Intervalo de la acción", item, "ExpirationActionInterval"),
                P("Días de maduración", item, "MaturityDays"), P("Días de retención", item, "HoldDays"), P("Intervalo de re-prueba", item, "RetestInterval"),
                P("Control de grado", item, "GradeControlFlag"), P("Grado por omisión", item, "DefaultGradeValue"),
                P("Estatus de lote habilitado", item, "LotStatusEnabledFlag"), P("Estatus de lote por omisión", item, "DefaultLotStatusValue"),
                P("Lote divisible", item, "LotDivisibleFlag"), P("Dividir lotes", item, "LotSplitEnabledFlag"), P("Combinar lotes", item, "LotMergeEnabledFlag"),
                P("Convertir lotes", item, "LotTranslateEnabledFlag"), P("Sublotes", item, "ChildLotEnabledFlag"), P("Control de revisión", item, "RevisionQuantityControlFlag"),
                P("Estatus de serie habilitado", item, "SerialStatusEnabledFlag"), P("Regla de picking", item, "PickingRuleValue"), P("Surtido a granel", item, "BulkPickedFlag"),
                P("Revisa faltantes", item, "CheckMaterialShortageFlag"), P("Merma", item, "ShrinkageRate"),
                P("Cantidad mínima de orden", item, "MinimumOrderQuantity"), P("Cantidad máxima de orden", item, "MaximumOrderQuantity"), P("Múltiplo de lote fijo", item, "FixedLotSizeMultiplier")
            }, UiKit.Lbl("FIFO consume primero el lote más antiguo por fecha de origen, LIFO el más reciente y FEFO el que caduca antes; los tres órdenes aparecen en la tabla de lotes. La regla de picking del artículo es informativa.", false, 8.5f, Ui.Muted));

            shelfItem = item;
            BuildStock(rows, onhand, lotsInfo);

            ThemeManager.ApplyTo(detail);
            detail.ResumeLayout(true);
            RelayoutAll();
        }

        private static string[] P(string caption, JToken t, string key) { return new[] { caption, V(t, key) }; }

        private static string Pair(JToken t, string qtyKey, string uomKey)
        {
            string q = V(t, qtyKey);
            if (q == "—") return q;
            string u = V(t, uomKey);
            return u == "—" ? q : q + " " + u;
        }

        private static string V(JToken t, string key)
        {
            JToken v = t == null ? null : t[key];
            if (v == null || v.Type == JTokenType.Null) return "—";
            switch (v.Type)
            {
                case JTokenType.Boolean: return (bool)v ? "Sí" : "No";
                case JTokenType.Integer:
                    // los identificadores (ItemId) no llevan separador de miles
                    return key.EndsWith("Id", StringComparison.Ordinal) ? v.ToString() : StockService.Fmt(v.Value<decimal>());
                case JTokenType.Float: return StockService.Fmt(v.Value<decimal>());
                case JTokenType.Date: return v.Value<DateTime>().ToString("dd/MM/yyyy HH:mm");
                default: string s = (string)v; return string.IsNullOrEmpty(s) ? "—" : s;
            }
        }

        /// <summary>Tarjeta con título y celdas caption/valor que se acomodan según el ancho disponible.</summary>
        private Section AddSection(string title, string[][] cells, Control below)
        {
            CardPanel card = new CardPanel { Height = 120, Margin = new Padding(0, 0, 0, 8) };
            Label t = UiKit.Lbl(title, true, 10f, Ui.Accent); t.Location = new Point(14, 10);
            FlowLayoutPanel flow = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = true, AutoSize = false, Location = new Point(10, 36) };
            foreach (string[] c in cells) flow.Controls.Add(Cell(c[0], c[1]));
            card.Controls.Add(t); card.Controls.Add(flow);
            if (below != null) card.Controls.Add(below);
            Section s = new Section { Card = card, Flow = flow, Below = below };
            sections.Add(s);
            detail.Controls.Add(card);
            return s;
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
            foreach (Control c in detail.Controls.OfType<CardPanel>().Where(c => (c.Tag as string) == "wide")) c.Width = width - (detail.VerticalScroll.Visible ? 0 : 0);
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
                    if (s.Below is DataGridView) s.Below.Width = width - 28;
                    bottom = s.Below.Bottom;
                }
                s.Card.Height = bottom + 12;
            }
        }

        #endregion

        #region Existencias

        private static string DateU(JToken t, string key)
        {
            JToken v = t == null ? null : t[key];
            if (v == null || v.Type == JTokenType.Null) return "—";
            // las fechas de lote vienen a medianoche UTC: se muestran en UTC para no correrlas un día
            if (v.Type == JTokenType.Date) return v.Value<DateTime>().ToUniversalTime().ToString("dd/MM/yyyy");
            string s = (string)v;
            return string.IsNullOrEmpty(s) ? "—" : s;
        }

        private static DateTime? DateValue(JToken t, string key)
        {
            JToken v = t == null ? null : t[key];
            return v != null && v.Type == JTokenType.Date ? v.Value<DateTime>().ToUniversalTime() : (DateTime?)null;
        }

        private void BuildStock(JArray rows, JObject onhand, JObject lotsInfo)
        {
            Dictionary<string, JToken> lotMap = new Dictionary<string, JToken>();
            if (lotsInfo != null)
                foreach (JToken l in FusionClient.Items(lotsInfo)) lotMap[UiKit.S(l, "LotNumber")] = l;

            // Reservado por (subinventario | localizador | lote), tomado de onhandQuantityDetails
            Dictionary<string, decimal> reserved = new Dictionary<string, decimal>();
            bool haveReserved = onhand != null;
            if (haveReserved)
                foreach (JToken r in FusionClient.Items(onhand))
                {
                    string k = Key(UiKit.S(r, "SubinventoryCode"), UiKit.S(r, "LocatorId"), UiKit.S(r, "LotNumber"));
                    decimal d;
                    decimal.TryParse(UiKit.S(r, "ReservedQuantity"), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out d);
                    reserved[k] = d;
                }

            stockRows = rows;
            decimal onh = 0, avail = 0, res = 0;
            SortedDictionary<string, decimal> bySub = new SortedDictionary<string, decimal>();

            gridStock = UiKit.Grid(false);
            gridStock.Height = 40;
            UiKit.Col(gridStock, "Subinventario", 1.1f); UiKit.Col(gridStock, "Localizador", 1.2f); UiKit.Col(gridStock, "Lote", 1.3f);
            UiKit.Col(gridStock, "Estatus lote", 0.8f); UiKit.Col(gridStock, "Origen", 0.9f); UiKit.Col(gridStock, "Caducidad", 0.9f);
            UiKit.Col(gridStock, "Existencia", 1f, true); UiKit.Col(gridStock, "Reservado", 1f, true); UiKit.Col(gridStock, "Transaccionable", 1.1f, true);
            foreach (JToken r in rows)
            {
                string sub = UiKit.S(r, "SubinventoryCode"), loc = UiKit.S(r, "Locator"), lot = UiKit.S(r, "LotNumber");
                decimal o = Dec(r, "PrimaryOnhandQuantity"), a = Dec(r, "AvailableToTransact"), rv = 0;
                bool hasRes = haveReserved && reserved.TryGetValue(Key(sub, UiKit.S(r, "LocatorId"), lot), out rv);
                onh += o; avail += a; res += rv;
                decimal acc; bySub.TryGetValue(sub, out acc); bySub[sub] = acc + o;
                JToken li;
                lotMap.TryGetValue(lot, out li);
                gridStock.Rows.Add(sub, loc.Length > 0 ? loc : "—", lot.Length > 0 ? lot : "—",
                    li == null ? "—" : V(li, "StatusCode"), DateU(li, "OriginationDate"), DateU(li, "ExpirationDate"),
                    StockService.Fmt(o), haveReserved ? StockService.Fmt(rv) : "n/d", StockService.Fmt(a));
                gridStock.Rows[gridStock.Rows.Count - 1].Tag = r;
            }
            int rowsH = Math.Min(Math.Max(1, rows.Count), 12);
            gridStock.Height = 34 + rowsH * 28;
            gridStock.SelectionChanged += (s, e) => btnRow.Enabled = SelectedStockRow() != null;
            gridStock.ClearSelection();
            btnRow.Enabled = false;

            List<string[]> cells = new List<string[]>
            {
                new[] { "Existencia total", StockService.Fmt(onh) },
                new[] { "Reservado", haveReserved ? StockService.Fmt(res) : "n/d" },
                new[] { "Transaccionable", StockService.Fmt(avail) }
            };
            foreach (KeyValuePair<string, decimal> kv in bySub) cells.Add(new[] { "Subinv. " + kv.Key, StockService.Fmt(kv.Value) });

            Control below = rows.Count == 0 ? (Control)UiKit.Lbl("Sin existencias de este artículo en la organización.", false, 9f, Ui.Warning) : gridStock;
            AddSection("Existencias · subinventarios, localizadores y lotes", cells.ToArray(), below);
            if (lotMap.Count > 0) AddLotsSection(lotMap.Values.ToList());
        }

        /// <summary>Todos los lotes del artículo con su orden FIFO (origen más antiguo), LIFO (origen más reciente) y FEFO (caducidad).</summary>
        private void AddLotsSection(List<JToken> lots)
        {
            Dictionary<JToken, int> fifo = new Dictionary<JToken, int>(), fefo = new Dictionary<JToken, int>();
            int n = 0;
            foreach (JToken l in lots.OrderBy(l => DateValue(l, "OriginationDate") ?? DateTime.MaxValue).ThenBy(l => UiKit.S(l, "LotNumber"))) fifo[l] = ++n;
            n = 0;
            List<JToken> byExpiry = lots.OrderBy(l => DateValue(l, "ExpirationDate") ?? DateTime.MaxValue).ThenBy(l => UiKit.S(l, "LotNumber")).ToList();
            foreach (JToken l in byExpiry) fefo[l] = ++n;

            // Vida útil: la del artículo (ShelfLifeDays de itemsV2) contra la real de cada lote (ExpirationDate − OriginationDate de inventoryItemLots)
            decimal? itemDaysDec = Dec(shelfItem, "ShelfLifeDays");
            int? itemDays = shelfItem != null && shelfItem["ShelfLifeDays"] != null && shelfItem["ShelfLifeDays"].Type != JTokenType.Null ? (int?)(int)itemDaysDec.Value : null;
            DateTime today = DateTime.UtcNow.Date;
            int same = 0, differ = 0, expired = 0, soon = 0;

            DataGridView g = UiKit.Grid(false);
            UiKit.Col(g, "FEFO", 0.5f, true); UiKit.Col(g, "FIFO", 0.5f, true); UiKit.Col(g, "LIFO", 0.5f, true); UiKit.Col(g, "Lote", 1.4f); UiKit.Col(g, "Estatus", 0.7f); UiKit.Col(g, "Grado", 0.6f);
            UiKit.Col(g, "Origen", 0.9f); UiKit.Col(g, "Caducidad", 0.9f);
            UiKit.Col(g, "Vida lote (d)", 0.8f, true); UiKit.Col(g, "Vida artículo (d)", 0.9f, true); UiKit.Col(g, "Dif. (d)", 0.6f, true); UiKit.Col(g, "Restan (d)", 0.8f, true); UiKit.Col(g, "% restante", 0.8f, true);
            UiKit.Col(g, "Mantener hasta", 0.9f); UiKit.Col(g, "Re-prueba", 0.9f); UiKit.Col(g, "Activo", 0.6f);
            foreach (JToken l in byExpiry)
            {
                DateTime? o = DateValue(l, "OriginationDate"), x = DateValue(l, "ExpirationDate");
                int? lotDays = o.HasValue && x.HasValue ? (int?)(x.Value.Date - o.Value.Date).Days : null;
                int? remain = x.HasValue ? (int?)(x.Value.Date - today).Days : null;
                int? diff = lotDays.HasValue && itemDays.HasValue ? (int?)(lotDays.Value - itemDays.Value) : null;
                int? basis = itemDays ?? lotDays;
                int? pct = remain.HasValue && basis.HasValue && basis.Value > 0 ? (int?)Math.Round(remain.Value * 100.0 / basis.Value) : null;
                if (diff.HasValue) { if (diff.Value == 0) same++; else differ++; }
                if (remain.HasValue && remain.Value < 0) expired++; else if (pct.HasValue && pct.Value < 25) soon++;
                int idx = g.Rows.Add(fefo[l], fifo[l], lots.Count - fifo[l] + 1, UiKit.S(l, "LotNumber"), V(l, "StatusCode"), V(l, "Grade"), DateU(l, "OriginationDate"), DateU(l, "ExpirationDate"),
                    lotDays.HasValue ? lotDays.Value.ToString("#,##0") : "—", itemDays.HasValue ? itemDays.Value.ToString("#,##0") : "—",
                    diff.HasValue ? (diff.Value > 0 ? "+" : string.Empty) + diff.Value : "—", remain.HasValue ? remain.Value.ToString("#,##0") : "—", pct.HasValue ? pct.Value + " %" : "—",
                    DateU(l, "HoldUntilDate"), DateU(l, "RetestDate"), V(l, "ActiveLot"));
                if (remain.HasValue && remain.Value < 0) g.Rows[idx].DefaultCellStyle.BackColor = Ui.RowDanger;       // vencido
                else if (pct.HasValue && pct.Value < 25) g.Rows[idx].DefaultCellStyle.BackColor = Ui.RowWarning;     // menos de 25 % de vida
            }
            g.Height = 34 + Math.Min(lots.Count, 12) * 28;
            AddSection("Lotes del artículo · orden de consumo FEFO / FIFO / LIFO y vida útil", new[]
            {
                new[] { "Lotes", lots.Count.ToString() }, P("Control de caducidad", shelfItem, "LotExpirationControlValue"),
                new[] { "Vida útil del artículo (ShelfLifeDays)", itemDays.HasValue ? itemDays.Value.ToString("#,##0") + " días" : "—" },
                new[] { "Lotes con la misma vida", same.ToString() }, new[] { "Lotes con vida distinta", differ.ToString() },
                new[] { "Vencidos", expired.ToString() }, new[] { "Con menos de 25 % de vida", soon.ToString() }
            }, g);
        }

        private static string Key(string sub, string loc, string lot) { return sub + "|" + loc + "|" + lot; }

        private static decimal Dec(JToken t, string key)
        {
            decimal d;
            return decimal.TryParse(UiKit.S(t, key), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out d) ? d : 0m;
        }

        private JToken SelectedStockRow()
        {
            if (gridStock == null || gridStock.IsDisposed || gridStock.SelectedRows.Count == 0) return null;
            return gridStock.SelectedRows[0].Tag as JToken;
        }

        #endregion

        #region Incrementar stock

        /// <summary>Abre el Miscellaneous Receipt del artículo; con una fila elegida llega con su subinventario, localizador y lote.</summary>
        private async Task OpenReceiptAsync(JToken row)
        {
            if (currentItem == null) return;
            JToken orgData = orgs.FirstOrDefault(o => UiKit.S(o, "OrganizationCode") == currentOrg);
            if (orgData == null) return;

            string uom = UiKit.S(stockRows.FirstOrDefault(), "PrimaryUOMCode");
            JObject mat = new JObject
            {
                ["ItemNumber"] = currentItem,
                ["ItemDescription"] = currentDescription ?? string.Empty,
                ["SupplySubinventory"] = row == null ? string.Empty : UiKit.S(row, "SubinventoryCode"),
                ["UOMCode"] = uom,
                ["PrefillLocator"] = row == null ? string.Empty : UiKit.S(row, "Locator"),
                ["PrefillLot"] = row == null ? string.Empty : UiKit.S(row, "LotNumber")
            };
            JObject wo = new JObject { ["Inventory"] = true };
            IList<KeyValuePair<JToken, JToken>> list = new List<KeyValuePair<JToken, JToken>> { new KeyValuePair<JToken, JToken>(mat, wo) };
            using (frmReceipt f = new frmReceipt(orgData, list)) f.ShowDialog(FindForm());
            await ShowItemAsync(currentItem, currentOrg); // el stock pudo cambiar
        }

        #endregion
    }
}
