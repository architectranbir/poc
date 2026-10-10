using Azure.Identity;
using Azure.Messaging.ServiceBus;
using LedgerPilot.Persistence;
using Npgsql;
using System.Text.Json;

if (Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") != "Development")
    throw new InvalidOperationException("Dispatcher is restricted to Development until deployment hardening is complete.");
await using var source = NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("COMMAND_STORE_CONNECTION")
    ?? throw new InvalidOperationException("COMMAND_STORE_CONNECTION required."));
var store = new CommandStore(source);
// Explicit test bootstrap/import tools. Neither is an application approval endpoint.
if (args is ["--initialize"]) { await store.Initialize(); return; }
if (args is ["--enqueue", var file]) {
    var command = JsonSerializer.Deserialize<SapCommand>(await File.ReadAllTextAsync(file), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new InvalidDataException("Empty command.");
    command.Validate();
    await store.Accept(command.CommandKey, command.CanonicalPayload()); return;
}
if (args is ["--receipt", var key]) { Console.WriteLine(await store.Receipt(key) ?? "null"); return; }
if (args.Length != 0) throw new ArgumentException("Unknown arguments.");
await using var bus = new ServiceBusClient(Environment.GetEnvironmentVariable("SERVICEBUS_NAMESPACE")
    ?? throw new InvalidOperationException("SERVICEBUS_NAMESPACE required."), new DefaultAzureCredential());
await using var sender = bus.CreateSender(Environment.GetEnvironmentVariable("SERVICEBUS_QUEUE") ?? "sap-commands");
using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
while (!stop.IsCancellationRequested) {
    try {
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        attempt.CancelAfter(TimeSpan.FromSeconds(30));
        var sent = await store.PublishNext((key, payload, ct) => sender.SendMessageAsync(
            new ServiceBusMessage(payload) { MessageId = key, ContentType = "application/json" }, ct), attempt.Token);
        if (!sent) await Task.Delay(TimeSpan.FromSeconds(1), stop.Token);
    }
    catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
    catch (Exception ex) when (ex is ServiceBusException or NpgsqlException or OperationCanceledException) {
        Console.Error.WriteLine("Dispatch failed; transaction remains pending for retry.");
        try { await Task.Delay(TimeSpan.FromSeconds(5), stop.Token); }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
    }
}
