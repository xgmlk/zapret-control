using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace ZapretControl
{
    public sealed class UpdateManifest
    {
        public int Schema { get; private set; }
        public string AppVersion { get; private set; }
        public string AppSetupUrl { get; private set; }
        public string AppSha256 { get; private set; }
        public string AppNotesUrl { get; private set; }
        public string AppSignerThumbprint { get; private set; }
        public string PayloadVersion { get; private set; }
        public string PayloadUrl { get; private set; }
        public string PayloadSha256 { get; private set; }
        public string MinAppVersion { get; private set; }

        public bool HasApp
        {
            get { return !string.IsNullOrEmpty(AppVersion); }
        }

        public bool HasPayload
        {
            get { return !string.IsNullOrEmpty(PayloadVersion); }
        }

        public static UpdateManifest Parse(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                throw new InvalidOperationException("Пустой release.json.");
            }
            json = json.TrimStart('\uFEFF').Trim();
            if (json.Length == 0)
            {
                throw new InvalidOperationException("Пустой release.json.");
            }
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            Dictionary<string, object> root;
            try
            {
                root = serializer.DeserializeObject(json) as Dictionary<string, object>;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("release.json не является корректным JSON.", ex);
            }
            if (root == null)
            {
                throw new InvalidOperationException("release.json должен быть объектом.");
            }

            Dictionary<string, object> app = GetObject(root, "app");
            Dictionary<string, object> payload = GetObject(root, "payload");

            UpdateManifest manifest = new UpdateManifest();
            manifest.Schema = GetInt(root, "schema");
            manifest.MinAppVersion = GetString(root, "min_app_version");

            if (app != null)
            {
                manifest.AppVersion = GetString(app, "version");
                manifest.AppSetupUrl = GetString(app, "setup_url");
                manifest.AppSha256 = NormalizeHash(GetString(app, "sha256"));
                manifest.AppNotesUrl = GetString(app, "notes_url");
                manifest.AppSignerThumbprint = GetString(app, "signer_thumbprint");
            }
            if (payload != null)
            {
                manifest.PayloadVersion = GetString(payload, "version");
                manifest.PayloadUrl = GetString(payload, "url");
                manifest.PayloadSha256 = NormalizeHash(GetString(payload, "sha256"));
            }

            if (!manifest.HasApp && !manifest.HasPayload)
            {
                throw new InvalidOperationException(
                    "release.json не содержит ни app, ни payload.");
            }
            return manifest;
        }

        public static UpdateManifest FromFile(string path)
        {
            return Parse(File.ReadAllText(path));
        }

        internal static bool IsSha256(string value)
        {
            string hash = NormalizeHash(value);
            if (hash.Length != 64)
            {
                return false;
            }
            for (int i = 0; i < hash.Length; i++)
            {
                char c = hash[i];
                bool hex = (c >= '0' && c <= '9') ||
                           (c >= 'A' && c <= 'F') ||
                           (c >= 'a' && c <= 'f');
                if (!hex)
                {
                    return false;
                }
            }
            return true;
        }

        private static string NormalizeHash(string value)
        {
            return value == null ? string.Empty : value.Trim().ToUpperInvariant();
        }

        private static Dictionary<string, object> GetObject(
            Dictionary<string, object> source, string key)
        {
            object value;
            if (!source.TryGetValue(key, out value))
            {
                return null;
            }
            return value as Dictionary<string, object>;
        }

        private static string GetString(Dictionary<string, object> source, string key)
        {
            object value;
            if (!source.TryGetValue(key, out value) || value == null)
            {
                return string.Empty;
            }
            return Convert.ToString(value).Trim();
        }

        private static int GetInt(Dictionary<string, object> source, string key)
        {
            object value;
            if (!source.TryGetValue(key, out value) || value == null)
            {
                return 0;
            }
            try
            {
                return Convert.ToInt32(value);
            }
            catch (Exception)
            {
                return 0;
            }
        }
    }
}
