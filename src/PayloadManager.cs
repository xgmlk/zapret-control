using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace ZapretControl
{
    public sealed class PayloadUpdateResult
    {
        public string OldVersion { get; private set; }
        public string NewVersion { get; private set; }
        public string BackupPath { get; private set; }
        public string StrategyName { get; private set; }
        public bool ServiceReinstalled { get; private set; }
        public bool ServiceStarted { get; private set; }
        public bool UserStatePreserved { get; private set; }
        public bool RolledBack { get; private set; }
        public string Warning { get; private set; }

        public PayloadUpdateResult(
            string oldVersion,
            string newVersion,
            string backupPath,
            string strategyName,
            bool serviceReinstalled,
            bool serviceStarted,
            bool userStatePreserved,
            bool rolledBack,
            string warning)
        {
            OldVersion = oldVersion;
            NewVersion = newVersion;
            BackupPath = backupPath;
            StrategyName = strategyName;
            ServiceReinstalled = serviceReinstalled;
            ServiceStarted = serviceStarted;
            UserStatePreserved = userStatePreserved;
            RolledBack = rolledBack;
            Warning = warning;
        }
    }

    public static class PayloadManager
    {
        private static readonly string[] UserListFiles = new string[]
        {
            @"lists\ipset-exclude-user.txt",
            @"lists\list-general-user.txt",
            @"lists\list-exclude-user.txt"
        };

        private static readonly string[] StateFiles = new string[]
        {
            @"lists\ipset-exclude-user.txt",
            @"lists\list-general-user.txt",
            @"lists\list-exclude-user.txt",
            @"utils\check_updates.enabled",
            @"utils\game_filter.enabled",
            @"bin\ACTIVE_DISCORD_UDP.bin",
            @"bin\ACTIVE_GAME_UDP.bin"
        };

        public static string GetVersion(string payloadRoot)
        {
            try
            {
                string service = Path.Combine(payloadRoot, "service.bat");
                if (!File.Exists(service))
                {
                    return string.Empty;
                }
                string text = File.ReadAllText(service, Encoding.Default);
                Match match = Regex.Match(text, @"LOCAL_VERSION=([0-9]+(?:\.[0-9]+){1,3})");
                return match.Success ? match.Groups[1].Value : string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        public static bool ValidatePayloadRoot(string root, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            {
                error = "Папка zapret не найдена.";
                return false;
            }
            if (AppPaths.IsReparsePoint(root))
            {
                error = "Папка zapret является reparse point.";
                return false;
            }
            string winws = Path.Combine(root, @"bin\winws.exe");
            if (!File.Exists(winws))
            {
                error = "Не найден bin\\winws.exe.";
                return false;
            }
            string windivert = Path.Combine(root, @"bin\WinDivert64.sys");
            if (!File.Exists(windivert))
            {
                error = "Не найден bin\\WinDivert64.sys.";
                return false;
            }
            if (!Directory.Exists(Path.Combine(root, "lists")))
            {
                error = "Не найдена папка lists.";
                return false;
            }
            if (StrategyParser.ListStrategies(root).Count == 0)
            {
                error = "Не найдено ни одной стратегии .bat.";
                return false;
            }
            return true;
        }

        public static void EnsureDefaultUserLists(string payloadRoot)
        {
            try
            {
                string lists = Path.Combine(payloadRoot, "lists");
                AppPaths.EnsureDirectory(lists);
                string ipset = Path.Combine(lists, "ipset-exclude-user.txt");
                if (!File.Exists(ipset))
                {
                    File.WriteAllText(ipset, "203.0.113.113/32" + Environment.NewLine,
                        new UTF8Encoding(false));
                }
                string general = Path.Combine(lists, "list-general-user.txt");
                if (!File.Exists(general))
                {
                    File.WriteAllText(
                        general,
                        "# Never leave this file empty" + Environment.NewLine +
                        "domain.example.abc" + Environment.NewLine,
                        new UTF8Encoding(false));
                }
                string exclude = Path.Combine(lists, "list-exclude-user.txt");
                if (!File.Exists(exclude))
                {
                    File.WriteAllText(exclude, "domain.example.abc" + Environment.NewLine,
                        new UTF8Encoding(false));
                }
            }
            catch (Exception)
            {
            }
        }

        public static void ImportPayload(string sourceRoot, string destinationRoot)
        {
            if (!Directory.Exists(sourceRoot))
            {
                throw new InvalidOperationException("Исходная папка zapret не найдена: " + sourceRoot);
            }
            string error;
            if (!ValidatePayloadRoot(sourceRoot, out error))
            {
                throw new InvalidOperationException("Исходная папка zapret некорректна: " + error);
            }
            if (Directory.Exists(destinationRoot) &&
                Directory.GetFileSystemEntries(destinationRoot).Length > 0)
            {
                throw new InvalidOperationException(
                    "Папка назначения не пуста: " + destinationRoot);
            }
            CopyDirectory(sourceRoot, destinationRoot, true);
            EnsureDefaultUserLists(destinationRoot);
        }

        public static PayloadUpdateResult UpdateFromZip(
            string zipPath,
            string expectedSha256,
            bool installService,
            Downloader.ProgressHandler progress)
        {
            return UpdateFromZip(
                AppPaths.ResolvePayloadRoot(), zipPath, expectedSha256, installService, progress);
        }

        // targetRoot is explicit so Setup.exe can update %ProgramFiles%\ZapretControl
        // even when it is started from Downloads or a staging folder.
        public static PayloadUpdateResult UpdateFromZip(
            string targetRoot,
            string zipPath,
            string expectedSha256,
            bool installService,
            Downloader.ProgressHandler progress)
        {
            if (!File.Exists(zipPath))
            {
                throw new InvalidOperationException("Архив обновления не найден: " + zipPath);
            }
            if (!string.IsNullOrEmpty(expectedSha256) &&
                !HashUtil.Matches(zipPath, expectedSha256))
            {
                throw new InvalidOperationException(
                    "SHA256 архива не совпадает с ожидаемым. Обновление отменено.");
            }

            string root = targetRoot;
            bool exists = Directory.Exists(root) && AppPaths.IsPayloadRoot(root);
            if (exists && AppPaths.IsReparsePoint(root))
            {
                throw new InvalidOperationException(
                    "Папка zapret является reparse point: " + root);
            }
            string invalidRoot = null;
            if (!exists && Directory.Exists(root))
            {
                invalidRoot = root + ".invalid-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                Directory.Move(root, invalidRoot);
            }
            string oldVersion = exists ? GetVersion(root) : string.Empty;
            string parent = Path.GetDirectoryName(root.TrimEnd('\\'));
            if (string.IsNullOrEmpty(parent))
            {
                throw new InvalidOperationException("Не удалось определить папку для staging.");
            }
            AppPaths.EnsureDirectory(parent);

            string token = Guid.NewGuid().ToString("N");
            string staging = Path.Combine(parent, ".zapret.new-" + token);
            string stateDirectory = Path.Combine(parent, ".zapret.state-" + token);
            string backup = null;
            bool wasRunning = ZapretService.GetStatus().State == ZapretState.Running;
            bool serviceInstalled = NativeService.Exists(AppInfo.ServiceName);
            bool manageService = serviceInstalled && IsServiceRoot(root);
            string oldStrategy = ServiceManager.GetCurrentStrategyName();
            int iface = AppPaths.GetServiceIfaceIndex();
            IfaceStatus ifaceStatus = IfaceStatusProvider.GetStatus();
            if (ifaceStatus.ActiveFound)
            {
                iface = ifaceStatus.ActiveIndex;
            }

            try
            {
                ExtractZip(zipPath, staging, progress);
                string newRoot = FindPayloadRoot(staging);
                string validationError;
                if (!ValidatePayloadRoot(newRoot, out validationError))
                {
                    throw new InvalidOperationException(
                        "Архив не содержит корректный zapret: " + validationError);
                }

                bool statePreserved = false;
                if (exists)
                {
                    CaptureUserState(root, stateDirectory);
                    statePreserved = true;
                    if (manageService)
                    {
                        if (installService)
                        {
                            ServiceManager.StopAndDelete(AppInfo.ServiceName);
                        }
                        else
                        {
                            ServiceManager.StopServiceOnly(AppInfo.ServiceName);
                        }
                    }

                    backup = Path.Combine(
                        parent,
                        "." + Path.GetFileName(root) + ".backup-" +
                        DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" +
                        Guid.NewGuid().ToString("N").Substring(0, 6));
                    if (Directory.Exists(backup))
                    {
                        Directory.Delete(backup, true);
                    }
                    Directory.Move(root, backup);
                }

                try
                {
                    Directory.Move(newRoot, root);
                    if (Directory.Exists(stateDirectory))
                    {
                        RestoreUserState(stateDirectory, root);
                    }
                    EnsureDefaultUserLists(root);
                }
                catch (Exception)
                {
                    TryRestoreBackup(root, backup, installService, oldStrategy, iface, wasRunning);
                    throw;
                }

                string strategyName = string.Empty;
                bool reinstalled = false;
                bool started = false;
                string warning = null;
                if (installService)
                {
                    string preferred = string.IsNullOrEmpty(oldStrategy)
                        ? Settings.Load().Strategy
                        : oldStrategy;
                    string strategyPath = ServiceManager.ChooseDefaultStrategy(root, preferred);
                    if (string.IsNullOrEmpty(strategyPath))
                    {
                        throw new InvalidOperationException(
                            "После обновления не найдена ни одна стратегия.");
                    }
                    strategyName = Path.GetFileNameWithoutExtension(strategyPath);
                    if (!string.IsNullOrEmpty(preferred) &&
                        !strategyName.Equals(preferred, StringComparison.OrdinalIgnoreCase))
                    {
                        warning = "Стратегия \"" + preferred +
                                  "\" отсутствует в новой версии; выбрана \"" +
                                  strategyName + "\".";
                    }
                    try
                    {
                        ServiceManager.Install(root, strategyPath, wasRunning, iface);
                        reinstalled = true;
                        started = wasRunning;
                    }
                    catch (Exception)
                    {
                        if (backup != null)
                        {
                            TryRestoreBackup(
                                root, backup, installService, oldStrategy, iface, wasRunning);
                        }
                        throw;
                    }
                }
                // When installService=false the caller (Setup) reinstalls or
                // starts the service itself after the payload swap.

                PruneBackups(root);
                return new PayloadUpdateResult(
                    oldVersion,
                    GetVersion(root),
                    backup,
                    strategyName,
                    reinstalled,
                    started,
                    statePreserved,
                    false,
                    warning);
            }
            catch (Exception ex)
            {
                if (backup != null && Directory.Exists(backup) && !Directory.Exists(root))
                {
                    TryRestoreBackup(root, backup, installService, oldStrategy, iface, wasRunning);
                    throw new InvalidOperationException(
                        "Обновление не удалось, выполнени откат к резервной копии. " +
                        ex.Message, ex);
                }
                throw;
            }
            finally
            {
                TryDeleteDirectory(staging);
                TryDeleteDirectory(stateDirectory);
                if (invalidRoot != null)
                {
                    TryDeleteDirectory(invalidRoot);
                }
            }
        }

        public static PayloadUpdateResult UpdateFromDirectory(
            string sourceRoot,
            bool installService,
            Downloader.ProgressHandler progress)
        {
            return UpdateFromDirectoryAt(
                AppPaths.ResolvePayloadRoot(), sourceRoot, installService, progress);
        }

        public static PayloadUpdateResult UpdateFromDirectoryAt(
            string targetRoot,
            string sourceRoot,
            bool installService,
            Downloader.ProgressHandler progress)
        {
            string tempZip = Path.Combine(AppPaths.StagingDirectory,
                "payload-direct-" + Guid.NewGuid().ToString("N") + ".zip");
            try
            {
                AppPaths.EnsureDirectory(AppPaths.StagingDirectory);
                string source = FindPayloadRoot(sourceRoot);
                string error;
                if (!ValidatePayloadRoot(source, out error))
                {
                    throw new InvalidOperationException("Некорректная папка zapret: " + error);
                }
                ZipFile.CreateFromDirectory(source, tempZip, CompressionLevel.Optimal, false);
                return UpdateFromZip(targetRoot, tempZip, null, installService, progress);
            }
            finally
            {
                TryDelete(tempZip);
            }
        }

        public static List<string> ListBackups(string root)
        {
            List<string> result = new List<string>();
            try
            {
                string parent = Path.GetDirectoryName(root);
                if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
                {
                    return result;
                }
                string prefix = "." + Path.GetFileName(root) + ".backup-";
                List<string> names = new List<string>();
                foreach (string directory in Directory.GetDirectories(parent))
                {
                    string name = Path.GetFileName(directory);
                    if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        names.Add(directory);
                    }
                }
                names.Sort(StringComparer.OrdinalIgnoreCase);
                names.Reverse();
                result.AddRange(names);
            }
            catch (Exception)
            {
            }
            return result;
        }

        public static PayloadUpdateResult RestoreLatestBackup(bool installService)
        {
            return RestoreLatestBackup(AppPaths.ResolvePayloadRoot(), installService);
        }

        public static PayloadUpdateResult RestoreLatestBackup(
            string root, bool installService)
        {
            List<string> backups = ListBackups(root);
            if (backups.Count == 0)
            {
                throw new InvalidOperationException("Резервные копии payload не найдены.");
            }
            string backup = backups[0];
            string validationError;
            if (!ValidatePayloadRoot(backup, out validationError))
            {
                throw new InvalidOperationException(
                    "Резервная копия повреждена: " + validationError);
            }
            string oldVersion = GetVersion(root);
            bool wasRunning = ZapretService.GetStatus().State == ZapretState.Running;
            string strategy = ServiceManager.GetCurrentStrategyName();
            int iface = AppPaths.GetServiceIfaceIndex();
            string failed = root + ".failed-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");

            ServiceManager.StopAndDelete(AppInfo.ServiceName);
            if (Directory.Exists(root))
            {
                Directory.Move(root, failed);
            }
            Directory.Move(backup, root);
            EnsureDefaultUserLists(root);

            bool reinstalled = false;
            bool started = false;
            if (installService)
            {
                string path = ServiceManager.ChooseDefaultStrategy(root, strategy);
                if (!string.IsNullOrEmpty(path))
                {
                    ServiceManager.Install(root, path, wasRunning, iface);
                    reinstalled = true;
                    started = wasRunning;
                }
            }
            return new PayloadUpdateResult(
                oldVersion,
                GetVersion(root),
                failed,
                strategy,
                reinstalled,
                started,
                true,
                true,
                null);
        }

        public static void PruneBackups(string root)
        {
            try
            {
                List<string> backups = ListBackups(root);
                for (int i = 2; i < backups.Count; i++)
                {
                    TryDeleteDirectory(backups[i]);
                }
            }
            catch (Exception)
            {
            }
        }

        internal static void ExtractZip(
            string zipPath,
            string destination,
            Downloader.ProgressHandler progress)
        {
            AppPaths.EnsureDirectory(destination);
            string root = Path.GetFullPath(destination).TrimEnd('\\') + "\\";
            using (ZipArchive archive = ZipFile.OpenRead(zipPath))
            {
                long total = 0;
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    total += entry.Length;
                }
                if (total > 524288000)
                {
                    throw new InvalidOperationException(
                        "Архив распаковывается более чем в 500 МБ. Обновление отменено.");
                }
                long done = 0;
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    string name = entry.FullName.Replace('/', '\\');
                    if (name.EndsWith("\\", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    if (name.IndexOf(':') >= 0 || name.StartsWith("\\", StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            "Архив содержит недопустимый путь: " + entry.FullName);
                    }
                    string target = Path.GetFullPath(Path.Combine(destination, name));
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            "Архив содержит путь вне папки распаковки: " + entry.FullName);
                    }
                    string directory = Path.GetDirectoryName(target);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        AppPaths.EnsureDirectory(directory);
                    }
                    using (Stream input = entry.Open())
                    using (FileStream output = new FileStream(
                        target, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        byte[] buffer = new byte[65536];
                        int read;
                        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            output.Write(buffer, 0, read);
                            done += read;
                            if (progress != null)
                            {
                                progress(done, total);
                            }
                        }
                    }
                }
            }
        }

        private static bool IsServiceRoot(string root)
        {
            string serviceRoot = AppPaths.GetServicePayloadRoot();
            if (!string.IsNullOrEmpty(serviceRoot) && SamePath(root, serviceRoot))
            {
                return true;
            }
            return AppPaths.IsUnder(root, AppPaths.TargetInstallDirectory);
        }

        private static bool SamePath(string left, string right)
        {
            if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
            {
                return false;
            }
            return string.Equals(
                Path.GetFullPath(left).TrimEnd('\\'),
                Path.GetFullPath(right).TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase);
        }

        internal static string FindPayloadRoot(string directory)
        {
            if (AppPaths.IsPayloadRoot(directory))
            {
                return directory;
            }
            try
            {
                foreach (string child in Directory.GetDirectories(directory))
                {
                    if (AppPaths.IsPayloadRoot(child))
                    {
                        return child;
                    }
                }
            }
            catch (Exception)
            {
            }
            return directory;
        }

        internal static void CopyDirectory(string source, string destination, bool skipBackups)
        {
            AppPaths.EnsureDirectory(destination);
            foreach (string file in Directory.GetFiles(source))
            {
                string name = Path.GetFileName(file);
                if (skipBackups && IsBackupFile(name))
                {
                    continue;
                }
                File.Copy(file, Path.Combine(destination, name), true);
            }
            foreach (string directory in Directory.GetDirectories(source))
            {
                string name = Path.GetFileName(directory);
                if (skipBackups && IsBackupFile(name))
                {
                    continue;
                }
                CopyDirectory(directory, Path.Combine(destination, name), skipBackups);
            }
        }

        private static bool IsBackupFile(string name)
        {
            return name.IndexOf(".bak-", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) ||
                   name.IndexOf(".failed-", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static void CaptureUserState(string payloadRoot, string stateDirectory)
        {
            AppPaths.EnsureDirectory(stateDirectory);
            foreach (string relative in StateFiles)
            {
                CopyRelative(payloadRoot, stateDirectory, relative);
            }
            string ipsetState = IpsetManager.GetState(payloadRoot);
            if (!string.Equals(ipsetState, IpsetManager.StateLoaded, StringComparison.Ordinal))
            {
                CopyRelative(payloadRoot, stateDirectory, @"lists\ipset-all.txt");
                CopyRelative(payloadRoot, stateDirectory, @"lists\ipset-all.txt.backup");
            }
        }

        internal static void RestoreUserState(string stateDirectory, string payloadRoot)
        {
            if (!Directory.Exists(stateDirectory))
            {
                return;
            }
            foreach (string file in Directory.GetFiles(stateDirectory, "*", SearchOption.AllDirectories))
            {
                string relative = file.Substring(stateDirectory.Length).TrimStart('\\');
                string target = Path.Combine(payloadRoot, relative);
                string directory = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(directory))
                {
                    AppPaths.EnsureDirectory(directory);
                }
                File.Copy(file, target, true);
            }
        }

        private static void CopyRelative(string sourceRoot, string destinationRoot, string relative)
        {
            string source = Path.Combine(sourceRoot, relative);
            if (!File.Exists(source))
            {
                return;
            }
            string target = Path.Combine(destinationRoot, relative);
            string directory = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(directory))
            {
                AppPaths.EnsureDirectory(directory);
            }
            File.Copy(source, target, true);
        }

        private static void TryRestoreBackup(
            string root,
            string backup,
            bool installService,
            string strategy,
            int iface,
            bool start)
        {
            if (string.IsNullOrEmpty(backup) || !Directory.Exists(backup))
            {
                return;
            }
            try
            {
                if (Directory.Exists(root))
                {
                    TryDeleteDirectory(root);
                }
                Directory.Move(backup, root);
                if (installService)
                {
                    string path = ServiceManager.ChooseDefaultStrategy(root, strategy);
                    if (!string.IsNullOrEmpty(path))
                    {
                        ServiceManager.Install(root, path, start, iface);
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        internal static bool TryDeleteDirectory(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal static void TryDelete(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
