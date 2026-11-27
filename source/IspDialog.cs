using System.Diagnostics;
using System.Text.Json;
using System.Drawing;
using System.Windows.Forms;
sealed class IspDialog : Form {
 readonly string root;
 readonly ComboBox programmer=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=140};
 readonly ComboBox port=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=90};
 readonly TextBox hex=new(){Width=350,ReadOnly=true};
 readonly CheckBox clock=new(){Text="External 8 MHz crystal/resonator physically confirmed",AutoSize=true};
 readonly Label status=new(){Dock=DockStyle.Bottom,Height=35,Text="Ready - no target detected"};
 readonly RichTextBox output=new(){Dock=DockStyle.Fill,ReadOnly=true,Font=new Font("Consolas",10),BackColor=Color.FromArgb(17,22,29),ForeColor=Color.Gainsboro};
 readonly FlowLayoutPanel top=new(){Dock=DockStyle.Top,Height=160,AutoScroll=true};
 bool busy;
 public IspDialog(string root,string[] ports){
 this.root=root;Text="HERO Chip Programmer - ATmega328P";Width=1050;Height=650;MinimumSize=new Size(650,420);StartPosition=FormStartPosition.CenterParent;
 programmer.Items.AddRange(new object[]{"USBasp","Arduino as ISP"});programmer.SelectedIndex=0;port.Items.AddRange(ports);port.Enabled=false;
 programmer.SelectedIndexChanged+=(_,_)=>port.Enabled=programmer.SelectedIndex==1;
 Controls.Add(output);Controls.Add(status);Controls.Add(top);
 void Add(string label,Action a){var b=new Button{Text=label,AutoSize=true};b.Click+=(_,_)=>a();top.Controls.Add(b);}
 top.Controls.Add(new Label{Text="Programmer",AutoSize=true});top.Controls.Add(programmer);top.Controls.Add(port);top.Controls.Add(new Label{Text="Target: 0x1E950F",AutoSize=true});
 top.SetFlowBreak(top.Controls[^1],true);
 top.Controls.Add(hex);Add("Select HEX",()=>{using var f=new OpenFileDialog{Filter="Intel HEX (*.hex)|*.hex"};if(f.ShowDialog(this)==DialogResult.OK)hex.Text=f.FileName;});top.SetFlowBreak(top.Controls[^1],true);
 Add("Detect / Read fuses",async()=>await Execute("detect"));Add("Backup flash + EEPROM",async()=>await Execute("backup"));Add("Verify flash",async()=>await Execute("verify"));
 Add("Write flash",async()=>await Execute("write-flash","ERASE-FLASH","Chip erase removes existing firmware and bootloader, and may erase EEPROM."));
 Add("Write EEPROM",async()=>await Execute("write-eeprom","WRITE-EEPROM","This overwrites EEPROM data."));
 top.SetFlowBreak(top.Controls[^1],true);
 top.Controls.Add(clock);Add("Burn bootloader",async()=>await Execute("bootloader","ERASE-AND-BURN","Erases application flash and may erase EEPROM. Programs external-clock fuses and bootloader."));
 Add("Configure fuses",async()=>await Execute("configure-fuses","SET-8MHZ-FUSES","Changes hardware clock and brownout configuration. An incorrect clock can make the chip inaccessible."));
 Add("Open logs",()=>Process.Start(new ProcessStartInfo(Path.Combine(root,"isp-logs")){UseShellExecute=true}));
 FormClosing+=(_,e)=>{if(busy)e.Cancel=true;};
 }
 async Task Execute(string action,string? confirm=null,string? warning=null){
 if(busy)return;
 var args=new List<string>{action,"-Programmer",programmer.SelectedIndex==0?"usbasp":"arduinoisp"};
 if(programmer.SelectedIndex==1){if(port.SelectedItem is not string p){status.Text="Select actual Arduino ISP programmer port, not target UART.";return;}args.AddRange(new[]{"-Port",p});}
 if(action is "verify" or "write-flash" or "write-eeprom"){if(!File.Exists(hex.Text)){status.Text="Select a HEX file.";return;}args.AddRange(new[]{"-Hex",hex.Text});}
 if(action is "bootloader" or "configure-fuses"){if(!clock.Checked){status.Text="Confirm physical external clock first.";return;}args.Add("-ExternalClockConfirmed");}
 if(confirm!=null){
 if(MessageBox.Show(warning+"\n\nProceed on the selected physical target?","Destructive ISP operation",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
 args.AddRange(new[]{"-Confirm",confirm});}
 busy=true;top.Enabled=false;status.Text="Running "+action+"...";
 try{
 var start=new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,@"WindowsPowerShell\v1.0\powershell.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=root};
 foreach(var a in new[]{"-NoProfile","-NonInteractive","-File",Path.Combine(root,"isp.ps1")}.Concat(args))start.ArgumentList.Add(a);
 using var proc=Process.Start(start)??throw new Exception("Cannot launch ISP backend");
 var o=proc.StandardOutput.ReadToEndAsync();var e=proc.StandardError.ReadToEndAsync();await proc.WaitForExitAsync();string raw=await o;string err=await e;output.Text=raw+"\n"+err;
 using var json=JsonDocument.Parse(raw);var r=json.RootElement;status.Text=r.GetProperty("success").GetBoolean()?"Operation successful":r.GetProperty("error").GetString();
 }catch(Exception ex){status.Text=ex.Message;}finally{busy=false;top.Enabled=true;port.Enabled=programmer.SelectedIndex==1;}
 }
}
