using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace InfiniteCanvas {
 public class CanvasItem {
  public string Id=Guid.NewGuid().ToString("N");
  public string Kind="text",Text="",Color="#252827",AssetId="";
  public double X,Y,Width=300,Height=180,FontSize=24;
  public bool Bold;
 }
 public class CanvasArrow { public string Id=Guid.NewGuid().ToString("N"),StartId,EndId; }
 public class CanvasDocument {
  public int Version=1;
  public string Id=Guid.NewGuid().ToString("N"),Name="未命名画布";
  public double PanX=90,PanY=100,Zoom=1;
  public List<CanvasItem> Items=new List<CanvasItem>();
  public List<CanvasArrow> Arrows=new List<CanvasArrow>();
 }
 public class Viewport {
  public double PanX,PanY,Zoom=1;
  public double WorldX(double x){return (x-PanX)/Zoom;}
  public double WorldY(double y){return (y-PanY)/Zoom;}
  public void ZoomAt(double x,double y,double z){double wx=WorldX(x),wy=WorldY(y);Zoom=Math.Max(.04,Math.Min(8,z));PanX=x-wx*Zoom;PanY=y-wy*Zoom;}
 }
 public static class Json {
  public static JavaScriptSerializer Serializer(){return new JavaScriptSerializer { MaxJsonLength=32*1024*1024,RecursionLimit=100 };}
  public static string Write(object value){return Serializer().Serialize(value);}
  public static T Read<T>(string text){return Serializer().Deserialize<T>(text);}
 }
 public class Session {
  public CanvasDocument Document;
  public Dictionary<string,byte[]> Assets=new Dictionary<string,byte[]>();
  public bool Dirty;
  public string FilePath;
  readonly List<string> undo=new List<string>(),redo=new List<string>();
  public Session(CanvasDocument document){Document=document;}
  public bool CanUndo {get{return undo.Count>0;}}
  public bool CanRedo {get{return redo.Count>0;}}
  public string Snapshot(){return Json.Write(Document);}
  public void Checkpoint(string before){if(before==Snapshot())return;undo.Add(before);if(undo.Count>100)undo.RemoveAt(0);redo.Clear();Dirty=true;}
  public void Change(Action action){string before=Snapshot();action();Checkpoint(before);}
  public bool Undo(){if(undo.Count==0)return false;redo.Add(Snapshot());Document=Json.Read<CanvasDocument>(undo[undo.Count-1]);undo.RemoveAt(undo.Count-1);Dirty=true;return true;}
  public bool Redo(){if(redo.Count==0)return false;undo.Add(Snapshot());Document=Json.Read<CanvasDocument>(redo[redo.Count-1]);redo.RemoveAt(redo.Count-1);Dirty=true;return true;}
 }
 public static class CanvasArchive {
  const long MaxAsset=64L*1024*1024,MaxTotal=512L*1024*1024;
  public static bool Finite(double d){return !double.IsNaN(d)&&!double.IsInfinity(d)&&Math.Abs(d)<1e12;}
  public static void Validate(CanvasDocument d,Dictionary<string,byte[]> assets){
   if(d==null||d.Version!=1||d.Items==null||d.Arrows==null||!Regex.IsMatch(d.Id??"","^[a-f0-9]{32}$")||string.IsNullOrWhiteSpace(d.Name)||d.Name.Length>120)throw new InvalidDataException("存档格式无效或版本不受支持。");
   if(d.Items.Count>10000||d.Arrows.Count>20000)throw new InvalidDataException("画布内容过多。");
   if(!Finite(d.PanX)||!Finite(d.PanY)||!Finite(d.Zoom)||d.Zoom<.04||d.Zoom>8)throw new InvalidDataException("画布视角数据无效。");
   HashSet<string> ids=new HashSet<string>();
   foreach(CanvasItem i in d.Items){
    if(i==null||string.IsNullOrEmpty(i.Id)||!ids.Add(i.Id)||!Finite(i.X)||!Finite(i.Y)||!Finite(i.Width)||!Finite(i.Height)||i.Width<20||i.Height<20||i.Width>100000||i.Height>100000||!Finite(i.FontSize)||i.FontSize<8||i.FontSize>200)throw new InvalidDataException("卡片尺寸或坐标无效。");
    if(i.Kind!="text"&&i.Kind!="image")throw new InvalidDataException("不支持的卡片类型。");
    if(i.Text==null||i.Text.Length>1000000||!Regex.IsMatch(i.Color??"","^#[0-9a-fA-F]{6}$"))throw new InvalidDataException("文本格式无效。");
    if(i.Kind=="image"&&(!Regex.IsMatch(i.AssetId??"","^[a-f0-9]{32}$")||!assets.ContainsKey(i.AssetId)||assets[i.AssetId].Length>MaxAsset))throw new InvalidDataException("存档缺少图片，或图片超过64MB。");
   }
   HashSet<string> arrows=new HashSet<string>();
   foreach(CanvasArrow a in d.Arrows)if(a==null||!arrows.Add(a.Id)||!ids.Contains(a.StartId)||!ids.Contains(a.EndId)||a.StartId==a.EndId)throw new InvalidDataException("连线端点无效。");
   long total=0;foreach(string id in d.Items.Where(i=>i.Kind=="image").Select(i=>i.AssetId).Distinct()){total+=assets[id].LongLength;if(total>MaxTotal)throw new InvalidDataException("存档图片超过512MB，请拆分为多个画布。");}
  }
  public static void Save(string path,CanvasDocument document,Dictionary<string,byte[]> assets){
   Validate(document,assets);
   string full=Path.GetFullPath(path);Directory.CreateDirectory(Path.GetDirectoryName(full));
   string temp=full+"."+Guid.NewGuid().ToString("N")+".tmp";
   try {
    using(FileStream f=new FileStream(temp,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None)){
     using(ZipArchive zip=new ZipArchive(f,ZipArchiveMode.Create,true)){
      using(StreamWriter w=new StreamWriter(zip.CreateEntry("document.json").Open(),new UTF8Encoding(false)))w.Write(Json.Write(document));
      foreach(string id in document.Items.Where(i=>i.Kind=="image").Select(i=>i.AssetId).Distinct())using(Stream s=zip.CreateEntry("assets/"+id,CompressionLevel.Fastest).Open()){byte[] data=assets[id];s.Write(data,0,data.Length);}
     }
     f.Flush(true);
    }
    if(File.Exists(full))File.Replace(temp,full,full+".bak",true);else File.Move(temp,full);
   } finally {if(File.Exists(temp))File.Delete(temp);}
  }
  static byte[] ReadEntry(ZipArchiveEntry e,long max){
   if(e.Length>max)throw new InvalidDataException("存档内容过大。");
   using(Stream s=e.Open())using(MemoryStream m=new MemoryStream()){
    byte[] buffer=new byte[32768];int n;long total=0;
    while((n=s.Read(buffer,0,buffer.Length))>0){total+=n;if(total>max)throw new InvalidDataException("存档内容过大。");m.Write(buffer,0,n);}return m.ToArray();
   }
  }
  public static CanvasDocument ReadHeader(string path){
   using(FileStream f=File.OpenRead(path))using(ZipArchive zip=new ZipArchive(f,ZipArchiveMode.Read)){
    ZipArchiveEntry entry=zip.GetEntry("document.json");if(entry==null)throw new InvalidDataException("缺少画布信息。");
    CanvasDocument d=Json.Read<CanvasDocument>(Encoding.UTF8.GetString(ReadEntry(entry,32*1024*1024)));
    if(d==null||!Regex.IsMatch(d.Id??"","^[a-f0-9]{32}$")||d.Version!=1||string.IsNullOrWhiteSpace(d.Name))throw new InvalidDataException("存档格式无效。");return d;
   }
  }
  public static Session Load(string path){
   using(FileStream f=File.OpenRead(path))using(ZipArchive zip=new ZipArchive(f,ZipArchiveMode.Read)){
    if(zip.Entries.Count>10002)throw new InvalidDataException("存档内容过多。");
    ZipArchiveEntry entry=zip.GetEntry("document.json");if(entry==null)throw new InvalidDataException("缺少画布信息。");
    CanvasDocument d=Json.Read<CanvasDocument>(Encoding.UTF8.GetString(ReadEntry(entry,32*1024*1024)));
    if(d==null||d.Items==null)throw new InvalidDataException("存档格式无效。");
    Session result=new Session(d);result.FilePath=Path.GetFullPath(path);long total=0;
    foreach(string id in d.Items.Where(i=>i!=null&&i.Kind=="image").Select(i=>i.AssetId).Distinct()){
     if(!Regex.IsMatch(id??"","^[a-f0-9]{32}$"))throw new InvalidDataException("图片编号无效。");
     ZipArchiveEntry image=zip.GetEntry("assets/"+id);if(image==null)throw new InvalidDataException("缺少内嵌图片。");total+=image.Length;if(total>MaxTotal)throw new InvalidDataException("存档图片超过512MB。");result.Assets[id]=ReadEntry(image,MaxAsset);
    }
    Validate(d,result.Assets);return result;
   }
  }
  public static Session LoadWithBackup(string path,out bool recovered){
   recovered=false;try{return Load(path);}catch(Exception original){
    if(!File.Exists(path+".bak"))throw;
    try{Session s=Load(path+".bak");s.FilePath=Path.GetFullPath(path);s.Dirty=true;recovered=true;return s;}catch{throw new InvalidDataException("存档和备份均无法读取。",original);}
   }
  }
 }
 public class ItemSize {public double Width,Height;}
 public static class ResizeMath {
  public static ItemSize Resize(double startWidth,double startHeight,double width,double height,bool image){
   if(!CanvasArchive.Finite(width))width=startWidth;if(!CanvasArchive.Finite(height))height=startHeight;
   if(!image)return new ItemSize{Width=Math.Max(60,Math.Min(100000,width)),Height=Math.Max(70,Math.Min(100000,height))};
   double ratio=startWidth/startHeight,min=Math.Max(20,20*ratio),max=Math.Min(100000,100000*ratio);
   double w=Math.Max(min,Math.Min(max,width));return new ItemSize{Width=w,Height=w/ratio};
  }
 }
 public class RecoveryEntry {public string Path,Name;public DateTime Modified;public override string ToString(){return Name+"  ·  "+Modified.ToLocalTime().ToString("MM-dd HH:mm");}}
 public static class RecoveryCatalog {
  public static List<RecoveryEntry> Find(string recoveryDirectory,IEnumerable<string> formalPaths){
   Dictionary<string,DateTime> saved=new Dictionary<string,DateTime>();
   foreach(string p in formalPaths.Distinct(StringComparer.OrdinalIgnoreCase)){
    try{CanvasDocument d=CanvasArchive.ReadHeader(p);DateTime time=File.GetLastWriteTimeUtc(p);if(!saved.ContainsKey(d.Id)||time>saved[d.Id])saved[d.Id]=time;}catch{}
   }
   List<RecoveryEntry> result=new List<RecoveryEntry>();
   foreach(string p in Directory.GetFiles(recoveryDirectory,"*.canvas").OrderByDescending(File.GetLastWriteTimeUtc)){
    try{CanvasDocument d=CanvasArchive.ReadHeader(p);DateTime time=File.GetLastWriteTimeUtc(p);if(saved.ContainsKey(d.Id)&&saved[d.Id]>=time)continue;result.Add(new RecoveryEntry{Path=p,Name=d.Name,Modified=time});}catch{}
   }
   return result;
  }
 }
}
