using System;
using System.IO;
using System.Threading;
using System.Linq;
namespace ServiceTool
{
 internal static class BundleTests
 {
  static int count;
  static void Check(bool ok,string message){count++;if(!ok)throw new Exception(message);}
  [STAThread] static int Main()
  {
   string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"bundle-fixtures-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
   try
   {
    foreach(string bad in new[]{"/../escape.exe","/a/../../escape.exe","/a:stream","/CON.exe","/a./b","/a//b","relative.exe","/LPT1.txt"})
    {bool denied=false;try{YandexBundleSource.LocalPath(root,bad);}catch(InvalidDataException){denied=true;}Check(denied,"Unsafe path accepted: "+bad);}
    string local=BundleFolders.PackageDestination(root,"/Microsoft .NET/test.exe");Check(local.StartsWith(root,StringComparison.OrdinalIgnoreCase),"Path containment");Directory.CreateDirectory(Path.GetDirectoryName(local));File.WriteAllText(local,"fixture only");
    string hash=YandexBundleSource.Hash(local,CancellationToken.None);Check(hash.Length==64,"SHA256");
    var file=new BundleRemoteFile {Path="/Microsoft .NET/test.exe",Sha256=hash,Size=new FileInfo(local).Length};var receipts=new BundleReceipts(root);
    Check(!receipts.CanReplace(file,hash),"Unknown file must not be replaceable");receipts.Record(file);Check(receipts.CanReplace(file,hash),"Unchanged managed installer");
    Check(BundlePublication.Matches(root,file,CancellationToken.None),"Legacy cloud path checks the published library copy");
    var canonical=new BundleRemoteFile {Path="/_libraries"+file.Path,Sha256=hash,Size=file.Size};
    Check(BundlePublication.Matches(root,canonical,CancellationToken.None)&&receipts.CanReplace(canonical,hash),"Canonical and legacy cloud paths share destination and receipt");
    Check(!receipts.CanReplace(file,new string('0',64)),"Edited managed installer protected");
    var config=new BundleRemoteFile {Path="/_tools/test.ini",Sha256=hash};receipts.Record(config);Check(!receipts.CanReplace(config,hash),"Configuration protected");
    var utility=new BundleRemoteFile {Path="/_tools/tool.exe",Sha256=hash};receipts.Record(utility);Check(!receipts.CanReplace(utility,hash),"Utility tree protected");
    var revised = new BundleRemoteFile { Path=file.Path, Sha256=new string('a',64), Size=file.Size };
    Check(BundleTransfer.PartPath(root,file)!=BundleTransfer.PartPath(root,revised),"Part keyed by remote content at any path length");
    string deep = Path.Combine(root, new string('d', Math.Max(8, 153 - root.Length)));
    string deepPart = BundleTransfer.PartPath(deep, new BundleRemoteFile { Path="/fixture.exe", Sha256=hash, Size=file.Size });
    Directory.CreateDirectory(Path.GetDirectoryName(deepPart));
    File.Copy(local, deepPart);
    BundlePackageIdentity.Read(deepPart, "/fixture.exe");
    Check(File.Exists(deepPart) && deepPart.Length < 230,"Checking metadata retains content in a long directory");
    var legacyFile = new BundleRemoteFile { Path="/legacy.zip", Sha256=hash, Size=file.Size };
    string legacyRoot = Path.Combine(root, new string('l', Math.Max(8, 156 - root.Length)));
    string legacyRelative = "/.bars-download/" + hash + "/legacy.zip.part";
    string legacyPart = YandexBundleSource.LocalPath(legacyRoot, legacyRelative);
    if (legacyPart.Length > 230 && legacyPart.Length < 260)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(legacyPart));
        File.Copy(local, legacyPart);
        new BundleTransfer(new YandexBundleSource()).Download(legacyFile, legacyRoot, null, _ => { }, CancellationToken.None);
        Check(!File.Exists(legacyPart) && File.Exists(BundleFolders.PackageDestination(legacyRoot, legacyFile.Path)),
            "Existing partial download migrates without network");
    }
    var discoveredRoot = Directory.CreateDirectory(Path.Combine(root, "adjacent")).FullName;
    for (int n = 0; n < 65; n++) Directory.CreateDirectory(Path.Combine(discoveredRoot, "folder-" + n.ToString("D3")));
    string foundAfterOldLimit = Directory.EnumerateDirectories(discoveredRoot).Skip(64).First();
    Directory.CreateDirectory(Path.Combine(foundAfterOldLimit, "_tools"));
    File.WriteAllText(Path.Combine(foundAfterOldLimit, "_tools", "fixture.zip"), "fixture");
    Check(BundleDiscovery.Find(BundleSection.Tools,null,null,null,new[] { discoveredRoot },CancellationToken.None)
        .Any(candidate => candidate.Root == foundAfterOldLimit), "Finds a bundle beyond the previous 64-folder limit");
    var same=new BundleRemoteFile {Path="/another.exe",Sha256=hash,Size=file.Size};
    Check(BundleTransfer.PartPath(root,file)!=BundleTransfer.PartPath(root,same),"Identical content independent parts");
    string part=BundleTransfer.PartPath(root,same);Directory.CreateDirectory(Path.GetDirectoryName(part));File.Copy(local,part);
    new BundleTransfer(new YandexBundleSource()).Download(same,root,null,bytes=>{},CancellationToken.None);
    Check(File.Exists(BundleFolders.PackageDestination(root,same.Path)),"Verified part published without network");
    var conflict=new BundleRemoteFile {Path="/conflict.exe",Sha256=hash,Size=file.Size};
    string target=BundleFolders.PackageDestination(root,conflict.Path);File.WriteAllText(target,"user changed");
    string conflictPart=BundleTransfer.PartPath(root,conflict);Directory.CreateDirectory(Path.GetDirectoryName(conflictPart));File.Copy(local,conflictPart);
    bool blocked=false;try{new BundleTransfer(new YandexBundleSource()).Download(conflict,root,hash,bytes=>{},CancellationToken.None);}catch(IOException){blocked=true;}
    Check(blocked&&File.ReadAllText(target)=="user changed","Destination conflict preserves original");
    Check(File.Exists(conflictPart),"Conflict preserves verified replacement");
    bool appeared = false;
    try { BundlePublication.Publish(conflictPart, target, null, CancellationToken.None, null); }
    catch(IOException) { appeared = true; }
    Check(appeared && File.ReadAllText(target) == "user changed" && File.Exists(conflictPart),
        "Publication preserves a file created after the queue snapshot");
    var versions = new[] { "Bars.Administrator_v2.12.2.168.exe", "Bars.Administrator_v2.12.1.166.exe" };
    Check(BarsPackagesForm.ApplyFolders(versions.Take(1), false).Single() == "Administrator", "Single installer retains its folder");
    bool duplicatePrograms = false, duplicateServices = false;
    try { BarsPackagesForm.ApplyFolders(versions, false); } catch (IOException) { duplicatePrograms = true; }
    try { BarsPackagesForm.ApplyFolders(new[] { "Agent.zip", "agent.rar" }, true); } catch (IOException) { duplicateServices = true; }
    Check(duplicatePrograms && duplicateServices, "Install and extract plans reject shared target folders");
    using(var cancel=new CancellationTokenSource()){cancel.Cancel();bool cancelled=false;try{new BundleTransfer(new YandexBundleSource()).Download(file,root,hash,bytes=>{},cancel.Token);}catch(OperationCanceledException){cancelled=true;}Check(cancelled,"Cancelled before network");}
    using(var form=new BundleDownloadForm()){form.Show();System.Windows.Forms.Application.DoEvents();Check(form.Visible,"Window opens without network");}
    BundleSession.Save(root,new[]{file,same});var saved=BundleSession.Load(root);Check(saved.Contains(file.Path+"|"+hash)&&saved.Count==2,"Queue persists content identity");
    BundleSession.Save(root,new BundleRemoteFile[0]);Check(BundleSession.Load(root).Count==0,"Completed queue cleared atomically");
    string owned=BundleTransfer.PartPath(root,new BundleRemoteFile {Path="/cleanup.bin",Sha256=hash});Directory.CreateDirectory(Path.GetDirectoryName(owned));File.WriteAllText(owned,"partial");
    string unrelated=Path.Combine(root,".bars-download","keep.txt");File.WriteAllText(unrelated,"user");
    Check(BundleMaintenance.Parts(root).Contains(owned),"Owned partial found");BundleMaintenance.CleanParts(root);
    Check(!File.Exists(owned)&&File.Exists(unrelated),"Cleanup preserves unknown service files");
    Check(receipts.CanReplace(file,hash),"Cleanup preserves receipts");
    var msi=new BundleRemoteFile {Path="/fixture.msi",Sha256=hash};receipts.Record(msi);Check(receipts.CanReplace(msi,hash),"Managed MSI eligible for metadata validation");
    bool unknownDenied=false;try{BundlePackageIdentity.ValidateUpgrade(local,local,file.Path);}catch(IOException){unknownDenied=true;}Check(unknownDenied,"Unknown installer identity cannot authorize replacement");
    Console.WriteLine("PASS: "+count+" bundle path/receipt/UI checks. Temporary fixtures only; no network/installers.");return 0;
   }
   catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
  }
 }
}
