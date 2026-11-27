using System.Text.Json;
using System.Security.Cryptography;
using System.Globalization;

sealed record FirmwareChoice(string Path,string Name,string Origin,string Version,DateTimeOffset Date,string DateLabel,string Sha256,int FlashBytes,int HexBytes,int? RamBytes,string Id,string Project) {
 public string Display=>Project.Length>0?Project+" / "+Version:Name+" | "+Date.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
}
static class FirmwareCatalog {
 const string Board="arduino:avr:pro:cpu=8MHzatmega328";
 static string Get(JsonElement j,string k)=>j.TryGetProperty(k,out var x)?x.ToString():"";
 static JsonElement Read(string path){using var d=JsonDocument.Parse(File.ReadAllText(path));return d.RootElement.Clone();}
 static bool Ok(JsonElement j)=>j.TryGetProperty("success",out var v)&&v.ValueKind==JsonValueKind.True;
 static DateTimeOffset Date(JsonElement j,string k,string path)=>DateTimeOffset.TryParse(Get(j,k),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out var d)?d:new DateTimeOffset(File.GetLastWriteTimeUtc(path));
 static IEnumerable<string> Dirs(string path)=>Directory.Exists(path)?Directory.EnumerateDirectories(path):Array.Empty<string>();
 static string ResolveInside(string folder,string relative){string full=Path.GetFullPath(Path.Combine(folder,relative));if(!full.StartsWith(Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Firmware path outside archive");return full;}
 static int CountHex(byte[] raw){
  var seen=new HashSet<int>();long offset=0;bool eof=false;
  foreach(string input in System.Text.Encoding.ASCII.GetString(raw).Split('\n')){
   string line=input.Trim();if(line.Length==0)continue;
   if(eof||line[0]!=':')throw new IOException("Invalid Intel HEX record");
   var b=Convert.FromHexString(line[1..]);if(b.Length<5||b.Length!=b[0]+5||b.Sum(x=>(int)x)%256!=0)throw new IOException("Invalid Intel HEX length/checksum");
   int n=b[0],addr=b[1]*256+b[2];switch(b[3]){
    case 0:if(offset+addr+n>30720)throw new IOException("HEX exceeds application flash limit (30,720 bytes)");for(int i=0;i<n;i++){if(!seen.Add((int)(offset+addr+i)))throw new IOException("Overlapping HEX data records");}break;
    case 1:if(n!=0||addr!=0)throw new IOException("Invalid EOF");eof=true;break;
    case 2:case 4:if(n!=2||addr!=0)throw new IOException("Invalid extended address");offset=(long)(b[4]*256+b[5])*(b[3]==2?16:65536);break;
    default:throw new IOException("Unsupported HEX record");
   }
  }
  if(!eof||seen.Count==0)throw new IOException("Incomplete or empty HEX");return seen.Count;
 }
 static (int? flash,int? ram) Memory(JsonElement sizes){
  int? f=null,r=null;if(sizes.ValueKind==JsonValueKind.Array)foreach(var s in sizes.EnumerateArray()){if(s.TryGetProperty("size",out var v)&&v.TryGetInt32(out int n)){if(Get(s,"name")=="text")f=n;if(Get(s,"name")=="data")r=n;}}return(f,r);
 }
 static (int? flash,int? ram) LogMemory(string path){try{var j=Read(path);return Memory(j.GetProperty("builder_result").GetProperty("executable_sections_size"));}catch{return(null,null);}}
 static FirmwareChoice Make(string path,string origin,string version,DateTimeOffset date,string dateLabel,string expected,int? flash,int? ram,string id,string project){
  path=System.IO.Path.GetFullPath(path);if(!File.Exists(path)||!path.EndsWith(".hex",StringComparison.OrdinalIgnoreCase)||path.Contains("with_bootloader",StringComparison.OrdinalIgnoreCase))throw new IOException("Select an application-only HEX");
  byte[] raw=File.ReadAllBytes(path);string hash=Convert.ToHexString(SHA256.HashData(raw));if(expected.Length>0&&!hash.Equals(expected,StringComparison.OrdinalIgnoreCase))throw new IOException("Archived firmware SHA-256 mismatch");
  int bytes=CountHex(raw);if(flash is <0 or >30720||ram is <0 or >2048)throw new IOException("Invalid build memory metadata");
  return new(path,System.IO.Path.GetFileName(path),origin,version,date,dateLabel,hash,flash??bytes,bytes,ram,id,project);
 }
 public static FirmwareChoice External(string path)=>Make(path,"External HEX","Unknown",new DateTimeOffset(File.GetLastWriteTimeUtc(path)),"File modified","",null,null,"","");
 public static void Revalidate(FirmwareChoice c){Make(c.Path,c.Origin,c.Version,c.Date,c.DateLabel,c.Sha256,c.FlashBytes,c.RamBytes,c.Id,c.Project);}
 public static FirmwareChoice? Latest(string root){
  var candidates=new List<FirmwareChoice>();var tests=new List<FirmwareChoice>();
  foreach(string dir in Dirs(Path.Combine(root,"build")))try{
   string m=Path.Combine(dir,"result.json");var j=Read(m);if(!Ok(j)||Get(j,"board")!=Board||Get(j,"sha256").Length!=64)continue;
   string path=ResolveInside(dir,System.IO.Path.GetFileName(Get(j,"hex_path")));var mem=LogMemory(Path.Combine(dir,"compiler.log"));
   bool test=Get(j,"action")=="selftest";var c=Make(path,test?"Compiler self-test":"Successful compilation","Unversioned",Date(j,"created",m),"Compiled",Get(j,"sha256"),mem.flash,mem.ram,Get(j,"build_id"),"");(test?tests:candidates).Add(c);
  }catch{}
  foreach(string p in Dirs(Path.Combine(root,"projects")))foreach(string b in Dirs(Path.Combine(p,"builds")))try{
   string m=Path.Combine(b,"build.json");var j=Read(m);var r=j.GetProperty("result");if(!Ok(r)||Get(j,"board")!=Board||Get(r,"sha256").Length!=64)continue;
   string path=ResolveInside(Path.Combine(b,"output"),System.IO.Path.GetFileName(Get(r,"hex_path")));var mem=r.TryGetProperty("memory",out var sizes)?Memory(sizes):LogMemory(Path.Combine(b,"output","compiler.log"));
   candidates.Add(Make(path,"Managed project build","Unversioned",Date(j,"created",m),"Compiled",Get(r,"sha256"),mem.flash,mem.ram,Get(j,"id"),System.IO.Path.GetFileName(p)));
  }catch{}
  return (candidates.Count>0?candidates:tests).OrderByDescending(c=>c.Date).FirstOrDefault();
 }
 public static List<FirmwareChoice> Releases(string root){
  var result=new List<FirmwareChoice>();foreach(string p in Dirs(Path.Combine(root,"projects")))foreach(string dir in Dirs(Path.Combine(p,"releases")))try{
   string m=Path.Combine(dir,"release.json");var j=Read(m);if(Get(j,"board")!=Board||Get(j,"sha256").Length!=64)continue;
   var b=Read(Path.Combine(dir,"build.json"));var r=b.GetProperty("result");if(!Ok(r)||!Get(r,"sha256").Equals(Get(j,"sha256"),StringComparison.OrdinalIgnoreCase))continue;
   var mem=r.TryGetProperty("memory",out var sizes)?Memory(sizes):LogMemory(Path.Combine(dir,"firmware","compiler.log"));
   result.Add(Make(ResolveInside(dir,Get(j,"hex")),"Project release",Get(j,"version"),Date(b,"created",m),"Compiled",Get(j,"sha256"),mem.flash,mem.ram,Get(j,"build_id"),System.IO.Path.GetFileName(p)));
  }catch{}
  return result.OrderBy(c=>c.Project).ThenByDescending(c=>c.Date).ToList();
 }
}
