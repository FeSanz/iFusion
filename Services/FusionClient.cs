using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using iFusion.Models;
using Newtonsoft.Json.Linq;

namespace iFusion.Services
{
    internal class FusionException : Exception
    {
        public FusionException(string message) : base(message) { }
    }

    /// <summary>Cliente REST de Oracle Fusion Cloud (Basic auth) para la instancia activa.</summary>
    internal static class FusionClient
    {
        private const string Root = "/fscmRestApi/resources/latest";
        private static readonly HttpClient Http = new HttpClient(new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate }) { Timeout = TimeSpan.FromMinutes(3) };

        // REST-framework-version: 4 en GET y POST directos (las colecciones hijas llegan como objetos { items, count, … });
        // 1 en el batch (POST a la raíz), cuyas partes devuelven los hijos como arreglos y usan ";" como AND en q=.
        private const string DirectVersion = "4", BatchVersion = "1";

        /// <summary>Elementos de una colección de Fusion: arreglo directo (framework 1) u objeto con "items" (framework 4).</summary>
        public static JArray Items(JToken collection)
        {
            JArray direct = collection as JArray;
            if (direct != null) return direct;
            return (collection as JObject)?["items"] as JArray ?? new JArray();
        }

        private static HttpRequestMessage Build(Instance inst, HttpMethod method, string path, string version = DirectVersion)
        {
            if (inst == null) throw new FusionException("No hay una instancia activa. Configura una desde el botón Instancias.");
            HttpRequestMessage req = new HttpRequestMessage(method, inst.Origin + Root + path);
            string cred = Convert.ToBase64String(Encoding.UTF8.GetBytes(inst.Username + ":" + inst.Password));
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic", cred);
            req.Headers.Add("REST-framework-version", version);
            req.Headers.Add("Accept-Language", "en-US");
            return req;
        }

        private static async Task<string> SendAsync(HttpRequestMessage req)
        {
            System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
            bool writes = await LogSendAsync(req);
            try
            {
                using (HttpResponseMessage res = await Http.SendAsync(req))
                {
                    string body = await res.Content.ReadAsStringAsync();
                    LogRequest(req, (int)res.StatusCode, body);
                    if (!res.IsSuccessStatusCode)
                    {
                        string detail = body;
                        try { JObject o = JObject.Parse(body); detail = (string)o["detail"] ?? (string)o["title"] ?? body; } catch (Exception) { }
                        if (detail.Length > 400) detail = detail.Substring(0, 400);
                        ConsoleLog.Log(LogKind.Error, "HTTP " + (int)res.StatusCode + " · " + clock.ElapsedMilliseconds + " ms", detail);
                        throw new FusionException("Oracle " + (int)res.StatusCode + ": " + detail);
                    }
                    ConsoleLog.Log(LogKind.Rx, "HTTP " + (int)res.StatusCode + " · " + clock.ElapsedMilliseconds + " ms · " + body.Length.ToString("#,##0") + " bytes",
                        writes ? body : null); // solo las escrituras llevan la respuesta cruda; las consultas serían ruido
                    return body;
                }
            }
            catch (HttpRequestException ex)
            {
                ConsoleLog.Log(LogKind.Error, "No se pudo conectar con Oracle", ex.Message);
                throw new FusionException("No se pudo conectar con Oracle: " + ex.Message);
            }
            catch (TaskCanceledException)
            {
                ConsoleLog.Log(LogKind.Error, "Oracle tardó demasiado en responder");
                throw new FusionException("Oracle tardó demasiado en responder.");
            }
        }

        /// <summary>Anota en la consola lo que se le pide a Oracle: la ruta y los filtros (sin la lista de campos ni credenciales) o el contenido del batch.</summary>
        private static async Task<bool> LogSendAsync(HttpRequestMessage req)
        {
            bool writes = false;
            try
            {
                string path = req.RequestUri.AbsolutePath;
                int at = path.IndexOf("/latest", StringComparison.Ordinal);
                if (at >= 0) path = path.Substring(at + 7);

                string mediaType = req.Content?.Headers.ContentType?.MediaType ?? string.Empty;
                if (req.Method == HttpMethod.Post && req.Content != null && !mediaType.Contains("batch"))
                {
                    // POST directo (un solo recurso): se anota la ruta y el contenido enviado
                    writes = true;
                    ConsoleLog.Log(LogKind.Tx, "POST " + path, await req.Content.ReadAsStringAsync());
                }
                else if (req.Method == HttpMethod.Post && req.Content != null)
                {
                    string body = await req.Content.ReadAsStringAsync();
                    JArray parts = (JArray)JObject.Parse(body)["parts"] ?? new JArray();
                    List<string> names = new List<string>();
                    foreach (JToken p in parts)
                    {
                        string target = ((string)p["path"] ?? string.Empty).Split('?')[0].TrimStart('/');
                        names.Add(((string)p["operation"] ?? "?") + " " + target);
                    }
                    writes = parts.Any(p => (string)p["operation"] != "get");
                    ConsoleLog.Log(LogKind.Tx, "POST batch · " + parts.Count + " parte(s): " + string.Join(", ", names),
                        writes ? string.Join(" | ", parts.Where(p => (string)p["operation"] != "get").Select(p => p["payload"]?.ToString(Newtonsoft.Json.Formatting.None))) : null);
                }
                else
                {
                    List<string> filters = new List<string>();
                    foreach (string pair in (req.RequestUri.Query ?? string.Empty).TrimStart('?').Split('&'))
                    {
                        string key = pair.Split('=')[0];
                        if (key == "q" || key == "finder") filters.Add(Uri.UnescapeDataString(pair));
                    }
                    ConsoleLog.Log(LogKind.Tx, req.Method + " " + path, filters.Count > 0 ? string.Join(" ", filters) : null);
                }
            }
            catch (Exception) { }
            return writes;
        }

        /// <summary>Última petición (URL sin credenciales, estado y inicio de la respuesta) para diagnosticar contra Postman.</summary>
        private static void LogRequest(HttpRequestMessage req, int status, string body)
        {
            try
            {
                string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "iFusion");
                System.IO.Directory.CreateDirectory(dir);
                string text = DateTime.Now.ToString("s") + " " + req.Method + " " + req.RequestUri + "\r\nHTTP " + status + "\r\n" +
                    (body.Length > 600 ? body.Substring(0, 600) + "…" : body) + "\r\n\r\n";
                System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "last-request.log"), text);
            }
            catch (Exception) { }
        }

        /// <summary>GET a un recurso (ruta relativa a /fscmRestApi/resources/latest).</summary>
        public static async Task<JObject> GetAsync(string path)
        {
            using (HttpRequestMessage req = Build(InstanceStore.Active, HttpMethod.Get, path))
            {
                return JObject.Parse(await SendAsync(req));
            }
        }

        /// <summary>POST directo (JSON, framework 4) a un recurso; devuelve el registro creado.</summary>
        public static async Task<JObject> PostAsync(string path, JObject payload)
        {
            using (HttpRequestMessage req = Build(InstanceStore.Active, HttpMethod.Post, path))
            {
                req.Content = new StringContent(payload.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
                return JObject.Parse(await SendAsync(req));
            }
        }

        /// <summary>POST batch (application/vnd.oracle.adf.batch+json). Devuelve el arreglo "parts" de la respuesta.</summary>
        public static async Task<JArray> BatchAsync(JArray parts)
        {
            using (HttpRequestMessage req = Build(InstanceStore.Active, HttpMethod.Post, string.Empty, BatchVersion))
            {
                req.Content = new StringContent(new JObject { ["parts"] = parts }.ToString(), Encoding.UTF8);
                req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.oracle.adf.batch+json");
                JObject res = JObject.Parse(await SendAsync(req));
                return (JArray)res["parts"] ?? new JArray();
            }
        }

        /// <summary>Batch de GET: devuelve el payload de cada ruta en el mismo orden.</summary>
        public static async Task<JObject[]> BatchGetAsync(params string[] paths)
        {
            JArray parts = new JArray();
            for (int i = 0; i < paths.Length; i++)
                parts.Add(new JObject { ["id"] = (i + 1).ToString(), ["path"] = paths[i], ["operation"] = "get" });
            JArray res = await BatchAsync(parts);
            JObject[] result = new JObject[paths.Length];
            for (int i = 0; i < paths.Length; i++)
            {
                JToken p = null;
                foreach (JToken t in res) if ((string)t["id"] == (i + 1).ToString()) p = t["payload"];
                result[i] = p as JObject ?? new JObject();
            }
            return result;
        }

        /// <summary>Prueba de conexión con las credenciales de una instancia (aún sin guardar).</summary>
        public static async Task PingAsync(Instance inst)
        {
            using (HttpRequestMessage req = Build(inst, HttpMethod.Get, "/inventoryOrganizations?limit=1&onlyData=true&fields=OrganizationCode"))
            {
                await SendAsync(req);
            }
        }
    }
}
