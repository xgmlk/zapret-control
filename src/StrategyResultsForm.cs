using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace ZapretControl
{
    // Ranked list of advisor results. The user chooses a strategy here; the
    // caller applies it only after an explicit confirmation.
    public sealed class StrategyResultsForm : Form
    {
        private readonly StrategyAdviceReport _report;
        private readonly bool _readOnly;
        private ListView _list;
        private TextBox _detail;
        private Button _apply;

        public StrategyProbeResult SelectedResult { get; private set; }

        public StrategyResultsForm(StrategyAdviceReport report)
            : this(report, false)
        {
        }

        // readOnly is used by the "last check" button: the list and the
        // timestamp stay visible, but applying still requires a fresh run.
        public StrategyResultsForm(StrategyAdviceReport report, bool readOnly)
        {
            _report = report ?? new StrategyAdviceReport();
            _readOnly = readOnly;
            Text = readOnly
                ? "Zapret Control — последняя проверка"
                : "Zapret Control — результаты подбора";
            ClientSize = new Size(900, 600);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            BackColor = Theme.Current.WindowBg;
            ForeColor = Theme.Current.TextPrimary;
            Font = new Font("Segoe UI", 9F);
            AutoScaleMode = AutoScaleMode.Dpi;

            using (System.IO.Stream iconStream = typeof(StrategyResultsForm).Assembly
                .GetManifestResourceStream("ZapretControl.app.ico"))
            {
                if (iconStream != null)
                {
                    Icon = new Icon(iconStream, new Size(64, 64));
                }
            }

            Label header = new Label();
            header.SetBounds(20, 8, 860, 72);
            header.Font = new Font("Segoe UI", 8.5F);
            header.Text = BuildHeader();
            header.ForeColor = _report.HasRecommendation
                ? Theme.Current.Success
                : Theme.Current.Warning;
            Controls.Add(header);

            Label checkedAt = new Label();
            checkedAt.SetBounds(20, 82, 860, 16);
            string elapsed = _report.ElapsedMs > 0
                ? " · " + (_report.ElapsedMs / 1000.0).ToString(
                    "0.0", CultureInfo.InvariantCulture) + " с"
                : string.Empty;
            checkedAt.Text = (readOnly ? "Последняя проверка: " : "Проверка выполнена: ") +
                (string.IsNullOrEmpty(_report.CheckedAtDisplay)
                    ? "время неизвестно"
                    : _report.CheckedAtDisplay) + elapsed;
            checkedAt.ForeColor = Theme.Current.TextSecondary;
            checkedAt.BackColor = Color.Transparent;
            checkedAt.Tag = "muted";
            checkedAt.Font = new Font("Segoe UI", 8.25F);
            Controls.Add(checkedAt);

            _list = new ListView();
            _list.SetBounds(20, 102, 860, 320);
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.MultiSelect = false;
            _list.HideSelection = false;
            _list.GridLines = false;
            _list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            _list.Columns.Add("Стратегия", 178);
            _list.Columns.Add("Discord", 50);
            _list.Columns.Add("YouTube", 66);
            _list.Columns.Add("Стр.", 40);
            _list.Columns.Add("Плеер", 42);
            _list.Columns.Add("Видео", 46);
            _list.Columns.Add("Google", 46);
            _list.Columns.Add("DPI", 52);
            _list.Columns.Add("TLS1.2", 42);
            _list.Columns.Add("TLS1.3", 42);
            _list.Columns.Add("Время", 54);
            _list.Columns.Add("Итог", 140);
            _list.SelectedIndexChanged += OnSelectionChanged;
            _list.DoubleClick += OnDoubleClick;
            Theme.StyleList(_list);
            Controls.Add(_list);

            Label detailCaption = new Label();
            detailCaption.Text = "Подробности:";
            detailCaption.ForeColor = Theme.Current.TextSecondary;
            detailCaption.BackColor = Color.Transparent;
            detailCaption.Tag = "muted";
            detailCaption.SetBounds(20, 428, 200, 18);
            Controls.Add(detailCaption);

            _detail = new TextBox();
            _detail.SetBounds(20, 446, 860, 70);
            _detail.Multiline = true;
            _detail.ReadOnly = true;
            _detail.ScrollBars = ScrollBars.Vertical;
            _detail.BackColor = Theme.Current.InputReadOnlyBg;
            _detail.ForeColor = Theme.Current.TextPrimary;
            _detail.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(_detail);

            _apply = new ThemedButton();
            _apply.Text = "Применить";
            _apply.SetBounds(668, 530, 100, 30);
            _apply.Click += OnApply;
            _apply.Visible = !readOnly;
            Theme.StyleButton(_apply);
            Controls.Add(_apply);

            Button close = new ThemedButton();
            close.Text = "Закрыть";
            close.SetBounds(780, 530, 100, 30);
            close.Click += delegate
            {
                SelectedResult = null;
                DialogResult = DialogResult.Cancel;
                Close();
            };
            Theme.StyleButton(close);
            Controls.Add(close);

            AcceptButton = readOnly ? close : _apply;
            CancelButton = close;
            Theme.Apply(this);
            Populate();
        }

        private string BuildHeader()
        {
            int full = 0;
            int partial = 0;
            foreach (StrategyProbeResult result in _report.Results)
            {
                if (result.Ok)
                {
                    full++;
                }
                else if (result.PartiallyOk)
                {
                    partial++;
                }
            }
            string text;
            if (full > 0)
            {
                text = "Полностью работают: " + full + " из " +
                       _report.Results.Count + ".";
                if (_report.HasRecommendation)
                {
                    text += " Лучшая: " + _report.RecommendedName + ".";
                }
                text += _readOnly
                    ? " Сохранённый результат: чтобы применить, подберите заново."
                    : " Выберите вариант и нажмите «Применить».";
            }
            else
            {
                text = "Полностью работающих стратегий нет: 0 из " +
                       _report.Results.Count + ".";
                if (partial > 0)
                {
                    text += " Показаны частичные результаты (" + partial + "). " +
                            "Проверьте, на каком этапе сломалось.";
                }
                else
                {
                    text += " Ни одна стратегия не прошла ни одной проверки.";
                }
                if (_readOnly)
                {
                    text += " Это сохранённый результат последней проверки.";
                }
            }
            if (!string.IsNullOrEmpty(_report.IpsetMode))
            {
                text += "\nIPSet при проверке: " + _report.IpsetMode +
                        (string.IsNullOrEmpty(_report.IpsetOriginal)
                            ? string.Empty
                            : " (исходное: " + _report.IpsetOriginal + ")") +
                        (_report.IpsetRestored ? " · восстановлен" : " · НЕ восстановлен");
            }
            // Old reports do not carry IPSet/curl fields; do not show a false
            // "curl missing" warning for them.
            if (!_report.CurlAvailable && !string.IsNullOrEmpty(_report.IpsetMode))
            {
                text += "\ncurl.exe не найден: DPI-заморозка не проверена, " +
                        "полная рекомендация невозможна.";
            }
            else if (_report.FreezeSuiteOffline)
            {
                text += "\nСписок suite.v2.json недоступен: использован встроенный.";
            }
            if ((full > 0 || partial > 0) && !string.IsNullOrEmpty(_report.Error))
            {
                text += "\n" + _report.Error;
            }
            if (_report.ServiceWasRunning && !_report.ServiceRestored)
            {
                text += "\nСлужба не вернулась после проверки — включите защиту вручную.";
            }
            return text;
        }

        private void Populate()
        {
            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                StrategyProbeResult first = null;
                foreach (StrategyProbeResult result in _report.Results)
                {
                    ListViewItem item = new ListViewItem(result.Name);
                    item.SubItems.Add(Counts(result.DiscordPassed, result.DiscordTotal));
                    item.SubItems.Add(Counts(
                        result.YouTubeHostsPassed, result.YouTubeHostsTotal));
                    item.SubItems.Add(result.YouTubePageOk ? "OK" : "нет");
                    item.SubItems.Add(result.YouTubePlayerOk ? "OK" : "нет");
                    item.SubItems.Add(result.VideoDisplay);
                    item.SubItems.Add(Counts(result.GooglePassed, result.GoogleTotal));
                    item.SubItems.Add(result.FreezeDisplay);
                    item.SubItems.Add(result.Tls12Display);
                    item.SubItems.Add(result.Tls13Display);
                    item.SubItems.Add((result.ElapsedMs / 1000.0).ToString(
                        "0.0", CultureInfo.InvariantCulture) + " с");
                    item.SubItems.Add(result.SummaryText);
                    item.Tag = result;
                    item.ForeColor = result.Ok
                        ? Theme.Current.Success
                        : (result.PartiallyOk
                            ? Theme.Current.Warning
                            : Theme.Current.Danger);
                    if (result.Ok && _report.HasRecommendation &&
                        result.Name.Equals(
                            _report.RecommendedName, StringComparison.OrdinalIgnoreCase))
                    {
                        item.BackColor = Theme.Current.SelectionBg;
                        item.UseItemStyleForSubItems = true;
                    }
                    _list.Items.Add(item);
                    if (first == null && result.PartiallyOk)
                    {
                        first = result;
                    }
                }
                int index = 0;
                if (_report.HasRecommendation)
                {
                    for (int i = 0; i < _list.Items.Count; i++)
                    {
                        StrategyProbeResult result =
                            _list.Items[i].Tag as StrategyProbeResult;
                        if (result != null &&
                            result.Name.Equals(
                                _report.RecommendedName,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            index = i;
                            break;
                        }
                    }
                }
                else if (first != null)
                {
                    for (int i = 0; i < _list.Items.Count; i++)
                    {
                        if (_list.Items[i].Tag == first)
                        {
                            index = i;
                            break;
                        }
                    }
                }
                if (_list.Items.Count > 0)
                {
                    _list.Items[index].Selected = true;
                    _list.Items[index].Focused = true;
                    _list.EnsureVisible(index);
                }
            }
            finally
            {
                _list.EndUpdate();
            }
            UpdateDetail();
        }

        private void OnSelectionChanged(object sender, EventArgs e)
        {
            UpdateDetail();
        }

        private void UpdateDetail()
        {
            StrategyProbeResult result = CurrentResult();
            _apply.Enabled = !_readOnly && result != null;
            if (result == null)
            {
                _detail.Text = "Выберите стратегию.";
                return;
            }
            string text = result.Name + "\r\n" + result.StagesText +
                          "\r\n" + result.Detail;
            if ((!result.DiscordOk || !result.YouTubeHostsOk || !result.GoogleOk) &&
                !string.IsNullOrEmpty(result.HostsDetail))
            {
                text += "\r\nХосты: " + result.HostsDetail;
            }
            if (!string.IsNullOrEmpty(result.VideoDetail))
            {
                text += "\r\nВидео: " + result.VideoDetail;
            }
            if (!string.IsNullOrEmpty(result.FreezeDetail))
            {
                text += "\r\nDPI: " + result.FreezeDetail;
            }
            _detail.Text = text;
        }

        private StrategyProbeResult CurrentResult()
        {
            if (_list.SelectedItems.Count == 0)
            {
                return null;
            }
            return _list.SelectedItems[0].Tag as StrategyProbeResult;
        }

        private void OnDoubleClick(object sender, EventArgs e)
        {
            OnApply(sender, e);
        }

        private void OnApply(object sender, EventArgs e)
        {
            if (_readOnly)
            {
                return;
            }
            StrategyProbeResult result = CurrentResult();
            if (result == null)
            {
                return;
            }
            SelectedResult = result;
            DialogResult = DialogResult.OK;
            Close();
        }

        private static string Counts(int passed, int total)
        {
            return total == 0 ? "—" : passed + "/" + total;
        }
    }
}
