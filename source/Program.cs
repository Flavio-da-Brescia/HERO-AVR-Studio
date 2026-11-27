using System.Diagnostics;
using System.Text.Json;
using System.Text;
using System.Drawing;
using System.Windows.Forms;

internal static class Program {
 [STAThread] static void Main(string[] args) {
  ApplicationConfiguration.Initialize();
#pragma warning disable WFO5001
  Application.SetColorMode(SystemColorMode.Dark);
#pragma warning restore WFO5001
  string root=AppContext.BaseDirectory;
  while(!File.Exists(Path.Combine(root,"hero.ps1"))) {
   var parent=Directory.GetParent(root);
   if(parent==null){MessageBox.Show("hero.ps1 not found.");return;}
   root=parent.FullName;
  }
  if(args.Length==3 && args[0]=="--firmware-inspect"){
   try{File.WriteAllText(args[2],JsonSerializer.Serialize(new{success=true,firmware=FirmwareCatalog.External(args[1])}));Environment.ExitCode=0;}catch(Exception e){File.WriteAllText(args[2],JsonSerializer.Serialize(new{success=false,error=e.Message}));Environment.ExitCode=1;}return;
  }
  if(args.Length==2 && args[0]=="--firmware-catalog"){
   try{File.WriteAllText(args[1],JsonSerializer.Serialize(new{success=true,latest=FirmwareCatalog.Latest(root),releases=FirmwareCatalog.Releases(root)}));Environment.ExitCode=0;}catch(Exception e){File.WriteAllText(args[1],JsonSerializer.Serialize(new{success=false,error=e.Message}));Environment.ExitCode=1;}return;
  }
  if(args.Length==4 && args[0]=="--plot-export"){
   try{var d=PlotData.Load(args[1]);d.Export(args[2]);File.WriteAllText(args[3],JsonSerializer.Serialize(d.Summary()));Environment.ExitCode=0;}
   catch(Exception e){File.WriteAllText(args[3],JsonSerializer.Serialize(new{success=false,error=e.Message}));Environment.ExitCode=1;}return;
  }
  if(args.Length==1 && args[0]=="--policy-selftest"){
   try{PolicySelfTest.Run(root).GetAwaiter().GetResult();Environment.ExitCode=0;}
   catch(Exception e){File.WriteAllText(Path.Combine(root,"policy-test-error.txt"),e.ToString());Environment.ExitCode=1;}return;
  }
  if(args.Length==1 && args[0]=="--isp-wizard-selftest"){
   try{IspDialog.SelfTest(root).GetAwaiter().GetResult();Environment.ExitCode=0;}
   catch(Exception e){File.WriteAllText(Path.Combine(root,"isp-wizard-test-error.txt"),e.ToString());Environment.ExitCode=1;}return;
  }
  if(args.Length==1 && args[0]=="--serial-icons-selftest"){
   try{using var monitor=new SerialMonitor(root);monitor.IconSelfTest();Environment.ExitCode=0;}
   catch(Exception e){File.WriteAllText(Path.Combine(root,"serial-icons-test-error.txt"),e.ToString());Environment.ExitCode=1;}return;
  }
  if(args.Length==2 && args[0]=="--workspace-selftest"){
   try{using var form=new Studio(root);form.WorkspaceSelfTest(args[1]);Environment.ExitCode=0;}
   catch(Exception e){File.WriteAllText(Path.Combine(root,"workspace-test-error.txt"),e.ToString());Environment.ExitCode=1;}return;
  }
  if(args.Length>0 && args[0]=="--selftest") {
   try { using var form=new Studio(root); form.AutomationTest().GetAwaiter().GetResult(); Environment.ExitCode=0; }
   catch(Exception e){File.WriteAllText(Path.Combine(root,"gui-test-error.txt"),e.ToString());Environment.ExitCode=1;}
   return;
  }
  Application.Run(new Studio(root));
 }
}
sealed class Studio : Form {
 readonly string root;
 readonly CodeEditor editor=new(){Dock=DockStyle.Fill,Font=new Font("Consolas",10),AcceptsTab=true,WordWrap=false,DetectUrls=false,BorderStyle=BorderStyle.None,BackColor=Color.FromArgb(25,30,39),ForeColor=Color.FromArgb(226,234,242)};
 readonly RichTextBox console=new(){Dock=DockStyle.Fill,ReadOnly=true,WordWrap=false,ScrollBars=RichTextBoxScrollBars.Both,Font=new Font("Consolas",9),BackColor=Color.FromArgb(17,22,29),ForeColor=Color.FromArgb(163,216,189),BorderStyle=BorderStyle.None,DetectUrls=false};
 readonly ComboBox ports=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=100};
 readonly Label status=new(){Dock=DockStyle.Fill,AutoEllipsis=true,Text="",Padding=new Padding(6,2,6,2)};
 readonly Label flashText=new(){AutoSize=true,Text="Flash —"};
 readonly Label ramText=new(){AutoSize=true,Text="SRAM —"};
 readonly HeroMemoryBar flash=new(){Maximum=30720,Width=140};
 readonly HeroMemoryBar ram=new(){Maximum=2048,Width=140};
 readonly List<Control> commands=new();
 string? file; bool dirty,busy,loading;
 readonly TreeView workspaceTree=new(){Dock=DockStyle.Fill,HideSelection=false,ShowNodeToolTips=true,FullRowSelect=true,ShowLines=false,ShowRootLines=true,Indent=15,BorderStyle=BorderStyle.None,ItemHeight=22,Font=new Font("Segoe UI",9)};
 readonly Label workspaceHeading=new(){Dock=DockStyle.Fill,AutoEllipsis=true,Padding=new Padding(6,0,0,0),TextAlign=ContentAlignment.MiddleLeft,Font=new Font("Segoe UI Semibold",10),Tag="accent"};
 readonly TableLayoutPanel workspaceLayout=new(){Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=new Padding(0,0,5,0)};
 readonly TableLayoutPanel editorHost=new(){Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};
 readonly SplitContainer main=new HeroSplitter(){Dock=DockStyle.Fill,Orientation=Orientation.Vertical,SplitterWidth=8,Margin=Padding.Empty};
 readonly Button treeToggle=new HeroFlatButton(){Text="",AutoSize=false,Dock=DockStyle.Fill,Margin=new Padding(2),AccessibleName="Hide project tree",Tag="toolbar",ImageAlign=ContentAlignment.MiddleCenter,TextImageRelation=TextImageRelation.Overlay};
 readonly ToolTip layoutTips=new();
 bool treeHidden,layoutReady,restoringLayout;
 double editorRatio=.60;
 string? activeProject; bool updatingWorkspace; readonly SerialMonitor monitor; bool refreshing,closeApproved,closing; string? selectedPort;
 const string template="void setup() {\n  \n}\n\nvoid loop() {\n  \n}\n";
 public Studio(string root) {
  this.root=root;LoadLayoutPreferences(); monitor=new SerialMonitor(root); Text="HERO AVR Studio"; Width=1450;Height=860;MinimumSize=new Size(960,620);StartPosition=FormStartPosition.CenterScreen;Font=new Font("Segoe UI",10);BackColor=Color.FromArgb(238,242,246);WindowState=FormWindowState.Maximized;
  var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=3,Padding=new Padding(8)};
  layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,139));layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
  layout.RowStyles.Add(new RowStyle(SizeType.Absolute,60));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,24));Controls.Add(layout);
  var header=new HeroHeader(root){Margin=new Padding(0,0,0,6)};layout.Controls.Add(header,0,0);layout.SetColumnSpan(header,2);
  var bar=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new Padding(3,2,3,0),Margin=new Padding(0,0,8,0)};layout.Controls.Add(bar,0,1);
  Button Add(string label,Action action,bool primary=false){var b=new HeroFlatButton{Text=label,Width=122,Height=26,Font=new Font("Segoe UI",9),FlatStyle=FlatStyle.Flat,TextAlign=ContentAlignment.MiddleLeft,BackColor=primary?Color.FromArgb(18,105,87):Color.White,ForeColor=primary?Color.White:Color.FromArgb(25,40,53),Margin=new Padding(1,1,1,1),Tag=primary?"primary":"toolbar",Padding=new Padding(4,0,0,0)};b.FlatAppearance.BorderColor=Color.FromArgb(190,201,211);b.Click+=(_,_)=>action();HeroTheme.SidebarIcon(b);bar.Controls.Add(b);commands.Add(b);return b;}
  void Section(string label){bar.Controls.Add(new Label{Text=label,Width=122,Height=22,Margin=new Padding(1,label=="SKETCH"?3:9,1,1),ForeColor=Color.FromArgb(100,115,125),Font=new Font("Segoe UI Semibold",8),Tag="accent",Padding=new Padding(3,2,0,0)});}
  Section("SKETCH");Add("New",NewSketch);Add("Open",OpenSketch);Add("Save",()=>Save());Add("Find",()=>FindReplace());
  Section("BUILD");Add("Compile",async()=>await RunBuild(false));Add("Upload",async()=>await RunBuild(true),true);
  Section("TOOLS");Add("Libraries",OpenLibraries);Add("Projects",OpenProjects);Add("ISP",async()=>await OpenHardware(()=>new IspDialog(root,ports.Items.Cast<string>().ToArray())));Add("Production",async()=>await OpenHardware(()=>new ProductionDialog(root,ports.Items.Cast<string>().ToArray(),ports.SelectedItem as string,activeProject)));Add("Diagnostics",async()=>await OpenHardware(()=>new DiagnosticsDialog(root,ports.Items.Cast<string>().ToArray())));
  main.IsSplitterFixed=false;layoutTips.SetToolTip(main,"Drag the divider to resize the editor and the entire debug / serial column.");
  layout.Controls.Add(main,1,1);
  var editorLayout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=Padding.Empty};
  editorLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,28));editorLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100));editorLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,66));editorLayout.RowCount=3;main.Panel1.Controls.Add(editorHost);
  editorHost.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,0));editorHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
  editorHost.RowStyles.Add(new RowStyle(SizeType.Percent,100));
  workspaceLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,28));workspaceLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
  workspaceLayout.Controls.Add(workspaceHeading,0,0);workspaceLayout.Controls.Add(workspaceTree,0,1);
  editorHost.Controls.Add(workspaceLayout,0,0);editorHost.Controls.Add(editorLayout,1,0);workspaceLayout.Visible=false;
  commands.Add(workspaceTree);HeroTheme.AttachTreeIcons(workspaceTree);treeToggle.Disposed+=(_,_)=>treeToggle.Image?.Dispose();Disposed+=(_,_)=>layoutTips.Dispose();
  workspaceTree.NodeMouseClick+=(_,e)=>{if(e.Button==MouseButtons.Left&&e.Node?.Tag is string tag)OpenWorkspaceNode(tag);};
  workspaceTree.KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Enter&&workspaceTree.SelectedNode?.Tag is string tag){e.Handled=true;e.SuppressKeyPress=true;OpenWorkspaceNode(tag);}};
  var editorHeading=HeroTheme.Heading("SOURCE EDITOR  /  Untitled");
  editorHeading.AutoEllipsis=true;
  var editorHeader=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};
  editorHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));editorHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,30));
  editorHeader.Controls.Add(editorHeading,0,0);editorHeader.Controls.Add(treeToggle,1,0);editorLayout.Controls.Add(editorHeader,0,0);
  treeToggle.Click+=(_,_)=>ToggleProjectTree();layoutTips.SetToolTip(treeToggle,"Show or hide the project tree (Ctrl+Shift+P)");treeToggle.Visible=false;
  var codePanel=new Panel{Dock=DockStyle.Fill,Margin=Padding.Empty,Padding=new Padding(2,3,3,3)};codePanel.Controls.Add(editor);codePanel.Controls.Add(new LineGutter(editor));editorLayout.Controls.Add(codePanel,0,1);
  var meters=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=3,Padding=new Padding(6,2,6,2),Margin=new Padding(0,5,0,0)};
  meters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,185));meters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));meters.RowStyles.Add(new RowStyle(SizeType.Absolute,20));meters.RowStyles.Add(new RowStyle(SizeType.Percent,50));meters.RowStyles.Add(new RowStyle(SizeType.Percent,50));
  var memoryHeading=HeroTheme.Heading("MEMORY USAGE");meters.Controls.Add(memoryHeading,0,0);meters.SetColumnSpan(memoryHeading,2);
  flashText.Text="Flash -";ramText.Text="SRAM -";flashText.Dock=DockStyle.Fill;ramText.Dock=DockStyle.Fill;flashText.TextAlign=ramText.TextAlign=ContentAlignment.MiddleLeft;flashText.Padding=ramText.Padding=new Padding(6,0,0,0);flash.Margin=ram.Margin=new Padding(3,0,3,0);flash.Dock=DockStyle.Fill;ram.Dock=DockStyle.Fill;meters.Controls.Add(flashText,0,1);meters.Controls.Add(flash,1,1);meters.Controls.Add(ramText,0,2);meters.Controls.Add(ram,1,2);editorLayout.Controls.Add(meters,0,2);
  var right=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=Padding.Empty};right.RowStyles.Add(new RowStyle(SizeType.Absolute,34));right.RowStyles.Add(new RowStyle(SizeType.Percent,100));main.Panel2.Controls.Add(right);
  var portbar=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,AutoScroll=true,Margin=Padding.Empty};
  portbar.Controls.Add(new Label{Text="PORT",AutoSize=true,Padding=new Padding(4,5,0,0),Font=new Font("Segoe UI Semibold",9)});portbar.Controls.Add(ports);commands.Add(ports);
  var refresh=new Button{Text="Refresh",AutoSize=true,Height=25,Font=new Font("Segoe UI",9)};refresh.Click+=async(_,_)=>await RefreshPorts();portbar.Controls.Add(refresh);commands.Add(refresh);right.Controls.Add(portbar,0,0);
  var vertical=new HeroSplitter(){Dock=DockStyle.Fill,Orientation=Orientation.Horizontal,SplitterWidth=6,Margin=Padding.Empty};right.Controls.Add(vertical,0,1);
  var debug=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=Padding.Empty};debug.RowStyles.Add(new RowStyle(SizeType.Absolute,26));debug.RowStyles.Add(new RowStyle(SizeType.Percent,100));debug.Controls.Add(HeroTheme.Heading("DEBUG / BUILD OUTPUT"),0,0);debug.Controls.Add(console,0,1);vertical.Panel1.Controls.Add(debug);vertical.Panel2.Controls.Add(monitor);
  layout.Controls.Add(status,0,2);layout.SetColumnSpan(status,2);
  editor.TextChanged+=(_,_)=>{if(!loading&&!editor.IsColoring){dirty=true;UpdateTitle();}};TextChanged+=(_,_)=>{var src=ProjectWorkspace.Locate(root,file);editorHeading.Text=(src==null?"SOURCE EDITOR":src.Diagnostic==null?"PRODUCTION":"DIAGNOSTIC")+"  /  "+(file==null?"Untitled":Path.GetFileName(file))+(dirty?" *":"");};
  ports.SelectedIndexChanged+=async(_,_)=>{if(!refreshing&&!busy)await ChangePort(ports.SelectedItem as string);};
  FormClosing+=async(_,e)=>{if(closeApproved)return;e.Cancel=true;if(busy||closing)return;if(!ConfirmDiscard())return;closing=true;SetBusy(true);try{await monitor.Shutdown();SaveLayoutPreferences();closeApproved=true;Close();}catch(Exception ex){Log(ex.Message);status.Text=ex.Message;}finally{if(!IsDisposed){closing=false;SetBusy(false);}}};
  main.SplitterMoved+=(_,_)=>{if(layoutReady&&!restoringLayout&&main.Width>0){editorRatio=(double)main.SplitterDistance/Math.Max(1,main.Width-main.SplitterWidth);SaveLayoutPreferences();}};
  Shown+=async(_,_)=>{
   ApplyOutputWidth();layoutReady=true;
   vertical.SplitterDistance=Math.Clamp((int)(vertical.Height*.40),vertical.Panel1MinSize,Math.Max(vertical.Panel1MinSize,vertical.Height-vertical.Panel2MinSize-vertical.SplitterWidth));
   if(vertical.Height>300){vertical.Panel1MinSize=80;vertical.Panel2MinSize=160;}
   editor.Highlight();await RefreshPorts();
  };
  main.SizeChanged+=(_,_)=>{if(layoutReady)ApplyOutputWidth();};
  SetDocument(null,template);HeroTheme.Apply(this);status.BackColor=HeroTheme.Background;status.ForeColor=HeroTheme.Accent;bar.BackColor=HeroTheme.Background;
 }
 async Task ChangePort(string? target){
  if(monitor.IsBusy){refreshing=true;ports.SelectedItem=selectedPort;refreshing=false;status.Text="Serial busy - retry port selection";return;}
  SetBusy(true);try{await monitor.SetTarget(target);selectedPort=target;if(target!=null)File.WriteAllText(Path.Combine(root,"preferred-port.txt"),target);if(target==null)status.Text="Select a port";else if(status.Text=="Select a port"||status.Text=="Ready")status.Text="";}catch(Exception e){Log(e.Message);status.Text=e.Message;}finally{SetBusy(false);}
 }
 async Task OpenHardware(Func<Form> create){if(busy)return;bool resume=false;SetBusy(true);try{resume=await monitor.SuspendAll();using var dlg=create();HeroTheme.Apply(dlg);dlg.ShowDialog(this);}catch(Exception e){Log(e.Message);status.Text=e.Message;}finally{if(resume)await monitor.ResumeAfterUpload();SetBusy(false);}}
 void OpenProjects(){if(busy||!ConfirmDiscard())return;using var dlg=new ProjectDialog(root,activeProject);HeroTheme.Apply(dlg);if(dlg.ShowDialog(this)==DialogResult.OK&&dlg.OpenedPath!=null)SetDocument(dlg.OpenedPath,File.ReadAllText(dlg.OpenedPath));}
 string? ManagedName()=>ProjectWorkspace.Locate(root,file)?.Project;
 void RefreshWorkspace(){
  if(updatingWorkspace)return;updatingWorkspace=true;
  try{
   var s=ProjectWorkspace.Locate(root,file);activeProject=s?.Project;
   ApplyTreeVisibility();
   if(activeProject==null){workspaceTree.Nodes.Clear();return;}
   workspaceHeading.Text=activeProject;ProjectWorkspace.Populate(workspaceTree,root,activeProject,file);
  }catch(Exception e){status.Text="Project workspace: "+e.Message;}finally{updatingWorkspace=false;}
 }
 void ApplyTreeVisibility(){
  bool show=activeProject!=null&&!treeHidden;
  workspaceLayout.Visible=show;editorHost.ColumnStyles[0].Width=show?192:0;
  treeToggle.Visible=activeProject!=null;treeToggle.Text="";string label=show?"Hide project tree":"Show project tree";
  if(treeToggle.AccessibleName!=label||treeToggle.Image==null){var old=treeToggle.Image;treeToggle.Image=HeroTheme.TreeGlyph(show?"collapse":"expand");old?.Dispose();}
  treeToggle.AccessibleName=label;layoutTips.SetToolTip(treeToggle,label+" (Ctrl+Shift+P)");
 }
 void ToggleProjectTree(){if(activeProject==null)return;treeHidden=!treeHidden;ApplyTreeVisibility();SaveLayoutPreferences();}
 string LayoutPath=>Path.Combine(root,"ui-layout.json");
 void LoadLayoutPreferences(){
  try{using var d=JsonDocument.Parse(File.ReadAllText(LayoutPath));var j=d.RootElement;
   if(j.TryGetProperty("editor_ratio",out var ratio)&&ratio.TryGetDouble(out double n)&&double.IsFinite(n))editorRatio=Math.Clamp(n,.18,.85);
   if(j.TryGetProperty("tree_hidden",out var hidden)&&hidden.ValueKind is JsonValueKind.True or JsonValueKind.False)treeHidden=hidden.GetBoolean();
  }catch{}
 }
 void SaveLayoutPreferences(){
  if(!layoutReady)return;
  try{File.WriteAllText(LayoutPath,JsonSerializer.Serialize(new{editor_ratio=editorRatio,tree_hidden=treeHidden}));}catch{}
 }
 void ApplyOutputWidth(){
  if(main.Width<500)return;restoringLayout=true;
  try{
   int usable=main.Width-main.SplitterWidth;
   // The right column is one shared panel containing BOTH consoles.
   int low=220,high=usable-265;
   if(high<low)return;
   main.Panel1MinSize=0;main.Panel2MinSize=0;
   main.SplitterDistance=Math.Clamp((int)Math.Round(usable*editorRatio),low,high);
   main.Panel1MinSize=low;main.Panel2MinSize=265;
  }finally{restoringLayout=false;}
 }
 void OpenWorkspaceNode(string tag){
  if(busy||updatingWorkspace||activeProject==null)return;
  if(string.Equals(tag,file,StringComparison.OrdinalIgnoreCase))return;
  if(tag.StartsWith("action:",StringComparison.Ordinal)&&tag!="action:new"){
   if(dirty&&tag is "action:history" or "action:configure"){
    if(MessageBox.Show("Save the current source before continuing?","Project workspace",MessageBoxButtons.OKCancel)!=DialogResult.OK||!Save()){RefreshWorkspace();return;}
   }
  }else if(!ConfirmDiscard()){RefreshWorkspace();return;}
  try{
   string n=activeProject;var current=ProjectWorkspace.Locate(root,file);
   if(tag=="action:new"){var path=ProjectWorkspace.AddDiagnostic(root,n);SetDocument(path,File.ReadAllText(path));return;}
   if(tag=="action:history"){using var d=new ProjectHistoryDialog(root,n,current?.Diagnostic);HeroTheme.Apply(d);d.ShowDialog(this);}
   else if(tag=="action:libraries"){OpenLibraries();}
   else if(tag=="action:configure"){using var d=new ProductionConfigDialog(root,n);HeroTheme.Apply(d);d.ShowDialog(this);}
   else if(tag=="action:production"){_=OpenHardware(()=>new ProductionDialog(root,ports.Items.Cast<string>().ToArray(),ports.SelectedItem as string,n,true));}
   else{
    var target=ProjectWorkspace.Locate(root,tag);if(target==null||target.Project!=n)throw new IOException("Invalid project source");
    SetDocument(tag,File.ReadAllText(tag));
   }
  }catch(Exception e){MessageBox.Show(e.Message,"Project workspace");}
  finally{RefreshWorkspace();}
 }
 async Task<JsonElement> ProjectCall(params string[] args){return await InvokeScript("projects.ps1",args);}
 void OpenLibraries(){if(busy)return;using var dlg=new LibraryDialog(root,a=>Invoke(a));HeroTheme.Apply(dlg);if(dlg.ShowDialog(this)==DialogResult.OK&&dlg.OpenedSketch!=null&&ConfirmDiscard())SetDocument(dlg.OpenedSketch,File.ReadAllText(dlg.OpenedSketch));}
 void FindReplace(){if(busy)return;using var dlg=new FindDialog(editor);HeroTheme.Apply(dlg);dlg.ShowDialog(this);}
 protected override bool ProcessCmdKey(ref Message msg,Keys keyData){
  if(!busy){
   if(keyData==(Keys.Control|Keys.Shift|Keys.P)){ToggleProjectTree();return true;}if(keyData==(Keys.Control|Keys.S)){Save();return true;}
   if(keyData==(Keys.Control|Keys.N)){NewSketch();return true;}
   if(keyData==(Keys.Control|Keys.O)){OpenSketch();return true;}
   if(keyData==(Keys.Control|Keys.F)||keyData==(Keys.Control|Keys.H)){FindReplace();return true;}
   if(keyData==(Keys.Control|Keys.B)){_=RunBuild(false);return true;}
   if(keyData==(Keys.Control|Keys.U)){_=RunBuild(true);return true;}
   if(keyData==(Keys.Control|Keys.OemCloseBrackets)){int m=editor.MatchingBracket();if(m>=0){editor.Select(m,1);editor.ScrollToCaret();}return true;}
  }
  return base.ProcessCmdKey(ref msg,keyData);
 }
 void UpdateTitle()=>Text="HERO AVR Studio — "+(file==null?"Untitled":Path.GetFileName(file))+(dirty?" *":"");
 void SetDocument(string? path,string content){loading=true;file=path;editor.Text=content;dirty=false;loading=false;UpdateTitle();RefreshWorkspace();}
 bool ConfirmDiscard(){if(!dirty)return true;var choice=MessageBox.Show("Save current sketch?","Unsaved changes",MessageBoxButtons.YesNoCancel);return choice==DialogResult.No||(choice==DialogResult.Yes&&Save());}
 void NewSketch(){if(ConfirmDiscard())SetDocument(null,template);}
 void OpenSketch(){if(!ConfirmDiscard())return;using var dlg=new OpenFileDialog{Filter="AVR sources (*.ino;*.c;*.cpp)|*.ino;*.c;*.cpp",InitialDirectory=Path.Combine(root,"workspace","sketches")};if(dlg.ShowDialog()==DialogResult.OK)try{SetDocument(dlg.FileName,File.ReadAllText(dlg.FileName));}catch(Exception e){MessageBox.Show(e.Message);}}
 bool Save(){try{
  if(file==null){using var dlg=new SaveFileDialog{Filter="Arduino-compatible sketch (*.ino)|*.ino|Native AVR C (*.c)|*.c|Native AVR C++ (*.cpp)|*.cpp",FileName="BlinkA0.ino",InitialDirectory=Path.Combine(root,"workspace","sketches")};if(dlg.ShowDialog()!=DialogResult.OK)return false;
   string name=Path.GetFileNameWithoutExtension(dlg.FileName);string dir=Path.GetDirectoryName(dlg.FileName)!;if(!string.Equals(Path.GetFileName(dir),name,StringComparison.OrdinalIgnoreCase))dir=Path.Combine(dir,name);Directory.CreateDirectory(dir);var dest=Path.Combine(dir,name+Path.GetExtension(dlg.FileName));if(File.Exists(dest)&&MessageBox.Show("Overwrite "+dest+"?","Confirm",MessageBoxButtons.YesNo)!=DialogResult.Yes)return false;file=dest;
  }
  if(ProjectWorkspace.Locate(root,file) is var source&&source!=null&&dirty&&File.Exists(file)){string projectFolder=ProjectWorkspace.Folder(root,source.Project);string folder=Path.Combine(projectFolder,"backups",Guid.NewGuid().ToString("N"));string backup=Path.Combine(folder,Path.GetRelativePath(projectFolder,file));Directory.CreateDirectory(Path.GetDirectoryName(backup)!);File.Copy(file,backup);}File.WriteAllText(file,editor.Text,new UTF8Encoding(false));dirty=false;UpdateTitle();return true;
 }catch(Exception e){MessageBox.Show(e.Message);return false;}}
 void Log(string s){
  if(string.IsNullOrWhiteSpace(s))return;
  console.SelectionStart=console.TextLength;console.SelectionLength=0;
  console.SelectionColor=s.StartsWith("[OK]")?HeroTheme.Success:s.StartsWith("[ERROR]")?HeroTheme.Danger:Color.FromArgb(176,224,207);
  console.AppendText(s+"\n");console.SelectionStart=console.TextLength;console.ScrollToCaret();
 }
 void SetBusy(bool value){busy=value;foreach(var c in commands)c.Enabled=!value;editor.ReadOnly=value;UseWaitCursor=value;}
 Task<JsonElement> Invoke(params string[] args)=>InvokeScript("hero.ps1",args);
 async Task<JsonElement> InvokeScript(string script,params string[] args){
  var info=new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,@"WindowsPowerShell\v1.0\powershell.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=root};
  foreach(var a in new[]{"-NoProfile","-NonInteractive","-ExecutionPolicy","Bypass","-File",Path.Combine(root,script)}.Concat(args))info.ArgumentList.Add(a);
  using var p=Process.Start(info)??throw new Exception("Cannot start backend");
  var output=p.StandardOutput.ReadToEndAsync();var err=p.StandardError.ReadToEndAsync();await p.WaitForExitAsync().ConfigureAwait(false);
  string raw=await output.ConfigureAwait(false);string stderr=await err.ConfigureAwait(false);
  try{using var doc=JsonDocument.Parse(raw);return doc.RootElement.Clone();}catch{throw new Exception("Backend exit "+p.ExitCode+": "+stderr+" "+raw);}
 }
 static bool Success(JsonElement j)=>j.TryGetProperty("success",out var v)&&v.ValueKind==JsonValueKind.True;
 static string Get(JsonElement j,string key)=>j.TryGetProperty(key,out var v)?v.ToString():"";
 async Task RefreshPorts(){if(busy||monitor.IsBusy)return;string? target=null;SetBusy(true);refreshing=true;try{var preferred=selectedPort;var pf=Path.Combine(root,"preferred-port.txt");if(preferred==null&&File.Exists(pf))preferred=File.ReadAllText(pf).Trim();var j=await Invoke("ports");ports.Items.Clear();foreach(var p in j.GetProperty("ports").EnumerateArray())ports.Items.Add(p.GetString()!);if(preferred!=null&&ports.Items.Contains(preferred))target=preferred;else if(ports.Items.Count==1)target=ports.Items[0] as string;ports.SelectedItem=target;if(target==null)status.Text="Select a port";}catch(Exception e){Log(e.Message);return;}finally{refreshing=false;SetBusy(false);}await ChangePort(target);}
 void ShowBuild(JsonElement j){
  string path=Get(j,"compiler_log");
  string rawOut="",rawErr="",duration="";int? flashBytes=null,ramBytes=null;
  if(File.Exists(path)){try{
   using var doc=JsonDocument.Parse(File.ReadAllText(path));var c=doc.RootElement;
   rawOut=Get(c,"compiler_out");rawErr=Get(c,"compiler_err");duration=Get(c,"duration_s");
   if(c.TryGetProperty("builder_result",out var b)&&b.TryGetProperty("executable_sections_size",out var sizes))foreach(var section in sizes.EnumerateArray()){
    int n=section.GetProperty("size").GetInt32();
    if(Get(section,"name")=="text"){flashBytes=n;flash.Value=Math.Min(n,flash.Maximum);flashText.Text=$"Flash {n:N0}/30,720 ({100.0*n/30720:F1}%)";}
    if(Get(section,"name")=="data"){ramBytes=n;ram.Value=Math.Min(n,ram.Maximum);ramText.Text=$"SRAM {n:N0}/2,048 ({100.0*n/2048:F1}%)";}
   }
  }catch(Exception e){Log("Log read: "+e.Message);}}
  console.Clear();
  Log(Success(j)?"[OK] Compilation successful":"[ERROR] Compilation failed");
  Log("Target   ATmega328P / 8 MHz");
  if(duration.Length>0)Log("Time     "+duration+" s");
  if(flashBytes.HasValue)Log($"Flash    {flashBytes:N0} bytes ({100.0*flashBytes.Value/30720:F1}%)");
  if(ramBytes.HasValue)Log($"SRAM     {ramBytes:N0} bytes ({100.0*ramBytes.Value/2048:F1}%)");
  Log(Get(j,"error"));
  Log("\n--- Compiler details ---");Log(rawOut);Log(rawErr);Log("Build: "+Get(j,"output_dir"));
  console.SelectionStart=0;console.SelectionLength=0;console.ScrollToCaret();
 }
 async Task RunBuild(bool upload){
  if(busy)return;string? port=ports.SelectedItem as string;if(upload&&port==null){MessageBox.Show("Select the target COM port.");return;}if(!Save())return;
  if(upload&&MessageBox.Show("Compile and replace application firmware on "+port+"? This is a development upload; it does not run the production diagnostic gate.","Confirm development upload",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return;
  bool resume=false;try{SetBusy(true);console.Clear();flash.Value=ram.Value=0;flashText.Text="Flash —";ramText.Text="SRAM —";status.Text="Compiling…";Log("Compiling "+file);var managed=ProjectWorkspace.Locate(root,file);JsonElement j;if(managed==null)j=await Invoke("compile","-Sketch",file!);else{var record=managed.Diagnostic==null?await ProjectCall("build","-Name",managed.Project):await ProjectCall("build","-Name",managed.Project,"-Diagnostic",managed.Diagnostic);j=record.TryGetProperty("build",out var build)?build.GetProperty("result").Clone():record;}ShowBuild(j);
   if(!Success(j)){status.Text="Compilation failed";return;}status.Text="Compilation successful";
   if(upload){if(monitor!=null&&!monitor.IsDisposed)resume=await monitor.SuspendForUpload(port!);status.Text="Uploading to "+port;var u=await Invoke("upload","-Port",port!,"-Hex",Get(j,"hex_path"),"-Sha256",Get(j,"sha256"));var log=Get(u,"stderr");if(File.Exists(log))Log(File.ReadAllText(log));Log(Get(u,"error"));bool ok=Success(u)&&Get(u,"verified")=="True";status.Text=ok?"Upload verified":"Upload failed — inspect log";Log(status.Text);}
  }catch(Exception e){Log(e.ToString());status.Text="Operation failed";}finally{if(resume&&monitor!=null&&!monitor.IsDisposed)await monitor.ResumeAfterUpload();SetBusy(false);}
 }
 public void WorkspaceSelfTest(string project){
  var toolbar=commands.OfType<Button>().Where(b=>b.Image!=null).ToArray();
  if(editor.Font.Size!=10||toolbar.Length!=11||toolbar.Any(b=>b is not HeroFlatButton||b.Width!=122||b.Height!=26||b.Font.Size!=9||b.Image!.Width!=16))throw new Exception("Compact toolbar geometry mismatch");
  treeHidden=false;
  string production=ProjectWorkspace.Entry(root,project);
  SetDocument(production,File.ReadAllText(production));
  if(activeProject!=project||editorHost.ColumnStyles[0].Width!=192||workspaceTree.Nodes.Count!=6||workspaceTree.SelectedNode?.Tag as string!=production)throw new Exception("Production workspace not populated");
  ToggleProjectTree();
  if(editorHost.ColumnStyles[0].Width!=0||activeProject!=project||file!=production||treeToggle.AccessibleName!="Show project tree"||treeToggle.Image==null||treeToggle.Text!="")throw new Exception("Hide tree changed active document");
  ToggleProjectTree();
  if(editorHost.ColumnStyles[0].Width!=192||activeProject!=project||treeToggle.AccessibleName!="Hide project tree"||workspaceTree.ShowLines||workspaceTree.ImageList?.Images.Count!=8)throw new Exception("Show tree failed");
  // Exercise shared splitter at multiple widths without starting serial.
  main.Size=new Size(1100,450);editorRatio=.45;ApplyOutputWidth();
  int first=main.SplitterDistance;
  editorRatio=.70;ApplyOutputWidth();
  if(main.IsSplitterFixed||main.SplitterDistance<=first||main.Panel2.Controls.Count!=1)throw new Exception("Shared output divider failed");
  string diagnostic=ProjectWorkspace.AddDiagnostic(root,project);
  RefreshWorkspace();
  if(ProjectWorkspace.Diagnostics(root,project).Count!=1||!File.ReadAllText(diagnostic).Contains("RESULT 01: FAIL"))throw new Exception("Diagnostic creation failed");
  OpenWorkspaceNode(diagnostic);
  if(file!=diagnostic||activeProject!=project||ProjectWorkspace.Locate(root,file)?.Diagnostic!="Diagnostic"||!editor.Text.Contains("DIAGNOSTIC COMPLETED"))throw new Exception("Diagnostic navigation failed");
  editor.AppendText("\r\n// workspace selftest");
  if(!dirty||!Save()||dirty||!File.ReadAllText(diagnostic).Contains("workspace selftest"))throw new Exception("Diagnostic save failed");
  var backups=Directory.GetFiles(Path.Combine(ProjectWorkspace.Folder(root,project),"backups"),"Diagnostic.ino",SearchOption.AllDirectories);
  if(backups.Length!=1||File.ReadAllText(backups[0]).Contains("workspace selftest"))throw new Exception("Backup failed");
  OpenWorkspaceNode(production);
  if(file!=production||activeProject!=project||ProjectWorkspace.Locate(root,file)?.Diagnostic!=null)throw new Exception("Production navigation failed");
  SetDocument(null,template);if(activeProject!=null||editorHost.ColumnStyles[0].Width!=0)throw new Exception("Loose sketch workspace failed");
  using var picker=new ProjectDialog(root,project);using var history=new ProjectHistoryDialog(root,project,"Diagnostic");
  File.WriteAllText(Path.Combine(root,"workspace-test.json"),JsonSerializer.Serialize(new{success=true,project,production,diagnostic,checks=new[]{"Production tree","Diagnostic creation (fail-safe)","Diagnostic navigation","Diagnostic save","Backup integrity","Production navigation","Loose sketch mode","Picker/history construction"}}));
 }
 public async Task AutomationTest(){var p=await Invoke("ports").ConfigureAwait(false);var j=await Invoke("selftest").ConfigureAwait(false);if(!Success(p)||!Success(j)||!File.Exists(Get(j,"hex_path"))||Get(j,"sha256").Length!=64)throw new Exception("Backend selftest failed");File.WriteAllText(Path.Combine(root,"gui-test.json"),JsonSerializer.Serialize(new{success=true,ports=p,compile=j}));}
}

