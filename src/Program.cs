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
        private static void Main()
        {
            if (!AdminCheck.IsAdministrator())
            {
                if (RelaunchElevated())
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

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
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

        private static bool RelaunchElevated()
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = Application.ExecutablePath;
                info.WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
                info.UseShellExecute = true;
                info.Verb = "runas";
                Process.Start(info);
                return true;
            }
            catch (Win32Exception)
            {
                return false;
            }
        }
    }
}
