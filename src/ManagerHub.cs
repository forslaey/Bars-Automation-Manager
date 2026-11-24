using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ServiceTool
{
    internal abstract class ManagerModuleForm : BrandWindow
    {
        protected ThemePalette Colors;
        protected readonly TableLayoutPanel Root;
        private readonly PictureBox logo;
        private readonly ThemeToggleButton theme = new ThemeToggleButton();
        private readonly Image lightLogo;
        private bool initialScaleApplied;
        private readonly ToolTip tips = new ToolTip();
        protected ManagerModuleForm(string title, Size size)
        {
            // Do not let Font/ClientSize trigger DPI scaling before the derived
            // module has added its controls, otherwise only the base header scales.
            logo = new SnowBanner();
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.None;
            Font = new Font("Segoe UI", 10F); Text = title; ClientSize = size;
            MinimumSize = new Size(860, 620); StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true; Icon = BrandAssets.LoadIcon();
            Colors = ThemePalette.Create(ThemePreference.LoadDark());
            NativeWindowTheme.Bind(this,()=>Colors.Dark);
            lightLogo = BrandAssets.LoadLogoResource("BARS.NewYear.png") ?? BrandAssets.LoadLogo(false);
            Root = new BufferedLayoutPanel { Size = size, Dock = DockStyle.Fill, Padding = new Padding(26, 12, 26, 12), ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
            Root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Root.RowStyles.Add(new RowStyle(SizeType.Absolute, 96)); Root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(Root);
            var header = new BufferedLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0, 0, 0, 12), Padding = new Padding(0, 8, 0, 0) };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            logo.Dock = DockStyle.Fill; logo.SizeMode = PictureBoxSizeMode.Zoom; logo.Margin = new Padding(0, 6, 0, 6); logo.Cursor = Cursors.Hand;
            BrandAssets.BindWebsite(logo, tips, this);
            header.Controls.Add(logo, 1, 0);
            theme.Size = new Size(86, 40); theme.Anchor = AnchorStyles.None; theme.AccessibleName = "Сменить тему";
            theme.Click += delegate { SetTheme(!Colors.Dark, true); }; header.Controls.Add(theme, 2, 0); Root.Controls.Add(header, 0, 0);
        }
        protected void HideThemeSelector(){theme.Visible=false;theme.Enabled=false;}
        protected override void OnLoad(EventArgs e)
        {
            if(!initialScaleApplied)
            {
                initialScaleApplied=true;
                AutoScaleMode=AutoScaleMode.Dpi;
                AutoScaleDimensions=new SizeF(96,96);
                PerformAutoScale();
                ResumeLayout(true);
                if(Root.AutoScroll&&Root.Parent==this)
                {
                    // A Dock.Fill body with a minimum height does not reliably
                    // create scrollbars in TableLayoutPanel.AutoScroll.
                    Control body=Root.GetControlFromPosition(0,1);
                    if(body!=null&&body.MinimumSize.Height>0)
                    {
                        Root.AutoScroll=false;
                        Root.MinimumSize=new Size(Root.MinimumSize.Width,body.MinimumSize.Height+Root.Padding.Vertical+(int)Math.Ceiling(Root.RowStyles[0].Height));
                        Controls.Remove(Root);Controls.Add(new FillContentPanel(Root));
                    }
                }
                Rectangle area=Screen.FromControl(this).WorkingArea;
                MinimumSize=new Size(Math.Min(MinimumSize.Width,area.Width),Math.Min(MinimumSize.Height,area.Height));
                if(WindowState==FormWindowState.Normal)Size=new Size(Math.Min(Width,area.Width),Math.Min(Height,area.Height));
            }
            WindowPlacement.Constrain(this);
            base.OnLoad(e);
        }
        internal virtual void ResumeSession() { SetTheme(ThemePreference.LoadDark(), false); }
        protected override void OnShown(EventArgs e) { WindowPlacement.Constrain(this); base.OnShown(e); }
        protected void LimitContent(Size maximum)
        {
            Controls.Remove(Root);
            Controls.Add(new BoundedContentPanel(Root, maximum));
        }
        protected void Describe(Control control, string description) { tips.SetToolTip(control, description); }
        protected BrandButton Button(string text, bool primary)
        { return new BrandButton { Text = text, Primary = primary, Palette = Colors, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 10, 0), Font = Font }; }
        protected Label Label(string text, bool muted)
        { return new Label { Text = text, Tag = muted ? "muted" : "", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty, AutoEllipsis = true }; }
        protected Label ModuleHeading(string text)
        { return ModuleHeadingStyle.Create(text,true); }
        protected Label Caption(string text, bool muted)
        { return new InputCaptionLabel { Text=text.TrimEnd(':')+":",Tag=muted?"muted":"",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,Margin=Padding.Empty,AutoEllipsis=true }; }
        public void SetTheme(bool dark, bool persist)
        {
            Colors = ThemePalette.Create(dark); BackColor = Colors.Canvas; ForeColor = Colors.Text;
            logo.Image = lightLogo; theme.Palette = Colors; theme.Dark = dark;
            ApplyTree(this, Colors.Canvas); NativeWindowTheme.Apply(this, dark); OnThemeChanged(); Invalidate(true);
            if (persist) ThemePreference.TrySave(dark);
        }
        private void ApplyTree(Control control, Color background)
        {
            var card = control as Card;
            if (card != null) { background = Colors.Surface; card.BorderColor = Colors.Border; }
            control.BackColor = background; control.ForeColor = Convert.ToString(control.Tag) == "muted" ? Colors.Muted : Colors.Text;
            var button = control as BrandButton; if (button != null) button.Palette = Colors;
            var check = control as BrandCheckBox; if(check!=null)check.Palette=Colors;
            var journalScroll = control as JournalScrollBar; if (journalScroll != null) journalScroll.Palette = Colors;
            var toggle = control as RefreshToggle; if (toggle != null) toggle.Palette = Colors;
            var input = control as BrandTextBox; if (input != null) { input.Palette = Colors; input.BackColor = Colors.SurfaceAlt; }
            var combo = control as BrandComboBox; if (combo != null) { combo.Palette = Colors; combo.BackColor = Colors.SurfaceAlt; }
            foreach (Control child in control.Controls) ApplyTree(child, background);
        }
        protected virtual void OnThemeChanged() { }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { logo.Image = null; if (lightLogo != null) lightLogo.Dispose(); tips.Dispose(); }
            base.Dispose(disposing);
        }
    }
    internal sealed class ManagerHub : ManagerModuleForm
    {
        public ManagerHub() : base("Менеджер автоматизации Барс", new Size(1120, 770))
        {
            FormBorderStyle=FormBorderStyle.FixedSingle;MaximizeBox=false; MinimumSize = new Size(870, 740); ClientSize = new Size(1120, 770);
            Root.Padding = new Padding(20, 8, 20, 10); Root.RowStyles[0].Height = 84;
            var launcherHeader=(TableLayoutPanel)Root.GetControlFromPosition(0,0);launcherHeader.Padding=new Padding(0,18,0,0);
            Root.AutoScroll=true;
            var body = new BufferedLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty, MinimumSize=new Size(0,670) };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 62)); body.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); body.RowStyles.Add(new RowStyle(SizeType.Absolute, 0)); body.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
            var title = new InheritedThemeLabel {Text="ИНСТРУМЕНТЫ АВТОМАТИЗАЦИИ",Dock=DockStyle.Fill}; title.Font = new Font("Segoe UI", 17, FontStyle.Bold); title.TextAlign = ContentAlignment.MiddleCenter; title.Margin = new Padding(0, 0, 0, 20); body.Controls.Add(title, 0, 0);
            var modules = new BufferedLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3, Margin = Padding.Empty };
            for (int i = 0; i < 3; i++)
            {
                modules.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / 3));
                modules.RowStyles.Add(new RowStyle(SizeType.Percent, 100F / 3));
            }
            bool columnsPending = false;
            modules.Layout += delegate
            {
                if (columnsPending || !modules.IsHandleCreated || modules.IsDisposed) return;
                int width = modules.ClientSize.Width;
                if (width < 3) return;
                bool matches = true;
                for (int i = 0; i < 3; i++)
                    matches &= modules.ColumnStyles[i].SizeType == SizeType.Absolute &&
                        modules.ColumnStyles[i].Width == width / 3 + (i < width % 3 ? 1 : 0);
                if (matches) return;
                columnsPending = true;
                modules.BeginInvoke((MethodInvoker)delegate
                {
                    try
                    {
                        if (modules.IsDisposed) return;
                        int current = modules.ClientSize.Width;
                        modules.SuspendLayout();
                        for (int i = 0; i < 3; i++)
                        {
                            var column = modules.ColumnStyles[i];
                            column.SizeType = SizeType.Absolute;
                            column.Width = current / 3 + (i < current % 3 ? 1 : 0);
                        }
                        modules.ResumeLayout(true);
                    }
                    finally { columnsPending = false; }
                });
            };

            modules.Controls.Add(ModuleCard("Менеджер служб", "Установка служб и настройка их запуска.\r\nПроверка состояния и удобный сбор логов.", "Менеджер служб", delegate { OpenModule(() => new MainForm(null, false)); }), 2, 0);
            modules.Controls.Add(ModuleCard("Менеджер библиотек", "Установка и проверка библиотек.\r\nОбновления .NET и Windows.", "Менеджер библиотек", delegate { OpenModule(() => new LibraryManagerForm(false)); }), 1, 0);
            modules.Controls.Add(ModuleCard("Прикладные программы", "Установка и удаление программ.\r\nНастройка удалённого доступа.", "Прикладные программы", delegate { OpenModule(() => new SoftwareManagerForm(false)); }), 0, 2);
            modules.Controls.Add(ModuleCard("Утилиты и прошивки", "Скачивание, распаковка, удаление фирменных утилит и прошивок.", "Утилиты и прошивки", delegate { OpenModule(() => new ToolsDeploymentForm(false)); }), 1, 2);
            modules.Controls.Add(ModuleCard("Первичные системные настройки", "Политики и защита Windows.\r\nПрава папок и режим КСО.", "Открыть настройки", delegate { OpenModule(() => new InitialSystemSettingsForm()); }), 0, 0);
            modules.Controls.Add(ModuleCard("Логи программ", "Удобный сбор логов для модулей Барс.", "ЛОГИ ПРОГРАММ", delegate { OpenModule(() => new ApplicationLogsForm()); }), 2, 2);
            modules.Controls.Add(ModuleCard("SQL Server и SQL Server Management Studio", "Комплекты SQL Server и SSMS.", "SQL Server и SSMS", delegate { OpenModule(() => new SqlModuleForm()); }), 0, 1);
            modules.Controls.Add(ModuleCard("Программы Барс", "Загрузка и тихая установка программ.", "Программы Барс", delegate { OpenModule(() => new BarsProgramsForm()); }), 1, 1);
            modules.Controls.Add(ModuleCard("Службы и агенты Барс", "Загрузка и распаковка архивов служб.", "Службы и агенты Барс", delegate { OpenModule(() => new BarsServicesForm()); }), 2, 1);
            body.Controls.Add(modules, 0, 1);
            var footer = new Panel { Dock = DockStyle.Fill, Margin = new Padding(5, 0, 5, 0), Padding = new Padding(0, 12, 0, 12) };
            var about = Button("ⓘ", false);
            about.AccessibleName = "О программе";
            Describe(about, about.AccessibleName);
            about.Font = new Font(Font.FontFamily, 13, FontStyle.Bold);
            about.Click += delegate { ModuleNavigation.ShowOwnedAsync(this, new AboutDialog(Colors, Icon)); };
            var guide = Button("Инструкция", false);
            guide.AccessibleName = "Открыть инструкцию";
            guide.Click += async delegate
            {
                using (var dialog = new UserGuideForm()) await ModuleNavigation.ShowOwnedAsync(this, dialog, false);
                if (!IsDisposed) SetTheme(ThemePreference.LoadDark(), false);
            };
            var bundle = Button("Скачать комплект", false);
            bundle.Click += delegate { OpenModule(() => new BundleDownloadForm()); };
            var exit = Button("Закрыть программу", false);
            exit.Click += delegate { Close(); };

            var footerButtons = new[] { about, guide, bundle, exit };
            foreach (var button in footerButtons)
            {
                button.Dock = DockStyle.None;
                button.Margin = Padding.Empty;
                footer.Controls.Add(button);
            }
            bool layingOutFooter = false;
            footer.Layout += delegate
            {
                if (layingOutFooter) return;
                layingOutFooter = true;
                try
                {
                    var area = footer.DisplayRectangle;
                    float scale = body.RowStyles[3].Height / 68F;
                    int height = Math.Min(area.Height, (int)Math.Round(ModuleNavigation.FooterButtonHeight * scale));
                    int gap = Math.Max(1, (int)Math.Round(10F * scale));
                    int remaining = area.Width - height - 3 * gap;
                    if (height < 1 || remaining < 3) return;
                    int width = remaining / 3;
                    int remainder = remaining % 3;
                    int left = area.Left;
                    int top = area.Top + (area.Height - height) / 2;
                    for (int i = 0; i < footerButtons.Length; i++)
                    {
                        int buttonWidth = i == 0 ? height : width;
                        footerButtons[i].SetBounds(left, top, buttonWidth, height);
                        left += buttonWidth + gap + (i < remainder ? 1 : 0);
                    }
                }
                finally { layingOutFooter = false; }
            };

            body.Controls.Add(footer, 0, 3);
            Root.Controls.Add(body, 0, 1);
            LimitContent(new Size(1260, 860));
            SetTheme(Colors.Dark, false);
        }
        private const int ModuleRowHeight = 160;
        private static void EqualRows(TableLayoutPanel panel, int rows)
        {
            if (panel.Height <= 0) return;
            int each = panel.Height / rows;
            if (each <= 0) return;
            bool changed = false;
            for (int i = 0; i < rows && i < panel.RowStyles.Count; i++)
                if ((int)panel.RowStyles[i].Height != each) { panel.RowStyles[i] = new RowStyle(SizeType.Absolute, each); changed = true; }
            if (changed) panel.PerformLayout();
        }
        private Card ModuleCard(string title, string description, string action, EventHandler click)
        {
            var card = new Card { Dock = DockStyle.Fill, Padding = new Padding(18, 12, 18, 14), Margin = new Padding(5, 0, 5, 10) };
            var layout = new BufferedLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            // Compact heading/action rows leave two full description lines visible.
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            var heading = new InheritedThemeLabel { Text = title.ToUpperInvariant(), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Margin = Padding.Empty }; heading.Font = new Font("Segoe UI", 13, FontStyle.Bold); layout.Controls.Add(heading, 0, 0);
            var detail = new InheritedThemeLabel { Font = new Font(Font, Font.Style), Text = description.Replace("\r\n", " "), Tag = "muted", Dock = DockStyle.Fill, Margin = Padding.Empty }; detail.TextAlign = ContentAlignment.MiddleCenter; detail.AutoEllipsis = false; layout.Controls.Add(detail, 0, 1);
            BrandButton open = Button(action, true); open.Margin = new Padding(0, 0, 0, 0); open.Dock = DockStyle.None; open.Anchor = AnchorStyles.Bottom; open.Size = new Size(280, 50); open.Click += click;
            layout.Controls.Add(open, 0, 2);
            bool fitting = false;
            layout.Layout += delegate
            {
                if (fitting || layout.ClientSize.Width < 1 || layout.ClientSize.Height < 1) return;
                fitting = true;
                try { FitCardText(layout, heading, detail); }
                finally { fitting = false; }
            };
            layout.Layout+=delegate{int width=Math.Max(1,Math.Min((int)Math.Round(280F*open.Height/50F),layout.ClientSize.Width));if(open.Width!=width)open.Width=width;}; card.Controls.Add(layout); return card;
        }
        private void FitCardText(TableLayoutPanel layout, Label heading, Label detail)
        {
            const TextFormatFlags format = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;
            int available = layout.ClientSize.Height - (int)layout.RowStyles[2].Height;
            if (available < 1) return;
            var bounds = new Size(layout.ClientSize.Width, Int32.MaxValue);
            for (int step = 0; step <= 5; step++)
            {
                using (var headingFont = new Font(Font.Name, 13F - step * .7F, FontStyle.Bold))
                using (var detailFont = new Font(Font.Name, Math.Max(8F, Font.SizeInPoints - step * .5F), FontStyle.Regular))
                {
                    int headingHeight = TextRenderer.MeasureText(heading.Text, headingFont, bounds, format).Height;
                    int detailHeight = TextRenderer.MeasureText(detail.Text, detailFont, bounds, format).Height;
                    if (headingHeight + detailHeight > available && step < 5) continue;
                    int rowHeight = Math.Max(1, Math.Min(available, (available + headingHeight - detailHeight) / 2));
                    if ((int)layout.RowStyles[0].Height != rowHeight) layout.RowStyles[0].Height = rowHeight;
                    SetCardFont(heading, headingFont);
                    SetCardFont(detail, detailFont);
                    return;
                }
            }
        }

        private static void SetCardFont(Label label, Font font)
        {
            if (label.Font.SizeInPoints == font.SizeInPoints) return;
            var previous = label.Font;
            label.Font = (Font)font.Clone();
            previous.Dispose();
        }

        internal Func<Form> RequestedModule { get; private set; }
        internal Type RequestedModuleType { get; private set; }

        internal void OpenModule<T>(Func<T> factory) where T : Form
        {
            OpenModule(typeof(T), () => factory());
        }
        internal void OpenModule(Type type, Func<Form> factory)
        {
            if (IsDisposed) return;
            var context = ManagerApplicationContext.ActiveContext;
            if (context != null) { context.OpenModule(type, factory); return; }
            if (RequestedModule != null) return;
            RequestedModuleType = type;
            RequestedModule = factory;
            Close();
            // A cancelled close must not retain a stale navigation request.
            if (!IsDisposed && Visible) RequestedModule = null;
        }
    }
}
