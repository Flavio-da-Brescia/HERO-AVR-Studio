# HERO conservative sketch preprocessing; native sources bypass this.
function Convert-HeroSketch([string[]]$Files,[string]$Destination){
 if(-not ('HeroSketchPreprocessor' -as [type])){
  Add-Type -Language CSharp -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
public static class HeroSketchPreprocessor {
 static string Blank(string s){return Regex.Replace(s,@"[^\r\n]"," ");}
 static string Mask(string s){
  s=Regex.Replace(s,@"(?s)/\*.*?\*/|//[^\r\n]*|""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*'",m=>Blank(m.Value));
  return Regex.Replace(s,@"(?m)^[ \t]*\#[^\r\n]*(?:\\\r?\n[^\r\n]*)*",m=>Blank(m.Value));
 }
 static bool ConditionalAt(string source,int end){
  int depth=0;
  foreach(Match m in Regex.Matches(source.Substring(0,end),@"(?m)^[ \t]*#\s*(if|ifdef|ifndef|endif)\b")){
   if(m.Groups[1].Value=="endif")depth=Math.Max(0,depth-1);else depth++;
  }
  return depth!=0;
 }
 static string Line(string path,int number){return "#line "+number+" \""+path.Replace("\\","/").Replace("\"","\\\"")+"\"\n";}
 static int LineNumber(string s,int pos){int n=1;for(int i=0;i<pos;i++)if(s[i]=='\n')n++;return n;}
 public static string Generate(string[] files){
  var texts=new List<string>();var starts=new List<int>();var all=new StringBuilder();
  foreach(string f in files){starts.Add(all.Length);string x=File.ReadAllText(f);texts.Add(x);all.Append(x).Append("\n");}
  string source=all.ToString(), masked=Mask(source);
  int depth=0,start=0,insert=-1;
  var prototypes=new List<string>();var seen=new HashSet<string>();
  for(int i=0;i<masked.Length;i++){
   char c=masked[i];
   if(c=='{' && depth==0){
    string segment=masked.Substring(start,i-start);
    string sig=segment.Trim();
    int p=start+segment.Length-segment.TrimStart().Length;
    // Only simple, unqualified, file-scope function definitions. Explicit declarations for templates/default arguments/attributes.
    if(Regex.IsMatch(sig,@"^(?:(?:static|inline|extern|constexpr|const|volatile|signed|unsigned|long|short)\s+)*[A-Za-z_]\w*(?:::[A-Za-z_]\w*)*(?:\s*[*&]\s*|\s+)+[A-Za-z_]\w*\s*\([^(){};=]*\)\s*$") &&
      !Regex.IsMatch(sig,@"^(?:class|struct|enum|union|namespace|typedef|using|return)\b") && !ConditionalAt(source,p)){
     if(insert<0)insert=p;
     string prototype=Regex.Replace(sig,@"\s+"," ")+";";
     if(seen.Add(prototype))prototypes.Add(prototype);
    }
    depth++;
   } else if(c=='{') depth++;
   else if(c=='}'){if(depth>0)depth--;if(depth==0)start=i+1;}
   else if(c==';'&&depth==0)start=i+1;
  }
  var output=new StringBuilder("#include <Arduino.h>\n");
  for(int j=0;j<files.Length;j++){
   string x=texts[j];int local=insert-starts[j];
   output.Append(Line(files[j],1));
   if(insert>=0&&local>=0&&local<x.Length){
    output.Append(x.Substring(0,local)).Append("\n");
    foreach(string proto in prototypes)output.Append(proto).Append("\n");
    output.Append(Line(files[j],LineNumber(x,local))).Append(x.Substring(local));
   }else output.Append(x);
   output.Append("\n");
  }
  return output.ToString();
 }
}
"@
 }
 $result=[HeroSketchPreprocessor]::Generate($Files)
 [IO.File]::WriteAllText($Destination,$result,(New-Object Text.UTF8Encoding($false)))
}
