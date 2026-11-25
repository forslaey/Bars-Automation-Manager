using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ServiceTool
{
    internal sealed class BundleChoiceDialog : BrandWindow
    {
        private readonly ServiceGrid grid = new ServiceGrid();
        private readonly Font headerFont = new Font("Segoe UI", 10F, FontStyle.Bold);
        internal string SelectedRoot { get; private set; }

        internal static async Task<string> Choose(Form owner, ThemePalette colors, BundleSection section, CancellationToken token)
        {
            string executable = BundleFolders.ExecutableDirectory;
            var candidates = await Task.Run(() => BundleDiscovery.Find(section,
                BundlePreferences.DiscoveredRoot(section) ?? BundlePreferences.RememberedRoot(), executable,
                BundleFolders.DefaultRoot, BundleDiscovery.LocalDrives(), token), token);
            token.ThrowIfCancellationRequested();
            if (owner.IsDisposed || !owner.Visible || candidates.Count == 0) return null;
            if (candidates.Count == 1) return candidates[0].Root;
            using (var dialog = new BundleChoiceDialog(colors, owner.Icon, candidates))
            {
                if (ScopedDialogs.Show(owner, dialog) != DialogResult.OK) throw new OperationCanceledException();
                token.ThrowIfCancellationRequested();
                return dialog.SelectedRoot;
            }
        }

        internal BundleChoiceDialog(ThemePalette colors, Icon icon, IList<BundleCandidate> choices)
        {
            Text = "Найденные комплекты";
            Icon = icon;
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 10F);
            ClientSize = new Size(780, 370);
            MinimumSize = new Size(620, 310);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = MaximizeBox = false;
            BackColor = colors.Canvas;
            ForeColor = colors.Text;
            NativeWindowTheme.Bind(this, () => colors.Dark);

            var layout = new BufferedLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(24, 16, 24, 20) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            layout.Controls.Add(new InputCaptionLabel { Text = "Выбери папку комплекта", Dock = DockStyle.Fill,
                Margin = Padding.Empty, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);

            var card = new Card { BackColor = colors.Surface, BorderColor = colors.Border, Padding = new Padding(10), Dock = DockStyle.Fill, Margin = Padding.Empty };
            var scroll = new LibraryGridScrollBar { Dock = DockStyle.Right, Width = 14, Palette = colors, UseCardBackground = true };
            grid.Dock = DockStyle.Fill;
            grid.ReadOnly = true;
            grid.RowHeadersVisible = false;
            grid.AllowUserToAddRows = grid.AllowUserToDeleteRows = grid.AllowUserToResizeRows = false;
            grid.MultiSelect = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.ScrollBars = ScrollBars.None;
            grid.TouchScrollingEnabled = true;
            grid.BorderStyle = BorderStyle.None;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.Single;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            grid.GridColor = colors.Border;
            grid.BackgroundColor = colors.Surface;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersHeight = 38;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.ColumnHeadersDefaultCellStyle.Font = headerFont;
            grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.DefaultCellStyle.Padding = new Padding(8, 5, 8, 5);
            grid.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            grid.DefaultCellStyle.BackColor = colors.Surface;
            grid.DefaultCellStyle.ForeColor = colors.Text;
            grid.DefaultCellStyle.SelectionBackColor = colors.Selection;
            grid.DefaultCellStyle.SelectionForeColor = colors.SelectionText;
            grid.ColumnHeadersDefaultCellStyle.BackColor = colors.SurfaceAlt;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = colors.Text;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = colors.SurfaceAlt;
            grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = colors.Text;
            grid.RowTemplate.MinimumHeight = 38;
            grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCellsExceptHeaders;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Папка", FillWeight = 50, SortMode = DataGridViewColumnSortMode.NotSortable });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Разделы", FillWeight = 50, SortMode = DataGridViewColumnSortMode.NotSortable });
            foreach (var choice in choices)
            {
                var row = grid.Rows[grid.Rows.Add(choice.Root, choice.Description)];
                row.Tag = choice.Root;
                row.Cells[0].ToolTipText = choice.Root;
                row.Cells[1].ToolTipText = choice.Description;
            }
            scroll.Bind(grid);
            card.Controls.Add(grid);
            card.Controls.Add(scroll);
            layout.Controls.Add(card, 0, 1);

            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(0, 12, 0, 0) };
            var cancel = new BrandButton { Text = "Отмена", DialogResult = DialogResult.Cancel, Palette = colors };
            var select = new BrandButton { Text = "Выбрать комплект", Palette = colors, Primary = true };
            select.Click += delegate
            {
                if (grid.CurrentRow == null) return;
                SelectedRoot = (string)grid.CurrentRow.Tag;
                DialogResult = DialogResult.OK;
            };
            ModuleNavigation.StandardFooter(footer, cancel, select);
            layout.Controls.Add(footer, 0, 2);
            Controls.Add(layout);
            CancelButton = cancel;
            AcceptButton = select;
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) headerFont.Dispose();
        }

    }
}
