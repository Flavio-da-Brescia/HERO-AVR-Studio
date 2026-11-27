using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

// Self-contained tutorial diagrams: logical connections, not physical board layouts.
sealed class IspIllustration:Control{
 public int Stage;public bool Arduino,Passed;
 public IspIllustration(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);AccessibleName="ISP tutorial diagram";}
 protected override void OnPaint(PaintEventArgs e){
  var g=e.Graphics;g.Clear(Color.FromArgb(15,21,25));g.SmoothingMode=SmoothingMode.AntiAlias;
  float scale=Math.Min(Width/420f,Height/400f);if(scale<=0)return;g.TranslateTransform((Width-420*scale)/2,(Height-400*scale)/2);g.ScaleTransform(scale,scale);
  using var ink=new SolidBrush(HeroTheme.Text);using var muted=new SolidBrush(HeroTheme.Muted);using var accent=new SolidBrush(HeroTheme.Accent);using var green=new SolidBrush(HeroTheme.Success);
  using var line=new Pen(HeroTheme.Border,1.4f);using var teal=new Pen(HeroTheme.Accent,2f);using var gray=new Pen(HeroTheme.Muted,1.8f);using var chip=new SolidBrush(Color.FromArgb(20,50,55));using var pcb=new SolidBrush(Color.FromArgb(25,74,80));
  using var h=new Font("Segoe UI Semibold",14);using var f=new Font("Segoe UI",10);using var small=new Font("Segoe UI",9);
  void T(string s,float x,float y,float w=390,Brush? b=null,Font? ft=null){g.DrawString(s,ft??f,b??ink,new RectangleF(x,y,w,100));}
  void Box(float x,float y,float w,float h,string label){g.FillRectangle(chip,x,y,w,h);g.DrawRectangle(teal,x,y,w,h);T(label,x+9,y+8,w-14,accent);}
  void Arrow(float x,float y,float x2,float y2){g.DrawLine(teal,x,y,x2,y2);g.FillPolygon(accent,new PointF[]{new(x2,y2),new(x2-8,y2-5),new(x2-8,y2+5)});}
  void Board(float x,float y,float w,float h,string label){g.FillRectangle(pcb,x,y,w,h);g.DrawRectangle(teal,x,y,w,h);g.FillRectangle(chip,x+w*.32f,y+h*.32f,w*.40f,h*.36f);for(int i=0;i<5;i++){float z=x+w*.34f+i*w*.085f;g.DrawLine(gray,z,y+h*.23f,z,y+h*.32f);g.DrawLine(gray,z,y+h*.68f,z,y+h*.77f);}T(label,x,y+h+10,w+16,muted,small);}
  void Laptop(float x,float y){g.DrawRectangle(gray,x,y,117,78);g.FillRectangle(chip,x+5,y+5,107,68);g.DrawLine(gray,x-10,y+82,x+127,y+82);T("HERO AVR",x+13,y+27,100,accent);}
  T("ISP  /  "+(Stage+1),18,16,380,accent,h);g.DrawLine(line,18,49,402,49);
  if(Stage==0){T("Two ways to program a blank AVR",18,64,380,ink,h);Board(30,114,142,82,"USBasp");Board(244,114,142,82,"Arduino helper");T("Dedicated programmer",24,232,173,muted);T("Requires ArduinoISP",237,232,173,muted);T("Both connect directly to the target ISP signals.",18,305,385,ink);}
  else if(Stage==1){T("Prepare the helper only",18,65,380,ink,h);Laptop(22,128);Arrow(151,168,227,168);Board(247,120,142,94,"Helper Arduino");T("USB",162,141,70,muted);T("Disconnect the target first.\nCompile → upload → verify ArduinoISP.",18,265,380);if(Passed)T("VERIFIED",18,335,380,green,h);}
  else if(Stage==2){T("Logical signal connections",18,65,380,ink,h);T(Arduino?"Helper Arduino":"USBasp header",20,106,180,accent);T("ATmega328P",280,106,132,accent);var from=Arduino?new[]{"D10","D11","D12","D13","GND"}:new[]{"5 RESET","4 MOSI","1 MISO","3 SCK","6 GND"};var to=new[]{"RESET","PB3 MOSI","PB4 MISO","PB5 SCK","GND"};for(int i=0;i<5;i++){float y=145+32*i;T(from[i],20,y-10,112);T(to[i],282,y-10,130);g.DrawLine(i==4?gray:teal,130,y,273,y);g.FillEllipse(accent,127,y-3,6,6);g.FillEllipse(accent,270,y-3,6,6);}T("Target supply / reference: check programmer.\nUse 3.3 V-compatible signals and common ground.",18,326,384,muted,small);}
  else if(Stage==3){T("Read before writing",18,65,380,ink,h);Board(28,120,130,78,"Programmer");Arrow(169,157,245,157);Board(258,120,130,78,"Target MCU");Box(25,256,370,85,"Expected signature: 1E950F");T("Actual fuse bytes shown on the left.",37,297,340,muted);if(Passed)T("TARGET DETECTED",25,356,370,green,h);}
  else{T(Passed?"Bootloader verified":"Programming sequence",18,65,380,ink,h);string[] stages={"Identify target","Back up flash and EEPROM","Erase and unlock","Write 8 MHz fuses","Write and verify bootloader","Set lock bits and verify readback"};for(int i=0;i<stages.Length;i++){float y=112+39*i;g.DrawEllipse(Passed?teal:gray,25,y,19,19);if(Passed)g.DrawLines(teal,new PointF[]{new(29,y+9),new(34,y+14),new(41,y+5)});T(stages[i],55,y-1,340,Passed?green:ink);}T(Passed?"Ready for UART upload at 57600 baud.":"Do not disconnect power during programming.",18,356,390,muted,small);}
 }
}
