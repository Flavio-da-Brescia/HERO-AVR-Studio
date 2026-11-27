using System.Text;
using System.Text.Json;
using System.Globalization;
using System.Text.RegularExpressions;
sealed class PlotData {
 public record Sample(DateTimeOffset Time,Dictionary<string,double> Values);
 public List<Sample> Samples {get;}=new();
 public int Rejected {get;private set;}
 string pending=""; bool discard;
 static readonly Regex number=new(@"^[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?$");
 static readonly Regex name=new(@"^[A-Za-z_][A-Za-z0-9_.-]{0,63}$");
 public void Feed(DateTimeOffset time,byte[] bytes){
 foreach(byte b in bytes){
  if(b==10){if(!discard)Parse(time,pending.Trim());pending="";discard=false;}
  else if(!discard){if(pending.Length>=8192){discard=true;pending="";Rejected++;}else pending+=(char)b;}
 }
 }
 void Parse(DateTimeOffset time,string line){
 if(line.Length==0)return;
 var parts=Regex.Split(line,@"[\s,;]+");var values=new Dictionary<string,double>(StringComparer.Ordinal);int i=0;
 foreach(var part in parts){i++;var a=part.Split(new[]{':','='},2);string key=a.Length==2?a[0]:"CH"+i;string raw=a[^1];
 if(!name.IsMatch(key)||!number.IsMatch(raw)||!double.TryParse(raw,NumberStyles.Float,CultureInfo.InvariantCulture,out double n)||!double.IsFinite(n)||!values.TryAdd(key,n)){Rejected++;return;}
 }
 if(values.Count>0)Samples.Add(new(time,values));
 }
 public void ReadEvent(string line){
 using var doc=JsonDocument.Parse(line);var j=doc.RootElement;if(j.GetProperty("kind").GetString()!="rx")return;
 Feed(j.GetProperty("time").GetDateTimeOffset(),Convert.FromBase64String(j.GetProperty("data").GetString()!));
 }
 public string[] Channels()=>Samples.SelectMany(s=>s.Values.Keys).Distinct().ToArray();
 public object Summary()=>new{success=true,samples=Samples.Count,rejected=Rejected,channels=Channels().Select(k=>{var a=Samples.Where(s=>s.Values.ContainsKey(k)).Select(s=>s.Values[k]).ToArray();return new{name=k,count=a.Length,min=a.Min(),max=a.Max(),mean=a.Average(),latest=a[^1]};}).ToArray()};
 public void Export(string path){
 var channels=Channels();using var w=new StreamWriter(path,false,new UTF8Encoding(false));w.WriteLine("timestamp,elapsed_s,"+string.Join(",",channels));if(Samples.Count==0)return;
 var t=Samples[0].Time;foreach(var s in Samples)w.WriteLine(s.Time.ToString("o")+","+(s.Time-t).TotalSeconds.ToString("R",CultureInfo.InvariantCulture)+","+string.Join(",",channels.Select(k=>s.Values.TryGetValue(k,out double v)?v.ToString("R",CultureInfo.InvariantCulture):"")));
 }
 public static PlotData Load(string path){var d=new PlotData();using var fs=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);using var reader=new StreamReader(fs);while(reader.ReadLine() is string line){try{d.ReadEvent(line);}catch(JsonException){}}return d;}
}