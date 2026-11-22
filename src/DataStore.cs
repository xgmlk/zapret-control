using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace ZapretControl
{
    internal sealed class DataCacheFile
    {
        public string data_b64 { get; set; }
        public string sig_b64 { get; set; }
        public string source { get; set; }
        public string fetched_utc { get; set; }
    }

    // Single source of truth for the Telegram data set. Reads the validated
    // cache once, falls back to the compiled snapshot, and swaps the active
    // document atomically when DataUpdater accepts a newer signed file.
    internal static class DataStore
    {
        private static readonly object Sync = new object();
        private static DataDocument _current;
        private static string _source = "встроенный снимок";
        private static string _cacheError;

        internal static DataDocument Current
        {
            get
            {
                lock (Sync)
                {
                    EnsureLoadedLocked();
                    return _current;
                }
            }
        }

        internal static string Source
        {
            get
            {
                lock (Sync)
                {
                    EnsureLoadedLocked();
                    return _source;
                }
            }
        }

        internal static string CacheError
        {
            get
            {
                lock (Sync)
                {
                    EnsureLoadedLocked();
                    return _cacheError;
                }
            }
        }

        internal static long Seq
        {
            get { return Current.seq; }
        }

        internal static string[] Fronts
        {
            get { return (string[])Current.telegram.fronts.Clone(); }
        }

        internal static string WebHost
        {
            get { return Current.telegram.web_host; }
        }

        internal static int[] DcIds
        {
            get
            {
                DataDc[] dc = Current.telegram.dc;
                int[] ids = new int[dc.Length];
                for (int i = 0; i < dc.Length; i++)
                {
                    ids[i] = dc[i].id;
                }
                return ids;
            }
        }

        internal static string DcAddress(int dc)
        {
            DataDc[] addresses = Current.telegram.dc;
            for (int i = 0; i < addresses.Length; i++)
            {
                if (addresses[i].id == dc)
                {
                    return addresses[i].ip;
                }
            }
            return string.Empty;
        }

        internal static bool TryDirectTarget(int dc, out string ip, out string sni)
        {
            ip = string.Empty;
            sni = string.Empty;
            DataDirect[] targets = Current.telegram.direct_ws;
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i].dc == dc)
                {
                    ip = targets[i].ip;
                    sni = targets[i].sni;
                    return true;
                }
            }
            return false;
        }

        internal static string[] DohEndpoints
        {
            get { return (string[])Current.doh.Clone(); }
        }

        internal static DataPayload Payload
        {
            get { return Current.payload; }
        }

        internal static void Apply(DataDocument document, string source)
        {
            if (document == null)
            {
                return;
            }
            lock (Sync)
            {
                _current = document;
                _source = string.IsNullOrEmpty(source) ? "неизвестно" : source;
                _cacheError = null;
            }
        }

        internal static bool SaveCache(
            byte[] data, byte[] signature, string source)
        {
            try
            {
                DataCacheFile cache = new DataCacheFile();
                cache.data_b64 = Convert.ToBase64String(data);
                cache.sig_b64 = Convert.ToBase64String(signature);
                cache.source = source;
                cache.fetched_utc = DateTime.UtcNow.ToString("o");
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                serializer.MaxJsonLength = 524288;
                return AtomicFile.WriteAllText(
                    AppPaths.DataCachePath, serializer.Serialize(cache));
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void EnsureLoadedLocked()
        {
            if (_current != null)
            {
                return;
            }
            _current = DataSnapshot.Load();
            _source = "встроенный снимок";
            try
            {
                string path = AppPaths.DataCachePath;
                if (!File.Exists(path))
                {
                    return;
                }
                string json = File.ReadAllText(path, Encoding.UTF8);
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                serializer.MaxJsonLength = 524288;
                DataCacheFile cache = serializer.Deserialize<DataCacheFile>(json);
                if (cache == null || string.IsNullOrEmpty(cache.data_b64) ||
                    string.IsNullOrEmpty(cache.sig_b64))
                {
                    _cacheError = "кэш пуст";
                    return;
                }
                byte[] data = Convert.FromBase64String(cache.data_b64);
                byte[] signature = Convert.FromBase64String(cache.sig_b64);
                string keyName;
                if (!DataSignature.Verify(data, signature, out keyName))
                {
                    _cacheError = "подпись кэша неверна";
                    return;
                }
                string text = Encoding.UTF8.GetString(data);
                string error;
                DataDocument document = DataDocumentParser.Parse(text, out error);
                if (document == null ||
                    !DataValidation.Validate(document, DateTime.UtcNow, out error))
                {
                    _cacheError = error ?? "кэш не прошёл проверку";
                    return;
                }
                if (document.seq < _current.seq)
                {
                    _cacheError = "seq кэша меньше снимка";
                    return;
                }
                _current = document;
                _source = (string.IsNullOrEmpty(cache.source) ? "кэш" : cache.source) +
                    " (кэш)";
                _cacheError = null;
            }
            catch (Exception ex)
            {
                _cacheError = ex.GetType().Name + ": " + ex.Message;
            }
        }
    }
}
