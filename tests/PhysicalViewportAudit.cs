using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace ServiceTool
{
    internal static class PhysicalViewportAudit
    {
        private sealed class PackageWindow : BarsPackagesForm
        {
            internal PackageWindow(bool services, string path) : base(services, path) { }
        }

        private static IEnumerable<Control> Descendants(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                yield return child;
                foreach (var descendant in Descendants(child)) yield return descendant;
            }
        }

        private static void Pump()
        {
            for (int i = 0; i < 4; i++) Application.DoEvents();
        }

        [STAThread]
        private static int Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var sizes = new[] { new Size(1024, 768), new Size(1280, 720), new Size(1366, 768),
                new Size(1920, 1080), new Size(2560, 1440), new Size(3840, 2160),
                new Size(768, 1024), new Size(1080, 1920), new Size(1440, 2560) };
            var scales = new[] { 1F, 1.25F, 1.5F, 1.75F, 2F, 2.25F, 2.5F };
            string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fixtures");
            Directory.CreateDirectory(root);
            int total = 0, affected = 0;
            foreach (bool dark in new[] { false, true })
            foreach (float scale in scales)
            {
                Func<Form>[] factories = {
                    () => new PackageWindow(false, root), () => new PackageWindow(true, root),
                    () => new BundleChoiceDialog(ThemePalette.Create(dark), null, new List<BundleCandidate>()),
                    () => new NewFolderNameDialog(ThemePalette.Create(dark), null, @"C:\A long folder name used for the layout audit\Manager\Program archives"),
                    () => new BrandPathDialog("Проверка выбора пути", root, null, null, null, true, false, true),
                    () => new UserGuideForm(), () => new ManagerHub()
                };
                foreach (var factory in factories)
                using (var form = factory())
                {
                    var manager = form as ManagerModuleForm;
                    if (manager != null) manager.SetTheme(dark, false);
                    form.StartPosition = FormStartPosition.Manual;
                    form.Location = new Point(-18000, -18000);
                    form.ShowInTaskbar = false;
                    form.Show();
                    Pump();
                    float nativeDpi; using (var graphics = form.CreateGraphics()) nativeDpi = graphics.DpiX;
                    if (total == 0) Console.WriteLine("Native process DPI: " + nativeDpi);
                    float nativeScale = nativeDpi / 96F;
                    if (Math.Abs(nativeScale - 1F) > 0.001F)
                        form.Scale(new SizeF(1F / nativeScale, 1F / nativeScale));
                    if (scale != 1F) form.Scale(new SizeF(scale, scale));
                    Pump();
                    Size preferredDialog = form.Size;
                    int borderWidth = form.Width - form.ClientSize.Width;
                    int borderHeight = form.Height - form.ClientSize.Height;
                    Control surface = form.Controls.Cast<Control>().First(c => c.Dock == DockStyle.Fill);
                    surface.Dock = DockStyle.None;
                    surface.Anchor = AnchorStyles.Top | AnchorStyles.Left;
                    surface.Location = Point.Empty;
                    foreach (var size in sizes)
                    {
                        string key = form.GetType().Name + "/" + form.Text + "/" + size.Width + "x" + size.Height + "/" + scale + "/" + (dark ? "dark" : "light");
                        var area = new Rectangle(-18000, -18000, size.Width, size.Height - 40);
                        var sizeToTest = form is PackageWindow || form is UserGuideForm || form is ManagerHub ? area.Size : preferredDialog;
                        form.MinimumSize = Size.Empty;
                        form.Size = sizeToTest;
                        WindowPlacement.Center(form, area);
                        int width = Math.Min(area.Width, sizeToTest.Width) - borderWidth;
                        int height = Math.Min(area.Height, sizeToTest.Height) - borderHeight;
                        surface.Size = new Size(Math.Max(1, width), Math.Max(1, height));
                        surface.PerformLayout();
                        Pump();
                        total++;
                        var findings = new List<string>();
                        if (surface.Width != width || surface.Height != height) findings.Add("content surface clamped to " + surface.Size);
                        foreach (var grid in Descendants(form).OfType<DataGridView>().Where(g => g.Visible))
                        {
                            if (grid.Height - grid.ColumnHeadersHeight < grid.RowTemplate.Height)
                                findings.Add("table cannot show one row (" + grid.Height + "/" + grid.ColumnHeadersHeight + "/" + grid.RowTemplate.Height + ")");
                            int actualColumns = grid.Columns.Cast<DataGridViewColumn>().Where(c => c.Visible).Sum(c => c.Width);
                            if (actualColumns > grid.ClientSize.Width + 2 && grid.ScrollBars == ScrollBars.None)
                                findings.Add("table columns exceed viewport (" + actualColumns + "/" + grid.ClientSize.Width + ")");
                        }
                        foreach (var button in Descendants(form).OfType<BrandButton>().Where(b => b.Visible && b.Text.Length > 0))
                        {
                            if (button.Width < 1 || button.Height < 1) findings.Add("zero size button " + button.Text);
                            if (!button.Parent.ClientRectangle.Contains(button.Bounds) && !button.Parent.AutoScrollEnabled())
                                findings.Add("button outside parent " + button.Text);
                        }
                        if (findings.Count > 0)
                        {
                            affected++;
                            Console.WriteLine(key + ": " + String.Join(", ", findings.Distinct()));
                        }
                    }
                    form.Close();
                    Pump();
                }
            }
            Console.WriteLine("Physical viewport cases: " + total + "; cases with warnings: " + affected);
            return affected == 0 ? 0 : 1;
        }

        private static bool AutoScrollEnabled(this Control control)
        {
            for (; control != null; control = control.Parent)
                if (control is ScrollableControl && ((ScrollableControl)control).AutoScroll) return true;
            return false;
        }
    }
}
