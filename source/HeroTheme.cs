using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
static class HeroTheme {
 public static readonly Color Background=Color.FromArgb(14,19,23),Panel=Color.FromArgb(23,30,35),Field=Color.FromArgb(12,17,21),Border=Color.FromArgb(55,72,80),Text=Color.FromArgb(232,239,242),Muted=Color.FromArgb(151,170,180),Accent=Color.FromArgb(57,220,214),Success=Color.FromArgb(107,227,166),Warning=Color.FromArgb(244,188,102),Danger=Color.FromArgb(255,128,128);
 [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h,int a,ref int v,int size);
 [DllImport("uxtheme.dll",CharSet=CharSet.Unicode)] static extern int SetWindowTheme(IntPtr h,string app,string? id);
 public static void Apply(Control c){
  if(c is Form f){f.BackColor=Background;f.ForeColor=Text;f.HandleCreated+=(_,_)=>DarkTitle(f);if(f.IsHandleCreated)DarkTitle(f);}
  else if(c is Button b){bool primary=b.BackColor==Color.FromArgb(18,105,87)||Equals(b.Tag,"primary");b.UseVisualStyleBackColor=false;b.FlatStyle=FlatStyle.Flat;b.FlatAppearance.BorderSize=1;b.FlatAppearance.BorderColor=primary?Accent:Border;b.FlatAppearance.MouseOverBackColor=Color.FromArgb(39,69,74);b.FlatAppearance.MouseDownBackColor=Color.FromArgb(28,90,91);b.BackColor=primary?Color.FromArgb(20,83,83):Panel;b.ForeColor=primary?Accent:Text;b.Cursor=Cursors.Hand;}
  else if(c is CodeEditor||c is LineGutter||c is PlotCanvas||c is HeroMemoryBar||c is HeroHeader){}
  else if(c is RichTextBox r){r.BackColor=Field;r.ForeColor=r.ReadOnly?Color.FromArgb(176,224,207):Text;}
  else if(c is ComboBox cb){cb.BackColor=Field;cb.ForeColor=Text;cb.FlatStyle=FlatStyle.Flat;cb.DrawMode=DrawMode.OwnerDrawFixed;cb.DrawItem+=(_,e)=>{if(e.Index<0)return;using var br=new SolidBrush((e.State&DrawItemState.Selected)!=0?Color.FromArgb(39,69,74):Field);e.Graphics.FillRectangle(br,e.Bounds);TextRenderer.DrawText(e.Graphics,cb.GetItemText(cb.Items[e.Index]),cb.Font,e.Bounds,Text,TextFormatFlags.VerticalCenter|TextFormatFlags.Left|TextFormatFlags.EndEllipsis);e.DrawFocusRectangle();};cb.HandleCreated+=(_,_)=>NativeDark(cb);if(cb.IsHandleCreated)NativeDark(cb);}
  else if(c is TextBoxBase||c is ListBox||c is ListView||c is TreeView||c is NumericUpDown){c.BackColor=Field;c.ForeColor=Text; if(c is ListView||c is TreeView){c.HandleCreated+=(_,_)=>NativeDark(c);if(c.IsHandleCreated)NativeDark(c);}}
  else if(c is Label l){l.ForeColor=l.ForeColor==Color.DarkGreen?Success:l.ForeColor==Color.Firebrick?Danger:l.ForeColor==Color.DarkOrange?Warning:Equals(l.Tag,"muted")?Muted:Equals(l.Tag,"accent")?Accent:Text;if(l.BackColor!=Color.Transparent)l.BackColor=Panel;}
  else if(c is CheckBox||c is RadioButton){c.BackColor=Panel;c.ForeColor=Text;}
  else if(c is Panel||c is SplitContainer||c is UserControl||c is GroupBox){c.BackColor=Panel;c.ForeColor=Text;}
  else {c.BackColor=Panel;c.ForeColor=Text;}
  foreach(Control child in c.Controls)Apply(child);
 }
 public static void SidebarIcon(Button b){
  var im=new Bitmap(22,22,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
  using(var g=Graphics.FromImage(im)){
   g.SmoothingMode=SmoothingMode.AntiAlias;using var p=new Pen(Accent,1.6f){StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round};
   void Line(float x,float y,float a,float c)=>g.DrawLine(p,x,y,a,c);
   void Rect(float x,float y,float w,float h)=>g.DrawRectangle(p,x,y,w,h);
   switch(b.Text){
    case "New":g.DrawPolygon(p,new PointF[]{new(5,2),new(13,2),new(18,7),new(18,20),new(5,20)});Line(13,2,13,7);Line(13,7,18,7);Line(9,13,14,13);Line(11.5f,10.5f,11.5f,15.5f);break;
    case "Open":g.DrawPolygon(p,new PointF[]{new(2,5),new(8,5),new(10,8),new(20,8),new(18,18),new(2,18)});break;
    case "Save":Rect(3,3,16,16);Rect(7,3,8,6);Rect(7,13,8,6);break;
    case "Find":g.DrawEllipse(p,3,3,11,11);Line(13,13,19,19);break;
    case "Compile":g.DrawEllipse(p,2,2,18,18);g.DrawLines(p,new PointF[]{new(6,11),new(10,15),new(16,7)});break;
    case "Upload":Line(11,16,11,3);g.DrawLines(p,new PointF[]{new(6,8),new(11,3),new(16,8)});g.DrawLines(p,new PointF[]{new(3,13),new(3,19),new(19,19),new(19,13)});break;
    case "Libraries":Rect(3,4,4,15);Rect(9,4,4,15);g.DrawPolygon(p,new PointF[]{new(15,4),new(18,3),new(21,18),new(18,19)});break;
    case "Projects":g.DrawPolygon(p,new PointF[]{new(2,5),new(8,5),new(10,8),new(20,8),new(20,19),new(2,19)});Line(6,12,15,12);break;
    case "ISP":Rect(5,5,12,12);for(int n=7;n<=15;n+=4){Line(n,2,n,5);Line(n,17,n,20);Line(2,n,5,n);Line(17,n,20,n);}break;
    case "Production":g.DrawPolygon(p,new PointF[]{new(2,19),new(2,9),new(8,13),new(8,9),new(14,13),new(14,5),new(19,5),new(20,19)});break;
    case "Diagnostics":g.DrawLines(p,new PointF[]{new(2,11),new(6,11),new(9,4),new(13,18),new(16,11),new(20,11)});break;
   }
  }
  b.Image=im;b.ImageAlign=ContentAlignment.MiddleLeft;b.TextImageRelation=TextImageRelation.ImageBeforeText;b.TextAlign=ContentAlignment.MiddleLeft;b.Padding=new Padding(7,0,0,0);b.Disposed+=(_,_)=>im.Dispose();
 }
 static void DarkTitle(Form f){try{int on=1;DwmSetWindowAttribute(f.Handle,20,ref on,4);}catch{}}
 static void NativeDark(Control c){try{SetWindowTheme(c.Handle,"DarkMode_Explorer",null);}catch{}}
 public static void Menu(ContextMenuStrip m){m.BackColor=Panel;m.ForeColor=Text;foreach(ToolStripItem i in m.Items){i.BackColor=Panel;i.ForeColor=Text;}}
 public static Label Heading(string name)=>new(){Text=name,Dock=DockStyle.Fill,Font=new Font("Segoe UI Semibold",10),TextAlign=ContentAlignment.MiddleLeft,Padding=new Padding(8,0,0,0),Tag="accent"};
}
sealed class HeroMemoryBar:Control {
 int value,maximum=100;
 [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public int Maximum {get=>maximum;set{maximum=Math.Max(1,value);Invalidate();}}
 [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public int Value {get=>value;set{this.value=Math.Clamp(value,0,maximum);Invalidate();}}
 public HeroMemoryBar(){DoubleBuffered=true;Height=14;BackColor=HeroTheme.Panel;ResizeRedraw=true;}
 protected override void OnPaint(PaintEventArgs e){
  var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
  var r=new Rectangle(1,3,Math.Max(1,Width-2),Math.Max(1,Height-6));using var bg=new SolidBrush(HeroTheme.Field);g.FillRectangle(bg,r);using var pen=new Pen(HeroTheme.Border);g.DrawRectangle(pen,r);
  int n=(int)((r.Width-2)*(double)value/maximum);if(n>0){using var br=new LinearGradientBrush(new Rectangle(r.X+1,r.Y+1,Math.Max(n,1),Math.Max(r.Height-1,1)),Color.FromArgb(26,133,140),HeroTheme.Accent,0f);g.FillRectangle(br,r.X+1,r.Y+1,n,Math.Max(1,r.Height-1));}
 }
}
sealed class HeroHeader:Control {
 readonly Bitmap? logo;
 public HeroHeader(string root){DoubleBuffered=true;Dock=DockStyle.Fill;ResizeRedraw=true;BackColor=HeroTheme.Background;
 try{using var im=new Bitmap(Path.Combine(root,"assets","hero-logo.png"));logo=new Bitmap(im.Width,im.Height,System.Drawing.Imaging.PixelFormat.Format32bppArgb);for(int y=0;y<im.Height;y++)for(int x=0;x<im.Width;x++){int a=im.GetPixel(x,y).R;logo.SetPixel(x,y,Color.FromArgb(a,230,235,238));}}catch{}
 }
 protected override void OnPaint(PaintEventArgs e){
  var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
  using var bg=new LinearGradientBrush(ClientRectangle,HeroTheme.Panel,HeroTheme.Background,0f);g.FillRectangle(bg,ClientRectangle);
  if(logo!=null)g.DrawImage(logo,new Rectangle(16,12,190,48));else{using var f=new Font("Segoe UI Semibold",21);g.DrawString("HERO",f,Brushes.White,12,10);}
  using var accent=new Pen(HeroTheme.Accent,2);g.DrawLine(accent,228,10,212,Height-11);g.DrawLine(accent,0,Height-1,Width,Height-1);
  using var title=new Font("Segoe UI Semibold",21);using var sub=new Font("Segoe UI",10);using var white=new SolidBrush(HeroTheme.Text);using var muted=new SolidBrush(HeroTheme.Muted);using var teal=new SolidBrush(HeroTheme.Accent);
  g.DrawString("AVR STUDIO",title,white,244,7);g.DrawString("EMBEDDED DEVELOPMENT ENVIRONMENT",sub,muted,247,43);
  if(Width>800){string target="ATmega328P  /  3.3 V  /  8 MHz";var w=g.MeasureString(target,sub).Width;g.DrawString(target,sub,teal,Width-w-18,17);string v="HERO  |  v2.0.0";g.DrawString(v,sub,muted,Width-g.MeasureString(v,sub).Width-18,39);}
 }
 protected override void Dispose(bool disposing){if(disposing)logo?.Dispose();base.Dispose(disposing);}
}
