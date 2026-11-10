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
    /// Conteos cíclicos: cabeceras (cycleCountDefinitions), tareas de conteo (cycleCountSequenceDetails, PATCH de CountQuantity)
    /// y envío de conteos como transacción directa (cycleCountTransactions).
    /// </summary>
    internal class CycleCountView : Panel
    {
        private class Section
        {
            public CardPanel Card;
            public FlowLayoutPanel Flow;
            public Control Below;
        }

        private readonly Button btnRefresh = UiKit.Btn("Actualizar lista", true, 140), btnNew = UiKit.Btn("Nuevo conteo", false, 130);
        private readonly TextBox txtFilter = new TextBox();
        private readonly DataGridView gridDefs = UiKit.Grid(false);
        private readonly FlowLayoutPanel detail = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        private readonly Button btnPatch = UiKit.Btn("Guardar conteos", true, 170), btnPost = UiKit.Btn("Enviar como transacción", false, 200), btnReload = UiKit.Btn("Recargar tareas", false, 150);
        private readonly CheckBox chkPending = new CheckBox { Text = "Solo pendientes de contar", AutoSize = true };
        private readonly Label lblChanges = UiKit.Lbl("Sin cambios", false, 9f, Ui.TextSecondary);
        private readonly ToolTip tip = new ToolTip();
        private readonly List<Section> sections = new List<Section>();

        private List<JToken> defs = new List<JToken>();
        private JToken current;
        private DataGridView gridSeq;
        private bool filling;
        private int version;
        private string loadedFor;

        public Action<string> Status = s => { };

        public CycleCountView()
        {
            Dock = DockStyle.Fill;
            Padding = new Padding(12, 8, 12, 8);

            CardPanel left = new CardPanel { Dock = DockStyle.Left, Width = 320 };
            Label title = UiKit.Lbl("Conteos cíclicos", true, 11f); title.Location = new Point(16, 14);
            Label lf = UiKit.Lbl("Filtrar por nombre u organización", false, 8.5f, Ui.TextSecondary); lf.Location = new Point(16, 46);
            txtFilter.SetBounds(16, 64, 288, 26);
            btnRefresh.Location = new Point(16, 102);
            btnNew.Location = new Point(164, 102);
            gridDefs.SetBounds(16, 150, 288, 400);
            gridDefs.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            UiKit.Col(gridDefs, "Conteo", 1.6f); UiKit.Col(gridDefs, "Org.", 0.9f); UiKit.Col(gridDefs, "Inicio", 1f);
            left.Controls.AddRange(new Control[] { title, lf, txtFilter, btnRefresh, btnNew, gridDefs });
            left.Resize += (s, e) => gridDefs.Height = Math.Max(80, left.Height - gridDefs.Top - 14);

            Panel gap = new Panel { Dock = DockStyle.Left, Width = 10 };
            Controls.Add(detail);
            Controls.Add(gap);
            Controls.Add(left);

            btnRefresh.Click += async (s, e) => await LoadDefinitionsAsync(true);
            btnNew.Click += async (s, e) => await NewCountAsync();
            txtFilter.TextChanged += (s, e) => FillDefinitions();
            gridDefs.CellClick += async (s, e) => { if (e.RowIndex >= 0) await ShowDefinitionAsync(gridDefs.Rows[e.RowIndex].Tag as JToken); };
            gridDefs.KeyDown += async (s, e) =>
            {
                if (e.KeyCode == Keys.Enter && gridDefs.CurrentRow != null) { e.SuppressKeyPress = true; await ShowDefinitionAsync(gridDefs.CurrentRow.Tag as JToken); }
            };
            detail.SizeChanged += (s, e) => RelayoutAll();
            btnPatch.Click += async (s, e) => await SaveAsync(false);
            btnPost.Click += async (s, e) => await SaveAsync(true);
            btnReload.Click += async (s, e) => { if (current != null) await ShowDefinitionAsync(current); };
            chkPending.CheckedChanged += (s, e) => ApplyPendingFilter();
            btnPatch.Enabled = btnPost.Enabled = false;
        }

        /// <summary>Carga las cabeceras la primera vez que se entra (o tras cambiar de instancia).</summary>
        public async Task EnsureLoadedAsync()
        {
            Instance inst = InstanceStore.Active;
            if (inst == null || loadedFor == inst.Id) return;
            await LoadDefinitionsAsync(false);
        }

        public void Reset()
        {
            loadedFor = null;
            defs = new List<JToken>();
            gridDefs.Rows.Clear();
            ClearDetail();
        }

        #region Cabeceras

        private async Task LoadDefinitionsAsync(bool force)
        {
            Instance inst = InstanceStore.Active;
            if (inst == null) { Status("No hay instancias configuradas."); return; }
            btnRefresh.Enabled = false;
            try
            {
                Status("Consultando conteos cíclicos…");
                JObject res = await FusionClient.GetAsync(EndPoints.CycleCountDefinitionsPath);
                defs = FusionClient.Items(res).OrderBy(d => UiKit.S(d, "OrganizationCode")).ThenBy(d => UiKit.S(d, "CycleCountName")).ToList();
                loadedFor = inst.Id;
                FillDefinitions();
                Status(defs.Count + " conteo(s) cíclico(s) definidos.");
            }
            catch (Exception ex) { UiKit.Error(ex.Message); }
            finally { btnRefresh.Enabled = true; }
        }

        /// <summary>Abre el alta de una cabecera; al crearla se recarga la lista y se muestra el conteo nuevo.</summary>
        private async Task NewCountAsync()
        {
            if (InstanceStore.Active == null) { UiKit.Error("No hay una instancia activa."); return; }
            string createdName, createdOrg;
            using (frmCreateCycleCount f = new frmCreateCycleCount(defs.Select(d => UiKit.S(d, "WorkdaySchedule")), current == null ? null : UiKit.S(current, "OrganizationCode")))
            {
                f.ShowDialog(FindForm());
                createdName = f.CreatedName; createdOrg = f.CreatedOrg;
            }
            if (string.IsNullOrEmpty(createdName)) return;
            await LoadDefinitionsAsync(true);
            JToken made = defs.FirstOrDefault(d => UiKit.S(d, "CycleCountName") == createdName && UiKit.S(d, "OrganizationCode") == createdOrg);
            if (made != null) await ShowDefinitionAsync(made);
        }

        private void FillDefinitions()
        {
            string f = txtFilter.Text.Trim();
            filling = true;
            gridDefs.Rows.Clear();
            foreach (JToken d in defs.Where(d => f.Length == 0 || UiKit.S(d, "CycleCountName").IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0
                                                 || UiKit.S(d, "OrganizationCode").IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0))
                gridDefs.Rows[gridDefs.Rows.Add(UiKit.S(d, "CycleCountName"), UiKit.S(d, "OrganizationCode"), DateU(d, "StartDate"))].Tag = d;
            filling = false;
            gridDefs.ClearSelection();
        }

        #endregion

        #region Detalle y tareas de conteo

        private void ClearDetail()
        {
            version++;
            current = null;
            gridSeq = null;
            sections.Clear();
            detail.Controls.Clear();
            btnPatch.Enabled = btnPost.Enabled = false;
        }

        private async Task ShowDefinitionAsync(JToken def)
        {
            if (def == null) return;
            int ver = ++version;
            current = def;
            string name = UiKit.S(def, "CycleCountName"), org = UiKit.S(def, "OrganizationCode");
            sections.Clear();
            detail.Controls.Clear();
            gridSeq = null;
            btnPatch.Enabled = btnPost.Enabled = false;
            detail.Controls.Add(UiKit.Lbl("Consultando las tareas de " + name + " (" + org + ")…", false, 9f, Ui.TextSecondary));
            try
            {
                JObject res;
                try { res = await FusionClient.GetAsync(EndPoints.CycleSequencesPath(name, org, true)); }
                catch (FusionException) { res = await FusionClient.GetAsync(EndPoints.CycleSequencesPath(name, org, false)); } // por si algún campo no existe en esta versión
                if (ver != version) return;
                BuildDetail(def, FusionClient.Items(res).ToList());
                Status(name + " · " + org + ": " + FusionClient.Items(res).Count + " tarea(s) de conteo.");
            }
            catch (Exception ex)
            {
                if (ver != version) return;
                detail.Controls.Clear();
                detail.Controls.Add(UiKit.Lbl("No se pudieron consultar las tareas de conteo.", false, 9f, Ui.Danger));
                UiKit.Error(ex.Message);
            }
        }

        private void BuildDetail(JToken def, List<JToken> seqs)
        {
            detail.SuspendLayout();
            detail.Controls.Clear();
            sections.Clear();

            CardPanel head = new CardPanel { Height = 112, Margin = new Padding(0, 0, 0, 8), Tag = "wide" };
            Label num = UiKit.Lbl(UiKit.S(def, "CycleCountName"), true, 13f, Ui.Accent); num.Location = new Point(14, 8);
            Label desc = UiKit.Lbl(UiKit.S(def, "Description") + "   ·   " + UiKit.S(def, "OrganizationCode") + " " + UiKit.S(def, "OrganizationName"), false, 9.5f); desc.Location = new Point(14, 38);
            chkPending.Location = new Point(14, 76); lblChanges.Location = new Point(220, 78);
            head.Controls.AddRange(new Control[] { num, desc, chkPending, lblChanges, btnReload, btnPost, btnPatch });
            head.Resize += (s, e) =>
            {
                btnPatch.Location = new Point(head.Width - btnPatch.Width - 14, 8);
                btnPost.Location = new Point(btnPatch.Left - btnPost.Width - 8, 8);
                btnReload.Location = new Point(btnPost.Left - btnReload.Width - 8, 8);
            };
            detail.Controls.Add(head);

            AddSection("Configuración del conteo", new[]
            {
                P("Inicio", def, "StartDate"), P("Fin", def, "EndDate"), P("Programación automática", def, "AutomaticallySchedule"), P("Frecuencia", def, "Frequency"),
                P("Próxima programación", def, "NextScheduledDate"), P("Calendario laboral", def, "WorkdaySchedule"), P("Secuencia inicial", def, "StartingCountSequence"),
                P("Días máx. antes de tardío", def, "MaximumDaysBeforeLate"), P("Recuentos máximos", def, "MaximumRecounts"),
                P("Requiere aprobación", def, "ApprovalRequired"), P("Tipo de aprobación", def, "ApprovalType"),
                P("Tolerancia + (%)", def, "PositiveTolerancePercentage"), P("Tolerancia − (%)", def, "NegativeTolerancePercentage"),
                P("Tolerancia ajuste +", def, "PositiveAdjustmentTolerance"), P("Tolerancia ajuste −", def, "NegativeAdjustmentTolerance"),
                P("Conteos manuales", def, "ManualCountsAllowed"), P("Contar cantidad cero", def, "CountZeroQuantity"), P("Mostrar cantidad sugerida", def, "DisplaySuggestedQuantity"),
                P("Series permitidas", def, "SerializedItemsAllowed"), P("Subinventarios incluidos", def, "IncludedSubinventoriesCount"), P("Conteo por categoría", def, "CountByCategoryFlag"),
                P("Conteos pendientes", def, "PendingCounts"), P("Creado por", def, "CreatedBy"), P("Últ. actualización", def, "LastUpdateDate")
            }, null);

            BuildSequences(seqs);

            ThemeManager.ApplyTo(detail);
            detail.ResumeLayout(true);
            RelayoutAll();
            UpdateChanges();
        }

        private void BuildSequences(List<JToken> seqs)
        {
            DataGridView g = UiKit.Grid(false);
            g.ReadOnly = false;
            g.EditMode = DataGridViewEditMode.EditOnEnter;
            string[] heads = { "Sec.", "Artículo", "Descripción", "Subinv.", "Localizador", "Lote", "Serie", "UOM", "Sugerida", "Contada", "Ajuste", "Estatus", "Vence", "Contado por" };
            foreach (string h in heads)
            {
                DataGridViewColumn c = UiKit.Col(g, h, h == "Descripción" ? 2f : h == "Sec." || h == "UOM" ? 0.6f : 1f, h == "Sugerida" || h == "Contada" || h == "Ajuste");
                c.ReadOnly = h != "Contada";
                if (h == "Contada") c.Name = "count";
            }
            foreach (JToken s in seqs)
            {
                string seqNo = UiKit.S(s, "CountSequenceWithPrefix").Length > 0 ? UiKit.S(s, "CountSequenceWithPrefix") : UiKit.S(s, "CountSequence");
                decimal? counted = Num(s, "CountQuantity");
                int idx = g.Rows.Add(seqNo, UiKit.S(s, "ItemNumber"), UiKit.S(s, "ItemDescription"), UiKit.S(s, "Subinventory"), Dash(UiKit.S(s, "Locator")), Dash(UiKit.S(s, "LotNumber")),
                    Dash(UiKit.S(s, "SerialNumber")), Dash(UiKit.S(s, "CountUOMCode")), Fmt(Num(s, "PrimarySuggestedQuantity")), counted.HasValue ? Fmt(counted) : string.Empty,
                    Fmt(Num(s, "PrimaryAdjustmentQuantity")), Dash(UiKit.S(s, "CountSequenceStatus")), DateU(s, "CountDueDate"), Dash(UiKit.S(s, "CountedBy")));
                g.Rows[idx].Tag = s;
            }
            g.CellEndEdit += (s, e) => OnCountEdited(g, e.RowIndex);
            g.Height = 34 + Math.Min(Math.Max(1, seqs.Count), 18) * 28;
            gridSeq = g;

            int pending = seqs.Count(s => !Num(s, "CountQuantity").HasValue);
            Control below = seqs.Count == 0 ? (Control)UiKit.Lbl("Este conteo no tiene tareas de conteo abiertas.", false, 9f, Ui.Warning) : g;
            AddSection("Tareas de conteo · captura la cantidad contada", new[]
            {
                new[] { "Tareas", seqs.Count.ToString() }, new[] { "Pendientes de contar", pending.ToString() }, new[] { "Contadas", (seqs.Count - pending).ToString() }
            }, below);
            ApplyPendingFilter();
        }

        private void ApplyPendingFilter()
        {
            if (gridSeq == null) return;
            gridSeq.CurrentCell = null;
            foreach (DataGridViewRow r in gridSeq.Rows)
                r.Visible = !chkPending.Checked || !Num((JToken)r.Tag, "CountQuantity").HasValue;
            RelayoutAll();
        }

        private void OnCountEdited(DataGridView g, int row)
        {
            if (row < 0) return;
            DataGridViewCell cell = g.Rows[row].Cells["count"];
            string text = Convert.ToString(cell.Value);
            decimal d;
            if (text.Trim().Length > 0 && (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out d) || d < 0))
            {
                UiKit.Error("La cantidad contada debe ser un número mayor o igual a cero.");
                decimal? original = Num((JToken)g.Rows[row].Tag, "CountQuantity");
                cell.Value = original.HasValue ? Fmt(original) : string.Empty;
            }
            cell.Style.BackColor = IsChanged(g.Rows[row]) ? Ui.RowWarning : Color.Empty;
            UpdateChanges();
        }

        private static bool IsChanged(DataGridViewRow r)
        {
            decimal? original = Num((JToken)r.Tag, "CountQuantity");
            string text = Convert.ToString(r.Cells["count"].Value);
            if (text == null || text.Trim().Length == 0) return false;
            decimal d;
            if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out d)) return false;
            return !original.HasValue || original.Value != d;
        }

        private List<DataGridViewRow> ChangedRows()
        {
            return gridSeq == null ? new List<DataGridViewRow>() : gridSeq.Rows.Cast<DataGridViewRow>().Where(IsChanged).ToList();
        }

        private void UpdateChanges()
        {
            int n = ChangedRows().Count;
            lblChanges.Text = n == 0 ? "Sin cambios" : n + " conteo(s) sin guardar";
            btnPatch.Enabled = btnPost.Enabled = n > 0;
        }

        #endregion

        #region Guardar

        /// <summary>
        /// PATCH: registra CountQuantity en cada tarea (el conteo sigue su flujo de aprobación).
        /// POST: envía los mismos conteos como transacción directa a cycleCountTransactions.
        /// </summary>
        private async Task SaveAsync(bool asTransaction)
        {
            List<DataGridViewRow> rows = ChangedRows();
            if (rows.Count == 0) return;
            string how = asTransaction ? "una transacción directa (POST /cycleCountTransactions)" : "la tarea de conteo (PATCH /cycleCountSequenceDetails)";
            if (MessageBox.Show("Se registrarán " + rows.Count + " conteo(s) en \"" + InstanceStore.Active.Name + "\" mediante " + how + ".\n\n¿Continuar?",
                    "Confirmar conteo", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            btnPatch.Enabled = btnPost.Enabled = false;
            string today = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            int ok = 0;
            try
            {
                if (asTransaction)
                {
                    JArray lines = new JArray();
                    foreach (DataGridViewRow r in rows) lines.Add(TransactionLine((JToken)r.Tag, ParseCount(r), today));
                    JObject res = await FusionClient.PostAsync("/cycleCountTransactions", new JObject { ["countLines"] = lines });
                    string code = UiKit.S(res, "ErrorCode");
                    if (code.Length > 0)
                    {
                        ConsoleLog.Log(LogKind.Error, "Conteo rechazado: " + code + " · " + UiKit.S(res, "ErrorExplanation"));
                        UiKit.Error("Oracle rechazó el conteo: " + code + " " + UiKit.S(res, "ErrorExplanation"));
                        UpdateChanges();
                        return;
                    }
                    ok = rows.Count;
                    ConsoleLog.Log(LogKind.Ok, "Transacción de conteo aceptada (" + UiKit.S(res, "ReturnStatus") + ") · " + ok + " línea(s).");
                }
                else
                {
                    foreach (DataGridViewRow r in rows)
                    {
                        JToken seq = (JToken)r.Tag;
                        string path = SelfPath(seq);
                        if (path == null) { ConsoleLog.Log(LogKind.Error, "La tarea " + UiKit.S(seq, "ItemNumber") + " no trae su enlace; recarga las tareas."); continue; }
                        JObject body = new JObject { ["CountQuantity"] = ParseCount(r), ["CountDate"] = today };
                        if (UiKit.S(seq, "CountUOMCode").Length > 0) body["CountUOMCode"] = UiKit.S(seq, "CountUOMCode");
                        try
                        {
                            await FusionClient.PatchAsync(path, body);
                            ok++;
                            ConsoleLog.Log(LogKind.Ok, UiKit.S(seq, "ItemNumber") + " · " + UiKit.S(seq, "Subinventory") + " · contado " + Fmt(ParseCount(r)));
                        }
                        catch (Exception ex) { ConsoleLog.Log(LogKind.Error, UiKit.S(seq, "ItemNumber") + " · no se guardó: " + ex.Message); }
                    }
                }
            }
            catch (Exception ex) { UiKit.Error(ex.Message); UpdateChanges(); return; }

            if (ok < rows.Count) UiKit.Error(ok + " de " + rows.Count + " conteo(s) se registraron. Revisa la consola para ver los que fallaron.");
            else UiKit.Info(ok + " conteo(s) registrados.");
            if (current != null) await ShowDefinitionAsync(current); // refleja estatus y ajustes
        }

        private static decimal ParseCount(DataGridViewRow r)
        {
            decimal d;
            decimal.TryParse(Convert.ToString(r.Cells["count"].Value), NumberStyles.Number, CultureInfo.CurrentCulture, out d);
            return d;
        }

        private static JObject TransactionLine(JToken s, decimal qty, string today)
        {
            JObject line = new JObject
            {
                ["CountName"] = UiKit.S(s, "CycleCountName"),
                ["CycleCountHeaderId"] = UiKit.S(s, "CycleCountHeaderId"),
                ["CycleCountEntryId"] = UiKit.S(s, "CycleCountEntryId"),
                ["OrganizationCode"] = UiKit.S(s, "OrganizationCode"),
                ["Item"] = UiKit.S(s, "ItemNumber"),
                ["Subinventory"] = UiKit.S(s, "Subinventory"),
                ["CountQuantity"] = qty,
                ["CountDate"] = today
            };
            Action<string, string> opt = (key, value) => { if (!string.IsNullOrEmpty(value)) line[key] = value; };
            opt("CountSequence", UiKit.S(s, "CountSequence"));
            opt("InventoryItemId", UiKit.S(s, "InventoryItemId"));
            opt("OrganizationId", UiKit.S(s, "OrganizationId"));
            opt("LocatorId", UiKit.S(s, "LocatorId"));
            opt("LocatorName", UiKit.S(s, "Locator"));
            opt("Lot", UiKit.S(s, "LotNumber"));
            opt("SerialNumber", UiKit.S(s, "SerialNumber"));
            opt("Revision", UiKit.S(s, "Revision"));
            opt("CountUOM", UiKit.S(s, "CountUOMCode"));
            opt("CountUnitOfMeasure", UiKit.S(s, "CountUOM"));
            return line;
        }

        /// <summary>Ruta del registro (/cycleCountSequenceDetails/{id}) tomada del enlace "self" que devuelve Oracle.</summary>
        private static string SelfPath(JToken seq)
        {
            JArray links = seq["links"] as JArray;
            if (links == null) return null;
            foreach (JToken l in links)
            {
                string rel = UiKit.S(l, "rel"), href = UiKit.S(l, "href");
                int at = href.IndexOf("/cycleCountSequenceDetails/", StringComparison.Ordinal);
                if ((rel == "self" || rel == "canonical") && at >= 0) return href.Substring(at);
            }
            return null;
        }

        #endregion

        #region Presentación

        private static string[] P(string caption, JToken t, string key) { return new[] { caption, V(t, key) }; }

        private static string Dash(string s) { return string.IsNullOrEmpty(s) ? "—" : s; }

        private static decimal? Num(JToken t, string key)
        {
            JToken v = t == null ? null : t[key];
            if (v == null || v.Type == JTokenType.Null) return null;
            decimal d;
            return decimal.TryParse(UiKit.S(t, key), NumberStyles.Any, CultureInfo.InvariantCulture, out d) ? d : (decimal?)null;
        }

        private static string Fmt(decimal? d) { return d.HasValue ? StockService.Fmt(d.Value) : "—"; }

        private static string DateU(JToken t, string key)
        {
            JToken v = t == null ? null : t[key];
            if (v == null || v.Type == JTokenType.Null) return "—";
            if (v.Type == JTokenType.Date) return v.Value<DateTime>().ToUniversalTime().ToString("dd/MM/yyyy");
            string s = (string)v;
            return string.IsNullOrEmpty(s) ? "—" : s;
        }

        private static string V(JToken t, string key)
        {
            JToken v = t == null ? null : t[key];
            if (v == null || v.Type == JTokenType.Null) return "—";
            switch (v.Type)
            {
                case JTokenType.Boolean: return (bool)v ? "Sí" : "No";
                case JTokenType.Integer: return StockService.Fmt(v.Value<decimal>());
                case JTokenType.Float: return StockService.Fmt(v.Value<decimal>());
                case JTokenType.Date: return v.Value<DateTime>().ToString("dd/MM/yyyy HH:mm");
                default: string s = (string)v; return string.IsNullOrEmpty(s) ? "—" : s;
            }
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
                        int visible = g.Rows.Cast<DataGridViewRow>().Count(r => r.Visible);
                        g.Height = 34 + Math.Min(Math.Max(1, visible), 18) * 28;
                    }
                    bottom = s.Below.Bottom;
                }
                s.Card.Height = bottom + 12;
            }
        }

        #endregion
    }
}
