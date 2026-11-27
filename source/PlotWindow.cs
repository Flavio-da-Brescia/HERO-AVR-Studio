using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
sealed class PlotWindow : Form {
 readonly Func<string?> livePath;
 readonly PlotCanvas canvas=new(){Dock=DockStyle.Fill};
 readonly Label status=new(){Dock=DockStyle.Bottom,Height=32};
 readonly ComboBox range=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=100};
 readonly CheckBox pause=new(){Text="Pause",AutoSize=true};
 readonly CheckedListBox channels=new(){Dock=DockStyle.Right,Width=155,CheckOnClick=true};
 readonly System.Windows.Forms.Timer timer=new(){Interval=400};
 string? selected,path; long offset; string pending=""; PlotData data=new();
 public PlotWindow(Func<string?> livePath){
 this.livePath=livePath;Text="HERO Serial Plotter";Width=1050;Height=640;MinimumSize=new Size(650,400);
 var top=new FlowLayoutPanel{Dock=DockStyle.Top,Height=44};Controls.Add(canvas);Controls.Add(channels);Controls.Add(top);Controls.Add(status);
 void Add(string label,Action a){var b=new Button{Text=label,AutoSize=true};b.Click+=(_,_)=>{try{a();}catch(Exception e){status.Text=e.Message;}};top.Controls.Add(b);}
 Add("Live",()=>{selected=null;path=null;Tick();});Add("Open log",()=>{using var f=new OpenFileDialog{Filter="Serial logs (*.jsonl)|*.jsonl"};if(f.ShowDialog(this)==DialogResult.OK){selected=f.FileName;path=null;Tick();}});
 Add("Export CSV",()=>{using var f=new SaveFileDialog{Filter="CSV (*.csv)|*.csv",FileName="measurements.csv"};if(f.ShowDialog(this)==DialogResult.OK){data.Export(f.FileName);status.Text="Exported "+data.Samples.Count+" samples";}});
 range.Items.AddRange(new object[]{"100 samples","500 samples","2000 samples","All"});range.SelectedIndex=1;top.Controls.Add(range);top.Controls.Add(pause);
 range.SelectedIndexChanged+=(_,_)=>RefreshChart();channels.ItemCheck+=(_,_)=>BeginInvoke(new Action(RefreshChart));timer.Tick+=(_,_)=>{if(!pause.Checked)Tick();};timer.Start();Shown+=(_,_)=>Tick();
 }
 void Tick(){
 try{string? next=selected??livePath();if(next!=path){path=next;offset=0;pending="";data=new();channels.Items.Clear();}
 if(path==null||!File.Exists(path)){status.Text="Connect Serial Monitor or open an existing log.";return;}
 using var fs=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);fs.Position=offset;using var sr=new StreamReader(fs);pending+=sr.ReadToEnd();offset=fs.Position;
 int n;while((n=pending.IndexOf('\n'))>=0){string line=pending[..n].TrimEnd('\r');pending=pending[(n+1)..];if(line.Length>0)data.ReadEvent(line);}
 foreach(string c in data.Channels())if(!channels.Items.Contains(c))channels.Items.Add(c,true);
 RefreshChart();var stats=data.Channels().Select(c=>{var a=data.Samples.Where(s=>s.Values.ContainsKey(c)).Select(s=>s.Values[c]).ToArray();return c+": "+a[^1].ToString("G5")+" ["+a.Min().ToString("G5")+".."+a.Max().ToString("G5")+"]";});
 status.Text=data.Samples.Count+" samples | "+data.Rejected+" nonnumeric lines | "+string.Join("  ",stats);
 }catch(Exception e){status.Text="Plot: "+e.Message;}
 }
 void RefreshChart(){canvas.Data=data;canvas.Selected=channels.CheckedItems.Cast<string>().ToArray();canvas.Window=range.SelectedIndex switch{0=>100,1=>500,2=>2000,_=>int.MaxValue};canvas.Invalidate();}
 protected override void Dispose(bool disposing){if(disposing)timer.Dispose();base.Dispose(disposing);}
}
sealed class PlotCanvas : Control {
 public PlotData Data=new();public string[] Selected=Array.Empty<string>();public int Window=500;
 static readonly Color[] colors={Color.FromArgb(54,211,153),Color.FromArgb(100,170,255),Color.Gold,Color.Orchid,Color.Coral,Color.Cyan};
 public PlotCanvas(){DoubleBuffered=true;BackColor=Color.FromArgb(17,22,29);ForeColor=Color.Gainsboro;ResizeRedraw=true;}
 protected override void OnPaint(PaintEventArgs e){
 base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;using var brush=new SolidBrush(ForeColor);using var grid=new Pen(Color.FromArgb(54,64,75));var rect=new RectangleF(72,32,Math.Max(1,Width-92),Math.Max(1,Height-82));
 var samples=Data.Samples.TakeLast(Math.Min(Window,Data.Samples.Count)).ToArray();var values=samples.SelectMany(s=>s.Values.Where(k=>Selected.Contains(k.Key)).Select(k=>k.Value)).ToArray();
 if(values.Length==0){g.DrawString("Waiting for numeric serial data...",Font,brush,20,20);return;}
 double lo=values.Min(),hi=values.Max();if(lo==hi){lo-=1;hi+=1;}else{double p=(hi-lo)*.05;lo-=p;hi+=p;}
 for(int i=0;i<=5;i++){float y=rect.Bottom-i*rect.Height/5;g.DrawLine(grid,rect.Left,y,rect.Right,y);g.DrawString((lo+(hi-lo)*i/5).ToString("G5",CultureInfo.InvariantCulture),Font,brush,3,y-8);}
 var t0=samples[0].Time;double duration=Math.Max(.001,(samples[^1].Time-t0).TotalSeconds);
 for(int i=0;i<=5;i++){float x=rect.Left+i*rect.Width/5;g.DrawLine(grid,x,rect.Top,x,rect.Bottom);g.DrawString((duration*i/5).ToString("0.0")+"s",Font,brush,x-10,rect.Bottom+8);}
 for(int c=0;c<Selected.Length;c++){string name=Selected[c];using var pen=new Pen(colors[c%colors.Length],1.8f);using var b=new SolidBrush(pen.Color);PointF? prev=null;foreach(var s in samples){if(!s.Values.TryGetValue(name,out double v)){prev=null;continue;}var pt=new PointF(rect.Left+(float)((s.Time-t0).TotalSeconds/duration)*rect.Width,rect.Bottom-(float)((v-lo)/(hi-lo))*rect.Height);if(prev.HasValue)g.DrawLine(pen,prev.Value,pt);else g.FillEllipse(b,pt.X-2,pt.Y-2,4,4);prev=pt;}g.DrawString(name,Font,b,rect.Left+c*100,5);}
 }
}
