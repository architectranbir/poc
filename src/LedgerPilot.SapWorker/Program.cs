using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
if(Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")!="Development")throw new InvalidOperationException("Local worker transport requires Development");
var endpoint=new Uri(Environment.GetEnvironmentVariable("SAP_SIMULATOR_URL")??"http://127.0.0.1:8090");
if(!endpoint.IsLoopback||endpoint.Scheme!="http")throw new InvalidOperationException("Local simulator URL required");
var directory=Path.GetFullPath(Environment.GetEnvironmentVariable("SAP_COMMAND_PATH")??"local-state/sap-commands");
Directory.CreateDirectory(directory);
using var ownership=new FileStream(Path.Combine(directory,"worker.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
using var http=new HttpClient{BaseAddress=endpoint,Timeout=TimeSpan.FromSeconds(10)};
var options=new JsonSerializerOptions{PropertyNameCaseInsensitive=true};
foreach(var path in Directory.GetFiles(directory,"*.command.json").Order(StringComparer.Ordinal)){
 var command=JsonSerializer.Deserialize<WorkerCommand>(await File.ReadAllTextAsync(path),options)??throw new InvalidDataException("Empty command");
 var hash=Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(command))));
 var resultPath=path+".result.json";
 if(File.Exists(resultPath)){
 var previous=JsonSerializer.Deserialize<WorkerResult>(await File.ReadAllTextAsync(resultPath),options)!;
 if(previous.CommandHash!=hash)throw new InvalidDataException("Completed command changed");
 continue;
 }
 try{
 JsonElement response;
 if(command.Operation=="Create"){
 using var lookup=await http.GetAsync("/commands/"+Uri.EscapeDataString(command.CommandKey));
 if(lookup.StatusCode==HttpStatusCode.NotFound)response=await Send("/documents",new {command.CommandKey,command.ProjectId,command.Net,command.Currency,command.StateHash});
 else{lookup.EnsureSuccessStatusCode();response=JsonDocument.Parse(await lookup.Content.ReadAsStringAsync()).RootElement.Clone();}
 var request=response.GetProperty("request");
 if(request.GetProperty("commandKey").GetString()!=command.CommandKey||request.GetProperty("projectId").GetString()!=command.ProjectId||request.GetProperty("net").GetDecimal()!=command.Net||request.GetProperty("currency").GetString()!=command.Currency||request.GetProperty("stateHash").GetString()!=command.StateHash)
 throw new InvalidDataException("Reconciled document differs from command");
 }else if(command.Operation=="Post"){
 using var lookup=await http.GetAsync("/documents/"+Uri.EscapeDataString(command.DocumentId??throw new InvalidDataException("Document ID required")));
 lookup.EnsureSuccessStatusCode();var doc=JsonDocument.Parse(await lookup.Content.ReadAsStringAsync()).RootElement.Clone();
 if(doc.GetProperty("status").GetString()=="POSTED"&&doc.GetProperty("postingCommandKey").GetString()==command.CommandKey&&doc.GetProperty("request").GetProperty("stateHash").GetString()==command.StateHash)response=doc;
 else response=await Send($"/documents/{Uri.EscapeDataString(command.DocumentId!)}/post",new {command.CommandKey,command.StateHash});
 }else throw new InvalidDataException("Unknown operation");
 var result=new WorkerResult(hash,response,DateTimeOffset.UtcNow);
 using(var stream=new FileStream(resultPath+".tmp",FileMode.Create,FileAccess.Write,FileShare.None)){JsonSerializer.Serialize(stream,result);stream.Flush(true);}
 File.Move(resultPath+".tmp",resultPath,true);
 }catch(HttpRequestException){Console.Error.WriteLine("Transport failure; command remains pending for retry.");Environment.ExitCode=1;}
}
async Task<JsonElement> Send(string path,object command){
 using var request=new HttpRequestMessage(HttpMethod.Post,path){Content=new StringContent(JsonSerializer.Serialize(command),Encoding.UTF8,"application/json")};
 if(Environment.GetEnvironmentVariable("SIMULATE_RESPONSE_LOSS")=="true")request.Headers.Add("X-Simulate-Response-Loss","true");
 using var response=await http.SendAsync(request);response.EnsureSuccessStatusCode();
 return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
}
record WorkerCommand(string Operation,string CommandKey,string ProjectId,decimal Net,string Currency,string StateHash,string? DocumentId=null);
record WorkerResult(string CommandHash,JsonElement Response,DateTimeOffset CompletedAt);
