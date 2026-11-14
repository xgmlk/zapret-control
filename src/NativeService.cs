using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

namespace ZapretControl
{
    // Thin, native SCM wrapper (advapi32.dll). No sc.exe dependency.
    internal static class NativeService
    {
        private const uint ScManagerAllAccess = 0x000F003F;
        private const uint ScManagerConnect = 0x0001;
        private const uint ServiceAllAccess = 0x000F01FF;
        private const uint ServiceQueryStatus = 0x0004;
        private const uint ServiceChangeConfig = 0x0002;
        private const uint ServiceWin32OwnProcess = 0x00000010;
        private const uint ServiceAutoStart = 0x00000002;
        private const uint ServiceDemandStart = 0x00000003;
        private const uint ServiceNoChange = 0xFFFFFFFF;
        private const uint ServiceErrorNormal = 0x00000001;
        private const uint ServiceConfigDescription = 1;
        private const int ErrorServiceDoesNotExist = 1060;
        private const int ErrorServiceMarkedForDelete = 1072;
        private const string ServiceKeyPath =
            @"SYSTEM\CurrentControlSet\Services\";

        [StructLayout(LayoutKind.Sequential)]
        private struct ServiceDescription
        {
            [MarshalAs(UnmanagedType.LPWStr)]
            public string Description;
        }

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr OpenSCManager(
            string machineName, string databaseName, uint desiredAccess);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateService(
            IntPtr manager,
            string serviceName,
            string displayName,
            uint desiredAccess,
            uint serviceType,
            uint startType,
            uint errorControl,
            string binaryPathName,
            string loadOrderGroup,
            IntPtr tagId,
            string dependencies,
            string serviceStartName,
            string password);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr OpenService(
            IntPtr manager, string serviceName, uint desiredAccess);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool DeleteService(IntPtr service);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool ChangeServiceConfig(
            IntPtr service,
            uint serviceType,
            uint startType,
            uint errorControl,
            string binaryPathName,
            string loadOrderGroup,
            IntPtr tagId,
            string dependencies,
            string serviceStartName,
            string password,
            string displayName);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool ChangeServiceConfig2(
            IntPtr service, uint infoLevel, ref ServiceDescription description);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool CloseServiceHandle(IntPtr handle);

        public static void Install(
            string serviceName,
            string displayName,
            string imagePath,
            string description)
        {
            if (string.IsNullOrEmpty(serviceName))
            {
                throw new ArgumentException("Не задано имя службы.", "serviceName");
            }
            if (string.IsNullOrEmpty(imagePath))
            {
                throw new ArgumentException("Не задана команда запуска службы.", "imagePath");
            }

            IntPtr manager = OpenSCManager(null, null, ScManagerAllAccess);
            if (manager == IntPtr.Zero)
            {
                throw Win32("Не удалось открыть менеджер служб");
            }

            IntPtr service = IntPtr.Zero;
            try
            {
                service = CreateService(
                    manager,
                    serviceName,
                    displayName,
                    ServiceAllAccess,
                    ServiceWin32OwnProcess,
                    ServiceAutoStart,
                    ServiceErrorNormal,
                    imagePath,
                    null,
                    IntPtr.Zero,
                    null,
                    null,
                    null);

                if (service == IntPtr.Zero)
                {
                    int code = Marshal.GetLastWin32Error();
                    if (code == 1073) // ERROR_SERVICE_EXISTS
                    {
                        throw new InvalidOperationException(
                            "Служба " + serviceName + " уже существует. Сначала удалите её.");
                    }
                    throw Win32("Не удалось создать службу " + serviceName);
                }

                if (!string.IsNullOrEmpty(description))
                {
                    ServiceDescription value = new ServiceDescription();
                    value.Description = description;
                    if (!ChangeServiceConfig2(service, ServiceConfigDescription, ref value))
                    {
                        throw Win32("Не удалось задать описание службы " + serviceName);
                    }
                }
            }
            finally
            {
                if (service != IntPtr.Zero)
                {
                    CloseServiceHandle(service);
                }
                CloseServiceHandle(manager);
            }
        }

        public static bool Exists(string serviceName)
        {
            IntPtr manager = OpenSCManager(null, null, ScManagerConnect);
            if (manager == IntPtr.Zero)
            {
                return false;
            }
            IntPtr service = IntPtr.Zero;
            try
            {
                service = OpenService(manager, serviceName, ServiceQueryStatus);
                if (service != IntPtr.Zero)
                {
                    return true;
                }
                int code = Marshal.GetLastWin32Error();
                return code != ErrorServiceDoesNotExist &&
                       code != ErrorServiceMarkedForDelete;
            }
            finally
            {
                if (service != IntPtr.Zero)
                {
                    CloseServiceHandle(service);
                }
                CloseServiceHandle(manager);
            }
        }

        public static bool IsAutoStart(string serviceName)
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(
                    ServiceKeyPath + serviceName, false))
                {
                    object value = key == null ? null : key.GetValue("Start");
                    return value is int && (int)value == (int)ServiceAutoStart;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static void SetAutoStart(string serviceName, bool autoStart)
        {
            IntPtr manager = OpenSCManager(null, null, ScManagerAllAccess);
            if (manager == IntPtr.Zero)
            {
                throw Win32("Не удалось открыть менеджер служб");
            }
            IntPtr service = IntPtr.Zero;
            try
            {
                service = OpenService(manager, serviceName, ServiceChangeConfig);
                if (service == IntPtr.Zero)
                {
                    throw Win32("Не удалось открыть службу " + serviceName);
                }
                if (!ChangeServiceConfig(
                    service,
                    ServiceNoChange,
                    autoStart ? ServiceAutoStart : ServiceDemandStart,
                    ServiceNoChange,
                    null,
                    null,
                    IntPtr.Zero,
                    null,
                    null,
                    null,
                    null))
                {
                    throw Win32("Не удалось изменить автозагрузку службы " + serviceName);
                }
            }
            finally
            {
                if (service != IntPtr.Zero)
                {
                    CloseServiceHandle(service);
                }
                CloseServiceHandle(manager);
            }
        }

        // The caller must stop the service first.
        public static void Delete(string serviceName)
        {
            IntPtr manager = OpenSCManager(null, null, ScManagerAllAccess);
            if (manager == IntPtr.Zero)
            {
                throw Win32("Не удалось открыть менеджер служб");
            }
            IntPtr service = IntPtr.Zero;
            try
            {
                service = OpenService(manager, serviceName, ServiceAllAccess);
                if (service == IntPtr.Zero)
                {
                    int code = Marshal.GetLastWin32Error();
                    if (code == ErrorServiceDoesNotExist)
                    {
                        return;
                    }
                    throw Win32("Не удалось открыть службу " + serviceName);
                }
                if (!DeleteService(service))
                {
                    throw Win32("Не удалось удалить службу " + serviceName);
                }
            }
            finally
            {
                if (service != IntPtr.Zero)
                {
                    CloseServiceHandle(service);
                }
                CloseServiceHandle(manager);
            }

            DateTime deadline = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < deadline)
            {
                if (!ServiceKeyExists(serviceName))
                {
                    return;
                }
                Thread.Sleep(250);
            }
            throw new InvalidOperationException(
                "Служба " + serviceName + " помечена для удаления, но ещё существует.");
        }

        private static bool ServiceKeyExists(string serviceName)
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Services\" + serviceName, false))
                {
                    return key != null;
                }
            }
            catch (Exception)
            {
                return true;
            }
        }

        private static Win32Exception Win32(string message)
        {
            int code = Marshal.GetLastWin32Error();
            return new Win32Exception(code, message + " (код " + code + ").");
        }
    }
}
