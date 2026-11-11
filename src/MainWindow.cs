using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace InfiniteCanvas {
 public class LibraryEntry {public string Path;public string Name;public override string ToString(){return Name;}}
 public class AppSettings {public bool Topmost;public double Width=1260,Height=820;public List<string> Recent=new List<string>();}
 public class MainWindow:Window {
  public readonly CanvasBoard Board=new CanvasBoard();
  public readonly MiniMap Map;
  public readonly ToggleButton Pin=new ToggleButton{Content="置顶",ToolTip="始终显示在其他普通窗口之上"};
  public readonly string DataRoot;
  readonly string archiveDir,recoveryDir;readonly bool testing;
  readonly TextBlock status=new TextBlock(),zoomText=new TextBlock(),documentName=new TextBlock();
  readonly ListBox library=new ListBox();readonly Border sidebar=new Border();
  readonly ComboBox fontSize=new ComboBox{Width=62,IsEditable=false};readonly ComboBox colors=new ComboBox{Width=74};
  readonly ToggleButton bold=new ToggleButton{Content="粗体"};
  readonly ToggleButton link=new ToggleButton{Content="连线"};
  readonly DispatcherTimer autosave=new DispatcherTimer();
  AppSettings settings=new AppSettings();bool syncing,ready;DateTime lastChange=DateTime.MinValue;
  string lastAutosaved="";bool reportedAutoError;
  AiServer aiServer;
  readonly Button aiButton=new Button{Content="AI：关闭",ToolTip="开启或关闭本机 AI 接入；MCP 插件可自动启动支持 AI 的画布窗口"};
  public void StartAi(){if(aiServer!=null)return;aiServer=new AiServer(this);aiButton.Content="AI：开启";SetStatus("AI 接入已开启 · "+aiServer.Url+" · 连接信息：数据/ai-connection.json");}
  void ToggleAi(){try{if(aiServer==null)StartAi();else{aiServer.Dispose();aiServer=null;aiButton.Content="AI：关闭";SetStatus("AI 接入已关闭");}}catch(Exception ex){ShowError("AI 接入启动失败",ex);}}
  public MainWindow(string dataRoot,bool test,bool deferRecovery=false){
   testing=test;DataRoot=dataRoot;archiveDir=Path.Combine(dataRoot,"存档");recoveryDir=Path.Combine(dataRoot,"自动恢复");Directory.CreateDirectory(archiveDir);Directory.CreateDirectory(recoveryDir);
   try{string p=Path.Combine(dataRoot,"settings.json");if(File.Exists(p))settings=Json.Read<AppSettings>(File.ReadAllText(p,Encoding.UTF8));}catch{settings=new AppSettings();}
   if(settings==null)settings=new AppSettings();if(settings.Recent==null)settings.Recent=new List<string>();
   Title="无限画布";Width=Math.Max(840,Math.Min(1800,settings.Width));Height=Math.Max(560,Math.Min(1100,settings.Height));MinWidth=840;MinHeight=560;WindowStartupLocation=WindowStartupLocation.CenterScreen;Background=Brushes.White;FontFamily=new FontFamily("Microsoft YaHei UI");FontSize=13;
   Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(Styles));
   Grid root=new Grid{Background=Brushes.White};root.RowDefinitions.Add(new RowDefinition{Height=new GridLength(58)});root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition());root.RowDefinitions.Add(new RowDefinition{Height=new GridLength(30)});Content=root;
   DockPanel head=new DockPanel{Margin=new Thickness(20,0,16,0),LastChildFill=true};Grid.SetRow(head,0);root.Children.Add(head);
   TextBlock brand=new TextBlock{Text="∞  无限画布",FontSize=19,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center,Width=190};head.Children.Add(brand);
   StackPanel headRight=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};DockPanel.SetDock(headRight,Dock.Right);headRight.Children.Add(Pin);headRight.Children.Add(Button("快捷键",ShowHelp));head.Children.Add(headRight);
   documentName.VerticalAlignment=VerticalAlignment.Center;documentName.FontSize=14;documentName.Foreground=CanvasBoard.Paint("#626A66");documentName.Cursor=Cursors.Hand;documentName.ToolTip="点击重命名当前画布";documentName.MouseLeftButtonDown+=delegate{Rename();};head.Children.Add(documentName);
   WrapPanel tools=new WrapPanel{Margin=new Thickness(12,6,12,6)};Border toolBorder=new Border{BorderBrush=CanvasBoard.Paint("#E6E8E4"),BorderThickness=new Thickness(0,1,0,1),Child=tools};Grid.SetRow(toolBorder,1);root.Children.Add(toolBorder);
   tools.Children.Add(Button("存档栏",delegate{sidebar.Visibility=sidebar.Visibility==Visibility.Visible?Visibility.Collapsed:Visibility.Visible;}));
   tools.Children.Add(Button("新建",NewDocument));tools.Children.Add(Button("打开",OpenDialog));
   Button save=Button("保存",delegate{Save(false);});save.Background=CanvasBoard.Paint("#272C29");save.Foreground=Brushes.White;tools.Children.Add(save);
   tools.Children.Add(Button("另存为",delegate{Save(true);}));tools.Children.Add(Button("恢复草稿",delegate{ShowRecovery(false);}));tools.Children.Add(aiButton);aiButton.Click+=delegate{ToggleAi();};AddDivider(tools);
   tools.Children.Add(Button("＋ 图片",ImportDialog));tools.Children.Add(Button("＋ 文字",delegate{CanvasItem i=Board.AddText("",NewPoint());Board.BeginEdit(i);}));
   tools.Children.Add(link);link.ToolTip="依次点击起点卡片和终点卡片；再次点击按钮退出";
   link.Checked+=delegate{Board.CommitEdit();Board.LinkMode=true;SetStatus("依次点击起点卡片与终点卡片");};link.Unchecked+=delegate{Board.LinkMode=false;Board.LinkStart=null;Board.Refresh();};
   AddDivider(tools);tools.Children.Add(Button("撤销",Board.Undo));tools.Children.Add(Button("重做",Board.Redo));tools.Children.Add(Button("删除",Board.DeleteSelection));
   AddDivider(tools);foreach(int n in new[]{12,16,20,24,28,32,40,48,64,96})fontSize.Items.Add(n);fontSize.SelectedItem=24;tools.Children.Add(fontSize);tools.Children.Add(bold);
   foreach(string name in new[]{"墨黑","深蓝","松绿","砖红","暖棕","灰色"})colors.Items.Add(name);colors.SelectedIndex=0;tools.Children.Add(colors);
   fontSize.SelectionChanged+=delegate{ApplyStyle();};colors.SelectionChanged+=delegate{ApplyStyle();};bold.Click+=delegate{ApplyStyle();};
   Grid body=new Grid();body.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});body.ColumnDefinitions.Add(new ColumnDefinition());Grid.SetRow(body,2);root.Children.Add(body);
   sidebar.Width=194;sidebar.Background=CanvasBoard.Paint("#FCFCFA");sidebar.BorderBrush=CanvasBoard.Paint("#E6E8E4");sidebar.BorderThickness=new Thickness(0,0,1,0);body.Children.Add(sidebar);
   DockPanel side=new DockPanel{Margin=new Thickness(12,16,12,12)};sidebar.Child=side;
   StackPanel sideHead=new StackPanel{Margin=new Thickness(0,0,0,12)};DockPanel.SetDock(sideHead,Dock.Top);sideHead.Children.Add(new TextBlock{Text="我的画布",FontSize=13,FontWeight=FontWeights.SemiBold,Margin=new Thickness(6,0,0,10)});sideHead.Children.Add(Button("＋ 新建独立画布",NewDocument));side.Children.Add(sideHead);
   TextBlock hint=new TextBlock{Text="每份存档独立保存图片和文字。\n双击名称打开画布。",FontSize=11,Foreground=Brushes.Gray,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(6,12,6,0)};DockPanel.SetDock(hint,Dock.Bottom);side.Children.Add(hint);
   library.BorderThickness=new Thickness(0);library.Background=Brushes.Transparent;library.FontSize=13;library.MouseDoubleClick+=delegate{LibraryEntry item=library.SelectedItem as LibraryEntry;if(item!=null)OpenFile(item.Path);};side.Children.Add(library);
   Grid scene=new Grid();Grid.SetColumn(scene,1);body.Children.Add(scene);scene.Children.Add(Board);
   Map=new MiniMap(Board){HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(16)};scene.Children.Add(Map);
   StackPanel zoomControls=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(16),Background=Brushes.White};zoomControls.Children.Add(Button("－",delegate{Board.ZoomAt(new Point(Board.ActualWidth/2,Board.ActualHeight/2),Board.Zoom/1.25);}));zoomText.Width=49;zoomText.TextAlignment=TextAlignment.Center;zoomText.VerticalAlignment=VerticalAlignment.Center;zoomControls.Children.Add(zoomText);zoomControls.Children.Add(Button("＋",delegate{Board.ZoomAt(new Point(Board.ActualWidth/2,Board.ActualHeight/2),Board.Zoom*1.25);}));zoomControls.Children.Add(Button("适合",Board.Fit));scene.Children.Add(zoomControls);
   status.FontSize=11;status.Foreground=CanvasBoard.Paint("#707873");status.VerticalAlignment=VerticalAlignment.Center;status.Margin=new Thickness(16,0,12,0);Grid.SetRow(status,3);root.Children.Add(status);
   Board.Changed=OnChanged;Board.SelectionChanged=SyncStyle;Board.ViewChanged=delegate{Map.InvalidateVisual();zoomText.Text=Math.Round(Board.Zoom*100)+"%";};
   Board.SetSession(new Session(new CanvasDocument()));
   Pin.IsChecked=settings.Topmost;Topmost=settings.Topmost;Pin.Checked+=delegate{Topmost=true;SetStatus("窗口已置顶");};Pin.Unchecked+=delegate{Topmost=false;SetStatus("已取消置顶");};
   AllowDrop=true;PreviewDragOver+=DragOverFiles;Drop+=DropFiles;PreviewKeyDown+=Keys;
   AddContextMenu();
   autosave.Interval=TimeSpan.FromSeconds(2);autosave.Tick+=delegate{AutoSave();};autosave.Start();
   Closing+=CloseRequested;
   Loaded+=delegate{ready=true;RefreshLibrary();UpdateTitle();SetStatus("滚轮缩放 · 拖动空白处平移 · 双击文字编辑 · Ctrl+V 粘贴");if(!testing&&!deferRecovery)RecoverLast();};
  }
  static Button Button(string label,Action action){Button b=new Button{Content=label};b.Click+=delegate{action();};return b;}
  static void AddDivider(Panel p){p.Children.Add(new Border{Width=1,Height=22,Background=CanvasBoard.Paint("#DDE1DB"),Margin=new Thickness(8,4,8,4)});}
  Point NewPoint(){Point p=Board.World(new Point(Board.ActualWidth/2-130,Board.ActualHeight/2-90));return new Point(p.X,p.Y);}
  void OnChanged(){lastChange=DateTime.UtcNow;reportedAutoError=false;UpdateTitle();}
  void UpdateTitle(){if(Board.Session==null)return;string mark=Board.Session.Dirty?" ●":"";documentName.Text=Board.Session.Document.Name+mark;Title=Board.Session.Document.Name+mark+" — 无限画布";}
  public void SetStatus(string text){status.Text=text;}
  void SyncStyle(){CanvasItem i=Board.Selected;if(i==null||i.Kind!="text")return;syncing=true;bold.IsChecked=i.Bold;if(!fontSize.Items.Contains((int)i.FontSize))fontSize.Items.Add((int)i.FontSize);fontSize.SelectedItem=(int)i.FontSize;string[] palette={"#252827","#1D5FBF","#21685E","#AF4D38","#84623E","#717975"};int index=Array.FindIndex(palette,c=>c.Equals(i.Color,StringComparison.OrdinalIgnoreCase));colors.SelectedIndex=Math.Max(0,index);syncing=false;}
  void ApplyStyle(){if(syncing||Board.Session==null)return;Board.CommitEdit();CanvasItem i=Board.Selected;if(i==null||i.Kind!="text")return;string[] palette={"#252827","#1D5FBF","#21685E","#AF4D38","#84623E","#717975"};Board.Session.Change(delegate{i.FontSize=fontSize.SelectedItem==null?24:Convert.ToDouble(fontSize.SelectedItem);i.Bold=bold.IsChecked==true;i.Color=palette[Math.Max(0,colors.SelectedIndex)];});Board.Notify();}
  public bool Save(bool copy){
   Board.CommitEdit();Session s=Board.Session;string path=s.FilePath;CanvasDocument doc=s.Document;
   if(copy||string.IsNullOrEmpty(path)){
    string name=AskName(this,copy?"另存为新画布":"保存画布",doc.Name);if(name==null)return false;
    CanvasDocument clone=Json.Read<CanvasDocument>(Json.Write(doc));clone.Name=name;if(copy)clone.Id=Guid.NewGuid().ToString("N");
    SaveFileDialog dialog=new SaveFileDialog{Title="保存独立画布",Filter="无限画布存档 (*.canvas)|*.canvas",DefaultExt=".canvas",InitialDirectory=archiveDir,FileName=SafeFile(name)+".canvas",OverwritePrompt=true};
    if(dialog.ShowDialog(this)!=true)return false;path=dialog.FileName;doc=clone;
   }
   try{
    CanvasArchive.Save(path,doc,s.Assets);
    if(copy){Session newSession=new Session(doc){Assets=s.Assets,FilePath=path};Board.SetSession(newSession);}else{s.Document=doc;s.FilePath=path;s.Dirty=false;}
    lastAutosaved=Board.Session.Snapshot();Remember(path);RefreshLibrary();UpdateTitle();SetStatus("已保存 · "+path);return true;
   }catch(Exception ex){ShowError("保存失败，原存档已保留。",ex);return false;}
  }
  static string SafeFile(string name){foreach(char c in Path.GetInvalidFileNameChars())name=name.Replace(c,'_');name=name.Trim().TrimEnd('.');if(name.Length>90)name=name.Substring(0,90);return name.Length==0?"画布":name;}
  void Rename(){Board.CommitEdit();string name=AskName(this,"重命名画布",Board.Session.Document.Name);if(name==null)return;Board.Session.Change(delegate{Board.Session.Document.Name=name;});Board.Notify();RefreshLibrary();}
  bool CanLeave(){Board.CommitEdit();if(!Board.Session.Dirty)return true;MessageBoxResult r=MessageBox.Show(this,"当前画布有未保存的修改。是否保存？","无限画布",MessageBoxButton.YesNoCancel,MessageBoxImage.Question);if(r==MessageBoxResult.Cancel)return false;if(r==MessageBoxResult.Yes)return Save(false);return true;}
  void NewDocument(){if(!CanLeave())return;string name=AskName(this,"新建独立画布","未命名画布");if(name==null)return;Board.SetSession(new Session(new CanvasDocument{Name=name}));lastAutosaved="";link.IsChecked=false;UpdateTitle();SetStatus("新画布已创建。添加图片或文字开始。");}
  void OpenDialog(){OpenFileDialog dialog=new OpenFileDialog{Title="打开画布",Filter="无限画布存档 (*.canvas)|*.canvas",InitialDirectory=archiveDir};if(dialog.ShowDialog(this)==true)OpenFile(dialog.FileName);}
  public void OpenFile(string path){if(!CanLeave())return;try{bool recovered;Session s=CanvasArchive.LoadWithBackup(path,out recovered);Board.SetSession(s);link.IsChecked=false;lastAutosaved="";Remember(path);RefreshLibrary();UpdateTitle();SetStatus(recovered?"原文件损坏，已恢复上一份备份。请另存为保留恢复结果。":"已打开 · "+s.Document.Name);}catch(Exception ex){ShowError("无法打开画布。",ex);}}
  void Remember(string path){settings.Recent.RemoveAll(p=>string.Equals(p,path,StringComparison.OrdinalIgnoreCase));settings.Recent.Insert(0,path);if(settings.Recent.Count>50)settings.Recent.RemoveRange(50,settings.Recent.Count-50);}
  public void RefreshLibrary(){
   if(!ready)return;library.Items.Clear();
   IEnumerable<string> files=settings.Recent.Concat(Directory.GetFiles(archiveDir,"*.canvas")).Distinct(StringComparer.OrdinalIgnoreCase).Where(File.Exists).Take(100);
   foreach(string p in files){string name=Path.GetFileNameWithoutExtension(p);try{using(FileStream fs=File.OpenRead(p))using(ZipArchive z=new ZipArchive(fs,ZipArchiveMode.Read)){ZipArchiveEntry e=z.GetEntry("document.json");if(e!=null&&e.Length<32*1024*1024)using(StreamReader r=new StreamReader(e.Open(),Encoding.UTF8))name=Json.Read<CanvasDocument>(r.ReadToEnd()).Name;}}catch{}
    library.Items.Add(new LibraryEntry{Path=p,Name=name});
   }
  }
  void ImportDialog(){OpenFileDialog d=new OpenFileDialog{Title="放置图片",Filter="图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|所有文件|*.*",Multiselect=true};if(d.ShowDialog(this)==true)ImportFiles(d.FileNames,NewPoint());}
  public void ImportFiles(string[] paths,Point where){Board.CommitEdit();int ok=0;List<string> failures=new List<string>();foreach(string p in paths){try{FileInfo fi=new FileInfo(p);if(fi.Length>64*1024*1024)throw new InvalidDataException("超过64MB");Board.AddImage(File.ReadAllBytes(p),new Point(where.X+ok*55,where.Y+ok*45));ok++;}catch(Exception e){failures.Add(Path.GetFileName(p)+"："+e.Message);}}SetStatus("已放置 "+ok+" 张图片");if(failures.Count>0)MessageBox.Show(this,string.Join("\n",failures.Take(8)),"部分图片未能导入",MessageBoxButton.OK,MessageBoxImage.Information);}
  void DragOverFiles(object sender,DragEventArgs e){if(e.Data.GetDataPresent(DataFormats.FileDrop)){e.Effects=DragDropEffects.Copy;e.Handled=true;}}
  void DropFiles(object sender,DragEventArgs e){if(!e.Data.GetDataPresent(DataFormats.FileDrop))return;string[] files=(string[])e.Data.GetData(DataFormats.FileDrop);if(files.Length==1&&string.Equals(Path.GetExtension(files[0]),".canvas",StringComparison.OrdinalIgnoreCase)){OpenFile(files[0]);return;}ImportFiles(files,Board.World(e.GetPosition(Board)));e.Handled=true;}
  public void Paste(){if(Board.Editor!=null){Board.Editor.Paste();return;}try{if(Clipboard.ContainsFileDropList()){ImportFiles(Clipboard.GetFileDropList().Cast<string>().ToArray(),NewPoint());}else if(Clipboard.ContainsImage()){Board.AddImage(CanvasBoard.PngBytes(Clipboard.GetImage()),NewPoint());}else if(Clipboard.ContainsText()){string text=Clipboard.GetText();if(text.Length>1000000)throw new InvalidDataException("文本超过100万字符。");Board.AddText(text,NewPoint());}}catch(Exception ex){ShowError("粘贴失败。",ex);}}
  void Copy(){CanvasItem i=Board.Selected;if(i==null)return;try{if(i.Kind=="image"){BitmapSource image=Board.ImageFor(i.AssetId);if(image!=null)Clipboard.SetImage(image);}else Clipboard.SetText(i.Text);}catch(Exception e){ShowError("剪贴板暂时无法访问。",e);}}
  void Keys(object sender,KeyEventArgs e){
   bool control=(Keyboard.Modifiers&ModifierKeys.Control)!=0;
   if(control&&e.Key==Key.S){Save((Keyboard.Modifiers&ModifierKeys.Shift)!=0);e.Handled=true;return;}
   if(control&&e.Key==Key.O){OpenDialog();e.Handled=true;return;}
   if(Board.Editor!=null){if(e.Key==Key.Escape){Board.CommitEdit();Board.Focus();e.Handled=true;}return;}
   if(control){if(e.Key==Key.V){Paste();e.Handled=true;}else if(e.Key==Key.C){Copy();e.Handled=true;}else if(e.Key==Key.Z){Board.Undo();e.Handled=true;}else if(e.Key==Key.Y){Board.Redo();e.Handled=true;}else if(e.Key==Key.D){Board.DuplicateSelection();e.Handled=true;}else if(e.Key==Key.N){NewDocument();e.Handled=true;}else if(e.Key==Key.D0){Board.Fit();e.Handled=true;}}
   else if(e.Key==Key.Delete||e.Key==Key.Back){Board.DeleteSelection();e.Handled=true;}
   else if(e.Key==Key.Escape){link.IsChecked=false;Board.SetSelection(null);}
   else if(e.Key==Key.F2)Rename();
  }
  void AddContextMenu(){ContextMenu menu=new ContextMenu();Action<string,Action> add=delegate(string text,Action a){MenuItem m=new MenuItem{Header=text};m.Click+=delegate{a();};menu.Items.Add(m);};add("粘贴",Paste);add("添加文字",delegate{CanvasItem i=Board.AddText("",NewPoint());Board.BeginEdit(i);});add("添加图片",ImportDialog);menu.Items.Add(new Separator());add("复制",Copy);add("创建副本",Board.DuplicateSelection);add("移到最前",Board.BringForward);add("删除",Board.DeleteSelection);Board.Surface.ContextMenu=menu;}
  string RecoveryPath(){return Path.Combine(recoveryDir,Board.Session.Document.Id+".canvas");}
  void AutoSave(){if(!ready||!Board.Session.Dirty||DateTime.UtcNow-lastChange<TimeSpan.FromSeconds(2))return;string snapshot=Board.Session.Snapshot();if(snapshot==lastAutosaved)return;try{CanvasArchive.Save(RecoveryPath(),Board.Session.Document,Board.Session.Assets);lastAutosaved=snapshot;SetStatus("自动恢复副本已保存 · "+DateTime.Now.ToString("HH:mm:ss")+"    Ctrl+S 保存正式存档");}catch(Exception e){if(!reportedAutoError){SetStatus("自动恢复保存失败，请使用 Ctrl+S："+e.Message);reportedAutoError=true;}}}
  void RecoverLast(){ShowRecovery(true);}
  void ShowRecovery(bool startup){
   List<RecoveryEntry> entries=RecoveryCatalog.Find(recoveryDir,settings.Recent.Concat(Directory.GetFiles(archiveDir,"*.canvas")));
   if(entries.Count==0){if(!startup)SetStatus("暂无需要恢复的草稿。");return;}
   if(startup&&MessageBox.Show(this,"发现 "+entries.Count+" 份可恢复草稿。是否打开恢复列表？\n稍后也可点击工具栏的“恢复草稿”。","恢复画布",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
   Window dialog=new Window{Title="恢复草稿",Owner=this,Width=450,Height=340,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Brushes.White,ShowInTaskbar=false};
   DockPanel panel=new DockPanel{Margin=new Thickness(18)};dialog.Content=panel;
   TextBlock help=new TextBlock{Text="恢复后会打开为未保存画布，请另行保存。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,14)};DockPanel.SetDock(help,Dock.Top);panel.Children.Add(help);
   Button open=new Button{Content="恢复所选草稿",Height=34,Margin=new Thickness(0,12,0,0)};DockPanel.SetDock(open,Dock.Bottom);panel.Children.Add(open);ListBox list=new ListBox{ItemsSource=entries,SelectedIndex=0};panel.Children.Add(list);open.Click+=delegate{if(list.SelectedItem!=null)dialog.DialogResult=true;};
   if(dialog.ShowDialog()!=true)return;RecoveryEntry entry=(RecoveryEntry)list.SelectedItem;
   if(!CanLeave())return;
   try{Session recovered=CanvasArchive.Load(entry.Path);recovered.FilePath=null;recovered.Dirty=true;Board.SetSession(recovered);lastAutosaved="";UpdateTitle();SetStatus("已恢复草稿，请保存为正式存档。");}catch(Exception ex){ShowError("草稿无法恢复，请尝试其他副本。",ex);}
  }
  void CloseRequested(object sender,CancelEventArgs e){if(!testing&&!CanLeave()){e.Cancel=true;return;}if(aiServer!=null){aiServer.Dispose();aiServer=null;}autosave.Stop();try{settings.Topmost=Topmost;settings.Width=RestoreBounds.Width;settings.Height=RestoreBounds.Height;File.WriteAllText(Path.Combine(DataRoot,"settings.json"),Json.Write(settings),Encoding.UTF8);}catch{} }
  void ShowError(string title,Exception ex){MessageBox.Show(this,title+"\n"+ex.Message,"无限画布",MessageBoxButton.OK,MessageBoxImage.Warning);}
  void ShowHelp(){MessageBox.Show(this,"拖动空白处 / 鼠标中键 / 空格＋拖动：平移\n鼠标滚轮：围绕指针缩放\n双击文字：编辑；Esc：结束编辑\n选中卡片右下角：调整大小\nCtrl+C / Ctrl+V：复制、粘贴文字或图片\nCtrl+D：创建所选卡片副本\nCtrl+Z / Ctrl+Y：撤销、重做\nDelete：删除所选卡片或连线\nCtrl+S：保存；Ctrl+Shift+S：另存为\nCtrl+O：打开；Ctrl+N：新建\nCtrl+0：显示全部；F2：重命名\n\n连线：启用后依次点击起点与终点\n左下导航：点击或拖动定位\n\n自动恢复副本独立保存，Ctrl+S 写入正式存档。","无限画布 · 操作说明",MessageBoxButton.OK,MessageBoxImage.Information);}
  public static string AskName(Window owner,string title,string current){
   Window d=new Window{Title=title,Owner=owner,Width=390,Height=180,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Brushes.White,ShowInTaskbar=false};
   StackPanel p=new StackPanel{Margin=new Thickness(22,18,22,15)};d.Content=p;p.Children.Add(new TextBlock{Text="画布名称",Margin=new Thickness(0,0,0,8)});
   TextBox name=new TextBox{Text=current,FontSize=15,Padding=new Thickness(7),MaxLength=120};p.Children.Add(name);
   StackPanel buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};p.Children.Add(buttons);
   Button cancel=new Button{Content="取消",Width=70,Margin=new Thickness(0,0,8,0),IsCancel=true};buttons.Children.Add(cancel);Button ok=new Button{Content="确定",Width=70,IsDefault=true};ok.Click+=delegate{if(!string.IsNullOrWhiteSpace(name.Text))d.DialogResult=true;};buttons.Children.Add(ok);
   d.Loaded+=delegate{name.Focus();name.SelectAll();};return d.ShowDialog()==true?name.Text.Trim():null;
  }
  const string Styles=@"<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
<Style TargetType='Button'><Setter Property='Height' Value='32'/><Setter Property='Padding' Value='10,3'/><Setter Property='Margin' Value='3,2'/><Setter Property='Background' Value='#F2F3F0'/><Setter Property='Foreground' Value='#313833'/><Setter Property='BorderThickness' Value='0'/><Setter Property='Cursor' Value='Hand'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'><Border x:Name='body' Background='{TemplateBinding Background}' CornerRadius='4' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='body' Property='Opacity' Value='.75'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter TargetName='body' Property='Opacity' Value='.4'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
<Style TargetType='ToggleButton'><Setter Property='Height' Value='32'/><Setter Property='Padding' Value='10,3'/><Setter Property='Margin' Value='3,2'/><Setter Property='Background' Value='#F2F3F0'/><Setter Property='BorderThickness' Value='0'/><Setter Property='Cursor' Value='Hand'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ToggleButton'><Border x:Name='body' Background='{TemplateBinding Background}' CornerRadius='4' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsChecked' Value='True'><Setter TargetName='body' Property='Background' Value='#CDD6DD'/></Trigger><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='body' Property='Opacity' Value='.75'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
<Style TargetType='ComboBox'><Setter Property='Height' Value='30'/><Setter Property='Margin' Value='3,3'/><Setter Property='VerticalContentAlignment' Value='Center'/></Style>
<Style TargetType='ListBoxItem'><Setter Property='Padding' Value='8,10'/><Setter Property='Margin' Value='0,2'/><Setter Property='HorizontalContentAlignment' Value='Stretch'/></Style>
</ResourceDictionary>";
 }
}
