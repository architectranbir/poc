using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Azure.Cosmos;
using Microsoft.Agents.AI;
using Azure.AI.Projects;
using LedgerPilot.Domain;

var builder = WebApplication.CreateBuilder(args);
// This inherited scaffold has no end-user authentication yet. Fail closed for
// hosted environments until the authenticated workflow replaces it.
if (!builder.Environment.IsDevelopment())
 throw new InvalidOperationException("Local development baseline only. Entra authentication and durable gates are required before deployment.");
builder.Services.AddCors(o=>o.AddDefaultPolicy(p=>p.WithOrigins(builder.Configuration["WEB_ORIGIN"]??"http://localhost:5173").AllowAnyMethod().AllowAnyHeader()));
builder.Services.AddSingleton<BillingStore>();
builder.Services.AddSingleton<BillingEngine>();
builder.Services.AddSingleton<AiAssistant>();
var app=builder.Build();
app.Use(async (context, next) => {
 var remote = context.Connection.RemoteIpAddress;
 if (remote is null || !System.Net.IPAddress.IsLoopback(remote)) {
   context.Response.StatusCode = 403; return;
 }
 await next(context);
});
app.UseCors();
app.MapGet("/health",()=>Results.Ok(new { status="ok", environment="SYNTHETIC SAP SANDBOX" }));
app.MapGet("/api/projects",async (BillingStore s)=>Results.Ok(await s.Projects()));
app.MapGet("/api/projects/{id}/wip",async(string id,BillingStore s,BillingEngine e)=>{
 var project=(await s.Projects()).FirstOrDefault(p=>p.Id==id);
 if(project is null)return Results.NotFound();
 var lines=(await s.Lines()).Where(x=>x.ProjectId==id).ToList();
 return Results.Ok(new {project,lines,total=e.Total(lines),findings=e.Findings(project,lines)});
});
app.MapPost("/api/projects/{id}/review",async(string id,ReviewRequest req,BillingStore s,BillingEngine e)=>{
 var project=(await s.Projects()).FirstOrDefault(p=>p.Id==id);
 if(project is null)return Results.NotFound();
 var lines=(await s.Lines()).Where(x=>x.ProjectId==id).ToList();
 var available=e.Total(lines);var gap=req.ProposedAmount-available;
 return Results.Ok(new {available,proposed=req.ProposedAmount,difference=gap,accrualQuestion=gap>10000m,thresholdAssumption="Proposed - Available WIP > 10000. Business sign-off pending.",findings=e.Findings(project,lines),financeApproval=e.RequiresFinanceApproval(project.Unit,req.WriteOff,req.WriteUp)});
});
app.MapPost("/api/projects/{id}/adjustments",async(string id,AdjustmentRequest req,BillingStore s,BillingEngine e)=>{
 if(req.Actor is null || req.Actor.Length<2 || req.Confirm!=true)return Results.BadRequest(new {error="Actor and explicit confirmation required."});
 if(req.Action is not ("POSTPONE" or "WRITE_OFF"))return Results.BadRequest(new {error="Invalid action"});
 var lines=(await s.Lines()).Where(x=>x.ProjectId==id).ToList();
 if(!lines.Any(x=>x.Id==req.LineId))return Results.NotFound();
 var result=await s.SaveAdjustment(id,req);
 return Results.Ok(result);
});
app.MapPost("/api/projects/{id}/pbd",async(string id,CreatePbdRequest req,BillingStore s,BillingEngine e)=>{
 if(!req.Confirm||string.IsNullOrWhiteSpace(req.Actor))return Results.BadRequest(new {error="Explicit human intent required"});
 var p=(await s.Projects()).FirstOrDefault(p=>p.Id==id);if(p is null)return Results.NotFound();
 var lines=(await s.Lines()).Where(x=>x.ProjectId==id).ToList();
 var findings=e.Findings(p,lines);
 if(findings.Any(f=>f.Severity=="BLOCK"))return Results.BadRequest(new {error="Blocking findings",findings});
 var amount=e.Total(lines);var hash=e.Hash(id,amount,req.Narrative);
 var doc=await s.CreatePbd(id,amount,p.Currency,hash,req.Actor,req.IdempotencyKey);
 return Results.Ok(doc);
});
app.MapGet("/api/pbd/{pbdId}",async(string pbdId,BillingStore s)=>await s.GetPbd(pbdId) is {} doc?Results.Ok(doc):Results.NotFound());
app.MapPost("/api/pbd/{pbdId}/approve",async(string pbdId,ApproveRequest req,BillingStore s)=>{
 var doc=await s.GetPbd(pbdId);if(doc is null)return Results.NotFound();
 if(!req.Confirm||string.IsNullOrEmpty(req.Actor)||req.StateHash!=doc.StateHash||req.Version!=doc.Version)return Results.Conflict(new {error="Approval must match exact PBD version/state and actor"});
 var project=(await s.Projects()).First(p=>p.Id==doc.ProjectId);
 if(!new[]{project.Requestor,project.Manager,project.Partner}.Contains(req.Actor,StringComparer.OrdinalIgnoreCase))return Results.StatusCode(403);
 return Results.Ok(await s.ApprovePbd(pbdId,req.Actor));
});
app.MapPost("/api/pbd/{pbdId}/post",async(string pbdId,PostRequest req,BillingStore s)=>{
 if(!req.Confirm||string.IsNullOrWhiteSpace(req.Actor))return Results.BadRequest(new {error="Explicit confirmation required"});
 var d=await s.GetPbd(pbdId);if(d is null)return Results.NotFound();if(d.Status=="POSTED")return Results.Ok(d);
 if(d.Status!="APPROVED")return Results.Conflict(new {error="PBD must be approved before posting"});
 return Results.Ok(await s.PostPbd(pbdId));
});
app.MapPost("/api/ai/explain",async(AiRequest req,AiAssistant ai)=>Results.Ok(new {text=await ai.Explain(req)}));
app.Run();

record Project(string Id,string Name,string Unit,string Currency,string BillingCurrency,string VatCode,string Email,string Po,string Requestor,string Manager,string Partner,string BillingType,string EndClient,string ReceivingEngagementCode);
record WipLine(string Id,string ProjectId,string Person,string Type,decimal Quantity,decimal Rate,decimal Amount,int AgeDays,bool PlannedChargeable,string Date);
record Finding(string Rule,string Severity,string Message);
record ReviewRequest(decimal ProposedAmount,decimal WriteOff=0,decimal WriteUp=0);
record AdjustmentRequest(string LineId,string Action,string Actor,string Reason,bool Confirm);
record CreatePbdRequest(string Actor,bool Confirm,string Narrative,string IdempotencyKey);
record ApproveRequest(string Actor,bool Confirm,string StateHash,int Version);
record PostRequest(string Actor,bool Confirm);
record Pbd(string Id,string ProjectId,decimal Amount,string Currency,string Status,int Version,string StateHash,string RequestedBy,string? ApprovedBy,string? MockSapPostingId);
record AiRequest(string UserMessage,string? ProjectId,string? Facts);

class BillingEngine{
 public decimal Total(IEnumerable<WipLine> lines)=>BillingCalculations.Replay(lines.Select(x => new SourceLine(
   x.Id, x.Type == "Time" ? WipKind.Time : WipKind.Expense, x.Quantity, x.Rate, x.Amount)), []).Bill;
 public bool RequiresFinanceApproval(string unit,decimal writeoff,decimal writeup)=>BillingCalculations.NeedsFinanceApproval(unit,writeoff,writeup);
 public List<Finding> Findings(Project p,List<WipLine> lines){var f=new List<Finding>();
 if(lines.Count==0)f.Add(new("1.05","BLOCK","No available WIP for selected project"));
 if(string.IsNullOrEmpty(p.Email))f.Add(new("V-07","BLOCK","Billing email is missing"));
 if(string.IsNullOrEmpty(p.Po))f.Add(new("V-07","BLOCK","PO/reference is missing"));
 if(p.BillingType=="MemberFirm"&&(string.IsNullOrEmpty(p.EndClient)||string.IsNullOrEmpty(p.ReceivingEngagementCode)))f.Add(new("V-08/V-09","BLOCK","Member-firm fields missing"));
 foreach(var l in lines.Where(x=>!x.PlannedChargeable))f.Add(new("W-03","CONFIRM",$"{l.Person} was not planned chargeable ({l.Id})"));
 foreach(var l in lines.Where(x=>x.AgeDays>180))f.Add(new("W-09","ADVISORY",$"Aged WIP {l.Id}: {l.AgeDays} days"));
 return f;}
 public string Hash(string project,decimal amount,string narrative)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{project}|{amount:F2}|{narrative}"))).ToLowerInvariant();
}
class BillingStore{
 readonly IConfiguration cfg; readonly ConcurrentDictionary<string,Pbd> pbds=new(); readonly ConcurrentDictionary<string,string> keys=new(); readonly ConcurrentBag<object> adjustments=new();
 readonly CosmosClient? cosmos;
 public BillingStore(IConfiguration c){cfg=c;
 var endpoint=c["COSMOS_ENDPOINT"];if(!string.IsNullOrWhiteSpace(endpoint))cosmos=new CosmosClient(endpoint,new DefaultAzureCredential());}
 async Task<T> Load<T>(string file){
 var con=cfg["BLOB_CONTAINER"];
 if(!string.IsNullOrEmpty(con)){
   BlobContainerClient b=new(new Uri(con),new DefaultAzureCredential());
   var blob=b.GetBlobClient(file);var d=await blob.DownloadContentAsync();return JsonSerializer.Deserialize<T>(d.Value.Content.ToString(),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
 }
 var basePath=cfg["SEED_PATH"]??Path.Combine(AppContext.BaseDirectory,"seed");
 var path=Path.Combine(basePath,file);return JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
 }
 public Task<List<Project>> Projects()=>Load<List<Project>>("projects.json");
 public Task<List<WipLine>> Lines()=>Load<List<WipLine>>("wip-lines.json");
 async Task Persist(string projectId,string id,object value){if(cosmos is not null){var container=cosmos.GetContainer("ledgerpilot","billing-state");await container.UpsertItemAsync(value,new PartitionKey(projectId));}}
 public async Task<object> SaveAdjustment(string id,AdjustmentRequest req){var ev=new {id=Guid.NewGuid().ToString(),projectId=id,eventType="adjustment",lineId=req.LineId,action=req.Action,actor=req.Actor,reason=req.Reason,at=DateTimeOffset.UtcNow}; adjustments.Add(ev);await Persist(id,ev.id,ev);return ev;}
 public async Task<Pbd> CreatePbd(string project,decimal amount,string currency,string hash,string actor,string key){
 if(string.IsNullOrWhiteSpace(key))throw new ArgumentException("Idempotency key required");
 var scope=$"{project}:create:{key}";
 if(keys.TryGetValue(scope,out var existing)&&pbds.TryGetValue(existing,out var old))return old;
 var doc=new Pbd($"PBD-{Guid.NewGuid().ToString("N")[..10].ToUpperInvariant()}",project,amount,currency,"DRAFT",1,hash,actor,null,null);
 var id=keys.GetOrAdd(scope,doc.Id);if(id!=doc.Id)return pbds[id];pbds[id]=doc;await Persist(project,doc.Id, new {id=doc.Id,projectId=project,kind="pbd",doc});return doc;}
 public Task<Pbd?> GetPbd(string id)=>Task.FromResult(pbds.TryGetValue(id,out var d)?d:null);
 public async Task<Pbd> ApprovePbd(string id,string actor){var d=pbds[id] with{Status="APPROVED",ApprovedBy=actor};pbds[id]=d;await Persist(d.ProjectId,id,new{id,projectId=d.ProjectId,kind="pbd",doc=d});return d;}
 public async Task<Pbd> PostPbd(string id){var old=pbds[id];if(old.Status=="POSTED")return old;var d=old with{Status="POSTED",MockSapPostingId=$"SIM-SAP-{Guid.NewGuid().ToString("N")[..10]}"};pbds[id]=d;await Persist(d.ProjectId,id,new{id,projectId=d.ProjectId,kind="pbd",doc=d});return d;}
}
class AiAssistant{
 readonly IConfiguration cfg;public AiAssistant(IConfiguration c)=>cfg=c;
 public async Task<string> Explain(AiRequest req){
 var ep=cfg["FOUNDRY_PROJECT_ENDPOINT"];var model=cfg["FOUNDRY_MODEL"];
 if(string.IsNullOrWhiteSpace(ep)||string.IsNullOrWhiteSpace(model))return "AI not configured. Configure a Microsoft Foundry model deployment. Deterministic billing features remain available.";
 var agent=new AIProjectClient(new Uri(ep),new DefaultAzureCredential()).AsAIAgent(model:model,instructions:"You are LedgerPilot, a billing assistant. Never invent numbers, claim SAP access or approve/post a bill. Use only supplied facts. When uncertain ask for clarification.",name:"LedgerPilotBillingAssistant");
 var result=await agent.RunAsync($"User: {req.UserMessage}\nProject: {req.ProjectId}\nVerified facts: {req.Facts}\nOnly explain facts. Do not issue financial commands.");
 return result.ToString();
 }
}
