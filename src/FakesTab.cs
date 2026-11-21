using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ZapretControl
{
    public sealed class FakesTab : TabView
    {
        private Label _discord;
        private Label _game;
        private ListBox _files;
        private Label _status;
        private string _payload;
        private bool _busy;

        public override string TabTitle
        {
            get { return "Фейки"; }
        }

        public FakesTab()
        {
            CardPanel active = AddCard("АКТИВНЫЕ ФЕЙКИ", 0, 0, 852, 160);
            AddMutedLabel(active, "DISCORD UDP", 20, 38, 160, 16);
            _discord = AddLabel(active, "—", 20, 58, 380, 22);
            AddMutedLabel(active, "GAMEFILTER UDP", 430, 38, 200, 16);
            _game = AddLabel(active, "—", 430, 58, 400, 22);
            AddMutedLabel(active,
                "Активный файл определяется по SHA256. Замена копирует выбранный .bin в " +
                "bin\\ACTIVE_DISCORD_UDP.bin или bin\\ACTIVE_GAME_UDP.bin и делает бэкап.",
                20, 92, 812, 36);

            CardPanel files = AddCard("ФАЙЛЫ ФЕЙКОВ В BIN", 0, 172, 852, 280);
            _files = new ListBox();
            _files.SetBounds(20, 38, 500, 210);
            _files.IntegralHeight = false;
            files.Controls.Add(_files);
            AddButton(files, "Заменить Discord", 540, 38, 180, 32,
                delegate { OnReplace(FakesManager.DiscordKind); });
            AddButton(files, "Заменить GameFilter", 540, 80, 180, 32,
                delegate { OnReplace(FakesManager.GameKind); });
            AddButton(files, "Обновить список", 540, 122, 180, 32,
                delegate { RefreshData(); });
            AddButton(files, "Как это работает", 540, 208, 180, 32, OnHelp);
            _status = AddMutedLabel(files, "—", 20, 252, 812, 20);
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
                List<FakeFile> fakes = FakesManager.List(_payload);
                string selected = _files.SelectedItem == null
                    ? null
                    : _files.SelectedItem.ToString();
                _files.Items.Clear();
                foreach (FakeFile fake in fakes)
                {
                    _files.Items.Add(fake);
                    if (selected != null && fake.Name == selected)
                    {
                        _files.SelectedItem = fake;
                    }
                }
                if (_files.SelectedIndex < 0 && _files.Items.Count > 0)
                {
                    _files.SelectedIndex = 0;
                }
                _discord.Text = FakesManager.DescribeActive(_payload, FakesManager.DiscordKind);
                _game.Text = FakesManager.DescribeActive(_payload, FakesManager.GameKind);
                _status.Text = "Найдено файлов: " + fakes.Count +
                    ". Активные файлы читаются winws.exe при запуске.";
                _status.ForeColor = MutedColor;
            }
            catch (Exception ex)
            {
                _status.Text = "Ошибка: " + ex.Message;
                _status.ForeColor = RedColor;
            }
        }

        private void OnHelp(object sender, EventArgs e)
        {
            ShowHelp("Фейки — как это работает", HelpTexts.Fakes);
        }

        private async void OnReplace(string kind)
        {
            if (_busy)
            {
                return;
            }
            FakeFile selected = _files.SelectedItem as FakeFile;
            if (selected == null)
            {
                ShowInfo("Выберите файл фейка.");
                return;
            }
            string target = kind == FakesManager.DiscordKind ? "Discord UDP" : "GameFilter UDP";
            if (!Confirm("Заменить " + target + " на " + selected.Name + "?"))
            {
                return;
            }
            _busy = true;
            string backup = null;
            Exception error = null;
            try
            {
                await Task.Run(delegate
                {
                    backup = FakesManager.Replace(_payload, kind, selected.Name);
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
                ShowError("Не удалось заменить фейк", error);
            }
            else
            {
                ShowInfo("Фейк заменён." +
                    (string.IsNullOrEmpty(backup)
                        ? string.Empty
                        : "\nБэкап: " + backup) +
                    "\nПерезапустите службу, чтобы применить изменения.");
            }
        }
    }
}
