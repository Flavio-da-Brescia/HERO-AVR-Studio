using System.Drawing;
using System.Windows.Forms;
sealed class FindDialog : Form {
 readonly CodeEditor editor;
 readonly TextBox find=new(){Width=260},replace=new(){Width=260};
 readonly CheckBox match=new(){Text="Match case",AutoSize=true};
 readonly Label result=new(){AutoSize=true};
 public FindDialog(CodeEditor editor){
 this.editor=editor;Text="Find and Replace";Width=480;Height=220;StartPosition=FormStartPosition.CenterParent;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;
 var p=new FlowLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(12),FlowDirection=FlowDirection.TopDown,WrapContents=false};Controls.Add(p);
 FlowLayoutPanel Row(string label,Control c){var r=new FlowLayoutPanel{AutoSize=true};r.Controls.Add(new Label{Text=label,Width=85});r.Controls.Add(c);return r;}
 p.Controls.Add(Row("Find",find));p.Controls.Add(Row("Replace",replace));p.Controls.Add(match);
 var buttons=new FlowLayoutPanel{AutoSize=true};p.Controls.Add(buttons);
 void Add(string title,Action a){var b=new Button{Text=title,AutoSize=true};b.Click+=(_,_)=>a();buttons.Controls.Add(b);}
 Add("Find next",Next);Add("Replace",()=>{if(editor.ReadOnly)return;if(find.Text.Length>0&&string.Equals(editor.SelectedText,find.Text,Comparison))editor.SelectedText=replace.Text;Next();});
 Add("Replace all",()=>{if(editor.ReadOnly||find.Text.Length==0)return;string t=editor.Text;int pos=0,n=0;var sb=new System.Text.StringBuilder();while(true){int i=t.IndexOf(find.Text,pos,Comparison);if(i<0)break;sb.Append(t,pos,i-pos);sb.Append(replace.Text);pos=i+find.Text.Length;n++;}sb.Append(t,pos,t.Length-pos);if(n>0){editor.SelectAll();editor.SelectedText=sb.ToString();}result.Text=n+" replaced";});p.Controls.Add(result);
 if(editor.SelectionLength>0&&!editor.SelectedText.Contains('\n'))find.Text=editor.SelectedText;
 }
 StringComparison Comparison=>match.Checked?StringComparison.Ordinal:StringComparison.OrdinalIgnoreCase;
 void Next(){if(find.Text.Length==0)return;int start=Math.Min(editor.TextLength,editor.SelectionStart+editor.SelectionLength);int i=editor.Text.IndexOf(find.Text,start,Comparison);if(i<0)i=editor.Text.IndexOf(find.Text,Comparison);if(i<0){result.Text="Not found";return;}editor.Select(i,find.Text.Length);editor.ScrollToCaret();result.Text="Found at line "+(editor.GetLineFromCharIndex(i)+1);}
}
