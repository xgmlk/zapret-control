using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace ZapretControl
{
    public sealed class SetupOptions
    {
        public bool Silent { get; set; }
        public bool NoLaunch { get; set; }
        public bool KeepData { get; set; }
        public bool InstallService { get; set; }
        public bool RegisterTask { get; set; }
        public bool CreateShortcuts { get; set; }
        public bool Repair { get; set; }
        public bool StartService { get; set; }
        public bool Force { get; set; }
        public bool AutoPickStrategy { get; set; }
        public string PayloadZip { get; set; }
        public string PayloadDirectory { get; set; }
        public string PayloadSha256 { get; set; }
        public string Strategy { get; set; }
        public int WaitPid { get; set; }
        public string Mode { get; set; }

        public SetupOptions()
        {
            KeepData = true;
            InstallService = true;
            RegisterTask = true;
            CreateShortcuts = true;
            StartService = true;
            PayloadZip = string.Empty;
            PayloadDirectory = string.Empty;
            PayloadSha256 = string.Empty;
            Strategy = string.Empty;
            Mode = "install";
        }
    }

    public static class SetupEngine
    {
        public const string GuiResource = "ZapretControl.Gui.exe";
        public const string CliResource = "ZapretControl.Cli.exe";
        public const string IconResource = "ZapretControl.app.ico";

        public static void Install(SetupOptions options, Action<string> log)
        {
            RequireAdmin();
            if (!Environment.Is64BitOperatingSystem)
            {
                throw new InvalidOperationException(
                    "Zapret Control требует 64-битную Windows (WinDivert64.sys).");
            }
            SetupOptions value = options ?? new SetupOptions();
            string target = AppPaths.TargetInstallDirectory;
            string payload = AppPaths.TargetPayloadDirectory;
            EnsureTarget(target);
            EnsureNoNewerInstalled(target, value.Force);
            CloseInstalledGui(target, value.WaitPid);
            StopScheduledTask();
            Log(log, "Установка в " + target);

            bool wasRunning = ZapretService.GetStatus().State == ZapretState.Running;
            bool hadService = NativeService.Exists(AppInfo.ServiceName);
            string previousPayload = AppPaths.GetServicePayloadRoot();
            bool hadSettingsFile = File.Exists(AppPaths.SettingsPath);
            string strategy = value.Strategy;
            if (string.IsNullOrEmpty(strategy))
            {
                strategy = ServiceManager.GetCurrentStrategyName();
            }
            bool autoPicked = false;
            bool autoPickedStarted = false;

            WriteApplicationFiles(target, log);
            AcquirePayload(value, target, log);
            string validationError;
            if (!PayloadManager.ValidatePayloadRoot(payload, out validationError))
            {
                throw new InvalidOperationException(
                    "Payload zapret не установлен: " + validationError);
            }
            PayloadManager.EnsureDefaultUserLists(payload);

            if (value.InstallService)
            {
                string strategyPath = ChooseStrategyPath(
                    payload, strategy, value.AutoPickStrategy, value.Silent, log,
                    out autoPicked);
                if (string.IsNullOrEmpty(strategyPath))
                {
                    throw new InvalidOperationException(
                        "В payload не найдено ни одной стратегии .bat.");
                }
                int iface = AppPaths.GetServiceIfaceIndex();
                IfaceStatus ifaceStatus = IfaceStatusProvider.GetStatus();
                if (ifaceStatus.ActiveFound)
                {
                    iface = ifaceStatus.ActiveIndex;
                }
                bool start = value.StartService || wasRunning;
                ServiceInstallResult result = ServiceManager.Install(
                    payload, strategyPath, start, iface);
                strategy = result.StrategyName;
                autoPickedStarted = autoPicked && result.Started;
                Log(log, "Служба zapret: стратегия " + result.StrategyName +
                         ", запуск " + (result.Started ? "да" : "нет") + ".");
                if (result.RolledBack)
                {
                    Log(log, "Стратегия не запустилась, восстановлена предыдущая: " +
                             result.PreviousStrategyName + ".");
                }
                if (start && !result.Started)
                {
                    Log(log, "Предупреждение: служба создана, но не запустилась. " +
                             result.Error);
                }
            }
            else if (hadService && !string.IsNullOrEmpty(previousPayload) &&
                     !AppPaths.IsUnder(previousPayload, target))
            {
                Log(log, "Служба zapret оставлена без изменений: " + previousPayload);
            }

            AclInstaller.ApplyInstallAcls(target, payload, AppPaths.DataDirectory);
            bool firstRunCompleted = autoPickedStarted;
            if (!firstRunCompleted && hadSettingsFile)
            {
                firstRunCompleted = Settings.Load().FirstRunCompleted;
            }
            SaveSettings(
                target, strategy, value.RegisterTask && value.InstallService,
                firstRunCompleted);
            if (value.RegisterTask && value.InstallService)
            {
                RegisterTask(log);
            }
            if (value.CreateShortcuts)
            {
                Shortcut.CreateAll(
                    Path.Combine(target, AppInfo.GuiFileName),
                    Path.Combine(target, AppInfo.SetupFileName));
            }
            UninstallRegistry.Register(
                target,
                Path.Combine(target, AppInfo.SetupFileName),
                PayloadManager.GetVersion(payload));

            Log(log, "Установка завершена. Версия " + AppInfo.Version +
                     ", payload " + PayloadManager.GetVersion(payload) + ".");
            if (!value.Silent && !value.NoLaunch)
            {
                LaunchGui(target, log);
            }
        }

        public static void Update(SetupOptions options, Action<string> log)
        {
            RequireAdmin();
            SetupOptions value = options ?? new SetupOptions();
            string target = AppPaths.TargetInstallDirectory;
            if (AppPaths.IsReparsePoint(target))
            {
                throw new InvalidOperationException(
                    "Папка установки является reparse point: " + target);
            }
            if (!File.Exists(Path.Combine(target, AppInfo.GuiFileName)) ||
                !File.Exists(Path.Combine(target, AppInfo.CliFileName)))
            {
                Install(value, log);
                return;
            }

            EnsureNoNewerInstalled(target, value.Force);
            CloseInstalledGui(target, value.WaitPid);
            StopScheduledTask();
            Log(log, "Обновление " + target + " до " + AppInfo.Version + ".");
            string backup = Path.Combine(
                target, ".app-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            AppPaths.EnsureDirectory(backup);
            BackupFile(Path.Combine(target, AppInfo.GuiFileName), backup);
            BackupFile(Path.Combine(target, AppInfo.CliFileName), backup);
            BackupFile(Path.Combine(target, AppInfo.SetupFileName), backup);

            try
            {
                WriteApplicationFiles(target, log);
                AcquirePayload(value, target, log);
                string payload = AppPaths.TargetPayloadDirectory;
                string validationError;
                if (!PayloadManager.ValidatePayloadRoot(payload, out validationError))
                {
                    throw new InvalidOperationException(
                        "Payload zapret не установлен: " + validationError);
                }

                bool wasRunning = ZapretService.GetStatus().State == ZapretState.Running;
                string strategy = value.Strategy;
                if (string.IsNullOrEmpty(strategy))
                {
                    strategy = ServiceManager.GetCurrentStrategyName();
                }
                bool autoPicked = false;
                ServiceInstallResult serviceResult = null;
                if (value.InstallService)
                {
                    string strategyPath = ChooseStrategyPath(
                        payload, strategy, value.AutoPickStrategy, value.Silent, log,
                        out autoPicked);
                    if (string.IsNullOrEmpty(strategyPath))
                    {
                        throw new InvalidOperationException(
                            "В payload не найдено ни одной стратегии .bat.");
                    }
                    int iface = AppPaths.GetServiceIfaceIndex();
                    IfaceStatus ifaceStatus = IfaceStatusProvider.GetStatus();
                    if (ifaceStatus.ActiveFound)
                    {
                        iface = ifaceStatus.ActiveIndex;
                    }
                    serviceResult = ServiceManager.Install(
                        payload, strategyPath, wasRunning || value.StartService, iface);
                    strategy = serviceResult.StrategyName;
                    if (serviceResult.RolledBack)
                    {
                        Log(log, "Стратегия не запустилась, восстановлена предыдущая: " +
                                 serviceResult.PreviousStrategyName + ".");
                    }
                    if ((wasRunning || value.StartService) && !serviceResult.Started)
                    {
                        Log(log, "Предупреждение: служба создана, но не запустилась. " +
                                 serviceResult.Error);
                    }
                }

                AclInstaller.ApplyInstallAcls(target, payload, AppPaths.DataDirectory);
                bool firstRunCompleted = Settings.Load().FirstRunCompleted;
                if (autoPicked && serviceResult != null && serviceResult.Started)
                {
                    firstRunCompleted = true;
                }
                SaveSettings(
                    target, strategy, value.RegisterTask && value.InstallService,
                    firstRunCompleted);
                if (value.RegisterTask && value.InstallService)
                {
                    RegisterTask(log);
                }
                if (value.CreateShortcuts)
                {
                    Shortcut.CreateAll(
                        Path.Combine(target, AppInfo.GuiFileName),
                        Path.Combine(target, AppInfo.SetupFileName));
                }
                UninstallRegistry.Register(
                    target,
                    Path.Combine(target, AppInfo.SetupFileName),
                    PayloadManager.GetVersion(payload));
                PruneAppBackups(target);
                Log(log, "Обновление завершено. Версия " + AppInfo.Version + ".");
                if (!value.Silent && !value.NoLaunch)
                {
                    LaunchGui(target, log);
                }
            }
            catch (Exception)
            {
                RestoreFile(Path.Combine(target, AppInfo.GuiFileName), backup);
                RestoreFile(Path.Combine(target, AppInfo.CliFileName), backup);
                RestoreFile(Path.Combine(target, AppInfo.SetupFileName), backup);
                throw;
            }
        }

        public static void Repair(SetupOptions options, Action<string> log)
        {
            SetupOptions value = options ?? new SetupOptions();
            value.Mode = "repair";
            value.PayloadZip = string.Empty;
            value.PayloadDirectory = string.Empty;
            Install(value, log);
        }

        public static void Uninstall(SetupOptions options, Action<string> log)
        {
            RequireAdmin();
            SetupOptions value = options ?? new SetupOptions();
            string target = AppPaths.TargetInstallDirectory;
            CloseInstalledGui(target, value.WaitPid);
            if (AppPaths.IsReparsePoint(target))
            {
                throw new InvalidOperationException(
                    "Папка установки является reparse point: " + target);
            }
            if (!IsInstalledTarget(target))
            {
                Log(log, "Zapret Control не установлен в " + target + ".");
                return;
            }
            Log(log, "Удаление Zapret Control.");

            string cli = Path.Combine(target, AppInfo.CliFileName);
            bool taskRemoved = false;
            if (File.Exists(cli))
            {
                taskRemoved = RunProcess(cli, "--unregister-task", 30000, log) == 0;
            }
            if (!taskRemoved)
            {
                try
                {
                    SyncTask.Unregister();
                }
                catch (Exception ex)
                {
                    Log(log, "Предупреждение: задача синхронизации не удалена: " + ex.Message);
                }
            }
            try
            {
                ServiceManager.Remove();
            }
            catch (Exception ex)
            {
                Log(log, "Предупреждение: не удалось удалить службу: " + ex.Message);
            }
            Shortcut.RemoveAll();
            UninstallRegistry.Unregister();

            if (!value.KeepData)
            {
                PayloadManager.TryDeleteDirectory(AppPaths.DataDirectory);
            }

            PayloadManager.TryDeleteDirectory(Path.Combine(target, AppInfo.PayloadFolderName));
            if (IsCurrentExecutableUnder(target))
            {
                ScheduleDirectoryRemoval(target);
                Log(log, "Файлы приложения будут удалены после выхода установщика.");
            }
            else if (!PayloadManager.TryDeleteDirectory(target))
            {
                ScheduleDirectoryRemoval(target);
            }
            Log(log, "Удаление завершено." + (value.KeepData
                ? " Пользовательские данные сохранены в " + AppPaths.DataDirectory + "."
                : string.Empty));
        }

        public static void Rollback(SetupOptions options, Action<string> log)
        {
            RequireAdmin();
            SetupOptions value = options ?? new SetupOptions();
            CloseInstalledGui(AppPaths.TargetInstallDirectory, value.WaitPid);
            PayloadUpdateResult result = PayloadManager.RestoreLatestBackup(
                AppPaths.TargetPayloadDirectory, value.InstallService);
            Log(log, "Откат payload: " + result.OldVersion + " -> " + result.NewVersion +
                     ", копия: " + result.BackupPath + ".");
        }

        public static List<string> SelfTest()
        {
            List<string> messages = new List<string>();
            string error;

            foreach (string resource in new string[] { GuiResource, CliResource, IconResource })
            {
                using (Stream stream = Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream(resource))
                {
                    if (stream == null || stream.Length == 0)
                    {
                        throw new InvalidOperationException(
                            "Встроенный ресурс не найден: " + resource);
                    }
                    messages.Add(resource + "=" + stream.Length);
                }
            }

            if (GameFilterSettings.NormalizeRange("1024-65535,80") != "1024-65535,80" ||
                GameFilterSettings.NormalizeRange("0-10") != null ||
                GameFilterSettings.NormalizeRange("65536") != null ||
                GameFilterSettings.NormalizeRange("100-50") != null)
            {
                throw new InvalidOperationException("GameFilterSettings.NormalizeRange");
            }
            messages.Add("game_filter=OK");

            if (!new SettingsData().AutoUpdatePayload ||
                new SettingsData().FirstRunCompleted)
            {
                throw new InvalidOperationException("SettingsData defaults");
            }
            messages.Add("settings=OK");

            string iface = StrategyParser.ApplyIface("--wf-iface=11 --wf-tcp=80", 7);
            if (iface != "--wf-iface=7 --wf-tcp=80")
            {
                throw new InvalidOperationException("StrategyParser.ApplyIface");
            }
            messages.Add("strategy_iface=OK");

            UpdateManifest manifest = UpdateManifest.Parse(
                "{\"schema\":1,\"app\":{\"version\":\"9.9.9\"," +
                "\"setup_url\":\"https://example.invalid/setup.exe\"," +
                "\"sha256\":\"" + new string('A', 64) + "\"}," +
                "\"payload\":{\"version\":\"9.9.9\"," +
                "\"url\":\"https://example.invalid/payload.zip\"," +
                "\"sha256\":\"" + new string('B', 64) + "\"}}");
            if (!manifest.HasApp || !manifest.HasPayload ||
                !AppInfo.IsNewer(manifest.AppVersion, "1.0.0"))
            {
                throw new InvalidOperationException("UpdateManifest.Parse");
            }
            messages.Add("manifest=OK");

            Shortcut.SelfTest(Application.ExecutablePath);
            messages.Add("shortcut=OK");

            if (!PayloadManager.ValidatePayloadRoot(AppPaths.TargetPayloadDirectory, out error))
            {
                messages.Add("payload=not-installed (" + error + ")");
            }
            else
            {
                messages.Add("payload=OK");
            }
            messages.Add("version=" + AppInfo.Version);
            return messages;
        }

        private static string ChooseStrategyPath(
            string payload,
            string preferred,
            bool autoPick,
            bool silent,
            Action<string> log,
            out bool autoPicked)
        {
            autoPicked = false;
            string path = ServiceManager.ChooseDefaultStrategy(payload, preferred);
            if (string.IsNullOrEmpty(path) || !autoPick || silent)
            {
                return path;
            }
            string picked = AutoPickStrategy(
                payload, System.IO.Path.GetFileNameWithoutExtension(path), log);
            if (string.IsNullOrEmpty(picked))
            {
                return path;
            }
            autoPicked = true;
            return picked;
        }

        private static string AutoPickStrategy(
            string payload, string preferred, Action<string> log)
        {
            try
            {
                Log(log, "Подбор рабочей стратегии…");
                StrategyAdviceReport report = StrategyAdvisor.Recommend(
                    payload,
                    new StrategyAdviceOptions
                    {
                        Preferred = preferred
                    },
                    delegate(string message)
                    {
                        Log(log, message);
                    });
                if (!string.IsNullOrEmpty(report.RecommendedPath))
                {
                    Log(log, "Рекомендована стратегия " + report.RecommendedName +
                             " (" + report.RecommendedPassed + " из " +
                             report.RecommendedTotal + ").");
                    return report.RecommendedPath;
                }
                Log(log, string.IsNullOrEmpty(report.Error)
                    ? "Полностью рабочая стратегия не найдена; используется " +
                      preferred + "."
                    : report.Error);
            }
            catch (Exception ex)
            {
                Log(log, "Подбор стратегии не выполнен: " + ex.Message);
            }
            return null;
        }

        private static void AcquirePayload(
            SetupOptions options, string target, Action<string> log)
        {
            string payload = AppPaths.TargetPayloadDirectory;
            if (!string.IsNullOrEmpty(options.PayloadZip))
            {
                Log(log, "Payload из архива: " + options.PayloadZip);
                PayloadManager.UpdateFromZip(
                    payload,
                    options.PayloadZip,
                    options.PayloadSha256,
                    false,
                    null);
                return;
            }
            if (!string.IsNullOrEmpty(options.PayloadDirectory))
            {
                Log(log, "Payload из папки: " + options.PayloadDirectory);
                PayloadManager.UpdateFromDirectoryAt(
                    payload, options.PayloadDirectory, false, null);
                return;
            }

            string validationError;
            if (PayloadManager.ValidatePayloadRoot(payload, out validationError) &&
                File.Exists(Path.Combine(payload, @"bin\winws.exe")))
            {
                Log(log, "Сохранён существующий payload " +
                         PayloadManager.GetVersion(payload) + ".");
                return;
            }

            string legacy = AppPaths.GetServicePayloadRoot();
            if (!string.IsNullOrEmpty(legacy) &&
                PayloadManager.ValidatePayloadRoot(legacy, out validationError) &&
                !string.Equals(
                    Path.GetFullPath(legacy),
                    Path.GetFullPath(payload),
                    StringComparison.OrdinalIgnoreCase))
            {
                Log(log, "Миграция payload из " + legacy + ".");
                ServiceManager.StopServiceOnly(AppInfo.ServiceName);
                PayloadManager.ImportPayload(legacy, payload);
                return;
            }

            string zip = Path.Combine(
                AppPaths.StagingDirectory,
                "flowseal-" + PayloadPin.Version + "-" +
                Guid.NewGuid().ToString("N") + ".zip");
            try
            {
                AppPaths.EnsureDirectory(AppPaths.StagingDirectory);
                Log(log, "Скачивание Flowseal " + PayloadPin.Version + "...");
                int lastPercent = -1;
                Downloader.DownloadFile(
                    PayloadPin.Url,
                    zip,
                    300000,
                    209715200,
                    delegate(long received, long total)
                    {
                        if (total > 0)
                        {
                            int percent = (int)(received * 100 / total);
                            if (percent != lastPercent)
                            {
                                lastPercent = percent;
                                Log(log, "Загрузка payload: " + percent + "%");
                            }
                        }
                    });
                if (!HashUtil.Matches(zip, PayloadPin.Sha256))
                {
                    throw new InvalidOperationException(
                        "SHA256 payload не совпадает с закреплённым значением.");
                }
                PayloadManager.UpdateFromZip(
                    payload, zip, PayloadPin.Sha256, false, null);
            }
            finally
            {
                PayloadManager.TryDelete(zip);
            }
        }

        private static void WriteApplicationFiles(string target, Action<string> log)
        {
            AppPaths.EnsureDirectory(target);
            string gui = Path.Combine(target, AppInfo.GuiFileName);
            string cli = Path.Combine(target, AppInfo.CliFileName);
            ExtractResource(GuiResource, gui);
            ExtractResource(CliResource, cli);

            string setup = AppPaths.ApplicationExecutablePathForDirectory(
                target, AppInfo.SetupFileName);
            string current = Path.GetFullPath(Application.ExecutablePath);
            if (!string.Equals(current, Path.GetFullPath(setup), StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(current, setup, true);
            }
            Log(log, "Приложение скопировано: " + AppInfo.Version + ".");
        }

        private static void ExtractResource(string resourceName, string destination)
        {
            string temporary = destination + ".new-" + Guid.NewGuid().ToString("N");
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        throw new InvalidOperationException(
                            "Встроенный ресурс не найден: " + resourceName);
                    }
                    using (FileStream file = new FileStream(
                        temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        stream.CopyTo(file);
                        file.Flush();
                    }
                }
                if (new FileInfo(temporary).Length == 0)
                {
                    throw new InvalidOperationException(
                        "Встроенный ресурс пуст: " + resourceName);
                }
                if (File.Exists(destination))
                {
                    File.Copy(temporary, destination, true);
                    File.Delete(temporary);
                }
                else
                {
                    File.Move(temporary, destination);
                }
            }
            finally
            {
                PayloadManager.TryDelete(temporary);
            }
        }

        private static void SaveSettings(
            string target, string strategy, bool registerTask, bool firstRunCompleted)
        {
            SettingsData settings = Settings.Load();
            settings.PayloadRoot = AppPaths.TargetPayloadDirectory;
            if (!string.IsNullOrEmpty(strategy))
            {
                settings.Strategy = strategy;
            }
            settings.InstalledVersion = AppInfo.Version;
            settings.InstallDate = DateTime.Now.ToString("yyyy-MM-dd");
            settings.RegisterSyncTask = registerTask;
            settings.FirstRunCompleted = firstRunCompleted;
            Settings.Save();
        }

        private static void RegisterTask(Action<string> log)
        {
            string cli = Path.Combine(AppPaths.TargetInstallDirectory, AppInfo.CliFileName);
            int exit = RunProcess(cli, "--register-task", 60000, log);
            if (exit != 0)
            {
                Log(log, "Предупреждение: задача автосинхронизации не зарегистрирована (код " +
                         exit + ").");
            }
        }

        private static int RunProcess(
            string fileName, string arguments, int timeoutMs, Action<string> log)
        {
            if (!File.Exists(fileName))
            {
                return -1;
            }
            try
            {
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = fileName;
                info.Arguments = arguments;
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                info.RedirectStandardOutput = true;
                info.RedirectStandardError = true;
                using (Process process = Process.Start(info))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit(timeoutMs);
                    string text = (output + " " + error).Trim();
                    if (text.Length > 0)
                    {
                        Log(log, text);
                    }
                    return process.ExitCode;
                }
            }
            catch (Exception ex)
            {
                Log(log, "Ошибка запуска " + Path.GetFileName(fileName) + ": " + ex.Message);
                return -1;
            }
        }

        private static void StopScheduledTask()
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System),
                    "schtasks.exe");
                info.Arguments = "/End /TN \"" + SyncTask.TaskName + "\"";
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                info.RedirectStandardOutput = true;
                info.RedirectStandardError = true;
                using (Process process = Process.Start(info))
                {
                    process.StandardOutput.ReadToEnd();
                    process.StandardError.ReadToEnd();
                    process.WaitForExit(15000);
                }
            }
            catch (Exception)
            {
            }
        }

        private static void CloseInstalledGui(string target, int waitPid)
        {            if (waitPid > 0)
            {
                try
                {
                    using (Process process = Process.GetProcessById(waitPid))
                    {
                        if (!process.WaitForExit(60000))
                        {
                            throw new InvalidOperationException(
                                "Приложение не закрылось за 60 секунд. Закройте его и повторите.");
                        }
                    }
                }
                catch (ArgumentException)
                {
                }
            }

            foreach (Process process in Process.GetProcessesByName("ZapretControl"))
            {
                try
                {
                    string path = process.MainModule.FileName;
                    if (AppPaths.IsUnder(path, target))
                    {
                        process.CloseMainWindow();
                        if (!process.WaitForExit(5000))
                        {
                            process.Kill();
                            process.WaitForExit(5000);
                        }
                    }
                }
                catch (Exception)
                {
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        private static bool IsInstalledTarget(string target)
        {
            if (string.IsNullOrEmpty(target) || !Directory.Exists(target))
            {
                return false;
            }
            return File.Exists(Path.Combine(target, AppInfo.SetupFileName)) ||
                   File.Exists(Path.Combine(target, AppInfo.GuiFileName)) ||
                   File.Exists(Path.Combine(target, AppInfo.CliFileName));
        }

        private static void EnsureNoNewerInstalled(string target, bool force)
        {
            if (force)
            {
                return;
            }
            string gui = Path.Combine(target, AppInfo.GuiFileName);
            if (!File.Exists(gui))
            {
                return;
            }
            try
            {
                Version installed = new Version(FileVersionInfo.GetVersionInfo(gui).FileVersion);
                Version comparable = new Version(
                    installed.Major,
                    installed.Minor,
                    installed.Build < 0 ? 0 : installed.Build);
                if (comparable > AppInfo.VersionObject)
                {
                    throw new InvalidOperationException(
                        "Установлена более новая версия " + installed +
                        ". Для отката запустите Setup с /force.");
                }
            }
            catch (FormatException)
            {
            }
            catch (ArgumentException)
            {
            }
        }

        private static void EnsureTarget(string target)
        {
            if (AppPaths.IsReparsePoint(target))
            {
                throw new InvalidOperationException(
                    "Папка установки является reparse point: " + target);
            }
            AppPaths.EnsureDirectory(target);
        }

        private static void PruneAppBackups(string target)
        {
            try
            {
                System.Collections.Generic.List<string> backups =
                    new System.Collections.Generic.List<string>();
                foreach (string directory in Directory.GetDirectories(target))
                {
                    if (Path.GetFileName(directory).StartsWith(
                        ".app-backup-", StringComparison.OrdinalIgnoreCase))
                    {
                        backups.Add(directory);
                    }
                }
                backups.Sort(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < backups.Count - 2; i++)
                {
                    PayloadManager.TryDeleteDirectory(backups[i]);
                }
            }
            catch (Exception)
            {
            }
        }

        private static void BackupFile(string source, string backupDirectory)
        {            if (File.Exists(source))
            {
                File.Copy(source, Path.Combine(backupDirectory, Path.GetFileName(source)), true);
            }
        }

        private static void RestoreFile(string destination, string backupDirectory)
        {
            string source = Path.Combine(backupDirectory, Path.GetFileName(destination));
            if (File.Exists(source))
            {
                try
                {
                    File.Copy(source, destination, true);
                }
                catch (Exception)
                {
                }
            }
        }

        private static bool IsCurrentExecutableUnder(string target)
        {
            return AppPaths.IsUnder(Path.GetFullPath(Application.ExecutablePath), target);
        }

        private static void ScheduleDirectoryRemoval(string target)
        {
            string command = "/c ping 127.0.0.1 -n 3 >nul & rd /s /q " +
                             "\"" + target + "\"";
            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
            info.Arguments = command;
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            Process.Start(info);
        }

        private static void LaunchGui(string target, Action<string> log)
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = Path.Combine(target, AppInfo.GuiFileName);
                info.WorkingDirectory = target;
                info.UseShellExecute = true;
                Process.Start(info);
            }
            catch (Exception ex)
            {
                Log(log, "Не удалось запустить GUI: " + ex.Message);
            }
        }

        private static void RequireAdmin()
        {
            if (!AdminCheck.IsAdministrator())
            {
                throw new InvalidOperationException("Требуются права администратора.");
            }
        }

        private static void Log(Action<string> log, string message)
        {
            string line = DateTime.Now.ToString("HH:mm:ss") + " " + message;
            if (log != null)
            {
                log(line);
            }
            try
            {
                AppPaths.EnsureDirectory(AppPaths.LogDirectory);
                File.AppendAllText(
                    AppPaths.SetupLogPath,
                    DateTime.Now.ToString("yyyy-MM-dd ") + line + Environment.NewLine,
                    new UTF8Encoding(true));
            }
            catch (Exception)
            {
            }
        }
    }
}
