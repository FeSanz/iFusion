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
    /// <summary>Alta de una cabecera de conteo cíclico: POST /cycleCountDefinitions (con subinventarios, clases ABC y artículos).</summary>
    internal class frmCreateCycleCount : Form
    {
        private class Choice
        {
            public readonly string Text, Value;
            public Choice(string text, string value) { Text = text; Value = value; }
            public override string ToString() { return Text; }
        }

        private readonly ComboBox cmbOrg = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly TextBox txtName = new TextBox(), txtDesc = new TextBox(), txtStart = new TextBox(), txtLate = new TextBox(), txtSeq = new TextBox(), txtPrefix = new TextBox(),
                                 txtAbcGroup = new TextBox(), txtSync = new TextBox();
        private readonly ComboBox cmbSchedule = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown },
                                  cmbApprovalType = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown },
                                  cmbSerialOpt = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown },
                                  cmbSerialAdj = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown };
        private readonly ComboBox cmbAuto = YesNo(false), cmbApproval = YesNo(true), cmbManual = YesNo(true), cmbZero = YesNo(false), cmbSuggested = YesNo(true),
                                  cmbSerials = YesNo(true), cmbRecord = YesNo(false), cmbDiscrepancy = YesNo(false);
        private readonly DataGridView gridSubs = UiKit.Grid(false), gridClasses = UiKit.Grid(false), gridItems = UiKit.Grid(false);
        private readonly Label lblStatus = UiKit.Lbl("Elige la organización y marca los subinventarios a contar.", false, 9f, Ui.TextSecondary);
        private readonly Button btnCreate = UiKit.Btn("Crear conteo", true, 150), btnClose = UiKit.Btn("Cerrar");
        private List<JToken> orgs = new List<JToken>();
        private string subsFor = string.Empty;
        private int abcClasses = -1; // clases ABC definidas en la organización (-1 = no se pudo consultar)

        public string CreatedName { get; private set; }
        public string CreatedOrg { get; private set; }

        private static ComboBox YesNo(bool yes)
        {
            ComboBox c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            c.Items.Add(new Choice("Sí", "Yes")); c.Items.Add(new Choice("No", "No"));
            c.SelectedIndex = yes ? 0 : 1;
            return c;
        }

        public frmCreateCycleCount(IEnumerable<string> knownSchedules, string preferredOrg)
        {
            Text = "Nuevo conteo cíclico";
            ClientSize = new Size(820, 700);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            Font = new Font(Ui.FontFamily, 9f);

            Panel body = new Panel { Dock = DockStyle.Fill };
            Action<string, Control, int, int, int> put = (caption, box, x, y, w) =>
            {
                Label l = UiKit.Lbl(caption, false, 8.5f, Ui.TextSecondary);
                l.Location = new Point(x, y);
                box.SetBounds(x, y + 20, w, 26);
                body.Controls.Add(l); body.Controls.Add(box);
            };
            put("Organización", cmbOrg, 16, 10, 360);
            put("Nombre del conteo", txtName, 392, 10, 200);
            put("Inicio (aaaa-mm-dd)", txtStart, 608, 10, 190);
            put("Descripción", txtDesc, 16, 66, 576);
            put("Días máx. antes de tardío", txtLate, 608, 66, 190);
            put("Calendario laboral (escríbelo como en Oracle)", cmbSchedule, 16, 122, 300);
            put("Secuencia inicial", txtSeq, 332, 122, 120);
            put("Prefijo de conteo manual", txtPrefix, 468, 122, 124);
            put("Programar automáticamente", cmbAuto, 608, 122, 190);
            put("Requiere aprobación", cmbApproval, 16, 178, 180);
            put("Tipo de aprobación", cmbApprovalType, 212, 178, 180);
            put("Conteos manuales", cmbManual, 408, 178, 184);
            put("Contar cantidad cero", cmbZero, 608, 178, 190);
            put("Mostrar cantidad sugerida", cmbSuggested, 16, 234, 180);
            put("Series permitidas", cmbSerials, 212, 234, 180);
            put("Opción de conteo de series", cmbSerialOpt, 408, 234, 184);
            put("Ajuste de series", cmbSerialAdj, 608, 234, 190);
            put("Registrar series contadas", cmbRecord, 16, 290, 180);
            put("Discrepancia de series", cmbDiscrepancy, 212, 290, 180);
            put("Grupo de asignación ABC (opcional)", txtAbcGroup, 408, 290, 184);
            put("Modo de sincronización (opcional)", txtSync, 608, 290, 190);

            txtStart.Text = DateTime.Today.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); // Oracle exige una fecha posterior a hoy
            txtLate.Text = "30"; txtSeq.Text = "1"; txtPrefix.Text = "MC";
            foreach (string s in knownSchedules.Where(x => !string.IsNullOrEmpty(x)).Distinct()) cmbSchedule.Items.Add(s);
            if (cmbSchedule.Items.Count > 0) cmbSchedule.SelectedIndex = 0;
            cmbApprovalType.Items.Add("Always"); cmbApprovalType.Text = "Always";
            cmbSerialOpt.Items.Add("One"); cmbSerialOpt.Text = "One";
            cmbSerialAdj.Items.Add("Adjust if possible"); cmbSerialAdj.Text = "Adjust if possible";

            Label ls = UiKit.Lbl("Subinventarios a contar", false, 8.5f, Ui.TextSecondary); ls.Location = new Point(16, 350);
            gridSubs.SetBounds(16, 370, 380, 250);
            gridSubs.ReadOnly = false;
            gridSubs.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "", FillWeight = 22, MinimumWidth = 30, Resizable = DataGridViewTriState.False });
            UiKit.Col(gridSubs, "Subinventario", 1.2f).ReadOnly = true; UiKit.Col(gridSubs, "Descripción", 2f).ReadOnly = true;
            UiKit.ThemeGridControls(gridSubs);
            gridSubs.CurrentCellDirtyStateChanged += (s, e) => { if (gridSubs.IsCurrentCellDirty) gridSubs.CommitEdit(DataGridViewDataErrorContexts.Commit); };

            Label lc = UiKit.Lbl("Clases ABC (opcional)", false, 8.5f, Ui.TextSecondary); lc.Location = new Point(412, 350);
            gridClasses.SetBounds(412, 370, 386, 110);
            gridClasses.ReadOnly = false; gridClasses.AllowUserToAddRows = true; gridClasses.AllowUserToDeleteRows = true;
            UiKit.Col(gridClasses, "Clase ABC (ej. Class A)", 1.6f); UiKit.Col(gridClasses, "Conteos por año", 1f, true);
            Label li = UiKit.Lbl("Artículos a contar (opcional)", false, 8.5f, Ui.TextSecondary); li.Location = new Point(412, 490);
            gridItems.SetBounds(412, 510, 386, 110);
            gridItems.ReadOnly = false; gridItems.AllowUserToAddRows = true; gridItems.AllowUserToDeleteRows = true;
            UiKit.Col(gridItems, "Artículo", 1.5f); UiKit.Col(gridItems, "Clase ABC", 1f); UiKit.Col(gridItems, "Incluir (Yes/No)", 0.9f);
            body.Controls.AddRange(new Control[] { ls, gridSubs, lc, gridClasses, li, gridItems });

            Panel bar = new Panel { Dock = DockStyle.Bottom, Height = 52 };
            lblStatus.Location = new Point(16, 18);
            bar.Resize += (s, e) =>
            {
                btnClose.Location = new Point(bar.Width - btnClose.Width - 16, 11);
                btnCreate.Location = new Point(btnClose.Left - btnCreate.Width - 8, 11);
            };
            bar.Controls.AddRange(new Control[] { lblStatus, btnCreate, btnClose });
            Controls.Add(body); Controls.Add(bar);
            ThemeManager.Apply(this);

            btnClose.Click += (s, e) => Close();
            btnCreate.Click += async (s, e) => await CreateAsync();
            cmbOrg.SelectedIndexChanged += async (s, e) => await LoadSubinventoriesAsync();
            cmbApproval.SelectedIndexChanged += (s, e) => cmbApprovalType.Enabled = ((Choice)cmbApproval.SelectedItem).Value == "Yes";
            cmbSerials.SelectedIndexChanged += (s, e) =>
                cmbSerialOpt.Enabled = cmbSerialAdj.Enabled = cmbRecord.Enabled = cmbDiscrepancy.Enabled = ((Choice)cmbSerials.SelectedItem).Value == "Yes";
            Shown += async (s, e) => await LoadOrgsAsync(preferredOrg);
        }

        private string OrgCode { get { Choice c = cmbOrg.SelectedItem as Choice; return c == null ? string.Empty : c.Value; } }

        private async System.Threading.Tasks.Task LoadOrgsAsync(string preferred)
        {
            try
            {
                lblStatus.Text = "Cargando organizaciones…";
                JObject res = await FusionClient.GetAsync(EndPoints.InventoryOrganizations);
                orgs = FusionClient.Items(res).Where(o => UiKit.S(o, "InventoryFlag") != "False").OrderBy(o => UiKit.S(o, "OrganizationCode")).ToList();
                foreach (JToken o in orgs) cmbOrg.Items.Add(new Choice(UiKit.S(o, "OrganizationCode") + " · " + UiKit.S(o, "OrganizationName"), UiKit.S(o, "OrganizationCode")));
                Choice pick = cmbOrg.Items.Cast<Choice>().FirstOrDefault(c => c.Value == preferred);
                if (pick != null) cmbOrg.SelectedItem = pick;
                else if (cmbOrg.Items.Count > 0) cmbOrg.SelectedIndex = 0;
            }
            catch (Exception ex) { UiKit.Error(ex.Message); }
        }

        private async System.Threading.Tasks.Task LoadSubinventoriesAsync()
        {
            string code = OrgCode;
            JToken org = orgs.FirstOrDefault(o => UiKit.S(o, "OrganizationCode") == code);
            if (org == null || subsFor == code) return;
            subsFor = code;
            gridSubs.Rows.Clear();
            lblStatus.Text = "Consultando subinventarios de " + code + "…";
            try
            {
                JObject res = await FusionClient.GetAsync("/subinventories?limit=500&onlyData=true&q=OrganizationId=" + Uri.EscapeDataString(UiKit.S(org, "OrganizationId")));
                if (subsFor != code) return;
                List<JToken> subs = FusionClient.Items(res).OrderBy(s => UiKit.S(s, "SecondaryInventoryName")).ToList();
                foreach (JToken s in subs) gridSubs.Rows.Add(false, UiKit.S(s, "SecondaryInventoryName"), UiKit.S(s, "Description"));
                lblStatus.Text = subs.Count + " subinventario(s) en " + code + ". Marca los que se contarán.";
                // Las clases ABC y los artículos solo se aceptan con clases ABC definidas en la organización
                abcClasses = -1;
                try
                {
                    JObject abc = await FusionClient.GetAsync("/abcClasses?limit=100&onlyData=true&q=OrganizationCode='" + code.Replace("'", "''") + "'");
                    if (subsFor == code)
                    {
                        abcClasses = FusionClient.Items(abc).Count;
                        if (abcClasses == 0) lblStatus.Text += " " + code + " no tiene clases ABC: no se pueden agregar clases ni artículos.";
                    }
                }
                catch (Exception) { }
            }
            catch (Exception ex) { subsFor = string.Empty; UiKit.Error(ex.Message); }
        }

        private static string Val(ComboBox c)
        {
            Choice ch = c.SelectedItem as Choice;
            return ch != null ? ch.Value : c.Text.Trim();
        }

        private async System.Threading.Tasks.Task CreateAsync()
        {
            string name = txtName.Text.Trim(), org = OrgCode;
            int late, seq;
            DateTime start;
            if (org.Length == 0) { UiKit.Error("Elige una organización."); return; }
            if (name.Length == 0) { UiKit.Error("Captura el nombre del conteo."); return; }
            if (name.Length > 30) { UiKit.Error("El nombre admite hasta 30 caracteres."); return; }
            if (!DateTime.TryParseExact(txtStart.Text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out start)) { UiKit.Error("La fecha de inicio debe tener el formato aaaa-mm-dd."); return; }
            if (start.Date <= DateTime.Today) { UiKit.Error("Oracle exige que la fecha de inicio sea posterior a hoy (desde " + DateTime.Today.AddDays(1).ToString("yyyy-MM-dd") + ")."); return; }
            if (!int.TryParse(txtLate.Text.Trim(), out late) || late < 0) { UiKit.Error("Los días máximos antes de tardío deben ser un número entero."); return; }
            if (!int.TryParse(txtSeq.Text.Trim(), out seq) || seq < 1) { UiKit.Error("La secuencia inicial debe ser un entero mayor a cero."); return; }
            if (cmbSchedule.Text.Trim().Length == 0) { UiKit.Error("Captura el calendario laboral (por ejemplo el de otro conteo de la misma organización)."); return; }
            List<string> subs = gridSubs.Rows.Cast<DataGridViewRow>().Where(r => r.Cells[0].Value is bool && (bool)r.Cells[0].Value).Select(r => Convert.ToString(r.Cells[1].Value)).ToList();
            if (subs.Count == 0) { UiKit.Error("Marca al menos un subinventario."); return; }

            JObject payload = new JObject
            {
                ["CycleCountName"] = name,
                ["Description"] = txtDesc.Text.Trim().Length > 0 ? txtDesc.Text.Trim() : name,
                ["OrganizationCode"] = org,
                ["AutomaticallySchedule"] = Val(cmbAuto),
                ["WorkdaySchedule"] = cmbSchedule.Text.Trim(),
                ["ManualCountsAllowed"] = Val(cmbManual),
                ["ApprovalRequired"] = Val(cmbApproval),
                ["MaximumDaysBeforeLate"] = late,
                ["StartDate"] = start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["CountZeroQuantity"] = Val(cmbZero),
                ["DisplaySuggestedQuantity"] = Val(cmbSuggested),
                ["SerializedItemsAllowed"] = Val(cmbSerials),
                ["StartingCountSequence"] = seq
            };
            Action<string, string> opt = (k, v) => { if (!string.IsNullOrWhiteSpace(v)) payload[k] = v.Trim(); };
            if (Val(cmbApproval) == "Yes") opt("ApprovalType", cmbApprovalType.Text);
            if (Val(cmbManual) == "Yes") opt("ManualCountSequencePrefix", txtPrefix.Text);
            if (Val(cmbSerials) == "Yes")
            {
                opt("SerialCountOption", cmbSerialOpt.Text); opt("SerialNumberAdjustment", cmbSerialAdj.Text);
                payload["RecordCountSerials"] = Val(cmbRecord); payload["SerialDiscrepancyAllowed"] = Val(cmbDiscrepancy);
            }
            opt("AbcAssigmentGroup", txtAbcGroup.Text); // (sic) así se llama el atributo en Oracle
            opt("SynchronizationMode", txtSync.Text);

            JArray subArr = new JArray();
            foreach (string s in subs) subArr.Add(new JObject { ["Subinventory"] = s, ["IncludeInCount"] = "Yes" });
            payload["countSubinventories"] = subArr;

            bool wantsAbc = gridClasses.Rows.Cast<DataGridViewRow>().Any(r => !r.IsNewRow && Convert.ToString(r.Cells[0].Value).Trim().Length > 0)
                            || gridItems.Rows.Cast<DataGridViewRow>().Any(r => !r.IsNewRow && Convert.ToString(r.Cells[0].Value).Trim().Length > 0);
            if (wantsAbc && abcClasses == 0)
            {
                UiKit.Error("La organización " + org + " no tiene clases ABC definidas, y Oracle exige una clase ABC válida para cada clase y cada artículo. Quita las filas de clases y artículos, o crea las clases ABC en Oracle.");
                return;
            }
            JArray classes = new JArray();
            foreach (DataGridViewRow r in gridClasses.Rows)
            {
                if (r.IsNewRow) continue;
                string abc = Convert.ToString(r.Cells[0].Value).Trim();
                int perYear;
                if (abc.Length == 0) continue;
                if (!int.TryParse(Convert.ToString(r.Cells[1].Value), out perYear) || perYear < 1) { UiKit.Error("La clase " + abc + " necesita un número de conteos por año mayor a cero."); return; }
                classes.Add(new JObject { ["AbcClass"] = abc, ["CountsPerYear"] = perYear });
            }
            if (classes.Count > 0) payload["countClasses"] = classes;

            JArray items = new JArray();
            foreach (DataGridViewRow r in gridItems.Rows)
            {
                if (r.IsNewRow) continue;
                string item = Convert.ToString(r.Cells[0].Value).Trim();
                if (item.Length == 0) continue;
                JObject line = new JObject { ["ItemNumber"] = item, ["IncludeInSchedule"] = Convert.ToString(r.Cells[2].Value).Trim().Length > 0 ? Convert.ToString(r.Cells[2].Value).Trim() : "Yes" };
                string cls = Convert.ToString(r.Cells[1].Value).Trim();
                if (cls.Length == 0) { UiKit.Error("El artículo " + item + " necesita una clase ABC válida de la organización (columna \"Clase ABC\")."); return; }
                line["AbcClass"] = cls;
                items.Add(line);
            }
            if (items.Count > 0) payload["countItems"] = items;

            if (MessageBox.Show("Se creará el conteo cíclico \"" + name + "\" en \"" + InstanceStore.Active.Name + "\":\n\nOrganización: " + org + "\nInicio: " + payload["StartDate"] +
                    "\nSubinventarios: " + string.Join(", ", subs) + (classes.Count > 0 ? "\nClases ABC: " + classes.Count : string.Empty) + (items.Count > 0 ? "\nArtículos: " + items.Count : string.Empty) +
                    "\n\n¿Continuar?", "Confirmar creación", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            btnCreate.Enabled = false; // evita duplicar el conteo con un doble clic
            lblStatus.Text = "Creando el conteo…";
            try
            {
                JObject res = await FusionClient.PostAsync("/cycleCountDefinitions", payload);
                CreatedName = name; CreatedOrg = org;
                string text = "Conteo " + name + " creado (ID " + UiKit.S(res, "CycleCountHeaderId") + ") en " + org + ".";
                ConsoleLog.Log(LogKind.Ok, text);
                lblStatus.Text = text;
                UiKit.Info(text);
            }
            catch (Exception ex)
            {
                btnCreate.Enabled = true;
                lblStatus.Text = "No se creó el conteo.";
                UiKit.Error(ex.Message);
            }
        }
    }
}
