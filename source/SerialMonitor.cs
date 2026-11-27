using System.Text;
using System.Text.Json;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
sealed class SerialMonitor : UserControl {
 readonly string root;
 string? port;
 readonly ComboBox baud=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=84};
 readonly ComboBox ending=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=68};
 readonly TextBox input=new(){Dock=DockStyle.Fill,PlaceholderText="Type to send (Enter)"},search=new(){Width=100};
 readonly RichTextBox output=new(){Dock=DockStyle.Fill,ReadOnly=true,Font=new Font("Consolas",10),BackColor=Color.FromArgb(17,22,29),ForeColor=Color.FromArgb(163,216,189),WordWrap=false,BorderStyle=BorderStyle.None,DetectUrls=false};
 readonly CheckBox hex=new(){Text="HEX",AutoSize=true},timestamps=new(){Text="Time",Checked=true,AutoSize=true};
 readonly Label status=new(){Dock=DockStyle.Fill,Text="Select a port",TextAlign=ContentAlignment.MiddleLeft,AutoEllipsis=true};
 readonly System.Windows.Forms.Timer timer=new(){Interval=250};
 readonly FlowLayoutPanel tools=new(){Dock=DockStyle.Fill,WrapContents=true,AutoScroll=false};
 readonly Button toggle=new(){Text="Connect",AutoSize=true,Height=28};
 PlotWindow? plot; string? session,log; long offset; bool busy,suspended,wantConnected,stopping; string pending=""; DateTime nextRetry=DateTime.MinValue;
 public bool IsBusy=>busy||suspended;
 public string? ConnectedPort=>session!=null?port:null;
 public SerialMonitor(string root){
 this.root=root;Dock=DockStyle.Fill;
 baud.Items.AddRange(new object[]{"9600","19200","38400","57600","115200","230400","250000","500000","1000000"});baud.SelectedItem="38400";
 ending.Items.AddRange(new object[]{"None","LF","CR","CR+LF"});ending.SelectedItem="LF";
 var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Margin=Padding.Empty};
 layout.RowStyles.Add(new RowStyle(SizeType.Absolute,29));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,72));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,34));Controls.Add(layout);
 var heading=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,Margin=Padding.Empty};heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,105));heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
 heading.Controls.Add(new Label{Text="SERIAL",Tag="accent",Font=new Font("Segoe UI Semibold",10),Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},0,0);heading.Controls.Add(status,1,0);layout.Controls.Add(heading,0,0);
 layout.Controls.Add(tools,0,1);layout.Controls.Add(output,0,2);
 void Add(string name,Func<Task> f){var b=new Button{Text=name,AutoSize=true,Height=28,Margin=new Padding(2)};b.Click+=async(_,_)=>await Guard(f);tools.Controls.Add(b);}
 tools.Controls.Add(baud);tools.Controls.Add(toggle);toggle.Click+=async(_,_)=>await Guard(async()=>{if(session!=null||wantConnected){wantConnected=false;await Disconnect();}else{wantConnected=true;await Connect();}UpdateState();});
 Add("Reset",()=>Command("reset"));Add("Plotter",()=>{if(plot==null||plot.IsDisposed){plot=new PlotWindow(()=>log);HeroTheme.Apply(plot);plot.Show(FindForm());}else plot.Activate();return Task.CompletedTask;});tools.Controls.Add(hex);tools.Controls.Add(timestamps);
 var more=new Button{Text="More",AutoSize=true,Height=28,Margin=new Padding(2)};tools.Controls.Add(more);
 var menu=new ContextMenuStrip();void Menu(string name,Func<Task> f)=>menu.Items.Add(name,null,async(_,_)=>await Guard(f));
 Menu("DTR on",()=>Command("dtr","-Value","true"));Menu("DTR off",()=>Command("dtr","-Value","false"));Menu("RTS on",()=>Command("rts","-Value","true"));Menu("RTS off",()=>Command("rts","-Value","false"));
 Menu("Clear view",()=>{output.Clear();return Task.CompletedTask;});Menu("Find text",()=>{using var f=new Form{Text="Find serial text",Width=340,Height=110,StartPosition=FormStartPosition.CenterParent};var t=new TextBox{Dock=DockStyle.Top,Text=search.Text};var b=new Button{Dock=DockStyle.Bottom,Text="Find next",DialogResult=DialogResult.OK};f.Controls.Add(t);f.Controls.Add(b);f.AcceptButton=b;HeroTheme.Apply(f);if(f.ShowDialog(FindForm())==DialogResult.OK){search.Text=t.Text;int i=output.Text.IndexOf(t.Text,Math.Min(output.TextLength,output.SelectionStart+output.SelectionLength),StringComparison.OrdinalIgnoreCase);if(i<0)i=output.Text.IndexOf(t.Text,StringComparison.OrdinalIgnoreCase);if(i>=0&&t.Text.Length>0){output.Select(i,t.Text.Length);output.ScrollToCaret();output.Focus();}}return Task.CompletedTask;});
 Menu("Open logs",()=>{Process.Start(new ProcessStartInfo(log==null?Path.Combine(root,"serial-logs"):Path.GetDirectoryName(log)!){UseShellExecute=true});return Task.CompletedTask;});HeroTheme.Menu(menu);more.Click+=(_,_)=>menu.Show(more,new Point(0,more.Height));
 var sendbar=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,Margin=Padding.Empty};sendbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));sendbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,76));sendbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,62));var send=new Button{Text="Send",Dock=DockStyle.Fill,Margin=new Padding(2)};send.Click+=async(_,_)=>await Guard(Send);sendbar.Controls.Add(input,0,0);sendbar.Controls.Add(ending,1,0);sendbar.Controls.Add(send,2,0);layout.Controls.Add(sendbar,0,3);
 input.KeyDown+=async(_,e)=>{if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;await Guard(Send);}};
 baud.SelectedIndexChanged+=async(_,_)=>{if(port!=null&&wantConnected&&!busy&&!suspended)await Guard(async()=>{await Disconnect();await Connect();});};
 timer.Tick+=async(_,_)=>{Poll();if(!stopping&&!busy&&!suspended&&wantConnected&&port!=null&&session==null&&DateTime.UtcNow>=nextRetry)await Guard(Connect);};timer.Start();
 }
 async Task<JsonElement> Call(params string[] args){
 var info=new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,@"WindowsPowerShell\v1.0\powershell.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=root};
 foreach(var a in new[]{"-NoProfile","-NonInteractive","-File",Path.Combine(root,"serial.ps1")}.Concat(args))info.ArgumentList.Add(a);
 using var p=Process.Start(info)??throw new Exception("Cannot start serial backend");var o=p.StandardOutput.ReadToEndAsync();var e=p.StandardError.ReadToEndAsync();await p.WaitForExitAsync();string raw=await o;string err=await e;using var j=JsonDocument.Parse(raw);var v=j.RootElement.Clone();if(!v.GetProperty("success").GetBoolean())throw new Exception(v.GetProperty("error").GetString());return v;
 }
 void UpdateState(){status.ForeColor=session!=null?HeroTheme.Success:wantConnected?HeroTheme.Warning:HeroTheme.Muted;toggle.Text=session!=null?"Disconnect":wantConnected?"Pause retry":"Connect";baud.Enabled=!busy&&!suspended;toggle.Enabled=!busy&&!suspended&&port!=null;}
 async Task Guard(Func<Task> f){if(busy||suspended||stopping)return;busy=true;UpdateState();try{await f();}catch(Exception e){status.Text=e.Message;Append("[ERROR] "+e.Message+"\n");nextRetry=DateTime.UtcNow.AddSeconds(6);}finally{busy=false;UpdateState();}}
 public async Task SetTarget(string? target){if(busy||suspended)throw new Exception("Serial operation in progress");if(target==port&&session!=null)return;busy=true;UpdateState();try{await Disconnect();port=target;wantConnected=target!=null;status.Text=target==null?"Select a port":"Connecting "+target;nextRetry=DateTime.MinValue;if(wantConnected)await Connect();}catch(Exception e){status.Text=e.Message;Append("[ERROR] "+e.Message+"\n");nextRetry=DateTime.UtcNow.AddSeconds(6);}finally{busy=false;UpdateState();}}
 async Task Connect(){if(session!=null||port==null)return;status.Text="Connecting "+port;var j=await Call("start","-Port",port,"-Baud",baud.Text);session=j.GetProperty("session").GetString();log=j.GetProperty("log").GetString();offset=0;pending="";status.Text="Connected "+port+" / "+baud.Text;Poll();}
 async Task Disconnect(){if(session==null)return;await Call("stop","-Session",session);Poll();session=null;status.Text="Disconnected";}
 public async Task<bool> SuspendForUpload(string target){if(target!=port)return false;if(busy)throw new Exception("Serial monitor busy; retry after it settles");if(suspended)throw new Exception("Serial already suspended");suspended=true;UpdateState();try{await Disconnect();status.Text="Paused for hardware operation";return true;}catch{suspended=false;UpdateState();throw;}}
 public async Task ResumeAfterUpload(){try{if(wantConnected)await Connect();else status.Text="Disconnected";}catch(Exception e){status.Text="Reconnect failed: "+e.Message;Append(status.Text+"\n");nextRetry=DateTime.UtcNow.AddSeconds(6);}finally{suspended=false;UpdateState();}}
 public async Task<bool> SuspendAll(){return port!=null&&await SuspendForUpload(port);}
 public async Task Shutdown(){if(busy||suspended)throw new Exception("Serial operation in progress");stopping=true;timer.Stop();try{await Disconnect();if(plot!=null&&!plot.IsDisposed)plot.Close();}catch{stopping=false;timer.Start();throw;}}
 async Task Command(string action,params string[] extra){if(session==null)throw new Exception("Connect first");await Call(new[]{action,"-Session",session}.Concat(extra).ToArray());Poll();}
 async Task Send(){if(session==null)throw new Exception("Connect first");byte[] data;if(hex.Checked){data=Convert.FromHexString(string.Concat(input.Text.Where(c=>!char.IsWhiteSpace(c))));}else{string suffix=ending.Text switch{"LF"=>"\n","CR"=>"\r","CR+LF"=>"\r\n",_=>""};data=Encoding.UTF8.GetBytes(input.Text+suffix);}if(data.Length==0)return;await Command("send","-Data",Convert.ToBase64String(data));input.Clear();}
 void Append(string s){if(output.TextLength>1000000){output.Select(0,200000);output.SelectedText="";}output.AppendText(s);output.SelectionStart=output.TextLength;output.ScrollToCaret();}
 void Poll(){
 if(log==null||!File.Exists(log))return;
 try{using var fs=new FileStream(log,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);fs.Position=offset;using var sr=new StreamReader(fs,Encoding.UTF8,false);pending+=sr.ReadToEnd();offset=fs.Position;int n;while((n=pending.IndexOf('\n'))>=0){string line=pending[..n].TrimEnd('\r');pending=pending[(n+1)..];if(line.Length==0)continue;using var doc=JsonDocument.Parse(line);var j=doc.RootElement;string kind=j.GetProperty("kind").GetString()!;var data=j.GetProperty("data");string prefix=timestamps.Checked?"["+j.GetProperty("time").GetDateTimeOffset().ToLocalTime().ToString("HH:mm:ss.fff")+"] ":"";if(kind=="rx"||kind=="tx"){var b=Convert.FromBase64String(data.GetString()!);string s=hex.Checked?string.Join(" ",b.Select(x=>x.ToString("X2")))+"\n":Encoding.ASCII.GetString(b);Append(prefix+(kind=="tx"?"TX ":"RX ")+s);}else if(kind!="ack")Append(prefix+kind+": "+data+"\n");if(kind=="disconnected"&&session!=null){session=null;status.Text="Disconnected";nextRetry=DateTime.UtcNow.AddSeconds(6);UpdateState();}}}catch(Exception e){status.Text="Log read: "+e.Message;}
 }
 protected override void Dispose(bool disposing){if(disposing)timer.Dispose();base.Dispose(disposing);}
}
