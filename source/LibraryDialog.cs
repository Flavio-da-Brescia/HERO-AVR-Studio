using System.Text.Json;
using System.Drawing;
using System.Windows.Forms;
sealed class LibraryDialog : Form {
 readonly Func<string[],Task<JsonElement>> invoke;
 readonly string root;
 readonly ListView list=new(){Dock=DockStyle.Fill,View=View.Details,FullRowSelect=true,MultiSelect=false};
 readonly ListBox examples=new(){Dock=DockStyle.Fill};
 readonly Label status=new(){Dock=DockStyle.Bottom,Height=30,Text="Local libraries"};
 readonly FlowLayoutPanel bar=new(){Dock=DockStyle.Top,Height=44};
 [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public string? OpenedSketch {get;private set;}
 public LibraryDialog(string root,Func<string[],Task<JsonElement>> invoke){
 this.root=root;this.invoke=invoke;Text="HERO AVR Studio - Libraries";Width=900;Height=530;StartPosition=FormStartPosition.CenterParent;MinimumSize=new Size(640,380);
 foreach(var (name,width) in new[]{("Name",220),("Version",85),("Architecture",115),("Source",80)})list.Columns.Add(name,width);
 var split=new SplitContainer{Dock=DockStyle.Fill};split.Panel1.Controls.Add(list);split.Panel2.Controls.Add(examples);Controls.Add(split);Controls.Add(bar);Controls.Add(status);Shown+=(_,_)=>{if(split.Width>520)split.SplitterDistance=(int)(split.Width*.57);};
 void Add(string name,Func<Task> action){var b=new Button{Text=name,AutoSize=true,Height=32};b.Click+=async(_,_)=>await Guard(action);bar.Controls.Add(b);}
 Add("Refresh",RefreshList);Add("Import ZIP",async()=>{using var d=new OpenFileDialog{Filter="ZIP libraries (*.zip)|*.zip"};if(d.ShowDialog(this)==DialogResult.OK)await Import(d.FileName);});
 Add("Import folder",async()=>{using var d=new FolderBrowserDialog();if(d.ShowDialog(this)==DialogResult.OK)await Import(d.SelectedPath);});
 Add("Open example copy",()=>{OpenExample();return Task.CompletedTask;});
 list.SelectedIndexChanged+=(_,_)=>{examples.Items.Clear();if(list.SelectedItems.Count==1&&list.SelectedItems[0].Tag is JsonElement j&&j.TryGetProperty("examples",out var e)&&e.ValueKind==JsonValueKind.Array)foreach(var p in e.EnumerateArray())examples.Items.Add(p.GetString()!);};
 examples.DoubleClick+=(_,_)=>{try{OpenExample();}catch(Exception e){status.Text=e.Message;}};
 Shown+=async(_,_)=>await Guard(RefreshList);
 }
 static string Get(JsonElement j,string name)=>j.TryGetProperty(name,out var p)?p.ToString():"";
 static void Require(JsonElement j){if(!j.TryGetProperty("success",out var s)||s.ValueKind!=JsonValueKind.True)throw new Exception(Get(j,"error"));}
 async Task Guard(Func<Task> action){bar.Enabled=false;list.Enabled=false;examples.Enabled=false;try{await action();}catch(Exception e){status.Text=e.Message;}finally{bar.Enabled=true;list.Enabled=true;examples.Enabled=true;}}
 async Task RefreshList(){var j=await invoke(new[]{"libraries"});Require(j);list.Items.Clear();examples.Items.Clear();foreach(var lib in j.GetProperty("libraries").EnumerateArray()){var item=new ListViewItem(new[]{Get(lib,"name"),Get(lib,"version"),Get(lib,"architectures"),Get(lib,"kind")}){Tag=lib.Clone()};list.Items.Add(item);}status.Text=list.Items.Count+" libraries available";}
 async Task Import(string path){var j=await invoke(new[]{"library-import","-Source",path});Require(j);await RefreshList();status.Text="Imported "+Get(j.GetProperty("library"),"name");}
 void OpenExample(){if(examples.SelectedItem is not string src)return;string name=Path.GetFileNameWithoutExtension(src);string parent=Path.Combine(root,"workspace","sketches","example-"+Guid.NewGuid().ToString("N")[..8]);string dest=Path.Combine(parent,name);Directory.CreateDirectory(dest);string sourceDir=Path.GetDirectoryName(src)!;foreach(var f in Directory.GetFiles(sourceDir,"*",SearchOption.AllDirectories)){var target=Path.Combine(dest,Path.GetRelativePath(sourceDir,f));Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(f,target);}OpenedSketch=Path.Combine(dest,Path.GetFileName(src));DialogResult=DialogResult.OK;Close();}
}
