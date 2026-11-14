using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ZapretControl
{
    public sealed class IpsetUpdateResult
    {
        public int Lines { get; private set; }
        public string State { get; private set; }
        public string BackupPath { get; private set; }
        public string Warning { get; private set; }

        public IpsetUpdateResult(int lines, string state, string backupPath, string warning)
        {
            Lines = lines;
            State = state;
            BackupPath = backupPath;
            Warning = warning;
        }
    }

    // Native equivalent of the IPSet Filter switch in service.bat.
    public static class IpsetManager
    {
        public const string StateLoaded = "loaded";
        public const string StateNone = "none";
        public const string StateAny = "any";
        public const string Sentinel = "203.0.113.113/32";

        public static string GetListPath(string payloadRoot)
        {
            return Path.Combine(payloadRoot ?? string.Empty, @"lists\ipset-all.txt");
        }

        public static string GetBackupPath(string payloadRoot)
        {
            return GetListPath(payloadRoot) + ".backup";
        }

        public static string GetState(string payloadRoot)
        {
            try
            {
                string path = GetListPath(payloadRoot);
                if (!File.Exists(path))
                {
                    return StateAny;
                }
                int lines = 0;
                string text = File.ReadAllText(path, Encoding.UTF8);
                foreach (string line in text.Replace("\r\n", "\n").Split('\n'))
                {
                    if (line.Length > 0)
                    {
                        lines++;
                    }
                }
                if (lines == 0)
                {
                    return StateAny;
                }
                if (text.IndexOf(Sentinel, StringComparison.Ordinal) >= 0)
                {
                    return StateNone;
                }
                return StateLoaded;
            }
            catch (Exception)
            {
                return StateAny;
            }
        }

        public static string GetDisplay(string state)
        {
            if (state == StateLoaded)
            {
                return "загружен";
            }
            if (state == StateNone)
            {
                return "отключён";
            }
            return "любой (пустой список)";
        }

        public static string SetState(string payloadRoot, string target)
        {
            string normalized = (target ?? string.Empty).Trim().ToLowerInvariant();
            if (normalized != StateLoaded && normalized != StateNone && normalized != StateAny)
            {
                throw new ArgumentException("Неизвестное состояние IPSet: " + target, "target");
            }

            string path = GetListPath(payloadRoot);
            string backup = GetBackupPath(payloadRoot);
            string current = GetState(payloadRoot);
            AppPaths.EnsureDirectory(Path.GetDirectoryName(path));

            if (normalized == StateLoaded)
            {
                if (current == StateLoaded)
                {
                    return StateLoaded;
                }
                if (!File.Exists(backup))
                {
                    throw new InvalidOperationException(
                        "Нет резервной копии списка. Сначала обновите IPSet.");
                }
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                File.Move(backup, path);
                return StateLoaded;
            }

            if (normalized == StateNone)
            {
                if (current == StateLoaded && File.Exists(path))
                {
                    if (File.Exists(backup))
                    {
                        File.Delete(backup);
                    }
                    File.Move(path, backup);
                }
                File.WriteAllText(path, Sentinel + Environment.NewLine, new UTF8Encoding(false));
                return StateNone;
            }

            if (current == StateLoaded && File.Exists(path))
            {
                if (File.Exists(backup))
                {
                    File.Delete(backup);
                }
                File.Move(path, backup);
            }
            File.WriteAllText(path, string.Empty, new UTF8Encoding(false));
            return StateAny;
        }

        public static IpsetUpdateResult Update(
            string payloadRoot,
            Downloader.ProgressHandler progress)
        {
            string temporary = Path.Combine(
                AppPaths.StagingDirectory,
                "ipset-" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                AppPaths.EnsureDirectory(AppPaths.StagingDirectory);
                Downloader.DownloadFile(
                    AppInfo.FlowsealIpsetUrl, temporary, 60000, 20971520, progress);
                List<string> lines = ReadValidatedLines(temporary);
                if (lines.Count < 100)
                {
                    throw new InvalidOperationException(
                        "Скачанный список IPSet слишком мал (" + lines.Count + " строк).");
                }

                string currentState = GetState(payloadRoot);
                string path = GetListPath(payloadRoot);
                string backup = GetBackupPath(payloadRoot);
                AppPaths.EnsureDirectory(Path.GetDirectoryName(path));
                string warning = null;

                if (currentState == StateNone)
                {
                    if (File.Exists(backup))
                    {
                        File.Delete(backup);
                    }
                    File.Copy(temporary, backup, true);
                    File.WriteAllText(path, Sentinel + Environment.NewLine, new UTF8Encoding(false));
                    warning = "Режим IPSet «отключён» сохранён; новый список лежит в резервной копии.";
                }
                else
                {
                    if (currentState == StateLoaded && File.Exists(path))
                    {
                        if (File.Exists(backup))
                        {
                            File.Delete(backup);
                        }
                        File.Copy(path, backup, true);
                    }
                    File.Copy(temporary, path, true);
                }

                return new IpsetUpdateResult(lines.Count, GetState(payloadRoot), backup, warning);
            }
            finally
            {
                PayloadManager.TryDelete(temporary);
            }
        }

        private static List<string> ReadValidatedLines(string path)
        {
            List<string> result = new List<string>();
            string text = File.ReadAllText(path, Encoding.UTF8);
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            Regex address = new Regex(@"^[0-9A-Fa-f:.]+(/[0-9]{1,3})?$");
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }
                if (!address.IsMatch(line))
                {
                    throw new InvalidOperationException(
                        "Список IPSet содержит недопустимую строку: " + Shorten(line));
                }
                result.Add(line);
            }
            return result;
        }

        private static string Shorten(string value)
        {
            return value.Length <= 80 ? value : value.Substring(0, 80);
        }
    }
}
