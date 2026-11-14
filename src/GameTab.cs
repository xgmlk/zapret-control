using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ZapretControl
{
    public sealed class GameTab : TabView
    {
        private RadioButton _disabled;
        private RadioButton _all;
        private RadioButton _tcp;
        private RadioButton _udp;
        private TextBox _tcpRange;
        private TextBox _udpRange;
        private Label _status;
        private Label _strategyInfo;
        private string _payload;
        private bool _loading;
        private bool _busy;

        public override string TabTitle
        {
            get { return "Игры"; }
        }

        public GameTab()
        {
            CardPanel settings = AddCard("GAME FILTER", 0, 0, 852, 300);
            AddMutedLabel(settings, "РЕЖИМ", 20, 36, 160, 16);
            _disabled = CreateRadio(settings, "Выключен", 20, 58);
            _all = CreateRadio(settings, "TCP и UDP", 180, 58);
            _tcp = CreateRadio(settings, "Только TCP", 340, 58);
            _udp = CreateRadio(settings, "Только UDP", 500, 58);

            AddMutedLabel(settings, "TCP-ПОРТЫ", 20, 104, 160, 16);
            _tcpRange = AddTextBox(settings, 20, 124, 300, 24, false);
            AddMutedLabel(settings, "UDP-ПОРТЫ", 360, 104, 160, 16);
            _udpRange = AddTextBox(settings, 360, 124, 300, 24, false);
            AddMutedLabel(settings,
                "Формат: 1024-65535 или 1024-1934,1936-65535. Диапазон проверяется до записи.",
                20, 156, 700, 20);

            _status = AddLabel(settings, "—", 20, 190, 812, 22);
            AddButton(settings, "Сохранить", 20, 228, 160, 32, OnSave);
            AddButton(settings, "Сохранить и применить", 190, 228, 220, 32, OnSaveApply);
            AddMutedLabel(settings,
                "Game Filter встраивается в ImagePath службы при пересоздании.",
                420, 236, 412, 20);

            CardPanel info = AddCard("КАК ЭТО РАБОТАЕТ", 0, 312, 852, 208);
            _strategyInfo = AddLabel(info, "—", 20, 42, 812, 20);
            _strategyInfo.AutoEllipsis = true;
            AddMutedLabel(info,
                "Режим и диапазоны хранятся в utils\\game_filter.enabled — так же, как в service.bat. " +
                "При следующем пересоздании службы подставляются %GameFilterTCP% и %GameFilterUDP%.",
                20, 70, 812, 36);
            AddMutedLabel(info,
                "Если стратегия не использует эти переменные, Game Filter не попадёт в winws.exe. " +
                "Приложение предупредит об этом ниже.",
                20, 112, 812, 36);
            AddMutedLabel(info,
                "Изменение диапазонов не требует переустановки zapret, но требует перезапуска службы.",
                20, 154, 812, 20);

            _loading = false;
            _disabled.CheckedChanged += delegate { UpdateStatus(); };
            _all.CheckedChanged += delegate { UpdateStatus(); };
            _tcp.CheckedChanged += delegate { UpdateStatus(); };
            _udp.CheckedChanged += delegate { UpdateStatus(); };
        }

        private RadioButton CreateRadio(Control parent, string text, int x, int y)
        {
            RadioButton radio = new RadioButton();
            radio.Text = text;
            radio.SetBounds(x, y, 150, 22);
            parent.Controls.Add(radio);
            return radio;
        }

        public override void ActivateTab()
        {
            LoadSettings();
        }

        private void LoadSettings()
        {
            _loading = true;
            _payload = AppPaths.ResolvePayloadRoot();
            GameFilterSettings game = GameFilterSettings.Load(_payload);
            _disabled.Checked = game.Mode == GameFilterSettings.ModeDisabled;
            _all.Checked = game.Mode == GameFilterSettings.ModeAll;
            _tcp.Checked = game.Mode == GameFilterSettings.ModeTcp;
            _udp.Checked = game.Mode == GameFilterSettings.ModeUdp;
            _tcpRange.Text = game.TcpRange;
            _udpRange.Text = game.UdpRange;
            _loading = false;
            UpdateStatus();
            CheckStrategy();
        }

        private GameFilterSettings ReadControls()
        {
            GameFilterSettings game = new GameFilterSettings();
            if (_all.Checked)
            {
                game.Mode = GameFilterSettings.ModeAll;
            }
            else if (_tcp.Checked)
            {
                game.Mode = GameFilterSettings.ModeTcp;
            }
            else if (_udp.Checked)
            {
                game.Mode = GameFilterSettings.ModeUdp;
            }
            else
            {
                game.Mode = GameFilterSettings.ModeDisabled;
            }
            string tcp = GameFilterSettings.NormalizeRange(_tcpRange.Text);
            string udp = GameFilterSettings.NormalizeRange(_udpRange.Text);
            if (tcp == null)
            {
                throw new InvalidOperationException("Некорректный TCP-диапазон.");
            }
            if (udp == null)
            {
                throw new InvalidOperationException("Некорректный UDP-диапазон.");
            }
            game.TcpRange = tcp;
            game.UdpRange = udp;
            return game;
        }

        private void UpdateStatus()
        {
            if (_loading)
            {
                return;
            }
            GameFilterSettings game = new GameFilterSettings();
            if (_all.Checked)
            {
                game.Mode = GameFilterSettings.ModeAll;
            }
            else if (_tcp.Checked)
            {
                game.Mode = GameFilterSettings.ModeTcp;
            }
            else if (_udp.Checked)
            {
                game.Mode = GameFilterSettings.ModeUdp;
            }
            else
            {
                game.Mode = GameFilterSettings.ModeDisabled;
            }
            _status.Text = "Сейчас: " + game.Display +
                " · TCP " + (game.Mode == GameFilterSettings.ModeTcp ||
                              game.Mode == GameFilterSettings.ModeAll
                    ? _tcpRange.Text
                    : GameFilterSettings.DisabledPort) +
                " · UDP " + (game.Mode == GameFilterSettings.ModeUdp ||
                              game.Mode == GameFilterSettings.ModeAll
                    ? _udpRange.Text
                    : GameFilterSettings.DisabledPort);
            _status.ForeColor = game.Mode == GameFilterSettings.ModeDisabled
                ? MutedColor
                : GreenColor;
        }

        private void CheckStrategy()
        {
            string root = string.IsNullOrEmpty(_payload) ? AppPaths.ResolvePayloadRoot() : _payload;
            string strategy = ServiceManager.GetCurrentStrategyName();
            string path = ServiceManager.FindStrategyPath(root, strategy);
            if (string.IsNullOrEmpty(path))
            {
                _strategyInfo.Text = "Текущая стратегия не найдена. Выберите её на вкладке «Служба».";
                _strategyInfo.ForeColor = AmberColor;
                return;
            }
            try
            {
                string text = StrategyParser.ReadText(path);
                bool uses = text.IndexOf("%GameFilterTCP%", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            text.IndexOf("%GameFilterUDP%", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            text.IndexOf("%GameFilter%", StringComparison.OrdinalIgnoreCase) >= 0;
                _strategyInfo.Text = "Стратегия " + strategy +
                    (uses
                        ? " использует переменные Game Filter."
                        : " НЕ использует переменные Game Filter — параметры не попадут в winws.");
                _strategyInfo.ForeColor = uses ? GreenColor : AmberColor;
            }
            catch (Exception ex)
            {
                _strategyInfo.Text = "Не удалось прочитать стратегию: " + ex.Message;
                _strategyInfo.ForeColor = RedColor;
            }
        }

        private void OnSave(object sender, EventArgs e)
        {
            if (_busy)
            {
                return;
            }
            try
            {
                GameFilterSettings game = ReadControls();
                if (!game.Save(_payload))
                {
                    throw new InvalidOperationException(
                        "Не удалось записать utils\\game_filter.enabled.");
                }
                UpdateStatus();
                ShowInfo("Настройки Game Filter сохранены. Перезапустите службу, " +
                         "чтобы применить их.");
            }
            catch (Exception ex)
            {
                ShowError("Не удалось сохранить Game Filter", ex);
            }
        }

        private async void OnSaveApply(object sender, EventArgs e)
        {
            if (_busy)
            {
                return;
            }
            _busy = true;
            try
            {
                GameFilterSettings game = ReadControls();
                if (!game.Save(_payload))
                {
                    throw new InvalidOperationException(
                        "Не удалось записать utils\\game_filter.enabled.");
                }
                UpdateStatus();
                string strategy = ServiceManager.GetCurrentStrategyName();
                string path = ServiceManager.FindStrategyPath(_payload, strategy);
                if (string.IsNullOrEmpty(path))
                {
                    ShowInfo("Настройки сохранены, но служба не установлена или стратегия " +
                             "не найдена. Примените стратегию на вкладке «Служба».");
                    return;
                }
                bool wasRunning = ZapretService.GetStatus().State == ZapretState.Running;
                int iface = AppPaths.GetServiceIfaceIndex();
                IfaceStatus ifaceStatus = IfaceStatusProvider.GetStatus();
                if (ifaceStatus.ActiveFound)
                {
                    iface = ifaceStatus.ActiveIndex;
                }
                ServiceInstallResult result = null;
                await Task.Run(delegate
                {
                    result = ServiceManager.Install(_payload, path, wasRunning, iface);
                });
                if (result != null && !string.IsNullOrEmpty(result.Error))
                {
                    string text = result.RolledBack
                        ? "Game Filter применён, но служба не запустилась. " +
                          "Восстановлена предыдущая стратегия: " +
                          (string.IsNullOrEmpty(result.PreviousStrategyName)
                              ? "без имени"
                              : result.PreviousStrategyName) + "."
                        : "Game Filter применён, но служба не запустилась.";
                    MessageBox.Show(
                        this,
                        text + "\n\n" + result.Error,
                        "Zapret Control",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }
                ShowInfo("Game Filter применён, служба пересоздана.");
            }
            catch (Exception ex)
            {
                ShowError("Не удалось применить Game Filter", ex);
            }
            finally
            {
                _busy = false;
            }
        }
    }
}
