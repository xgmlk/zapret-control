using System;
using System.Collections.Generic;
using System.IO;

namespace ZapretControl
{
    public sealed class FakeFile
    {
        public string Name { get; private set; }
        public string Path { get; private set; }
        public string Hash { get; private set; }
        public long Size { get; private set; }

        public FakeFile(string name, string path, string hash, long size)
        {
            Name = name;
            Path = path;
            Hash = hash;
            Size = size;
        }

        public override string ToString()
        {
            return Name;
        }
    }

    // Native equivalent of "Replace active fakes" in service.bat.
    public static class FakesManager
    {
        public const string DiscordKind = "discord";
        public const string GameKind = "game";
        public const string DiscordActive = "ACTIVE_DISCORD_UDP.bin";
        public const string GameActive = "ACTIVE_GAME_UDP.bin";

        public static string GetActiveName(string kind)
        {
            return NormalizeKind(kind) == DiscordKind ? DiscordActive : GameActive;
        }

        public static List<FakeFile> List(string payloadRoot)
        {
            List<FakeFile> result = new List<FakeFile>();
            string bin = System.IO.Path.Combine(payloadRoot ?? string.Empty, "bin");
            if (!Directory.Exists(bin))
            {
                return result;
            }
            try
            {
                List<string> names = new List<string>();
                foreach (string file in Directory.GetFiles(bin, "*.bin"))
                {
                    string name = System.IO.Path.GetFileName(file);
                    if (name.StartsWith("ACTIVE_", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    names.Add(file);
                }
                names.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (string file in names)
                {
                    result.Add(new FakeFile(
                        System.IO.Path.GetFileName(file),
                        file,
                        HashUtil.ComputeSha256(file),
                        new FileInfo(file).Length));
                }
            }
            catch (Exception)
            {
            }
            return result;
        }

        public static string DescribeActive(string payloadRoot, string kind)
        {
            string active = System.IO.Path.Combine(
                payloadRoot ?? string.Empty, "bin", GetActiveName(kind));
            if (!File.Exists(active))
            {
                return "не найден";
            }
            string hash = HashUtil.ComputeSha256(active);
            foreach (FakeFile fake in List(payloadRoot))
            {
                if (string.Equals(fake.Hash, hash, StringComparison.OrdinalIgnoreCase))
                {
                    return fake.Name;
                }
            }
            return "пользовательский файл";
        }

        public static FakeFile Find(string payloadRoot, string fileName)
        {
            foreach (FakeFile fake in List(payloadRoot))
            {
                if (fake.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase))
                {
                    return fake;
                }
            }
            return null;
        }

        public static string Replace(string payloadRoot, string kind, string sourceName)
        {
            string normalized = NormalizeKind(kind);
            FakeFile source = Find(payloadRoot, sourceName);
            if (source == null)
            {
                throw new InvalidOperationException("Файл фейка не найден: " + sourceName);
            }
            string bin = System.IO.Path.Combine(payloadRoot, "bin");
            string active = System.IO.Path.Combine(bin, GetActiveName(normalized));
            string backupDirectory = System.IO.Path.Combine(AppPaths.BackupDirectory, "fakes");
            string backup = null;
            if (File.Exists(active))
            {
                AppPaths.EnsureDirectory(backupDirectory);
                backup = System.IO.Path.Combine(
                    backupDirectory,
                    GetActiveName(normalized) + ".bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                File.Copy(active, backup, true);
            }
            string temporary = active + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.Copy(source.Path, temporary, true);
                if (File.Exists(active))
                {
                    File.Delete(active);
                }
                File.Move(temporary, active);
            }
            finally
            {
                PayloadManager.TryDelete(temporary);
            }
            return backup;
        }

        private static string NormalizeKind(string kind)
        {
            string value = (kind ?? string.Empty).Trim().ToLowerInvariant();
            if (value == DiscordKind || value == "discord_udp" || value == "1")
            {
                return DiscordKind;
            }
            if (value == GameKind || value == "game_udp" || value == "2")
            {
                return GameKind;
            }
            throw new ArgumentException("Неизвестный тип фейка: " + kind, "kind");
        }
    }
}
