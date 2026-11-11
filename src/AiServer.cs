using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace InfiniteCanvas {
 // Bounded HTTP/1.1 REST server without HTTP.sys URL reservations or administrator installation.
 public sealed class AiServer:IDisposable {
  readonly MainWindow window;readonly AiController controller;readonly TcpListener listener;
  readonly string token,connectionFile;readonly int pid=Process.GetCurrentProcess().Id;
  readonly Semaphore slots=new Semaphore(8,8);
  volatile bool stopped;
  public readonly string Url;
  public AiServer(MainWindow owner){
   window=owner;controller=new AiController(owner);connectionFile=Path.Combine(owner.DataRoot,"ai-connection.json");
   byte[] bytes=new byte[32];using(var rng=RandomNumberGenerator.Create())rng.GetBytes(bytes);token=Convert.ToBase64String(bytes);
   for(int port=17653;port<17753;port++){
    var candidate=new TcpListener(IPAddress.Loopback,port);
    try{candidate.Start(16);listener=candidate;Url="http://127.0.0.1:"+port;break;}catch(SocketException){candidate.Stop();if(port==17752)throw;}
   }
   string temp=connectionFile+"."+pid+".tmp";
   File.WriteAllText(temp,Json.Write(new {url=Url,token=token,pid=pid,version="1.0.0"}),new UTF8Encoding(false));
   if(File.Exists(connectionFile))File.Replace(temp,connectionFile,null);else File.Move(temp,connectionFile);
   Thread worker=new Thread(Serve);worker.IsBackground=true;worker.Start();
  }
  void Serve(){while(!stopped){try{TcpClient client=listener.AcceptTcpClient();if(!slots.WaitOne(0)){client.Close();continue;}ThreadPool.QueueUserWorkItem(delegate{try{Handle(client);}finally{slots.Release();}});}catch{if(stopped)return;}}}
  static string Line(Stream stream,ref int budget){
   using(var buffer=new MemoryStream()){
    while(true){if(--budget<0)throw new InvalidDataException("HTTP headers too large");int b=stream.ReadByte();if(b<0)throw new EndOfStreamException();if(b==10)break;buffer.WriteByte((byte)b);}
    return Encoding.ASCII.GetString(buffer.ToArray()).TrimEnd('\r');
   }
  }
  static void Reply(Stream stream,int code,object body){
   byte[] bytes=Encoding.UTF8.GetBytes(Json.Write(body));
   byte[] header=Encoding.ASCII.GetBytes("HTTP/1.1 "+code+" "+(code==200?"OK":"Error")+"\r\nContent-Type: application/json; charset=utf-8\r\nCache-Control: no-store\r\nConnection: close\r\nContent-Length: "+bytes.Length+"\r\n\r\n");
   stream.Write(header,0,header.Length);stream.Write(bytes,0,bytes.Length);
  }
  void Handle(TcpClient client){
   using(client){client.ReceiveTimeout=10000;client.SendTimeout=10000;using(NetworkStream stream=client.GetStream()){
    try{
     int budget=16384;string[] first=Line(stream,ref budget).Split(' ');if(first.Length!=3||first[2]!="HTTP/1.1")throw new InvalidDataException("HTTP/1.1 required");
     var headers=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);string line;
     while((line=Line(stream,ref budget)).Length>0){int colon=line.IndexOf(':');if(colon<1)throw new InvalidDataException("Invalid HTTP header");string key=line.Substring(0,colon);if(headers.ContainsKey(key))throw new InvalidDataException("Duplicate HTTP header");headers[key]=line.Substring(colon+1).Trim();}
     string auth,host;
     if(!headers.TryGetValue("Host",out host)||host!=new Uri(Url).Authority){Reply(stream,403,new {error="Invalid Host"});return;}
     if(headers.ContainsKey("Origin")){Reply(stream,403,new {error="Browser origins are not allowed"});return;}
     if(!headers.TryGetValue("Authorization",out auth)||auth!="Bearer "+token){Reply(stream,401,new {error="Bearer authentication required"});return;}
     string method=first[0],path=first[1];
     if(method=="GET"&&path=="/health"){Reply(stream,200,new {service="infinite-canvas",version="1.0.0",pid=pid});return;}
     if(method=="GET"&&path=="/v1/tools"){Reply(stream,200,new {tools=AiTools.List(),instructions=AiTools.Instructions});return;}
     if(method!="POST"||path!="/v1/call"){Reply(stream,404,new {error="Use GET /v1/tools or POST /v1/call"});return;}
     string lengthText;int length;
     if(headers.ContainsKey("Transfer-Encoding")||!headers.TryGetValue("Content-Length",out lengthText)||!int.TryParse(lengthText,out length)||length<0||length>20*1024*1024){Reply(stream,413,new {error="Request requires Content-Length <= 20 MB; chunked transfer is not supported"});return;}
     string expect;if(headers.TryGetValue("Expect",out expect)&&expect.Equals("100-continue",StringComparison.OrdinalIgnoreCase)){byte[] interim=Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n");stream.Write(interim,0,interim.Length);}
     byte[] bytes=new byte[length];int offset=0;while(offset<length){int n=stream.Read(bytes,offset,length-offset);if(n==0)throw new EndOfStreamException();offset+=n;}
     var body=Json.Read<Dictionary<string,object>>(new UTF8Encoding(false,true).GetString(bytes));if(body==null)throw new ArgumentException("JSON object required");
     string name=AiController.Str(body,"name",null);object raw;var args=body.TryGetValue("arguments",out raw)?raw as Dictionary<string,object>:new Dictionary<string,object>();if(args==null)throw new ArgumentException("arguments must be an object");
     object result=window.Dispatcher.Invoke(new Func<object>(delegate{object value=controller.Call(name,args);if(name!="canvas_get"&&name!="canvas_list"&&name!="canvas_preview")window.SetStatus("AI 已操作 · "+name);return value;}));
     Reply(stream,200,new {result=result});
    }catch(Exception e){try{while(e.InnerException!=null)e=e.InnerException;Reply(stream,400,new {error=e.Message});}catch{}}
   }}
  }
  public void Dispose(){stopped=true;listener.Stop();try{if(File.Exists(connectionFile)){var d=Json.Read<Dictionary<string,object>>(File.ReadAllText(connectionFile));if(Convert.ToInt32(d["pid"])==pid)File.Delete(connectionFile);}}catch{}}
 }
}
