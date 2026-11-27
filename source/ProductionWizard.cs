using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using System.Windows.Forms;

sealed class ProductionDialog:Form {
 readonly string root;readonly string? preferredPort,preferredProject;readonly bool historyOnly;
 readonly string[] headings={"Select project","Verify firmware","Check connection","Prepare batch","Production"};
 readonly Panel[] pages=new Panel[5];readonly Label[] steps=new Label[5];
 readonly TableLayoutPanel frame=new(){Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Padding=new Padding(16)};
 readonly Panel body=new(){Dock=DockStyle.Fill};
 readonly Button back=new(){Text="Back",Width=120,Height=40},next=new(){Text="Next",Width=150,Height=40,Tag="primary"};
 readonly Label message=new(){Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,AutoEllipsis=true,Padding=new Padding(8)};
 readonly Label projectInfo=new(){Dock=DockStyle.Top,Height=88,Padding=new Padding(8)};
 readonly ListBox projectList=new(){Dock=DockStyle.Fill,Font=new Font("Segoe UI",13)};
 readonly Label diagInfo=new(){Dock=DockStyle.Fill,Padding=new Padding(12),Font=new Font("Segoe UI",11)};
 readonly Label prodInfo=new(){Dock=DockStyle.Fill,Padding=new Padding(12),Font=new Font("Segoe UI",11)};
 readonly Label pairInfo=new(){Dock=DockStyle.Fill,Padding=new Padding(12)};
 readonly ComboBox port=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=160};
 readonly Label connection=new(){Dock=DockStyle.Fill,Padding=new Padding(12),Font=new Font("Segoe UI",12)};
 readonly TextBox name=new(){Width=300},operatorName=new(){Width=240};
 readonly NumericUpDown quantity=new(){Minimum=0,Maximum=1000000,Width=140};
 readonly RadioButton newBatch=new(){Text="Create new batch",AutoSize=true,Checked=true},resumeBatch=new(){Text="Resume existing batch",AutoSize=true};
 readonly ComboBox batch=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=500};
 readonly Label batchInfo=new(){Dock=DockStyle.Fill,Padding=new Padding(12)};
 readonly Label stationInfo=new(){Dock=DockStyle.Fill,Padding=new Padding(8),Font=new Font("Segoe UI Semibold",11)};
 readonly Label processed=Counter(),passed=Counter(),failed=Counter(),yield=Counter();
 readonly Label phase=new(){Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,Font=new Font("Segoe UI",11),Padding=new Padding(8)};
 readonly Label verdict=new(){Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter,Font=new Font("Segoe UI Semibold",24),Text="READY"};
 readonly Button start=new(){Text="START TEST & PROGRAM PCB",Dock=DockStyle.Fill,Height=58,Font=new Font("Segoe UI Semibold",15),Tag="primary"};
 readonly ListView tests=new(){Dock=DockStyle.Fill,View=View.Details,FullRowSelect=true,MultiSelect=false},history=new(){Dock=DockStyle.Fill,View=View.Details,FullRowSelect=true,MultiSelect=false};
 readonly RichTextBox comments=new(){Dock=DockStyle.Fill,ReadOnly=true,Font=new Font("Consolas",10)};
 readonly List<JsonElement> projects=new(),batches=new();
 readonly System.Windows.Forms.Timer timer=new(){Interval=300};
 JsonElement pair,locked;string verifiedPort="",activeId="";int step;bool busy,running,pairVerified,readOnly;
 string? activeAttempt;string lastProgress="";
 SplitContainer? split,upper;
 public ProductionDialog(string root,string[] ports,string? preferredPort=null,string? preferredProject=null,bool historyOnly=false){
  this.root=root;this.preferredPort=preferredPort;this.preferredProject=preferredProject;this.historyOnly=historyOnly;Text="HERO Production Wizard";Width=1170;Height=780;MinimumSize=new Size(900,650);StartPosition=FormStartPosition.CenterParent;WindowState=FormWindowState.Maximized;
  frame.RowStyles.Add(new RowStyle(SizeType.Absolute,78));frame.RowStyles.Add(new RowStyle(SizeType.Percent,100));frame.RowStyles.Add(new RowStyle(SizeType.Absolute,42));frame.RowStyles.Add(new RowStyle(SizeType.Absolute,52));Controls.Add(frame);
  var strip=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=5,RowCount=1};
  for(int i=0;i<5;i++){strip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,20));steps[i]=new Label{Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter,Font=new Font("Segoe UI Semibold",11),Margin=new Padding(3),Text=(i+1)+"  "+headings[i]};strip.Controls.Add(steps[i],i,0);}
  frame.Controls.Add(strip,0,0);frame.Controls.Add(body,0,1);frame.Controls.Add(message,0,2);
  var nav=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=1};nav.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,135));nav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));nav.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,165));back.Dock=next.Dock=DockStyle.Fill;nav.Controls.Add(back,0,0);nav.Controls.Add(next,2,0);frame.Controls.Add(nav,0,3);
  for(int i=0;i<5;i++){pages[i]=new Panel{Dock=DockStyle.Fill,Visible=false,Padding=new Padding(12)};body.Controls.Add(pages[i]);}
  BuildProject();BuildFirmware();BuildConnection();BuildBatch();BuildStation();
  back.Click+=(_,_)=>{if(!busy&&step>0&&step<4)ShowStep(step-1);};
  next.Click+=async(_,_)=>await Guard(Advance);
  projectList.SelectedIndexChanged+=(_,_)=>{if(step==0)ShowProject();};
  port.SelectedIndexChanged+=(_,_)=>{verifiedPort="";UpdateConnection();};
  newBatch.CheckedChanged+=(_,_)=>UpdateBatchControls();resumeBatch.CheckedChanged+=(_,_)=>UpdateBatchControls();
  batch.SelectedIndexChanged+=(_,_)=>ShowBatchSelection();
  history.SelectedIndexChanged+=(_,_)=>{if(!running&&history.SelectedItems.Count==1&&history.SelectedItems[0].Tag is JsonElement j)ShowRecord(j);};
  timer.Tick+=(_,_)=>Poll();timer.Start();
  FormClosing+=(_,e)=>{if(busy||running)e.Cancel=true;};
  Shown+=async(_,_)=>{RefreshPorts(ports,preferredPort);ShowStep(0);await Guard(LoadProjects);if(historyOnly){await Guard(BrowseHistory);if(step!=4)Close();else next.Text="Close history";}};
 }
 static Label Counter()=>new(){Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter,Font=new Font("Segoe UI Semibold",18),Text="0"};
 static string G(JsonElement j,string k)=>j.ValueKind==JsonValueKind.Object?ProductionBackend.Get(j,k):"";
 static bool B(JsonElement j,string k)=>j.ValueKind==JsonValueKind.Object&&j.TryGetProperty(k,out var v)&&v.ValueKind==JsonValueKind.True;
 static string HashFile(string path)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
 Task<JsonElement> Call(params string[] args)=>ProductionBackend.Call(root,args);
 string Project()=>projectList.SelectedIndex>=0?G(projects[projectList.SelectedIndex],"name"):throw new Exception("Select a managed project");
 string Port()=>port.SelectedItem as string??throw new Exception("Select a COM port");
 void Say(string s,Color? c=null){message.Text=s;message.ForeColor=c??HeroTheme.Muted;}
 Button ActionButton(string text,Func<Task> f,int width=210){var b=new Button{Text=text,Width=width,Height=38};b.Click+=async(_,_)=>await Guard(f);return b;}
 TableLayoutPanel MakeLayout(Panel page,params int[] heights){
  var t=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=heights.Length};foreach(int h in heights)t.RowStyles.Add(new RowStyle(h<0?SizeType.Percent:SizeType.Absolute,h<0?-h:h));page.Controls.Add(t);return t;
 }
 Label Title(string s)=>new(){Text=s,Dock=DockStyle.Fill,Font=new Font("Segoe UI Semibold",22),Padding=new Padding(5),TextAlign=ContentAlignment.MiddleLeft,Tag="accent"};
 Label Copy(string s)=>new(){Text=s,Dock=DockStyle.Fill,Padding=new Padding(6),Font=new Font("Segoe UI",11),Tag="muted"};
 FlowLayoutPanel Flow(params Control[] c){var p=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,Padding=new Padding(4)};p.Controls.AddRange(c);return p;}
 void BuildProject(){var t=MakeLayout(pages[0],54,46,-100,90,48);t.Controls.Add(Title("01 / Select project"),0,0);t.Controls.Add(Copy("Choose the product to manufacture. Projects without a HEX pair can be configured in the next step."),0,1);t.Controls.Add(projectList,0,2);t.Controls.Add(projectInfo,0,3);t.Controls.Add(Flow(ActionButton("Refresh projects",LoadProjects),ActionButton("View batch history",BrowseHistory)),0,4);}
 void BuildFirmware(){var t=MakeLayout(pages[1],54,48,-50,-50,94,48);t.Controls.Add(Title("02 / Verify firmware"),0,0);t.Controls.Add(Copy("Review both application HEX files. Each checksum must match the saved project configuration."),0,1);t.Controls.Add(diagInfo,0,2);t.Controls.Add(prodInfo,0,3);t.Controls.Add(pairInfo,0,4);t.Controls.Add(Flow(ActionButton("Configure HEX pair",Configure),ActionButton("Verify again",LoadPair)),0,5);}
 void BuildConnection(){var t=MakeLayout(pages[2],54,52,62,-100,48);t.Controls.Add(Title("03 / Check connection"),0,0);t.Controls.Add(Copy("Connect the intended PCB to the programming station. Checking opens and closes the serial port; no firmware is written."),0,1);t.Controls.Add(Flow(new Label{Text="COM port",AutoSize=true,Padding=new Padding(4,7,0,0)},port,ActionButton("Refresh ports",RefreshAvailablePorts),ActionButton("Check port",CheckPort)),0,2);t.Controls.Add(connection,0,3);t.Controls.Add(Copy("ATmega328P / 3.3 V / 8 MHz  |  Upload: STK500v1, 57600 baud"),0,4);}
 void BuildBatch(){var t=MakeLayout(pages[3],54,52,48,58,58,58,58,-100);t.Controls.Add(Title("04 / Prepare batch"),0,0);t.Controls.Add(Copy("Create a locked batch or resume one using the exact reviewed HEX pair and COM port."),0,1);t.Controls.Add(Flow(newBatch,resumeBatch),0,2);t.Controls.Add(Flow(new Label{Text="Batch name",Width=145,Padding=new Padding(3,6,0,0)},name),0,3);t.Controls.Add(Flow(new Label{Text="Operator",Width=145,Padding=new Padding(3,6,0,0)},operatorName),0,4);t.Controls.Add(Flow(new Label{Text="Target passed PCBs",Width=145,Padding=new Padding(3,6,0,0)},quantity,new Label{Text="0 = no target; advisory, not a limit",AutoSize=true,Padding=new Padding(4,6,0,0)}),0,5);t.Controls.Add(Flow(new Label{Text="Existing batch",Width=145,Padding=new Padding(3,6,0,0)},batch),0,6);t.Controls.Add(batchInfo,0,7);}
 void BuildStation(){
  var t=MakeLayout(pages[4],36,56,54,32,-100,50,44);
  t.Controls.Add(stationInfo,0,0);var counters=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=4,RowCount=1};
  foreach(var (label,value) in new[]{("PROCESSED",processed),("PASSED",passed),("FAILED",failed),("YIELD",yield)}){int i=counters.Controls.Count;counters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,25));var c=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=2,ColumnCount=1,Margin=new Padding(4)};c.RowStyles.Add(new RowStyle(SizeType.Absolute,20));c.RowStyles.Add(new RowStyle(SizeType.Percent,100));c.Controls.Add(new Label{Text=label,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter,Tag="muted"},0,0);c.Controls.Add(value,0,1);counters.Controls.Add(c,i,0);}
  t.Controls.Add(counters,0,1);t.Controls.Add(start,0,2);t.Controls.Add(phase,0,3);
  foreach(var (n,w) in new[]{("Test",65),("Description",325),("Result",95)})tests.Columns.Add(n,w);
  foreach(var (n,w) in new[]{("Sequence",80),("Result",90),("Diagnostics",95),("Firmware",90),("Time (s)",80),("Error",430)})history.Columns.Add(n,w);
  split=new SplitContainer{Dock=DockStyle.Fill,Orientation=Orientation.Horizontal,SplitterWidth=6};upper=new SplitContainer{Dock=DockStyle.Fill,Orientation=Orientation.Vertical,SplitterWidth=6};
  upper.Panel1.Controls.Add(tests);upper.Panel2.Controls.Add(comments);split.Panel1.Controls.Add(upper);split.Panel2.Controls.Add(history);t.Controls.Add(split,0,4);
  t.Controls.Add(verdict,0,5);t.Controls.Add(Flow(ActionButton("Export report",Report),ActionButton("Open batch folder",OpenFolder)),0,6);
  start.Click+=async(_,_)=>await Guard(ProgramNext);
 }
 void ShowStep(int n){
  step=n;for(int i=0;i<5;i++){pages[i].Visible=i==step;steps[i].BackColor=i==step?Color.FromArgb(20,83,83):HeroTheme.Panel;steps[i].ForeColor=i==step?HeroTheme.Accent:i<step?HeroTheme.Success:HeroTheme.Muted;steps[i].Text=(i<step?"OK":(i+1).ToString())+"  "+headings[i];}
  pages[n].BringToFront();back.Visible=step<4;back.Enabled=!busy&&step>0;next.Text=step==4?"Finish batch":step==3?"Open station":"Next";next.Enabled=!busy;Say("");
  if(step==0)ShowProject();if(step==2)UpdateConnection();if(step==3)UpdateBatchControls();
  if(step==4&&split!=null&&upper!=null){pages[n].PerformLayout();if(split.Height>100)split.SplitterDistance=(int)(split.Height*.56);if(upper.Width>100)upper.SplitterDistance=(int)(upper.Width*.49);}
 }
 async Task Guard(Func<Task> f){
  if(busy)return;busy=true;back.Enabled=next.Enabled=false;body.Enabled=false;
  try{await f();}catch(Exception e){Say(e.Message,HeroTheme.Danger);}finally{busy=false;body.Enabled=true;back.Enabled=step>0&&step<4;next.Enabled=true;start.Enabled=!readOnly;}
 }
 async Task LoadProjects(){string old=projectList.SelectedIndex>=0?Project():preferredProject??"";var j=await Call("projects");ProductionBackend.Require(j);projects.Clear();projectList.Items.Clear();foreach(var p in j.GetProperty("projects").EnumerateArray()){projects.Add(p.Clone());projectList.Items.Add(G(p,"name")+(B(p,"configured")?"    |    HEX pair configured":"    |    Configuration required"));}int i=projects.FindIndex(p=>G(p,"name")==old);if(i<0)i=projects.FindIndex(p=>B(p,"configured"));if(projects.Count>0)projectList.SelectedIndex=Math.Max(0,i);ShowProject();}
 void ShowProject(){if(projectList.SelectedIndex<0){projectInfo.Text="No managed project available. Create one in the Projects window first.";return;}var p=projects[projectList.SelectedIndex];projectInfo.Text=Project()+"\n"+(B(p,"configured")?"Diagnostic and production HEX files are configured.":"This project requires a diagnostic HEX and a production HEX.");projectInfo.ForeColor=B(p,"configured")?HeroTheme.Success:HeroTheme.Warning;}
 async Task LoadPair(){
  pairVerified=false;pair=default;diagInfo.Text="Diagnostic HEX: not configured";prodInfo.Text="Production HEX: not configured";pairInfo.Text="Select both HEX files before continuing.";
  var j=await Call("project","-Project",Project());ProductionBackend.Require(j);if(!B(j,"configured")){Say("Configure both HEX files to proceed.",HeroTheme.Warning);return;}
  pair=j.GetProperty("configuration").Clone();string rev=G(pair,"revision");if(!System.Text.RegularExpressions.Regex.IsMatch(rev,"^[a-f0-9]{32}$"))throw new Exception("Invalid configuration revision");var dir=Path.Combine(root,"projects",Project(),"production-configs",rev);
  var d=FirmwareCatalog.External(Path.Combine(dir,"diagnostic.hex"));var p=FirmwareCatalog.External(Path.Combine(dir,"firmware.hex"));
  pairVerified=d.Sha256==G(pair,"diagnostic_sha256")&&p.Sha256==G(pair,"sha256")&&d.Sha256!=p.Sha256;
  diagInfo.Text="DIAGNOSTIC FIRMWARE\n"+G(pair,"diagnostic_name")+"   |   "+d.FlashBytes+" data bytes\nSHA-256: "+d.Sha256;
  prodInfo.Text="PRODUCTION FIRMWARE\n"+G(pair,"source_name")+"   |   "+p.FlashBytes+" data bytes\nSHA-256: "+p.Sha256;
  diagInfo.ForeColor=prodInfo.ForeColor=pairVerified?HeroTheme.Success:HeroTheme.Danger;
  pairInfo.Text="Completion: DIAGNOSTIC COMPLETED\nDiagnostic baud: "+G(pair,"diagnostic_baud")+"   |   Timeout: "+G(pair,"timeout_seconds")+" seconds";
  Say(pairVerified?"Both HEX checksums verified.":"Firmware pair checksum mismatch.",pairVerified?HeroTheme.Success:HeroTheme.Danger);
 }
 async Task Configure(){using var dlg=new ProductionConfigDialog(root,Project());HeroTheme.Apply(dlg);if(dlg.ShowDialog(this)==DialogResult.OK){await LoadProjects();await LoadPair();}}
 void RefreshPorts(IEnumerable<string> ports,string? selection){verifiedPort="";port.Items.Clear();foreach(var p in ports.Distinct().OrderBy(x=>x))port.Items.Add(p);if(selection!=null&&port.Items.Contains(selection))port.SelectedItem=selection;else if(port.Items.Count==1)port.SelectedIndex=0;UpdateConnection();}
 async Task RefreshAvailablePorts(){var j=await Call("ports");ProductionBackend.Require(j);RefreshPorts(j.GetProperty("ports").EnumerateArray().Select(x=>x.GetString()!).ToArray(),port.SelectedItem as string??preferredPort);}
 void UpdateConnection(){connection.Text="Port: "+(port.SelectedItem as string??"not selected")+"\nDiagnostic baud: "+G(pair,"diagnostic_baud")+"\n"+(verifiedPort!=""&&verifiedPort==port.SelectedItem as string?"Port opened and released successfully.":"Check the port before continuing.");connection.ForeColor=verifiedPort!=""?HeroTheme.Success:HeroTheme.Text;}
 async Task CheckPort(){verifiedPort="";UpdateConnection();string p=Port();var j=await Call("check-port","-Port",p,"-Baud",G(pair,"diagnostic_baud"));ProductionBackend.Require(j);verifiedPort=p;UpdateConnection();Say("Port available. This does not verify board identity.",HeroTheme.Success);}
 bool Matches(JsonElement b)=>G(b,"schema_version")=="2"&&G(b,"project")==Project()&&G(b,"port")==Port()&&G(b,"sha256")==G(pair,"sha256")&&G(b,"diagnostic_sha256")==G(pair,"diagnostic_sha256")&&G(b,"diagnostic_baud")==G(pair,"diagnostic_baud")&&G(b,"timeout_seconds")==G(pair,"timeout_seconds");
 async Task LoadBatches(){var j=await Call("list");ProductionBackend.Require(j);batches.Clear();batch.Items.Clear();foreach(var b in j.GetProperty("batches").EnumerateArray()){if(!Matches(b))continue;batches.Add(b.Clone());batch.Items.Add(G(b,"name")+" | "+G(b,"operator")+" | "+G(b,"created"));}if(batches.Count>0)batch.SelectedIndex=0;else newBatch.Checked=true;UpdateBatchControls();}
 void UpdateBatchControls(){bool create=newBatch.Checked;name.Enabled=operatorName.Enabled=quantity.Enabled=create;batch.Enabled=!create;resumeBatch.Enabled=batches.Count>0;ShowBatchSelection();}
 void ShowBatchSelection(){if(newBatch.Checked){batchInfo.Text="New batch: both reviewed firmware files will be copied and locked.\nTarget quantity is a goal for passed PCBs, not an automatic stop.";return;}batchInfo.Text=batch.SelectedIndex>=0?"Resume the selected locked batch. Existing results and original target are preserved.":"No compatible batch selected.";}
 async Task OpenStation(){
  string reviewed=G(pair,"revision");await LoadPair();if(!pairVerified)throw new Exception("Firmware verification failed.");if(reviewed!=G(pair,"revision")){ShowStep(1);throw new Exception("Firmware configuration changed. Review both HEX files again.");}
  if(verifiedPort!=Port())throw new Exception("Return to connection and check the selected port.");
  await CheckPort();
  if(newBatch.Checked){
   if(string.IsNullOrWhiteSpace(name.Text)||string.IsNullOrWhiteSpace(operatorName.Text))throw new Exception("Enter batch name and operator.");
   var j=await Call("create","-Project",Project(),"-Revision",G(pair,"revision"),"-Name",name.Text.Trim(),"-Operator",operatorName.Text.Trim(),"-Port",Port(),"-TargetQuantity",quantity.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));ProductionBackend.Require(j);locked=j.GetProperty("batch").Clone();
  }else{if(batch.SelectedIndex<0)throw new Exception("Select a compatible existing batch.");locked=batches[batch.SelectedIndex];if(!Matches(locked))throw new Exception("Batch settings differ from reviewed configuration.");}
  activeId=G(locked,"id");await ValidateBatch();readOnly=false;start.Text="START TEST & PROGRAM PCB";tests.Items.Clear();comments.Clear();verdict.Text="READY";verdict.ForeColor=HeroTheme.Accent;phase.Text="Connect the next PCB, then start the cycle.";await RefreshStatus();ShowStep(4);
 }
 async Task ValidateBatch(){
  var j=await Call("status","-Batch",activeId);ProductionBackend.Require(j);locked=j.GetProperty("batch").Clone();
  if(!Matches(locked))throw new Exception("Locked batch differs from the verified selection.");
  var dir=Path.Combine(root,"production",activeId);if(HashFile(Path.Combine(dir,"diagnostic.hex"))!=G(locked,"diagnostic_sha256")||HashFile(Path.Combine(dir,"firmware.hex"))!=G(locked,"sha256"))throw new Exception("Locked batch firmware checksum mismatch.");
 }
 async Task BrowseHistory(){
  var j=await Call("list");ProductionBackend.Require(j);var rows=j.GetProperty("batches").EnumerateArray().Where(x=>!historyOnly||G(x,"project")==preferredProject).Select(x=>x.Clone()).ToList();if(rows.Count==0){if(historyOnly)MessageBox.Show("No production batches yet for "+preferredProject+".","Production records");else Say("No production batches yet.");return;}
  using var dlg=new Form{Text="Production batch history (read-only)",Width=820,Height=450,StartPosition=FormStartPosition.CenterParent};
  var list=new ListBox{Dock=DockStyle.Fill};foreach(var b in rows)list.Items.Add(G(b,"name")+" | "+G(b,"project")+" | "+G(b,"port")+" | "+G(b,"created"));list.SelectedIndex=0;
  var ok=new Button{Text="View history",Dock=DockStyle.Bottom,Height=42,DialogResult=DialogResult.OK};dlg.Controls.Add(list);dlg.Controls.Add(ok);dlg.AcceptButton=ok;HeroTheme.Apply(dlg);
  if(dlg.ShowDialog(this)!=DialogResult.OK||list.SelectedIndex<0)return;
  locked=rows[list.SelectedIndex];activeId=G(locked,"id");readOnly=true;tests.Items.Clear();comments.Clear();verdict.Text="HISTORY";verdict.ForeColor=HeroTheme.Muted;phase.Text="Read-only historical batch. No programming.";await RefreshStatus();ShowStep(4);next.Text="Return to wizard";start.Text="READ-ONLY HISTORY";
 }
 async Task Advance(){
  if(step==0){_ =Project();ShowStep(1);await LoadPair();}
  else if(step==1){await LoadPair();if(!pairVerified)throw new Exception("Select and verify both HEX files first.");ShowStep(2);}
  else if(step==2){await CheckPort();ShowStep(3);await LoadBatches();}
  else if(step==3)await OpenStation();
  else{if(historyOnly){busy=false;Close();return;}activeId="";locked=default;readOnly=false;tests.Items.Clear();comments.Clear();history.Items.Clear();verifiedPort="";ShowStep(0);await LoadProjects();}
 }
 async Task RefreshStatus(){var j=await Call("status","-Batch",activeId);ProductionBackend.Require(j);locked=j.GetProperty("batch").Clone();var s=j.GetProperty("statistics");stationInfo.Text=G(locked,"project")+" / "+G(locked,"name")+"   |   "+G(locked,"port")+"   |   Operator: "+G(locked,"operator")+(readOnly?"   |   HISTORY":"");
  processed.Text=G(s,"attempts");passed.Text=G(s,"passed");failed.Text=G(s,"failed");yield.Text=G(s,"yield_percent")+"%";passed.ForeColor=HeroTheme.Success;failed.ForeColor=HeroTheme.Danger;
  if(int.TryParse(G(locked,"target_quantity"),out int goal)&&goal>0){stationInfo.Text+="   |   Goal: "+G(s,"passed")+"/"+goal;if(int.TryParse(G(s,"passed"),out int done)&&done>=goal)stationInfo.Text+=" (reached)";}
  if(G(s,"unknown")!="0")stationInfo.Text+="   |   Unknown: "+G(s,"unknown");
  history.BeginUpdate();history.Items.Clear();foreach(var r in j.GetProperty("records").EnumerateArray()){string state=G(r,"status");history.Items.Add(new ListViewItem(new[]{G(r,"sequence"),state,G(r,"diagnostic_passed"),G(r,"production_verified"),G(r,"duration_s"),G(r,"error")}){Tag=r.Clone(),ForeColor=state=="passed"?HeroTheme.Success:state=="failed"?HeroTheme.Danger:HeroTheme.Warning});}history.EndUpdate();
 }
 void ShowTests(JsonElement d){tests.BeginUpdate();tests.Items.Clear();var log=new System.Text.StringBuilder();if(d.TryGetProperty("tests",out var rows))foreach(var t in rows.EnumerateArray()){string state=G(t,"result");tests.Items.Add(new ListViewItem(new[]{G(t,"id"),G(t,"description"),state}){ForeColor=state=="PASS"?HeroTheme.Success:state=="FAIL"?HeroTheme.Danger:HeroTheme.Warning});log.AppendLine("TEST "+G(t,"id")+": "+G(t,"description"));if(t.TryGetProperty("comments",out var c))foreach(var l in c.EnumerateArray())log.AppendLine(l.ToString());log.AppendLine("RESULT "+G(t,"id")+": "+state).AppendLine();}tests.EndUpdate();if(B(d,"complete"))log.AppendLine("DIAGNOSTIC COMPLETED");if(d.TryGetProperty("errors",out var errors))foreach(var e in errors.EnumerateArray())log.AppendLine("ERROR: "+e);comments.Text=log.ToString();}
 void ShowRecord(JsonElement r){tests.Items.Clear();comments.Clear();if(r.TryGetProperty("diagnostics",out var d)&&d.ValueKind==JsonValueKind.Object)ShowTests(d);string state=G(r,"status");verdict.Text=state=="passed"?"PCB PASSED":state=="failed"?"PCB FAILED":"UNKNOWN";verdict.ForeColor=state=="passed"?HeroTheme.Success:state=="failed"?HeroTheme.Danger:HeroTheme.Warning;phase.Text="Attempt "+G(r,"sequence")+" | Diagnostic upload: "+(G(r,"diagnostic_upload_id").Length>0?"verified":"not verified")+" | Diagnostics: "+(B(r,"diagnostic_passed")?"PASS":"not passed")+" | Production: "+(B(r,"production_verified")?"verified":"not verified")+" | "+G(r,"error");}
 void Poll(){if(!running||activeAttempt==null)return;try{string f=Path.Combine(activeAttempt,"progress.json");if(!File.Exists(f))return;string raw=File.ReadAllText(f);if(raw==lastProgress)return;lastProgress=raw;using var doc=JsonDocument.Parse(raw);var j=doc.RootElement;phase.Text=G(j,"message");if(j.TryGetProperty("diagnostics",out var d)&&d.ValueKind==JsonValueKind.Object)ShowTests(d);string stage=G(j,"phase");verdict.Text=stage=="upload-diagnostic"?"UPLOADING DIAGNOSTIC":stage=="diagnostics"?"TESTING":stage=="upload-production"?"UPLOADING PRODUCTION":"FINALIZING";}catch{}}
 async Task ProgramNext(){
  if(readOnly||activeId.Length==0)throw new Exception("No programmable batch selected.");
  // The station start button is the operator authorization for this cycle.
  var attempts=Path.Combine(root,"production",activeId,"attempts");int seq=Directory.GetDirectories(attempts).Select(x=>int.TryParse(Path.GetFileName(x),out int n)?n:0).DefaultIfEmpty().Max()+1;activeAttempt=Path.Combine(attempts,seq.ToString("D8"));lastProgress="";tests.Items.Clear();comments.Clear();running=true;verdict.Text="RUNNING";verdict.ForeColor=HeroTheme.Accent;phase.Text="Uploading diagnostic firmware...";
  try{var j=await Call("program","-Batch",activeId,"-Confirm","PROGRAM-BOARD");Poll();if(j.TryGetProperty("record",out var r)){ShowRecord(r);Say(ProductionBackend.Ok(j)?"Firmware verified. Connect the next PCB.":"Attempt failed. Review the test results.",ProductionBackend.Ok(j)?HeroTheme.Success:HeroTheme.Danger);}else{verdict.Text="NOT PROGRAMMED";verdict.ForeColor=HeroTheme.Danger;phase.Text=G(j,"error");}await RefreshStatus();}finally{running=false;activeAttempt=null;}
 }
 async Task Report(){var j=await Call("report","-Batch",activeId);ProductionBackend.Require(j);Process.Start(new ProcessStartInfo(Path.GetDirectoryName(G(j,"csv"))!){UseShellExecute=true});}
 Task OpenFolder(){Process.Start(new ProcessStartInfo(Path.Combine(root,"production",activeId)){UseShellExecute=true});return Task.CompletedTask;}
 protected override void Dispose(bool disposing){if(disposing)timer.Dispose();base.Dispose(disposing);}
}
