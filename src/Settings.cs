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
        public bool AllowUnsignedUpdates { get; set; }

        public SettingsData()
        {
            ManifestUrl = AppInfo.DefaultManifestUrl;
            PayloadRoot = string.Empty;
            Strategy = string.Empty;
            AutoCheckUpdates = true;
            AutoUpdatePayload = false;
            LastCheckUtc = string.Empty;
            InstalledVersion = string.Empty;
            InstallDate = string.Empty;
            RegisterSyncTask = true;
            UpdateSignerThumbprint = string.Empty;
            AllowUnsignedUpdates = false;
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
                    AppPaths.EnsureDirectory(AppPaths.DataDirectory);
                    JavaScriptSerializer serializer = new JavaScriptSerializer();
                    string json = serializer.Serialize(_cached);
                    string path = AppPaths.SettingsPath;
                    string temporary = path + ".tmp";
                    File.WriteAllText(temporary, json, new UTF8Encoding(false));
                    if (File.Exists(path))
                    {
                        try
                        {
                            File.Replace(temporary, path, null);
                        }
                        catch (Exception)
                        {
                            File.Copy(temporary, path, true);
                            File.Delete(temporary);
                        }
                    }
                    else
                    {
                        File.Move(temporary, path);
                    }
                    return true;
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
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                SettingsData data = serializer.Deserialize<SettingsData>(json);
                if (data == null)
                {
                    return defaults;
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
                return data;
            }
            catch (Exception)
            {
                return defaults;
            }
        }
    }
}
