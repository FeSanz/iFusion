using System;
using System.Drawing;
using System.Windows.Forms;
using iFusion.Models;
using iFusion.Services;

namespace iFusion.Views
{
    /// <summary>Alta, edición y baja de instancias de Oracle Cloud (Desarrollo, Pruebas, Producción...).</summary>
    internal class frmInstances : Form
    {
        private readonly ListBox list = new ListBox();
        private readonly TextBox txtName = new TextBox(), txtUrl = new TextBox(), txtUser = new TextBox(), txtPass = new TextBox();
        private readonly Button btnNew = UiKit.Btn("Nueva"), btnSave = UiKit.Btn("Guardar", true), btnDelete = UiKit.Btn("Eliminar"),
                                btnTest = UiKit.Btn("Probar conexión", false, 140), btnClose = UiKit.Btn("Cerrar");
        private readonly ComboBox cmbKind = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private Instance editing;

        private class Choice
        {
            public readonly string Value, Text;
            public Choice(string value, string text) { Value = value; Text = text; }
            public override string ToString() { return Text; }
        }

        public frmInstances()
        {
            Text = "Instancias de Oracle Cloud";
            ClientSize = new Size(760, 450);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            Font = new Font(Ui.FontFamily, 9f);

            list.SetBounds(16, 16, 220, 380);
            list.BorderStyle = BorderStyle.FixedSingle;
            // La lista nativa queda blanca con selección azul del sistema en el tema oscuro: se dibuja con los colores del tema vigente.
            list.DrawMode = DrawMode.OwnerDrawFixed;
            list.ItemHeight = 28;
            list.DrawItem += (s, e) =>
            {
                if (e.Index < 0) return;
                bool sel = (e.State & DrawItemState.Selected) != 0;
                using (SolidBrush back = new SolidBrush(sel ? Ui.AccentTint : Ui.Input)) e.Graphics.FillRectangle(back, e.Bounds);
                TextRenderer.DrawText(e.Graphics, Convert.ToString(list.Items[e.Index]), e.Font, new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 12, e.Bounds.Height),
                    sel ? Ui.Accent : Ui.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            };
            list.SelectedIndexChanged += (s, e) => LoadInto(list.SelectedItem as Instance);
            btnNew.Location = new Point(16, 404);
            btnNew.Click += (s, e) => { list.ClearSelected(); LoadInto(null); txtName.Focus(); };

            string[] labels = { "Nombre de la instancia (alias)", "URL base (sin http, ej. ehbm-test.fa.us2.oraclecloud.com)", "Usuario", "Contraseña / API Token" };
            TextBox[] boxes = { txtName, txtUrl, txtUser, txtPass };
            txtPass.UseSystemPasswordChar = true;
            for (int i = 0; i < 4; i++)
            {
                Label l = UiKit.Lbl(labels[i], false, 9f, Ui.TextSecondary);
                l.Location = new Point(260, 16 + i * 62);
                boxes[i].SetBounds(260, 38 + i * 62, 484, 26);
                Controls.Add(l); Controls.Add(boxes[i]);
            }
            Label lblKind = UiKit.Lbl("Tipo de manufactura que usa esta instancia", false, 9f, Ui.TextSecondary);
            lblKind.Location = new Point(260, 16 + 4 * 62);
            cmbKind.SetBounds(260, 38 + 4 * 62, 484, 26);
            cmbKind.Items.Add(new Choice("both", "Procesos y discreta"));
            cmbKind.Items.Add(new Choice("process", "Solo manufactura por procesos"));
            cmbKind.Items.Add(new Choice("discrete", "Solo manufactura discreta"));
            cmbKind.SelectedIndex = 0;
            Controls.Add(lblKind); Controls.Add(cmbKind);

            Label hint = UiKit.Lbl("Las credenciales se cifran con Windows (DPAPI) y solo este usuario puede leerlas.", false, 8.5f, Ui.Muted);
            hint.Location = new Point(260, 320);

            btnSave.Location = new Point(260, 350);
            btnTest.Location = new Point(380, 350);
            btnDelete.Location = new Point(530, 350);
            btnClose.Location = new Point(634, 404);
            btnSave.Click += (s, e) => SaveCurrent();
            btnDelete.Click += (s, e) => DeleteCurrent();
            btnTest.Click += async (s, e) => await TestAsync();
            btnClose.Click += (s, e) => Close();

            Controls.AddRange(new Control[] { list, btnNew, hint, btnSave, btnTest, btnDelete, btnClose });
            ThemeManager.Apply(this);
            list.BackColor = Ui.Input; // el fondo vacío de la lista también sigue el tema
            Refill(InstanceStore.Active);
        }

        private void Refill(Instance select)
        {
            list.Items.Clear();
            foreach (Instance i in InstanceStore.All) list.Items.Add(i);
            if (select != null) list.SelectedItem = select;
            if (list.SelectedItem == null) LoadInto(null);
        }

        private void LoadInto(Instance inst)
        {
            editing = inst;
            txtName.Text = inst == null ? string.Empty : inst.Name;
            txtUrl.Text = inst == null ? string.Empty : inst.BaseUrl;
            txtUser.Text = inst == null ? string.Empty : inst.Username;
            txtPass.Text = string.Empty; // nunca se muestra; vacío al editar = conservar la actual
            string kind = inst == null || string.IsNullOrEmpty(inst.Manufacturing) ? "both" : inst.Manufacturing;
            foreach (Choice c in cmbKind.Items) if (c.Value == kind) cmbKind.SelectedItem = c;
            btnDelete.Enabled = inst != null;
        }

        private Instance Collect()
        {
            if (txtName.Text.Trim().Length == 0 || txtUrl.Text.Trim().Length == 0 || txtUser.Text.Trim().Length == 0)
            { UiKit.Error("Alias, URL y usuario son obligatorios."); return null; }
            // Se captura solo el host (ej. ehbm-test.fa.us2.oraclecloud.com); https:// se agrega al conectar.
            Uri uri;
            if (!Uri.TryCreate(Instance.ToOrigin(txtUrl.Text), UriKind.Absolute, out uri) || uri.Host.Length == 0
                || !(uri.Scheme == Uri.UriSchemeHttps || (uri.IsLoopback && uri.Scheme == Uri.UriSchemeHttp)))
            { UiKit.Error("Captura solo el host de la instancia, por ejemplo: ehbm-test.fa.us2.oraclecloud.com"); return null; }
            if (editing == null && txtPass.Text.Length == 0) { UiKit.Error("La contraseña es obligatoria en una instancia nueva."); return null; }

            Instance inst = new Instance { Id = editing != null ? editing.Id : Guid.NewGuid().ToString(), PasswordProtected = editing == null ? null : editing.PasswordProtected };
            inst.Name = txtName.Text.Trim();
            string host = txtUrl.Text.Trim().TrimEnd('/');
            inst.BaseUrl = host.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? host.Substring(8) : host;
            inst.Username = txtUser.Text.Trim();
            inst.Manufacturing = ((Choice)cmbKind.SelectedItem).Value;
            if (txtPass.Text.Length > 0) inst.Password = txtPass.Text;
            return inst;
        }

        private void SaveCurrent()
        {
            Instance inst = Collect();
            if (inst == null) return;
            if (editing != null) InstanceStore.All.Remove(editing);
            InstanceStore.Save(inst);
            Refill(inst);
            UiKit.Info("Instancia guardada.");
        }

        private void DeleteCurrent()
        {
            if (editing == null) return;
            if (MessageBox.Show("¿Eliminar la instancia \"" + editing.Name + "\"?", "Confirmar", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            InstanceStore.Delete(editing);
            Refill(null);
        }

        private async System.Threading.Tasks.Task TestAsync()
        {
            Instance inst = Collect();
            if (inst == null) return;
            btnTest.Enabled = false;
            try { await FusionClient.PingAsync(inst); UiKit.Info("Conexión correcta."); }
            catch (Exception ex) { UiKit.Error(ex.Message); }
            finally { btnTest.Enabled = true; }
        }
    }
}
