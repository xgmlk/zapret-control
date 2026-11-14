using System;
using System.IO;
using System.Text;

namespace ZapretControl
{
    public sealed class HomeCheckReport
    {
        public ConnectionTestReport Tests { get; private set; }
        public bool HostsChecked { get; private set; }
        public bool HostsOk { get; private set; }
        public bool HostsUpdated { get; private set; }
        public string HostsDetail { get; private set; }
        public DateTime StartedAt { get; private set; }

        public HomeCheckReport(
            ConnectionTestReport tests,
            bool hostsChecked,
            bool hostsOk,
            bool hostsUpdated,
            string hostsDetail,
            DateTime startedAt)
        {
            Tests = tests;
            HostsChecked = hostsChecked;
            HostsOk = hostsOk;
            HostsUpdated = hostsUpdated;
            HostsDetail = hostsDetail ?? string.Empty;
            StartedAt = startedAt;
        }

        public int Total
        {
            get { return Tests.Results.Count + (HostsChecked ? 1 : 0); }
        }

        public int Passed
        {
            get { return Tests.Passed + (HostsChecked && HostsOk ? 1 : 0); }
        }

        public bool AllOk
        {
            get
            {
                return Tests.Passed == Tests.Results.Count &&
                       (!HostsChecked || HostsOk);
            }
        }

        public string Summary
        {
            get
            {
                if (AllOk)
                {
                    return Total + " из " + Total + " · всё в порядке";
                }
                return Passed + " из " + Total + " · " +
                    (Passed == 0 ? "ничего не работает" : "есть проблемы");
            }
        }

        public string ToText()
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Zapret Control — быстрая проверка (DPI + hosts), " +
                               StartedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            foreach (ConnectionTestResult result in Tests.Results)
            {
                builder.Append(result.Success ? "[OK] " : "[FAIL] ")
                    .Append(result.Name).Append(" — ").Append(result.Detail)
                    .Append(" (").Append(result.ElapsedMs).AppendLine(" ms)");
            }
            if (HostsChecked)
            {
                builder.Append(HostsOk ? "[OK] " : "[FAIL] ");
            }
            else
            {
                builder.Append("[--] ");
            }
            builder.Append("hosts — ").AppendLine(HostsDetail);
            builder.AppendLine("Итог: " + Summary);
            return builder.ToString();
        }
    }

    // Compact check for the home tab: DPI connectivity plus the managed hosts
    // block. Detailed output is passed to the tests tab.
    public static class HomeCheck
    {
        public static HomeCheckReport Run()
        {
            DateTime startedAt = DateTime.Now;
            ConnectionTestReport tests = ConnectionTests.RunHome();
            bool hostsChecked = false;
            bool hostsOk = false;
            bool hostsUpdated = false;
            string detail;
            try
            {
                HostsCheckResult check = HostsManager.Check();
                if (check.Error != null)
                {
                    detail = "не проверено: " + check.Error;
                }
                else
                {
                    hostsChecked = true;
                    if (!check.NeedsUpdate)
                    {
                        hostsOk = true;
                        detail = check.ManagedBlockFound
                            ? "управляемый блок актуален"
                            : "записи уже есть в hosts";
                    }
                    else
                    {
                        HostsApplyResult apply = HostsManager.Apply(check.UpstreamText);
                        hostsOk = true;
                        hostsUpdated = true;
                        detail = "обновлён управляемый блок, строк " + apply.Lines;
                        if (!string.IsNullOrEmpty(apply.BackupPath))
                        {
                            detail += ", бэкап " + Path.GetFileName(apply.BackupPath);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                detail = "ошибка: " + ex.Message;
            }
            return new HomeCheckReport(
                tests, hostsChecked, hostsOk, hostsUpdated, detail, startedAt);
        }
    }
}
