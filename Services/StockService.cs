using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using iFusion.Models;
using Newtonsoft.Json.Linq;

namespace iFusion.Services
{
    /// <summary>Existencias por artículo (itemStockingInquiries) en una sola llamada batch por tanda de artículos.</summary>
    internal static class StockService
    {
        private const int BatchSize = 20;

        /// <summary>Filas de stock de cada artículo (subinventario / lote), indexadas por ItemNumber.</summary>
        public static async Task<Dictionary<string, JArray>> LoadAsync(IEnumerable<string> items, string orgCode)
        {
            List<string> list = items.Where(i => !string.IsNullOrEmpty(i)).Distinct().ToList();
            Dictionary<string, JArray> result = new Dictionary<string, JArray>();
            for (int i = 0; i < list.Count; i += BatchSize)
            {
                string[] chunk = list.Skip(i).Take(BatchSize).ToArray();
                JObject[] payloads = await FusionClient.BatchGetAsync(chunk.Select(it => EndPoints.ItemStockPath(it, orgCode, true)).ToArray());
                for (int k = 0; k < chunk.Length; k++) result[chunk[k]] = payloads[k]["items"] as JArray ?? new JArray();
            }
            return result;
        }

        /// <summary>
        /// Qué artículos llevan lote. itemsV2 no expone LotControlCode en esta instancia; su equivalente es LotControlValue
        /// ("Full lot control" = código 2, "No lot control" = código 1).
        /// </summary>
        public static async Task<Dictionary<string, bool>> LoadLotsAsync(IEnumerable<string> items, string orgCode)
        {
            List<string> list = items.Where(i => !string.IsNullOrEmpty(i)).Distinct().ToList();
            Dictionary<string, bool> result = new Dictionary<string, bool>();
            string scope = (InstanceStore.Active == null ? string.Empty : InstanceStore.Active.Id) + "|" + orgCode + "|";
            // El control de lote casi nunca cambia y esta consulta tarda ~11 s: solo se piden los artículos que aún no están en caché.
            List<string> missing = new List<string>();
            lock (lotCache)
                foreach (string it in list) { bool v; if (lotCache.TryGetValue(scope + it, out v)) result[it] = v; else missing.Add(it); }
            for (int i = 0; i < missing.Count; i += BatchSize)
            {
                string[] chunk = missing.Skip(i).Take(BatchSize).ToArray();
                JObject[] payloads = await FusionClient.BatchGetAsync(chunk.Select(it => EndPoints.ItemLotPath(it, orgCode, true)).ToArray());
                for (int k = 0; k < chunk.Length; k++)
                {
                    bool lot = Views.frmStock.IsLotControlled(payloads[k]["items"]?.FirstOrDefault());
                    result[chunk[k]] = lot;
                    lock (lotCache) lotCache[scope + chunk[k]] = lot;
                }
            }
            return result;
        }

        private static readonly Dictionary<string, bool> lotCache = new Dictionary<string, bool>();

        /// <summary>Suma de un campo (AvailableToTransact / PrimaryOnhandQuantity), opcionalmente solo de un subinventario.</summary>
        public static decimal Total(JArray rows, string field, string subinventory = null)
        {
            decimal sum = 0;
            if (rows == null) return sum;
            foreach (JToken r in rows)
            {
                if (subinventory != null && !string.Equals((string)r["SubinventoryCode"], subinventory, StringComparison.OrdinalIgnoreCase)) continue;
                decimal d;
                if (decimal.TryParse((string)r[field], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out d)) sum += d;
            }
            return sum;
        }

        public static string Fmt(decimal d) { return d.ToString("#,##0.####", System.Globalization.CultureInfo.CurrentCulture); }
    }
}
