using System;
using System.Reflection;

namespace ZapretControl
{
    // One place for product identity, version and the public update channel.
    public static class AppInfo
    {
        public const string ProductName = "Zapret Control";
        public const string ProductId = "ZapretControl";
        public const string Publisher = "Zapret Control";
        public const string GuiFileName = "ZapretControl.exe";
        public const string CliFileName = "ZapretControl.Cli.exe";
        public const string SetupFileName = "ZapretControl.Setup.exe";
        public const string ServiceName = "zapret";
        public const string PayloadFolderName = "zapret";
        public const string FlowsealRepository = "Flowseal/zapret-discord-youtube";

        // Planned public repository. If it changes, update release.json and this
        // default; users can override the manifest URL in the Updates tab.
        public const string Repository = "xgmlk/zapret-control";
        public const string DefaultManifestUrl =
            "https://github.com/xgmlk/zapret-control/releases/latest/download/release.json";

        public const string FlowsealVersionUrl =
            "https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube/main/.service/version.txt";
        public const string FlowsealIpsetUrl =
            "https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube/refs/heads/main/.service/ipset-service.txt";
        public const string FlowsealHostsUrl =
            "https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube/refs/heads/main/.service/hosts";

        public static string Version
        {
            get
            {
                try
                {
                    Assembly assembly = typeof(AppInfo).Assembly;
                    object[] attributes = assembly.GetCustomAttributes(
                        typeof(AssemblyInformationalVersionAttribute), false);
                    if (attributes.Length > 0)
                    {
                        string value = ((AssemblyInformationalVersionAttribute)attributes[0])
                            .InformationalVersion;
                        if (!string.IsNullOrEmpty(value))
                        {
                            return value;
                        }
                    }
                    Version version = assembly.GetName().Version;
                    return version == null ? "0.0.0" : version.ToString(3);
                }
                catch (Exception)
                {
                    return "0.0.0";
                }
            }
        }

        public static Version VersionObject
        {
            get
            {
                Version parsed;
                if (TryVersion(Version, out parsed))
                {
                    return parsed;
                }
                return new Version(0, 0, 0);
            }
        }

        public static string ReleaseTag
        {
            get { return "v" + Version; }
        }

        public static bool IsNewer(string candidate, string current)
        {
            Version left;
            Version right;
            if (!TryVersion(NormalizeVersion(candidate), out left))
            {
                return false;
            }
            if (!TryVersion(NormalizeVersion(current), out right))
            {
                return true;
            }
            return left > right;
        }

        private static bool TryVersion(string text, out Version version)
        {
            version = null;
            try
            {
                version = new Version(text);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string NormalizeVersion(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "0.0.0";
            }
            string text = value.Trim();
            if (text.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                text = text.Substring(1);
            }
            int dash = text.IndexOf('-');
            if (dash > 0)
            {
                text = text.Substring(0, dash);
            }
            return text;
        }
    }
}
