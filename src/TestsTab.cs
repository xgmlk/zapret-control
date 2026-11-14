using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ZapretControl
{
    public sealed class TestsTab : TabView
    {
        private TextBox _output;
        private Label _status;
        private Button _standard;
        private Button _dpi;
        private bool _busy;

        public override string TabTitle
        {
            get { return "Тесты"; }
        }

        public TestsTab()
        {
            CardPanel card = AddCard("ПРОВЕРКА СОЕДИНЕНИЙ", 0, 0, 852, 470);
            AddMutedLabel(card,
                "Стандартные тесты проверяют DNS и TCP 443. DPI-тест делает HTTPS-запросы " +
                "к Discord, YouTube, Google, Instagram и X через текущий обход.",
                20, 34, 812, 36);
            _standard = AddButton(card, "Стандартные тесты", 20, 76, 190, 32, OnStandard);
            _dpi = AddButton(card, "DPI-тест", 220, 76, 140, 32, OnDpi);
            _status = AddMutedLabel(card, "Тесты не запускались.", 380, 84, 452, 20);
            _output = AddTextBox(card, 20, 120, 812, 330, true);
            _output.Font = new System.Drawing.Font("Consolas", 8.5F);
        }

        private void OnStandard(object sender, EventArgs e)
        {
            RunTests("standard");
        }

        private void OnDpi(object sender, EventArgs e)
        {
            RunTests("dpi");
        }

        private async void RunTests(string mode)
        {
            if (_busy)
            {
                return;
            }
            _busy = true;
            _standard.Enabled = false;
            _dpi.Enabled = false;
            SetState(_status,
                mode == "dpi" ? "Выполняю DPI-тест…" : "Выполняю стандартные тесты…",
                AmberColor);
            _output.Clear();
            ConnectionTestReport report = null;
            Exception error = null;
            try
            {
                await Task.Run(delegate
                {
                    report = ConnectionTests.Run(mode);
                });
            }
            catch (Exception ex)
            {
                error = ex;
            }
            _busy = false;
            _standard.Enabled = true;
            _dpi.Enabled = true;
            if (error != null)
            {
                SetState(_status, "Ошибка: " + ShortError(error), RedColor);
                ShowError("Не удалось выполнить тесты", error);
                return;
            }
            _output.Text = report.ToText();
            bool all = report.Passed == report.Results.Count;
            SetState(_status,
                "Готово: " + report.Passed + " из " + report.Results.Count + " успешно.",
                all ? GreenColor : (report.Passed > 0 ? AmberColor : RedColor));
        }
    }
}
