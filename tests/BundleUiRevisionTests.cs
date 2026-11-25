using System;
using System.Drawing;
using System.Linq;
using System.IO;
using System.Windows.Forms;
namespace ServiceTool
{
 internal static class BundleUiRevisionTests
 {
  static int count;static void Check(bool ok,string message){if(!ok)throw new Exception(message);count++;}
  static System.Collections.Generic.IEnumerable<Control> Children(Control p){foreach(Control c in p.Controls){yield return c;foreach(var child in Children(c))yield return child;}}
  [STAThread] static int Main(){try{
   foreach(bool dark in new[]{false,true})using(var form=new BundleDownloadForm()){
    form.SetTheme(dark,false);form.Show();Application.DoEvents();var controls=Children(form).ToArray();
    Check(form.Text=="Скачать комплект","Downloader uses its module title");
    Check(!controls.OfType<ServiceGrid>().Any(),"No per-file grid in primary module");Check(!controls.OfType<NumericUpDown>().Any(),"No concurrency selector");
    var choices=controls.OfType<BrandCheckBox>().OrderBy(c=>c.Top).ThenBy(c=>c.Left).ToArray();
    Check(choices.Select(c=>c.Text).SequenceEqual(new[]{"Библиотеки и .NET","SQL server и SSMS","Программы, службы и агенты Барс","Прикладные программы","Утилиты и прошивки","Обои для рабочего стола"}),"Six sections in the requested order");
    Check(choices.All(c=>!c.Checked),"No components selected by default");
    Check(!choices[2].Enabled&&!choices[2].Checked&&choices.Where((c,i)=>i!=2).All(c=>c.Enabled),"Only the future BARS section is disabled");
    Check(form.FormBorderStyle==FormBorderStyle.FixedSingle&&!form.MaximizeBox,"Fixed compact main window");Check(controls.OfType<BrandButton>().Any(b=>b.Text=="Результаты"),"Reopen results button");Check(controls.OfType<JournalTextBox>().Count()==1,"Embedded journal");Check(controls.OfType<BundleProgressBar>().Count()==1&&!controls.OfType<ProgressBar>().Any(),"Branded progress only");
    var buttons=controls.OfType<BrandButton>().Where(b=>new[]{"Проверить","Скачать","Результаты","Пауза","Отменить","В меню","Закрыть"}.Contains(b.Text)).ToArray();Check(buttons.Length==7&&buttons.Max(b=>b.Height)-buttons.Min(b=>b.Height)<=1,"Equal lower action button heights (DPI rounding <=1px)");
    controls.OfType<BrandButton>().Single(b=>b.Text=="В меню").PerformClick();Check(form.MenuRequested,"Module menu request");
   }
   using(var sqlOnly=new BundleDownloadForm(sqlOnly:true)){
    var selected=Children(sqlOnly).OfType<BrandCheckBox>().Where(c=>c.Checked).ToArray();
    Check(selected.Length==1&&selected[0].Text=="SQL server и SSMS","SQL entry point still selects SQL");
   }
   string temporary=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"bars-logs-path-"+Guid.NewGuid().ToString("N"));
   try {
    string folder=System.IO.Directory.CreateDirectory(System.IO.Path.Combine(temporary,"Program")).FullName;
    string setting=System.IO.Path.Combine(temporary,"settings","logs.txt");
    ApplicationLogsForm.RememberFolder(setting,folder);
    ApplicationLogsForm.RememberFolder(setting,System.IO.Path.Combine(temporary,"missing"));
    Check(ApplicationLogsForm.ReadFolderPreference(setting)==folder,"Last valid folder survives reopening");
    using(var logs=new ApplicationLogsForm()){
     var buttons=Children(logs).OfType<BrandButton>().ToArray();
     Check(buttons.Any(button=>button.Text=="Выбрать папку…")&&!buttons.Any(button=>button.Text=="Выбрать файл…"),"Logs expose only a folder picker");
    }
   }finally{if(System.IO.Directory.Exists(temporary))System.IO.Directory.Delete(temporary,true);}
   string customRoot=Path.Combine(Path.GetTempPath(),"Комплект в другой папке");
   Check(BarsPackagesForm.PackageFolder(customRoot,false)==Path.Combine(customRoot,"_soft","_Bars","_Soft"),"BARS programs use the selected bundle root");
   Check(BarsPackagesForm.PackageFolder(customRoot,true)==Path.Combine(customRoot,"_soft","_Bars","_Services"),"BARS services use the selected bundle root");
   var nextVersion=new Version(2,3,104,0);
   string standard=Path.Combine(customRoot,"Bars.Automation.Manager.2.3.103.exe");
   string renamed=Path.Combine(customRoot,"Менеджер автоматизации Барс 2.3.102.exe");
   string custom=Path.Combine(customRoot,"My portable manager.exe");
   string next=Path.Combine(customRoot,"Bars.Automation.Manager.2.3.104.exe");
   Check(ManagerUpdates.ReleaseFileName(nextVersion)==Path.GetFileName(next),"Canonical release asset name");
   Check(ManagerUpdates.UpdatedPath(standard,nextVersion)==next,"Standard executable advances its version");
   Check(ManagerUpdates.UpdatedPath(renamed,nextVersion)==next,"Previous Russian executable is renamed to the standard");
   Check(ManagerUpdates.UpdatedPath(custom,nextVersion)==custom,"Custom executable names remain unchanged");
   Check(BundleFolders.DefaultRoot==@"C:\Менеджер автоматизации Барс","Renamed default bundle folder");
   Check(BundleFolders.PackageDestination(BundleFolders.DefaultRoot,"/_barsmanager/NDP48-x86-x64-AllOS-ENU.exe")==Path.Combine(BundleFolders.DefaultRoot,"_barsmanager","NDP48-x86-x64-AllOS-ENU.exe"),"Framework uses the new installer folder");
   using(var hub=new ManagerHub()){
    Check(hub.Text=="Менеджер автоматизации Барс","Launcher title matches the new product name");
    hub.Show();Application.DoEvents();var all=Children(hub).ToArray();Check(!all.OfType<Label>().Any(l=>l.Text.StartsWith("Версия ")),"No launcher version label");
    var bundle=all.OfType<BrandButton>().Single(b=>b.Text=="Скачать комплект");var guide=all.OfType<BrandButton>().Single(b=>b.Text=="Инструкция");var about=all.OfType<BrandButton>().Single(b=>b.AccessibleName=="О программе");var exit=all.OfType<BrandButton>().Single(b=>b.Text=="Закрыть программу");Check(about.Width==about.Height&&about.Height==exit.Height,"Square About button");Check(about.Left<guide.Left&&guide.Left<bundle.Left&&bundle.Left<exit.Left,"Launcher footer order");Check(Math.Abs(bundle.Width-guide.Width)<=1&&Math.Abs(guide.Width-exit.Width)<=1,"Equal text button widths");Check(hub.FormBorderStyle==FormBorderStyle.FixedSingle&&!hub.MaximizeBox,"Launcher fixed border and maximize");
    string fixture=Path.Combine(Path.GetTempPath(),"bars-downloader-navigation-"+Guid.NewGuid().ToString("N"));
    try {
     string libraries=Directory.CreateDirectory(Path.Combine(fixture,"_libraries")).FullName;
     Directory.CreateDirectory(Path.Combine(fixture,"_tools"));
     File.WriteAllText(Path.Combine(libraries,"fixture.msi"),"navigation fixture");
     BundlePreferences.UseDiscoveredRoot(BundleSection.Any,fixture);
     bundle.PerformClick();
     var wait=System.Diagnostics.Stopwatch.StartNew();
     while(hub.Visible&&wait.ElapsedMilliseconds<5000){Application.DoEvents();System.Threading.Thread.Sleep(10);}
     Check(hub.RequestedModuleType==typeof(BundleDownloadForm)&&!hub.Visible,"Launcher opens downloader after background bundle lookup");
     using(var downloader=hub.RequestedModule())
      Check(Children(downloader).OfType<BrandTextBox>().Any(field=>field.Text==fixture),"Downloader receives the matched bundle root");
    }finally{
     BundlePreferences.UseDiscoveredRoot(BundleSection.Any,null);
     if(Directory.Exists(fixture))Directory.Delete(fixture,true);
    }
   }
   using(var software=new SoftwareManagerForm(true)){Check(software.FormBorderStyle==FormBorderStyle.FixedSingle&&software.MaximizeBox,"Software fixed border and maximize");Check(!Children(software).OfType<Label>().Any(l=>l.Cursor==Cursors.Hand&&l.Text.Contains("комплект")),"No clickable software status");}
   using(var tools=new ToolsDeploymentForm(true)){Check(tools.FormBorderStyle==FormBorderStyle.Sizable&&tools.MaximizeBox,"Tools window is resizable");}
   using(var library=new LibraryManagerForm(true)){library.Show();Application.DoEvents();var all=Children(library).ToArray();Check(!all.OfType<Label>().Any(l=>l.Cursor==Cursors.Hand&&l.Text.Contains("комплект")),"No clickable library status");var journal=all.OfType<BrandButton>().Single(b=>b.Text=="Открыть журнал");var exit=all.OfType<BrandButton>().Single(b=>b.Text=="Закрыть");Check(journal.Parent==exit.Parent&&journal.Left<exit.Left,"Journal and close are in the same footer");}
   using(var result=new BundleDifferencesForm(new[]{new[]{"test.exe","Нет в папке — скачать"}})){result.Show();Application.DoEvents();Check(result.FormBorderStyle==FormBorderStyle.Sizable&&result.MaximizeBox,"Results window is resizable");Check(Children(result).OfType<ServiceGrid>().Single().Rows.Count==1,"Only supplied differences appear");}
   Console.WriteLine("PASS: "+count+" compact downloader/navigation/window policy checks. No network.");return 0;
  }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
 }
}
