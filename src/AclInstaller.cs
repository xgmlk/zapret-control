using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace ZapretControl
{
    // Windows ACLs by well-known SIDs, without localized group names.
    public static class AclInstaller
    {
        private const string Administrators = "*S-1-5-32-544";
        private const string SystemSid = "*S-1-5-18";
        private const string Users = "*S-1-5-32-545";

        public static void ApplyInstallAcls(
            string installDirectory,
            string payloadDirectory,
            string dataDirectory)
        {
            HardenDirectory(installDirectory);
            if (!string.IsNullOrEmpty(payloadDirectory) && Directory.Exists(payloadDirectory))
            {
                HardenDirectory(payloadDirectory);
            }
            if (!string.IsNullOrEmpty(dataDirectory))
            {
                AppPaths.EnsureDirectory(dataDirectory);
                HardenDirectory(dataDirectory);
            }
            foreach (string file in new string[]
            {
                Path.Combine(installDirectory, AppInfo.GuiFileName),
                Path.Combine(installDirectory, AppInfo.CliFileName),
                Path.Combine(installDirectory, AppInfo.SetupFileName)
            })
            {
                if (File.Exists(file))
                {
                    HardenFile(file);
                }
            }
        }

        public static void HardenDirectory(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            {
                return;
            }
            string[] grants = new string[]
            {
                Administrators + ":(OI)(CI)F",
                SystemSid + ":(OI)(CI)F",
                Users + ":(OI)(CI)RX"
            };
            RunIcacls(path, new string[] { "/inheritance:r", "/grant:r" }
                .Concat(grants).ToArray());
            SetOwner(path);
        }

        public static void HardenFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return;
            }
            string[] grants = new string[]
            {
                Administrators + ":F",
                SystemSid + ":F",
                Users + ":RX"
            };
            RunIcacls(path, new string[] { "/inheritance:r", "/grant:r" }
                .Concat(grants).ToArray());
            SetOwner(path);
        }

        private static void SetOwner(string path)
        {
            RunIcacls(path, "/setowner", Administrators);
        }

        private static void RunIcacls(string path, params string[] arguments)
        {
            string icacls = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System), "icacls.exe");
            if (!File.Exists(icacls))
            {
                throw new InvalidOperationException("icacls.exe не найден.");
            }
            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = icacls;
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.Arguments = Quote(path);
            foreach (string argument in arguments)
            {
                info.Arguments += " " + (argument.StartsWith("/", StringComparison.Ordinal)
                    ? argument
                    : Quote(argument));
            }
            using (Process process = Process.Start(info))
            {
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException(
                        "icacls завершился с кодом " + process.ExitCode + ": " +
                        (output + " " + error).Trim());
                }
            }
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }

    public static class Shortcut
    {
        private const string StartMenuName = "Zapret Control.lnk";
        private const string UninstallName = "Удалить Zapret Control.lnk";

        [ComImport]
        [Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLink
        {
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder file,
                int maxPath, IntPtr findData, uint flags);
            void GetIDList(out IntPtr itemIdList);
            void SetIDList(IntPtr itemIdList);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder name,
                int maxName);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder dir,
                int maxPath);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder arguments,
                int maxPath);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
            void GetHotkey(out short hotkey);
            void SetHotkey(short hotkey);
            void GetShowCmd(out int showCommand);
            void SetShowCmd(int showCommand);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder icon,
                int maxPath, out int iconIndex);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string icon, int iconIndex);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
            void Resolve(IntPtr window, uint flags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
        }

        public static void CreateAll(string guiPath, string setupPath)
        {
            Exception error = null;
            System.Threading.Thread thread = new System.Threading.Thread(
                new System.Threading.ThreadStart(delegate
                {
                    try
                    {
                        CreateAllCore(guiPath, setupPath);
                    }
                    catch (Exception ex)
                    {
                        error = ex;
                    }
                }));
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (error != null)
            {
                throw new InvalidOperationException(
                    "Не удалось создать ярлыки: " + error.Message, error);
            }
        }

        private static void CreateAllCore(string guiPath, string setupPath)
        {            string startMenu = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                @"Programs\Zapret Control");
            AppPaths.EnsureDirectory(startMenu);
            CreateLink(
                Path.Combine(startMenu, StartMenuName),
                guiPath, string.Empty,
                Path.GetDirectoryName(guiPath), "Zapret Control", guiPath);
            if (!string.IsNullOrEmpty(setupPath) && File.Exists(setupPath))
            {
                CreateLink(
                    Path.Combine(startMenu, UninstallName),
                    setupPath, "/uninstall",
                    Path.GetDirectoryName(setupPath), "Удалить Zapret Control", setupPath);
            }
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
            if (!string.IsNullOrEmpty(desktop))
            {
                CreateLink(
                    Path.Combine(desktop, StartMenuName),
                    guiPath, string.Empty,
                    Path.GetDirectoryName(guiPath), "Zapret Control", guiPath);
            }
        }

        public static void SelfTest(string executablePath)
        {
            string path = Path.Combine(
                Path.GetTempPath(), "zapret-control-link-" +
                Guid.NewGuid().ToString("N") + ".lnk");
            try
            {
                CreateLink(path, executablePath, string.Empty,
                    Path.GetDirectoryName(executablePath), "Zapret Control test",
                    executablePath);
                if (!File.Exists(path) || new FileInfo(path).Length == 0)
                {
                    throw new InvalidOperationException(
                        "Ярлык не создан через IShellLink.");
                }
            }
            finally
            {
                TryDelete(path);
            }
        }

        public static void RemoveAll()
        {            TryDelete(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                @"Programs\Zapret Control", StartMenuName));
            TryDelete(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                @"Programs\Zapret Control", UninstallName));
            TryDelete(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                StartMenuName));
            try
            {
                string folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                    @"Programs\Zapret Control");
                if (Directory.Exists(folder) &&
                    Directory.GetFileSystemEntries(folder).Length == 0)
                {
                    Directory.Delete(folder);
                }
            }
            catch (Exception)
            {
            }
        }

        public static void CreateLink(
            string linkPath,
            string targetPath,
            string arguments,
            string workingDirectory,
            string description,
            string iconPath)
        {            object instance = Activator.CreateInstance(Type.GetTypeFromCLSID(
                new Guid("00021401-0000-0000-C000-000000000046")));
            IShellLinkW link = (IShellLinkW)instance;
            link.SetPath(targetPath);
            link.SetArguments(arguments ?? string.Empty);
            link.SetWorkingDirectory(workingDirectory ?? string.Empty);
            link.SetDescription(description ?? string.Empty);
            link.SetIconLocation(iconPath ?? targetPath, 0);
            ((IPersistFile)instance).Save(linkPath, true);
            Marshal.FinalReleaseComObject(instance);
        }

        private static void TryDelete(string path)
        {
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
    }

    public static class UninstallRegistry
    {
        private const string KeyPath =
            @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ZapretControl";

        public static void Register(string installDirectory, string setupPath, string payloadVersion)
        {
            using (Microsoft.Win32.RegistryKey key =
                Microsoft.Win32.Registry.LocalMachine.CreateSubKey(KeyPath))
            {
                if (key == null)
                {
                    throw new InvalidOperationException(
                        "Не удалось создать запись об удалении программы.");
                }
                key.SetValue("DisplayName", "Zapret Control", Microsoft.Win32.RegistryValueKind.String);
                key.SetValue("DisplayVersion", AppInfo.Version, Microsoft.Win32.RegistryValueKind.String);
                key.SetValue("Publisher", AppInfo.Publisher, Microsoft.Win32.RegistryValueKind.String);
                key.SetValue("InstallLocation", installDirectory, Microsoft.Win32.RegistryValueKind.String);
                key.SetValue("DisplayIcon", Path.Combine(installDirectory, AppInfo.GuiFileName),
                    Microsoft.Win32.RegistryValueKind.String);
                key.SetValue("UninstallString", "\"" + setupPath + "\" /uninstall",
                    Microsoft.Win32.RegistryValueKind.String);
                key.SetValue("QuietUninstallString", "\"" + setupPath + "\" /S /uninstall",
                    Microsoft.Win32.RegistryValueKind.String);
                key.SetValue("NoModify", 1, Microsoft.Win32.RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, Microsoft.Win32.RegistryValueKind.DWord);
                key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"),
                    Microsoft.Win32.RegistryValueKind.String);
                if (!string.IsNullOrEmpty(payloadVersion))
                {
                    key.SetValue("ZapretPayloadVersion", payloadVersion,
                        Microsoft.Win32.RegistryValueKind.String);
                }
                long size = 0;
                try
                {
                    foreach (string file in Directory.GetFiles(installDirectory, "*", SearchOption.AllDirectories))
                    {
                        size += new FileInfo(file).Length;
                    }
                }
                catch (Exception)
                {
                }
                key.SetValue("EstimatedSize", (int)Math.Max(1, size / 1024),
                    Microsoft.Win32.RegistryValueKind.DWord);
            }
        }

        public static void Unregister()
        {
            try
            {
                Microsoft.Win32.Registry.LocalMachine.DeleteSubKeyTree(KeyPath, false);
            }
            catch (Exception)
            {
            }
        }
    }
}
