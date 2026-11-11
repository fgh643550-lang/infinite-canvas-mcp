using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Runtime.InteropServices;

namespace InfiniteCanvas {
 public class AiController {
  [DllImport("user32.dll")]static extern bool IsWindowEnabled(IntPtr handle);
  readonly MainWindow window;
  CanvasBoard Board {get{return window.Board;}}
  Session Current {get{return Board.Session;}}
  string ArchiveRoot {get{return Path.Combine(window.DataRoot,"存档");}}
  public AiController(MainWindow owner){window=owner;}
  public static string Str(IDictionary<string,object> a,string key,string fallback){object v;if(!a.TryGetValue(key,out v))return fallback;if(!(v is string))throw new ArgumentException(key+" must be a string");return (string)v;}
  static double Num(IDictionary<string,object> a,string key,double fallback){object v;if(!a.TryGetValue(key,out v))return fallback;if(v==null||v is bool||v is string)throw new ArgumentException(key+" must be a number");double n=Convert.ToDouble(v);if(!CanvasArchive.Finite(n))throw new ArgumentException("Invalid number: "+key);return n;}
  static bool Flag(IDictionary<string,object> a,string key,bool fallback){object v;if(!a.TryGetValue(key,out v))return fallback;if(!(v is bool))throw new ArgumentException(key+" must be boolean");return (bool)v;}
  string Revision(){using(SHA256 sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Current.Snapshot()))).Replace("-","").ToLowerInvariant();}
  void CheckRevision(IDictionary<string,object> a){if(a.ContainsKey("expected_revision")&&Str(a,"expected_revision",null)!=Revision())throw new InvalidOperationException("Revision conflict / 版本冲突：请重新读取画布。");}
  object State(){return new {document=Json.Read<CanvasDocument>(Current.Snapshot()),revision=Revision(),dirty=Current.Dirty,selected_id=Board.SelectedId,filename=Current.FilePath==null?null:Path.GetFileName(Current.FilePath)};}
  void CanSwitch(){if(Current.Dirty)throw new InvalidOperationException("Unsaved changes / 当前画布未保存，请先调用 canvas_save。");}
  string FileInArchive(string filename){
   if(string.IsNullOrWhiteSpace(filename)||filename.Length>150||filename!=Path.GetFileName(filename)||filename.IndexOfAny(Path.GetInvalidFileNameChars())>=0||!filename.EndsWith(".canvas",StringComparison.OrdinalIgnoreCase)||filename.Trim()!=filename||filename.TrimEnd('.')!=filename)throw new ArgumentException("Use a single safe .canvas filename, without directories.");
   return Path.Combine(ArchiveRoot,filename);
  }
  void Refresh(){Board.Notify();window.RefreshLibrary();}
  public object Call(string name,Dictionary<string,object> a){
   AiTools.ValidateArguments(name,a);
   IntPtr handle=new WindowInteropHelper(window).Handle;
   if(!window.IsEnabled||ComponentDispatcher.IsThreadModal||(handle!=IntPtr.Zero&&!IsWindowEnabled(handle)))throw new InvalidOperationException("A dialog is open in the canvas. Close it before retrying.");
   if(Board.IsInteracting||Board.Editor!=null)throw new InvalidOperationException("Canvas is being edited by the user; retry after the gesture or text edit ends.");
   if(name=="canvas_get")return State();
   if(name=="canvas_list"){
    var entries=new List<object>();foreach(string p in Directory.GetFiles(ArchiveRoot,"*.canvas")){try{var d=CanvasArchive.ReadHeader(p);entries.Add(new {filename=Path.GetFileName(p),name=d.Name,id=d.Id});}catch{}}
    return new {archives=entries};
   }
   if(name=="canvas_new"){
    CanSwitch();var d=new CanvasDocument{Name=Str(a,"name",null)};CanvasArchive.Validate(d,new Dictionary<string,byte[]>());Board.SetSession(new Session(d));Refresh();return State();
   }
   if(name=="canvas_open"){
    CanSwitch();Session loaded=CanvasArchive.Load(FileInArchive(Str(a,"filename",null)));Board.SetSession(loaded);Refresh();return State();
   }
   if(name=="canvas_preview"){
    Board.UpdateLayout();int w=(int)Math.Max(1,Math.Min(2560,Board.ActualWidth)),h=(int)Math.Max(1,Math.Min(1600,Board.ActualHeight));
    RenderTargetBitmap bitmap=new RenderTargetBitmap(w,h,96,96,PixelFormats.Pbgra32);bitmap.Render(Board);
    return new {mimeType="image/png",data=Convert.ToBase64String(CanvasBoard.PngBytes(bitmap))};
   }
   CheckRevision(a);
   if(name=="canvas_save"){
    string path=Current.FilePath;string filename=Str(a,"filename",null);
    if(filename!=null)path=FileInArchive(filename);
    if(path==null)path=FileInArchive(Current.Document.Id+".canvas");
    // API only writes into its archive root, including after a user opens an external file.
    if(!string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)),Path.GetFullPath(ArchiveRoot),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Choose filename to save a copy into the archive folder.");
    if(File.Exists(path)&&!string.Equals(Path.GetFullPath(path),Current.FilePath,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Archive already exists; open it first or choose another filename.");
    CanvasArchive.Save(path,Current.Document,Current.Assets);Current.FilePath=Path.GetFullPath(path);Current.Dirty=false;Refresh();return State();
   }
   if(name=="canvas_apply")return Apply(a);
   if(name=="canvas_undo"||name=="canvas_redo"){
    bool changed=name=="canvas_undo"?Current.Undo():Current.Redo();if(changed){Board.SetSelection(null);Refresh();}return State();
   }
   if(name=="canvas_view"){
    var d=Json.Read<CanvasDocument>(Current.Snapshot());d.PanX=Num(a,"pan_x",d.PanX);d.PanY=Num(a,"pan_y",d.PanY);d.Zoom=Num(a,"zoom",d.Zoom);CanvasArchive.Validate(d,Current.Assets);
    Current.Document=d;if(Flag(a,"fit",false))Board.Fit();else Board.Refresh();return State();
   }
   throw new ArgumentException("Unknown tool: "+name);
  }
  object Apply(Dictionary<string,object> a){
   object raw;if(!a.TryGetValue("operations",out raw)||!(raw is IList))throw new ArgumentException("operations must be an array");
   IList operations=(IList)raw;if(operations.Count<1||operations.Count>200)throw new ArgumentException("Use 1..200 operations.");
   CanvasDocument doc=Json.Read<CanvasDocument>(Current.Snapshot());var assets=new Dictionary<string,byte[]>(Current.Assets);var ids=new List<string>();
   foreach(object value in operations){
    var op=value as Dictionary<string,object>;if(op==null)throw new ArgumentException("Operation must be an object");
    string kind=Str(op,"op",null),id=Str(op,"id",null);
    if(kind=="add_text"||kind=="add_image"){
     if(id==null)id=Guid.NewGuid().ToString("N");var item=new CanvasItem{Id=id};SetFields(item,op);
     if(kind=="add_image"){
      string data=Str(op,"data_base64",null);if(data==null||data.Length>16*1024*1024)throw new ArgumentException("Image base64 is missing or exceeds 16 MB.");
      byte[] bytes=Convert.FromBase64String(data);BitmapSource check;
      using(var stream=new MemoryStream(bytes)){var decoder=BitmapDecoder.Create(stream,BitmapCreateOptions.DelayCreation,BitmapCacheOption.OnLoad);check=decoder.Frames[0];if(check.PixelWidth<1||check.PixelHeight<1||((long)check.PixelWidth*check.PixelHeight)>100000000)throw new ArgumentException("Invalid or oversized image dimensions.");}
      item.Kind="image";item.AssetId=Guid.NewGuid().ToString("N");assets[item.AssetId]=bytes;
      if(!op.ContainsKey("width"))item.Width=Math.Max(20,Math.Min(520,check.PixelWidth));
      if(!op.ContainsKey("height"))item.Height=Math.Max(20,Math.Min(100000,item.Width*check.PixelHeight/check.PixelWidth));
     }
     doc.Items.Add(item);ids.Add(id);
    }else if(kind=="update"){
     CanvasItem item=doc.Items.FirstOrDefault(i=>i.Id==id);if(item==null)throw new ArgumentException("Unknown item: "+id);SetFields(item,op);ids.Add(id);
    }else if(kind=="delete"){
     if(doc.Items.RemoveAll(i=>i.Id==id)==0)throw new ArgumentException("Unknown item: "+id);doc.Arrows.RemoveAll(ar=>ar.StartId==id||ar.EndId==id);
    }else if(kind=="connect"){
     if(id==null)id=Guid.NewGuid().ToString("N");doc.Arrows.Add(new CanvasArrow{Id=id,StartId=Str(op,"start_id",null),EndId=Str(op,"end_id",null)});ids.Add(id);
    }else if(kind=="disconnect"){
     if(doc.Arrows.RemoveAll(ar=>ar.Id==id)==0)throw new ArgumentException("Unknown connector: "+id);
    }else if(kind=="rename")doc.Name=Str(op,"text",null);
    else throw new ArgumentException("Unknown operation: "+kind);
   }
   CanvasArchive.Validate(doc,assets);Json.Write(doc); // Enforce serializer budget before touching live state or history.
   string before=Current.Snapshot();Current.Document=doc;Current.Assets=assets;Current.Checkpoint(before);Board.SetSelection(null);Refresh();
   return new {revision=Revision(),ids=ids,dirty=Current.Dirty};
  }
  static void SetFields(CanvasItem i,Dictionary<string,object> a){
   i.Text=Str(a,"text",i.Text);i.Color=Str(a,"color",i.Color);i.Bold=Flag(a,"bold",i.Bold);
   i.X=Num(a,"x",i.X);i.Y=Num(a,"y",i.Y);i.Width=Num(a,"width",i.Width);i.Height=Num(a,"height",i.Height);i.FontSize=Num(a,"font_size",i.FontSize);
  }
 }
}
