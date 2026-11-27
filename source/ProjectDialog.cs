using System.Diagnostics;
using System.Text.Json;
using System.Windows.Forms;
using System.Drawing;

sealed class ProjectDialog : Form {
 readonly string root; readonly ListBox list=new(){Dock=DockStyle.Fill,Font=new Font("Segoe UI",12),BorderStyle=BorderStyle.FixedSingle};
 readonly Label note=new(){Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,Tag="muted",AutoEllipsis=true};
 readonly Button open=new(){Text="Open project",Width=115,Height=29,Font=new Font("Segoe UI",9),Tag="primary"};
 readonly Button create=new(){Text="New project",Width=100,Height=29,Font=new Font("Segoe UI",9)};
 readonly Button import=new(){Text="Import",Width=78,Height=29,Font=new Font("Segoe UI",9)};
 bool busy; public string? OpenedPath;
 public ProjectDialog(string root,string? current=null){
  this.root=root;Text="Projects";ClientSize=new Size(550,420);MinimumSize=new Size(470,350);StartPosition=FormStartPosition.CenterParent;
  var grid=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(18),ColumnCount=1,RowCount=5};
  foreach(int h in new[]{42,28,-100,28,37})grid.RowStyles.Add(new RowStyle(h<0?SizeType.Percent:SizeType.Absolute,Math.Abs(h)));
  grid.Controls.Add(new Label{Text="Open a project",Dock=DockStyle.Fill,Font=new Font("Segoe UI Semibold",20),Tag="accent"},0,0);
  grid.Controls.Add(new Label{Text="Choose a workspace or create a new one.",Dock=DockStyle.Fill,Tag="muted"},0,1);
  grid.Controls.Add(list,0,2);grid.Controls.Add(note,0,3);
  var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,WrapContents=false};
  buttons.Controls.AddRange(new Control[]{open,import,create});grid.Controls.Add(buttons,0,4);Controls.Add(grid);
  list.SelectedIndexChanged+=(_,_)=>UpdateSelection();list.DoubleClick+=(_,_)=>OpenSelected();
  open.Click+=(_,_)=>OpenSelected();create.Click+=async(_,_)=>await Guard(()=>Create(null));
  import.Click+=async(_,_)=>{using var f=new FolderBrowserDialog{Description="Choose an Arduino sketch or native AVR source folder"};if(f.ShowDialog(this)==DialogResult.OK)await Guard(()=>Create(f.SelectedPath));};
  Shown+=async(_,_)=>await Guard(()=>Refresh(current));FormClosing+=(_,e)=>{if(busy)e.Cancel=true;};
  AcceptButton=open;UpdateSelection();
 }
 async Task Guard(Func<Task> fn){if(busy)return;busy=true;list.Enabled=create.Enabled=import.Enabled=open.Enabled=false;try{await fn();}catch(Exception e){note.Text=e.Message;}finally{busy=false;list.Enabled=create.Enabled=import.Enabled=true;open.Enabled=list.SelectedItem!=null;}}
 async Task Refresh(string? current){var j=await ProjectWorkspace.Call(root,"list");ProjectWorkspace.Require(j);list.Items.Clear();foreach(var p in j.GetProperty("projects").EnumerateArray())list.Items.Add(ProjectWorkspace.G(p,"name"));if(current!=null&&list.Items.Contains(current))list.SelectedItem=current;else if(list.Items.Count>0)list.SelectedIndex=0;else note.Text="No projects yet.";}
 void UpdateSelection(){open.Enabled=!busy&&list.SelectedItem!=null;if(list.SelectedItem is string n){try{note.Text=ProjectWorkspace.Diagnostics(root,n).Count+" diagnostic sketch(es)  |  "+Path.GetFileName(ProjectWorkspace.Entry(root,n));}catch(Exception e){note.Text=e.Message;}}}
 void OpenSelected(){if(busy||list.SelectedItem is not string n)return;try{OpenedPath=ProjectWorkspace.Entry(root,n);DialogResult=DialogResult.OK;Close();}catch(Exception e){note.Text=e.Message;}}
 async Task Create(string? source){
  using var d=new Form{Text=source==null?"New project":"Import project",ClientSize=new Size(420,200),StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false};
  var name=new TextBox{Dock=DockStyle.Fill,Text=source==null?"":Path.GetFileName(source)};
  var mode=new ComboBox{Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList};mode.Items.AddRange(new object[]{"Arduino-compatible","Native AVR C","Native AVR C++"});mode.SelectedIndex=0;mode.Enabled=source==null;
  var go=new Button{Text=source==null?"Create":"Import",Dock=DockStyle.Fill,Tag="primary",DialogResult=DialogResult.OK};
  var t=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(14),ColumnCount=1,RowCount=5};foreach(var h in new[]{25,35,25,35,45})t.RowStyles.Add(new RowStyle(SizeType.Absolute,h));
  t.Controls.Add(new Label{Text="Project name",Dock=DockStyle.Fill},0,0);t.Controls.Add(name,0,1);t.Controls.Add(new Label{Text="Source type",Dock=DockStyle.Fill},0,2);t.Controls.Add(mode,0,3);t.Controls.Add(go,0,4);d.Controls.Add(t);d.AcceptButton=go;HeroTheme.Apply(d);
  if(d.ShowDialog(this)!=DialogResult.OK)return;
  var a=new List<string>{"create","-Name",name.Text.Trim()};if(source!=null)a.AddRange(new[]{"-Source",source});else a.AddRange(new[]{"-Mode",new[]{"Arduino","NativeC","NativeCpp"}[mode.SelectedIndex]});
  ProjectWorkspace.Require(await ProjectWorkspace.Call(root,a.ToArray()));await Refresh(name.Text.Trim());
  note.Text="Project created. Click Open project.";
 }
}

sealed class ProjectHistoryDialog : Form {
 readonly string root,project;readonly ComboBox role=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=220};
 readonly ListView history=new(){Dock=DockStyle.Fill,View=View.Details,FullRowSelect=true,MultiSelect=false};
 readonly RichTextBox details=new(){Dock=DockStyle.Fill,ReadOnly=true,Font=new Font("Consolas",9)};
 readonly TextBox version=new(){Width=85,Text="1.0.0"};readonly Label status=new(){Dock=DockStyle.Fill,AutoEllipsis=true};
 readonly FlowLayoutPanel commands=new(){Dock=DockStyle.Fill,WrapContents=true};bool busy;
 public ProjectHistoryDialog(string root,string project,string? diagnostic=null){
  this.root=root;this.project=project;Text="Builds & releases / "+project;ClientSize=new Size(870,560);MinimumSize=new Size(700,450);StartPosition=FormStartPosition.CenterParent;
  var grid=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(12),ColumnCount=1,RowCount=5};foreach(int h in new[]{36,65,-65,-35,30})grid.RowStyles.Add(new RowStyle(h<0?SizeType.Percent:SizeType.Absolute,Math.Abs(h)));
  var title=new FlowLayoutPanel{Dock=DockStyle.Fill};title.Controls.Add(new Label{Text="Firmware",AutoSize=true,Padding=new Padding(0,7,6,0)});title.Controls.Add(role);grid.Controls.Add(title,0,0);
  role.Items.Add("Production");foreach(var d in ProjectWorkspace.Diagnostics(root,project))role.Items.Add("Diagnostic / "+d);
  role.SelectedItem=diagnostic==null?"Production":"Diagnostic / "+diagnostic;if(role.SelectedIndex<0)role.SelectedIndex=0;
  void Add(string label,Func<Task> fn){var b=new Button{Text=label,AutoSize=true,Height=26,Font=new Font("Segoe UI",9),Margin=new Padding(2)};b.Click+=async(_,_)=>await Guard(fn);commands.Controls.Add(b);}
  Add("Build",async()=>{Show(await Call("build"));await LoadHistory();});Add("Refresh",LoadHistory);
  commands.Controls.Add(new Label{Text="Version",AutoSize=true,Padding=new Padding(4,8,0,0)});commands.Controls.Add(version);
  Add("Release selected build",async()=>{Show(await Call("release","-BuildId",ProjectWorkspace.G(Row("build"),"id"),"-Version",version.Text));await LoadHistory();});
  Add("Rebuild release",async()=>Show(await Call("rebuild","-Version",ProjectWorkspace.G(Row("release"),"version"))));
  Add("Restore copy",async()=>{var row=Row("release");using var f=new FolderBrowserDialog{Description="Choose parent folder for restored source"};if(f.ShowDialog(this)==DialogResult.OK)Show(await Call("restore","-Version",ProjectWorkspace.G(row,"version"),"-Destination",Path.Combine(f.SelectedPath,Diagnostic??project)));});
  foreach(var (s,w) in new[]{("Kind",75),("Version / build",290),("Status",100),("Created",190)})history.Columns.Add(s,w);
  grid.Controls.Add(commands,0,1);grid.Controls.Add(history,0,2);grid.Controls.Add(details,0,3);grid.Controls.Add(status,0,4);Controls.Add(grid);
  role.SelectedIndexChanged+=async(_,_)=>await Guard(LoadHistory);
  history.SelectedIndexChanged+=(_,_)=>{if(history.SelectedItems.Count==1&&history.SelectedItems[0].Tag is JsonElement j)details.Text=JsonSerializer.Serialize(j,new JsonSerializerOptions{WriteIndented=true});};
  Shown+=async(_,_)=>await Guard(LoadHistory);FormClosing+=(_,e)=>{if(busy)e.Cancel=true;};
 }
 string? Diagnostic=>role.SelectedIndex>0?(role.SelectedItem as string)!.Substring("Diagnostic / ".Length):null;
 Task<JsonElement> Call(string action,params string[] extra){var a=new List<string>{action,"-Name",project};if(Diagnostic is string d)a.AddRange(new[]{"-Diagnostic",d});a.AddRange(extra);return ProjectWorkspace.Call(root,a.ToArray());}
 async Task Guard(Func<Task> f){if(busy)return;busy=true;role.Enabled=commands.Enabled=history.Enabled=false;try{await f();}catch(Exception e){status.Text=e.Message;}finally{busy=false;role.Enabled=commands.Enabled=history.Enabled=true;}}
 JsonElement Row(string kind)=>history.SelectedItems.Count==1&&history.SelectedItems[0].Text==kind?(JsonElement)history.SelectedItems[0].Tag!:throw new Exception("Select a "+kind+" row.");
 void Show(JsonElement j){details.Text=JsonSerializer.Serialize(j,new JsonSerializerOptions{WriteIndented=true});status.Text=ProjectWorkspace.Ok(j)?"Operation successful":"Operation failed: "+ProjectWorkspace.G(j,"error");}
 async Task LoadHistory(){history.Items.Clear();var j=await Call("history");ProjectWorkspace.Require(j);foreach(var b in j.GetProperty("builds").EnumerateArray())history.Items.Add(new ListViewItem(new[]{"build",ProjectWorkspace.G(b,"id"),ProjectWorkspace.Ok(b.GetProperty("result"))?"OK":"Failed",ProjectWorkspace.G(b,"created")}){Tag=b.Clone()});foreach(var r in j.GetProperty("releases").EnumerateArray())history.Items.Add(new ListViewItem(new[]{"release",ProjectWorkspace.G(r,"version"),"Archived",ProjectWorkspace.G(r,"created")}){Tag=r.Clone()});}
}
