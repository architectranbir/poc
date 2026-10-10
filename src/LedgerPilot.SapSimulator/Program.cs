using System.Text.Json;
var builder=WebApplication.CreateBuilder(args);
if(!builder.Environment.IsDevelopment())throw new InvalidOperationException("Simulator is local Development only until service authentication is configured.");
builder.Services.AddSingleton<SimulatorStore>();
var app=builder.Build();
app.Services.GetRequiredService<SimulatorStore>();
app.Use(async(context,next)=>{
 if(context.Connection.RemoteIpAddress is not {} ip||!System.Net.IPAddress.IsLoopback(ip)){context.Response.StatusCode=403;return;}
 try{await next(context);}catch(ArgumentException ex){context.Response.StatusCode=400;await context.Response.WriteAsJsonAsync(new{error=ex.Message});}
 catch(CommandConflict ex){context.Response.StatusCode=409;await context.Response.WriteAsJsonAsync(new{error=ex.Message});}
});
app.MapGet("/health",()=>new{status="ok",system="Separate synthetic SAP simulator"});
app.MapPost("/documents",(CreateDocument command,SimulatorStore store)=>Results.Ok(store.Create(command)));
app.MapGet("/documents/{id}",(string id,SimulatorStore store)=>store.Get(id) is {} doc?Results.Ok(doc):Results.NotFound());
app.MapGet("/commands/{key}",(string key,SimulatorStore store)=>store.Lookup(key) is {} doc?Results.Ok(doc):Results.NotFound());
app.MapPost("/documents/{id}/post",(string id,PostDocument command,SimulatorStore store)=>Results.Ok(store.Post(id,command)));
app.Run();
record CreateDocument(string CommandKey,string ProjectId,decimal Net,string Currency,string StateHash);
record PostDocument(string CommandKey,string StateHash);
record SapDocument(string Id,CreateDocument Request,string Status,string? PostingReference);
record SimulatorSnapshot(int Schema,Dictionary<string,SapDocument> Documents,Dictionary<string,string> Commands);
class CommandConflict(string message):Exception(message);
class SimulatorStore:IDisposable{
 readonly object gate=new();readonly string path;readonly FileStream owner;
 Dictionary<string,SapDocument> documents=new();Dictionary<string,string> commands=new();
 public SimulatorStore(IConfiguration cfg){
 path=Path.GetFullPath(cfg["SAP_STATE_PATH"]??Path.Combine(AppContext.BaseDirectory,"local-state","sap.json"));
 Directory.CreateDirectory(Path.GetDirectoryName(path)!);owner=new(path+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
 if(File.Exists(path)){
 var saved=JsonSerializer.Deserialize<SimulatorSnapshot>(File.ReadAllText(path))??throw new InvalidDataException("Empty simulator state");
 if(saved.Schema!=1||saved.Commands.Values.Any(id=>!saved.Documents.ContainsKey(id)))throw new InvalidDataException("Invalid simulator state");
 documents=saved.Documents;commands=saved.Commands;
 }}
 public void Dispose()=>owner.Dispose();
 void Save(){using(var stream=new FileStream(path+".tmp",FileMode.Create,FileAccess.Write,FileShare.None)){JsonSerializer.Serialize(stream,new SimulatorSnapshot(1,documents,commands));stream.Flush(true);}File.Move(path+".tmp",path,true);}
 static void ValidateKey(string key){if(string.IsNullOrWhiteSpace(key)||key.Length>128)throw new ArgumentException("Command key must contain 1–128 characters");}
 public SapDocument Create(CreateDocument request){lock(gate){
 ValidateKey(request.CommandKey);
 if(string.IsNullOrWhiteSpace(request.ProjectId)||string.IsNullOrWhiteSpace(request.StateHash)||request.Net<0||string.IsNullOrWhiteSpace(request.Currency)||request.Currency.Length!=3)throw new ArgumentException("Invalid document contract");
 var key="create:"+request.CommandKey;
 if(commands.TryGetValue(key,out var existing)){var old=documents[existing];if(old.Request!=request)throw new CommandConflict("Create command key reused with different content");return old;}
 var doc=new SapDocument("SAP-PBD-"+Guid.NewGuid().ToString("N"),request,"DRAFT",null);
 documents.Add(doc.Id,doc);commands.Add(key,doc.Id);
 try{Save();}catch{documents.Remove(doc.Id);commands.Remove(key);throw;}return doc;
 }}
 public SapDocument? Get(string id){lock(gate)return documents.GetValueOrDefault(id);}
 public SapDocument? Lookup(string key){lock(gate)return commands.TryGetValue("create:"+key,out var id)?documents[id]:null;}
 public SapDocument Post(string id,PostDocument request){lock(gate){
 ValidateKey(request.CommandKey);
 if(!documents.TryGetValue(id,out var old))throw new ArgumentException("Unknown document");
 if(request.StateHash!=old.Request.StateHash)throw new CommandConflict("Posting state does not match document");
 var key="post:"+request.CommandKey;
 if(commands.TryGetValue(key,out var existing)){if(existing!=id)throw new CommandConflict("Post command key reused for another document");return documents[id];}
 if(old.Status=="POSTED")throw new CommandConflict("Document already posted with another command key");
 var posted=old with{Status="POSTED",PostingReference="SAP-POST-"+Guid.NewGuid().ToString("N")};
 documents[id]=posted;commands.Add(key,id);
 try{Save();}catch{documents[id]=old;commands.Remove(key);throw;}return posted;
 }}
}
