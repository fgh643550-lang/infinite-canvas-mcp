using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace InfiniteCanvas {
 public class CanvasBoard : Grid {
  public Session Session;
  public string SelectedId,LinkStart;
  public bool LinkMode;
  public Action Changed,SelectionChanged,ViewChanged;
  public readonly CanvasSurface Surface;
  readonly Canvas overlay=new Canvas();
  readonly Dictionary<string,BitmapSource> images=new Dictionary<string,BitmapSource>();
  public TextBox Editor {get;private set;}
  CanvasItem edited;string editBefore;
  bool drag,pan,resize;Point down;double startX,startY,startW,startH,startPanX,startPanY;string before;
  public bool IsInteracting {get{return drag||pan||resize;}}
  public CanvasBoard(){
   ClipToBounds=true;Focusable=true;Background=Brushes.Transparent;
   Surface=new CanvasSurface(this);Children.Add(Surface);overlay.IsHitTestVisible=true;Children.Add(overlay);
   Surface.MouseDown+=Down;Surface.MouseMove+=Move;Surface.MouseUp+=Up;
   Surface.LostMouseCapture+=delegate {if(drag||pan||resize)FinishGesture();};
   PreviewMouseWheel+=delegate(object sender,MouseWheelEventArgs e){Point p=e.GetPosition(this);ZoomAt(p,Zoom*Math.Pow(1.14,e.Delta/120.0));e.Handled=true;};
   SizeChanged+=delegate{Refresh();};
  }
  public double Zoom {get{return Session==null?1:Session.Document.Zoom;}}
  public Rect ItemRect(CanvasItem i){return new Rect(i.X,i.Y,i.Width,i.Height);}
  public CanvasItem Selected {get{return Session==null?null:Session.Document.Items.FirstOrDefault(i=>i.Id==SelectedId);}}
  public Point World(Point p){return new Point((p.X-Session.Document.PanX)/Zoom,(p.Y-Session.Document.PanY)/Zoom);}
  public Point Screen(Point p){return new Point(p.X*Zoom+Session.Document.PanX,p.Y*Zoom+Session.Document.PanY);}
  public Rect VisibleWorld {get{return Session==null?new Rect(0,0,1000,700):new Rect(World(new Point(0,0)),new Size(Math.Max(1,ActualWidth)/Zoom,Math.Max(1,ActualHeight)/Zoom));}}
  public void SetSession(Session s){CommitEdit();Session=s;SelectedId=null;LinkStart=null;images.Clear();Refresh();}
  public void Refresh(){Surface.InvalidateVisual();PlaceEditor();if(ViewChanged!=null)ViewChanged();}
  public void Notify(){Refresh();if(Changed!=null)Changed();}
  public void SetSelection(string id){SelectedId=id;Refresh();if(SelectionChanged!=null)SelectionChanged();}
  public void ZoomAt(Point p,double z){if(Session==null)return;Viewport v=new Viewport{PanX=Session.Document.PanX,PanY=Session.Document.PanY,Zoom=Zoom};v.ZoomAt(p.X,p.Y,z);Session.Document.Zoom=v.Zoom;Session.Document.PanX=v.PanX;Session.Document.PanY=v.PanY;Refresh();}
  public void CenterAt(Point p){Session.Document.PanX=ActualWidth/2-p.X*Zoom;Session.Document.PanY=ActualHeight/2-p.Y*Zoom;Refresh();}
  public void Fit(){CommitEdit();if(Session.Document.Items.Count==0){Session.Document.Zoom=1;CenterAt(new Point(0,0));return;}Rect b=ContentBounds();double z=Math.Min((ActualWidth-100)/Math.Max(1,b.Width),(ActualHeight-100)/Math.Max(1,b.Height));Session.Document.Zoom=Math.Max(.04,Math.Min(2,z));CenterAt(new Point(b.X+b.Width/2,b.Y+b.Height/2));}
  public Rect ContentBounds(){Rect b=Rect.Empty;foreach(CanvasItem i in Session.Document.Items)b.Union(ItemRect(i));return b.IsEmpty?new Rect(-300,-200,600,400):b;}
  public CanvasItem Hit(Point p){for(int k=Session.Document.Items.Count-1;k>=0;k--)if(ItemRect(Session.Document.Items[k]).Contains(p))return Session.Document.Items[k];return null;}
  public BitmapSource ImageFor(string id){
   if(images.ContainsKey(id))return images[id];
   byte[] data;if(!Session.Assets.TryGetValue(id,out data))return null;
   try{using(MemoryStream stream=new MemoryStream(data)){BitmapDecoder decoder=BitmapDecoder.Create(stream,BitmapCreateOptions.DelayCreation,BitmapCacheOption.OnLoad);int width=decoder.Frames[0].PixelWidth;stream.Position=0;BitmapImage image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.DecodePixelWidth=Math.Min(4096,width);image.StreamSource=stream;image.EndInit();image.Freeze();images[id]=image;return image;}}
   catch{return null;}
  }
  public static byte[] PngBytes(BitmapSource source){PngBitmapEncoder enc=new PngBitmapEncoder();enc.Frames.Add(BitmapFrame.Create(source));using(MemoryStream s=new MemoryStream()){enc.Save(s);return s.ToArray();}}
  public void AddImage(byte[] bytes,Point where){
   if(bytes.Length>64*1024*1024)throw new InvalidDataException("单张图片不能超过64MB。");
   BitmapSource check;using(MemoryStream ms=new MemoryStream(bytes)){BitmapDecoder dec=BitmapDecoder.Create(ms,BitmapCreateOptions.DelayCreation,BitmapCacheOption.OnLoad);check=dec.Frames[0];if(check.PixelWidth<1||check.PixelHeight<1)throw new InvalidDataException("无法读取图片。");}
   double w=Math.Min(520,Math.Max(120,check.PixelWidth));double h=w*check.PixelHeight/check.PixelWidth;
   if(h>650){w*=650/h;h=650;}
   if(w<20||h<20)throw new InvalidDataException("图片比例过于狭长，请先裁剪。");
   CanvasItem item=new CanvasItem{Kind="image",AssetId=Guid.NewGuid().ToString("N"),X=where.X,Y=where.Y,Width=w,Height=h};
   Session.Assets[item.AssetId]=bytes;Session.Change(delegate{Session.Document.Items.Add(item);});SetSelection(item.Id);Notify();
  }
  public CanvasItem AddText(string text,Point where){CommitEdit();CanvasItem i=new CanvasItem{X=where.X,Y=where.Y,Text=text,Width=320,Height=180};Session.Change(delegate{Session.Document.Items.Add(i);});SetSelection(i.Id);Notify();return i;}
  public void BeginEdit(CanvasItem item){
   if(item==null||item.Kind!="text")return;CommitEdit();edited=item;editBefore=Session.Snapshot();SetSelection(item.Id);
   Editor=new TextBox{Text=item.Text,AcceptsReturn=true,AcceptsTab=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,BorderThickness=new Thickness(0),Padding=new Thickness(14),Background=Brushes.White,FontFamily=new FontFamily("Microsoft YaHei UI"),FontSize=item.FontSize*Zoom,FontWeight=item.Bold?FontWeights.Bold:FontWeights.Normal,Foreground=Paint(item.Color)};
   overlay.Children.Add(Editor);PlaceEditor();
   Editor.TextChanged+=delegate{if(edited!=null){edited.Text=Editor.Text;Session.Dirty=true;Notify();}};
   Editor.LostKeyboardFocus+=delegate{if(Editor!=null&&!Editor.IsKeyboardFocusWithin)CommitEdit();};
   Editor.Focus();Editor.CaretIndex=Editor.Text.Length;
  }
  public void CommitEdit(){if(Editor==null)return;TextBox old=Editor;Editor=null;CanvasItem item=edited;edited=null;if(item!=null)item.Text=old.Text;overlay.Children.Remove(old);Session.Checkpoint(editBefore);if(Changed!=null)Changed();Surface.InvalidateVisual();}
  void PlaceEditor(){if(Editor==null||edited==null)return;Point p=Screen(new Point(edited.X,edited.Y));Canvas.SetLeft(Editor,p.X);Canvas.SetTop(Editor,p.Y);Editor.Width=Math.Max(20,edited.Width*Zoom);Editor.Height=Math.Max(20,edited.Height*Zoom);Editor.FontSize=Math.Max(1,edited.FontSize*Zoom);Editor.Padding=new Thickness(14*Zoom);}
  void Down(object sender,MouseButtonEventArgs e){
   if(Session==null)return;CommitEdit();Focus();Point p=e.GetPosition(this);Point w=World(p);CanvasItem hit=Hit(w);
   if(e.ChangedButton==MouseButton.Right)return;
   if(e.ChangedButton!=MouseButton.Left&&e.ChangedButton!=MouseButton.Middle)return;
   if(e.ChangedButton==MouseButton.Middle||Keyboard.IsKeyDown(Key.Space))pan=true;
   else if(LinkMode){if(hit!=null){if(LinkStart==null)LinkStart=hit.Id;else if(LinkStart!=hit.Id){string start=LinkStart;Session.Change(delegate{Session.Document.Arrows.Add(new CanvasArrow{StartId=start,EndId=hit.Id});});LinkStart=null;Notify();}}Refresh();e.Handled=true;return;}
   else if(hit!=null){SetSelection(hit.Id);if(e.ClickCount==2&&hit.Kind=="text"){BeginEdit(hit);e.Handled=true;return;}Point corner=Screen(new Point(hit.X+hit.Width,hit.Y+hit.Height));resize=Math.Abs(p.X-corner.X)<15&&Math.Abs(p.Y-corner.Y)<15;drag=!resize;startX=hit.X;startY=hit.Y;startW=hit.Width;startH=hit.Height;}
   else{SetSelection(null);CanvasArrow arrow=ArrowHit(w);if(arrow!=null){SetSelection(arrow.Id);e.Handled=true;return;}pan=true;}
   down=p;startPanX=Session.Document.PanX;startPanY=Session.Document.PanY;before=Session.Snapshot();Surface.CaptureMouse();e.Handled=true;
  }
  void Move(object sender,MouseEventArgs e){
   if(Session==null)return;Point p=e.GetPosition(this);CanvasItem item=Selected;
   if(pan){Session.Document.PanX=startPanX+p.X-down.X;Session.Document.PanY=startPanY+p.Y-down.Y;Refresh();}
   else if((drag||resize)&&item!=null){double dx=(p.X-down.X)/Zoom,dy=(p.Y-down.Y)/Zoom;if(drag){item.X=startX+dx;item.Y=startY+dy;}else{ItemSize size=ResizeMath.Resize(startW,startH,startW+dx,startH+dy,item.Kind=="image");item.Width=size.Width;item.Height=size.Height;}Refresh();}
   else{Point w=World(p);CanvasItem h=Hit(w);if(h!=null){Point c=Screen(new Point(h.X+h.Width,h.Y+h.Height));Surface.Cursor=(Math.Abs(c.X-p.X)<15&&Math.Abs(c.Y-p.Y)<15)?Cursors.SizeNWSE:Cursors.Arrow;}else Surface.Cursor=Cursors.Hand;}
  }
  void Up(object sender,MouseButtonEventArgs e){if(drag||resize||pan){FinishGesture();Surface.ReleaseMouseCapture();e.Handled=true;}}
  void FinishGesture(){if(drag||resize){Session.Checkpoint(before);if(Changed!=null)Changed();}drag=resize=pan=false;Refresh();}
  public void DeleteSelection(){CommitEdit();string id=SelectedId;if(id==null)return;Session.Change(delegate{Session.Document.Items.RemoveAll(i=>i.Id==id);Session.Document.Arrows.RemoveAll(a=>a.Id==id||a.StartId==id||a.EndId==id);});SetSelection(null);Notify();}
  public void DuplicateSelection(){CommitEdit();CanvasItem i=Selected;if(i==null)return;CanvasItem copy=Json.Read<CanvasItem>(Json.Write(i));copy.Id=Guid.NewGuid().ToString("N");copy.X+=35;copy.Y+=35;Session.Change(delegate{Session.Document.Items.Add(copy);});SetSelection(copy.Id);Notify();}
  public void Undo(){CommitEdit();if(Session.Undo()){SelectedId=null;LinkStart=null;Notify();if(SelectionChanged!=null)SelectionChanged();}}
  public void Redo(){CommitEdit();if(Session.Redo()){SelectedId=null;Notify();if(SelectionChanged!=null)SelectionChanged();}}
  public void BringForward(){CanvasItem i=Selected;if(i==null)return;Session.Change(delegate{Session.Document.Items.Remove(i);Session.Document.Items.Add(i);});Notify();}
  CanvasArrow ArrowHit(Point p){foreach(CanvasArrow a in Session.Document.Arrows.AsEnumerable().Reverse()){Point start,end;if(!ArrowEnds(a,out start,out end))continue;Vector d=end-start;double t=d.LengthSquared==0?0:Math.Max(0,Math.Min(1,Vector.Multiply(p-start,d)/d.LengthSquared));if((p-(start+d*t)).Length<7/Zoom)return a;}return null;}
  static Point Edge(CanvasItem i,Point toward){Point center=new Point(i.X+i.Width/2,i.Y+i.Height/2);Vector d=toward-center;if(d.Length<.001)return center;double t=1/Math.Max(Math.Abs(d.X)/(i.Width/2),Math.Abs(d.Y)/(i.Height/2));return center+d*t;}
  public bool ArrowEnds(CanvasArrow a,out Point start,out Point end){CanvasItem x=Session.Document.Items.FirstOrDefault(i=>i.Id==a.StartId),y=Session.Document.Items.FirstOrDefault(i=>i.Id==a.EndId);start=end=new Point();if(x==null||y==null)return false;start=Edge(x,new Point(y.X+y.Width/2,y.Y+y.Height/2));end=Edge(y,new Point(x.X+x.Width/2,x.Y+x.Height/2));return true;}
  public static Brush Paint(string color){return (Brush)new BrushConverter().ConvertFromString(color);}
  public static FormattedText Text(string value,double size,Brush brush,bool bold){return new FormattedText(value??"",CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface(new FontFamily("Microsoft YaHei UI"),FontStyles.Normal,bold?FontWeights.Bold:FontWeights.Normal,FontStretches.Normal),size,brush);}
 }
 public class CanvasSurface:FrameworkElement {
  readonly CanvasBoard board;
  public CanvasSurface(CanvasBoard b){board=b;Focusable=true;}
  protected override void OnRender(DrawingContext dc){
   dc.DrawRectangle(CanvasBoard.Paint("#F4F3F1"),null,new Rect(RenderSize));Session s=board.Session;if(s==null)return;
   dc.PushClip(new RectangleGeometry(new Rect(RenderSize)));
   dc.PushTransform(new MatrixTransform(board.Zoom,0,0,board.Zoom,s.Document.PanX,s.Document.PanY));
   foreach(CanvasArrow a in s.Document.Arrows){Point p,q;if(!board.ArrowEnds(a,out p,out q))continue;Brush brush=CanvasBoard.Paint(a.Id==board.SelectedId?"#1D5FBF":"#737B7C");Pen pen=new Pen(brush,2/board.Zoom);dc.DrawLine(pen,p,q);Vector d=q-p;if(d.Length>1){d.Normalize();Vector n=new Vector(-d.Y,d.X);Point root=q-d*(12/board.Zoom);StreamGeometry g=new StreamGeometry();using(StreamGeometryContext c=g.Open()){c.BeginFigure(q,true,true);c.LineTo(root+n*(5/board.Zoom),true,false);c.LineTo(root-n*(5/board.Zoom),true,false);}dc.DrawGeometry(brush,null,g);}}
   Rect visible=board.VisibleWorld;visible.Inflate(30/board.Zoom,30/board.Zoom);
   foreach(CanvasItem i in s.Document.Items){Rect r=board.ItemRect(i);if(!visible.IntersectsWith(r))continue;
    dc.DrawRoundedRectangle(Brushes.White,new Pen(CanvasBoard.Paint("#DADDD9"),1/board.Zoom),r,5,5);
    if(i.Kind=="image"){BitmapSource image=board.ImageFor(i.AssetId);if(image!=null)dc.DrawImage(image,r);else dc.DrawText(CanvasBoard.Text("图片无法显示",18,Brushes.Gray,false),new Point(r.X+15,r.Y+15));}
    else{FormattedText t=CanvasBoard.Text(string.IsNullOrEmpty(i.Text)?"双击编辑文字":i.Text,i.FontSize,string.IsNullOrEmpty(i.Text)?Brushes.Gray:CanvasBoard.Paint(i.Color),i.Bold);t.MaxTextWidth=Math.Max(1,r.Width-28);t.MaxTextHeight=Math.Max(1,r.Height-28);dc.PushClip(new RectangleGeometry(r));dc.DrawText(t,new Point(r.X+14,r.Y+14));dc.Pop();}
    if(i.Id==board.SelectedId||i.Id==board.LinkStart){dc.DrawRoundedRectangle(null,new Pen(CanvasBoard.Paint("#1D5FBF"),1.8/board.Zoom),r,5,5);dc.DrawRectangle(Brushes.White,new Pen(CanvasBoard.Paint("#1D5FBF"),1/board.Zoom),new Rect(r.Right-5/board.Zoom,r.Bottom-5/board.Zoom,10/board.Zoom,10/board.Zoom));}
   }
   dc.Pop();
   if(s.Document.Items.Count==0){FormattedText title=CanvasBoard.Text("把想法放在这里",27,CanvasBoard.Paint("#414744"),false);dc.DrawText(title,new Point(Math.Max(30,(ActualWidth-title.Width)/2),Math.Max(60,ActualHeight/2-60)));FormattedText help=CanvasBoard.Text("拖入图片 · 粘贴截图 · 添加文字\n滚轮缩放，拖动空白处移动画布",14,CanvasBoard.Paint("#858B86"),false);help.TextAlignment=TextAlignment.Center;help.MaxTextWidth=450;dc.DrawText(help,new Point(Math.Max(20,(ActualWidth-450)/2),Math.Max(110,ActualHeight/2-12)));}
   dc.Pop();
  }
 }
 public class MiniMap:FrameworkElement {
  readonly CanvasBoard board;Rect bounds,dragBounds;double scale,ox,oy,dragScale,dragOx,dragOy;bool dragging;
  public MiniMap(CanvasBoard b){board=b;Width=176;Height=120;Cursor=Cursors.Cross;
   MouseDown+=delegate(object sender,MouseButtonEventArgs e){if(e.ChangedButton!=MouseButton.Left)return;dragging=true;dragBounds=bounds;dragScale=scale;dragOx=ox;dragOy=oy;CaptureMouse();Navigate(e.GetPosition(this));e.Handled=true;};
   MouseMove+=delegate(object sender,MouseEventArgs e){if(dragging)Navigate(e.GetPosition(this));};
   MouseUp+=delegate{dragging=false;ReleaseMouseCapture();InvalidateVisual();};
   LostMouseCapture+=delegate{dragging=false;};
  }
  void Navigate(Point p){if(dragScale<=0)return;board.CenterAt(new Point(dragBounds.X+(p.X-dragOx)/dragScale,dragBounds.Y+(p.Y-dragOy)/dragScale));}
  public Point MapWorldPoint(Point p){return new Point(ox+(p.X-bounds.X)*scale,oy+(p.Y-bounds.Y)*scale);}
  protected override void OnRender(DrawingContext dc){
   dc.DrawRoundedRectangle(Brushes.White,new Pen(CanvasBoard.Paint("#D9DDDA"),1),new Rect(0,0,Width,Height),6,6);
   dc.DrawText(CanvasBoard.Text("导航",12,CanvasBoard.Paint("#555C58"),false),new Point(12,9));
   if(board.Session==null)return;
   bounds=dragging?dragBounds:board.ContentBounds();if(!dragging){bounds.Union(board.VisibleWorld);bounds.Inflate(90,90);}
   scale=Math.Min((Width-20)/Math.Max(1,bounds.Width),(Height-43)/Math.Max(1,bounds.Height));ox=10+(Width-20-bounds.Width*scale)/2;oy=31+(Height-43-bounds.Height*scale)/2;
   foreach(CanvasItem i in board.Session.Document.Items){Point p=MapWorldPoint(new Point(i.X,i.Y));dc.DrawRectangle(CanvasBoard.Paint(i.Kind=="image"?"#A9B9C9":"#BDC3BE"),null,new Rect(p,new Size(Math.Max(1,i.Width*scale),Math.Max(1,i.Height*scale))));}
   Rect view=board.VisibleWorld;Point v=MapWorldPoint(view.TopLeft);Rect r=new Rect(v,new Size(view.Width*scale,view.Height*scale));dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(22,29,95,191)),new Pen(CanvasBoard.Paint("#1D5FBF"),1.3),r);
  }
 }
}
