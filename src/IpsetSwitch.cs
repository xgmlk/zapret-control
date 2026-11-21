using System;
using System.IO;
using System.Text;

namespace ZapretControl
{
    // Switches IPSet to "any" for the duration of the advisor run and restores
    // the original mode in finally. The flag file lets the next application
    // start recover the list if the process is killed mid-run.
    internal static class IpsetSwitch
    {
        internal const string FlagFileName = "ipset_switched.flag";

        internal static string FlagPath
        {
            get { return Path.Combine(AppPaths.DataDirectory, FlagFileName); }
        }

        internal static bool Begin(
            string payloadRoot, bool serviceWasRunning, out string originalState)
        {
            return Begin(payloadRoot, FlagPath, serviceWasRunning, out originalState);
        }

        internal static bool Begin(string payloadRoot, out string originalState)
        {
            return Begin(payloadRoot, FlagPath, false, out originalState);
        }

        internal static bool Begin(
            string payloadRoot, string flagPath, out string originalState)
        {
            return Begin(payloadRoot, flagPath, false, out originalState);
        }

        internal static bool Begin(
            string payloadRoot,
            string flagPath,
            bool serviceWasRunning,
            out string originalState)
        {
            originalState = IpsetManager.GetState(payloadRoot);
            if (originalState == IpsetManager.StateAny)
            {
                return false;
            }
            string text = "payload=" + payloadRoot + Environment.NewLine +
                          "state=" + originalState + Environment.NewLine +
                          "service=" + (serviceWasRunning ? "running" : "stopped") +
                          Environment.NewLine +
                          "started=" + DateTime.UtcNow.ToString("o") +
                          Environment.NewLine;
            if (!AtomicFile.WriteAllText(flagPath, text))
            {
                throw new InvalidOperationException(
                    "Не удалось записать флаг переключения IPSet.");
            }
            IpsetManager.SetState(payloadRoot, IpsetManager.StateAny);
            return true;
        }

        internal static string Restore(string payloadRoot, string originalState)
        {
            return Restore(payloadRoot, originalState, FlagPath, false);
        }

        internal static string Restore(
            string payloadRoot, string originalState, string flagPath)
        {
            return Restore(payloadRoot, originalState, flagPath, false);
        }

        // keepFlag is used by the advisor: the flag stays until the service is
        // restored, so a kill between the two steps is still recoverable.
        internal static string Restore(
            string payloadRoot, string originalState, bool keepFlag)
        {
            return Restore(payloadRoot, originalState, FlagPath, keepFlag);
        }

        internal static string Restore(
            string payloadRoot,
            string originalState,
            string flagPath,
            bool keepFlag)
        {
            try
            {
                if (string.IsNullOrEmpty(originalState) ||
                    originalState == IpsetManager.StateAny)
                {
                    if (!keepFlag)
                    {
                        TryDelete(flagPath);
                    }
                    return null;
                }
                IpsetManager.SetState(payloadRoot, originalState);
                if (!keepFlag)
                {
                    TryDelete(flagPath);
                }
                return null;
            }
            catch (Exception ex)
            {
                Log("Не удалось восстановить IPSet (" + originalState + "): " +
                    ex.Message);
                return "IPSet не восстановлен: " + ex.Message;
            }
        }

        internal static void ClearFlag()
        {
            TryDelete(FlagPath);
        }

        internal static string RecoverStale()
        {
            string expectedRoot = null;
            try
            {
                expectedRoot = AppPaths.ResolvePayloadRoot();
            }
            catch (Exception)
            {
            }
            return RecoverStale(FlagPath, expectedRoot);
        }

        internal static string RecoverStale(string flagPath)
        {
            string expectedRoot = null;
            try
            {
                expectedRoot = AppPaths.ResolvePayloadRoot();
            }
            catch (Exception)
            {
            }
            return RecoverStale(flagPath, expectedRoot);
        }

        internal static string RecoverStale(string flagPath, string expectedRoot)
        {
            try
            {
                if (string.IsNullOrEmpty(flagPath) || !File.Exists(flagPath))
                {
                    return null;
                }
                if (AppPaths.IsReparsePoint(flagPath))
                {
                    Log("Флаг IPSet является reparse point; восстановление пропущено.");
                    return "Флаг IPSet является reparse point.";
                }
                string text;
                using (FileStream stream = new FileStream(
                    flagPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (stream.Length <= 0 || stream.Length > 4096)
                    {
                        Log("Флаг IPSet пуст или слишком большой; удалён.");
                        TryDelete(flagPath);
                        return "Флаг IPSet повреждён.";
                    }
                    using (StreamReader reader = new StreamReader(
                        stream, Encoding.UTF8, true))
                    {
                        text = reader.ReadToEnd();
                    }
                }
                string payload = null;
                string state = null;
                foreach (string raw in text.Replace("\r\n", "\n").Split('\n'))
                {
                    int equals = raw.IndexOf('=');
                    if (equals <= 0)
                    {
                        continue;
                    }
                    string key = raw.Substring(0, equals).Trim();
                    string value = raw.Substring(equals + 1).Trim();
                    if (key == "payload")
                    {
                        payload = value;
                    }
                    else if (key == "state")
                    {
                        state = value;
                    }
                }
                if (string.IsNullOrEmpty(payload) || !AppPaths.IsPayloadRoot(payload))
                {
                    Log("Флаг IPSet указывает на несуществующий payload; удалён.");
                    TryDelete(flagPath);
                    return "Флаг IPSet указывает на несуществующий payload.";
                }
                if (string.IsNullOrEmpty(expectedRoot))
                {
                    Log("Не удалось определить payload для восстановления IPSet; " +
                        "флаг оставлен.");
                    return "Не удалось определить payload для восстановления IPSet.";
                }
                if (!Path.GetFullPath(payload).Equals(
                        Path.GetFullPath(expectedRoot),
                        StringComparison.OrdinalIgnoreCase))
                {
                    // A planted flag must not make the elevated process write
                    // into an arbitrary directory.
                    Log("Флаг IPSet указывает на другой payload; удалён.");
                    TryDelete(flagPath);
                    return "Флаг IPSet указывает на другой payload.";
                }
                if (AppPaths.IsReparsePoint(payload))
                {
                    Log("Payload из флага IPSet является reparse point; удалён.");
                    TryDelete(flagPath);
                    return "Payload из флага IPSet является reparse point.";
                }
                if (state != IpsetManager.StateLoaded &&
                    state != IpsetManager.StateNone &&
                    state != IpsetManager.StateAny)
                {
                    Log("Флаг IPSet содержит неизвестное состояние; удалён.");
                    TryDelete(flagPath);
                    return "Флаг IPSet повреждён.";
                }
                if (state == IpsetManager.StateAny)
                {
                    TryDelete(flagPath);
                    return null;
                }
                string error = Restore(payload, state, flagPath);
                if (error == null)
                {
                    Log("IPSet восстановлен при старте: " + state + ".");
                }
                return error;
            }
            catch (Exception ex)
            {
                Log("Ошибка восстановления IPSet при старте: " + ex.Message);
                return ex.Message;
            }
        }

        internal static bool RecoverStaleSafe()
        {
            bool ignored;
            return RecoverStaleSafe(out ignored);
        }

        internal static bool RecoverStaleSafe(out bool serviceWasRunning)
        {
            serviceWasRunning = false;
            try
            {
                if (!File.Exists(FlagPath))
                {
                    return false;
                }
                serviceWasRunning = FlagRequestsRunningService(FlagPath);
                RecoverStale();
                // A successful restore deletes the flag; a failure keeps it
                // for the next start.
                return !File.Exists(FlagPath);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool FlagRequestsRunningService(string flagPath)
        {
            try
            {
                if (AppPaths.IsReparsePoint(flagPath))
                {
                    return false;
                }
                string text;
                using (FileStream stream = new FileStream(
                    flagPath, FileMode.Open, FileAccess.Read,
                    FileShare.Read | FileShare.Delete))
                {
                    if (stream.Length <= 0 || stream.Length > 4096)
                    {
                        return false;
                    }
                    using (StreamReader reader = new StreamReader(
                        stream, Encoding.UTF8, true))
                    {
                        text = reader.ReadToEnd();
                    }
                }
                return text.IndexOf(
                    "service=running",
                    StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void TryDelete(string path)
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

        private static void Log(string message)
        {
            try
            {
                AppPaths.EnsureDirectory(AppPaths.LogDirectory);
                File.AppendAllText(
                    Path.Combine(AppPaths.LogDirectory, "ipset-guard.log"),
                    "=== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===" +
                    Environment.NewLine + message + Environment.NewLine,
                    new UTF8Encoding(true));
            }
            catch (Exception)
            {
            }
        }
    }
}
