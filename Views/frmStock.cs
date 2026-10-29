using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using iFusion.Models;
using iFusion.Services;
using Newtonsoft.Json.Linq;

namespace iFusion.Views
{
    /// <summary>Stock de un material: sin lote GET directo; con lote consulta batch agrupada por lote.</summary>
    internal class frmStock : Form
    {
        private readonly string item, orgCode;
        private readonly Label lblInfo = UiKit.Lbl("Consultando…", true, 10f);
        private readonly DataGridView grid = UiKit.Grid(false);

        public frmStock(string itemNumber, string organizationCode)
        {
            item = itemNumber; orgCode = organizationCode;
            Text = "Stock · " + item + " · " + orgCode;
            ClientSize = new Size(860, 420);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font(Ui.FontFamily, 9f);
            lblInfo.Location = new Point(16, 14);
            grid.SetBounds(16, 48, 828, 356);
            grid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(lblInfo); Controls.Add(grid);
            ThemeManager.Apply(this);
            Shown += async (s, e) => await LoadAsync();
        }

        /// <summary>LotControlCode 2 = con lote; si Fusion solo devuelve LotControlValue, "Full lot control" = con lote.</summary>
        public static bool IsLotControlled(JToken itemInfo)
        {
            if (itemInfo == null) return false;
            string code = UiKit.S(itemInfo, "LotControlCode");
            if (code.Length > 0) return code == "2";
            return UiKit.S(itemInfo, "LotControlValue").IndexOf("full", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static async System.Threading.Tasks.Task<JToken> GetItemInfoAsync(string item, string orgCode)
        {
            return FusionClient.Items((await FusionClient.GetAsync(EndPoints.ItemLotPath(item, orgCode)))["items"]).FirstOrDefault();
        }

        private async System.Threading.Tasks.Task LoadAsync()
        {
            try
            {
                bool lot = IsLotControlled(await GetItemInfoAsync(item, orgCode));
                JToken rows = lot
                    ? (await FusionClient.BatchGetAsync(EndPoints.ItemStockPath(item, orgCode, true)))[0]["items"]   // con lote: batch GET (framework 1)
                    : (await FusionClient.GetAsync(EndPoints.ItemStockPath(item, orgCode)))["items"];                // sin lote: GET directo (framework 4)
                JArray items = rows as JArray ?? new JArray();

                grid.Columns.Clear();
                UiKit.Col(grid, "Subinventario", 1.4f);
                if (lot) { UiKit.Col(grid, "Lote", 1.2f); UiKit.Col(grid, "Serie", 1f); }
                UiKit.Col(grid, "Locator", 1f);
                UiKit.Col(grid, "Disponible", 1f, true);
                UiKit.Col(grid, "Existencia primaria", 1.2f, true);
                foreach (JToken r in items)
                {
                    string loc = UiKit.S(r, "Locator") + UiKit.S(r, "LocatorName");
                    if (lot) grid.Rows.Add(UiKit.S(r, "SubinventoryCode"), UiKit.S(r, "LotNumber"), UiKit.S(r, "SerialNumber"), loc, UiKit.Num(r, "AvailableToTransact"), UiKit.Num(r, "PrimaryOnhandQuantity"));
                    else grid.Rows.Add(UiKit.S(r, "SubinventoryCode"), loc, UiKit.Num(r, "AvailableToTransact"), UiKit.Num(r, "PrimaryOnhandQuantity"));
                }
                decimal onhand = items.Sum(r => UiKit.D(r, "PrimaryOnhandQuantity")), avail = items.Sum(r => UiKit.D(r, "AvailableToTransact"));
                lblInfo.Text = (lot ? "Con control de lote" : "Sin control de lote") + "   ·   Existencia primaria: " + onhand.ToString("#,##0.####")
                    + "   ·   Disponible a transaccionar: " + avail.ToString("#,##0.####");
            }
            catch (Exception ex) { lblInfo.Text = "No se pudo consultar el stock"; UiKit.Error(ex.Message); }
        }
    }
}
