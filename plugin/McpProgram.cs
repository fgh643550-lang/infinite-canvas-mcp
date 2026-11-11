using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using InfiniteCanvas;

public static class McpProgram {
 static string root,data;
 static Dictionary<string,object> connection;
 static bool initialized,ready;
 static object Empty(){return new Dictionary<string,object>();}
 static object Http(string route,object body){
  var req=(HttpWebRequest)WebRequest.Create((string)connection["url"]+route);req.Proxy=null;req.Timeout=15000;req.Headers["Authorization"]="Bearer "+(string)connection["token"];
  if(body!=null){byte[] bytes=Encoding.UTF8.GetBytes(Json.Write(body));req.Method="POST";req.ContentType="application/json";req.ContentLength=bytes.Length;using(Stream s=req.GetRequestStream())s.Write(bytes,0,bytes.Length);}
  try{using(var resp=req.GetResponse())using(var reader=new StreamReader(resp.GetResponseStream(),Encoding.UTF8))return Json.Read<Dictionary<string,object>>(reader.ReadToEnd());}
  catch(WebException e){if(e.Response!=null){using(var resp=e.Response)using(var reader=new StreamReader(resp.GetResponseStream())){var error=Json.Read<Dictionary<string,object>>(reader.ReadToEnd());throw new InvalidOperationException(Convert.ToString(error["error"]));}}throw;}
 }
 static bool Attach(){
  try{var c=Json.Read<Dictionary<string,object>>(File.ReadAllText(Path.Combine(data,"ai-connection.json"),Encoding.UTF8));
   var uri=new Uri((string)c["url"]);if(uri.Scheme!="http"||uri.Host!="127.0.0.1"||uri.Port<17653||uri.Port>=17753)return false;
   connection=c;var health=(Dictionary<string,object>)Http("/health",null);return Convert.ToInt32(health["pid"])==Convert.ToInt32(c["pid"]);
  }catch{return false;}
 }
 static void EnsureCanvas(){
  if(Attach())return;
  Directory.CreateDirectory(data);
  // One launch lock per data root across multiple independently started AI clients.
  string lockPath=Path.Combine(data,"ai-launch.lock");
  for(int attempt=0;attempt<100;attempt++){
   FileStream launchLock=null;try{launchLock=new FileStream(lockPath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}catch(IOException){Thread.Sleep(100);if(Attach())return;continue;}
   using(launchLock){
    if(Attach())return;
    string exe=Path.Combine(root,"无限画布.exe");if(!File.Exists(exe))throw new FileNotFoundException("Keep the plugin folder beside 无限画布.exe",exe);
    Process.Start(new ProcessStartInfo{FileName=exe,Arguments="--ai --data-root \""+data+"\"",WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true});
    for(int i=0;i<100;i++){Thread.Sleep(100);if(Attach())return;}
    throw new InvalidOperationException("Canvas AI service did not start. Check 数据/ai-error.log and startup-error.txt.");
   }
  }
  throw new InvalidOperationException("Timed out waiting for another canvas launch.");
 }
 static void Send(object value){Console.WriteLine(Json.Write(value));Console.Out.Flush();}
 static object Handle(string method,Dictionary<string,object> p){
  if(method=="initialize"){
   if(initialized)throw new InvalidOperationException("Already initialized");initialized=true;
   string requested=AiControllerFreeStr(p,"protocolVersion","");string negotiated=Array.IndexOf(new[]{"2024-11-05","2025-03-26","2025-06-18","2025-11-25"},requested)>=0?requested:"2025-11-25";
   return new {protocolVersion=negotiated,capabilities=new {tools=new {listChanged=false}},serverInfo=new {name="infinite-canvas",version="1.0.0"},instructions=AiTools.Instructions};
  }
  if(method=="ping")return Empty();
  if(!ready)throw new InvalidOperationException("Send initialize then notifications/initialized first");
  if(method=="tools/list")return new {tools=AiTools.List()};
  if(method=="tools/call"){
   try{
    string name=AiControllerFreeStr(p,"name",null);object args;Dictionary<string,object> a=p.TryGetValue("arguments",out args)?args as Dictionary<string,object>:new Dictionary<string,object>();if(a==null)throw new ArgumentException("arguments must be an object");
    EnsureCanvas();var response=(Dictionary<string,object>)Http("/v1/call",new {name=name,arguments=a});var result=response["result"];
    if(name=="canvas_preview"){
     var image=(Dictionary<string,object>)result;return new {content=new object[]{new {type="image",mimeType=image["mimeType"],data=image["data"]}},isError=false};
    }
    return new {content=new object[]{new {type="text",text=Json.Write(result)}},isError=false};
   }catch(Exception e){return new {content=new object[]{new {type="text",text=e.Message}},isError=true};}
  }
  throw new MissingMethodException("Unknown method: "+method);
 }
 static string AiControllerFreeStr(Dictionary<string,object> p,string key,string fallback){object v;if(!p.TryGetValue(key,out v))return fallback;if(!(v is string))throw new ArgumentException(key+" must be string");return (string)v;}
 public static int Main(string[] args){
  Console.InputEncoding=new UTF8Encoding(false);Console.OutputEncoding=new UTF8Encoding(false);
  root=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,".."));data=Path.Combine(root,"数据");
  int index=Array.IndexOf(args,"--data-root");if(index>=0&&index+1<args.Length)data=Path.GetFullPath(args[index+1]);
  string line;
  while((line=Console.ReadLine())!=null){
   object id=null;bool hasId=false;
   try{
    Dictionary<string,object> request;try{request=Json.Read<Dictionary<string,object>>(line);}catch{Send(new {jsonrpc="2.0",id=(object)null,error=new {code=-32700,message="Parse error"}});continue;}
    if(request==null||!request.ContainsKey("method")||!request.ContainsKey("jsonrpc")||Convert.ToString(request["jsonrpc"])!="2.0"){Send(new {jsonrpc="2.0",id=(object)null,error=new {code=-32600,message="Invalid Request"}});continue;}
    hasId=request.TryGetValue("id",out id);string method=AiControllerFreeStr(request,"method",null);object raw;
    var p=request.TryGetValue("params",out raw)?raw as Dictionary<string,object>:new Dictionary<string,object>();if(p==null)throw new ArgumentException("params must be an object");
    if(!hasId){if(method=="notifications/initialized"&&initialized)ready=true;continue;}
    object result=Handle(method,p);Send(new {jsonrpc="2.0",id=id,result=result});
   }catch(Exception e){if(hasId)Send(new {jsonrpc="2.0",id=id,error=new {code=e is MissingMethodException?-32601:e is ArgumentException?-32602:-32600,message=e.Message}});}
  }
  return 0;
 }
}
