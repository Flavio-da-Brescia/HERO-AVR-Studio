using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Forms;

sealed class IspDialog : Form {
 readonly string root;
 readonly Func<string[],Task<JsonElement>> backend;
 readonly ComboBox programmer=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=180};
 readonly ComboBox port=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=100};
 readonly ComboBox board=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=275};
 readonly CheckBox helperConsent=new(){Text="This is the helper Arduino, not my target PCB.",AutoSize=true};
 readonly CheckBox wiring=new(){Text="Wiring, ground and voltage levels checked.",AutoSize=true};
 readonly CheckBox clock=new(){Text="External 8 MHz clock physically confirmed.",AutoSize=true};
 readonly CheckBox consent=new(){Text="I accept the target flash erase and possible EEPROM erase.",AutoSize=true};
 readonly Label[] steps=new Label[5]; readonly Panel[] pages=new Panel[5];
 readonly string[] names={"Choose programmer","Prepare programmer","Connect target","Detect chip","Upload bootloader"};
 readonly string[] boardIds={"uno","nano-new","nano-old","pro8"};
 readonly Panel body=new(){Dock=DockStyle.Fill};
 readonly Label message=new(){Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,AutoEllipsis=true};
 readonly Label elapsed=new(){Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleRight,Tag="muted"};
 readonly Label connection=new(){Dock=DockStyle.Fill,Font=new Font("Segoe UI",10),Padding=new Padding(4)};
 readonly Label detected=new(){Dock=DockStyle.Fill,Font=new Font("Segoe UI",10),Padding=new Padding(4)};
 readonly Label review=new(){Dock=DockStyle.Fill,Font=new Font("Segoe UI",10),Padding=new Padding(4)};
 readonly RichTextBox preparation=new(){Dock=DockStyle.Fill,ReadOnly=true,WordWrap=true,Font=new Font("Consolas",9),BorderStyle=BorderStyle.None};
 readonly RichTextBox result=new(){Dock=DockStyle.Fill,ReadOnly=true,WordWrap=true,Font=new Font("Consolas",9),BorderStyle=BorderStyle.None};
 readonly Button back=new(){Text="Back",Dock=DockStyle.Fill}, next=new(){Text="Next",Dock=DockStyle.Fill,Tag="primary"}, logs=new(){Text="Open logs",Dock=DockStyle.Fill}, cancel=new(){Text="Cancel",Dock=DockStyle.Fill};
 readonly Button prepare=new(){Text="Upload ArduinoISP sketch",Width=220,Height=32,Tag="primary"};
 readonly Button detect=new(){Text="Detect / read fuses",Width=180,Height=32,Tag="primary"};
 readonly IspIllustration illustration=new(){Dock=DockStyle.Fill,Margin=new Padding(10,5,2,5)};
 readonly System.Windows.Forms.Timer timer=new(){Interval=200};
 readonly Stopwatch watch=new();
 int step; bool busy,identified,finished,attempted,prepared,closeRequested;
 string verifiedConnection="",preparedConnection="",bootHash="",lastLog="",activeAction="",progressFile="",operationLog="";
 CancellationTokenSource? cancellation;
 JsonElement identification;
 bool Arduino=>programmer.SelectedIndex==1;
 string BootPath=>Path.Combine(root,"compatibility","arduino-avr","bootloaders","atmega","ATmegaBOOT_168_atmega328_pro_8MHz.hex");
 public IspDialog(string root,string[] ports):this(root,ports,null){}
 internal IspDialog(string root,string[] ports,Func<string[],Task<JsonElement>>? testBackend){
  this.root=root;backend=testBackend??Call;
  Text="HERO ISP | Bootloader tutorial";ClientSize=new Size(1050,610);MinimumSize=new Size(940,570);StartPosition=FormStartPosition.CenterParent;Font=new Font("Segoe UI",10);
  var frame=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Padding=new Padding(14)};
  foreach(int h in new[]{48,-100,34,40})frame.RowStyles.Add(new RowStyle(h<0?SizeType.Percent:SizeType.Absolute,Math.Abs(h)));
  var strip=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=5,RowCount=1,Margin=Padding.Empty};
  for(int i=0;i<5;i++){strip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,20));steps[i]=new Label{Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter,Font=new Font("Segoe UI Semibold",10),Margin=new Padding(2)};strip.Controls.Add(steps[i],i,0);}
  var content=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,57));content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,43));content.Controls.Add(body,0,0);content.Controls.Add(illustration,1,0);
  var footer=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,155));footer.Controls.Add(message,0,0);footer.Controls.Add(elapsed,1,0);
  frame.Controls.Add(strip,0,0);frame.Controls.Add(content,0,1);frame.Controls.Add(footer,0,2);
  var nav=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=5,RowCount=1,Margin=Padding.Empty};foreach(int w in new[]{90,-100,105,100,180})nav.ColumnStyles.Add(new ColumnStyle(w<0?SizeType.Percent:SizeType.Absolute,Math.Abs(w)));
  nav.Controls.Add(back,0,0);nav.Controls.Add(logs,2,0);nav.Controls.Add(cancel,3,0);nav.Controls.Add(next,4,0);frame.Controls.Add(nav,0,3);Controls.Add(frame);
  for(int i=0;i<5;i++){pages[i]=new Panel{Dock=DockStyle.Fill,Padding=new Padding(6),Visible=false};body.Controls.Add(pages[i]);}
  programmer.Items.AddRange(new object[]{"USBasp","Arduino as ISP"});programmer.SelectedIndex=0;port.Items.AddRange(ports);
  board.Items.AddRange(new object[]{"Uno R3 / ATmega328P 16 MHz","Nano 16 MHz / new bootloader","Nano 16 MHz / old bootloader","Pro Mini 3.3 V / 8 MHz"});board.SelectedIndex=0;
  var refresh=new Button{Text="Refresh",AutoSize=true,Height=28};refresh.Click+=(_,_)=>{var previous=port.SelectedItem as string;port.Items.Clear();port.Items.AddRange(AvailablePorts());if(previous!=null&&port.Items.Contains(previous))port.SelectedItem=previous;};
  var p=Page(0,42,66,44,-100);
  p.Controls.Add(Title("01 / Choose programmer"),0,0);p.Controls.Add(Copy("A blank AVR needs a physical ISP programmer. The target's USB-to-UART connector alone cannot install its bootloader."),0,1);
  p.Controls.Add(Flow(programmer),0,2);
  p.Controls.Add(Copy("USBasp: a dedicated USB programmer. Install the appropriate Windows USB driver if needed.\n\nArduino as ISP: use a separate ATmega328P Arduino with a working UART bootloader. The next page uploads the ArduinoISP sketch automatically.\n\nTarget profile: ATmega328P / 3.3 V / external 8 MHz."),0,3);
  p=Page(1,42,75,44,44,36,42,-100);
  p.Controls.Add(Title("02 / Prepare programmer"),0,0);
  p.Controls.Add(Copy("Connect only the helper Arduino to your PC. Disconnect the target and remove any helper auto-reset suppression capacitor before uploading. This replaces the helper's application."),0,1);
  p.Controls.Add(Flow(board),0,2);p.Controls.Add(Flow(new Label{Text="Helper COM port",AutoSize=true,Padding=new Padding(0,6,0,0)},port,refresh),0,3);
  p.Controls.Add(helperConsent,0,4);p.Controls.Add(Flow(prepare),0,5);p.Controls.Add(preparation,0,6);
  p=Page(2,42,65,-100,36,36);
  p.Controls.Add(Title("03 / Connect the target"),0,0);
  p.Controls.Add(Copy("Power off before wiring. Use compatible logic levels and a shared ground. Never connect a 5 V output directly to an unprotected 3.3 V target or tie competing supplies."),0,1);
  p.Controls.Add(connection,0,2);p.Controls.Add(wiring,0,3);p.Controls.Add(clock,0,4);
  p=Page(3,42,64,-100,42);
  p.Controls.Add(Title("04 / Detect chip"),0,0);
  p.Controls.Add(Copy("Read the target signature, fuses and lock bits. No target memory is written. Detection times out after 10 seconds of AVRDUDE communication; cancel at any time."),0,1);
  p.Controls.Add(detected,0,2);p.Controls.Add(Flow(detect),0,3);
  p=Page(4,42,110,40,-100);
  p.Controls.Add(Title("05 / Upload bootloader"),0,0);p.Controls.Add(review,0,1);p.Controls.Add(consent,0,2);p.Controls.Add(result,0,3);
  programmer.SelectedIndexChanged+=(_,_)=>InvalidatePreparation();port.SelectedIndexChanged+=(_,_)=>InvalidatePreparation();board.SelectedIndexChanged+=(_,_)=>InvalidatePreparation();
  wiring.CheckedChanged+=(_,_)=>InvalidateDetection();clock.CheckedChanged+=(_,_)=>InvalidateDetection();consent.CheckedChanged+=(_,_)=>UpdateNav();helperConsent.CheckedChanged+=(_,_)=>UpdateNav();
  prepare.Click+=async(_,_)=>await Guard("prepare",Prepare);detect.Click+=async(_,_)=>await Guard("detect",Detect);
  back.Click+=(_,_)=>{if(!busy&&!finished&&step>0){if(attempted){attempted=false;InvalidateDetection();ShowStep(3);}else ShowStep(step==2&&!Arduino?0:step-1);}};
  next.Click+=async(_,_)=>await Guard(step==4&&!finished?"bootloader":"navigation",Advance);
  logs.Click+=(_,_)=>{string folder=Directory.Exists(lastLog)?lastLog:Path.Combine(root,"isp-logs");if(Directory.Exists(folder))Process.Start(new ProcessStartInfo(folder){UseShellExecute=true});};
  cancel.Click+=(_,_)=>{if(busy){if(activeAction is "detect" or "prepare")cancellation?.Cancel();}else Close();};
  FormClosing+=(_,e)=>{if(busy){e.Cancel=true;if(activeAction is "detect" or "prepare"){closeRequested=true;cancellation?.Cancel();}}};
  timer.Tick+=(_,_)=>{if(!busy)return;string phase="";try{if(File.Exists(progressFile))phase=File.ReadAllText(progressFile);}catch{}elapsed.Text=$"{watch.Elapsed.TotalSeconds:F0} s"+(activeAction=="detect"?" / 10 s communication":"");if(phase.Length>0)message.Text=phase;};
  timer.Start();Disposed+=(_,_)=>{timer.Dispose();cancellation?.Dispose();};
  Shown+=(_,_)=>ShowStep(0);ShowStep(0);
 }
 TableLayoutPanel Page(int index,params int[] heights){var t=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=heights.Length,Margin=Padding.Empty};foreach(int h in heights)t.RowStyles.Add(new RowStyle(h<0?SizeType.Percent:SizeType.Absolute,Math.Abs(h)));pages[index].Controls.Add(t);return t;}
 static Label Title(string text)=>new(){Text=text,Dock=DockStyle.Fill,Font=new Font("Segoe UI Semibold",18),Tag="accent",TextAlign=ContentAlignment.MiddleLeft};
 static Label Copy(string text)=>new(){Text=text,Dock=DockStyle.Fill,Padding=new Padding(4),Tag="muted"};
 static FlowLayoutPanel Flow(params Control[] children){var p=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};p.Controls.AddRange(children);return p;}
 static string[] AvailablePorts(){using var key=Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");return key==null?Array.Empty<string>():key.GetValueNames().Select(n=>key.GetValue(n) as string).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n=>n).ToArray();}
 string ConnectionKey()=>!Arduino?"usbasp":"arduinoisp|"+(port.SelectedItem as string??"")+"|"+boardIds[board.SelectedIndex];
 bool ValidConnection()=>!Arduino||port.SelectedItem is string;
 bool Prepared()=>!Arduino||(prepared&&preparedConnection==ConnectionKey());
 bool PhysicalReady()=>ValidConnection()&&Prepared()&&wiring.Checked&&clock.Checked;
 bool ReadyToBurn()=>identified&&PhysicalReady()&&verifiedConnection==ConnectionKey()&&bootHash.Length==64&&consent.Checked&&!attempted&&!finished;
 void InvalidatePreparation(){prepared=false;preparedConnection="";helperConsent.Checked=false;wiring.Checked=false;clock.Checked=false;preparation.Text="Select a helper board and its COM port.";InvalidateDetection();}
 void InvalidateDetection(){identified=false;verifiedConnection="";consent.Checked=false;detected.Text="Target not checked.";UpdateNav();}
 void Say(string text,Color? color=null){message.Text=text;message.ForeColor=color??HeroTheme.Muted;}
 void ShowStep(int n){
  step=n;for(int i=0;i<5;i++){pages[i].Visible=i==n;steps[i].Text=(i+1)+"  "+names[i]+(i==1&&!Arduino?" (skip)":"");steps[i].BackColor=i==n?Color.FromArgb(20,83,83):HeroTheme.Panel;steps[i].ForeColor=i==1&&!Arduino?HeroTheme.Muted:i==n?HeroTheme.Accent:i<n?HeroTheme.Success:HeroTheme.Muted;}
  pages[n].BringToFront();
  if(n==2)connection.Text=Arduino?"Helper Arduino / "+port.Text+"\n\nD10 → RESET\nD11 → MOSI (PB3)\nD12 ← MISO (PB4)\nD13 → SCK (PB5)\nGND ↔ GND\n\nUse level shifting for 5 V helpers. Power the 3.3 V target safely. For Uno/Nano if auto-reset interferes, add 10 µF between helper RESET and GND AFTER ArduinoISP upload (positive to RESET).":"USBasp / 6-pin ISP header\n\n1 MISO   2 VCC/reference\n3 SCK     4 MOSI\n5 RESET  6 GND\n\nCheck pin 1 orientation on your adapter. Verify actual 3.3 V signal compatibility; jumper may only change supply voltage.";
  if(n==4){consent.Checked=false;attempted=false;result.Text="Ready. The backend re-detects the target and backs up flash and EEPROM before any erase.\n";bootHash=File.Exists(BootPath)?Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(BootPath))):"";review.Text="ATmega328P / "+(!Arduino?"USBasp":"Arduino as ISP / "+port.Text)+"\nATmegaBOOT 8 MHz | UART 57600 baud\nFuses: FF / DA / FD  |  Lock: 0F\nExisting application will be erased.";if(bootHash.Length!=64)Say("Bundled bootloader missing.",HeroTheme.Danger);}
  else Say("");
  UpdateNav();
 }
 void UpdateNav(){
  port.Enabled=!busy&&Arduino;body.Enabled=!busy;back.Enabled=!busy&&step>0&&!finished;logs.Enabled=!busy;
  cancel.Enabled=!busy||activeAction is "detect" or "prepare";cancel.Text=busy?"Cancel operation":"Cancel";
  prepare.Enabled=!busy&&Arduino&&ValidConnection()&&helperConsent.Checked;detect.Enabled=!busy&&PhysicalReady();
  next.Text=finished?"Finish":step==4?"Upload bootloader":"Next";
  next.Enabled=!busy&&(finished||step switch{0=>true,1=>Prepared(),2=>PhysicalReady(),3=>identified&&PhysicalReady()&&verifiedConnection==ConnectionKey(),4=>ReadyToBurn(),_=>false});
  illustration.Stage=step;illustration.Arduino=Arduino;illustration.Passed=step==1?Prepared():step==3?identified:step==4&&finished;illustration.Invalidate();
 }
 async Task Guard(string action,Func<Task> f){
  if(busy)return;busy=true;activeAction=action;watch.Restart();elapsed.Text="";progressFile="";cancellation=new CancellationTokenSource();UpdateNav();
  try{await f();}catch(OperationCanceledException){if(action=="prepare"){InvalidatePreparation();preparation.Text="Cancelled. Helper firmware may be incomplete; upload again before proceeding.";}else{InvalidateDetection();detected.Text="Detection cancelled. No target memory written.";}Say("Operation cancelled.",HeroTheme.Warning);}
  catch(Exception e){Say(e.Message,HeroTheme.Danger);if(action=="prepare"){prepared=false;preparation.AppendText("\nERROR: "+e.Message);}if(action=="detect"){identified=false;detected.Text="Detection failed.\n"+e.Message;}if(action=="bootloader"){identified=false;result.AppendText("\nERROR: "+e.Message+"\nInspect logs and target state before retrying.");}}
  finally{watch.Stop();busy=false;cancellation.Dispose();cancellation=null;activeAction="";UpdateNav();if(closeRequested){closeRequested=false;Close();}}
 }
 async Task Advance(){if(finished){busy=false;Close();return;}switch(step){case 0:ShowStep(Arduino?1:2);break;case 1:if(Prepared())ShowStep(2);break;case 2:if(PhysicalReady())ShowStep(3);break;case 3:if(identified&&PhysicalReady()&&verifiedConnection==ConnectionKey())ShowStep(4);break;case 4:if(ReadyToBurn())await Burn();break;}}
 string[] Args(string action,params string[] extra){var a=new List<string>{action,"-Programmer",Arduino?"arduinoisp":"usbasp"};if(Arduino)a.AddRange(new[]{"-Port",port.SelectedItem as string??throw new Exception("Select the helper COM port.")});a.AddRange(extra);return a.ToArray();}
 static string G(JsonElement j,string k)=>j.ValueKind==JsonValueKind.Object&&j.TryGetProperty(k,out var v)?v.ToString():"";
 static bool OK(JsonElement j)=>j.ValueKind==JsonValueKind.Object&&j.TryGetProperty("success",out var v)&&v.ValueKind==JsonValueKind.True;
 static bool Verified(JsonElement j)=>j.TryGetProperty("verified",out var v)&&v.ValueKind==JsonValueKind.True;
 async Task Prepare(){
  if(!Arduino||!ValidConnection()||!helperConsent.Checked)throw new Exception("Confirm helper board and port.");
  prepared=false;InvalidateDetection();preparation.Text="Compiling ArduinoISP with selected helper clock; then uploading and verifying...\n";Say("Preparing helper Arduino...");
  var j=await backend(new[]{"prepare","-Board",boardIds[board.SelectedIndex],"-Port",port.Text,"-Confirm","UPLOAD-HELPER"});lastLog=G(j,"output_dir");
  if(!OK(j)||!Verified(j))throw new Exception(G(j,"error"));
  prepared=true;preparedConnection=ConnectionKey();preparation.Text="PASS: ArduinoISP compiled, uploaded and verified.\nHelper protocol: STK500v1 at 19200 baud.\n\nNow connect the target. Logs: "+lastLog;Say("Programmer ready.",HeroTheme.Success);
 }
 async Task Detect(){
  if(!PhysicalReady())throw new Exception("Complete preparation and physical checks.");
  identified=false;detected.Text="Reading signature and fuses...";Say("Detecting target...");
  var j=await backend(Args("detect"));lastLog=G(j,"output_dir");
  if(!OK(j)||G(j,"signature")!="1E950F"){string detail=G(j,"error");throw new Exception(detail.Length>0?detail:"Wrong target signature; expected 1E950F.");}
  identification=j.GetProperty("fuses");verifiedConnection=ConnectionKey();identified=true;
  detected.Text="PASS / ATmega328P detected\n\nSignature: "+G(j,"signature")+"\nLow fuse: "+G(identification,"lfuse")+"\nHigh fuse: "+G(identification,"hfuse")+"\nExtended fuse: "+G(identification,"efuse")+"\nLock bits: "+G(identification,"lock");Say("Target identified. No memory written.",HeroTheme.Success);
 }
 async Task Burn(){
  if(!ReadyToBurn())throw new Exception("Complete the preceding checks.");
  if(!File.Exists(BootPath)||Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(BootPath)))!=bootHash)throw new Exception("Bootloader changed. Review again.");
  attempted=true;result.Text="Programming... do not disconnect power or ISP.\n";Say("Backing up, erasing, setting fuses and programming...");
  var j=await backend(Args("bootloader","-ExternalClockConfirmed","-Confirm","ERASE-AND-BURN"));lastLog=G(j,"output_dir");
  result.Clear();if(j.TryGetProperty("steps",out var a)&&a.ValueKind==JsonValueKind.Array)foreach(var s in a.EnumerateArray())result.AppendText((G(s,"exit_code")=="0"?"[OK] ":"[FAIL] ")+G(s,"step")+"\n");
  result.AppendText("\nLogs: "+lastLog+"\n");
  if(!OK(j)||!Verified(j)){identified=false;result.AppendText("\nFAILED: "+G(j,"error")+"\nInspect logs and target before returning to detection.");Say("Bootloader programming not verified.",HeroTheme.Danger);return;}
  finished=true;consent.Enabled=false;result.AppendText("\nBOOTLOADER UPLOADED AND VERIFIED\nFuses and lock bits read back successfully.\n");Say("Bootloader uploaded and verified.",HeroTheme.Success);
 }
 internal Task<JsonElement> PolicyProbe()=>Call(new[]{"detect","-Programmer","arduinoisp","-Port","INVALID"});
 async Task<JsonElement> Call(string[] args){
  bool prep=args[0]=="prepare";string action=args[0];string folder=Path.Combine(root,"isp-logs","wizard-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);operationLog=folder;lastLog=folder;progressFile=Path.Combine(folder,"progress.txt");
  var start=new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,@"WindowsPowerShell\v1.0\powershell.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=root};
  foreach(var a in new[]{"-NoProfile","-NonInteractive","-ExecutionPolicy","Bypass","-File",Path.Combine(root,prep?"prepare-isp.ps1":"isp.ps1")}.Concat(prep?args.Skip(1):args).Concat(new[]{"-ProgressPath",progressFile}))start.ArgumentList.Add(a);
  using var proc=Process.Start(start)??throw new Exception("Cannot launch ISP backend");
  var o=proc.StandardOutput.ReadToEndAsync();var e=proc.StandardError.ReadToEndAsync();
  using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation?.Token??CancellationToken.None);
  // Read-only detection also has an outer watchdog for a hung PowerShell process.
  if(action=="detect")timeout.CancelAfter(TimeSpan.FromSeconds(15));else if(prep)timeout.CancelAfter(TimeSpan.FromMinutes(4));
  try{await proc.WaitForExitAsync(timeout.Token);}
  catch(OperationCanceledException){try{if(!proc.HasExited)proc.Kill(true);await proc.WaitForExitAsync();}catch{}File.WriteAllText(Path.Combine(folder,"cancelled.txt"),"Operation cancelled or watchdog expired: "+action+"\n"+DateTimeOffset.Now);if(cancellation?.IsCancellationRequested==true)throw;throw new TimeoutException(action=="detect"?"Detection watchdog expired. Check programmer and connection.":"Helper preparation timed out. Retry after checking helper firmware.");}
  string raw=await o;string err=await e;File.WriteAllText(Path.Combine(folder,"result.json"),raw);File.WriteAllText(Path.Combine(folder,"stderr.txt"),err);
  try{using var json=JsonDocument.Parse(raw);return json.RootElement.Clone();}catch{throw new Exception("ISP backend exit "+proc.ExitCode+": "+err+" "+raw);}
 }
 internal static async Task SelfTest(string root){
  var calls=new List<string[]>();bool failDetect=false;
  Task<JsonElement> Fake(string[] args){calls.Add(args);using var j=JsonDocument.Parse(args[0]=="detect"?(failDetect?"{\"success\":false,\"error\":\"timeout\"}":"{\"success\":true,\"signature\":\"1E950F\",\"fuses\":{\"lfuse\":\"62\",\"hfuse\":\"D9\",\"efuse\":\"FF\",\"lock\":\"3F\"}}"):"{\"success\":true,\"verified\":true,\"steps\":[{\"step\":\"backup\",\"exit_code\":0},{\"step\":\"bootloader\",\"exit_code\":0},{\"step\":\"readback\",\"exit_code\":0}]}");return Task.FromResult(j.RootElement.Clone());}
  using var w=new IspDialog(root,new[]{"COM99"},Fake);
  await w.Advance();if(w.step!=2||w.PhysicalReady())throw new Exception("USBasp skip failed");
  w.wiring.Checked=w.clock.Checked=true;await w.Advance();if(w.step!=3)throw new Exception("USBasp detection navigation failed");
  await w.Advance();if(w.step!=3||calls.Count!=0)throw new Exception("Detection gate failed");
  failDetect=true;await w.Guard("detect",w.Detect);if(w.identified)throw new Exception("Failed detection accepted");
  failDetect=false;await w.Detect();await w.Advance();if(w.step!=4||w.ReadyToBurn())throw new Exception("Erase consent gate failed");
  w.consent.Checked=true;await w.Advance();if(!w.finished||calls[^1][0]!="bootloader")throw new Exception("USBasp burn simulation failed");
  calls.Clear();using var a=new IspDialog(root,new[]{"COM99"},Fake);a.programmer.SelectedIndex=1;await a.Advance();if(a.step!=1)throw new Exception("Arduino preparation step missing");
  await a.Advance();if(a.step!=1)throw new Exception("Unprepared helper accepted");
  a.port.SelectedIndex=0;a.helperConsent.Checked=true;await a.Prepare();if(!a.Prepared()||calls[0][0]!="prepare")throw new Exception("Prepare failed");
  await a.Advance();if(a.step!=2)throw new Exception("Preparation navigation failed");a.wiring.Checked=a.clock.Checked=true;await a.Advance();await a.Detect();await a.Advance();a.consent.Checked=true;await a.Advance();
  if(!a.finished||calls.Count!=3||!calls[2].Contains("ERASE-AND-BURN")||!calls[2].Contains("-ExternalClockConfirmed"))throw new Exception("Arduino burn simulation failed");
  using var b=new Bitmap(420,320);a.illustration.Size=b.Size;for(int i=0;i<5;i++){a.illustration.Stage=i;a.illustration.DrawToBitmap(b,new Rectangle(Point.Empty,b.Size));}
 }
}
