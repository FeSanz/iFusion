using System;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace iFusion.Models
{
    /// <summary>Instancia de Oracle Cloud. La contraseña se guarda cifrada con DPAPI (solo este usuario de Windows).</summary>
    internal class Instance
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; }
        public string BaseUrl { get; set; }
        public string Username { get; set; }
        public string PasswordProtected { get; set; }

        /// <summary>Tipo de manufactura que se usa en esta instancia: "process", "discrete" o "both" (por omisión, también para las ya guardadas).</summary>
        public string Manufacturing { get; set; }

        [JsonIgnore]
        public bool AllowsProcess { get { return Manufacturing != "discrete"; } }

        [JsonIgnore]
        public bool AllowsDiscrete { get { return Manufacturing != "process"; } }

        /// <summary>URL completa a partir del host capturado (sin esquema = https://). Acepta también URLs con esquema.</summary>
        [JsonIgnore]
        public string Origin { get { return ToOrigin(BaseUrl); } }

        public static string ToOrigin(string host)
        {
            string s = (host ?? string.Empty).Trim().TrimEnd('/');
            return s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || s.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? s : "https://" + s;
        }

        [JsonIgnore]
        public string Password
        {
            get
            {
                if (string.IsNullOrEmpty(PasswordProtected)) return string.Empty;
                byte[] raw = ProtectedData.Unprotect(Convert.FromBase64String(PasswordProtected), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(raw);
            }
            set
            {
                PasswordProtected = Convert.ToBase64String(
                    ProtectedData.Protect(Encoding.UTF8.GetBytes(value ?? string.Empty), null, DataProtectionScope.CurrentUser));
            }
        }

        public override string ToString() { return Name; }
    }
}
