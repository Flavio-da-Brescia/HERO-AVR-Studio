using System.Diagnostics;
using System.Text.Json;
using System.Windows.Forms;
using System.Drawing;
sealed class ProjectDialog : Form {
 readonly string root;
 readonly ListBox list=new(){Dock=DockStyle.Left,Width=220};
 readonly ListView history=new(){Dock=DockStyle.Fill,View=View.Details,FullRowSelect=true,MultiSelect=false};
 readonly Label status=new(){Dock=DockStyle.Bottom,Height=34};
 readonly FlowLayoutPanel bar=new(){Dock=DockStyle.Top,Height=115,WrapContents=true};
 readonly TextBox name=new(){Width=130},version=new(){Width=75,Text="1.0.0"};
 readonly RichTextBox info=new(){Dock=DockStyle.Bottom,Height=125,ReadOnly=true,Font=new Font("Consolas",9)};
 readonly ComboBox mode=new(){Width=150,DropDownStyle=ComboBoxStyle.DropDownList};
 bool busy;
 public string? OpenedPath;
 public ProjectDialog(string root){
 this.root=root;Text="HERO Project Manager";Width=1080;Height=590;MinimumSize=new Size(720,420);StartPosition=FormStartPosition.CenterParent;
 foreach(var (n,w) in new[]{("Kind",75),("Version / Build",250),("Status",85),("Created",190)})history.Columns.Add(n,w);
 Controls.Add(history);Controls.Add(list);Controls.Add(info);Controls.Add(status);Controls.Add(bar);
 void Add(string text,Func<Task> action){var b=new Button{Text=text,AutoSize=true};b.Click+=async(_,_)=>await Guard(action);bar.Controls.Add(b);}
 bar.Controls.Add(new Label{Text="Name",AutoSize=true});bar.Controls.Add(name);
 mode.Items.AddRange(new object[]{"Arduino-compatible","Native AVR C","Native AVR C++"});mode.SelectedIndex=0;bar.Controls.Add(mode);
 Add("New",async()=>{await Create(null);});
 Add("Import sketch",async()=>{using var d=new FolderBrowserDialog{Description="Select an Arduino sketch or native AVR source folder"};if(d.ShowDialog(this)==DialogResult.OK)await Create(d.SelectedPath);});
 Add("Open",()=>{OpenProject();return Task.CompletedTask;});Add("Refresh",RefreshProjects);
 Add("Build",async()=>{var j=await Call("build","-Name",Selected());ShowResult(j);await LoadHistory();});
 bar.Controls.Add(new Label{Text="Version",AutoSize=true});bar.Controls.Add(version);
 Add("Release build",async()=>{var h=SelectedRow("build");var j=await Call("release","-Name",Selected(),"-BuildId",Get(h,"id"),"-Version",version.Text);ShowResult(j);await LoadHistory();});
 Add("Rebuild release",async()=>{var h=SelectedRow("release");ShowResult(await Call("rebuild","-Name",Selected(),"-Version",Get(h,"version")));});
 Add("Restore copy",async()=>{var h=SelectedRow("release");using var d=new FolderBrowserDialog{Description="Choose a parent folder for a new restored sketch"};if(d.ShowDialog(this)==DialogResult.OK){string dest=Path.Combine(d.SelectedPath,Selected());var j=await Call("restore","-Name",Selected(),"-Version",Get(h,"version"),"-Destination",dest);ShowResult(j);}});
 Add("Configure production HEX pair",async()=>{using var d=new ProductionConfigDialog(root,Selected());HeroTheme.Apply(d);if(d.ShowDialog(this)==DialogResult.OK){status.Text="Production configuration saved";await LoadHistory();}});
 Add("Open folder",()=>{Process.Start(new ProcessStartInfo(Path.Combine(root,"projects",Selected())){UseShellExecute=true});return Task.CompletedTask;});
 list.SelectedIndexChanged+=async(_,_)=>{if(!busy)await Guard(LoadHistory);};
 history.SelectedIndexChanged+=(_,_)=>{if(history.SelectedItems.Count==1&&history.SelectedItems[0].Tag is JsonElement j)info.Text=JsonSerializer.Serialize(j,new JsonSerializerOptions{WriteIndented=true});};
 list.DoubleClick+=(_,_)=>{if(!busy&&list.SelectedItem!=null)OpenProject();};
 FormClosing+=(_,e)=>{if(busy)e.Cancel=true;};Shown+=async(_,_)=>await Guard(RefreshProjects);
 }
 static string Get(JsonElement j,string n)=>j.TryGetProperty(n,out var v)?v.ToString():"";
 string Selected()=>list.SelectedItem as string??throw new Exception("Select a project");
 JsonElement SelectedRow(string kind){if(history.SelectedItems.Count!=1||history.SelectedItems[0].Text!=kind)throw new Exception("Select a "+kind+" row");return (JsonElement)history.SelectedItems[0].Tag!;}
 async Task<JsonElement> Call(params string[] args){
 var pinfo=new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,@"WindowsPowerShell\v1.0\powershell.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=root};
 foreach(var a in new[]{"-NoProfile","-NonInteractive","-File",Path.Combine(root,"projects.ps1")}.Concat(args))pinfo.ArgumentList.Add(a);
 using var p=Process.Start(pinfo)??throw new Exception("Cannot start project backend");var o=p.StandardOutput.ReadToEndAsync();var e=p.StandardError.ReadToEndAsync();await p.WaitForExitAsync();string raw=await o;string err=await e;
 try{using var doc=JsonDocument.Parse(raw);return doc.RootElement.Clone();}catch{throw new Exception(err+" "+raw);}
 }
 static bool Ok(JsonElement j)=>j.TryGetProperty("success",out var v)&&v.ValueKind==JsonValueKind.True;
 void ShowResult(JsonElement j){info.Text=JsonSerializer.Serialize(j,new JsonSerializerOptions{WriteIndented=true});status.Text=Ok(j)?"Operation successful":"Operation failed: "+Get(j,"error");}
 async Task Guard(Func<Task> action){if(busy)return;busy=true;bar.Enabled=false;list.Enabled=false;history.Enabled=false;try{await action();}catch(Exception e){status.Text=e.Message;}finally{busy=false;bar.Enabled=true;list.Enabled=true;history.Enabled=true;}}
 async Task Create(string? source){var args=new List<string>{"create","-Name",name.Text.Trim()};if(source!=null)args.AddRange(new[]{"-Source",source});else args.AddRange(new[]{"-Mode",new[]{"Arduino","NativeC","NativeCpp"}[mode.SelectedIndex]});var j=await Call(args.ToArray());ShowResult(j);if(Ok(j)){await RefreshProjects();list.SelectedItem=name.Text.Trim();await LoadHistory();}}
 async Task RefreshProjects(){string? old=list.SelectedItem as string;var j=await Call("list");if(!Ok(j)){ShowResult(j);return;}list.Items.Clear();foreach(var p in j.GetProperty("projects").EnumerateArray())list.Items.Add(Get(p,"name"));if(old!=null&&list.Items.Contains(old))list.SelectedItem=old;else if(list.Items.Count>0)list.SelectedIndex=0;await LoadHistory();}
 async Task LoadHistory(){history.Items.Clear();if(list.SelectedItem==null)return;var j=await Call("history","-Name",Selected());if(!Ok(j)){ShowResult(j);return;}foreach(var b in j.GetProperty("builds").EnumerateArray()){var item=new ListViewItem(new[]{"build",Get(b,"id"),Ok(b.GetProperty("result"))?"OK":"Failed",Get(b,"created")}){Tag=b.Clone()};history.Items.Add(item);}foreach(var r in j.GetProperty("releases").EnumerateArray()){var item=new ListViewItem(new[]{"release",Get(r,"version"),"Archived",Get(r,"created")}){Tag=r.Clone()};history.Items.Add(item);}}
 void OpenProject(){string n=Selected();string folder=Path.Combine(root,"projects",n);using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(folder,"project.json")));OpenedPath=Path.GetFullPath(Path.Combine(folder,Get(doc.RootElement,"sketch")));if(!OpenedPath.StartsWith(folder+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||!File.Exists(OpenedPath))throw new IOException("Invalid project entry file");DialogResult=DialogResult.OK;Close();}
}
