using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.ServiceProcess;
using System.Text;

namespace ZapretControl
{
    // Console-subsystem CLI (ZapretControl.Cli.exe). Kept separate from the GUI
    // binary because a winexe cannot reliably write to the caller's console.
    internal static class Cli
    {
        private const string Usage =
            "usage: ZapretControl.Cli.exe --status|--toggle|--iface|--sync-iface|" +
            "--register-task|--unregister-task|--version|--selftest|--selftest-payload|" +
            "--manifest-check=<path>|--strategies|--strategy-command=<name>|" +
            "--install-service|--remove-service|--restart|--game-status|" +
            "--set-game-filter=<mode>|--ipset-status|--set-ipset=<state>|" +
            "--update-ipset|--fakes|--replace-fake=<kind> --file=<name>|" +
            "--hosts-check|--hosts-apply|--clear-discord-cache|--diagnostics|" +
            "--test[=<standard|dpi>] [--gate=1..16]|--youtube-probe|--update-check|" +
            "--update-payload|--update-app|--rollback-payload|--tg-check|" +
            "--tg-selftest|--tg-status|--tg-link|--tg-bridge";

        private static int Main(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                Console.Error.WriteLine(Usage);
                return 64;
            }

            string commandRaw = args[0];
            string command = commandRaw.ToLowerInvariant();
            try
            {
                if (AdminCheck.IsAdministrator())
                {
                    IpsetSwitch.RecoverStaleSafe();
                }
                if (command == "--status")
                {
                    return PrintStatus();
                }
                if (command == "--toggle")
                {
                    return Toggle();
                }
                if (command == "--iface")
                {
                    return PrintIface();
                }
                if (command == "--sync-iface")
                {
                    return SyncIface(args);
                }
                if (command == "--register-task")
                {
                    return RegisterTask();
                }
                if (command == "--unregister-task")
                {
                    return UnregisterTask();
                }
                if (command == "--version")
                {
                    Console.WriteLine("version=" + AppInfo.Version);
                    return 0;
                }
                if (command == "--selftest")
                {
                    return SelfTest();
                }
                if (command == "--selftest-payload")
                {
                    return SelfTestPayload();
                }
                if (command.StartsWith("--manifest-check=", StringComparison.Ordinal))
                {
                    return CheckManifest(
                        commandRaw.Substring("--manifest-check=".Length));
                }
                if (command == "--strategies")
                {
                    return PrintStrategies();
                }
                if (command.StartsWith("--strategy-command=", StringComparison.Ordinal))
                {
                    return PrintStrategyCommand(
                        commandRaw.Substring("--strategy-command=".Length));
                }
                if (command == "--install-service")
                {
                    return InstallService(args);
                }
                if (command == "--remove-service")
                {
                    return RemoveService();
                }
                if (command == "--restart")
                {
                    return RestartService();
                }
                if (command == "--game-status")
                {
                    return PrintGameFilter();
                }
                if (command.StartsWith("--set-game-filter=", StringComparison.Ordinal))
                {
                    return SetGameFilter(args);
                }
                if (command == "--ipset-status")
                {
                    return PrintIpset();
                }
                if (command.StartsWith("--set-ipset=", StringComparison.Ordinal))
                {
                    return SetIpset(
                        commandRaw.Substring("--set-ipset=".Length).ToLowerInvariant());
                }
                if (command == "--update-ipset")
                {
                    return UpdateIpset();
                }
                if (command == "--fakes")
                {
                    return PrintFakes();
                }
                if (command.StartsWith("--replace-fake=", StringComparison.Ordinal))
                {
                    return ReplaceFake(
                        args, commandRaw.Substring("--replace-fake=".Length)
                            .ToLowerInvariant());
                }
                if (command == "--hosts-check")
                {
                    return CheckHosts();
                }
                if (command == "--hosts-apply")
                {
                    return ApplyHosts();
                }
                if (command == "--clear-discord-cache")
                {
                    return ClearDiscordCache();
                }
                if (command == "--diagnostics")
                {
                    return PrintDiagnostics();
                }
                if (command == "--test")
                {
                    int gate;
                    if (!TryParseGate(args, out gate))
                    {
                        Console.Error.WriteLine("error=invalid_gate");
                        return 64;
                    }
                    return RunAdvisor(gate);
                }
                if (command.StartsWith("--test=", StringComparison.Ordinal))
                {
                    return RunLegacyTests(
                        commandRaw.Substring("--test=".Length).ToLowerInvariant());
                }
                if (command == "--youtube-probe")
                {
                    return ProbeYouTube();
                }
                if (command == "--tg-check")
                {
                    return CheckTelegram();
                }
                if (command == "--tg-selftest")
                {
                    return SelfTestTelegramProxy();
                }
                if (command == "--tg-status")
                {
                    return PrintTelegramStatus();
                }
                if (command == "--tg-link")
                {
                    return PrintTelegramLink();
                }
                if (command == "--tg-bridge")
                {
                    return BridgeTelegramProxy();
                }
                if (command == "--update-check")
                {
                    return CheckUpdates();
                }
                if (command == "--update-payload")
                {
                    return UpdatePayload();
                }
                if (command == "--update-app")
                {
                    return UpdateApplication();
                }
                if (command == "--rollback-payload")
                {
                    return RollbackPayload();
                }

                Console.Error.WriteLine("error=unknown_command");
                return 64;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("error=" + ex.GetType().Name);
                Console.Error.WriteLine("detail=" + ex.Message);
                return 7;
            }
        }

        // 0 running, 1 stopped, 2 transitional/unknown, 3 not installed, 4 access denied.
        private static int PrintStatus()
        {
            ZapretStatus status = ZapretService.GetStatus();
            Console.WriteLine("state=" + status.State);
            Console.WriteLine("service=" + status.ServiceStatus);
            Console.WriteLine("pid=" + status.WinwsPid);

            switch (status.State)
            {
                case ZapretState.Running:
                    return 0;
                case ZapretState.Stopped:
                    return 1;
                case ZapretState.NotFound:
                    return 3;
                case ZapretState.AccessDenied:
                    return 4;
                default:
                    return 2;
            }
        }

        // 0 ok, 3 admin required, 4 not found, 5 access denied, 6 busy/timeout, 7 error.
        private static int Toggle()
        {
            if (!RequireAdmin())
            {
                return 3;
            }
            ZapretStatus status = ZapretService.GetStatus();
            if (status.State == ZapretState.NotFound)
            {
                Console.Error.WriteLine("error=service_not_found");
                return 4;
            }
            if (status.State == ZapretState.AccessDenied)
            {
                Console.Error.WriteLine("error=access_denied");
                return 5;
            }
            if (status.State != ZapretState.Running && status.State != ZapretState.Stopped)
            {
                Console.Error.WriteLine("error=service_busy");
                return 6;
            }

            try
            {
                if (status.State == ZapretState.Running)
                {
                    ZapretService.Stop();
                }
                else
                {
                    ZapretService.Start();
                }
            }
            catch (System.ServiceProcess.TimeoutException)
            {
                Console.Error.WriteLine("error=timeout");
                return 6;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("error=" + ex.GetType().Name);
                return 7;
            }

            ZapretStatus after = ZapretService.GetStatus();
            Console.WriteLine("state=" + after.State);
            if (after.State == ZapretState.Running || after.State == ZapretState.Stopped)
            {
                return 0;
            }
            Console.Error.WriteLine("error=state_not_settled");
            return 7;
        }

        // 0 matches, 1 needs change, 2 unavailable.
        private static int PrintIface()
        {
            IfaceStatus status = IfaceStatusProvider.GetStatus();
            string activeType = status.ActiveFound
                ? (status.ActiveWireless ? "wifi" : "ethernet")
                : string.Empty;
            string configType = status.HasConfig
                ? (status.ConfigWireless ? "wifi" : "ethernet")
                : string.Empty;

            Console.WriteLine("active=" + activeType);
            Console.WriteLine("active_display=" + (status.ActiveFound ? status.ActiveDisplay : string.Empty));
            Console.WriteLine("active_index=" + (status.ActiveFound ? status.ActiveIndex.ToString() : string.Empty));
            Console.WriteLine("active_wireless=" + (status.ActiveWireless ? "true" : "false"));
            Console.WriteLine("service_found=" + (status.ServiceFound ? "true" : "false"));
            Console.WriteLine("config=" + configType);
            Console.WriteLine("config_display=" + (status.HasConfig ? status.ConfigDisplay : string.Empty));
            Console.WriteLine("config_index=" + (status.HasConfig ? status.ConfigIndex.ToString() : string.Empty));
            Console.WriteLine("config_wireless=" + (status.ConfigWireless ? "true" : "false"));
            Console.WriteLine("needs_change=" + (status.NeedsChange ? "true" : "false"));

            if (!status.ActiveFound || !status.ServiceFound)
            {
                return 2;
            }
            return status.NeedsChange ? 1 : 0;
        }

        // 0 ok, 1 changed but the config still differs, 3 admin required,
        // 4 service/interface not found, 5 access denied, 6 timeout, 7 error,
        // 64 invalid arguments.
        private static int SyncIface(string[] args)
        {
            int waitSeconds = 0;
            for (int i = 1; i < args.Length; i++)
            {
                string argument = args[i];
                if (argument.StartsWith("--wait=", StringComparison.OrdinalIgnoreCase))
                {
                    if (!int.TryParse(argument.Substring(7), out waitSeconds) ||
                        waitSeconds < 0 || waitSeconds > 600)
                    {
                        Console.Error.WriteLine("error=invalid_wait");
                        return 64;
                    }
                }
                else
                {
                    Console.Error.WriteLine("error=unknown_argument");
                    return 64;
                }
            }

            if (!RequireAdmin())
            {
                return 3;
            }

            IfaceStatus before = IfaceStatusProvider.GetStatus();
            if (!before.ServiceFound)
            {
                Console.Error.WriteLine("error=service_not_found");
                return 4;
            }
            if (!before.ActiveFound && waitSeconds == 0)
            {
                Console.Error.WriteLine("error=no_active_interface");
                return 4;
            }

            try
            {
                IfaceSyncResult result = IfaceStatusProvider.SyncInterface(waitSeconds);
                Console.WriteLine("active_index=" + result.ActiveIndex);
                Console.WriteLine("old_config_index=" +
                    (result.OldConfigIndex >= 0 ? result.OldConfigIndex.ToString() : string.Empty));
                Console.WriteLine("image_changed=" + (result.ImagePathChanged ? "true" : "false"));
                Console.WriteLine("strategy=" + (result.StrategyPath ?? string.Empty));
                Console.WriteLine("strategy_changed=" + (result.StrategyChanged ? "true" : "false"));
                Console.WriteLine("strategy_error=" + (result.StrategyError ?? string.Empty));
                Console.WriteLine("restarted=" + (result.ServiceRestarted ? "true" : "false"));
                Console.WriteLine("needs_change=" + (result.NeedsChange ? "true" : "false"));
                Console.WriteLine("log=" + (result.LogPath ?? string.Empty));
                return result.NeedsChange ? 1 : 0;
            }
            catch (System.ServiceProcess.TimeoutException)
            {
                Console.Error.WriteLine("error=timeout");
                return 6;
            }
            catch (UnauthorizedAccessException)
            {
                Console.Error.WriteLine("error=access_denied");
                return 5;
            }
            catch (InvalidOperationException ex)
            {
                IfaceStatus failed = IfaceStatusProvider.GetStatus();
                if (!failed.ServiceFound)
                {
                    Console.Error.WriteLine("error=service_not_found");
                    return 4;
                }
                if (!failed.ActiveFound)
                {
                    Console.Error.WriteLine("error=no_active_interface");
                    return 4;
                }
                Console.Error.WriteLine("error=sync_failed");
                Console.Error.WriteLine("detail=" + ex.Message);
                return 7;
            }
        }

        // 0 ok, 3 admin required, 7 error.
        private static int RegisterTask()
        {
            if (!RequireAdmin())
            {
                return 3;
            }
            try
            {
                SyncTask.Register();
                Console.WriteLine("task=" + SyncTask.TaskName);
                Console.WriteLine("registered=true");
                return 0;
            }
            catch (InvalidOperationException ex)
            {
                Console.Error.WriteLine("error=task_failed");
                Console.Error.WriteLine("detail=" + ex.Message);
                return 7;
            }
        }

        // 0 ok, 3 admin required, 7 error.
        private static int UnregisterTask()
        {
            if (!RequireAdmin())
            {
                return 3;
            }
            try
            {
                SyncTask.Unregister();
                Console.WriteLine("task=" + SyncTask.TaskName);
                Console.WriteLine("registered=false");
                return 0;
            }
            catch (InvalidOperationException ex)
            {
                Console.Error.WriteLine("error=task_failed");
                Console.Error.WriteLine("detail=" + ex.Message);
                return 7;
            }
        }

        private static int PrintStrategies()
        {
            string root = AppPaths.ResolvePayloadRoot();
            string current = ServiceManager.GetCurrentStrategyName();
            Console.WriteLine("root=" + root);
            Console.WriteLine("current=" + current);
            foreach (StrategyInfo strategy in StrategyParser.ListStrategies(root))
            {
                Console.WriteLine("strategy=" + strategy.Name +
                    (strategy.Name.Equals(current, StringComparison.OrdinalIgnoreCase)
                        ? " current=true"
                        : " current=false"));
            }
            return 0;
        }

        private static int PrintStrategyCommand(string name)
        {
            string root = AppPaths.ResolvePayloadRoot();
            string path = ServiceManager.FindStrategyPath(root, name);
            if (string.IsNullOrEmpty(path))
            {
                Console.Error.WriteLine("error=strategy_not_found");
                return 4;
            }
            GameFilterSettings game = GameFilterSettings.Load(root);
            int iface = AppPaths.GetServiceIfaceIndex();
            StrategyCommand command = StrategyParser.Build(root, path, game, iface);
            Console.WriteLine("strategy=" + command.StrategyName);
            Console.WriteLine("winws=" + command.WinwsPath);
            Console.WriteLine("iface=" + iface);
            Console.WriteLine("game=" + game.Display);
            Console.WriteLine("arguments=" + command.Arguments);
            Console.WriteLine("command=" + command.CommandLine);
            return 0;
        }

        private static int InstallService(string[] args)
        {            if (!RequireAdmin())
            {
                return 3;
            }
            Dictionary<string, string> options = ParseOptions(args, 1);
            if (options == null)
            {
                return 64;
            }
            string root = AppPaths.ResolvePayloadRoot();
            string requested;
            options.TryGetValue("--strategy", out requested);
            if (string.IsNullOrEmpty(requested))
            {
                requested = ServiceManager.GetCurrentStrategyName();
            }
            string strategy = ServiceManager.ChooseDefaultStrategy(root, requested);
            if (string.IsNullOrEmpty(strategy))
            {
                Console.Error.WriteLine("error=strategy_not_found");
                return 4;
            }
            bool start = !options.ContainsKey("--no-start");
            int iface = AppPaths.GetServiceIfaceIndex();
            IfaceStatus status = IfaceStatusProvider.GetStatus();
            if (status.ActiveFound)
            {
                iface = status.ActiveIndex;
            }
            ServiceInstallResult result = ServiceManager.Install(root, strategy, start, iface);
            Console.WriteLine("strategy=" + result.StrategyName);
            Console.WriteLine("started=" + (result.Started ? "true" : "false"));
            Console.WriteLine("service_running=" + (result.ServiceRunning ? "true" : "false"));
            Console.WriteLine("rolled_back=" + (result.RolledBack ? "true" : "false"));
            Console.WriteLine("command=" + result.CommandLine);
            if (!string.IsNullOrEmpty(result.Error))
            {
                Console.Error.WriteLine("detail=" + result.Error.Replace(
                    "\r\n", " | ").Replace('\n', ' ').Replace('\r', ' '));
            }
            if (start && !result.Started)
            {
                Console.Error.WriteLine("error=service_start_failed");
                return 5;
            }
            return 0;
        }

        private static int RemoveService()
        {
            if (!RequireAdmin())
            {
                return 3;
            }
            ServiceManager.Remove();
            Console.WriteLine("service=" + AppInfo.ServiceName);
            Console.WriteLine("removed=true");
            return 0;
        }

        private static int RestartService()
        {
            if (!RequireAdmin())
            {
                return 3;
            }
            ZapretStatus status = ZapretService.GetStatus();
            if (status.State == ZapretState.Running)
            {
                ZapretService.Stop();
                ZapretService.Start();
            }
            else if (status.State == ZapretState.Stopped)
            {
                ZapretService.Start();
            }
            else
            {
                Console.Error.WriteLine("error=service_not_settled");
                return 6;
            }
            Console.WriteLine("state=" + ZapretService.GetStatus().State);
            return 0;
        }

        private static int PrintGameFilter()
        {
            string root = AppPaths.ResolvePayloadRoot();
            GameFilterSettings game = GameFilterSettings.Load(root);
            Console.WriteLine("mode=" + game.Mode);
            Console.WriteLine("tcp=" + game.TcpRange);
            Console.WriteLine("udp=" + game.UdpRange);
            Console.WriteLine("effective_tcp=" + game.EffectiveTcp);
            Console.WriteLine("effective_udp=" + game.EffectiveUdp);
            Console.WriteLine("status=" + game.Display);
            return 0;
        }

        private static int SetGameFilter(string[] args)
        {
            if (!RequireAdmin())
            {
                return 3;
            }
            string mode = args[0].Substring("--set-game-filter=".Length).ToLowerInvariant();            if (mode != GameFilterSettings.ModeDisabled &&
                mode != GameFilterSettings.ModeAll &&
                mode != GameFilterSettings.ModeTcp &&
                mode != GameFilterSettings.ModeUdp)
            {
                Console.Error.WriteLine("error=invalid_mode");
                return 64;
            }
            Dictionary<string, string> options = ParseOptions(args, 1);
            if (options == null)
            {
                return 64;
            }
            string root = AppPaths.ResolvePayloadRoot();
            GameFilterSettings game = GameFilterSettings.Load(root);
            game.Mode = mode;
            string value;
            if (options.TryGetValue("--tcp", out value))
            {
                string range = GameFilterSettings.NormalizeRange(value);
                if (range == null)
                {
                    Console.Error.WriteLine("error=invalid_tcp_range");
                    return 64;
                }
                game.TcpRange = range;
            }
            if (options.TryGetValue("--udp", out value))
            {
                string range = GameFilterSettings.NormalizeRange(value);
                if (range == null)
                {
                    Console.Error.WriteLine("error=invalid_udp_range");
                    return 64;
                }
                game.UdpRange = range;
            }
            if (!game.Save(root))
            {
                Console.Error.WriteLine("error=save_failed");
                return 7;
            }
            if (options.ContainsKey("--apply"))
            {
                string strategy = ServiceManager.ChooseDefaultStrategy(
                    root, ServiceManager.GetCurrentStrategyName());
                if (string.IsNullOrEmpty(strategy))
                {
                    Console.Error.WriteLine("error=strategy_not_found");
                    return 4;
                }
                int iface = AppPaths.GetServiceIfaceIndex();
                IfaceStatus status = IfaceStatusProvider.GetStatus();
                if (status.ActiveFound)
                {
                    iface = status.ActiveIndex;
                }
                bool wasRunning = ZapretService.GetStatus().State == ZapretState.Running;
                ServiceInstallResult result = ServiceManager.Install(
                    root, strategy, wasRunning, iface);
                Console.WriteLine("started=" + (result.Started ? "true" : "false"));
                Console.WriteLine("service_running=" +
                    (result.ServiceRunning ? "true" : "false"));
                Console.WriteLine("rolled_back=" + (result.RolledBack ? "true" : "false"));
                if (!string.IsNullOrEmpty(result.Error))
                {
                    Console.Error.WriteLine("detail=" + result.Error.Replace(
                        "\r\n", " | ").Replace('\n', ' ').Replace('\r', ' '));
                    Console.WriteLine("applied=false");
                    return 5;
                }
                Console.WriteLine("applied=true");
            }
            Console.WriteLine("mode=" + game.Mode);
            Console.WriteLine("tcp=" + game.TcpRange);
            Console.WriteLine("udp=" + game.UdpRange);
            return 0;
        }

        private static int PrintIpset()
        {
            string root = AppPaths.ResolvePayloadRoot();
            string state = IpsetManager.GetState(root);
            Console.WriteLine("state=" + state);
            Console.WriteLine("display=" + IpsetManager.GetDisplay(state));
            Console.WriteLine("list=" + IpsetManager.GetListPath(root));
            Console.WriteLine("backup=" + IpsetManager.GetBackupPath(root));
            return 0;
        }

        private static int SetIpset(string state)
        {
            if (!RequireAdmin())
            {
                return 3;
            }
            string normalized = (state ?? string.Empty).Trim().ToLowerInvariant();
            string root = AppPaths.ResolvePayloadRoot();
            try
            {
                string result = IpsetManager.SetState(root, normalized);
                Console.WriteLine("state=" + result);
                return 0;
            }
            catch (ArgumentException)
            {
                Console.Error.WriteLine("error=invalid_state");
                return 64;
            }
        }

        private static int UpdateIpset()
        {
            if (!RequireAdmin())
            {
                return 3;
            }
            string root = AppPaths.ResolvePayloadRoot();
            IpsetUpdateResult result = IpsetManager.Update(root, null);
            Console.WriteLine("lines=" + result.Lines);
            Console.WriteLine("state=" + result.State);
            Console.WriteLine("backup=" + result.BackupPath);
            Console.WriteLine("warning=" + (result.Warning ?? string.Empty));
            return 0;
        }

        private static int PrintFakes()
        {
            string root = AppPaths.ResolvePayloadRoot();
            Console.WriteLine("discord=" + FakesManager.DescribeActive(root, FakesManager.DiscordKind));
            Console.WriteLine("game=" + FakesManager.DescribeActive(root, FakesManager.GameKind));
            foreach (FakeFile fake in FakesManager.List(root))
            {
                Console.WriteLine("fake=" + fake.Name);
                Console.WriteLine("sha256=" + fake.Hash);
                Console.WriteLine("size=" + fake.Size);
            }
            return 0;
        }

        private static int ReplaceFake(string[] args, string kind)
        {
            if (!RequireAdmin())
            {
                return 3;
            }
            Dictionary<string, string> options = ParseOptions(args, 1);
            if (options == null)
            {
                return 64;
            }
            string file;
            if (!options.TryGetValue("--file", out file) || string.IsNullOrEmpty(file))
            {
                Console.Error.WriteLine("error=file_required");
                return 64;
            }
            string root = AppPaths.ResolvePayloadRoot();
            string backup = FakesManager.Replace(root, kind, file);
            Console.WriteLine("kind=" + kind);
            Console.WriteLine("file=" + file);
            Console.WriteLine("backup=" + (backup ?? string.Empty));
            Console.WriteLine("restart_required=true");
            return 0;
        }

        private static int CheckHosts()
        {
            HostsCheckResult result = HostsManager.Check();
            if (result.Error != null)
            {
                Console.Error.WriteLine("error=" + result.Error);
                return 7;
            }
            Console.WriteLine("needs_update=" + (result.NeedsUpdate ? "true" : "false"));
            Console.WriteLine("managed_block=" + (result.ManagedBlockFound ? "true" : "false"));
            Console.WriteLine("preview=" + (result.Preview ?? string.Empty));
            return result.NeedsUpdate ? 1 : 0;
        }

        private static int ApplyHosts()
        {
            if (!RequireAdmin())
            {
                return 3;
            }
            HostsCheckResult check = HostsManager.Check();
            if (check.Error != null)
            {
                Console.Error.WriteLine("error=" + check.Error);
                return 7;
            }
            if (string.IsNullOrEmpty(check.UpstreamText))
            {
                Console.Error.WriteLine("error=download_failed");
                return 7;
            }
            HostsApplyResult result = HostsManager.Apply(check.UpstreamText);
            Console.WriteLine("lines=" + result.Lines);
            Console.WriteLine("backup=" + (result.BackupPath ?? string.Empty));
            return 0;
        }

        private static int ClearDiscordCache()
        {
            if (!RequireAdmin())
            {
                return 3;
            }
            DiscordCacheResult result = DiscordCache.Clear();
            foreach (string message in result.Messages)
            {
                Console.WriteLine("message=" + message);
            }
            Console.WriteLine("deleted_directories=" + result.DeletedDirectories);
            return 0;
        }

        private static int PrintDiagnostics()
        {
            DiagnosticReport report = Diagnostics.Run(AppPaths.ResolvePayloadRoot());
            Console.WriteLine("warnings=" + report.Count(DiagnosticState.Warning));
            Console.WriteLine("fails=" + report.Count(DiagnosticState.Fail));
            foreach (DiagnosticItem item in report.Items)
            {
                Console.WriteLine("check=" + item.Symbol + " " + item.Name + ": " + item.Detail);
            }
            return report.Count(DiagnosticState.Fail) > 0 ? 1 : 0;
        }

        private static bool TryParseGate(string[] args, out int gate)
        {
            gate = 16;
            if (args == null)
            {
                return true;
            }
            for (int i = 1; i < args.Length; i++)
            {
                string arg = args[i];
                if (!arg.StartsWith("--gate=", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                int parsed;
                if (!int.TryParse(
                        arg.Substring("--gate=".Length),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out parsed) ||
                    parsed < 1 || parsed > 16)
                {
                    return false;
                }
                gate = parsed;
            }
            return true;
        }

        // Full advisor run: stops the service, tests the whole .bat pool and
        // restores the service and IPSet in finally. Requires admin.
        private static int RunAdvisor(int gateLimit)
        {
            if (!RequireAdmin())
            {
                return 3;
            }
            Console.WriteLine("gate=" + gateLimit);
            StrategyAdviceOptions options = new StrategyAdviceOptions();
            options.GateLimit = gateLimit;
            StrategyAdviceReport report = StrategyAdvisor.Recommend(
                AppPaths.ResolvePayloadRoot(),
                options,
                delegate(string message)
                {
                    Console.WriteLine("progress=" + message);
                });
            foreach (StrategyProbeResult result in report.Results)
            {
                Console.WriteLine(
                    "strategy=" + result.Name +
                    " status=" + (result.Ok ? "full" :
                        (result.PartiallyOk ? "partial" : "fail")) +
                    " passed=" + result.Passed + "/" + result.Total +
                    " discord=" + result.DiscordPassed + "/" + result.DiscordTotal +
                    " youtube_hosts=" + result.YouTubeHostsPassed +
                    "/" + result.YouTubeHostsTotal +
                    " page=" + OkFail(result.YouTubePageOk) +
                    " player=" + OkFail(result.YouTubePlayerOk) +
                    " video=" + result.VideoSuccesses + "/" + result.VideoAttempts +
                    " google=" + result.GooglePassed + "/" + result.GoogleTotal +
                    " freeze=" + result.FreezeDisplay +
                    " tls12=" + result.Tls12Display +
                    " tls13=" + result.Tls13Display +
                    " elapsed_ms=" + result.ElapsedMs);
            }
            Console.WriteLine("recommended=" + report.RecommendedName);
            Console.WriteLine("recommendation_full=" +
                (report.HasRecommendation ? "true" : "false"));
            Console.WriteLine("best_partial=" + report.BestName);
            Console.WriteLine("ipset_mode=" + report.IpsetMode);
            Console.WriteLine("ipset_original=" + report.IpsetOriginal);
            Console.WriteLine("ipset_restored=" +
                (report.IpsetRestored ? "true" : "false"));
            Console.WriteLine("curl_available=" +
                (report.CurlAvailable ? "true" : "false"));
            Console.WriteLine("suite_offline=" +
                (report.FreezeSuiteOffline ? "true" : "false"));
            Console.WriteLine("elapsed_ms=" + report.ElapsedMs);
            if (!string.IsNullOrEmpty(report.Error))
            {
                Console.Error.WriteLine("error=" + report.Error);
            }
            bool saved = report.Results.Count > 0 &&
                         StrategyReportStore.Save(report);
            Console.WriteLine("report_saved=" + (saved ? "true" : "false"));
            return report.HasRecommendation ? 0 : 1;
        }

        // The old names stay as aliases. They no longer run a separate DPI or
        // standard mode: both map to the unified check on the current strategy.
        private static int RunLegacyTests(string mode)
        {
            if (mode != "standard" && mode != "dpi")
            {
                Console.Error.WriteLine("error=unknown_test_mode");
                return 64;
            }
            Console.WriteLine("note=legacy --test=" + mode +
                " mapped to the unified single-strategy check");
            ConnectionTestReport report = ConnectionTests.RunSingle();
            foreach (ConnectionTestResult result in report.Results)
            {
                Console.WriteLine("test=" + (result.Success ? "OK" : "FAIL") +
                    " " + result.Name + " " + result.Detail);
            }
            if (!string.IsNullOrEmpty(report.Notes))
            {
                Console.WriteLine("note_detail=" + report.Notes);
            }
            Console.WriteLine("passed=" + report.Passed + "/" + report.Results.Count);
            return report.Passed == report.Results.Count ? 0 : 1;
        }

        private static string OkFail(bool value)
        {
            return value ? "OK" : "FAIL";
        }

        private static int ProbeYouTube()
        {
            YouTubeProbeResult result = YouTubeProbe.Run(4000, 3);
            Console.WriteLine("page=" + (result.PageOk ? "OK" : "FAIL"));
            Console.WriteLine("page_detail=" + result.PageDetail);
            Console.WriteLine("player=" + (result.PlayerOk ? "OK" : "FAIL"));
            Console.WriteLine("player_detail=" + result.PlayerDetail);
            Console.WriteLine("video=" + (result.CdnOk ? "OK" : "FAIL"));
            Console.WriteLine("video_detail=" + result.CdnDetail);
            Console.WriteLine("attempts=" + result.Attempts);
            Console.WriteLine("successes=" + result.Successes);
            Console.WriteLine("bytes=" + result.Bytes);
            Console.WriteLine("tls12=" + (result.Tls12Attempted
                ? OkFail(result.Tls12Ok)
                : "n/a"));
            Console.WriteLine("tls13=" + (result.Tls13Attempted
                ? OkFail(result.Tls13Ok)
                : "n/a"));
            foreach (YouTubeVideoResult video in result.Videos)
            {
                Console.WriteLine("video_result=" + video.Display);
            }
            Console.WriteLine("elapsed_ms=" + result.ElapsedMs);
            return result.CdnOk ? 0 : 1;
        }

        private static int CheckTelegram()
        {
            TelegramProbeReport report;
            using (NetworkGate gate = new NetworkGate(16))
            {
                report = TelegramProbe.Run(gate, 4000);
            }
            foreach (TelegramProbeItem item in report.Items)
            {
                Console.WriteLine("check=" + (item.Success ? "OK" : "FAIL") +
                    " " + item.Name + " " + item.Detail +
                    (item.Informational ? " info=true" : string.Empty));
            }
            Console.WriteLine("dc=" + report.DcPassed + "/" + report.DcTotal);
            Console.WriteLine("web=" + (report.WebOk ? "OK" : "FAIL"));
            Console.WriteLine("doh=" + (report.DohOk ? "OK" : "FAIL"));
            Console.WriteLine("telegram=" + (report.Ok ? "OK" : "FAIL"));
            return report.Ok ? 0 : 1;
        }

        private static int SelfTestTelegramProxy()
        {
            TgProxyService service = new TgProxyService();
            service.PublishStatus = false;
            service.LogLine += delegate(string message)
            {
                Console.WriteLine("log=" + message);
            };
            try
            {
                service.Configure(0, null);
                if (!service.Start())
                {
                    Console.Error.WriteLine(
                        "error=" + (service.StartError ?? "start_failed"));
                    return 1;
                }
                Console.WriteLine("port=" + service.Port);
                Console.WriteLine("secret=dd" + service.SecretHex);
                Console.WriteLine("link=" + service.ProxyLink);
                string upstream = service.SelfTestAsync(
                    System.Threading.CancellationToken.None)
                    .GetAwaiter().GetResult();
                Console.WriteLine("upstream=" + upstream);
                string bridge = service.BridgeSelfTestAsync(
                    2, System.Threading.CancellationToken.None)
                    .GetAwaiter().GetResult();
                Console.WriteLine("bridge=" + bridge);
                return bridge.StartsWith("МОСТ OK", StringComparison.Ordinal)
                    ? 0
                    : 1;
            }
            finally
            {
                service.Dispose();
            }
        }

        private static int PrintTelegramStatus()
        {
            TgProxyStatusInfo status = TgProxyService.ReadStatusFile();
            Console.WriteLine("running=" + (status.Running ? "true" : "false"));
            Console.WriteLine("port=" +
                (status.Port > 0 ? status.Port.ToString() : string.Empty));
            Console.WriteLine("pid=" +
                (status.Pid > 0 ? status.Pid.ToString() : string.Empty));
            Console.WriteLine("secret=" + (status.Secret ?? string.Empty));
            Console.WriteLine("started_utc=" + (status.StartedUtc ?? string.Empty));
            return status.Running ? 0 : 1;
        }

        private static int BridgeTelegramProxy()
        {
            TgProxyStatusInfo status = TgProxyService.ReadStatusFile();
            if (!status.Running)
            {
                Console.Error.WriteLine("error=proxy_not_running");
                return 4;
            }
            string secret = status.Secret;
            if (string.IsNullOrEmpty(secret))
            {
                Console.Error.WriteLine("error=secret_missing");
                return 4;
            }
            if (secret.StartsWith("dd", StringComparison.Ordinal))
            {
                secret = secret.Substring(2);
            }
            byte[] key = TgProtocol.FromHex(secret);
            if (key == null || key.Length != 16)
            {
                Console.Error.WriteLine("error=invalid_secret");
                return 4;
            }
            Console.WriteLine("port=" + status.Port);
            string verdict = TgProxyService.ProbeBridgeAsync(
                "127.0.0.1", status.Port, key, 2,
                System.Threading.CancellationToken.None,
                delegate(string message)
                {
                    Console.WriteLine("log=" + message);
                }).GetAwaiter().GetResult();
            Console.WriteLine("bridge=" + verdict);
            return verdict.StartsWith("МОСТ OK", StringComparison.Ordinal)
                ? 0
                : 1;
        }

        private static int PrintTelegramLink()
        {            SettingsData settings = Settings.Load();
            TgProxyStatusInfo status = TgProxyService.ReadStatusFile();
            int port = status.Port > 0 ? status.Port : settings.TgProxyPort;
            string secret = status.Secret;
            if (string.IsNullOrEmpty(secret))
            {
                secret = settings.TgProxySecret;
                if (!string.IsNullOrEmpty(secret) &&
                    !secret.StartsWith("dd", StringComparison.Ordinal))
                {
                    secret = "dd" + secret;
                }
            }
            if (string.IsNullOrEmpty(secret))
            {
                // Stable secret before the GUI ever starts: the proxy link is
                // useless if it changes on every launch.
                string hex = TgProtocol.ToHex(TgProtocol.RandomBytes(16));
                settings.TgProxySecret = hex;
                Settings.Save();
                secret = "dd" + hex;
            }
            Console.WriteLine("running=" +
                (status.Running ? "true" : "false"));
            Console.WriteLine("server=127.0.0.1");
            Console.WriteLine("port=" + port);
            Console.WriteLine("secret=" + (secret ?? string.Empty));
            if (!string.IsNullOrEmpty(secret))
            {
                Console.WriteLine("link=tg://proxy?server=127.0.0.1&port=" +
                    port + "&secret=" + secret);
            }
            return 0;
        }

        private static int CheckUpdates()
        {
            string root = AppPaths.ResolvePayloadRoot();
            UpdateCheckResult result = UpdateService.Check(
                Settings.Load().ManifestUrl, root);
            Console.WriteLine("current_app=" + result.CurrentAppVersion);
            Console.WriteLine("latest_app=" + result.LatestAppVersion);
            Console.WriteLine("current_payload=" + result.CurrentPayloadVersion);
            Console.WriteLine("latest_payload=" + result.LatestPayloadVersion);
            Console.WriteLine("app_update=" + (result.AppUpdateAvailable ? "true" : "false"));
            Console.WriteLine("payload_update=" + (result.PayloadUpdateAvailable ? "true" : "false"));
            Console.WriteLine("manifest_available=" + (result.ManifestAvailable ? "true" : "false"));
            Console.WriteLine("manifest_error=" + (result.ManifestError ?? string.Empty));
            return 0;
        }

        private static int UpdatePayload()
        {
            if (!RequireAdmin())
            {
                return 3;
            }
            string root = AppPaths.ResolvePayloadRoot();
            UpdateCheckResult check = UpdateService.Check(
                Settings.Load().ManifestUrl, root);
            if (!check.PayloadUpdateAvailable)
            {
                Console.Error.WriteLine("error=no_payload_update");
                if (!string.IsNullOrEmpty(check.ManifestError))
                {
                    Console.Error.WriteLine("detail=" + check.ManifestError);
                }
                return 4;
            }
            PayloadUpdateResult result = UpdateService.ApplyPayload(check, root, null);
            Console.WriteLine("old_version=" + result.OldVersion);
            Console.WriteLine("new_version=" + result.NewVersion);
            Console.WriteLine("strategy=" + result.StrategyName);
            Console.WriteLine("backup=" + (result.BackupPath ?? string.Empty));
            Console.WriteLine("warning=" + (result.Warning ?? string.Empty));
            return 0;
        }

        private static int UpdateApplication()
        {
            if (!RequireAdmin())
            {
                return 3;
            }
            string root = AppPaths.ResolvePayloadRoot();
            UpdateCheckResult check = UpdateService.Check(
                Settings.Load().ManifestUrl, root);
            if (!check.AppUpdateAvailable)
            {
                Console.Error.WriteLine("error=no_app_update");
                if (!string.IsNullOrEmpty(check.ManifestError))
                {
                    Console.Error.WriteLine("detail=" + check.ManifestError);
                }
                return 4;
            }
            AppUpdateLaunch launch = UpdateService.ApplyAppUpdate(check, null);
            Console.WriteLine("setup=" + launch.SetupPath);
            Console.WriteLine("pid=" + launch.ProcessId);
            Console.WriteLine("unsigned=" + (launch.Unsigned ? "true" : "false"));
            Console.WriteLine("app_will_close=true");
            return 0;
        }

        private static int RollbackPayload()
        {
            if (!RequireAdmin())
            {
                return 3;
            }
            PayloadUpdateResult result = PayloadManager.RestoreLatestBackup(true);
            Console.WriteLine("version=" + result.NewVersion);
            Console.WriteLine("strategy=" + result.StrategyName);
            Console.WriteLine("failed_copy=" + (result.BackupPath ?? string.Empty));
            return 0;
        }

        private static bool BytesEqual(byte[] left, byte[] right)
        {
            return TgProtocol.Equal(left, right);
        }

        private static bool RequireAdmin()
        {
            if (AdminCheck.IsAdministrator())
            {
                return true;
            }
            Console.Error.WriteLine("error=administrator_required");
            return false;
        }

        private static Dictionary<string, string> ParseOptions(string[] args, int start)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            for (int i = start; i < args.Length; i++)
            {
                string argument = args[i] ?? string.Empty;
                if (!argument.StartsWith("--", StringComparison.Ordinal))
                {
                    Console.Error.WriteLine("error=unknown_argument");
                    return null;
                }
                int separator = argument.IndexOf('=');
                if (separator < 0)
                {
                    result[argument] = string.Empty;
                }
                else
                {
                    result[argument.Substring(0, separator)] =
                        argument.Substring(separator + 1);
                }
            }
            return result;
        }

        private static int CheckManifest(string path)
        {
            if (!File.Exists(path))
            {
                Console.Error.WriteLine("error=manifest_not_found");
                return 4;
            }
            UpdateManifest manifest = UpdateManifest.FromFile(path);
            Console.WriteLine("schema=" + manifest.Schema);
            Console.WriteLine("app_version=" + manifest.AppVersion);
            Console.WriteLine("app_setup_url=" + manifest.AppSetupUrl);
            Console.WriteLine("app_sha256=" + manifest.AppSha256);
            Console.WriteLine("payload_version=" + manifest.PayloadVersion);
            Console.WriteLine("payload_url=" + manifest.PayloadUrl);
            Console.WriteLine("payload_sha256=" + manifest.PayloadSha256);
            Console.WriteLine("min_app_version=" + manifest.MinAppVersion);
            if (!UpdateManifest.IsSha256(manifest.AppSha256) ||
                !UpdateManifest.IsSha256(manifest.PayloadSha256))
            {
                Console.Error.WriteLine("error=invalid_sha256");
                return 1;
            }
            return 0;
        }

        // Exercises ZIP extraction, payload validation, atomic swap, backup and
        // user-list preservation in a temporary folder without touching the SCM.
        private static int SelfTestPayload()
        {
            string temp = Path.Combine(
                Path.GetTempPath(), "zapret-control-payload-" + Guid.NewGuid().ToString("N"));
            try
            {
                string source = Path.Combine(temp, "source");
                AppPaths.EnsureDirectory(Path.Combine(source, "bin"));
                AppPaths.EnsureDirectory(Path.Combine(source, "lists"));
                AppPaths.EnsureDirectory(Path.Combine(source, "utils"));
                File.WriteAllText(Path.Combine(source, "service.bat"),
                    "set \"LOCAL_VERSION=9.9.9\"" + Environment.NewLine);
                File.WriteAllText(Path.Combine(source, "general.bat"),
                    "start \"zapret\" /min \"%BIN%winws.exe\" --wf-iface=1 " +
                    "--wf-tcp=%GameFilterTCP%" + Environment.NewLine);
                File.WriteAllBytes(Path.Combine(source, @"bin\winws.exe"),
                    new byte[] { 0x4D, 0x5A, 0x00, 0x00 });
                File.WriteAllBytes(Path.Combine(source, @"bin\WinDivert64.sys"),
                    new byte[] { 0x00, 0x01 });
                File.WriteAllText(Path.Combine(source, @"lists\ipset-all.txt"),
                    "1.1.1.0/24" + Environment.NewLine);
                File.WriteAllText(Path.Combine(source, @"utils\game_filter.enabled"),
                    "mode=disabled" + Environment.NewLine);

                string zip1 = Path.Combine(temp, "one.zip");
                System.IO.Compression.ZipFile.CreateFromDirectory(
                    source, zip1, System.IO.Compression.CompressionLevel.Optimal, false);
                string target = Path.Combine(temp, "target");
                string before = ZapretService.GetStatus().State.ToString();
                PayloadManager.UpdateFromZip(target, zip1, null, false, null);
                if (!File.Exists(Path.Combine(target, @"bin\winws.exe")) ||
                    PayloadManager.GetVersion(target) != "9.9.9")
                {
                    Console.Error.WriteLine("selftest-payload failed: first install");
                    return 1;
                }

                File.WriteAllText(Path.Combine(target, @"lists\list-general-user.txt"),
                    "user.example" + Environment.NewLine);
                string service = Path.Combine(source, "service.bat");
                File.WriteAllText(service,
                    "set \"LOCAL_VERSION=9.9.10\"" + Environment.NewLine);
                string zip2 = Path.Combine(temp, "two.zip");
                System.IO.Compression.ZipFile.CreateFromDirectory(
                    source, zip2, System.IO.Compression.CompressionLevel.Optimal, false);
                PayloadManager.UpdateFromZip(target, zip2, null, false, null);
                if (PayloadManager.GetVersion(target) != "9.9.10" ||
                    !File.Exists(Path.Combine(target, @"lists\list-general-user.txt")) ||
                    File.ReadAllText(Path.Combine(target, @"lists\list-general-user.txt"))
                        .IndexOf("user.example", StringComparison.Ordinal) < 0)
                {
                    Console.Error.WriteLine("selftest-payload failed: update or user lists");
                    return 1;
                }
                if (PayloadManager.ListBackups(target).Count == 0)
                {
                    Console.Error.WriteLine("selftest-payload failed: backup was not created");
                    return 1;
                }
                if (ZapretService.GetStatus().State.ToString() != before)
                {
                    Console.Error.WriteLine("selftest-payload failed: service state changed");
                    return 1;
                }
                Console.WriteLine("selftest-payload=OK");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("selftest-payload failed: " + ex.Message);
                return 1;
            }
            finally
            {
                PayloadManager.TryDeleteDirectory(temp);
            }
        }

        // Runs without SCM access: verifies state mapping and pure helpers.
        private static int SelfTest()
        {            ServiceControllerStatus[] statuses = new ServiceControllerStatus[]
            {
                ServiceControllerStatus.Running,
                ServiceControllerStatus.Stopped,
                ServiceControllerStatus.StartPending,
                ServiceControllerStatus.StopPending,
                ServiceControllerStatus.Paused,
                ServiceControllerStatus.ContinuePending
            };
            ZapretState[] expected = new ZapretState[]
            {
                ZapretState.Running,
                ZapretState.Stopped,
                ZapretState.Starting,
                ZapretState.Stopping,
                ZapretState.Paused,
                ZapretState.Starting
            };

            for (int i = 0; i < statuses.Length; i++)
            {
                ZapretState actual = ZapretService.Map(statuses[i]);
                if (actual != expected[i])
                {
                    Console.Error.WriteLine(
                        "selftest failed: " + statuses[i] + " -> " + actual + ", expected " + expected[i]);
                    return 1;
                }
            }

            int parsedIndex;
            if (!IfaceStatusProvider.TryParseWfIface(
                    "\"C:\\x\\winws.exe\" --wf-iface=11 --wf-tcp=80", out parsedIndex) ||
                parsedIndex != 11)
            {
                Console.Error.WriteLine("selftest failed: --wf-iface=11 was not parsed");
                return 1;
            }
            if (IfaceStatusProvider.TryParseWfIface("\"C:\\x\\winws.exe\" --wf-tcp=80", out parsedIndex))
            {
                Console.Error.WriteLine("selftest failed: a config without --wf-iface was accepted");
                return 1;
            }
            if (!IfaceStatusProvider.TryParseWfIface(
                    "\"C:\\x\\winws.exe\" --WF-IFACE=8", out parsedIndex) ||
                parsedIndex != 8)
            {
                Console.Error.WriteLine("selftest failed: uppercase --WF-IFACE=8 was not parsed");
                return 1;
            }
            if (IfaceStatusProvider.TryParseWfIface(
                    "\"C:\\x\\winws.exe\" --wf-iface=8x --wf-tcp=80", out parsedIndex))
            {
                Console.Error.WriteLine("selftest failed: --wf-iface=8x was accepted");
                return 1;
            }

            string imagePath = IfaceStatusProvider.BuildImagePath(
                "\"C:\\x\\winws.exe\" --wf-iface=11 --wf-tcp=80", 7);
            if (imagePath != "\"C:\\x\\winws.exe\" --wf-iface=7 --wf-tcp=80")
            {
                Console.Error.WriteLine("selftest failed: ImagePath replacement: " + imagePath);
                return 1;
            }
            imagePath = IfaceStatusProvider.BuildImagePath(
                "\"C:\\x\\winws.exe\" --wf-tcp=80", 7);
            if (imagePath != "\"C:\\x\\winws.exe\" --wf-iface=7 --wf-tcp=80")
            {
                Console.Error.WriteLine("selftest failed: ImagePath insertion: " + imagePath);
                return 1;
            }
            imagePath = IfaceStatusProvider.BuildImagePath("\"C:\\x\\winws.exe\"", 7);
            if (imagePath != "\"C:\\x\\winws.exe\" --wf-iface=7")
            {
                Console.Error.WriteLine("selftest failed: ImagePath append: " + imagePath);
                return 1;
            }

            string strategy = IfaceStatusProvider.BuildStrategyText(
                "start \"zapret\" /min \"%BIN%winws.exe\" --wf-iface=11 --wf-tcp=80", 7);
            if (strategy != "start \"zapret\" /min \"%BIN%winws.exe\" --wf-iface=7 --wf-tcp=80")
            {
                Console.Error.WriteLine("selftest failed: strategy replacement: " + strategy);
                return 1;
            }
            strategy = IfaceStatusProvider.BuildStrategyText(
                "start \"zapret\" /min \"%BIN%winws.exe\" --wf-tcp=80", 7);
            if (strategy != "start \"zapret\" /min \"%BIN%winws.exe\" --wf-iface=7 --wf-tcp=80")
            {
                Console.Error.WriteLine("selftest failed: strategy insertion: " + strategy);
                return 1;
            }
            string ignored;
            if (IfaceStatusProvider.TryBuildStrategyText(
                    ":: winws.exe --wf-tcp=80\r\necho hi\r\n", 7, out ignored))
            {
                Console.Error.WriteLine("selftest failed: a commented winws line was rewritten");
                return 1;
            }

            string taskArguments = SyncTask.BuildCreateArguments(
                @"C:\Program Files\ZapretControl\ZapretControl.Cli.exe");
            if (taskArguments !=
                "/Create /TN \"Zapret-IfIndex-Sync\" /TR \"\\\"C:\\Program Files\\ZapretControl\\ZapretControl.Cli.exe\\\" --sync-iface --wait=120\" /SC ONLOGON /DELAY 0000:20 /RU SYSTEM /RL HIGHEST /F")
            {
                Console.Error.WriteLine("selftest failed: task arguments: " + taskArguments);
                return 1;
            }

            if (GameFilterSettings.NormalizeRange("1024-65535,80") != "1024-65535,80" ||
                GameFilterSettings.NormalizeRange("0-10") != null ||
                GameFilterSettings.NormalizeRange("65536") != null ||
                GameFilterSettings.NormalizeRange("100-50") != null ||
                GameFilterSettings.NormalizeRange("1024-65535,") != null)
            {
                Console.Error.WriteLine("selftest failed: GameFilterSettings.NormalizeRange");
                return 1;
            }
            if (StrategyParser.ApplyIface("--wf-iface=11 --wf-tcp=80", 7) !=
                "--wf-iface=7 --wf-tcp=80")
            {
                Console.Error.WriteLine("selftest failed: StrategyParser.ApplyIface");
                return 1;
            }
            GameFilterSettings game = new GameFilterSettings();
            game.Mode = GameFilterSettings.ModeAll;
            game.TcpRange = "1024-65535";
            string expanded = StrategyParser.ExpandVariables(
                "--hostlist=\"%LISTS%list.txt\" --wf-tcp=%GameFilterTCP%", @"C:\z", game);
            if (expanded != "--hostlist=\"C:\\z\\lists\\list.txt\" --wf-tcp=1024-65535")
            {
                Console.Error.WriteLine("selftest failed: StrategyParser.ExpandVariables: " + expanded);
                return 1;
            }
            string unescaped = StrategyParser.ExpandVariables(
                "--dpi-desync-fake-tls=^! --x=^^ \"a^b\"", @"C:\z", game);
            if (unescaped != "--dpi-desync-fake-tls=! --x=^ \"a^b\"")
            {
                Console.Error.WriteLine("selftest failed: StrategyParser.UnescapeCarets: " + unescaped);
                return 1;
            }
            if (StrategyParser.NaturalCompare("general (ALT2)", "general (ALT10)") >= 0)
            {
                Console.Error.WriteLine("selftest failed: natural strategy sort");
                return 1;
            }
            List<StrategyInfo> sample = new List<StrategyInfo>();
            sample.Add(new StrategyInfo("general (ALT2)", "C:\\z\\a.bat"));
            sample.Add(new StrategyInfo("general", "C:\\z\\b.bat"));
            sample.Add(new StrategyInfo("general (FAKE TLS AUTO)", "C:\\z\\c.bat"));
            sample.Add(new StrategyInfo("other", "C:\\z\\d.bat"));
            List<StrategyInfo> ordered = StrategyAdvisor.OrderCandidates(sample, "other");
            if (ordered.Count != 4 ||
                ordered[0].Name != "other" ||
                ordered[1].Name != "general" ||
                ordered[2].Name != "general (FAKE TLS AUTO)" ||
                ordered[3].Name != "general (ALT2)")
            {
                Console.Error.WriteLine("selftest failed: strategy advisor order");
                return 1;
            }
            if (new StrategyAdviceOptions().MaxCandidates != 0)
            {
                Console.Error.WriteLine("selftest failed: strategy advisor pool limit");
                return 1;
            }
            string playerJson = YouTubeProbe.ExtractPlayerResponse(
                "<script>var ytInitialPlayerResponse = " +
                "{\"playabilityStatus\":{\"status\":\"OK\"}," +
                "\"text\":\"}\"};</script>");
            if (playerJson == null || playerJson.IndexOf("\"OK\"", StringComparison.Ordinal) < 0)
            {
                Console.Error.WriteLine("selftest failed: YouTube player response parsing");
                return 1;
            }
            if (!FreezeProbe.IsLikelyBlocked(28, 65536, 0, 5.02, 5) ||
                FreezeProbe.IsLikelyBlocked(0, 65536, 0, 5.02, 5) ||
                FreezeProbe.IsLikelyBlocked(28, 65536, 1024, 5.02, 5) ||
                FreezeProbe.IsLikelyBlocked(28, 65536, 0, 4.1, 5))
            {
                Console.Error.WriteLine("selftest failed: DPI freeze detection");
                return 1;
            }
            if (!YouTubeProbe.CombineVideos(new int[] { 3, 1 }, 3) ||
                YouTubeProbe.CombineVideos(new int[] { 1, 0 }, 3) ||
                !YouTubeProbe.IsVideoOk(2, 3) ||
                YouTubeProbe.IsVideoOk(1, 3))
            {
                Console.Error.WriteLine("selftest failed: YouTube video verdict");
                return 1;
            }
            StandardProbeReport compat = new StandardProbeReport();
            for (int i = 0; i < 4; i++)
            {
                string host = "host" + i + ".example";
                compat.Results.Add(new HostProbeResult(
                    host, "Discord", "HTTP/1.1", true, "ok", 1));
                compat.Results.Add(new HostProbeResult(
                    host, "Discord", "TLS1.2", true, "ok", 1));
                compat.Results.Add(new HostProbeResult(
                    host, "Discord", "TLS1.3", false, "unsupported", 1));
            }
            compat.Tls13Unsupported = true;
            if (!compat.GroupOk("Discord") || compat.Passed("Discord") != 12)
            {
                Console.Error.WriteLine(
                    "selftest failed: TLS 1.3 unsupported compatibility");
                return 1;
            }
            compat.Tls13Unsupported = false;
            if (compat.GroupOk("Discord"))
            {
                Console.Error.WriteLine(
                    "selftest failed: TLS 1.3 failure accepted without reason");
                return 1;
            }

            List<StrategyProbeResult> ranked = new List<StrategyProbeResult>();
            StrategyProbeResult pages = new StrategyProbeResult(
                "pages", "C:\\z\\p.bat");
            pages.DiscordOk = true;
            pages.YouTubeHostsOk = true;
            pages.YouTubePageOk = true;
            pages.YouTubePlayerOk = true;
            pages.GoogleOk = true;
            pages.VideoSuccesses = 0;
            pages.VideoAttempts = 3;
            pages.SetCounts(12, 12, 12, 12, 6, 6, 6, 6);
            pages.SetFreeze(true, true, true, 0, 36, 108, 0, "ok");
            StrategyProbeResult video = new StrategyProbeResult(
                "video", "C:\\z\\v.bat");
            video.DiscordOk = true;
            video.YouTubeHostsOk = true;
            video.YouTubePageOk = true;
            video.YouTubePlayerOk = true;
            video.YouTubeVideoOk = true;
            video.GoogleOk = false;
            video.VideoSuccesses = 2;
            video.VideoAttempts = 3;
            video.SetCounts(12, 12, 12, 12, 5, 6, 6, 6);
            video.SetFreeze(true, true, true, 0, 36, 108, 0, "ok");
            StrategyProbeResult full = new StrategyProbeResult(
                "full", "C:\\z\\f.bat");
            full.DiscordOk = true;
            full.YouTubeHostsOk = true;
            full.YouTubePageOk = true;
            full.YouTubePlayerOk = true;
            full.YouTubeVideoOk = true;
            full.GoogleOk = true;
            full.VideoSuccesses = 3;
            full.VideoAttempts = 3;
            full.SetCounts(12, 12, 12, 12, 6, 6, 6, 6);
            full.SetFreeze(true, true, true, 0, 36, 108, 0, "ok");
            ranked.Add(pages);
            ranked.Add(video);
            ranked.Add(full);
            StrategyAdvisor.SortResults(ranked);
            if (ranked[0].Name != "full" ||
                ranked[1].Name != "video" ||
                ranked[2].Name != "pages")
            {
                Console.Error.WriteLine("selftest failed: strategy advisor ranking");
                return 1;
            }
            string reportPath = Path.Combine(
                Path.GetTempPath(),
                "ZapretControl-selftest-strategy-report-" +
                Guid.NewGuid().ToString("N") + ".json");
            try
            {
                StrategyAdviceReport savedReport = new StrategyAdviceReport();
                StrategyProbeResult savedResult = new StrategyProbeResult(
                    "general", "C:\\z\\g.bat");
                savedResult.DiscordOk = true;
                savedResult.YouTubeHostsOk = true;
                savedResult.YouTubePageOk = true;
                savedResult.YouTubePlayerOk = true;
                savedResult.YouTubeVideoOk = true;
                savedResult.GoogleOk = true;
                savedResult.VideoSuccesses = 3;
                savedResult.VideoAttempts = 6;
                savedResult.SetCounts(12, 12, 12, 12, 6, 6, 6, 6);
                savedResult.SetFreeze(true, true, true, 0, 36, 108, 0, "ok");
                savedReport.Results.Add(savedResult);
                savedReport.SetRecommended(savedResult);
                savedReport.SetIpset("loaded", "any");
                savedReport.SetIpsetSwitched(true);
                savedReport.SetIpsetRestored(true);
                savedReport.SetCurlAvailable(true);
                savedReport.SetCheckedAtUtc(
                    new DateTime(2026, 11, 15, 12, 30, 0, DateTimeKind.Utc));
                if (!StrategyReportStore.SaveToPath(savedReport, reportPath))
                {
                    Console.Error.WriteLine("selftest failed: strategy report save");
                    return 1;
                }
                StrategyAdviceReport loadedReport =
                    StrategyReportStore.LoadFromPath(reportPath);
                if (loadedReport == null ||
                    loadedReport.Results.Count != 1 ||
                    loadedReport.RecommendedName != "general" ||
                    !loadedReport.Results[0].Ok ||
                    loadedReport.IpsetMode != "any" ||
                    !loadedReport.IpsetRestored ||
                    !loadedReport.CurlAvailable ||
                    loadedReport.CheckedAtUtc.Kind != DateTimeKind.Utc ||
                    loadedReport.CheckedAtUtc !=
                        new DateTime(2026, 11, 15, 12, 30, 0, DateTimeKind.Utc))
                {
                    Console.Error.WriteLine("selftest failed: strategy report roundtrip");
                    return 1;
                }
                if (StrategyReportStore.SaveToPath(
                        new StrategyAdviceReport(), reportPath))
                {
                    Console.Error.WriteLine("selftest failed: empty strategy report saved");
                    return 1;
                }
                File.WriteAllText(reportPath, "{not a report");
                if (StrategyReportStore.LoadFromPath(reportPath) != null)
                {
                    Console.Error.WriteLine("selftest failed: corrupt strategy report accepted");
                    return 1;
                }
            }
            finally
            {
                try
                {
                    if (File.Exists(reportPath))
                    {
                        File.Delete(reportPath);
                    }
                }
                catch (Exception)
                {
                }
            }
            string ipsetRoot = Path.Combine(
                Path.GetTempPath(),
                "ZapretControl-selftest-ipset-" + Guid.NewGuid().ToString("N"));
            string ipsetFlag = Path.Combine(
                Path.GetTempPath(),
                "ZapretControl-selftest-ipset-" + Guid.NewGuid().ToString("N") + ".flag");
            try
            {
                Directory.CreateDirectory(Path.Combine(ipsetRoot, "lists"));
                Directory.CreateDirectory(Path.Combine(ipsetRoot, "bin"));
                File.WriteAllText(
                    Path.Combine(ipsetRoot, @"bin\winws.exe"),
                    string.Empty,
                    new UTF8Encoding(false));
                File.WriteAllText(
                    IpsetManager.GetListPath(ipsetRoot),
                    "1.1.1.1/32" + Environment.NewLine,
                    new UTF8Encoding(false));
                string originalIpset;
                if (!IpsetSwitch.Begin(ipsetRoot, ipsetFlag, out originalIpset) ||
                    originalIpset != IpsetManager.StateLoaded ||
                    IpsetManager.GetState(ipsetRoot) != IpsetManager.StateAny ||
                    !File.Exists(ipsetFlag))
                {
                    Console.Error.WriteLine("selftest failed: ipset switch to any");
                    return 1;
                }
                string restoreError = null;
                try
                {
                    throw new InvalidOperationException("simulated advisor failure");
                }
                catch (InvalidOperationException)
                {
                }
                finally
                {
                    restoreError = IpsetSwitch.Restore(
                        ipsetRoot, originalIpset, ipsetFlag);
                }
                if (restoreError != null)
                {
                    Console.Error.WriteLine(
                        "selftest failed: ipset restore: " + restoreError);
                    return 1;
                }
                if (IpsetManager.GetState(ipsetRoot) != IpsetManager.StateLoaded ||
                    File.Exists(ipsetFlag))
                {
                    Console.Error.WriteLine("selftest failed: ipset state after exception");
                    return 1;
                }
                if (!IpsetSwitch.Begin(ipsetRoot, ipsetFlag, out originalIpset))
                {
                    Console.Error.WriteLine("selftest failed: ipset second switch");
                    return 1;
                }
                if (IpsetSwitch.RecoverStale(ipsetFlag, ipsetRoot) != null ||
                    IpsetManager.GetState(ipsetRoot) != IpsetManager.StateLoaded ||
                    File.Exists(ipsetFlag))
                {
                    Console.Error.WriteLine("selftest failed: ipset stale recovery");
                    return 1;
                }
            }
            finally
            {
                try
                {
                    if (File.Exists(ipsetFlag))
                    {
                        File.Delete(ipsetFlag);
                    }
                    if (Directory.Exists(ipsetRoot))
                    {
                        Directory.Delete(ipsetRoot, true);
                    }
                }
                catch (Exception)
                {
                }
            }
            if (new SettingsData().FirstRunCompleted)
            {
                Console.Error.WriteLine("selftest failed: FirstRunCompleted default");
                return 1;
            }
            if (!ServiceManager.CanRollback(true, "C:\\z\\winws.exe") ||
                ServiceManager.CanRollback(false, "C:\\z\\winws.exe") ||
                ServiceManager.CanRollback(true, string.Empty))
            {
                Console.Error.WriteLine("selftest failed: ServiceManager.CanRollback");
                return 1;
            }
            if (!new SettingsData().AutoUpdatePayload)
            {
                Console.Error.WriteLine("selftest failed: AutoUpdatePayload default");
                return 1;
            }
            if (!HostsManager.IsValidHostsText(
                    "0.0.0.0 example.com\n2606:50c0:8000::154 example.org\n") ||
                HostsManager.IsValidHostsText("<html>not a hosts file</html>") ||
                HostsManager.IsValidHostsText("0.0.0.0\n"))
            {
                Console.Error.WriteLine("selftest failed: hosts validation");
                return 1;
            }
            ConnectionTestReport skippedTests = new ConnectionTestReport("dpi");
            skippedTests.Results.Add(new ConnectionTestResult("probe", true, "ok", 1));
            HomeCheckReport skipped = new HomeCheckReport(
                skippedTests, false, false, false, "не проверено", DateTime.Now);
            if (skipped.Total != 1 || skipped.Passed != 1 || !skipped.AllOk ||
                skipped.Summary != "1 из 1 · всё в порядке")
            {
                Console.Error.WriteLine("selftest failed: HomeCheck skipped hosts");
                return 1;
            }
            if (!OperationGuard.TryEnter() || !OperationGuard.IsBusy)
            {
                Console.Error.WriteLine("selftest failed: OperationGuard enter");
                return 1;
            }
            OperationGuard.Exit();
            if (OperationGuard.IsBusy)
            {
                Console.Error.WriteLine("selftest failed: OperationGuard exit");
                return 1;
            }
            ConnectionTestReport emptyTests = new ConnectionTestReport("dpi");
            HomeCheckReport home = new HomeCheckReport(
                emptyTests, true, true, false, "актуален", DateTime.Now);
            if (home.Summary != "1 из 1 · всё в порядке" || !home.AllOk)
            {
                Console.Error.WriteLine("selftest failed: HomeCheckReport summary");
                return 1;
            }
            if (FakesManager.GetActiveName("game") != FakesManager.GameActive ||
                FakesManager.GetActiveName("discord") != FakesManager.DiscordActive)
            {
                Console.Error.WriteLine("selftest failed: FakesManager.GetActiveName");
                return 1;
            }
            if (IpsetManager.GetDisplay(IpsetManager.StateNone) != "отключён")
            {
                Console.Error.WriteLine("selftest failed: IpsetManager.GetDisplay");
                return 1;
            }
            UpdateManifest manifest = UpdateManifest.Parse(
                "{\"schema\":1,\"app\":{\"version\":\"9.9.9\"," +
                "\"setup_url\":\"https://example.invalid/setup.exe\"," +
                "\"sha256\":\"" + new string('A', 64) + "\"}," +
                "\"payload\":{\"version\":\"9.9.9\"," +
                "\"url\":\"https://example.invalid/payload.zip\"," +
                "\"sha256\":\"" + new string('B', 64) + "\"}}");
            if (!manifest.HasApp || !manifest.HasPayload ||
                !AppInfo.IsNewer(manifest.AppVersion, "1.0.0") ||
                AppInfo.IsNewer("1.0.0", manifest.AppVersion))
            {
                Console.Error.WriteLine("selftest failed: UpdateManifest/version compare");
                return 1;
            }
            if (PayloadPin.Sha256.Length != 64 ||
                !HashUtil.Normalize(PayloadPin.Sha256).Equals(
                    PayloadPin.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine("selftest failed: PayloadPin hash");
                return 1;
            }

            byte[] aesKey = new byte[32];
            byte[] aesIv = new byte[16];
            for (int i = 0; i < aesKey.Length; i++)
            {
                aesKey[i] = (byte)(i * 7 + 1);
            }
            for (int i = 0; i < aesIv.Length; i++)
            {
                aesIv[i] = (byte)(255 - i);
            }
            byte[] tgSample = new byte[1000];
            for (int i = 0; i < tgSample.Length; i++)
            {
                tgSample[i] = (byte)(i * 13 + 5);
            }
            byte[] whole;
            using (TgAesCtr cipher = new TgAesCtr(aesKey, aesIv))
            {
                whole = cipher.Update(tgSample);
            }
            List<byte> streamed = new List<byte>();
            using (TgAesCtr cipher = new TgAesCtr(aesKey, aesIv))
            {
                streamed.AddRange(cipher.Update(
                    TgProtocol.Slice(tgSample, 0, 7)));
                streamed.AddRange(cipher.Update(
                    TgProtocol.Slice(tgSample, 7, 57)));
                streamed.AddRange(cipher.Update(
                    TgProtocol.Slice(tgSample, 64, tgSample.Length - 64)));
            }
            if (!BytesEqual(whole, streamed.ToArray()))
            {
                Console.Error.WriteLine("selftest failed: TgAesCtr streaming");
                return 1;
            }

            byte[] tgSecret = new byte[16];
            for (int i = 0; i < tgSecret.Length; i++)
            {
                tgSecret[i] = (byte)(i * 11 + 3);
            }
            TgHandshake handshake = TgProtocol.TryHandshake(
                TgProtocol.GenerateClientInit(
                    TgProtocol.ProtoTagSecure, 4, tgSecret), tgSecret);
            if (handshake == null || handshake.Dc != 4 ||
                handshake.IsMedia ||
                !TgProtocol.Equal(
                    handshake.ProtoTag, TgProtocol.ProtoTagSecure))
            {
                Console.Error.WriteLine(
                    "selftest failed: TgProtocol client handshake");
                return 1;
            }
            handshake = TgProtocol.TryHandshake(
                TgProtocol.GenerateClientInit(
                    TgProtocol.ProtoTagIntermediate, -2, tgSecret), tgSecret);
            if (handshake == null || handshake.Dc != 2 ||
                !handshake.IsMedia ||
                !TgProtocol.Equal(
                    handshake.ProtoTag, TgProtocol.ProtoTagIntermediate))
            {
                Console.Error.WriteLine(
                    "selftest failed: TgProtocol media handshake");
                return 1;
            }
            if (TgProtocol.TryHandshake(
                    TgProtocol.GenerateClientInit(
                        TgProtocol.ProtoTagSecure, 4, tgSecret),
                    new byte[16]) != null)
            {
                Console.Error.WriteLine(
                    "selftest failed: a wrong secret was accepted");
                return 1;
            }

            byte[] relayInit = TgProtocol.GenerateRelayInit(
                TgProtocol.ProtoTagIntermediate, 3);
            byte[] relayPlain;
            using (TgAesCtr decryptor = new TgAesCtr(
                TgProtocol.Slice(relayInit, TgProtocol.SkipLen,
                    TgProtocol.PrekeyLen),
                TgProtocol.Slice(relayInit,
                    TgProtocol.SkipLen + TgProtocol.PrekeyLen,
                    TgProtocol.IvLen)))
            {
                relayPlain = decryptor.Update(relayInit);
            }
            short relayDc = (short)(relayPlain[TgProtocol.DcIdxPos] |
                (relayPlain[TgProtocol.DcIdxPos + 1] << 8));
            if (!TgProtocol.Equal(
                    TgProtocol.Slice(relayPlain, TgProtocol.ProtoTagPos, 4),
                    TgProtocol.ProtoTagIntermediate) ||
                relayDc != 3)
            {
                Console.Error.WriteLine(
                    "selftest failed: TgProtocol relay init");
                return 1;
            }

            byte[] messageFrame = new byte[24];
            TgProtocol.WriteInt32LittleEndian(messageFrame, 0, 20);
            TgProtocol.WriteUInt32LittleEndian(messageFrame, 4, 0xbe7e8ef1);
            byte[] encryptedFrame;
            using (TgAesCtr encryptor = new TgAesCtr(
                TgProtocol.Slice(relayInit, TgProtocol.SkipLen,
                    TgProtocol.PrekeyLen),
                TgProtocol.Slice(relayInit,
                    TgProtocol.SkipLen + TgProtocol.PrekeyLen,
                    TgProtocol.IvLen)))
            {
                encryptor.Update(new byte[TgProtocol.HandshakeLen]);
                encryptedFrame = encryptor.Update(messageFrame);
            }
            using (TgMsgSplitter splitter = new TgMsgSplitter(
                relayInit, TgProtocol.ProtoIntermediateInt))
            {
                List<byte[]> parts = splitter.Split(encryptedFrame);
                if (parts.Count != 1 || parts[0].Length != 24 ||
                    !BytesEqual(parts[0], encryptedFrame))
                {
                    Console.Error.WriteLine(
                        "selftest failed: TgMsgSplitter framing");
                    return 1;
                }
            }

            string[] fronts = TgFronts.AllBaseDomains;
            if (fronts.Length != 20)
            {
                Console.Error.WriteLine(
                    "selftest failed: TgFronts domain count");
                return 1;
            }
            for (int i = 0; i < fronts.Length; i++)
            {
                if (!fronts[i].EndsWith(".co.uk", StringComparison.Ordinal))
                {
                    Console.Error.WriteLine(
                        "selftest failed: TgFronts domain " + fronts[i]);
                    return 1;
                }
            }
            string[] coverage = TelegramCoverage.Domains();
            if (coverage.Length < 100 ||
                Array.IndexOf(coverage, "kws2.pclead.co.uk") < 0)
            {
                Console.Error.WriteLine(
                    "selftest failed: TelegramCoverage domains");
                return 1;
            }
            string coverageRoot = Path.Combine(
                Path.GetTempPath(),
                "ZapretControl-selftest-coverage-" +
                Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(
                    Path.Combine(coverageRoot, "lists"));
                TelegramCoverageResult applied = TelegramCoverage.Apply(
                    coverageRoot, true);
                if (!applied.Ok || !applied.Changed ||
                    !File.Exists(applied.Path) ||
                    !TelegramCoverage.IsApplied(coverageRoot))
                {
                    Console.Error.WriteLine(
                        "selftest failed: TelegramCoverage apply");
                    return 1;
                }
                string firstText = File.ReadAllText(applied.Path);
                TelegramCoverageResult repeated = TelegramCoverage.Apply(
                    coverageRoot, true);
                if (!repeated.Ok || repeated.Changed ||
                    File.ReadAllText(applied.Path) != firstText)
                {
                    Console.Error.WriteLine(
                        "selftest failed: TelegramCoverage is not idempotent");
                    return 1;
                }
                TelegramCoverageResult removedCoverage =
                    TelegramCoverage.Apply(coverageRoot, false);
                if (!removedCoverage.Ok || !removedCoverage.Changed ||
                    TelegramCoverage.IsApplied(coverageRoot) ||
                    File.ReadAllText(applied.Path).Length != 0)
                {
                    Console.Error.WriteLine(
                        "selftest failed: TelegramCoverage remove");
                    return 1;
                }
            }
            finally
            {
                try
                {
                    if (Directory.Exists(coverageRoot))
                    {
                        Directory.Delete(coverageRoot, true);
                    }
                }
                catch (Exception)
                {
                }
            }
            SettingsData telegramDefaults = new SettingsData();
            if (telegramDefaults.TgProxyPort != 1443 ||
                telegramDefaults.TgProxyAutostart ||
                !telegramDefaults.TgProxyZapret ||
                telegramDefaults.DarkTheme)
            {
                Console.Error.WriteLine(
                    "selftest failed: Telegram settings defaults");
                return 1;
            }
            if (!TgProxyService.StatusDirectory.EndsWith(
                    "proxy", StringComparison.OrdinalIgnoreCase) ||
                !TgProxyService.LegacyStatusPath.EndsWith(
                    "tg-proxy.json", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine(
                    "selftest failed: Telegram status paths");
                return 1;
            }

            Console.WriteLine("version=" + AppInfo.Version);
            Console.WriteLine("selftest=OK");
            return 0;
        }
    }
}
