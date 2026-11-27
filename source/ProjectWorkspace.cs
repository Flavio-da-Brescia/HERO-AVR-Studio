using System.Diagnostics;
using System.Text.Json;
using System.Windows.Forms;

static class ProjectWorkspace {
 internal record Source(string Project,string? Diagnostic,string Path);
 public static string G(JsonElement j,string k)=>j.TryGetProperty(k,out var v)?v.ToString():"";
 public static bool Ok(JsonElement j)=>j.TryGetProperty("success",out var v)&&v.ValueKind==JsonValueKind.True;
 public static void Require(JsonElement j){if(!Ok(j))throw new IOException(G(j,"error"));}
 public static async Task<JsonElement> Call(string root,params string[] args){
  var pinfo=new ProcessStartInfo(System.IO.Path.Combine(Environment.SystemDirectory,@"WindowsPowerShell\v1.0\powershell.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=root};
  foreach(var a in new[]{"-NoProfile","-NonInteractive","-ExecutionPolicy","Bypass","-File",Path.Combine(root,"projects.ps1")}.Concat(args))pinfo.ArgumentList.Add(a);
  using var p=Process.Start(pinfo)??throw new IOException("Cannot start project engine");var o=p.StandardOutput.ReadToEndAsync();var e=p.StandardError.ReadToEndAsync();await p.WaitForExitAsync();string raw=await o;string err=await e;using var j=JsonDocument.Parse(raw);return j.RootElement.Clone();
 }
 static bool Valid(string n)=>System.Text.RegularExpressions.Regex.IsMatch(n,@"^[A-Za-z][A-Za-z0-9_]{0,47}$")&&!System.Text.RegularExpressions.Regex.IsMatch(n,@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
 public static string Folder(string root,string n){if(!Valid(n))throw new IOException("Invalid project name");return Path.Combine(root,"projects",n);}
 static bool Within(string path,string folder)=>Path.GetFullPath(path).StartsWith(Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
 public static string Entry(string root,string n){var folder=Folder(root,n);using var j=JsonDocument.Parse(File.ReadAllText(Path.Combine(folder,"project.json")));var p=Path.GetFullPath(Path.Combine(folder,G(j.RootElement,"sketch")));if(!Within(p,Path.Combine(folder,"src",n))||!File.Exists(p))throw new IOException("Invalid project entry");return p;}
 public static List<string> Diagnostics(string root,string n){
  string dir=Path.Combine(Folder(root,n),"diagnostic-src");if(!Directory.Exists(dir))return new();
  return Directory.GetDirectories(dir).Where(p=>!File.GetAttributes(p).HasFlag(FileAttributes.ReparsePoint)).Select(Path.GetFileName).OfType<string>().Where(x=>Valid(x)&&File.Exists(Path.Combine(dir,x,x+".ino"))).OrderBy(x=>x).ToList();
 }
 static bool SourceFile(string p)=>new[]{".ino",".c",".cpp",".cc",".cxx",".h",".hpp",".S",".s"}.Contains(Path.GetExtension(p),StringComparer.OrdinalIgnoreCase);
 public static Source? Locate(string root,string? path){
  if(path==null||!Within(path,Path.Combine(root,"projects")))return null;
  var rel=Path.GetRelativePath(Path.Combine(root,"projects"),path).Split(Path.DirectorySeparatorChar);
  if(rel.Length<4||!Valid(rel[0])||!SourceFile(path)||!File.Exists(Path.Combine(Folder(root,rel[0]),"project.json")))return null;
  if(rel[1]=="src"&&rel[2]==rel[0])return new(rel[0],null,path);
  if(rel[1]=="diagnostic-src"&&Diagnostics(root,rel[0]).Contains(rel[2]))return new(rel[0],rel[2],path);
  return null;
 }
 public static string AddDiagnostic(string root,string project){
  const string name="Diagnostic";var dir=Path.Combine(Folder(root,project),"diagnostic-src",name);
  if(Directory.Exists(dir))throw new IOException("Diagnostic already exists; no overwrite");
  Directory.CreateDirectory(dir);string path=Path.Combine(dir,name+".ino");
  File.WriteAllText(path,"void setup() {\r\n  Serial.begin(38400);\r\n  delay(1500);\r\n  Serial.println(F(\"TEST 01: Implement actual PCB test\"));\r\n  // Replace with a real measured hardware test.\r\n  Serial.println(F(\"RESULT 01: FAIL\"));\r\n  Serial.println(F(\"DIAGNOSTIC COMPLETED\"));\r\n  Serial.flush();\r\n}\r\nvoid loop() {}\r\n",new System.Text.UTF8Encoding(false));return path;
 }
 public static void Populate(TreeView tree,string root,string project,string? selected){
  tree.BeginUpdate();try{
   tree.Nodes.Clear();
   var prod=tree.Nodes.Add("Production firmware");SetIcon(prod,"firmware");var entry=Entry(root,project);
   AddFiles(prod,Path.GetDirectoryName(entry)!,entry);
   var diag=tree.Nodes.Add("Diagnostic firmware");SetIcon(diag,"diagnostic");
   foreach(string n in Diagnostics(root,project)){var folder=Path.Combine(Folder(root,project),"diagnostic-src",n);var group=diag.Nodes.Add(n);SetIcon(group,"folder");AddFiles(group,folder,Path.Combine(folder,n+".ino"));group.Expand();}
   if(diag.Nodes.Count==0)diag.Nodes.Add(new TreeNode("Add diagnostic..."){Tag="action:new",ImageKey="add",SelectedImageKey="add"});
   prod.Expand();diag.Expand();
   tree.Nodes.Add(new TreeNode("Builds & releases"){Tag="action:history",ImageKey="history",SelectedImageKey="history"});
   tree.Nodes.Add(new TreeNode("Libraries"){Tag="action:libraries",ImageKey="folder",SelectedImageKey="folder"});
   tree.Nodes.Add(new TreeNode("Production setup"){Tag="action:configure",ImageKey="setup",SelectedImageKey="setup"});
   tree.Nodes.Add(new TreeNode("Production records"){Tag="action:production",ImageKey="records",SelectedImageKey="records"});
   foreach(TreeNode node in tree.Nodes)SelectMatching(node);
   void SelectMatching(TreeNode n){if(n.Tag is string p&&selected!=null&&string.Equals(p,selected,StringComparison.OrdinalIgnoreCase))tree.SelectedNode=n;foreach(TreeNode c in n.Nodes)SelectMatching(c);}
  }finally{tree.EndUpdate();}
 }
 static void SetIcon(TreeNode node,string key){node.ImageKey=key;node.SelectedImageKey=key;}
 static void AddFiles(TreeNode parent,string folder,string entry){
  var opts=new EnumerationOptions{RecurseSubdirectories=true,AttributesToSkip=FileAttributes.ReparsePoint|FileAttributes.System,IgnoreInaccessible=false};
  var paths=Directory.EnumerateFiles(folder,"*",opts).Where(SourceFile).OrderBy(p=>string.Equals(p,entry,StringComparison.OrdinalIgnoreCase)?0:1).ThenBy(p=>p);
  foreach(var path in paths){var n=new TreeNode(Path.GetRelativePath(folder,path)){Tag=path,ToolTipText=path,ImageKey="file",SelectedImageKey="file"};parent.Nodes.Add(n);}
 }
}
