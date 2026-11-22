using System;
using System.Diagnostics;
using System.IO;

namespace ZapretControl
{
    public sealed class UpdateCheckResult
    {
        public bool ManifestAvailable { get; set; }
        public string ManifestError { get; set; }
        public string LatestAppVersion { get; set; }
        public string LatestPayloadVersion { get; set; }
        public string CurrentAppVersion { get; set; }
        public string CurrentPayloadVersion { get; set; }
        public bool AppUpdateAvailable { get; set; }
        public bool PayloadUpdateAvailable { get; set; }
        public string AppSetupUrl { get; set; }
        public string AppSha256 { get; set; }
        public string PayloadUrl { get; set; }
        public string PayloadSha256 { get; set; }
        public string NotesUrl { get; set; }
        public UpdateManifest Manifest { get; set; }

        public UpdateCheckResult()
        {
            LatestAppVersion = string.Empty;
            LatestPayloadVersion = string.Empty;
            CurrentAppVersion = AppInfo.Version;
            CurrentPayloadVersion = string.Empty;
            AppSetupUrl = string.Empty;
            AppSha256 = string.Empty;
            PayloadUrl = string.Empty;
            PayloadSha256 = string.Empty;
            NotesUrl = string.Empty;
        }
    }

    public sealed class AppUpdateLaunch
    {
        public string SetupPath { get; private set; }
        public int ProcessId { get; private set; }
        public bool Unsigned { get; private set; }

        public AppUpdateLaunch(string setupPath, int processId, bool unsigned)
        {
            SetupPath = setupPath;
            ProcessId = processId;
            Unsigned = unsigned;
        }
    }

    // Dual update channel: Flowseal payload and our own Setup.exe.
    public static class UpdateService
    {
        public static UpdateCheckResult Check(string manifestUrl, string payloadRoot)
        {
            UpdateCheckResult result = new UpdateCheckResult();
            result.CurrentPayloadVersion = PayloadManager.GetVersion(payloadRoot);
            manifestUrl = (manifestUrl ?? string.Empty).Trim();
            if (manifestUrl.Length == 0)
            {
                manifestUrl = AppInfo.DefaultManifestUrl;
            }

            string json = null;
            try
            {
                json = Downloader.GetString(manifestUrl, 20000, 1048576);
            }
            catch (Exception ex)
            {
                result.ManifestError = "Не удалось скачать release.json: " + ex.Message;
            }

            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    UpdateManifest manifest = UpdateManifest.Parse(json);
                    result.Manifest = manifest;
                    result.ManifestAvailable = true;
                    result.LatestAppVersion = manifest.AppVersion ?? string.Empty;
                    result.LatestPayloadVersion = manifest.PayloadVersion ?? string.Empty;
                    result.AppSetupUrl = manifest.AppSetupUrl ?? string.Empty;
                    result.AppSha256 = manifest.AppSha256 ?? string.Empty;
                    result.PayloadUrl = manifest.PayloadUrl ?? string.Empty;
                    result.PayloadSha256 = manifest.PayloadSha256 ?? string.Empty;
                    result.NotesUrl = manifest.AppNotesUrl ?? string.Empty;
                    result.AppUpdateAvailable = manifest.HasApp &&
                        AppInfo.IsNewer(manifest.AppVersion, AppInfo.Version);
                    result.PayloadUpdateAvailable = manifest.HasPayload &&
                        (string.IsNullOrEmpty(result.CurrentPayloadVersion) ||
                         AppInfo.IsNewer(manifest.PayloadVersion, result.CurrentPayloadVersion));
                    if (result.PayloadUpdateAvailable && !UpdateManifest.IsSha256(manifest.PayloadSha256))
                    {
                        result.PayloadUpdateAvailable = false;
                        result.ManifestError =
                            "В release.json нет SHA256 для payload; обновление отключено.";
                    }
                    if (result.AppUpdateAvailable && !UpdateManifest.IsSha256(manifest.AppSha256))
                    {
                        result.AppUpdateAvailable = false;
                        result.ManifestError =
                            "В release.json нет SHA256 для установщика; обновление отключено.";
                    }
                    MergeDataPayload(result);
                    return result;
                }
                catch (Exception ex)
                {
                    result.ManifestError = "release.json некорректен: " + ex.Message;
                }
            }

            // Fallback: show the current Flowseal release, but never install it
            // without a hash from our manifest.
            try
            {
                string latest = Downloader.GetString(AppInfo.FlowsealVersionUrl, 10000, 256);
                result.LatestPayloadVersion = latest;
                if (string.IsNullOrEmpty(result.ManifestError))
                {
                    result.ManifestError = "release.json недоступен.";
                }
            }
            catch (Exception)
            {
            }
            MergeDataPayload(result);
            return result;
        }

        // The signed data channel carries the current Flowseal release even
        // when our own release.json still names an older payload. It is the
        // faster channel, so a newer version from data.json wins.
        private static void MergeDataPayload(UpdateCheckResult result)
        {
            DataPayload data = DataStore.Payload;
            if (result == null || data == null ||
                string.IsNullOrEmpty(data.version) ||
                !UpdateManifest.IsSha256(data.sha256) ||
                !DataValidation.IsHttps(data.url))
            {
                return;
            }
            if (!AppInfo.IsNewer(data.version, result.CurrentPayloadVersion))
            {
                return;
            }
            if (result.PayloadUpdateAvailable &&
                !AppInfo.IsNewer(data.version, result.LatestPayloadVersion))
            {
                return;
            }
            result.LatestPayloadVersion = data.version;
            result.PayloadUrl = data.url;
            result.PayloadSha256 = data.sha256;
            result.PayloadUpdateAvailable = true;
        }

        public static PayloadUpdateResult ApplyPayload(
            UpdateCheckResult check,
            string payloadRoot,
            Downloader.ProgressHandler progress)
        {
            if (check == null)
            {
                throw new InvalidOperationException(
                    "Нет данных об обновлении payload.");
            }
            // The version, URL and hash may come from release.json or from the
            // signed data channel; UpdateService.Check fills both into the
            // result, so this method does not depend on the manifest.
            string version = !string.IsNullOrEmpty(check.LatestPayloadVersion)
                ? check.LatestPayloadVersion
                : (check.Manifest == null ? string.Empty : check.Manifest.PayloadVersion);
            string url = !string.IsNullOrEmpty(check.PayloadUrl)
                ? check.PayloadUrl
                : (check.Manifest == null ? string.Empty : check.Manifest.PayloadUrl);
            string sha256 = !string.IsNullOrEmpty(check.PayloadSha256)
                ? check.PayloadSha256
                : (check.Manifest == null ? string.Empty : check.Manifest.PayloadSha256);
            if (string.IsNullOrEmpty(version))
            {
                throw new InvalidOperationException("Нет версии payload.");
            }
            if (!UpdateManifest.IsSha256(sha256))
            {
                throw new InvalidOperationException(
                    "Нет SHA256 payload. Обновление отменено.");
            }
            RequireHttps(url);

            string zip = Path.Combine(
                AppPaths.StagingDirectory,
                "flowseal-" + version + "-" +
                Guid.NewGuid().ToString("N") + ".zip");
            try
            {
                AppPaths.EnsureDirectory(AppPaths.StagingDirectory);
                Downloader.DownloadFile(
                    url, zip, 300000, 209715200, progress);
                if (!HashUtil.Matches(zip, sha256))
                {
                    throw new InvalidOperationException(
                        "SHA256 payload не совпадает. Обновление отменено.");
                }
                PayloadUpdateResult result = PayloadManager.UpdateFromZip(
                    payloadRoot, zip, sha256, true, progress);
                Log("payload update " + result.OldVersion + " -> " +
                    result.NewVersion + " strategy=" + result.StrategyName +
                    " backup=" + (result.BackupPath ?? "none"));
                return result;
            }
            finally
            {
                PayloadManager.TryDelete(zip);
            }
        }

        public static AppUpdateLaunch ApplyAppUpdate(
            UpdateCheckResult check,
            Downloader.ProgressHandler progress)
        {
            if (check == null || check.Manifest == null)
            {
                throw new InvalidOperationException(
                    "Нет release.json: обновление приложения недоступно.");
            }
            if (!AppPaths.IsInstalledLayout())
            {
                throw new InvalidOperationException(
                    "Обновление приложения доступно только из установленной копии в Program Files.");
            }
            UpdateManifest manifest = check.Manifest;
            if (!manifest.HasApp || !AppInfo.IsNewer(manifest.AppVersion, AppInfo.Version))
            {
                throw new InvalidOperationException("Обновление приложения не требуется.");
            }
            if (!UpdateManifest.IsSha256(manifest.AppSha256))
            {
                throw new InvalidOperationException(
                    "В release.json нет SHA256 установщика. Обновление отменено.");
            }
            RequireHttps(manifest.AppSetupUrl);

            string setup = Path.Combine(
                AppPaths.StagingDirectory,
                "ZapretControl.Setup-" + manifest.AppVersion + "-" +
                Guid.NewGuid().ToString("N") + ".exe");
            try
            {
                AppPaths.EnsureDirectory(AppPaths.StagingDirectory);
                Downloader.DownloadFile(
                    manifest.AppSetupUrl, setup, 300000, 209715200, progress);
                if (!HashUtil.Matches(setup, manifest.AppSha256))
                {
                    throw new InvalidOperationException(
                        "SHA256 установщика не совпадает. Обновление отменено.");
                }

                SettingsData settings = Settings.Load();
                string expectedSigner = settings.UpdateSignerThumbprint;
                if (string.IsNullOrEmpty(expectedSigner))
                {
                    expectedSigner = manifest.AppSignerThumbprint;
                }
                SignatureCheck signature = SignatureVerifier.Verify(
                    setup, expectedSigner);
                bool unsigned = !signature.IsSigned;
                if (!signature.IsValid && !unsigned)
                {
                    throw new InvalidOperationException(
                        "Установщик не прошёл проверку Authenticode: " +
                        (signature.Error ?? "неизвестная ошибка") +
                        ". Установите вручную после проверки подписи. Обновление отменено.");
                }

                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = setup;
                info.Arguments = "/S /update /wait-pid=" +
                    Process.GetCurrentProcess().Id.ToString();
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                Process process = Process.Start(info);
                if (process == null)
                {
                    throw new InvalidOperationException("Не удалось запустить установщик.");
                }
                Log("app update " + AppInfo.Version + " -> " + manifest.AppVersion +
                    " setup=" + setup + " pid=" + process.Id +
                    " signer=" + (signature.Thumbprint ?? "none") +
                    " unsigned=" + (unsigned ? "yes" : "no"));
                return new AppUpdateLaunch(setup, process.Id, unsigned);
            }
            catch (Exception)
            {
                PayloadManager.TryDelete(setup);
                throw;
            }
        }

        private static void RequireHttps(string url)
        {
            if (string.IsNullOrEmpty(url) ||
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Ссылка обновления должна быть HTTPS.");
            }
        }

        private static void Log(string message)
        {
            try
            {
                AppPaths.EnsureDirectory(AppPaths.LogDirectory);
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                              " " + message + Environment.NewLine;
                File.AppendAllText(
                    AppPaths.UpdateLogPath, line, new System.Text.UTF8Encoding(true));
            }
            catch (Exception)
            {
            }
        }
    }
}
