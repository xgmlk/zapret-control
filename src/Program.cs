using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace ZapretControl
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (!AdminCheck.IsAdministrator())
            {
                if (RelaunchElevated(args))
                {
                    return;
                }
                MessageBox.Show(
                    "Для управления службой zapret нужны права администратора.",
                    "Zapret Control",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            // A previous advisor run may have been killed while IPSet was in
            // "any"; restore it before any service operation and bring the
            // service back to the persisted state. A running winws keeps the
            // empty list until restarted, so reload it.
            bool serviceWasRunning;
            if (IpsetSwitch.RecoverStaleSafe(out serviceWasRunning))
            {
                ZapretState state = ZapretService.GetStatus().State;
                try
                {
                    if (serviceWasRunning && state != ZapretState.Running)
                    {
                        ZapretService.Start();
                    }
                    else if (state == ZapretState.Running)
                    {
                        ZapretService.Stop();
                        ZapretService.Start();
                    }
                }
                catch (Exception)
                {
                }
            }

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
            {
                ReportFatal(e.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
            {
                Exception error = e.ExceptionObject as Exception;
                if (error != null)
                {
                    LogException(error);
                }
            };

            bool startHidden = HasArgument(args, "--tray") ||
                HasArgument(args, "--autostart");
            EventWaitHandle showEvent = null;
            EventWaitHandle exitEvent = null;
            try
            {
                showEvent = new EventWaitHandle(
                    false, EventResetMode.AutoReset, TrayIntegration.ShowEventName);
                exitEvent = new EventWaitHandle(
                    false, EventResetMode.AutoReset, TrayIntegration.ExitEventName);
            }
            catch (Exception)
            {
                // Without the handshake the app still works; only the second
                // launch and Setup cannot ask this window to appear or exit.
            }

            Mutex instanceMutex;
            if (!TrayIntegration.TryAcquire(out instanceMutex))
            {
                // Another GUI owns the mutex: ask it to come forward and leave
                // without creating a second window or tray icon.
                if (showEvent != null)
                {
                    try
                    {
                        showEvent.Set();
                    }
                    catch (Exception)
                    {
                    }
                }
                if (showEvent != null)
                {
                    showEvent.Dispose();
                }
                if (exitEvent != null)
                {
                    exitEvent.Dispose();
                }
                return;
            }

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Theme.Initialize();
                Application.Run(new MainForm(startHidden, showEvent, exitEvent));
            }
            finally
            {
                if (showEvent != null)
                {
                    showEvent.Dispose();
                }
                if (exitEvent != null)
                {
                    exitEvent.Dispose();
                }
                if (instanceMutex != null)
                {
                    try
                    {
                        instanceMutex.ReleaseMutex();
                    }
                    catch (Exception)
                    {
                    }
                    instanceMutex.Dispose();
                }
            }
        }

        private static bool HasArgument(string[] args, string name)
        {
            if (args == null)
            {
                return false;
            }
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static void ReportFatal(Exception error)
        {
            LogException(error);
            string code = error == null ? "Unknown" : error.GetType().Name;
            MessageBox.Show(
                "Непредвиденная ошибка (код " + code + ").\n" +
                "Подробности записаны в logs\\ui-error.log.",
                "Zapret Control",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }

        private static void LogException(Exception error)
        {
            try
            {
                AppPaths.EnsureDirectory(AppPaths.LogDirectory);
                StringBuilder text = new StringBuilder();
                text.AppendLine("=== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===");
                text.AppendLine(error == null ? "Unknown exception" : error.ToString());
                File.AppendAllText(
                    Path.Combine(AppPaths.LogDirectory, "ui-error.log"),
                    text.ToString(),
                    new UTF8Encoding(true));
            }
            catch (Exception)
            {
            }
        }

        private static bool RelaunchElevated(string[] args)
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = Application.ExecutablePath;
                info.WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
                info.UseShellExecute = true;
                info.Verb = "runas";
                // --tray/--autostart must survive self-elevation, otherwise a
                // login launch would pop the window in the user's face.
                info.Arguments = BuildArguments(args);
                Process.Start(info);
                return true;
            }
            catch (Win32Exception)
            {
                return false;
            }
        }

        private static string BuildArguments(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                return string.Empty;
            }
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < args.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(' ');
                }
                string value = args[i] ?? string.Empty;
                if (value.IndexOf(' ') >= 0 || value.IndexOf('"') >= 0)
                {
                    builder.Append('"').Append(value.Replace("\"", "\\\"")).Append('"');
                }
                else
                {
                    builder.Append(value);
                }
            }
            return builder.ToString();
        }
    }
}
