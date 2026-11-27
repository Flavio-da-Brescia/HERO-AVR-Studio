using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Windows.Forms;

sealed class CodeEditor : RichTextBox {
 [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h,int m,IntPtr w,IntPtr l);
 readonly System.Windows.Forms.Timer timer=new(){Interval=350};
 bool coloring;
 public bool IsColoring=>coloring;
 static readonly Regex tokens=new(@"//[^\n]*|/\*[\s\S]*?\*/|""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*'|^\s*#[^\n]*|\b(?:void|bool|char|int|long|short|float|double|byte|uint8_t|uint16_t|uint32_t|const|static|volatile|unsigned|signed|if|else|for|while|do|switch|case|break|continue|return|struct|class|enum|true|false|HIGH|LOW|INPUT|OUTPUT|INPUT_PULLUP|setup|loop|pinMode|digitalWrite|digitalRead|analogRead|analogWrite|delay|millis|micros|Serial)\b|\b(?:0[xX][0-9a-fA-F]+|\d+(?:\.\d+)?)\b",RegexOptions.Multiline|RegexOptions.Compiled);
 public event Action? ViewChanged;
 public CodeEditor(){timer.Tick+=(_,_)=>{timer.Stop();Highlight();};TextChanged+=(_,_)=>{if(!coloring){timer.Stop();timer.Start();ViewChanged?.Invoke();}};VScroll+=(_,_)=>ViewChanged?.Invoke();Resize+=(_,_)=>ViewChanged?.Invoke();SelectionChanged+=(_,_)=>{if(!coloring)ViewChanged?.Invoke();};}
 public void Highlight(){
  if(coloring||IsDisposed||!IsHandleCreated||TextLength>150000)return;
  coloring=true;int start=SelectionStart,len=SelectionLength;
  SendMessage(Handle,0x000B,IntPtr.Zero,IntPtr.Zero);
  try{SelectAll();SelectionColor=Color.FromArgb(226,234,242);
   foreach(Match m in tokens.Matches(Text)){
    string t=m.Value.TrimStart();Select(m.Index,m.Length);
    SelectionColor=t.StartsWith("//")||t.StartsWith("/*")?Color.FromArgb(123,198,142):t.StartsWith("\"")||t.StartsWith("'")?Color.FromArgb(236,194,130):t.StartsWith("#")?Color.FromArgb(190,157,235):char.IsDigit(t[0])?Color.FromArgb(240,177,119):Color.FromArgb(90,215,218);
   }
  }finally{Select(start,len);if(len==0)SelectionColor=Color.FromArgb(226,234,242);SendMessage(Handle,0x000B,(IntPtr)1,IntPtr.Zero);Invalidate();coloring=false;ViewChanged?.Invoke();}
 }
 protected override void OnKeyDown(KeyEventArgs e){
  if(!ReadOnly&&e.KeyCode==Keys.Enter&&!e.Control&&!e.Shift){
   int pos=SelectionStart;int line=GetLineFromCharIndex(pos);int first=GetFirstCharIndexFromLine(line);string before=Text.Substring(first,pos-first);string indent=Regex.Match(before,@"^[ \t]*").Value;if(before.TrimEnd().EndsWith("{"))indent+="  ";SelectedText="\n"+indent;e.SuppressKeyPress=true;return;
  }
  if(!ReadOnly&&e.KeyCode==Keys.Tab&&!e.Control&&!e.Shift){SelectedText="  ";e.SuppressKeyPress=true;return;}
  base.OnKeyDown(e);
 }
 public int MatchingBracket(){
  int p=SelectionStart;if(p>=TextLength||!"()[]{}".Contains(Text[p]))p--;if(p<0||p>=TextLength)return -1;
  string text=Text;char c=text[p];int a="([{".IndexOf(c),b=")]}".IndexOf(c);if(a<0&&b<0)return -1;int step=a>=0?1:-1;char other=a>=0?")]}"[a]:"([{"[b];int depth=0;
  for(int i=p;i>=0&&i<text.Length;i+=step){if(text[i]==c)depth++;if(text[i]==other&&--depth==0)return i;}return -1;
 }
 protected override void Dispose(bool disposing){if(disposing)timer.Dispose();base.Dispose(disposing);}
}
sealed class LineGutter : Control {
 readonly CodeEditor editor;
 public LineGutter(CodeEditor editor){this.editor=editor;Width=48;Dock=DockStyle.Left;BackColor=Color.FromArgb(34,41,52);DoubleBuffered=true;editor.ViewChanged+=Invalidate;}
 protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);if(!editor.IsHandleCreated)return;int first=editor.GetLineFromCharIndex(editor.GetCharIndexFromPosition(new Point(1,1)));int count=editor.GetLineFromCharIndex(editor.TextLength)+1;using var brush=new SolidBrush(Color.FromArgb(137,153,173));using var font=new Font("Consolas",10);for(int i=first;i<count;i++){int index=editor.GetFirstCharIndexFromLine(i);if(index<0)break;int y=editor.GetPositionFromCharIndex(index).Y;if(y>Height)break;e.Graphics.DrawString((i+1).ToString(),font,brush,3,y);}}
}
