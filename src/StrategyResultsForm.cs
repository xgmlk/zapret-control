using System;
using System.Drawing;
using System.Windows.Forms;

namespace ZapretControl
{
    // Ranked list of advisor results. The user chooses a strategy here; the
    // caller applies it only after an explicit confirmation.
    public sealed class StrategyResultsForm : Form
    {
        private readonly StrategyAdviceReport _report;
        private ListView _list;
        private TextBox _detail;
        private Button _apply;

        public StrategyProbeResult SelectedResult { get; private set; }

        public StrategyResultsForm(StrategyAdviceReport report)
        {
            _report = report ?? new StrategyAdviceReport();
            Text = "Zapret Control — результаты подбора";
            ClientSize = new Size(860, 532);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            BackColor = Color.FromArgb(247, 246, 243);
            ForeColor = Color.FromArgb(44, 44, 46);
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
            header.SetBounds(20, 16, 820, 54);
            header.Text = BuildHeader();
            header.ForeColor = _report.HasRecommendation
                ? Color.FromArgb(31, 122, 51)
                : Color.FromArgb(150, 112, 28);
            Controls.Add(header);

            _list = new ListView();
            _list.SetBounds(20, 76, 820, 306);
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.MultiSelect = false;
            _list.HideSelection = false;
            _list.GridLines = false;
            _list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            _list.Columns.Add("Стратегия", 236);
            _list.Columns.Add("Discord", 62);
            _list.Columns.Add("YouTube", 70);
            _list.Columns.Add("Видео", 68);
            _list.Columns.Add("Google", 62);
            _list.Columns.Add("Время", 72);
            _list.Columns.Add("Итог", 180);
            _list.SelectedIndexChanged += OnSelectionChanged;
            _list.DoubleClick += OnDoubleClick;
            Controls.Add(_list);

            Label detailCaption = new Label();
            detailCaption.Text = "Подробности:";
            detailCaption.ForeColor = Color.FromArgb(110, 110, 115);
            detailCaption.SetBounds(20, 390, 200, 18);
            Controls.Add(detailCaption);

            _detail = new TextBox();
            _detail.SetBounds(20, 410, 820, 54);
            _detail.Multiline = true;
            _detail.ReadOnly = true;
            _detail.ScrollBars = ScrollBars.Vertical;
            _detail.BackColor = Color.FromArgb(252, 251, 248);
            _detail.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(_detail);

            _apply = new Button();
            _apply.Text = "Применить";
            _apply.SetBounds(624, 480, 100, 30);
            _apply.Click += OnApply;
            Controls.Add(_apply);

            Button close = new Button();
            close.Text = "Закрыть";
            close.SetBounds(736, 480, 100, 30);
            close.Click += delegate
            {
                SelectedResult = null;
                DialogResult = DialogResult.Cancel;
                Close();
            };
            Controls.Add(close);

            AcceptButton = _apply;
            CancelButton = close;
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
                text += " Выберите вариант и нажмите «Применить».";
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
                    item.SubItems.Add(result.DiscordOk ? "OK" : "нет");
                    item.SubItems.Add(result.YouTubePageOk ? "OK" : "нет");
                    item.SubItems.Add(result.VideoDisplay);
                    item.SubItems.Add(result.GoogleOk ? "OK" : "нет");
                    item.SubItems.Add(result.ElapsedMs + " мс");
                    item.SubItems.Add(result.SummaryText);
                    item.Tag = result;
                    item.ForeColor = result.Ok
                        ? Color.FromArgb(31, 122, 51)
                        : (result.PartiallyOk
                            ? Color.FromArgb(150, 112, 28)
                            : Color.FromArgb(168, 58, 51));
                    if (result.Ok && _report.HasRecommendation &&
                        result.Name.Equals(
                            _report.RecommendedName, StringComparison.OrdinalIgnoreCase))
                    {
                        item.BackColor = Color.FromArgb(232, 245, 233);
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
            _apply.Enabled = result != null;
            if (result == null)
            {
                _detail.Text = "Выберите стратегию.";
                return;
            }
            _detail.Text = result.Name + "\r\n" + result.StagesText +
                           "\r\n" + result.Detail;
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
            StrategyProbeResult result = CurrentResult();
            if (result == null)
            {
                return;
            }
            SelectedResult = result;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
