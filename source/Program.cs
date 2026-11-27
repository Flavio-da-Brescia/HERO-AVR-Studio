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
 readonly CodeEditor editor=new(){Dock=DockStyle.Fill,Font=new Font("Consolas",12),AcceptsTab=true,WordWrap=false,DetectUrls=false,BorderStyle=BorderStyle.None,BackColor=Color.FromArgb(25,30,39),ForeColor=Color.FromArgb(226,234,242)};
 readonly RichTextBox console=new(){Dock=DockStyle.Fill,ReadOnly=true,Font=new Font("Consolas",10),BackColor=Color.FromArgb(17,22,29),ForeColor=Color.FromArgb(163,216,189),BorderStyle=BorderStyle.None,DetectUrls=false};
 readonly ComboBox ports=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=100};
 readonly Label status=new(){Dock=DockStyle.Fill,AutoEllipsis=true,Text="Ready",Padding=new Padding(6)};
 readonly Label flashText=new(){AutoSize=true,Text="Flash —"};
 readonly Label ramText=new(){AutoSize=true,Text="SRAM —"};
 readonly HeroMemoryBar flash=new(){Maximum=30720,Width=140};
 readonly HeroMemoryBar ram=new(){Maximum=2048,Width=140};
 readonly List<Control> commands=new();
 string? file; bool dirty,busy,loading; readonly SerialMonitor monitor; bool refreshing,closeApproved,closing; string? selectedPort;
 const string template="void setup() {\n  \n}\n\nvoid loop() {\n  \n}\n";
 public Studio(string root) {
  this.root=root; monitor=new SerialMonitor(root); Text="HERO AVR Studio"; Width=1450;Height=860;MinimumSize=new Size(960,620);StartPosition=FormStartPosition.CenterScreen;Font=new Font("Segoe UI",10);BackColor=Color.FromArgb(238,242,246);WindowState=FormWindowState.Maximized;
  var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=3,Padding=new Padding(10)};
  layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,178));layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
  layout.RowStyles.Add(new RowStyle(SizeType.Absolute,76));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,35));Controls.Add(layout);
  var header=new HeroHeader(root){Margin=new Padding(0,0,0,8)};layout.Controls.Add(header,0,0);layout.SetColumnSpan(header,2);
  var bar=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new Padding(2,0,2,0)};layout.Controls.Add(bar,0,1);
  Button Add(string label,Action action,bool primary=false){var b=new Button{Text=label,Width=156,Height=35,FlatStyle=FlatStyle.Flat,TextAlign=ContentAlignment.MiddleLeft,BackColor=primary?Color.FromArgb(18,105,87):Color.White,ForeColor=primary?Color.White:Color.FromArgb(25,40,53),Margin=new Padding(2,3,2,3),Padding=new Padding(11,0,0,0)};b.FlatAppearance.BorderColor=Color.FromArgb(190,201,211);b.Click+=(_,_)=>action();HeroTheme.SidebarIcon(b);bar.Controls.Add(b);commands.Add(b);return b;}
  void Section(string label){bar.Controls.Add(new Label{Text=label,Width=156,Height=24,ForeColor=Color.FromArgb(100,115,125),Font=new Font("Segoe UI Semibold",9),Tag="accent",Padding=new Padding(4,4,0,0)});}
  Section("SKETCH");Add("New",NewSketch);Add("Open",OpenSketch);Add("Save",()=>Save());Add("Find",()=>FindReplace());
  Section("BUILD");Add("Compile",async()=>await RunBuild(false));Add("Upload",async()=>await RunBuild(true),true);
  Section("TOOLS");Add("Libraries",OpenLibraries);Add("Projects",OpenProjects);Add("ISP",async()=>await OpenHardware(()=>new IspDialog(root,ports.Items.Cast<string>().ToArray())));Add("Production",async()=>await OpenHardware(()=>new ProductionDialog(root,ports.Items.Cast<string>().ToArray(),ports.SelectedItem as string)));Add("Diagnostics",async()=>await OpenHardware(()=>new DiagnosticsDialog(root,ports.Items.Cast<string>().ToArray())));
  var main=new SplitContainer{Dock=DockStyle.Fill,Orientation=Orientation.Vertical,SplitterWidth=7};
  layout.Controls.Add(main,1,1);
  var editorLayout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=Padding.Empty};
  editorLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,32));editorLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100));editorLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,84));editorLayout.RowCount=3;main.Panel1.Controls.Add(editorLayout);
  var editorHeading=HeroTheme.Heading("SOURCE EDITOR  /  Untitled");editorLayout.Controls.Add(editorHeading,0,0);
  var codePanel=new Panel{Dock=DockStyle.Fill,Margin=Padding.Empty};codePanel.Controls.Add(editor);codePanel.Controls.Add(new LineGutter(editor));editorLayout.Controls.Add(codePanel,0,1);
  var meters=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=3,Padding=new Padding(8,4,8,4),Margin=new Padding(0,5,0,0)};
  meters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,215));meters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));meters.RowStyles.Add(new RowStyle(SizeType.Absolute,22));meters.RowStyles.Add(new RowStyle(SizeType.Percent,50));meters.RowStyles.Add(new RowStyle(SizeType.Percent,50));
  var memoryHeading=HeroTheme.Heading("MEMORY USAGE");meters.Controls.Add(memoryHeading,0,0);meters.SetColumnSpan(memoryHeading,2);
  flashText.Text="Flash -";ramText.Text="SRAM -";flashText.Dock=DockStyle.Fill;ramText.Dock=DockStyle.Fill;flash.Dock=DockStyle.Fill;ram.Dock=DockStyle.Fill;meters.Controls.Add(flashText,0,1);meters.Controls.Add(flash,1,1);meters.Controls.Add(ramText,0,2);meters.Controls.Add(ram,1,2);editorLayout.Controls.Add(meters,0,2);
  var right=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=Padding.Empty};right.RowStyles.Add(new RowStyle(SizeType.Absolute,40));right.RowStyles.Add(new RowStyle(SizeType.Percent,100));main.Panel2.Controls.Add(right);
  var portbar=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,AutoScroll=true,Margin=Padding.Empty};
  portbar.Controls.Add(new Label{Text="PORT",AutoSize=true,Padding=new Padding(2,8,0,0),Font=new Font("Segoe UI Semibold",10)});portbar.Controls.Add(ports);commands.Add(ports);
  var refresh=new Button{Text="Refresh",AutoSize=true,Height=30};refresh.Click+=async(_,_)=>await RefreshPorts();portbar.Controls.Add(refresh);commands.Add(refresh);right.Controls.Add(portbar,0,0);
  var vertical=new SplitContainer{Dock=DockStyle.Fill,Orientation=Orientation.Horizontal,SplitterWidth=7};right.Controls.Add(vertical,0,1);
  var debug=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=Padding.Empty};debug.RowStyles.Add(new RowStyle(SizeType.Absolute,27));debug.RowStyles.Add(new RowStyle(SizeType.Percent,100));debug.Controls.Add(HeroTheme.Heading("DEBUG / BUILD OUTPUT"),0,0);debug.Controls.Add(console,0,1);vertical.Panel1.Controls.Add(debug);vertical.Panel2.Controls.Add(monitor);
  layout.Controls.Add(status,0,2);layout.SetColumnSpan(status,2);
  editor.TextChanged+=(_,_)=>{if(!loading&&!editor.IsColoring){dirty=true;UpdateTitle();}};TextChanged+=(_,_)=>editorHeading.Text="SOURCE EDITOR  /  "+(file==null?"Untitled":Path.GetFileName(file))+(dirty?" *":"");
  ports.SelectedIndexChanged+=async(_,_)=>{if(!refreshing&&!busy)await ChangePort(ports.SelectedItem as string);};
  FormClosing+=async(_,e)=>{if(closeApproved)return;e.Cancel=true;if(busy||closing)return;if(!ConfirmDiscard())return;closing=true;SetBusy(true);try{await monitor.Shutdown();closeApproved=true;Close();}catch(Exception ex){Log(ex.Message);status.Text=ex.Message;}finally{if(!IsDisposed){closing=false;SetBusy(false);}}};
  Shown+=async(_,_)=>{main.SplitterDistance=Math.Clamp((int)(main.Width*.57),main.Panel1MinSize,Math.Max(main.Panel1MinSize,main.Width-main.Panel2MinSize-main.SplitterWidth));vertical.SplitterDistance=Math.Clamp((int)(vertical.Height*.43),vertical.Panel1MinSize,Math.Max(vertical.Panel1MinSize,vertical.Height-vertical.Panel2MinSize-vertical.SplitterWidth));if(main.Width>600){main.Panel1MinSize=250;main.Panel2MinSize=310;}if(vertical.Height>300){vertical.Panel1MinSize=80;vertical.Panel2MinSize=160;}editor.Highlight();await RefreshPorts();};
  SetDocument(null,template);HeroTheme.Apply(this);status.BackColor=HeroTheme.Background;status.ForeColor=HeroTheme.Accent;bar.BackColor=HeroTheme.Background;
 }
 async Task ChangePort(string? target){
  if(monitor.IsBusy){refreshing=true;ports.SelectedItem=selectedPort;refreshing=false;status.Text="Serial busy - retry port selection";return;}
  SetBusy(true);try{await monitor.SetTarget(target);selectedPort=target;if(target!=null)File.WriteAllText(Path.Combine(root,"preferred-port.txt"),target);status.Text=target==null?"Select a port":"Target "+target;}catch(Exception e){Log(e.Message);status.Text=e.Message;}finally{SetBusy(false);}
 }
 async Task OpenHardware(Func<Form> create){if(busy)return;bool resume=false;SetBusy(true);try{resume=await monitor.SuspendAll();using var dlg=create();HeroTheme.Apply(dlg);dlg.ShowDialog(this);}catch(Exception e){Log(e.Message);status.Text=e.Message;}finally{if(resume)await monitor.ResumeAfterUpload();SetBusy(false);}}
 void OpenProjects(){if(busy||!ConfirmDiscard())return;using var dlg=new ProjectDialog(root);HeroTheme.Apply(dlg);if(dlg.ShowDialog(this)==DialogResult.OK&&dlg.OpenedPath!=null)SetDocument(dlg.OpenedPath,File.ReadAllText(dlg.OpenedPath));}
 string? ManagedName(){if(file==null)return null;var p=Path.GetRelativePath(Path.Combine(root,"projects"),file).Split(Path.DirectorySeparatorChar);return p.Length==4&&p[1]=="src"&&p[0]==p[2]&&(p[3]==p[0]+".ino"||p[3]=="main.c"||p[3]=="main.cpp")?p[0]:null;}
 async Task<JsonElement> ProjectCall(params string[] args){return await InvokeScript("projects.ps1",args);}
 void OpenLibraries(){if(busy)return;using var dlg=new LibraryDialog(root,a=>Invoke(a));HeroTheme.Apply(dlg);if(dlg.ShowDialog(this)==DialogResult.OK&&dlg.OpenedSketch!=null&&ConfirmDiscard())SetDocument(dlg.OpenedSketch,File.ReadAllText(dlg.OpenedSketch));}
 void FindReplace(){if(busy)return;using var dlg=new FindDialog(editor);HeroTheme.Apply(dlg);dlg.ShowDialog(this);}
 protected override bool ProcessCmdKey(ref Message msg,Keys keyData){
  if(!busy){
   if(keyData==(Keys.Control|Keys.S)){Save();return true;}
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
 void SetDocument(string? path,string content){loading=true;file=path;editor.Text=content;dirty=false;loading=false;UpdateTitle();}
 bool ConfirmDiscard(){if(!dirty)return true;var choice=MessageBox.Show("Save current sketch?","Unsaved changes",MessageBoxButtons.YesNoCancel);return choice==DialogResult.No||(choice==DialogResult.Yes&&Save());}
 void NewSketch(){if(ConfirmDiscard())SetDocument(null,template);}
 void OpenSketch(){if(!ConfirmDiscard())return;using var dlg=new OpenFileDialog{Filter="AVR sources (*.ino;*.c;*.cpp)|*.ino;*.c;*.cpp",InitialDirectory=Path.Combine(root,"workspace","sketches")};if(dlg.ShowDialog()==DialogResult.OK)try{SetDocument(dlg.FileName,File.ReadAllText(dlg.FileName));}catch(Exception e){MessageBox.Show(e.Message);}}
 bool Save(){try{
  if(file==null){using var dlg=new SaveFileDialog{Filter="Arduino-compatible sketch (*.ino)|*.ino|Native AVR C (*.c)|*.c|Native AVR C++ (*.cpp)|*.cpp",FileName="BlinkA0.ino",InitialDirectory=Path.Combine(root,"workspace","sketches")};if(dlg.ShowDialog()!=DialogResult.OK)return false;
   string name=Path.GetFileNameWithoutExtension(dlg.FileName);string dir=Path.GetDirectoryName(dlg.FileName)!;if(!string.Equals(Path.GetFileName(dir),name,StringComparison.OrdinalIgnoreCase))dir=Path.Combine(dir,name);Directory.CreateDirectory(dir);var dest=Path.Combine(dir,name+Path.GetExtension(dlg.FileName));if(File.Exists(dest)&&MessageBox.Show("Overwrite "+dest+"?","Confirm",MessageBoxButtons.YesNo)!=DialogResult.Yes)return false;file=dest;
  }
  if(ManagedName()!=null&&dirty&&File.Exists(file)){string folder=Path.Combine(root,"projects",ManagedName()!,"backups",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);File.Copy(file,Path.Combine(folder,Path.GetFileName(file)));}File.WriteAllText(file,editor.Text,new UTF8Encoding(false));dirty=false;UpdateTitle();return true;
 }catch(Exception e){MessageBox.Show(e.Message);return false;}}
 void Log(string s){console.AppendText(s+"\n");console.SelectionStart=console.TextLength;console.ScrollToCaret();}
 void SetBusy(bool value){busy=value;foreach(var c in commands)c.Enabled=!value;editor.ReadOnly=value;UseWaitCursor=value;}
 Task<JsonElement> Invoke(params string[] args)=>InvokeScript("hero.ps1",args);
 async Task<JsonElement> InvokeScript(string script,params string[] args){
  var info=new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,@"WindowsPowerShell\v1.0\powershell.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=root};
  foreach(var a in new[]{"-NoProfile","-NonInteractive","-File",Path.Combine(root,script)}.Concat(args))info.ArgumentList.Add(a);
  using var p=Process.Start(info)??throw new Exception("Cannot start backend");
  var output=p.StandardOutput.ReadToEndAsync();var err=p.StandardError.ReadToEndAsync();await p.WaitForExitAsync().ConfigureAwait(false);
  string raw=await output.ConfigureAwait(false);string stderr=await err.ConfigureAwait(false);
  try{using var doc=JsonDocument.Parse(raw);return doc.RootElement.Clone();}catch{throw new Exception("Backend exit "+p.ExitCode+": "+stderr+" "+raw);}
 }
 static bool Success(JsonElement j)=>j.TryGetProperty("success",out var v)&&v.ValueKind==JsonValueKind.True;
 static string Get(JsonElement j,string key)=>j.TryGetProperty(key,out var v)?v.ToString():"";
 async Task RefreshPorts(){if(busy||monitor.IsBusy)return;string? target=null;SetBusy(true);refreshing=true;try{var preferred=selectedPort;var pf=Path.Combine(root,"preferred-port.txt");if(preferred==null&&File.Exists(pf))preferred=File.ReadAllText(pf).Trim();var j=await Invoke("ports");ports.Items.Clear();foreach(var p in j.GetProperty("ports").EnumerateArray())ports.Items.Add(p.GetString()!);if(preferred!=null&&ports.Items.Contains(preferred))target=preferred;else if(ports.Items.Count==1)target=ports.Items[0] as string;ports.SelectedItem=target;status.Text=ports.Items.Count+" ports detected";}catch(Exception e){Log(e.Message);return;}finally{refreshing=false;SetBusy(false);}await ChangePort(target);}
 void ShowBuild(JsonElement j){
  var path=Get(j,"compiler_log");if(File.Exists(path)){try{using var doc=JsonDocument.Parse(File.ReadAllText(path));var c=doc.RootElement;Log(Get(c,"compiler_out"));Log(Get(c,"compiler_err"));if(c.TryGetProperty("builder_result",out var b)&&b.TryGetProperty("executable_sections_size",out var sizes))foreach(var section in sizes.EnumerateArray()){int n=section.GetProperty("size").GetInt32();if(Get(section,"name")=="text"){flash.Value=Math.Min(n,flash.Maximum);flashText.Text=$"Flash {n:N0}/30,720 ({100.0*n/30720:F1}%)";}if(Get(section,"name")=="data"){ram.Value=Math.Min(n,ram.Maximum);ramText.Text=$"SRAM {n:N0}/2,048 ({100.0*n/2048:F1}%)";}}}catch(Exception e){Log("Log read: "+e.Message);}}
  Log(Get(j,"error"));Log("Build: "+Get(j,"output_dir"));
 }
 async Task RunBuild(bool upload){
  if(busy)return;string? port=ports.SelectedItem as string;if(upload&&port==null){MessageBox.Show("Select the target COM port.");return;}if(!Save())return;
  if(upload&&MessageBox.Show("Compile and replace application firmware on "+port+"?","Confirm upload",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return;
  bool resume=false;try{SetBusy(true);console.Clear();flash.Value=ram.Value=0;flashText.Text="Flash —";ramText.Text="SRAM —";status.Text="Compiling…";Log("Compiling "+file);var managed=ManagedName();JsonElement j;if(managed==null)j=await Invoke("compile","-Sketch",file!);else{var record=await ProjectCall("build","-Name",managed);j=record.TryGetProperty("build",out var build)?build.GetProperty("result").Clone():record;}ShowBuild(j);
   if(!Success(j)){status.Text="Compilation failed";return;}status.Text="Compilation successful";Log("[OK] Compilation successful");
   if(upload){if(monitor!=null&&!monitor.IsDisposed)resume=await monitor.SuspendForUpload(port!);status.Text="Uploading to "+port;var u=await Invoke("upload","-Port",port!,"-Hex",Get(j,"hex_path"),"-Sha256",Get(j,"sha256"));var log=Get(u,"stderr");if(File.Exists(log))Log(File.ReadAllText(log));Log(Get(u,"error"));bool ok=Success(u)&&Get(u,"verified")=="True";status.Text=ok?"Upload verified":"Upload failed — inspect log";Log(status.Text);}
  }catch(Exception e){Log(e.ToString());status.Text="Operation failed";}finally{if(resume&&monitor!=null&&!monitor.IsDisposed)await monitor.ResumeAfterUpload();SetBusy(false);}
 }
 public async Task AutomationTest(){var p=await Invoke("ports").ConfigureAwait(false);var j=await Invoke("selftest").ConfigureAwait(false);if(!Success(p)||!Success(j)||!File.Exists(Get(j,"hex_path"))||Get(j,"sha256").Length!=64)throw new Exception("Backend selftest failed");File.WriteAllText(Path.Combine(root,"gui-test.json"),JsonSerializer.Serialize(new{success=true,ports=p,compile=j}));}
}

