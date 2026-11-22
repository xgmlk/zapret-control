using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ZapretControl
{
    internal sealed class DataUpdateResult
    {
        internal bool Ok;
        internal bool Changed;
        internal bool Busy;
        internal long Seq;
        internal int Fronts;
        internal string Source;
        internal string Error;
        internal string Coverage;
    }

    // Downloads, verifies and installs the signed data channel. Network work
    // may take a few seconds; callers run it off the UI thread (GUI) or use
    // the CLI switch directly.
    internal static class DataUpdater
    {
        private const int TimeoutMs = 10000;
        internal const int MaxDataBytes = 262144;
        private const int MaxSignatureBytes = 8192;
        private const int CoverageRestartHours = 24;

        private static readonly object Sync = new object();
        private static readonly string[] FallbackFiles =
            new string[] { "data.json", "data.json.sig" };

        internal static event Action<DataUpdateResult> Completed;

        internal static DataUpdateResult Check(bool updateCoverage)
        {
            DataUpdateResult result = new DataUpdateResult();
            if (!System.Threading.Monitor.TryEnter(Sync))
            {
                result.Busy = true;
                result.Error = "проверка уже выполняется";
                return result;
            }
            try
            {
                long previousSeq = DataStore.Seq;
                string previousSource = DataStore.Source;
                string lastError = null;

                for (int source = 0; source < 2; source++)
                {
                    string dataUrl;
                    string signatureUrl;
                    string label;
                    if (source == 0)
                    {
                        dataUrl = AppInfo.DataUrl;
                        signatureUrl = AppInfo.DataSignatureUrl;
                        label = "release";
                    }
                    else
                    {
                        dataUrl = AppInfo.DataFallbackBase + FallbackFiles[0];
                        signatureUrl = AppInfo.DataFallbackBase + FallbackFiles[1];
                        label = "jsdelivr";
                    }

                    byte[] data;
                    byte[] signature;
                    try
                    {
                        data = Downloader.GetBytes(
                            dataUrl, TimeoutMs, MaxDataBytes, null);
                        byte[] signatureText = Downloader.GetBytes(
                            signatureUrl, TimeoutMs, MaxSignatureBytes, null);
                        signature = DataSignature.DecodeSignature(
                            Encoding.UTF8.GetString(signatureText));
                        if (signature == null)
                        {
                            throw new InvalidOperationException(
                                "подпись не декодируется");
                        }
                    }
                    catch (Exception ex)
                    {
                        lastError = label + ": " + Short(ex);
                        continue;
                    }

                    string keyName;
                    if (!DataSignature.Verify(data, signature, out keyName))
                    {
                        lastError = label + ": подпись неверна";
                        continue;
                    }
                    string text = Encoding.UTF8.GetString(data);
                    string error;
                    DataDocument document = DataDocumentParser.Parse(text, out error);
                    if (document == null ||
                        !DataValidation.Validate(document, DateTime.UtcNow, out error))
                    {
                        lastError = label + ": " + (error ?? "не прошёл проверку");
                        continue;
                    }
                    if (!DataValidation.SequenceAllowed(document.seq, previousSeq))
                    {
                        lastError = label + ": seq " + document.seq +
                            " меньше сохранённого " + previousSeq;
                        continue;
                    }

                    bool changed = document.seq > previousSeq ||
                        previousSeq == 0;
                    if (changed)
                    {
                        if (!DataStore.SaveCache(data, signature, label))
                        {
                            Log("cache write failed source=" + label);
                        }
                        DataStore.Apply(document, label);
                    }
                    result.Ok = true;
                    result.Changed = changed;
                    result.Seq = document.seq;
                    result.Fronts = document.telegram.fronts.Length;
                    result.Source = label + " (" + keyName + ")";
                    if (previousSource.StartsWith(
                        "встроенный", StringComparison.Ordinal))
                    {
                        result.Changed = true;
                    }
                    if (updateCoverage)
                    {
                        result.Coverage = SyncCoverage();
                    }
                    Log("check ok source=" + label + " seq=" + document.seq +
                        " fronts=" + document.telegram.fronts.Length +
                        " changed=" + (result.Changed ? "yes" : "no"));
                    RaiseCompleted(result);
                    return result;
                }

                result.Error = lastError ?? "нет источника данных";
                result.Seq = previousSeq;
                result.Source = previousSource;
                result.Fronts = DataStore.Fronts.Length;
                if (updateCoverage)
                {
                    // Even without a network the cached fronts may be newer
                    // than the file written before the last shutdown.
                    result.Coverage = SyncCoverage();
                }
                Log("check failed: " + result.Error);
                RaiseCompleted(result);
                return result;
            }
            finally
            {
                System.Threading.Monitor.Exit(Sync);
            }
        }

        // Rewrites the zapret coverage block when the active fronts changed.
        // Restarting the service is rate-limited: a running winws keeps the
        // old list until restarted, but doing that on every data refresh would
        // disturb the user.
        internal static string SyncCoverage()
        {
            try
            {
                SettingsData settings = Settings.Load();
                if (!settings.TgProxyZapret)
                {
                    return "выключено";
                }
                string root = AppPaths.ResolvePayloadRoot();
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                {
                    return "payload не найден";
                }
                if (!TelegramCoverage.IsApplied(root))
                {
                    return "блок не включён";
                }
                TelegramCoverageResult coverage = TelegramCoverage.Apply(root, true);
                if (!coverage.Ok)
                {
                    return "ошибка: " + (coverage.Error ?? "неизвестно");
                }
                if (!coverage.Changed)
                {
                    return "без изменений";
                }
                if (ZapretService.GetStatus().State != ZapretState.Running)
                {
                    return "список обновлён, служба остановлена";
                }
                DateTime last;
                if (DataValidation.TryParseUtc(
                        settings.LastCoverageRestartUtc, out last) &&
                    DateTime.UtcNow - last < TimeSpan.FromHours(CoverageRestartHours))
                {
                    return "список обновлён, перезапуск отложен";
                }
                if (!OperationGuard.TryEnter())
                {
                    return "список обновлён, служба занята";
                }
                try
                {
                    if (ZapretService.GetStatus().State == ZapretState.Running)
                    {
                        ZapretService.RestartIfRunning();
                        settings.LastCoverageRestartUtc = DateTime.UtcNow.ToString("o");
                        Settings.Save();
                        Log("coverage restarted service, fronts=" +
                            DataStore.Fronts.Length);
                        return "список обновлён, служба перезапущена";
                    }
                }
                finally
                {
                    OperationGuard.Exit();
                }
                return "список обновлён";
            }
            catch (Exception ex)
            {
                return "ошибка: " + Short(ex);
            }
        }

        internal static string StatusText()
        {
            DataDocument document = DataStore.Current;
            StringBuilder text = new StringBuilder();
            text.AppendLine("schema=" + document.schema);
            text.AppendLine("seq=" + document.seq);
            text.AppendLine("source=" + DataStore.Source);
            text.AppendLine("generated_utc=" + (document.generated_utc ?? string.Empty));
            text.AppendLine("valid_until=" + (document.valid_until ?? string.Empty));
            text.AppendLine("fronts=" + document.telegram.fronts.Length);
            text.AppendLine("dc=" + document.telegram.dc.Length);
            text.AppendLine("doh=" + document.doh.Length);
            text.AppendLine("payload=" + (document.payload == null
                ? string.Empty
                : document.payload.version));
            text.AppendLine("cache=" + (File.Exists(AppPaths.DataCachePath)
                ? "present"
                : "absent"));
            if (!string.IsNullOrEmpty(DataStore.CacheError))
            {
                text.AppendLine("cache_error=" + DataStore.CacheError);
            }
            return text.ToString().TrimEnd();
        }

        internal static void Log(string message)
        {
            try
            {
                AppPaths.EnsureDirectory(AppPaths.LogDirectory);
                File.AppendAllText(
                    Path.Combine(AppPaths.LogDirectory, "data-update.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message +
                    Environment.NewLine,
                    new UTF8Encoding(true));
            }
            catch (Exception)
            {
            }
        }

        private static void RaiseCompleted(DataUpdateResult result)
        {
            Action<DataUpdateResult> handler = Completed;
            if (handler == null)
            {
                return;
            }
            try
            {
                handler(result);
            }
            catch (Exception)
            {
            }
        }

        private static string Short(Exception error)
        {
            if (error == null)
            {
                return "неизвестная ошибка";
            }
            string text = error.Message;
            if (text == null)
            {
                text = error.GetType().Name;
            }
            text = text.Replace("\r", " ").Replace("\n", " ").Trim();
            return text.Length > 160 ? text.Substring(0, 160) : text;
        }
    }
}
