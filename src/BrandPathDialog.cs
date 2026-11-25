using System;
using System.Drawing;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace ServiceTool
{
    internal sealed class BrandPathDialog : ManagerModuleForm, IMessageFilter
    {
        private readonly BrandTextBox path=new BrandTextBox{Dock=DockStyle.Fill};
        private readonly BrandTextBox name=new BrandTextBox{Dock=DockStyle.Fill};
        private readonly ServiceGrid grid=new ServiceGrid();
        private readonly Label status;
        private readonly bool folders, save, create;
        private readonly string[] patterns;
        private readonly string extension;
        private string directory;
        private readonly List<string> history=new List<string>();
        private int historyIndex=-1;
        private int navigationRequest;
        private bool readingFolder;
        private Button acceptPath, makeFolder;

        private void SetReading(bool reading)
        {
            readingFolder = reading;
            acceptPath.Enabled = !reading;
            if (!makeFolder.IsDisposed) makeFolder.Enabled = !reading;
        }
        private string lastTapPath;
        private long lastTapTime;
        internal string SelectedPath {get;private set;}
        internal BrandPathDialog(string title,string start,string file,string filter,string defaultExtension,bool folders,bool save,bool create)
            :base(title,new Size(670,500))
        {
            this.folders=folders;this.save=save;this.create=create;
            extension=defaultExtension;
            string[] filters=(filter??"").Split('|');
            patterns=filters.Length>1?filters.Where((value,index)=>index%2==1).SelectMany(value=>value.Split(';')).ToArray():new[]{"*"};
            directory=BundleFolders.PickerStart(start);
            ClientSize=new Size(670,500);MinimumSize=new Size(450,360);
            Root.RowStyles[0].Height=58;HideThemeSelector();
            var body=new BufferedLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=5,Margin=Padding.Empty};
            foreach(int height in new[]{44,0,folders?0:38,28,50})
                body.RowStyles.Add(height==0?new RowStyle(folders&&body.RowStyles.Count==2?SizeType.Absolute:SizeType.Percent,folders&&body.RowStyles.Count==2?0:100):new RowStyle(SizeType.Absolute,height));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            var nav=new BufferedLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=new Padding(0,0,0,8)};
            nav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            nav.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,96));
            nav.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,96));
            nav.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            path.AccessibleName="Путь к папке";path.Margin=new Padding(0,0,10,0);
            path.Dock=DockStyle.None;path.Anchor=AnchorStyles.Left|AnchorStyles.Right;
            var pathFrame=new PathFieldFrame(path);nav.Controls.Add(pathFrame,0,0);
            nav.Layout+=delegate{int height=PathFieldMetrics.Measure(path).Height;if(pathFrame.Height!=height)pathFrame.Height=height;};
            var up=Button("Выше",false);up.Click+=delegate{if(String.IsNullOrEmpty(directory))return;var parent=Directory.GetParent(directory);if(parent==null)ShowDrives();else Navigate(parent.FullName);};
            var go=Button("Перейти",false);go.Click+=delegate{NavigateFromControls();};
            nav.Controls.Add(up,1,0);nav.Controls.Add(go,2,0);
            path.KeyDown+=delegate(object sender,KeyEventArgs e){if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;Navigate(path.Text.Trim());}};
            body.Controls.Add(nav,0,0);
            grid.BorderStyle=BorderStyle.None;grid.ScrollBars=ScrollBars.None;grid.RowHeadersVisible=false;
            grid.CellBorderStyle=DataGridViewCellBorderStyle.Single;grid.ColumnHeadersBorderStyle=DataGridViewHeaderBorderStyle.Single;grid.ReadOnly=true;grid.MultiSelect=false;grid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;
            grid.AllowUserToAddRows=grid.AllowUserToDeleteRows=grid.AllowUserToResizeRows=false;
            grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;grid.RowTemplate.Height=30;grid.TouchScrollingEnabled=true;
            grid.Columns.Add("name","Имя");
            grid.Columns[0].SortMode=DataGridViewColumnSortMode.NotSortable;
            var scroll=new LibraryGridScrollBar{Dock=DockStyle.Right,Width=14,AlignToRows=true,UseCardBackground=true,LimitTrackToWholeRows=true};
            var card=new Card{Dock=DockStyle.Fill,Padding=new Padding(10),Margin=new Padding(0,0,0,8)};
            grid.Dock=DockStyle.Fill;card.Controls.Add(grid);card.Controls.Add(scroll);scroll.Bind(grid);
            body.Controls.Add(card,0,1);
            grid.SelectionChanged+=delegate
            {
                if(folders)return;
                string item=SelectedRowPath;
                if(item!=null&&!IsFolderRow(item))name.Text=Path.GetFileName(item);
                else if(!save&&IsFolderRow(item))name.Clear();
            };
            grid.TouchCellTapped+=OnTouchCellTapped;
            grid.TouchTapInterrupted+=delegate{lastTapPath=null;};
            grid.CellDoubleClick+=delegate(object sender,DataGridViewCellEventArgs e){if(e.RowIndex>=0)OpenRow(grid.Rows[e.RowIndex].Tag as string);};
            grid.KeyDown+=delegate(object sender,KeyEventArgs e){if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;OpenRow(SelectedRowPath);}};
            if(!folders)
            {
                name.Text=file??"";name.AccessibleName="Имя файла";name.Margin=new Padding(0,0,0,4);
                body.Controls.Add(new PathFieldFrame(name),0,2);
            }
            status=Label("",true);body.Controls.Add(status,0,3);
            var footer=new BufferedLayoutPanel{Dock=DockStyle.Fill,Margin=Padding.Empty};
            var cancel=Button("Отмена",false);cancel.DialogResult=DialogResult.Cancel;CancelButton=cancel;
            var accept=Button(save?"Сохранить":folders?"Выбрать папку":"Открыть",true);accept.Click+=delegate{AcceptPath();};AcceptButton=accept;
            var make=Button("Новая папка",false);make.Click+=delegate{CreateFolder();};
            ModuleNavigation.StandardFooter(footer,create?new[]{make,cancel,accept}:new[]{cancel,accept});
            acceptPath=accept;makeFolder=make;
            if(!create)make.Dispose();
            body.Controls.Add(footer,0,4);Root.Controls.Add(body,0,1);
            Shown+=delegate
            {
                Application.AddMessageFilter(this);
                path.Text=directory;
                Navigate(directory, initialFile: file);
            };
            SetTheme(Colors.Dark,false);
            {scroll.Palette=Colors;scroll.BackColor=Colors.Surface;}
        }
        protected override void OnThemeChanged()
        {
            base.OnThemeChanged();
            if(grid==null)return;
            grid.EnableHeadersVisualStyles=false;
            grid.BackgroundColor=grid.DefaultCellStyle.BackColor=Colors.Surface;
            grid.DefaultCellStyle.ForeColor=Colors.Text;grid.DefaultCellStyle.SelectionBackColor=Colors.Selection;
            grid.DefaultCellStyle.SelectionForeColor=Colors.SelectionText;grid.GridColor=Colors.Border;
            grid.ColumnHeadersDefaultCellStyle.BackColor=Colors.SurfaceAlt;grid.ColumnHeadersDefaultCellStyle.ForeColor=Colors.Text;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor=Colors.SurfaceAlt;grid.ColumnHeadersDefaultCellStyle.SelectionForeColor=Colors.Text;
            if(grid.Parent!=null)foreach(var scroll in grid.Parent.Controls.OfType<LibraryGridScrollBar>()){scroll.Palette=Colors;scroll.BackColor=Colors.Surface;}
        }
        protected override void Dispose(bool disposing)
        {
            if(disposing)Application.RemoveMessageFilter(this);
            base.Dispose(disposing);
        }

        bool IMessageFilter.PreFilterMessage(ref Message message)
        {
            const int xButtonUp=0x020C;
            if(message.Msg!=xButtonUp||!Visible||!Enabled)return false;
            var control=Control.FromChildHandle(message.HWnd);
            if(control==null||control.FindForm()!=this)return false;
            int button=(int)((message.WParam.ToInt64()>>16)&0xFFFF);
            if(button!=1&&button!=2)return false;
            MoveHistory(button==1?-1:1);
            return true;
        }

        private void MoveHistory(int offset)
        {
            int target=historyIndex+offset;
            if(target<0||target>=history.Count)return;
            string address=history[target];
            if(address==null) { ShowDrives(false); historyIndex=target; }
            else Navigate(address, historyTarget: target);
        }

        private void Remember(string address)
        {
            if(historyIndex>=0&&String.Equals(history[historyIndex],address,StringComparison.OrdinalIgnoreCase))return;
            history.RemoveRange(historyIndex+1,history.Count-historyIndex-1);
            history.Add(address);
            historyIndex=history.Count-1;
        }

        private void OnTouchCellTapped(object sender,DataGridViewCellEventArgs e)
        {
            string item=grid.Rows[e.RowIndex].Tag as string;
            long now=System.Diagnostics.Stopwatch.GetTimestamp();
            double elapsed=(now-lastTapTime)*1000.0/System.Diagnostics.Stopwatch.Frequency;
            if(item!=null&&String.Equals(item,lastTapPath,StringComparison.OrdinalIgnoreCase)&&elapsed<=SystemInformation.DoubleClickTime)
            {
                lastTapPath=null;
                OpenRow(item);
                return;
            }
            lastTapPath=item;
            lastTapTime=now;
        }

        private void ShowDrives(bool remember=true)
        {
            navigationRequest++;
            SetReading(false);
            grid.Rows.Clear();
            foreach(string root in Directory.GetLogicalDrives())grid.Rows[grid.Rows.Add(root)].Tag=root;
            grid.ClearSelection();
            directory=null;path.Clear();lastTapPath=null;
            if(remember)Remember(null);
            status.Text="Выбери диск.";
        }
        private void Navigate(string value, int historyTarget = -1, string initialFile = null)
        {
            int request = ++navigationRequest;
            if (!Path.IsPathRooted(value))
            {
                SetReading(false);
                status.Text = "Укажи полный путь к папке.";
                return;
            }
            SetReading(true);
            status.Text = "Читаем папку…";
            System.Threading.Tasks.Task.Run(() =>
            {
                string next = null, failure = null;
                string[] dirs = null, files = null;
                try
                {
                    next = Path.GetFullPath(value);
                    dirs = Directory.GetDirectories(next).OrderBy(item => item, StringComparer.CurrentCultureIgnoreCase).ToArray();
                    files = folders ? new string[0] : patterns.SelectMany(pattern => Directory.GetFiles(next, pattern))
                        .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(item => item, StringComparer.CurrentCultureIgnoreCase).ToArray();
                }
                catch (Exception error) { failure = OperationMessage.FinishSentence(error.Message); }

                if (!IsHandleCreated || IsDisposed) return;
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (request != navigationRequest || IsDisposed || !Visible) return;
                        if (failure != null) { status.Text = failure; SetReading(false); return; }
                        directory = next;
                        path.Text = next;
                        if (historyTarget >= 0) historyIndex = historyTarget;
                        else Remember(next);
                        grid.Rows.Clear();
                        foreach (string item in dirs) grid.Rows[grid.Rows.Add("▸  " + Path.GetFileName(item))].Tag = item;
                        foreach (string item in files) grid.Rows[grid.Rows.Add(Path.GetFileName(item))].Tag = item;
                        grid.ClearSelection();
                        if (!folders && (!save || initialFile != null)) name.Text = initialFile ?? "";
                        lastTapPath = null;
                        status.Text = "";
                        SetReading(false);
                    });
                }
                catch (InvalidOperationException) { } // The dialog closed before dispatch.
            });
        }

        private string SelectedRowPath
        {
            get { return grid.SelectedRows.Count==1?grid.SelectedRows[0].Tag as string:null; }
        }

        private bool IsFolderRow(string item)
        {
            if (item == null) return false;
            return directory == null || folders || grid.Rows.Cast<DataGridViewRow>().Any(row =>
                Equals(row.Tag, item) && Convert.ToString(row.Cells[0].Value).StartsWith("▸  ", StringComparison.Ordinal));
        }

        private void NavigateFromControls()
        {
            string address=path.Text.Trim();
            string selected=SelectedRowPath;
            if(String.Equals(address,directory??"",StringComparison.OrdinalIgnoreCase)&&IsFolderRow(selected))
                address=selected;
            Navigate(address);
        }

        private void OpenRow(string item)
        {
            if(IsFolderRow(item))Navigate(item);
            else if(!folders&&item!=null)
            {
                name.Text=Path.GetFileName(item);
                AcceptPath();
            }
        }
        private void AcceptPath()
        {
            if (readingFolder) return;
            try
            {
                if(!folders&&!save&&String.IsNullOrWhiteSpace(name.Text)&&IsFolderRow(SelectedRowPath))
                {
                    NavigateFromControls();
                    return;
                }
                string value=folders?(String.Equals(path.Text,directory,StringComparison.OrdinalIgnoreCase)&&grid.SelectedRows.Count==1?grid.SelectedRows[0].Tag as string:path.Text):name.Text.Trim();
                if(String.IsNullOrWhiteSpace(value))throw new IOException("Укажи имя файла или папку.");
                string result=Path.GetFullPath(Path.IsPathRooted(value)?value:Path.Combine(directory??"",value));
                if(folders&&!Directory.Exists(result)||!folders&&!save&&!File.Exists(result))throw new IOException("Выбранный путь не найден.");
                if(save)
                {
                    if(!Path.HasExtension(result)&&!String.IsNullOrEmpty(extension))result+="."+extension.TrimStart('.');
                    if(Directory.Exists(result))throw new IOException("Выбранный путь занят папкой. Укажи имя файла."); if(!Directory.Exists(Path.GetDirectoryName(result)))throw new IOException("Папка назначения не найдена.");
                    if(File.Exists(result)&&BrandMessageBox.Show(this,"Файл уже существует. Заменить его?",Text,MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
                }
                SelectedPath=result;DialogResult=DialogResult.OK;Close();
            }
            catch(Exception ex){status.Text=OperationMessage.FinishSentence(ex.Message);}
        }
        private void CreateFolder()
        {
            if (readingFolder) return;
            try
            {
                if (String.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                    throw new IOException("Сначала открой папку, в которой нужно создать новую.");
                using (var prompt = new NewFolderNameDialog(Colors, Icon, directory))
                {
                    if (ScopedDialogs.Show(this, prompt) == DialogResult.OK)
                        Navigate(prompt.CreatedPath);
                }
            }
            catch(Exception ex){status.Text=OperationMessage.FinishSentence(ex.Message);}
        }
    }

    internal sealed class NewFolderNameDialog : BrandWindow
    {
        internal string CreatedPath { get; private set; }

        internal static string CreateAt(string parent, string name)
        {
            if (String.IsNullOrEmpty(name) || name != name.Trim() || name.EndsWith(".", StringComparison.Ordinal) ||
                name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                System.Text.RegularExpressions.Regex.IsMatch(name, @"^(CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³])(?:\s*\.|$)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                throw new IOException("Введи допустимое имя одной папки.");
            if (!Directory.Exists(parent)) throw new DirectoryNotFoundException("Родительская папка больше не доступна.");
            string path = Path.Combine(parent, name);
            if (Directory.Exists(path) || File.Exists(path)) throw new IOException("Такое имя уже занято.");
            Directory.CreateDirectory(path);
            return path;
        }

        internal static string DestinationText(string parent)
        {
            string root = Path.GetPathRoot(parent);
            if (String.IsNullOrEmpty(root) || root.Length < 2 || root[1] != ':')
                return "Папка будет создана по пути " + parent + ".";
            string drive = root.Substring(0, 2);
            return String.Equals(root.TrimEnd('\\'), parent.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
                ? "Папка будет создана на диске " + root
                : "Папка будет создана на диске " + drive + " в папке " + parent + ".";
        }

        internal NewFolderNameDialog(ThemePalette colors, Icon icon, string parent)
        {
            Text = "Новая папка";
            Icon = icon;
            ClientSize = new Size(470, 168);
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 10);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = MaximizeBox = false;
            ShowInTaskbar = false;
            BackColor = colors.Canvas;
            ForeColor = colors.Text;
            NativeWindowTheme.Bind(this, () => colors.Dark);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(22, 12, 22, 14) };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            var name = new BrandTextBox { Dock = DockStyle.Fill, Palette = colors, BackColor = colors.SurfaceAlt,
                ForeColor = colors.Text, AccessibleName = "Имя новой папки", MaxLength = 255, Margin = Padding.Empty };
            var caption = new InputCaptionLabel { Text = "Имя новой папки", Dock = DockStyle.Fill,
                Margin = Padding.Empty, TextAlign = ContentAlignment.TopLeft };
            var status = new FlushLabel { Text = DestinationText(parent), ForeColor = colors.Muted,
                Dock = DockStyle.Fill, Margin = Padding.Empty, SingleLine = true, AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft };
            layout.Controls.Add(caption, 0, 0);
            layout.Controls.Add(new PathFieldFrame(name), 0, 1);
            layout.Controls.Add(status, 0, 2);
            status.AccessibleDescription = status.Text;
            var destinationTip = new ToolTip { ShowAlways = true };
            destinationTip.SetToolTip(status, status.Text);
            Disposed += delegate { destinationTip.Dispose(); };
            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            var cancel = new BrandButton { Text = "Отмена", Palette = colors, DialogResult = DialogResult.Cancel };
            var create = new BrandButton { Text = "Создать", Primary = true, Palette = colors, Enabled = false };
            name.TextChanged += delegate { create.Enabled = !String.IsNullOrWhiteSpace(name.Text); };
            create.Click += delegate
            {
                try { CreatedPath = CreateAt(parent, name.Text); DialogResult = DialogResult.OK; Close(); }
                catch (Exception error)
                {
                    if (!(error is IOException) && !(error is UnauthorizedAccessException) && !(error is ArgumentException)) throw;
                    status.Text = error.Message;
                    name.Focus();
                }
            };
            ModuleNavigation.StandardFooter(footer, cancel, create);
            layout.Controls.Add(footer, 0, 3);
            Controls.Add(layout);
            AcceptButton = create;
            CancelButton = cancel;
            Shown += delegate { name.Focus(); };
        }
    }
}
