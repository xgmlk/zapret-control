using System;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace ZapretControl
{
    // Filesystem layout shared by GUI, CLI and Setup.
    //
    //   %ProgramFiles%\ZapretControl\            application (admin-only)
    //   %ProgramFiles%\ZapretControl\zapret\     Flowseal payload (admin-only)
    //   %ProgramData%\ZapretControl\             user data, backups, logs
    public static class AppPaths
    {
        private const string ServiceKeyPath =
            @"SYSTEM\CurrentControlSet\Services\" + AppInfo.ServiceName;

        public static string InstallDirectory
        {
            get
            {
                string location = Assembly.GetExecutingAssembly().Location;
                if (string.IsNullOrEmpty(location))
                {
                    return Path.GetFullPath(".");
                }
                string directory = Path.GetDirectoryName(location);
                return string.IsNullOrEmpty(directory) ? Path.GetFullPath(".") : directory;
            }
        }

        public static string InstallDirectoryName
        {
            get { return Path.GetFileName(InstallDirectory.TrimEnd('\\')); }
        }

        public static string ManagedPayloadDirectory
        {
            get { return Path.Combine(InstallDirectory, AppInfo.PayloadFolderName); }
        }

        public static string TargetInstallDirectory
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    AppInfo.ProductId);
            }
        }

        public static string TargetPayloadDirectory
        {
            get { return Path.Combine(TargetInstallDirectory, AppInfo.PayloadFolderName); }
        }

        public static string DataDirectory
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    AppInfo.ProductId);
            }
        }

        public static string DataCacheDirectory
        {
            get { return Path.Combine(DataDirectory, "data"); }
        }

        public static string DataCachePath
        {
            get { return Path.Combine(DataCacheDirectory, "cache.json"); }
        }

        public static string StagingDirectory
        {
            get { return Path.Combine(DataDirectory, "staging"); }
        }

        public static string BackupDirectory
        {
            get { return Path.Combine(DataDirectory, "backups"); }
        }

        public static string LogDirectory
        {
            get { return Path.Combine(DataDirectory, "logs"); }
        }

        public static string UserListsDirectory
        {
            get { return Path.Combine(DataDirectory, "user-lists"); }
        }

        public static string SettingsPath
        {
            get { return Path.Combine(DataDirectory, "settings.json"); }
        }

        public static string InstallStatePath
        {
            get { return Path.Combine(InstallDirectory, "install.json"); }
        }

        public static string SetupLogPath
        {
            get { return Path.Combine(LogDirectory, "setup.log"); }
        }

        public static string SyncLogPath
        {
            get { return Path.Combine(LogDirectory, "sync-iface.log"); }
        }

        public static string UpdateLogPath
        {
            get { return Path.Combine(LogDirectory, "update.log"); }
        }

        public static string ResolvePayloadRoot()
        {
            return ResolvePayloadRoot(Settings.Load().PayloadRoot);
        }

        // Configured root wins, then the managed folder, then an existing
        // service outside Program Files (legacy or development install).
        public static string ResolvePayloadRoot(string configuredRoot)
        {
            if (IsPayloadRoot(configuredRoot))
            {
                return Path.GetFullPath(configuredRoot);
            }
            string managed = ManagedPayloadDirectory;
            if (IsPayloadRoot(managed))
            {
                return Path.GetFullPath(managed);
            }
            string legacy = GetServicePayloadRoot();
            if (IsPayloadRoot(legacy))
            {
                return Path.GetFullPath(legacy);
            }
            return Path.GetFullPath(managed);
        }

        public static bool IsPayloadRoot(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }
            try
            {
                return File.Exists(Path.Combine(path, @"bin\winws.exe"));
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static string GetServiceImagePath()
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(ServiceKeyPath, false))
                {
                    return key == null ? null : key.GetValue("ImagePath") as string;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static string GetServiceStrategy()
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(ServiceKeyPath, false))
                {
                    return key == null ? null : key.GetValue("zapret-discord-youtube") as string;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static int GetServiceIfaceIndex()
        {
            string imagePath = GetServiceImagePath();
            int index;
            if (IfaceStatusProvider.TryParseWfIface(imagePath, out index))
            {
                return index;
            }
            return -1;
        }

        public static string GetServicePayloadRoot()
        {
            string imagePath = GetServiceImagePath();
            if (string.IsNullOrEmpty(imagePath))
            {
                return null;
            }
            try
            {
                string value = imagePath.Trim();
                if (value.StartsWith("\"", StringComparison.Ordinal))
                {
                    int closing = value.IndexOf('"', 1);
                    if (closing <= 1)
                    {
                        return null;
                    }
                    value = value.Substring(1, closing - 1);
                }
                else
                {
                    int space = value.IndexOf(' ');
                    if (space > 0)
                    {
                        value = value.Substring(0, space);
                    }
                }
                if (value.StartsWith(@"\??\", StringComparison.Ordinal))
                {
                    value = value.Substring(4);
                }
                if (!Path.IsPathRooted(value))
                {
                    return null;
                }
                string bin = Path.GetDirectoryName(Path.GetFullPath(value));
                return string.IsNullOrEmpty(bin) ? null : Path.GetDirectoryName(bin);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static bool IsUnderProgramFiles(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            return IsUnder(path, programFiles) ||
                   (!string.IsNullOrEmpty(programFilesX86) && IsUnder(path, programFilesX86));
        }

        public static bool IsUnder(string path, string root)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(root))
            {
                return false;
            }
            string prefix = Path.GetFullPath(root).TrimEnd('\\') + "\\";
            return Path.GetFullPath(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsReparsePoint(string path)
        {
            try
            {
                FileAttributes attributes = File.GetAttributes(path);
                return (attributes & FileAttributes.ReparsePoint) != 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static bool EnsureDataLayout()
        {
            try
            {
                EnsureDirectory(DataDirectory);
                EnsureDirectory(StagingDirectory);
                EnsureDirectory(BackupDirectory);
                EnsureDirectory(LogDirectory);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static void EnsureDirectory(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }

        public static string ApplicationExecutablePath(string fileName)
        {
            return Path.Combine(InstallDirectory, fileName);
        }

        public static string ApplicationExecutablePathForDirectory(
            string directory, string fileName)
        {
            return Path.Combine(directory, fileName);
        }

        public static bool IsInstalledLayout()
        {
            return IsUnderProgramFiles(InstallDirectory) &&
                   File.Exists(ApplicationExecutablePath(AppInfo.GuiFileName)) &&
                   File.Exists(ApplicationExecutablePath(AppInfo.CliFileName));
        }
    }
}
