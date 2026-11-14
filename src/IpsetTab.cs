using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ZapretControl
{
    public sealed class IpsetTab : TabView
    {
        private Label _state;
        private Label _detail;
        private Label _backup;
        private Label _updateStatus;
        private Button _update;
        private string _payload;
        private bool _busy;

        public override string TabTitle
        {
            get { return "IPSet"; }
        }

        public IpsetTab()
        {
            CardPanel stateCard = AddCard("IPSET FILTER", 0, 0, 852, 190);
            _state = AddLabel(stateCard, "Проверка…", 20, 42, 812, 26);
            _state.Font = new System.Drawing.Font("Segoe UI", 12F, System.Drawing.FontStyle.Bold);
            _detail = AddMutedLabel(stateCard, string.Empty, 20, 74, 812, 20);
            _backup = AddMutedLabel(stateCard, string.Empty, 20, 96, 812, 20);
            _backup.AutoEllipsis = true;
            AddMutedLabel(stateCard,
                "loaded — список используется; none — отключён (203.0.113.113/32); " +
                "any — пустой список. Переключение сохраняет резервную копию.",
                20, 122, 812, 36);
            AddButton(stateCard, "Загружен", 20, 160, 140, 26, OnLoaded);
            AddButton(stateCard, "Отключён", 170, 160, 140, 26, OnNone);
            AddButton(stateCard, "Пустой (any)", 320, 160, 140, 26, OnAny);

            CardPanel updateCard = AddCard("ОБНОВЛЕНИЕ СПИСКА", 0, 202, 852, 160);
            AddMutedLabel(updateCard,
                "Источник: Flowseal .service/ipset-service.txt. Скачивание проверяется по размеру " +
                "и формату строк, затем список применяется к текущему режиму.",
                20, 42, 812, 36);
            _update = AddButton(
                updateCard, "Обновить список IPSet", 20, 92, 220, 32, OnUpdate);
            _updateStatus = AddMutedLabel(updateCard, "—", 260, 100, 572, 20);
            _updateStatus.AutoEllipsis = true;
        }

        public override void ActivateTab()
        {
            RefreshData();
        }

        public override void RefreshData()
        {
            if (_busy || IsDisposed || Disposing)
            {
                return;
            }
            _payload = AppPaths.ResolvePayloadRoot();
            try
            {
                string state = IpsetManager.GetState(_payload);
                int lines = CountLines(IpsetManager.GetListPath(_payload));
                _state.Text = "Режим: " + IpsetManager.GetDisplay(state);
                _state.ForeColor = state == IpsetManager.StateLoaded ? GreenColor : MutedColor;
                _detail.Text = "Строк в ipset-all.txt: " + lines +
                    " · файл: " + IpsetManager.GetListPath(_payload);
                _detail.AutoEllipsis = true;
                string backup = IpsetManager.GetBackupPath(_payload);
                if (File.Exists(backup))
                {
                    _backup.Text = "Резервная копия: " + backup +
                        " (" + CountLines(backup) + " строк)";
                    _backup.ForeColor = MutedColor;
                }
                else
                {
                    _backup.Text = "Резервная копия отсутствует.";
                    _backup.ForeColor = AmberColor;
                }
            }
            catch (Exception ex)
            {
                _state.Text = "Ошибка: " + ex.Message;
                _state.ForeColor = RedColor;
            }
        }

        private static int CountLines(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return 0;
                }
                int count = 0;
                foreach (string line in File.ReadLines(path))
                {
                    if (line.Length > 0)
                    {
                        count++;
                    }
                }
                return count;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private async void OnLoaded(object sender, EventArgs e)
        {
            await SetState(IpsetManager.StateLoaded);
        }

        private async void OnNone(object sender, EventArgs e)
        {
            await SetState(IpsetManager.StateNone);
        }

        private async void OnAny(object sender, EventArgs e)
        {
            await SetState(IpsetManager.StateAny);
        }

        private async Task SetState(string state)
        {
            if (_busy)
            {
                return;
            }
            if (!Confirm("Переключить IPSet в режим \"" +
                IpsetManager.GetDisplay(state) + "\"?"))
            {
                return;
            }
            _busy = true;
            Exception error = null;
            try
            {
                await Task.Run(delegate
                {
                    IpsetManager.SetState(_payload, state);
                });
            }
            catch (Exception ex)
            {
                error = ex;
            }
            _busy = false;
            RefreshData();
            if (error != null)
            {
                ShowError("Не удалось переключить IPSet", error);
            }
            else
            {
                ShowInfo("Режим IPSet изменён. Перезапустите службу, чтобы winws.exe " +
                         "перечитал список.");
            }
        }

        private async void OnUpdate(object sender, EventArgs e)
        {
            if (_busy)
            {
                return;
            }
            _busy = true;
            _update.Enabled = false;
            _updateStatus.Text = "Скачивание списка…";
            Exception error = null;
            IpsetUpdateResult result = null;
            try
            {
                await Task.Run(delegate
                {
                    result = IpsetManager.Update(
                        _payload,
                        delegate(long received, long total)
                        {
                            if (total > 0)
                            {
                                int percent = (int)(received * 100 / total);
                                ReportUpdate("Скачивание: " + percent + "%");
                            }
                        });
                });
            }
            catch (Exception ex)
            {
                error = ex;
            }
            _busy = false;
            _update.Enabled = true;
            RefreshData();
            if (error != null)
            {
                _updateStatus.Text = "Ошибка: " + ShortError(error);
                _updateStatus.ForeColor = RedColor;
                ShowError("Не удалось обновить IPSet", error);
                return;
            }
            if (result != null)
            {
                _updateStatus.Text = "Обновлено: " + result.Lines + " строк, режим " +
                    IpsetManager.GetDisplay(result.State) +
                    (result.Warning == null ? "." : ". " + result.Warning);
                _updateStatus.ForeColor = GreenColor;
                ShowInfo("Список IPSet обновлён: " + result.Lines + " строк." +
                    (result.Warning == null ? string.Empty : "\n" + result.Warning));
            }
        }

        private void ReportUpdate(string text)
        {
            if (IsDisposed || Disposing)
            {
                return;
            }
            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(new Action<string>(ReportUpdate), text);
                }
                catch (Exception)
                {
                }
                return;
            }
            _updateStatus.Text = text;
            _updateStatus.ForeColor = AmberColor;
        }
    }
}
