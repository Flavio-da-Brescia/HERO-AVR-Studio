using System.Diagnostics;
using System.Text.Json;
using System.Drawing;
using System.Windows.Forms;
static class ProductionBackend {
 public static string Get(JsonElement j,string key)=>j.TryGetProperty(key,out var v)?v.ToString():"";
 public static bool Ok(JsonElement j)=>j.TryGetProperty("success",out var v)&&v.ValueKind==JsonValueKind.True;
 public static async Task<JsonElement> Call(string root,params string[] args){
  var pinfo=new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,@"WindowsPowerShell\v1.0\powershell.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=root};
  foreach(var a in new[]{"-NoProfile","-NonInteractive","-File",Path.Combine(root,"production.ps1")}.Concat(args))pinfo.ArgumentList.Add(a);
  using var p=Process.Start(pinfo)??throw new Exception("Cannot launch production engine");var o=p.StandardOutput.ReadToEndAsync();var e=p.StandardError.ReadToEndAsync();await p.WaitForExitAsync();string raw=await o;string err=await e;
  try{using var doc=JsonDocument.Parse(raw);return doc.RootElement.Clone();}catch{throw new Exception(err+" "+raw);}
 }
 public static void Require(JsonElement j){if(!Ok(j))throw new Exception(Get(j,"error"));}
}
sealed class ProductionConfigDialog:Form {
 readonly string root,project;
 readonly TextBox diagnostic=new(){Dock=DockStyle.Fill,ReadOnly=true},firmware=new(){Dock=DockStyle.Fill,ReadOnly=true};
 readonly NumericUpDown baud=new(){Minimum=300,Maximum=1000000,Value=38400,Increment=9600,Width=100},timeout=new(){Minimum=5,Maximum=600,Value=60,Width=85};
 readonly Label status=new(){Dock=DockStyle.Fill,AutoEllipsis=true,Text="Select both application HEX files. Completion marker: DIAGNOSTIC COMPLETED."};
 readonly Button save=new(){Text="Save production configuration",AutoSize=true,Tag="primary"};
 FirmwareChoice? diag,prod;bool busy;
 public ProductionConfigDialog(string root,string project){
 this.root=root;this.project=project;Text="Production configuration / "+project;Width=880;Height=360;MinimumSize=new Size(720,340);StartPosition=FormStartPosition.CenterParent;
 var grid=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(12),ColumnCount=3,RowCount=6};grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,155));grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,110));
 foreach(int h in new[]{35,42,42,44,70,42})grid.RowStyles.Add(new RowStyle(SizeType.Absolute,h));Controls.Add(grid);
 var title=HeroTheme.Heading("PROJECT: "+project);grid.Controls.Add(title,0,0);grid.SetColumnSpan(title,3);
 void Choose(int row,string label,TextBox box,bool isDiag){grid.Controls.Add(new Label{Text=label,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},0,row);grid.Controls.Add(box,1,row);var b=new Button{Text="Browse HEX",Dock=DockStyle.Fill};b.Click+=(_,_)=>{try{using var f=new OpenFileDialog{Filter="Application HEX (*.hex)|*.hex"};if(f.ShowDialog(this)!=DialogResult.OK)return;var c=FirmwareCatalog.External(f.FileName);if(isDiag)diag=c;else prod=c;box.Text=c.Path;status.Text=c.Name+" | "+c.FlashBytes+" data bytes | SHA256 "+c.Sha256;}catch(Exception e){status.Text=e.Message;}};grid.Controls.Add(b,2,row);}
 Choose(1,"Diagnostic HEX",diagnostic,true);Choose(2,"Production HEX",firmware,false);
 var settings=new FlowLayoutPanel{Dock=DockStyle.Fill};settings.Controls.AddRange(new Control[]{new Label{Text="Baud",AutoSize=true},baud,new Label{Text="Timeout (s)",AutoSize=true},timeout});grid.Controls.Add(settings,0,3);grid.SetColumnSpan(settings,3);
 grid.Controls.Add(status,0,4);grid.SetColumnSpan(status,3);grid.Controls.Add(save,1,5);save.Click+=async(_,_)=>await SavePair();FormClosing+=(_,e)=>{if(busy)e.Cancel=true;};Shown+=async(_,_)=>await LoadPair();
 }
 async Task LoadPair(){busy=true;save.Enabled=false;try{var j=await ProductionBackend.Call(root,"project","-Project",project);ProductionBackend.Require(j);if(j.GetProperty("configured").GetBoolean()){var c=j.GetProperty("configuration");var dir=Path.Combine(root,"projects",project,"production-configs",ProductionBackend.Get(c,"revision"));diag=FirmwareCatalog.External(Path.Combine(dir,"diagnostic.hex"));prod=FirmwareCatalog.External(Path.Combine(dir,"firmware.hex"));if(diag.Sha256!=ProductionBackend.Get(c,"diagnostic_sha256")||prod.Sha256!=ProductionBackend.Get(c,"sha256"))throw new Exception("Existing configuration checksum mismatch; select both HEX files again");diagnostic.Text=ProductionBackend.Get(c,"diagnostic_name")+" (archived)";firmware.Text=ProductionBackend.Get(c,"source_name")+" (archived)";baud.Value=c.GetProperty("diagnostic_baud").GetInt32();timeout.Value=c.GetProperty("timeout_seconds").GetInt32();status.Text="Current pair loaded. Saving creates a new revision.";} }catch(Exception e){diag=null;prod=null;status.Text=e.Message;}finally{busy=false;save.Enabled=true;}}
 async Task SavePair(){if(busy)return;busy=true;save.Enabled=false;try{if(diag==null||prod==null)throw new Exception("Select both HEX files");FirmwareCatalog.Revalidate(diag);FirmwareCatalog.Revalidate(prod);var j=await ProductionBackend.Call(root,"configure","-Project",project,"-DiagnosticHex",diag.Path,"-DiagnosticSha256",diag.Sha256,"-Hex",prod.Path,"-Sha256",prod.Sha256,"-Baud",baud.Value.ToString(),"-TimeoutSeconds",timeout.Value.ToString());ProductionBackend.Require(j);busy=false;DialogResult=DialogResult.OK;Close();}catch(Exception e){status.Text=e.Message;status.ForeColor=HeroTheme.Danger;}finally{busy=false;save.Enabled=true;}}
}
