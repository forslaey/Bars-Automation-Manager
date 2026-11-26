using System;
using System.Data.SqlClient;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ServiceTool
{
    internal sealed class BarsSqlDialog : BrandWindow
    {
        private readonly BrandTextBox user, password;
        private readonly SqlChoiceField server, database;
        private readonly BrandButton discover, databases, check, save;
        private readonly Label status;
        private CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly BarsSqlConfiguration configuration;
        private bool busy;

        internal BarsSqlDialog(ThemePalette colors, Icon icon, BarsSqlConfiguration configuration)
        {
            this.configuration = configuration;
            Text = "Подключение к базе данных";
            Icon = icon;
            NativeWindowTheme.Bind(this, () => colors.Dark);
            BackColor = colors.Canvas; ForeColor = colors.Text;
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(720, 384); FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent; MinimizeBox = MaximizeBox = false;
            Font = new Font("Segoe UI", 10);

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22), ColumnCount = 3, RowCount = 6 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 166));
            for (int i = 0; i < 4; i++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            user = Input(colors); password = Input(colors); password.UseSystemPasswordChar = true;
            server = new SqlChoiceField(colors); database = new SqlChoiceField(colors);
            discover = Button(colors, "Найти серверы");
            databases = Button(colors, "Показать базы"); check = Button(colors, "Проверить вход");
            server.EmptyListRequested += delegate { discover.PerformClick(); };
            database.EmptyListRequested += delegate { databases.PerformClick(); };
            save = Button(colors, "Сохранить"); save.Primary = true;
            var close = Button(colors, "Закрыть"); close.DialogResult = DialogResult.Cancel; CancelButton = close;
            AddRow(layout, 0, "SQL Server", server, discover);
            AddRow(layout, 1, "Имя пользователя", new PathFieldFrame(user), null);
            AddRow(layout, 2, "Пароль", new PathFieldFrame(password), null);
            AddRow(layout, 3, "База данных", database, databases);
            status = new Label { Text = "Пустой логин означает вход под текущей учётной записью Windows.", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            layout.Controls.Add(status, 0, 4); layout.SetColumnSpan(status, 3);
            var footer = new TableLayoutPanel { Dock = DockStyle.Fill };
            ModuleNavigation.StandardFooter(footer, check, save, close);
            layout.Controls.Add(footer, 0, 5); layout.SetColumnSpan(footer, 3);
            Controls.Add(layout);
            var existing = configuration.Entries[0].Read();
            server.Text = existing.DataSource; database.Text = existing.InitialCatalog;
            user.Text = existing.IntegratedSecurity ? "" : existing.UserID;
            discover.Click += async delegate
            {
                await Work("Ищем доступные SQL Server…", async token =>
                {
                    var names = await Task.Run(() => BarsSqlConfiguration.FindServers(), token);
                    token.ThrowIfCancellationRequested();
                    server.Fill(names);
                    status.Text = names.Length == 0 ? "Серверы не обнаружены. Введи адрес вручную." : "Выбери сервер из списка или введи адрес вручную.";
                });
            };
            databases.Click += async delegate { await Work("Читаем список баз…", async token =>
            {
                var names = await BarsSqlConfiguration.Connect(Settings(false), true, token);
                token.ThrowIfCancellationRequested();
                database.Fill(names);
                status.Text = names.Length == 0 ? "У этой учётной записи нет доступных баз." : "Выбери базу данных из списка.";
            }); };
            check.Click += async delegate { await Work("Проверяем подключение…", async token =>
            {
                await BarsSqlConfiguration.Connect(Settings(true), false, token);
                token.ThrowIfCancellationRequested();
                status.Text = "Подключение к выбранной базе подтверждено.";
            }); };
            save.Click += async delegate { await Work("Проверяем подключение перед сохранением…", async token =>
            {
                var selected = configuration.Entries[0];
                var settings = Settings(true);
                await BarsSqlConfiguration.Connect(settings, false, token);
                token.ThrowIfCancellationRequested();
                configuration.Save(selected, settings);
                DialogResult = DialogResult.OK;
            }); };
            FormClosed += delegate { ReleaseLifetime(); };
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            discover.Enabled = databases.Enabled = check.Enabled = !busy;
            server.Enabled = database.Enabled = user.Enabled = password.Enabled = !busy;
            save.Enabled = !busy;
        }

        private async Task Work(string message, Func<CancellationToken, Task> action)
        {
            if (busy || lifetime == null) return;
            busy = true; UpdateButtons(); status.Text = message;
            var source = lifetime;
            var token = source.Token;
            try { await action(token); }
            catch (OperationCanceledException) { }
            catch (SqlException ex)
            {
                if (!token.IsCancellationRequested) status.Text = ex.Number == 18456
                    ? "SQL Server отклонил вход. Проверь имя пользователя и пароль."
                    : ex.Number == 4060 ? "У этой учётной записи нет доступа к выбранной базе."
                    : "Подключение к SQL Server не удалось. Код " + ex.Number + ".";
            }
            catch (InvalidOperationException ex) { if (!token.IsCancellationRequested) status.Text = ex.Message; }
            catch (Exception) { if (!token.IsCancellationRequested) status.Text = "Операция не выполнена. Проверь доступ к серверу или право записи в файл."; }
            finally
            {
                busy = false;
                if (lifetime == null) source.Dispose();
                else if (!IsDisposed && !token.IsCancellationRequested) UpdateButtons();
            }
        }

        private SqlConnectionStringBuilder Settings(bool requireDatabase)
        {
            if (String.IsNullOrWhiteSpace(server.Text)) throw new InvalidOperationException("Укажи адрес SQL Server.");
            if (requireDatabase && String.IsNullOrWhiteSpace(database.Text)) throw new InvalidOperationException("Укажи базу данных.");
            var selected = configuration.Entries[0];
            var settings = selected.Read();
            settings.DataSource = server.Text.Trim(); settings.InitialCatalog = database.Text.Trim();
            settings.UserID = user.Text.Trim(); settings.Password = password.Text;
            settings.IntegratedSecurity = settings.UserID.Length == 0;
            if (settings.IntegratedSecurity && settings.Password.Length > 0)
                throw new InvalidOperationException("Для входа по паролю укажи имя пользователя SQL.");
            return settings;
        }

        private static BrandButton Button(ThemePalette colors, string title)
        { return new BrandButton { Text = title, Palette = colors, Dock = DockStyle.Fill, Margin = Padding.Empty }; }

        private static BrandTextBox Input(ThemePalette colors)
        { return new BrandTextBox { Palette = colors, BackColor = colors.SurfaceAlt, ForeColor = colors.Text, KeepCaretOnFocus = true }; }

        private static void AddRow(TableLayoutPanel layout, int row, string caption, Control field, Control button)
        {
            field.AccessibleName = caption; field.Dock = DockStyle.Fill; field.Margin = Padding.Empty;
            layout.Controls.Add(new Label { Text = caption, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
            if (button == null)
            {
                field.Margin = new Padding(0, 7, 0, 7);
                layout.Controls.Add(field, 1, row);
                layout.SetColumnSpan(field, 2);
                return;
            }
            var pair = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1,
                Padding = new Padding(0, 7, 0, 7), Margin = Padding.Empty };
            pair.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            pair.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 10));
            pair.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 156));
            pair.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            pair.Controls.Add(field, 0, 0); pair.Controls.Add(button, 2, 0);
            layout.Controls.Add(pair, 1, row); layout.SetColumnSpan(pair, 2);
        }

        private void ReleaseLifetime()
        {
            var source = Interlocked.Exchange(ref lifetime, null);
            if (source == null) return;
            source.Cancel();
            if (!busy) source.Dispose();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) ReleaseLifetime();
            base.Dispose(disposing);
        }
    }

    internal sealed class SqlChoiceField : Panel
    {
        private readonly BrandComboBox choices;
        private readonly BrandTextBox editor;
        internal event EventHandler EmptyListRequested
        {
            add { choices.EmptyManagedPopupRequested += value; }
            remove { choices.EmptyManagedPopupRequested -= value; }
        }

        internal SqlChoiceField(ThemePalette colors)
        {
            BackColor = colors.SurfaceAlt;
            choices = new BrandComboBox { Palette = colors, BackColor = colors.SurfaceAlt,
                ForeColor = colors.Text, DropDownStyle = ComboBoxStyle.DropDownList, UseManagedPopup = true,
                DrawMode = DrawMode.OwnerDrawFixed, TabStop = false, HideSelectedText = true };
            editor = new BrandTextBox { Palette = colors, BackColor = colors.SurfaceAlt, ForeColor = colors.Text,
                AutoSize = false, AccessibleName = "Адрес или имя", KeepCaretOnFocus = true };
            Controls.Add(choices); Controls.Add(editor); editor.BringToFront();
            choices.SelectionChangeCommitted += delegate
            {
                if (choices.SelectedItem != null) Text = choices.GetItemText(choices.SelectedItem);
            };
            editor.KeyDown += delegate(object sender, KeyEventArgs args)
            {
                if (args.KeyCode != Keys.F4 && !(args.Alt && args.KeyCode == Keys.Down)) return;
                choices.DroppedDown = true;
                args.Handled = args.SuppressKeyPress = true;
            };
        }

        public override string Text
        {
            get { return editor == null ? base.Text : editor.Text; }
            set
            {
                if (editor == null) { base.Text = value; return; }
                editor.Text = value;
                editor.Select(editor.TextLength, 0);
            }
        }

        internal void Fill(string[] values)
        {
            choices.BeginUpdate();
            try { choices.Items.Clear(); choices.Items.AddRange(values); }
            finally { choices.EndUpdate(); }
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (editor == null || choices == null) return;
            int pad = Math.Max(1, (int)Math.Round(DeviceScale() * 7));
            int arrow = Math.Max(25, SystemInformation.VerticalScrollBarWidth + pad);
            int itemHeight = Math.Max(1, ClientSize.Height - 6);
            if (choices.ItemHeight != itemHeight) choices.ItemHeight = itemHeight;
            choices.SetBounds(0, 0, ClientSize.Width, ClientSize.Height);
            int textHeight = Math.Min(editor.PreferredHeight, Math.Max(1, ClientSize.Height - 2));
            editor.SetBounds(pad, (ClientSize.Height - textHeight) / 2, Math.Max(1, ClientSize.Width - pad - arrow), textHeight);
        }

        private float DeviceScale() { return Font.Height / 17f; }
    }
}
