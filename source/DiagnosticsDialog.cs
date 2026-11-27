using System.Diagnostics;
using System.Text.Json;
using System.Drawing;
using System.Windows.Forms;
sealed class DiagnosticsDialog : Form {
 readonly string root;
 readonly ComboBox profile=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=350};
 readonly ComboBox port=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=90};
 readonly ListView results=new(){Dock=DockStyle.Fill,View=View.Details,FullRowSelect=true};
 readonly Label status=new(){Dock=DockStyle.Top,Height=65,Padding=new Padding(10),Font=new Font("Segoe UI",12),Text="Select diagnostic profile and port"};
 readonly FlowLayoutPanel bar=new(){Dock=DockStyle.Top,Height=75,AutoScroll=true};
 readonly List<string> files=new();
 string? outputDir;bool busy;
 public DiagnosticsDialog(string root,string[] ports){
 this.root=root;Text="HERO Hardware Diagnostics";Width=1050;Height=600;MinimumSize=new Size(650,400);StartPosition=FormStartPosition.CenterParent;
 port.Items.AddRange(ports);
 foreach(var (n,w) in new[]{("Test",260),("Status",100),("Values",220),("Notes",430)})results.Columns.Add(n,w);
 Controls.Add(results);Controls.Add(status);Controls.Add(bar);
 void Add(string label,Func<Task> f){var b=new Button{Text=label,AutoSize=true,Height=33};b.Click+=async(_,_)=>await Guard(f);bar.Controls.Add(b);}
 bar.Controls.Add(profile);bar.Controls.Add(port);Add("Refresh profiles",RefreshProfiles);
 Add("Run diagnostics",Run);Add("Open report",()=>{if(outputDir!=null)Process.Start(new ProcessStartInfo(outputDir){UseShellExecute=true});return Task.CompletedTask;});
 Add("Open profiles",()=>{Process.Start(new ProcessStartInfo(Path.Combine(root,"diagnostics","profiles")){UseShellExecute=true});return Task.CompletedTask;});
 Add("Open history",()=>{Process.Start(new ProcessStartInfo(Path.Combine(root,"diagnostics","runs")){UseShellExecute=true});return Task.CompletedTask;});
 Shown+=async(_,_)=>await Guard(RefreshProfiles);FormClosing+=(_,e)=>{if(busy)e.Cancel=true;};
 }
 static string Get(JsonElement j,string k)=>j.TryGetProperty(k,out var v)?v.ToString():"";
 internal Task<JsonElement> PolicyProbe()=>Call("list");
 async Task<JsonElement> Call(params string[] args){
 var s=new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,@"WindowsPowerShell\v1.0\powershell.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=root};
 foreach(var a in new[]{"-NoProfile","-NonInteractive","-ExecutionPolicy","Bypass","-File",Path.Combine(root,"diagnostics.ps1")}.Concat(args))s.ArgumentList.Add(a);
 using var p=Process.Start(s)??throw new Exception("Cannot launch diagnostic runner");var o=p.StandardOutput.ReadToEndAsync();var e=p.StandardError.ReadToEndAsync();await p.WaitForExitAsync();string raw=await o;string err=await e;
 try{using var doc=JsonDocument.Parse(raw);return doc.RootElement.Clone();}catch{throw new Exception(err+" "+raw);}
 }
 async Task Guard(Func<Task> f){if(busy)return;busy=true;bar.Enabled=false;try{await f();}catch(Exception e){status.Text=e.Message;status.ForeColor=HeroTheme.Danger;}finally{busy=false;bar.Enabled=true;}}
 async Task RefreshProfiles(){var j=await Call("list");if(!j.GetProperty("success").GetBoolean())throw new Exception(Get(j,"error"));files.Clear();profile.Items.Clear();foreach(var p in j.GetProperty("profiles").EnumerateArray()){files.Add(Get(p,"file"));profile.Items.Add(Get(p,"name"));}if(files.Count>0)profile.SelectedIndex=0;}
 async Task Run(){
 if(profile.SelectedIndex<0||port.SelectedItem is not string p)throw new Exception("Select profile and port");
 if(MessageBox.Show("Run the selected diagnostic protocol on "+p+"? Close other serial sessions first. Requires matching diagnostic firmware.","Run diagnostics",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
 status.Text="Running diagnostic sequence...";status.ForeColor=HeroTheme.Text;results.Items.Clear();outputDir=null;
 var j=await Call("run","-Profile",files[profile.SelectedIndex],"-Port",p,"-Confirm","RUN-DIAGNOSTICS");
 if(!j.TryGetProperty("steps",out var rows)){status.Text=Get(j,"error");status.ForeColor=HeroTheme.Danger;return;}
 outputDir=Get(j,"output_dir");foreach(var r in rows.EnumerateArray()){
 string state=Get(r,"status");var row=new ListViewItem(new[]{Get(r,"label"),state,Get(r,"values"),Get(r,"reason")});
 row.ForeColor=state=="passed"?HeroTheme.Success:state=="failed"?HeroTheme.Danger:HeroTheme.Warning;results.Items.Add(row);
 }
 string q=Get(j,"qualification");status.Text=q.ToUpperInvariant()+" | Passed: "+Get(j,"passed")+" | Failed: "+Get(j,"failed")+" | Skipped: "+Get(j,"skipped")+"\n"+Get(j,"error");
 status.ForeColor=q=="passed"?HeroTheme.Success:q=="partial"?HeroTheme.Warning:HeroTheme.Danger;
 }
}
