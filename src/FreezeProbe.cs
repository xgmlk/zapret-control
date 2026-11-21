using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace ZapretControl
{
    internal sealed class FreezeTarget
    {
        public string Id { get; private set; }
        public string Provider { get; private set; }
        public string Country { get; private set; }
        public string Host { get; private set; }

        public FreezeTarget(string id, string provider, string country, string host)
        {
            Id = id ?? string.Empty;
            Provider = provider ?? string.Empty;
            Country = country ?? string.Empty;
            Host = host ?? string.Empty;
        }
    }

    internal sealed class FreezeVariantResult
    {
        public string Label { get; private set; }
        public string Status { get; private set; }
        public string Code { get; private set; }
        public long UploadBytes { get; private set; }
        public long DownloadBytes { get; private set; }
        public double TimeTotal { get; private set; }
        public int ExitCode { get; private set; }
        public string Detail { get; private set; }

        public FreezeVariantResult(
            string label,
            string status,
            string code,
            long uploadBytes,
            long downloadBytes,
            double timeTotal,
            int exitCode,
            string detail)
        {
            Label = label ?? string.Empty;
            Status = status ?? string.Empty;
            Code = code ?? string.Empty;
            UploadBytes = uploadBytes;
            DownloadBytes = downloadBytes;
            TimeTotal = timeTotal;
            ExitCode = exitCode;
            Detail = detail ?? string.Empty;
        }
    }

    internal sealed class FreezeTargetResult
    {
        public FreezeTarget Target { get; private set; }
        public List<FreezeVariantResult> Variants { get; private set; }

        public FreezeTargetResult(FreezeTarget target)
        {
            Target = target;
            Variants = new List<FreezeVariantResult>();
        }

        public bool LikelyBlocked
        {
            get
            {
                foreach (FreezeVariantResult variant in Variants)
                {
                    if (variant.Status == FreezeProbe.StatusLikelyBlocked)
                    {
                        return true;
                    }
                }
                return false;
            }
        }
    }

    internal sealed class FreezeReport
    {
        public bool CurlAvailable { get; internal set; }
        public bool Checked { get; internal set; }
        public bool SuiteOffline { get; internal set; }
        public List<FreezeTargetResult> Targets { get; private set; }
        public List<string> Errors { get; private set; }
        internal object Sync = new object();
        public int ExpectedTargets { get; internal set; }
        public int VariantsCount { get; internal set; }
        public int TargetsWithOk { get; internal set; }
        public int OkCount { get; internal set; }
        public int FailCount { get; internal set; }
        public int UnsupportedCount { get; internal set; }
        public int BlockedCount { get; internal set; }
        public long ElapsedMs { get; internal set; }
        public string Detail { get; internal set; }

        public FreezeReport()
        {
            Targets = new List<FreezeTargetResult>();
            Errors = new List<string>();
            Detail = string.Empty;
        }

        public bool Ok
        {
            get
            {
                // Positive evidence is required: an all-FAIL stage must not be
                // reported as "no freeze".
                return Checked &&
                       ExpectedTargets > 0 &&
                       Targets.Count == ExpectedTargets &&
                       VariantsCount == ExpectedTargets * 3 &&
                       TargetsWithOk == ExpectedTargets &&
                       BlockedCount == 0;
            }
        }
    }

    // DPI freeze stage: POST a 64 KB random body with "Range: 0-65535" and look
    // for the 16-20 KB freeze signature. The target list and detection follow
    // hyperion-cs/dpi-checkers (Apache-2.0), the same reference as
    // utils\test zapret.ps1; the built-in list keeps the stage usable offline.
    internal static class FreezeProbe
    {
        internal const string SuiteUrl =
            "https://hyperion-cs.github.io/dpi-checkers/ru/tcp-16-20/suite.v2.json";
        internal const string StatusOk = "OK";
        internal const string StatusFail = "FAIL";
        internal const string StatusUnsupported = "UNSUPPORTED";
        internal const string StatusLikelyBlocked = "LIKELY_BLOCKED";

        private const int MaxTargets = 64;
        private const int PayloadBytes = 65536;
        private const int CurlTimeoutMs = 15000;

        private static readonly Regex ResultLine = new Regex(
            @"^(?<code>\d{3})\s+(?<up>\d+)\s+(?<down>\d+)\s+(?<time>[\d.,]+)$",
            RegexOptions.Compiled);

        private static readonly string[,] BuiltInTargets = new string[,]
        {
            { "hyperion-cs.github.io", "Self check", "" },
            { "www.mobil.com.se", "Akamai", "PL" },
            { "cdn.apple-mapkit.com", "Akamai", "SE" },
            { "amplifon.com", "AWS", "DE" },
            { "optout.aboutads.info", "AWS", "US" },
            { "cdn.eso.org", "CDN77", "US" },
            { "go.coveo.com", "Cloudflare", "CA" },
            { "gcore.com", "Gcore", "LU" }
        };

        internal static string FindCurl()
        {
            try
            {
                string system = Environment.GetFolderPath(
                    Environment.SpecialFolder.System);
                if (string.IsNullOrEmpty(system))
                {
                    return null;
                }
                string path = Path.Combine(system, "curl.exe");
                return File.Exists(path) ? path : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        internal static List<FreezeTarget> LoadSuite(int timeoutMs, out bool offline)
        {
            List<FreezeTarget> targets = new List<FreezeTarget>();
            YouTubeHttp.EnableTls();
            try
            {
                string text;
                int code;
                string error;
                if (YouTubeHttp.GetText(
                        SuiteUrl, timeoutMs, out text, out code, out error) &&
                    !string.IsNullOrEmpty(text))
                {
                    targets = ParseSuite(text);
                }
            }
            catch (Exception)
            {
            }
            if (targets.Count == 0)
            {
                offline = true;
                targets = BuildBuiltIn();
            }
            else
            {
                offline = false;
            }
            return targets;
        }

        internal static string CreatePayloadFile()
        {
            try
            {
                return CreatePayloadFile(AppPaths.StagingDirectory);
            }
            catch (Exception)
            {
                // The CLI test may run without admin and cannot write to
                // ProgramData; a random file in the user temp folder is enough.
                return CreatePayloadFile(Path.GetTempPath());
            }
        }

        private static string CreatePayloadFile(string directory)
        {
            AppPaths.EnsureDirectory(directory);
            string path = Path.Combine(
                directory,
                "ZapretControl-freeze-" + Guid.NewGuid().ToString("N") + ".bin");
            byte[] data = new byte[PayloadBytes];
            try
            {
                using (RandomNumberGenerator random = RandomNumberGenerator.Create())
                {
                    random.GetBytes(data);
                }
                using (FileStream stream = new FileStream(
                    path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(data, 0, data.Length);
                }
                return path;
            }
            catch (Exception)
            {
                TryDelete(path);
                throw;
            }
        }

        internal static void TryDelete(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
            }
        }

        internal static bool IsLikelyBlocked(
            int exitCode,
            long uploadBytes,
            long downloadBytes,
            double timeTotal,
            int timeoutSeconds)
        {
            return uploadBytes > 0 &&
                   downloadBytes == 0 &&
                   timeTotal >= timeoutSeconds &&
                   exitCode != 0;
        }

        internal static FreezeReport Run(
            List<FreezeTarget> targets,
            string curlPath,
            string payloadFile,
            NetworkGate gate,
            int timeoutSeconds,
            bool suiteOffline)
        {
            Stopwatch watch = Stopwatch.StartNew();
            FreezeReport report = new FreezeReport();
            report.SuiteOffline = suiteOffline;
            report.CurlAvailable = !string.IsNullOrEmpty(curlPath) &&
                                   File.Exists(curlPath);
            if (!report.CurlAvailable)
            {
                report.Checked = false;
                report.Detail = "curl.exe не найден: DPI-заморозка не проверена";
                watch.Stop();
                report.ElapsedMs = watch.ElapsedMilliseconds;
                return report;
            }
            if (targets == null || targets.Count == 0)
            {
                report.Checked = false;
                report.Detail = "список целей DPI пуст";
                watch.Stop();
                report.ElapsedMs = watch.ElapsedMilliseconds;
                return report;
            }

            string ownedPayload = null;
            if (string.IsNullOrEmpty(payloadFile) || !File.Exists(payloadFile))
            {
                try
                {
                    ownedPayload = CreatePayloadFile();
                    payloadFile = ownedPayload;
                }
                catch (Exception ex)
                {
                    report.Checked = false;
                    report.Detail = "не удалось создать файл 64 КБ: " + ex.Message;
                    watch.Stop();
                    report.ElapsedMs = watch.ElapsedMilliseconds;
                    return report;
                }
            }

            try
            {
                report.Checked = true;
                report.ExpectedTargets = targets.Count;
                FreezeTargetResult[] results = new FreezeTargetResult[targets.Count];
                Action[] actions = new Action[targets.Count];
                for (int i = 0; i < targets.Count; i++)
                {
                    int index = i;
                    actions[i] = delegate()
                    {
                        results[index] = ProbeTarget(
                            targets[index], curlPath, payloadFile, gate,
                            timeoutSeconds);
                    };
                }
                ParallelWork.RunAll(actions, delegate(Exception ex)
                {
                    lock (report.Sync)
                    {
                        report.FailCount++;
                        if (report.Errors.Count < 3)
                        {
                            report.Errors.Add(
                                ex.GetType().Name + ": " + ex.Message);
                        }
                    }
                });
                foreach (FreezeTargetResult target in results)
                {
                    if (target == null)
                    {
                        continue;
                    }
                    report.Targets.Add(target);
                    report.VariantsCount += target.Variants.Count;
                    bool targetOk = false;
                    foreach (FreezeVariantResult variant in target.Variants)
                    {
                        if (variant.Status == StatusLikelyBlocked)
                        {
                            report.BlockedCount++;
                        }
                        else if (variant.Status == StatusUnsupported)
                        {
                            report.UnsupportedCount++;
                        }
                        else if (variant.Status == StatusFail)
                        {
                            report.FailCount++;
                        }
                        else
                        {
                            report.OkCount++;
                            targetOk = true;
                        }
                    }
                    if (targetOk)
                    {
                        report.TargetsWithOk++;
                    }
                }
                report.Detail = BuildDetail(report);
                watch.Stop();
                report.ElapsedMs = watch.ElapsedMilliseconds;
                return report;
            }
            finally
            {
                TryDelete(ownedPayload);
            }
        }

        private static FreezeTargetResult ProbeTarget(
            FreezeTarget target,
            string curlPath,
            string payloadFile,
            NetworkGate gate,
            int timeoutSeconds)
        {
            FreezeTargetResult result = new FreezeTargetResult(target);
            result.Variants.Add(RunVariant(
                target.Host, "HTTP/1.1", " --http1.1",
                curlPath, payloadFile, gate, timeoutSeconds));
            result.Variants.Add(RunVariant(
                target.Host, "TLS1.2", " --tlsv1.2 --tls-max 1.2",
                curlPath, payloadFile, gate, timeoutSeconds));
            result.Variants.Add(RunVariant(
                target.Host, "TLS1.3", " --tlsv1.3 --tls-max 1.3",
                curlPath, payloadFile, gate, timeoutSeconds));
            return result;
        }

        private static FreezeVariantResult RunVariant(
            string host,
            string label,
            string variantArguments,
            string curlPath,
            string payloadFile,
            NetworkGate gate,
            int timeoutSeconds)
        {
            int exitCode = -1;
            int timeout = timeoutSeconds < 1
                ? 1
                : (timeoutSeconds > 30 ? 30 : timeoutSeconds);
            string stdout = string.Empty;
            string stderr = string.Empty;
            gate.Enter();
            try
            {
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = curlPath;
                info.Arguments =
                    "--range 0-65535" +
                    " -m " + timeout +
                    " --connect-timeout 3" +
                    " -w \"%{http_code} %{size_upload} %{size_download} %{time_total}\"" +
                    " -o NUL -X POST --data-binary @\"" + payloadFile + "\"" +
                    " -s" + variantArguments +
                    " https://" + host;
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                info.RedirectStandardOutput = true;
                info.RedirectStandardError = true;
                using (Process process = Process.Start(info))
                {
                    if (process == null)
                    {
                        stderr = "curl.exe не запустился";
                    }
                    else
                    {
                        StringBuilder output = new StringBuilder();
                        StringBuilder errors = new StringBuilder();
                        process.OutputDataReceived += delegate(
                            object sender, DataReceivedEventArgs e)
                        {
                            if (e.Data != null)
                            {
                                lock (output)
                                {
                                    output.AppendLine(e.Data);
                                }
                            }
                        };
                        process.ErrorDataReceived += delegate(
                            object sender, DataReceivedEventArgs e)
                        {
                            if (e.Data != null)
                            {
                                lock (errors)
                                {
                                    errors.AppendLine(e.Data);
                                }
                            }
                        };
                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();
                        if (!process.WaitForExit(CurlTimeoutMs))
                        {
                            try
                            {
                                process.Kill();
                            }
                            catch (Exception)
                            {
                            }
                            process.WaitForExit(2000);
                            exitCode = -2;
                        }
                        else
                        {
                            // The parameterless overload flushes asynchronous
                            // output handlers; otherwise stderr can be lost.
                            process.WaitForExit();
                            exitCode = process.ExitCode;
                        }
                        lock (output)
                        {
                            stdout = output.ToString();
                        }
                        lock (errors)
                        {
                            stderr = errors.ToString();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                stderr = ex.GetType().Name + ": " + ex.Message;
            }
            finally
            {
                gate.Exit();
            }

            string code;
            long uploadBytes;
            long downloadBytes;
            double timeTotal;
            ParseCurlResult(stdout, out code, out uploadBytes, out downloadBytes, out timeTotal);
            string status;
            if (IsUnsupported(exitCode, stderr))
            {
                status = StatusUnsupported;
            }
            else if (exitCode != 0 || code == "ERR" || code == "NA")
            {
                status = StatusFail;
            }
            else
            {
                status = StatusOk;
            }
            if (IsLikelyBlocked(
                exitCode, uploadBytes, downloadBytes, timeTotal, timeout))
            {
                status = StatusLikelyBlocked;
            }
            string detail = label + ": " + status;
            if (status != StatusOk)
            {
                detail += " (" + code + ", up " + uploadBytes +
                          ", down " + downloadBytes + ", " +
                          timeTotal.ToString("0.0", CultureInfo.InvariantCulture) + " с)";
                if (!string.IsNullOrEmpty(stderr))
                {
                    detail += " " + Short(stderr);
                }
            }
            return new FreezeVariantResult(
                label, status, code, uploadBytes, downloadBytes,
                timeTotal, exitCode, detail);
        }

        private static void ParseCurlResult(
            string stdout,
            out string code,
            out long uploadBytes,
            out long downloadBytes,
            out double timeTotal)
        {
            code = "NA";
            uploadBytes = 0;
            downloadBytes = 0;
            timeTotal = -1;
            string text = (stdout ?? string.Empty).Trim();
            int lastBreak = text.LastIndexOfAny(new char[] { '\r', '\n' });
            if (lastBreak >= 0)
            {
                text = text.Substring(lastBreak + 1).Trim();
            }
            Match match = ResultLine.Match(text);
            if (!match.Success)
            {
                if (text.Length > 0)
                {
                    code = "ERR";
                }
                return;
            }
            code = match.Groups["code"].Value;
            long.TryParse(match.Groups["up"].Value, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out uploadBytes);
            long.TryParse(match.Groups["down"].Value, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out downloadBytes);
            double.TryParse(
                match.Groups["time"].Value.Replace(',', '.'),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out timeTotal);
        }

        private static bool IsUnsupported(int exitCode, string text)
        {
            if (exitCode == 35)
            {
                return true;
            }
            string value = (text ?? string.Empty).ToLowerInvariant();
            return value.IndexOf("not supported", StringComparison.Ordinal) >= 0 ||
                   value.IndexOf("unsupported protocol", StringComparison.Ordinal) >= 0 ||
                   value.IndexOf("unsupported option", StringComparison.Ordinal) >= 0 ||
                   value.IndexOf("unsupported feature", StringComparison.Ordinal) >= 0 ||
                   value.IndexOf("unrecognized option", StringComparison.Ordinal) >= 0 ||
                   value.IndexOf("unknown option", StringComparison.Ordinal) >= 0 ||
                   value.IndexOf("schannel", StringComparison.Ordinal) >= 0 ||
                   value.IndexOf("does not support", StringComparison.Ordinal) >= 0;
        }

        private static string BuildDetail(FreezeReport report)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("целей ").Append(report.Targets.Count);
            if (report.ExpectedTargets != report.Targets.Count)
            {
                builder.Append(" из ").Append(report.ExpectedTargets);
            }
            builder.Append(" (с OK ").Append(report.TargetsWithOk).Append(")");
            builder.Append(" · OK ").Append(report.OkCount);
            builder.Append(" · блок ").Append(report.BlockedCount);
            builder.Append(" · ошибок ").Append(report.FailCount);
            if (report.UnsupportedCount > 0)
            {
                builder.Append(" · без поддержки ").Append(report.UnsupportedCount);
            }
            if (report.SuiteOffline)
            {
                builder.Append(" · список оффлайн");
            }
            if (report.Errors.Count > 0)
            {
                builder.Append(" · сбои: ");
                for (int i = 0; i < report.Errors.Count; i++)
                {
                    if (i > 0)
                    {
                        builder.Append("; ");
                    }
                    builder.Append(Short(report.Errors[i]));
                }
            }
            return builder.ToString();
        }

        private static List<FreezeTarget> BuildBuiltIn()
        {
            List<FreezeTarget> targets = new List<FreezeTarget>();
            for (int i = 0; i < BuiltInTargets.GetLength(0); i++)
            {
                targets.Add(new FreezeTarget(
                    "BUILTIN-" + (i + 1),
                    BuiltInTargets[i, 1],
                    BuiltInTargets[i, 2],
                    BuiltInTargets[i, 0]));
            }
            return targets;
        }

        private static List<FreezeTarget> ParseSuite(string text)
        {
            List<FreezeTarget> targets = new List<FreezeTarget>();
            HashSet<string> seen = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = 2 * 1024 * 1024;
            object root = serializer.DeserializeObject(text);
            object[] items = root as object[];
            if (items == null)
            {
                return targets;
            }
            foreach (object item in items)
            {
                if (targets.Count >= MaxTargets)
                {
                    break;
                }
                Dictionary<string, object> entry =
                    item as Dictionary<string, object>;
                if (entry == null)
                {
                    continue;
                }
                string host = GetString(entry, "host");
                if (!IsValidHost(host) || !seen.Add(host))
                {
                    continue;
                }
                string id = GetString(entry, "id");
                string provider = GetString(entry, "provider");
                string country = GetString(entry, "country");
                targets.Add(new FreezeTarget(
                    string.IsNullOrEmpty(id) ? "SUITE" : id,
                    provider,
                    country,
                    host));
            }
            return targets;
        }

        private static bool IsValidHost(string host)
        {
            if (string.IsNullOrEmpty(host) || host.Length < 4 || host.Length > 253)
            {
                return false;
            }
            if (host.IndexOf(' ') >= 0 || host.IndexOf('/') >= 0 ||
                host.IndexOf('\\') >= 0 || host.IndexOf(':') >= 0)
            {
                return false;
            }
            if (host[0] == '.' || host[host.Length - 1] == '.')
            {
                return false;
            }
            if (host.IndexOf("..", StringComparison.Ordinal) >= 0)
            {
                return false;
            }
            bool dot = false;
            for (int i = 0; i < host.Length; i++)
            {
                char c = host[i];
                if (c == '.')
                {
                    dot = true;
                    continue;
                }
                if (!char.IsLetterOrDigit(c) && c != '-')
                {
                    return false;
                }
            }
            return dot;
        }

        private static string GetString(
            Dictionary<string, object> entry, string key)
        {
            object value;
            if (entry.TryGetValue(key, out value))
            {
                return value as string;
            }
            return null;
        }

        private static string Short(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }
            string value = text.Replace("\r", " ").Replace("\n", " ").Trim();
            return value.Length > 160 ? value.Substring(0, 160) : value;
        }
    }
}
