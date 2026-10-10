using LedgerPilot.Persistence;
using LedgerPilot.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

var host = new HostBuilder().ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services => {
        if (Environment.GetEnvironmentVariable("AZURE_FUNCTIONS_ENVIRONMENT") != "Development")
            throw new InvalidOperationException("Workflow host is restricted to Development until deployment hardening is complete.");
        services.AddSingleton(_ => NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("COMMAND_STORE_CONNECTION")
            ?? throw new InvalidOperationException("COMMAND_STORE_CONNECTION required.")));
        services.AddSingleton<CommandStore>();
        services.AddSingleton<PostingReceiptReader>();
    }).Build();
await host.RunAsync();
