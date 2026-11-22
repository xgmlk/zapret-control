using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ZapretControl
{
    internal sealed class TelegramCoverageResult
    {
        internal bool Ok;
        internal bool Changed;
        internal string Error;
        internal string Path;
        internal int Lines;
    }

    // Manages a marked block in the user-owned list-general-user.txt. The
    // Flowseal strategies include that list with TCP 80/443, so the proxy's
    // own upstream TLS (web.telegram.org and the Cloudflare fronts) gets the
    // strategy's desync without editing any Flowseal file: invariant 8 of the
    // passport stays intact and the list survives payload updates
    // (invariant 11). Markers are .invalid hostnames, never real traffic.
    internal static class TelegramCoverage
    {
        private const string StartMarker =
            "zapretcontrol-telegram-start.invalid";
        private const string EndMarker =
            "zapretcontrol-telegram-end.invalid";

        internal static string ListPath(string payloadRoot)
        {
            return Path.Combine(payloadRoot, "lists", "list-general-user.txt");
        }

        internal static string[] Domains()
        {
            List<string> domains = new List<string>();
            for (int dc = 1; dc <= 5; dc++)
            {
                domains.Add("kws" + dc + ".web.telegram.org");
                domains.Add("kws" + dc + "-1.web.telegram.org");
            }
            string[] fronts = TgFronts.AllBaseDomains;
            for (int front = 0; front < fronts.Length; front++)
            {
                for (int dc = 1; dc <= 5; dc++)
                {
                    domains.Add("kws" + dc + "." + fronts[front]);
                }
            }
            return domains.ToArray();
        }

        internal static bool IsApplied(string payloadRoot)
        {
            try
            {
                string path = ListPath(payloadRoot);
                if (!File.Exists(path))
                {
                    return false;
                }
                return File.ReadAllText(path, Encoding.UTF8)
                    .IndexOf(StartMarker, StringComparison.Ordinal) >= 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal static TelegramCoverageResult Apply(
            string payloadRoot, bool enabled)
        {
            TelegramCoverageResult result = new TelegramCoverageResult();
            result.Path = ListPath(payloadRoot ?? string.Empty);
            try
            {
                if (string.IsNullOrEmpty(payloadRoot) ||
                    !Directory.Exists(Path.Combine(payloadRoot, "lists")))
                {
                    result.Error = "payload не найден";
                    return result;
                }
                string path = result.Path;
                string existing = File.Exists(path)
                    ? File.ReadAllText(path, Encoding.UTF8)
                    : string.Empty;
                string normalized = existing.Replace("\r\n", "\n");
                string cleaned = RemoveBlock(normalized);

                string updated;
                if (enabled)
                {
                    StringBuilder builder = new StringBuilder(
                        cleaned.TrimEnd());
                    if (builder.Length > 0)
                    {
                        builder.Append('\n');
                    }
                    builder.Append(StartMarker).Append('\n');
                    string[] domains = Domains();
                    for (int i = 0; i < domains.Length; i++)
                    {
                        builder.Append(domains[i]).Append('\n');
                    }
                    builder.Append(EndMarker).Append('\n');
                    updated = builder.ToString();
                    result.Lines = domains.Length;
                }
                else
                {
                    updated = cleaned.TrimEnd();
                    if (updated.Length > 0)
                    {
                        updated += "\n";
                    }
                }

                string crlf = updated.Replace("\n", Environment.NewLine);
                if (crlf == existing)
                {
                    result.Ok = true;
                    return result;
                }
                if (File.Exists(path) && AppPaths.IsReparsePoint(path))
                {
                    result.Error = "файл списка — reparse point";
                    return result;
                }
                if (!AtomicFile.WriteAllText(path, crlf))
                {
                    result.Error = "не удалось записать список";
                    return result;
                }
                result.Ok = true;
                result.Changed = true;
                return result;
            }
            catch (Exception ex)
            {
                result.Error = ex.GetType().Name + ": " + ex.Message;
                return result;
            }
        }

        private static string RemoveBlock(string text)
        {
            int start = text.IndexOf(StartMarker, StringComparison.Ordinal);
            if (start < 0)
            {
                return text;
            }
            int lineStart = text.LastIndexOf('\n', start);
            lineStart = lineStart < 0 ? 0 : lineStart + 1;
            int end = text.IndexOf(EndMarker, start, StringComparison.Ordinal);
            if (end < 0)
            {
                return text.Substring(0, lineStart);
            }
            int lineEnd = text.IndexOf('\n', end);
            int after = lineEnd < 0 ? text.Length : lineEnd + 1;
            return text.Substring(0, lineStart) + text.Substring(after);
        }
    }
}
