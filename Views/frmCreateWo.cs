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
    /// <summary>Alta de órdenes de trabajo discretas directo a Liberada: POST /workOrders.</summary>
    internal class frmCreateWo : Form
    {
        private class Choice
        {
            public readonly string Value, Text;
            public Choice(string value, string text) { Value = value; Text = text; }
            public override string ToString() { return Text; }
        }

        private readonly ComboBox cmbOrg = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList },
                                  cmbFound = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList },
                                  cmbDef = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly TextBox txtItem = new TextBox(), txtQty = new TextBox(), txtDate = new TextBox();
        private readonly CheckBox chkFg = new CheckBox { Text = "Solo Finished Good", Checked = true, AutoSize = true },
                                  chkExplode = new CheckBox { Text = "Explosionar la lista de materiales (ExplosionFlag)", Checked = true, AutoSize = true };
        private readonly Button btnFind = UiKit.Btn("Buscar", false, 90), btnCreate = UiKit.Btn("Crear OT liberada", true, 170), btnClose = UiKit.Btn("Cerrar");
        private readonly Label lblStatus = UiKit.Lbl("Elige organización y artículo.", false, 9f, Ui.TextSecondary);
        private readonly ConsolePanel console = new ConsolePanel { Dock = DockStyle.Bottom, Height = 170, Padding = new Padding(12, 0, 12, 6) };
        private string defsFor = string.Empty;

        /// <summary>Número de la última OT creada (para que la ventana principal la muestre).</summary>
        public string CreatedNumber { get; private set; }

        public frmCreateWo(IEnumerable<JToken> manufacturingOrgs, string selectedOrgCode)
        {
            Text = "Nueva orden de trabajo · directo a Liberada";
            ClientSize = new Size(648, 560);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            Font = new Font(Ui.FontFamily, 9f);

            foreach (JToken o in manufacturingOrgs.OrderBy(o => UiKit.S(o, "OrganizationCode")))
            {
                Choice c = new Choice(UiKit.S(o, "OrganizationCode"), UiKit.S(o, "OrganizationCode") + " · " + UiKit.S(o, "OrganizationName"));
                cmbOrg.Items.Add(c);
                if (c.Value == selectedOrgCode) cmbOrg.SelectedItem = c;
            }
            if (cmbOrg.SelectedItem == null && cmbOrg.Items.Count > 0) cmbOrg.SelectedIndex = 0;

            Panel body = new Panel { Dock = DockStyle.Fill };
            Action<string, Control, int, int, int> put = (caption, box, x, y, w) =>
            {
                Label l = UiKit.Lbl(caption, false, 8.5f, Ui.TextSecondary);
                l.Location = new Point(x, y);
                box.SetBounds(x, y + 20, w, 26);
                body.Controls.Add(l); body.Controls.Add(box);
            };
            put("Organización (solo manufactura)", cmbOrg, 16, 12, 616);
            put("Artículo (ItemNumber; escribe 3 o más caracteres del inicio y presiona Buscar)", txtItem, 16, 70, 380);
            btnFind.Location = new Point(404, 90);
            chkFg.Location = new Point(504, 94);
            put("Resultados de la búsqueda", cmbFound, 16, 128, 616);
            put("Definición de trabajo", cmbDef, 16, 186, 300);
            put("Cantidad a producir", txtQty, 332, 186, 120);
            put("Inicio (aaaa-mm-dd hh:mm)", txtDate, 468, 186, 164);
            chkExplode.Location = new Point(16, 252);
            Label fixedInfo = UiKit.Lbl("Se crea con estatus ORA_RELEASED (Liberada).", false, 8.5f, Ui.Muted);
            fixedInfo.Location = new Point(16, 280);
            body.Controls.AddRange(new Control[] { btnFind, chkFg, chkExplode, fixedInfo });

            txtDate.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

            Panel bar = new Panel { Dock = DockStyle.Bottom, Height = 52 };
            lblStatus.Location = new Point(16, 18);
            bar.Resize += (s, e) =>
            {
                btnClose.Location = new Point(bar.Width - btnClose.Width - 16, 11);
                btnCreate.Location = new Point(btnClose.Left - btnCreate.Width - 8, 11);
            };
            bar.Controls.AddRange(new Control[] { lblStatus, btnCreate, btnClose });

            Controls.Add(body); Controls.Add(console); Controls.Add(bar);
            ThemeManager.Apply(this);

            btnClose.Click += (s, e) => Close();
            btnFind.Click += async (s, e) => await FindItemsAsync();
            btnCreate.Click += async (s, e) => await CreateAsync();
            txtItem.KeyDown += async (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await FindItemsAsync(); } };
            txtItem.Leave += async (s, e) => await LoadDefinitionsAsync();
            txtItem.TextChanged += (s, e) => btnCreate.Enabled = true;
            txtQty.TextChanged += (s, e) => btnCreate.Enabled = true;
            cmbFound.SelectionChangeCommitted += async (s, e) =>
            {
                Choice c = cmbFound.SelectedItem as Choice;
                if (c != null) { txtItem.Text = c.Value; await LoadDefinitionsAsync(); }
            };
            cmbOrg.SelectedIndexChanged += async (s, e) => { cmbFound.Items.Clear(); defsFor = string.Empty; await LoadDefinitionsAsync(); };
        }

        private string OrgCode { get { Choice c = cmbOrg.SelectedItem as Choice; return c == null ? string.Empty : c.Value; } }

        private async System.Threading.Tasks.Task FindItemsAsync()
        {
            string prefix = txtItem.Text.Trim();
            if (OrgCode.Length == 0) { UiKit.Error("Elige una organización."); return; }
            if (prefix.Length < 3) { UiKit.Error("Escribe al menos 3 caracteres del inicio del número de artículo (Oracle no permite buscar con menos)."); return; }
            btnFind.Enabled = false;
            lblStatus.Text = "Buscando artículos…";
            try
            {
                JObject res = await FusionClient.GetAsync(EndPoints.FinishedGoodSearchPath(OrgCode, prefix, chkFg.Checked));
                List<JToken> items = FusionClient.Items(res).OrderBy(i => UiKit.S(i, "ItemNumber")).ToList();
                cmbFound.Items.Clear();
                foreach (JToken i in items) cmbFound.Items.Add(new Choice(UiKit.S(i, "ItemNumber"), UiKit.S(i, "ItemNumber") + " · " + UiKit.S(i, "ItemDescription")));
                lblStatus.Text = items.Count + " artículo(s)" + (chkFg.Checked ? " Finished Good" : string.Empty) + " en " + OrgCode + (items.Count >= 100 ? " (se muestran 100; afina la búsqueda)" : string.Empty) + ".";
                if (items.Count == 1) { cmbFound.SelectedIndex = 0; txtItem.Text = items[0].Value<string>("ItemNumber"); await LoadDefinitionsAsync(); }
                else if (items.Count > 0) { cmbFound.DroppedDown = true; }
            }
            catch (Exception ex) { UiKit.Error(ex.Message); }
            finally { btnFind.Enabled = true; }
        }

        /// <summary>Definiciones de trabajo del artículo en la organización; ORA_MAIN queda seleccionada si existe.</summary>
        private async System.Threading.Tasks.Task LoadDefinitionsAsync()
        {
            string item = txtItem.Text.Trim(), org = OrgCode;
            string key = org + "|" + item;
            if (item.Length < 3 || org.Length == 0 || key == defsFor) return;
            defsFor = key;
            cmbDef.Items.Clear();
            try
            {
                JObject res = await FusionClient.GetAsync(EndPoints.WorkDefinitionsPath(org, item));
                if (key != defsFor) return;
                foreach (JToken d in FusionClient.Items(res))
                {
                    string code = UiKit.S(d, "WorkDefinitionCode");
                    if (code.Length == 0 || cmbDef.Items.Cast<Choice>().Any(c => c.Value == code)) continue;
                    string expired = UiKit.S(d, "EffectiveToDate").Length > 0 ? " · vencida" : string.Empty;
                    cmbDef.Items.Add(new Choice(code, code + " · " + UiKit.S(d, "WorkDefinitionName") + " (v" + UiKit.S(d, "VersionNumber") + ")" + expired));
                }
                if (cmbDef.Items.Count == 0) lblStatus.Text = "El artículo " + item + " no tiene definición de trabajo en " + org + ".";
                else
                {
                    Choice main = cmbDef.Items.Cast<Choice>().FirstOrDefault(c => c.Value == "ORA_MAIN") ?? (Choice)cmbDef.Items[0];
                    cmbDef.SelectedItem = main;
                    lblStatus.Text = cmbDef.Items.Count + " definición(es) para " + item + ".";
                }
            }
            catch (Exception ex) { defsFor = string.Empty; UiKit.Error(ex.Message); }
        }

        private async System.Threading.Tasks.Task CreateAsync()
        {
            string item = txtItem.Text.Trim();
            Choice def = cmbDef.SelectedItem as Choice;
            decimal qty;
            DateTime start;
            if (OrgCode.Length == 0) { UiKit.Error("Elige una organización."); return; }
            if (item.Length == 0) { UiKit.Error("Captura el artículo."); return; }
            if (def == null) { UiKit.Error("Elige la definición de trabajo (si no hay, el artículo no tiene una en esta organización)."); return; }
            if (!decimal.TryParse(txtQty.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out qty) || qty <= 0) { UiKit.Error("La cantidad a producir debe ser mayor a cero."); return; }
            if (!DateTime.TryParseExact(txtDate.Text.Trim(), new[] { "yyyy-MM-dd HH:mm", "yyyy-MM-dd" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out start))
            { UiKit.Error("La fecha de inicio debe tener el formato aaaa-mm-dd hh:mm."); return; }

            if (MessageBox.Show("Se creará una OT liberada en \"" + InstanceStore.Active.Name + "\":\n\nOrganización: " + OrgCode + "\nArtículo: " + item +
                    "\nDefinición: " + def.Value + "\nCantidad: " + qty.ToString("#,##0.####", CultureInfo.CurrentCulture) + "\nInicio: " + txtDate.Text.Trim() + "\n\n¿Continuar?",
                    "Confirmar creación", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            JObject payload = new JObject
            {
                ["OrganizationCode"] = OrgCode,
                ["ItemNumber"] = item,
                ["WorkOrderStatusCode"] = "ORA_RELEASED",
                ["WorkDefinitionCode"] = def.Value,
                ["PlannedStartQuantity"] = qty,
                ["PlannedStartDate"] = new DateTimeOffset(start).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture),
                ["ExplosionFlag"] = chkExplode.Checked
            };
            btnCreate.Enabled = false; // evita duplicar: se habilita de nuevo al cambiar artículo o cantidad
            lblStatus.Text = "Creando la orden…";
            try
            {
                JObject wo = await FusionClient.PostAsync("/workOrders", payload);
                string number = UiKit.S(wo, "WorkOrderNumber");
                CreatedNumber = number;
                string text = "OT " + number + " creada · " + UiKit.S(wo, "WorkOrderStatusCode") + " · " + item + " × " + qty.ToString("#,##0.####", CultureInfo.CurrentCulture);
                ConsoleLog.Log(LogKind.Ok, text);
                lblStatus.Text = text;
                UiKit.Info("Orden de trabajo " + number + " creada y liberada.");
            }
            catch (Exception ex)
            {
                btnCreate.Enabled = true;
                lblStatus.Text = "No se creó la orden.";
                UiKit.Error(ex.Message);
            }
        }
    }
}
