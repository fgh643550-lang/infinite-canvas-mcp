using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using InfiniteCanvas;

class AiUiTests {
 static int failures;
 static void Check(bool value,string name){Console.WriteLine((value?"PASS ":"FAIL ")+name);if(!value)failures++;}
 static Dictionary<string,object> Args(string json){return Json.Read<Dictionary<string,object>>(json);}
 [STAThread]static int Main(){
  var app=new Application();string data=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ai-ui-"+Guid.NewGuid().ToString("N"));
  var win=new MainWindow(data,true);win.Show();var api=new AiController(win);
  api.Call("canvas_apply",Args("{\"operations\":[{\"op\":\"add_text\",\"id\":\"a\",\"text\":\"original\"}]}"));
  object state=api.Call("canvas_get",Args("{}"));win.Board.Session.Document.Items[0].Text="human edit";
  Check(Json.Write(state).Contains("original")&&!Json.Write(state).Contains("human edit"),"response is an immutable snapshot matching its revision");
  var modal=new Window{Owner=win,Width=200,Height=100,ShowInTaskbar=false};bool blocked=false;
  modal.Loaded+=delegate{try{api.Call("canvas_apply",Args("{\"operations\":[{\"op\":\"delete\",\"id\":\"a\"}]}"));}catch(InvalidOperationException){blocked=true;}modal.Close();};
  modal.ShowDialog();Check(blocked&&win.Board.Session.Document.Items.Count==1,"modal UI workflows block AI mutations");
  win.Board.SetSession(new Session(new CanvasDocument{Name="atomic"}));
  var ops=new List<object>();string text=new string('x',1000000);
  for(int i=0;i<35;i++)ops.Add(new Dictionary<string,object>{{"op","add_text"},{"id","large"+i},{"text",text}});
  bool rejected=false;try{api.Call("canvas_apply",new Dictionary<string,object>{{"operations",ops}});}catch(InvalidOperationException){rejected=true;}catch(ArgumentException){rejected=true;}
  Check(rejected&&win.Board.Session.Document.Items.Count==0&&!win.Board.Session.Dirty&&!win.Board.Session.CanUndo,"oversized serialized candidate cannot change live document or undo history");
  api.Call("canvas_apply",Args("{\"operations\":[{\"op\":\"add_text\",\"id\":\"title\",\"text\":\"AI 接入验证\\n实时画布\",\"x\":0,\"y\":0,\"font_size\":32},{\"op\":\"add_text\",\"id\":\"note\",\"text\":\"外部 AI 可读写文字、图片和连线\",\"x\":400,\"y\":220},{\"op\":\"connect\",\"start_id\":\"title\",\"end_id\":\"note\"}]}"));
  win.Board.Fit();Program.Capture(win,Path.Combine(data,"desktop.png"));
  win.Width=860;win.Height=580;win.UpdateLayout();win.Board.Fit();Program.Capture(win,Path.Combine(data,"compact.png"));Console.WriteLine("ARTIFACTS "+data);
  win.Close();return failures==0?0:1;
 }
}
