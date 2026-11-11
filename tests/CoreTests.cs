using System;
using System.IO;
using InfiniteCanvas;
class CoreTests {
 static int checks,failures;
 static void Check(bool b,string name) { if(!b){failures++;Console.WriteLine("FAIL "+name);}else{checks++;Console.WriteLine("PASS "+name);} }
 static void Main() {
  string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-data");Directory.CreateDirectory(root);
  Viewport v=new Viewport();v.PanX=130;v.PanY=-60;v.Zoom=.5;
  Check(Math.Abs(v.WorldX(280)-300)<.00001,"negative and scaled coordinates");
  double wx=v.WorldX(240),wy=v.WorldY(320);v.ZoomAt(240,320,2);
  Check(Math.Abs(v.WorldX(240)-wx)<.00001&&Math.Abs(v.WorldY(320)-wy)<.00001,"zoom remains anchored to cursor");
  CanvasDocument d=new CanvasDocument();d.Name="庄园 · 人物流程";
  CanvasItem a=new CanvasItem();a.Text="女仆\n第二行";a.X=-900;a.Y=120;a.Bold=true;a.Color="#21685E";d.Items.Add(a);
  CanvasItem b=new CanvasItem();b.Kind="image";b.AssetId=Guid.NewGuid().ToString("N");b.X=800;d.Items.Add(b);
  d.Arrows.Add(new CanvasArrow { StartId=a.Id,EndId=b.Id });
  Session s=new Session(d);s.Assets[b.AssetId]=new byte[]{1,2,3,4};
  s.Change(delegate { a.X=555; });Check(s.Document.Items[0].X==555,"change recorded");s.Undo();Check(s.Document.Items[0].X==-900,"undo move");s.Redo();Check(s.Document.Items[0].X==555,"redo move");
  s.Change(delegate { s.Document.Items.RemoveAt(1);s.Document.Arrows.Clear(); });s.Undo();Check(s.Document.Items.Count==2&&s.Document.Arrows.Count==1,"undo deletion restores nodes and connectors");
  string file=Path.Combine(root,"中文.canvas");CanvasArchive.Save(file,s.Document,s.Assets);Session loaded=CanvasArchive.Load(file);
  Check(loaded.Document.Name==d.Name&&loaded.Document.Items[0].Text=="女仆\n第二行"&&loaded.Document.Items[0].Bold,"unicode and style round trip");
  Check(loaded.Assets[b.AssetId].Length==4&&loaded.Document.Arrows.Count==1,"embedded image and connectors survive reopen");
  loaded.Document.Name="第二份";CanvasArchive.Save(file,loaded.Document,loaded.Assets);Check(File.Exists(file+".bak"),"atomic replacement leaves previous save");
  File.WriteAllText(file,"damaged");bool recovered;Session backup=CanvasArchive.LoadWithBackup(file,out recovered);Check(recovered&&backup.Document.Name==d.Name,"corrupt save recovers intact backup");
  CanvasDocument invalid=new CanvasDocument();invalid.Items.Add(new CanvasItem { X=double.NaN });bool rejected=false;try { CanvasArchive.Save(Path.Combine(root,"invalid.canvas"),invalid,s.Assets); }catch(InvalidDataException){rejected=true;}Check(rejected,"nonfinite coordinates rejected");
  CanvasDocument missing=new CanvasDocument();missing.Items.Add(new CanvasItem { Kind="image",AssetId=Guid.NewGuid().ToString("N") });rejected=false;try { CanvasArchive.Save(Path.Combine(root,"missing.canvas"),missing,new System.Collections.Generic.Dictionary<string,byte[]>()); }catch(InvalidDataException){rejected=true;}Check(rejected,"missing embedded asset rejected before writing");
  CanvasDocument unsafeId=new CanvasDocument{Id="../outside"};rejected=false;try{CanvasArchive.Save(Path.Combine(root,"bad-id.canvas"),unsafeId,s.Assets);}catch(InvalidDataException){rejected=true;}Check(rejected,"unsafe document ID rejected before recovery paths");
  CanvasDocument tooLarge=new CanvasDocument();var manyAssets=new System.Collections.Generic.Dictionary<string,byte[]>();byte[] shared=new byte[32*1024*1024];for(int i=0;i<17;i++){string id=Guid.NewGuid().ToString("N");manyAssets[id]=shared;tooLarge.Items.Add(new CanvasItem{Kind="image",AssetId=id});}rejected=false;try{CanvasArchive.Save(Path.Combine(root,"too-large.canvas"),tooLarge,manyAssets);}catch(InvalidDataException){rejected=true;}Check(rejected,"save and load share total embedded image budget");
  ItemSize resized=ResizeMath.Resize(520,52,60,52,true);Check(resized.Height>=20&&Math.Abs(resized.Width/resized.Height-10)<.001,"wide image resize remains savable with aspect ratio");
  ItemSize tall=ResizeMath.Resize(52,520,100000,520,true);Check(tall.Width<=100000&&tall.Height<=100000,"portrait resize respects maximum height");
  string recovery=Path.Combine(root,"recovery-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(recovery);
  CanvasDocument dirty=new CanvasDocument{Name="较旧但未保存的草稿"},clean=new CanvasDocument{Name="已正式保存"};string oldRecovery=Path.Combine(recovery,dirty.Id+".canvas"),cleanRecovery=Path.Combine(recovery,clean.Id+".canvas"),formal=Path.Combine(root,"formal.canvas");var empty=new System.Collections.Generic.Dictionary<string,byte[]>();CanvasArchive.Save(oldRecovery,dirty,empty);CanvasArchive.Save(cleanRecovery,clean,empty);CanvasArchive.Save(formal,clean,empty);File.SetLastWriteTimeUtc(oldRecovery,DateTime.UtcNow.AddMinutes(-10));File.SetLastWriteTimeUtc(cleanRecovery,DateTime.UtcNow.AddMinutes(-5));File.WriteAllText(Path.Combine(recovery,"newest-corrupt.canvas"),"invalid");var found=RecoveryCatalog.Find(recovery,new[]{formal});Check(found.Count==1&&found[0].Path==oldRecovery,"older dirty recovery remains discoverable behind clean or corrupt latest file");
  Console.WriteLine(checks+" PASSED; "+failures+" FAILED");Environment.ExitCode=failures==0?0:1;
 }
}
