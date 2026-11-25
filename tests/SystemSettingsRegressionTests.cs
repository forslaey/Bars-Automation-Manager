using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
namespace ServiceTool
{
    internal static class SystemSettingsRegressionTests
    {
        static int count;
        static void Check(bool value,string message){if(!value)throw new Exception(message);count++;}
        static T Field<T>(object value,string name){return (T)value.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(value);}
        static void Call(object value,string name,params object[] args){value.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(value,args);}
        [STAThread] static int Main()
        {
            try
            {
                // Real Windows PowerShell 5.1 process, but no system-setting commands.
                string large="#"+new string('x',100000)+"\r\n'Проверка длинного сценария выполнена'";
                Check(WindowsComponents.Run(large,delegate{}).Message.Contains("Проверка длинного сценария выполнена"),"Large stdin script and Cyrillic output");
                bool rejected=false;try{WindowsComponents.Run("throw 'Тестовая ошибка'",delegate{});}catch(InvalidOperationException ex){rejected=ex.Message.Contains("Тестовая ошибка");}Check(rejected,"Readable Cyrillic error propagated");
                Check(WindowsComponents.Run("'BARS_RESTART_REQUIRED'",delegate{}).ExitCode==3010,"Restart marker preserved");
                string streams="[Console]::Out.Write(('o'*100000));[Console]::Error.Write(('e'*100000));' Готово'";
                Check(WindowsComponents.Run(streams,delegate{}).Message.EndsWith("Готово"),"Both pipes drained without deadlock");
                string phase = null;
                using(var pulse = new ManualResetEvent(false))
                using(var heartbeat = new LibraryHeartbeat(text => { phase=text; pulse.Set(); },20,InitialSystemSettings.OperationPhase("apply:firewall")))
                    Check(pulse.WaitOne(3000)&&phase.StartsWith("Применяем настройки Windows, прошло "), "Initial heartbeat describes application of settings");
                Check(InitialSystemSettings.OperationPhase("query:all")=="Проверяем настройки Windows","Verification stage has its own text");
                var firewall = WindowsComponents.Parse("firewall.Domain=False\nfirewall.Private=0\nfirewall.Public=False");
                Check(SystemSettingsPresentation.FirewallMatches(firewall,false)&&!SystemSettingsPresentation.FirewallMatches(firewall,true),"All effective profiles must match the requested state");
                firewall["firewall.Public"]="True";
                Check(!SystemSettingsPresentation.FirewallMatches(firewall,false),"Later policy override cannot be reported as successful");
                firewall.Remove("firewall.Public");
                Check(!SystemSettingsPresentation.FirewallMatches(firewall,false),"Missing profile cannot be reported as successful");
                firewall["firewall.Public"]="NotConfigured";
                Check(!SystemSettingsPresentation.FirewallMatches(firewall,false),"Unknown profile is not treated as disabled");
                var empty = WindowsComponents.Parse("");
                var details = SystemSettingsPresentation.Unconfirmed("firewall",false,empty,firewall).Single();
                Check(details.Contains("Общедоступный: не удалось проверить"),"Unknown firewall profile named in result");
                var uac = InitialSystemSettings.AdminPolicies.ToDictionary(p => "admin."+p.Name,p => InitialSystemSettings.Value(p.Bars));
                Check(!SystemSettingsPresentation.Unconfirmed("admin",false,empty,uac).Any(),"All UAC parameters confirmed");
                uac["admin.ConsentPromptBehaviorAdmin"]="5";
                Check(SystemSettingsPresentation.Unconfirmed("admin",false,empty,uac).Count()==1,"UAC secondary parameter mismatch detected");
                var passwords = InitialSystemSettings.PasswordPolicies.ToDictionary(p => p.Name,p => InitialSystemSettings.Value(p.Default));
                var readback = passwords.ToDictionary(p => "passwords."+p.Key,p => p.Value);
                Check(!SystemSettingsPresentation.Unconfirmed("passwords",true,passwords,readback).Any(),"Password defaults verified against applied values");
                readback.Remove("passwords.PasswordComplexity");
                Check(SystemSettingsPresentation.Unconfirmed("passwords",true,passwords,readback).Count()==1,"Missing password policy is not confirmed");
                var screen = WindowsComponents.Parse("smartscreen.Local=Off\nsmartscreen.Policy=1");
                Check(SystemSettingsPresentation.Unconfirmed("smartscreen",false,empty,screen).Any(),"SmartScreen policy overrides local preference");
                var folders = WindowsComponents.Parse("folders.Count=1\nfolders.Checked=7\nfolders.Restricted=0\nfolders.Errors=0\nfolders.Skipped=1");
                Check(SystemSettingsPresentation.Unconfirmed("folders",false,empty,folders).Any(),"Skipped folder entries block full confirmation");
                folders["folders.Skipped"]="0";
                Check(!SystemSettingsPresentation.Unconfirmed("folders",false,empty,folders).Any(),"Fully audited folder access is confirmed");
                var kiosk = WindowsComponents.Parse("kiosk.Configured=1\nkiosk.Backup=1\nkiosk.Support=Unconfirmed");
                Check(!SystemSettingsPresentation.Unconfirmed("kiosk",false,empty,kiosk).Any(),"Stored KSO settings confirm separately from gestures");
                Check(SystemSettingsPresentation.Unconfirmed("kiosk",true,kiosk,kiosk).Any(),"Remaining backup blocks restoration confirmation");
                Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
                foreach(bool dark in new[]{false,true})using(var form=new InitialSystemSettingsForm(true))
                {
                    form.SetTheme(dark,false);form.ShowInTaskbar=false;form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-20000,-20000);form.Show();Application.DoEvents();
                    var grid=Field<ServiceGrid>(form,"grid");Check(grid.Rows.Cast<DataGridViewRow>().Select(item=>Convert.ToString(item.Tag)).SequenceEqual(new[]{"passwords","admin","firewall","smartscreen","folders","kiosk"})&&grid.TouchScrollingEnabled,"Supported settings and native touch scrolling");
                    for(int pass=0;pass<8;pass++)
                    {
                        form.WindowState=FormWindowState.Maximized;Application.DoEvents();form.WindowState=FormWindowState.Normal;form.ClientSize=pass%2==0?new Size(1000,740):new Size(1200,900);Application.DoEvents();
                        Check(grid.Parent.ClientRectangle.Contains(grid.Bounds),"Grid stays inside viewport on resize");
                        var log=Field<JournalTextBox>(form,"journal");Check(log.Parent.ClientRectangle.Contains(log.Bounds),"Journal stays inside card");
                        Check(grid.PointToScreen(new Point(0,grid.Height)).Y<=log.PointToScreen(Point.Empty).Y,"Table does not overlap journal");
                        var cell=grid.GetCellDisplayRectangle(0,grid.FirstDisplayedScrollingRowIndex,false);var point=new Point(cell.Left+20,cell.Top+20);
                        grid.TouchPan(point,true,false);grid.TouchPan(new Point(point.X,point.Y-500),false,true);Check(grid.FirstDisplayedScrollingRowIndex>=0,"Swipe remains valid after resize");
                    }
                    var secondaryCaptions=new[]{"Вернуть стандартные политики","Вернуть стандартные политики","Включить все профили","Вернуть стандартные политики","Проверить папки","Вернуть прежние настройки"};
                    var apply = Field<BrandButton>(form,"applyButton");
                    var standard = Field<BrandButton>(form,"standardButton");
                    foreach(var size in new[]{new Size(950,720),new Size(1050,780),new Size(1260,940)})
                    {
                        form.ClientSize = size;
                        grid.CurrentCell=grid.Rows[0].Cells[0];
                        Application.DoEvents();
                        var band = apply.Parent.Controls.Cast<Control>().Select(button => button.Bounds).ToArray();
                        for(int row=0;row<secondaryCaptions.Length;row++)
                        {
                            grid.CurrentCell=grid.Rows[row].Cells[0];
                            Application.DoEvents();
                            Check(apply.Enabled==(row!=5||Field<bool>(form,"kioskAvailable")),"Selected setting respects availability");
                            Check(standard.Text==secondaryCaptions[row],"Secondary action matches selected row");
                            Check(apply.Parent.Controls.Cast<Control>().Select(button => button.Bounds).SequenceEqual(band),
                                "All five top buttons keep their size and position when the setting changes");
                        }
                    }
                    Call(form,"ShowState","admin.EnableLUA=0\r\nfirewall.Domain=True\r\nfirewall.Private=False\r\nfirewall.Public=True\r\nsmartscreen.Error=Тестовая ошибка");
                    Check(Convert.ToString(grid.Rows[1].Cells[2].Value).Contains("Отключено"),"UAC state updated in grid");Check(Convert.ToString(grid.Rows[3].Cells[2].Value).Contains("не выполнена"),"Query error not reported as disabled");
                    Call(form,"Lock",true);Check(!Field<BrandButton>(form,"applyButton").Enabled&&!Field<BrandButton>(form,"refresh").Enabled,"Busy blocks actions");Call(form,"Lock",false);
                    using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"settings-grid-"+dark+".png"));}
                    form.Close();
                }
                Console.WriteLine("SystemSettingsRegressionTests: "+count+" passed; actual PowerShell transport, preview resize/touch. No system settings changed.");return 0;
            }
            catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
        }
    }
}
