using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace ZapretControl
{
    public sealed class SettingsData
    {
        public string ManifestUrl { get; set; }
        public string PayloadRoot { get; set; }
        public string Strategy { get; set; }
        public bool AutoCheckUpdates { get; set; }
        public bool AutoUpdatePayload { get; set; }
        public string LastCheckUtc { get; set; }
        public string InstalledVersion { get; set; }
        public string InstallDate { get; set; }
        public bool RegisterSyncTask { get; set; }
        public string UpdateSignerThumbprint { get; set; }
        public bool FirstRunCompleted { get; set; }
        public int TgProxyPort { get; set; }
        public string TgProxySecret { get; set; }
        public bool TgProxyAutostart { get; set; }
        public bool TgProxyZapret { get; set; }
        public bool DarkTheme { get; set; }
        public bool CloseToTray { get; set; }
        public bool StartInTray { get; set; }
        public bool TrayBalloons { get; set; }
        public string LastCoverageRestartUtc { get; set; }
        public bool CoverageRestartPending { get; set; }

        public SettingsData()
        {
            ManifestUrl = AppInfo.DefaultManifestUrl;
            PayloadRoot = string.Empty;
            Strategy = string.Empty;
            AutoCheckUpdates = true;
            AutoUpdatePayload = true;
            LastCheckUtc = string.Empty;
            InstalledVersion = string.Empty;
            InstallDate = string.Empty;
            RegisterSyncTask = true;
            UpdateSignerThumbprint = string.Empty;
            FirstRunCompleted = false;
            TgProxyPort = 1443;
            TgProxySecret = string.Empty;
            TgProxyAutostart = false;
            TgProxyZapret = true;
            DarkTheme = false;
            CloseToTray = true;
            StartInTray = false;
            TrayBalloons = true;
            LastCoverageRestartUtc = string.Empty;
            CoverageRestartPending = false;
        }
    }

    public static class Settings
    {
        private static readonly object Sync = new object();
        private static SettingsData _cached;

        public static SettingsData Load()
        {
            lock (Sync)
            {
                if (_cached != null)
                {
                    return _cached;
                }
                _cached = LoadFromDisk();
                return _cached;
            }
        }

        public static void Reload()
        {
            lock (Sync)
            {
                _cached = LoadFromDisk();
            }
        }

        public static bool Save()
        {
            lock (Sync)
            {
                if (_cached == null)
                {
                    _cached = new SettingsData();
                }
                try
                {
                    JavaScriptSerializer serializer = new JavaScriptSerializer();
                    string json = serializer.Serialize(_cached);
                    return AtomicFile.WriteAllText(AppPaths.SettingsPath, json);
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        public static void SetPayloadRoot(string root)
        {
            SettingsData data = Load();
            data.PayloadRoot = root ?? string.Empty;
        }

        private static SettingsData LoadFromDisk()
        {
            SettingsData defaults = new SettingsData();
            string path = AppPaths.SettingsPath;
            try
            {
                if (!File.Exists(path))
                {
                    return defaults;
                }
                string json = File.ReadAllText(path, Encoding.UTF8);
                bool hasFirstRunField = json.IndexOf(
                    "\"FirstRunCompleted\"", StringComparison.OrdinalIgnoreCase) >= 0;
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                SettingsData data = serializer.Deserialize<SettingsData>(json);
                if (data == null)
                {
                    return defaults;
                }
                // Settings written before FirstRunCompleted existed belong to
                // an existing installation: updating must not look like a
                // first launch. A fresh install writes the field explicitly.
                if (!hasFirstRunField && !string.IsNullOrEmpty(data.InstalledVersion))
                {
                    data.FirstRunCompleted = true;
                }
                if (string.IsNullOrEmpty(data.ManifestUrl))
                {
                    data.ManifestUrl = AppInfo.DefaultManifestUrl;
                }
                if (data.PayloadRoot == null)
                {
                    data.PayloadRoot = string.Empty;
                }
                if (data.Strategy == null)
                {
                    data.Strategy = string.Empty;
                }
                if (data.LastCheckUtc == null)
                {
                    data.LastCheckUtc = string.Empty;
                }
                if (data.InstalledVersion == null)
                {
                    data.InstalledVersion = string.Empty;
                }
                if (data.InstallDate == null)
                {
                    data.InstallDate = string.Empty;
                }
                if (data.UpdateSignerThumbprint == null)
                {
                    data.UpdateSignerThumbprint = string.Empty;
                }
                if (data.TgProxyPort < 1 || data.TgProxyPort > 65535)
                {
                    data.TgProxyPort = 1443;
                }
                if (data.TgProxySecret == null)
                {
                    data.TgProxySecret = string.Empty;
                }
                if (data.LastCoverageRestartUtc == null)
                {
                    data.LastCoverageRestartUtc = string.Empty;
                }
                return data;
            }
            catch (Exception)
            {
                return defaults;
            }
        }
    }
}
