using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;

namespace InfiniteCanvas {
 public static class AiTools {
  public const string Instructions="Use canvas_get first. Preserve existing content. Use expected_revision from canvas_get for edits. canvas_apply is atomic and undoable. Coordinates are canvas/world pixels. Dirty documents must be saved before switching. Images accept base64 encoded PNG/JPEG/etc. HTTP is local REST, MCP transport is stdio.";
  public static void ValidateArguments(string name,Dictionary<string,object> arguments){
   foreach(object tool in List()){
    var d=Json.Read<Dictionary<string,object>>(Json.Write(tool));if((string)d["name"]==name){ValidateValue(arguments,(Dictionary<string,object>)d["inputSchema"],"arguments");return;}
   }
   throw new ArgumentException("Unknown tool: "+name);
  }
  static void ValidateValue(object value,Dictionary<string,object> schema,string location){
   string type=(string)schema["type"];
   if(type=="object"){
    var obj=value as Dictionary<string,object>;if(obj==null)throw new ArgumentException(location+" must be an object");
    var props=(Dictionary<string,object>)schema["properties"];object required;
    if(schema.TryGetValue("required",out required))foreach(string key in (IList)required)if(!obj.ContainsKey(key))throw new ArgumentException(location+" missing required field: "+key);
    foreach(var pair in obj){object prop;if(!props.TryGetValue(pair.Key,out prop))throw new ArgumentException(location+" has unknown field: "+pair.Key);ValidateValue(pair.Value,(Dictionary<string,object>)prop,location+"."+pair.Key);}
   }else if(type=="array"){
    IList values=value as IList;if(values==null)throw new ArgumentException(location+" must be an array");
    if(values.Count<Convert.ToInt32(schema["minItems"])||values.Count>Convert.ToInt32(schema["maxItems"]))throw new ArgumentException(location+" has invalid array length");
    foreach(object entry in values)ValidateValue(entry,(Dictionary<string,object>)schema["items"],location+"[]");
   }else if(type=="string"){
    if(!(value is string))throw new ArgumentException(location+" must be a string");object choices;
    if(schema.TryGetValue("enum",out choices)&&!((IList)choices).Cast<object>().Contains(value))throw new ArgumentException(location+" has invalid value");
   }else if(type=="boolean"){if(!(value is bool))throw new ArgumentException(location+" must be boolean");}
   else if(type=="number"){if(!(value is int||value is long||value is double||value is decimal)||!CanvasArchive.Finite(Convert.ToDouble(value)))throw new ArgumentException(location+" must be a finite number");}
  }
  static object Prop(string type,string description){return new {type=type,description=description};}
  static object Tool(string name,string description,Dictionary<string,object> properties,string[] required,bool readOnly){
   return new {name=name,description=description,inputSchema=new {type="object",properties=properties,required=required,additionalProperties=false},annotations=new {readOnlyHint=readOnly,destructiveHint=!readOnly,openWorldHint=false}};
  }
  public static object[] List(){
   var revision=Prop("string","Optional optimistic concurrency token from canvas_get. A stale token rejects the entire edit.");
   var name=Prop("string","Canvas name, 1-120 characters.");
   var file=Prop("string","Single .canvas filename in the configured archive folder, without directory components.");
   var operationProperties=new Dictionary<string,object>{
    {"op",new {type="string",@enum=new[]{"add_text","add_image","update","delete","connect","disconnect","rename"}}},
    {"id",Prop("string","Item/arrow identifier; optional on creation. Choose stable unique IDs for references within a batch.")},
    {"text",Prop("string","Text content, also used as document name for rename.")},
    {"x",Prop("number","World X")},{"y",Prop("number","World Y")},
    {"width",Prop("number","Card width, 20..100000")},{"height",Prop("number","Card height, 20..100000")},
    {"font_size",Prop("number","Font size, 8..200")},{"bold",Prop("boolean","Bold text")},
    {"color",Prop("string","#RRGGBB text color")},
    {"data_base64",Prop("string","Raw image bytes encoded as base64, no data URL prefix. Decoded maximum 12 MB per call.")},
    {"start_id",Prop("string","Connector start item ID")},{"end_id",Prop("string","Connector end item ID")}
   };
   return new[]{
    Tool("canvas_get","Read current live document, revision, selected item and unsaved state. Images are represented by asset IDs, not raw bytes.",new Dictionary<string,object>(),new string[0],true),
    Tool("canvas_list","List readable .canvas archives in the configured archive folder.",new Dictionary<string,object>(),new string[0],true),
    Tool("canvas_new","Create an empty canvas. Refuses if current document has unsaved edits.",new Dictionary<string,object>{{"name",name}},new[]{"name"},false),
    Tool("canvas_open","Open an archive in the live window. Refuses if current document has unsaved edits.",new Dictionary<string,object>{{"filename",file}},new[]{"filename"},false),
    Tool("canvas_save","Save to the archive folder without a dialog. New filenames never overwrite an existing different archive. Omit filename to save the current file.",new Dictionary<string,object>{{"filename",file},{"expected_revision",revision}},new string[0],false),
    Tool("canvas_apply","Atomically apply 1..200 operations as ONE undo step. Updates accept only provided fields. Delete also removes attached arrows. rename uses text. Invalid operations leave everything unchanged.",new Dictionary<string,object>{{"expected_revision",revision},{"operations",new {type="array",minItems=1,maxItems=200,items=new {type="object",properties=operationProperties,required=new[]{"op"},additionalProperties=false}}}},new[]{"operations"},false),
    Tool("canvas_undo","Undo one document edit.",new Dictionary<string,object>{{"expected_revision",revision}},new string[0],false),
    Tool("canvas_redo","Redo one document edit.",new Dictionary<string,object>{{"expected_revision",revision}},new string[0],false),
    Tool("canvas_view","Fit all content or set pan/zoom. View changes do not create a document undo step.",new Dictionary<string,object>{{"fit",Prop("boolean","Fit all cards")},{"pan_x",Prop("number","Screen pan X")},{"pan_y",Prop("number","Screen pan Y")},{"zoom",Prop("number","Zoom, 0.04..8")}},new string[0],false),
    Tool("canvas_preview","Return a PNG image of the visible live board. Use canvas_view fit first to show all content.",new Dictionary<string,object>(),new string[0],true)
   };
  }
 }
}
