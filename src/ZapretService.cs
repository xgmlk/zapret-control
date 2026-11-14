using System;
using System.ComponentModel;
using System.Diagnostics;
using System.ServiceProcess;

namespace ZapretControl
{
    public enum ZapretState
    {
        Running,
        Stopped,
        Starting,
        Stopping,
        Paused,
        Unknown,
        NotFound,
        AccessDenied
    }

    public sealed class ZapretStatus
    {
        public ZapretState State { get; private set; }
        public string ServiceStatus { get; private set; }
        public int WinwsPid { get; private set; }

        public ZapretStatus(ZapretState state, string serviceStatus, int winwsPid)
        {
            State = state;
            ServiceStatus = serviceStatus;
            WinwsPid = winwsPid;
        }

        public bool IsRunning
        {
            get { return State == ZapretState.Running; }
        }
    }

    public static class ZapretService
    {
        public const string ServiceName = "zapret";
        public const string ProcessName = "winws";

        public static ZapretStatus GetStatus()
        {
            int pid = FindWinwsPid();
            try
            {
                using (ServiceController sc = new ServiceController(ServiceName))
                {
                    ServiceControllerStatus status = sc.Status;
                    return new ZapretStatus(
                        Map(status),
                        status.ToString(),
                        status == ServiceControllerStatus.Running ? pid : 0);
                }
            }
            catch (InvalidOperationException ex)
            {
                // ServiceController wraps the real SCM error in Win32Exception.
                Win32Exception win32 = ex.InnerException as Win32Exception;
                int code = win32 == null ? 0 : win32.NativeErrorCode;
                if (code == 5)
                {
                    return new ZapretStatus(ZapretState.AccessDenied, "Access denied", 0);
                }
                if (code == 1060)
                {
                    return new ZapretStatus(ZapretState.NotFound, "Not installed", 0);
                }
                return new ZapretStatus(
                    ZapretState.Unknown,
                    code == 0 ? "InvalidOperation" : "Error " + code,
                    0);
            }
            catch (Exception)
            {
                return new ZapretStatus(ZapretState.Unknown, "Unknown", 0);
            }
        }

        public static void Start()
        {
            using (ServiceController sc = new ServiceController(ServiceName))
            {
                ServiceControllerStatus status = sc.Status;
                if (status == ServiceControllerStatus.Running ||
                    status == ServiceControllerStatus.StartPending)
                {
                    return;
                }
                sc.Start();
                sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(25));
            }
        }

        public static void Stop()
        {
            using (ServiceController sc = new ServiceController(ServiceName))
            {
                ServiceControllerStatus status = sc.Status;
                if (status == ServiceControllerStatus.Stopped ||
                    status == ServiceControllerStatus.StopPending)
                {
                    return;
                }
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(25));
            }
        }

        // Restarts only a running service: syncing a stopped service must not
        // start DPI bypass behind the user's back.
        public static bool RestartIfRunning()
        {
            using (ServiceController sc = new ServiceController(ServiceName))
            {
                if (sc.Status == ServiceControllerStatus.StartPending)
                {
                    sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(25));
                }
                if (sc.Status != ServiceControllerStatus.Running)
                {
                    return false;
                }
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(25));
                sc.Start();
                sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(25));
                return true;
            }
        }

        // Best effort: shows the first winws.exe visible to the user. A manually
        // started winws would be reported too, but the service is the normal owner.
        private static int FindWinwsPid()
        {
            try
            {
                Process[] processes = Process.GetProcessesByName(ProcessName);
                if (processes.Length == 0)
                {
                    return 0;
                }
                int pid = processes[0].Id;
                foreach (Process process in processes)
                {
                    process.Dispose();
                }
                return pid;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        internal static ZapretState Map(ServiceControllerStatus status)
        {
            switch (status)
            {
                case ServiceControllerStatus.Running:
                    return ZapretState.Running;
                case ServiceControllerStatus.Stopped:
                    return ZapretState.Stopped;
                case ServiceControllerStatus.StartPending:
                    return ZapretState.Starting;
                case ServiceControllerStatus.StopPending:
                    return ZapretState.Stopping;
                case ServiceControllerStatus.Paused:
                case ServiceControllerStatus.PausePending:
                    return ZapretState.Paused;
                case ServiceControllerStatus.ContinuePending:
                    return ZapretState.Starting;
                default:
                    return ZapretState.Unknown;
            }
        }
    }
}
