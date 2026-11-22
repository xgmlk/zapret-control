using System;
using System.Threading;
using Microsoft.Win32;

namespace ZapretControl
{
    // Opt-in per-user autostart. The GUI needs elevation, so this is off by
    // default and only writes HKCU; Setup and services never touch it.
    internal static class AutostartManager
    {
        private const string RunKey =
            @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "ZapretControl";

        internal static bool SetEnabled(bool enabled, string executablePath)
        {
            if (string.IsNullOrEmpty(executablePath))
            {
                return false;
            }
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (key == null)
                    {
                        return false;
                    }
                    if (enabled)
                    {
                        key.SetValue(
                            ValueName,
                            "\"" + executablePath + "\" --autostart",
                            RegistryValueKind.String);
                    }
                    else
                    {
                        key.DeleteValue(ValueName, false);
                    }
                    return true;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    // Cross-process handshake between GUI, its second launch and Setup.
    // The GUI owns the mutex and listens for the two events; a second launch
    // asks the first window to come forward, Setup asks it to exit before
    // replacing files. Names are local to the interactive session on purpose:
    // a service/session-0 process must never signal a user's window.
    internal static class TrayIntegration
    {
        internal const string MutexName = @"Local\ZapretControl.Gui";
        internal const string ShowEventName = @"Local\ZapretControl.Gui.Show";
        internal const string ExitEventName = @"Local\ZapretControl.Gui.Exit";

        internal static bool TryAcquire(out Mutex mutex)
        {
            mutex = null;
            try
            {
                bool created;
                Mutex candidate = new Mutex(true, MutexName, out created);
                if (!created)
                {
                    candidate.Dispose();
                    return false;
                }
                mutex = candidate;
                return true;
            }
            catch (Exception)
            {
                // A restricted token may forbid the named mutex. Running
                // without single-instance protection is better than refusing
                // to start at all; Setup still has the exit event.
                return true;
            }
        }

        // Opens an existing event without creating it: a signal must never
        // leave a stray kernel object behind when no GUI is running.
        internal static bool Signal(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }
            try
            {
                using (EventWaitHandle handle = EventWaitHandle.OpenExisting(name))
                {
                    handle.Set();
                    return true;
                }
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal static bool SignalShow()
        {
            return Signal(ShowEventName);
        }

        // Setup calls this before waiting for the process. Returns false when
        // no GUI with the event exists (older build or app not running).
        internal static bool RequestExit()
        {
            return Signal(ExitEventName);
        }
    }
}
