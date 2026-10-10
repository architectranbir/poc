using LedgerPilot.Persistence;
using Npgsql;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
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
var databaseConnection=Environment.GetEnvironmentVariable("COMMAND_STORE_CONNECTION");
await using var database=databaseConnection is null ? null : NpgsqlDataSource.Create(databaseConnection);
var commandStore=database is null ? null : new CommandStore(database);
var transport=Environment.GetEnvironmentVariable("SAP_TRANSPORT") ?? "LocalFiles";
if(transport=="LocalFiles"){
 foreach(var path in Directory.GetFiles(directory,"*.command.json").Order(StringComparer.Ordinal))
 if(!await ProcessFile(path))Environment.ExitCode=1;
}else if(transport=="ServiceBus"){
 if(commandStore is null)throw new InvalidOperationException("ServiceBus requires COMMAND_STORE_CONNECTION");
 var fullyQualifiedNamespace=Environment.GetEnvironmentVariable("SERVICEBUS_NAMESPACE") ?? throw new InvalidOperationException("SERVICEBUS_NAMESPACE required");
 await using var bus=new ServiceBusClient(fullyQualifiedNamespace,new DefaultAzureCredential());
 await using var processor=bus.CreateProcessor(Environment.GetEnvironmentVariable("SERVICEBUS_QUEUE")??"sap-commands",new ServiceBusProcessorOptions{AutoCompleteMessages=false,MaxConcurrentCalls=1,MaxAutoLockRenewalDuration=TimeSpan.FromMinutes(5)});
 processor.ProcessMessageAsync+=async args=>{
 try{
 var command=JsonSerializer.Deserialize<SapCommand>(args.Message.Body.ToString(),options)??throw new InvalidDataException("Empty command");
 if(command.Operation is not ("Create" or "Post" or "CreateAndPost")||string.IsNullOrWhiteSpace(command.CommandKey)||command.CommandKey.Length>128||args.Message.MessageId!=command.CommandKey)throw new InvalidDataException("Invalid command envelope or MessageId");
 command.Validate();
 var canonical=command.CanonicalPayload();
 var key=Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(command.Operation+":"+command.CommandKey)));
 var path=Path.Combine(directory,key+".command.json");
 if(File.Exists(path)&&await File.ReadAllTextAsync(path)!=canonical)throw new InvalidDataException("Command key reused with different content");
 if(!File.Exists(path)){
 using(var stream=new FileStream(path+".tmp",FileMode.Create,FileAccess.Write,FileShare.None)){var bytes=Encoding.UTF8.GetBytes(canonical);stream.Write(bytes);stream.Flush(true);}
 File.Move(path+".tmp",path,true);
 }
 if(await ProcessFile(path))await args.CompleteMessageAsync(args.Message);
 else await args.AbandonMessageAsync(args.Message);
 }catch(Exception ex) when(ex is JsonException or InvalidDataException or ArgumentException){await args.DeadLetterMessageAsync(args.Message,"InvalidCommand","Command validation or reconciliation failed");}
 };
 processor.ProcessErrorAsync+=args=>{Console.Error.WriteLine("Service Bus processing error; inspect redacted operational telemetry.");return Task.CompletedTask;};
 using var stop=new CancellationTokenSource();
 Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;stop.Cancel();};
 await processor.StartProcessingAsync();
 try{await Task.Delay(Timeout.Infinite,stop.Token);}catch(OperationCanceledException){}
 await processor.StopProcessingAsync();
}else throw new InvalidOperationException("SAP_TRANSPORT must be LocalFiles or ServiceBus");
async Task<bool> ProcessFile(string path){
 var command=JsonSerializer.Deserialize<SapCommand>(await File.ReadAllTextAsync(path),options)??throw new InvalidDataException("Empty command");
 command.Validate();
 var canonical=command.CanonicalPayload();
 if(commandStore is not null)await commandStore.VerifyAccepted(command.CommandKey,canonical);
 var hash=Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(command.CanonicalPayload())));
 var resultPath=path+".result.json";
 Guid? leaseOwner=null;
 if(commandStore is not null){
 var claim=await commandStore.Claim(command.CommandKey,canonical);
 if(claim.Result is not null)return true;
 if(claim.Owner is null){Console.Error.WriteLine("Command is already being processed; retry later.");return false;}
 leaseOwner=claim.Owner;
 }
 using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(45));
 if(commandStore is null && File.Exists(resultPath)){
 var previous=JsonSerializer.Deserialize<WorkerResult>(await File.ReadAllTextAsync(resultPath),options)!;
 if(previous.CommandHash!=hash)throw new InvalidDataException("Completed command changed");
 return true;
 }
 try{
 JsonElement response;
 if(command.Operation is "Create" or "CreateAndPost"){
 using var lookup=await http.GetAsync("/commands/"+Uri.EscapeDataString(command.CommandKey),deadline.Token);
 if(lookup.StatusCode==HttpStatusCode.NotFound)response=await Send("/documents",new {command.CommandKey,command.ProjectId,command.Net,command.Currency,command.StateHash},deadline.Token);
 else{lookup.EnsureSuccessStatusCode();response=JsonDocument.Parse(await lookup.Content.ReadAsStringAsync()).RootElement.Clone();}
 var request=response.GetProperty("request");
 if(request.GetProperty("commandKey").GetString()!=command.CommandKey||request.GetProperty("projectId").GetString()!=command.ProjectId||request.GetProperty("net").GetDecimal()!=command.Net||request.GetProperty("currency").GetString()!=command.Currency||request.GetProperty("stateHash").GetString()!=command.StateHash)
 throw new InvalidDataException("Reconciled document differs from command");
 }else if(command.Operation=="Post"){
 using var lookup=await http.GetAsync("/documents/"+Uri.EscapeDataString(command.DocumentId??throw new InvalidDataException("Document ID required")),deadline.Token);
 lookup.EnsureSuccessStatusCode();var doc=JsonDocument.Parse(await lookup.Content.ReadAsStringAsync()).RootElement.Clone();
 if(doc.GetProperty("status").GetString()=="POSTED"&&doc.GetProperty("postingCommandKey").GetString()==command.CommandKey&&doc.GetProperty("request").GetProperty("stateHash").GetString()==command.StateHash)response=doc;
 else response=await Send($"/documents/{Uri.EscapeDataString(command.DocumentId!)}/post",new {command.CommandKey,command.StateHash},deadline.Token);
 }else throw new InvalidDataException("Unknown operation");
 if(command.Operation=="CreateAndPost"){
 var doc=response;
 if(doc.GetProperty("status").GetString()=="POSTED"){
 if(doc.GetProperty("postingCommandKey").GetString()!=command.CommandKey)throw new InvalidDataException("Posting command differs");
 }else response=await Send($"/documents/{Uri.EscapeDataString(doc.GetProperty("id").GetString()!)}/post",new {command.CommandKey,command.StateHash},deadline.Token);
 }
 var result=new WorkerResult(hash,response,DateTimeOffset.UtcNow);
 if(commandStore is not null){await commandStore.CompleteOwned(command.CommandKey,leaseOwner!.Value,JsonSerializer.Serialize(response),deadline.Token);return true;}
 using(var stream=new FileStream(resultPath+".tmp",FileMode.Create,FileAccess.Write,FileShare.None)){JsonSerializer.Serialize(stream,result);stream.Flush(true);}
 File.Move(resultPath+".tmp",resultPath,true);
 return true;
 }catch(Exception ex) when(ex is HttpRequestException or OperationCanceledException or ExecutionLeaseLostException){Console.Error.WriteLine("Execution incomplete; command remains pending for reconciliation.");return false;}
 finally{
 if(commandStore is not null && leaseOwner is not null){
 using var releaseDeadline=new CancellationTokenSource(TimeSpan.FromSeconds(5));
 try{await commandStore.Release(command.CommandKey,leaseOwner.Value,releaseDeadline.Token);}
 catch(Exception ex) when(ex is NpgsqlException or OperationCanceledException){Console.Error.WriteLine("Lease release failed; ownership will expire.");}
 }
 }
}
async Task<JsonElement> Send(string path,object command,CancellationToken ct){
 using var request=new HttpRequestMessage(HttpMethod.Post,path){Content=new StringContent(JsonSerializer.Serialize(command),Encoding.UTF8,"application/json")};
 if(Environment.GetEnvironmentVariable("SIMULATE_RESPONSE_LOSS")=="true")request.Headers.Add("X-Simulate-Response-Loss","true");
 using var response=await http.SendAsync(request,ct);response.EnsureSuccessStatusCode();
 return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
}
record WorkerResult(string CommandHash,JsonElement Response,DateTimeOffset CompletedAt);
