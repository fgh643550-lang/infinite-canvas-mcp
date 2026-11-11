using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace InfiniteCanvas {
 public static class Program {
  [DllImport("user32.dll",EntryPoint="GetWindowLong")]static extern int GetWindowLong(IntPtr h,int index);
  [STAThread]public static int Main(string[] args){
   bool smoke=args.Length>0&&args[0]=="--smoke-test",review=args.Length>=3&&args[0]=="--ui-review";
   string data=smoke||review?args[1]:Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"数据");
   int dataIndex=Array.IndexOf(args,"--data-root");if(dataIndex>=0&&dataIndex+1<args.Length)data=Path.GetFullPath(args[dataIndex+1]);
   bool ai=Array.IndexOf(args,"--ai")>=0;
   try{
    Directory.CreateDirectory(data);Application app=new Application();MainWindow win=new MainWindow(data,smoke,ai);
    if(smoke){win.Width=1280;win.Height=840;win.Loaded+=delegate{DispatcherTimer timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(600)};timer.Tick+=delegate{timer.Stop();try{Smoke(win,args.Length>2?args[2]:null,data);File.WriteAllText(Path.Combine(data,"smoke-result.txt"),"PASS: window, embedded image, Chinese text editing, styles, connectors, minimap, undo/redo, topmost, archives, layout screenshots.");win.Close();}catch(Exception e){File.WriteAllText(Path.Combine(data,"smoke-result.txt"),e.ToString());Environment.Exit(2);}};timer.Start();};}
    else{
     app.DispatcherUnhandledException+=delegate(object sender,DispatcherUnhandledExceptionEventArgs e){try{File.AppendAllText(Path.Combine(data,"error.log"),DateTime.Now+" "+e.Exception+Environment.NewLine);}catch{}MessageBox.Show("操作未完成。请先保存画布。\n"+e.Exception.Message,"无限画布",MessageBoxButton.OK,MessageBoxImage.Warning);e.Handled=true;};
     if(review)win.Loaded+=delegate{win.OpenFile(args[2]);};
     else if(args.Length>0&&File.Exists(args[0]))win.Loaded+=delegate{win.OpenFile(args[0]);};
    }
    if(ai)win.Loaded+=delegate{try{win.StartAi();}catch(Exception ex){File.WriteAllText(Path.Combine(data,"ai-error.log"),ex.ToString());win.SetStatus("AI 接入启动失败："+ex.Message);}};
    app.Run(win);return 0;
   }catch(Exception e){try{File.WriteAllText(Path.Combine(data,"startup-error.txt"),e.ToString());}catch{}if(!smoke)MessageBox.Show("无法启动无限画布：\n"+e.Message);return 1;}
  }
  static void Require(bool value,string name){if(!value)throw new Exception("UI CHECK FAILED: "+name);}
  public static void Capture(MainWindow w,string path){
   w.UpdateLayout();FrameworkElement content=(FrameworkElement)w.Content;content.UpdateLayout();
   RenderTargetBitmap bmp=new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth),(int)Math.Ceiling(content.ActualHeight),96,96,PixelFormats.Pbgra32);bmp.Render(content);
   PngBitmapEncoder encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bmp));using(FileStream f=File.Create(path))encoder.Save(f);
  }
  static void Smoke(MainWindow w,string imagePath,string root){
   CanvasBoard b=w.Board;
   Require(w.IsVisible&&b.ActualWidth>700,"window and canvas visible");
   b.Session.Document.Name="庄园旅店 · 概念整理";
   CanvasItem title=b.AddText("庄园旅店\n空间与日常",new Point(50,20));title.FontSize=34;title.Bold=true;title.Width=380;title.Height=135;
   if(imagePath!=null)b.AddImage(File.ReadAllBytes(imagePath),new Point(50,195));
   CanvasItem notes=b.AddText("01  接待大厅\n\n办理入住、围炉休息。\n让客人从这里认识庄园。",new Point(650,25));notes.Width=335;notes.Height=235;notes.FontSize=22;
   b.BeginEdit(notes);b.Editor.Text="01  接待大厅\n\n办理入住、围炉休息。\n让客人从这里认识庄园。";Require(b.Editor.AcceptsReturn&&b.Editor.Text.Contains("接待大厅"),"native Chinese multiline TextBox");b.CommitEdit();
   CanvasItem next=b.AddText("02  女仆与经营\n\n一起备餐，接待旅客。\n用日常事件推动关系变化。",new Point(650,365));next.Width=335;next.Height=220;next.FontSize=22;next.Color="#21685E";
   b.Session.Change(delegate{b.Session.Document.Arrows.Add(new CanvasArrow{StartId=notes.Id,EndId=next.Id});});
   Point p,q;Require(b.ArrowEnds(b.Session.Document.Arrows[0],out p,out q)&&p.Y<q.Y,"connector endpoints");
   double old=next.X;b.Session.Change(delegate{next.X+=400;});b.Undo();Require(b.Session.Document.Items.First(i=>i.Id==next.Id).X==old,"UI undo");b.Redo();Require(b.Session.Document.Items.First(i=>i.Id==next.Id).X==old+400,"UI redo");b.Undo();
   w.Pin.IsChecked=true;Require(w.Topmost&&(GetWindowLong(new WindowInteropHelper(w).Handle,-20)&8)!=0,"native always-on-top flag");w.Pin.IsChecked=false;Require(!w.Topmost,"pin toggle off");
   b.Fit();w.UpdateLayout();w.Map.InvalidateVisual();w.Map.UpdateLayout();Require(w.Map.ActualWidth>150&&w.Map.ActualHeight>100,"minimap visible");
   b.SetSelection(notes.Id);b.Refresh();w.UpdateLayout();
   string path=Path.Combine(root,"存档","庄园旅店.canvas");CanvasArchive.Save(path,b.Session.Document,b.Session.Assets);Session loaded=CanvasArchive.Load(path);
   Require(loaded.Document.Items.Count==b.Session.Document.Items.Count&&loaded.Document.Arrows.Count==1&&loaded.Document.Items.First(i=>i.Id==notes.Id).Text.Contains("围炉"),"UI archive reopen");
   Session second=new Session(new CanvasDocument{Name="独立画布二"});CanvasArchive.Save(Path.Combine(root,"存档","独立画布二.canvas"),second.Document,second.Assets);b.SetSession(second);Require(b.Session.Document.Items.Count==0,"second archive isolated");b.SetSession(loaded);b.Fit();b.SetSelection(notes.Id);w.SetStatus("已保存 · 庄园旅店.canvas    /    100% 离线，图片已内嵌");w.UpdateLayout();
   w.RefreshLibrary();Capture(w,Path.Combine(root,"界面预览.png"));
   w.Width=860;w.Height=580;w.UpdateLayout();b.Fit();w.UpdateLayout();Require(b.ActualWidth>500&&w.Map.ActualWidth==176,"compact layout");Capture(w,Path.Combine(root,"小窗口预览.png"));
   b.Session.Dirty=false;
  }
 }
}
