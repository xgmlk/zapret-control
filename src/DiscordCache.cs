using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace ZapretControl
{
    public sealed class DiscordCacheResult
    {
        public List<string> Messages { get; private set; }
        public int DeletedDirectories { get; private set; }

        public DiscordCacheResult()
        {
            Messages = new List<string>();
            DeletedDirectories = 0;
        }

        public bool Found
        {
            get { return Messages.Count > 0; }
        }

        public void AddDeleted()
        {
            DeletedDirectories++;
        }
    }

    // Native equivalent of the Discord cache cleanup in service.bat diagnostics.
    public static class DiscordCache
    {
        private sealed class Variant
        {
            public string Process;
            public string Display;
            public string Folder;

            public Variant(string process, string display, string folder)
            {
                Process = process;
                Display = display;
                Folder = folder;
            }
        }

        public static DiscordCacheResult Clear()
        {
            DiscordCacheResult result = new DiscordCacheResult();
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            Variant[] variants = new Variant[]
            {
                new Variant("Discord", "Discord", "discord"),
                new Variant("DiscordPTB", "Discord PTB", "discordptb"),
                new Variant("DiscordCanary", "Discord Canary", "discordcanary"),
                new Variant("DiscordDevelopment", "Discord Development", "discorddevelopment")
            };

            foreach (Variant variant in variants)
            {
                string directory = Path.Combine(appData, variant.Folder);
                if (!Directory.Exists(directory))
                {
                    continue;
                }
                result.Messages.Add("Найден " + variant.Display + ".");
                CloseProcess(variant.Process, variant.Display, result);
                foreach (string name in new string[] { "Cache", "Code Cache", "GPUCache" })
                {
                    string cache = Path.Combine(directory, name);
                    if (!Directory.Exists(cache))
                    {
                        continue;
                    }
                    try
                    {
                        Directory.Delete(cache, true);
                        result.AddDeleted();
                        result.Messages.Add("Удалено: " + cache);
                    }
                    catch (Exception ex)
                    {
                        result.Messages.Add("Не удалось удалить " + cache + ": " + ex.Message);
                    }
                }
            }
            if (result.Messages.Count == 0)
            {
                result.Messages.Add("Установки Discord не найдены.");
            }
            return result;
        }

        private static void CloseProcess(
            string processName, string display, DiscordCacheResult result)
        {
            try
            {
                Process[] processes = Process.GetProcessesByName(processName);
                if (processes.Length == 0)
                {
                    return;
                }
                foreach (Process process in processes)
                {
                    try
                    {
                        process.CloseMainWindow();
                        if (!process.WaitForExit(3000))
                        {
                            process.Kill();
                            process.WaitForExit(3000);
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
                result.Messages.Add(display + " закрыт.");
            }
            catch (Exception)
            {
            }
        }
    }
}
