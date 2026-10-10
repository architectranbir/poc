using LedgerPilot.Persistence;
using Npgsql;

namespace LedgerPilot.Persistence.Tests;
[Collection("Database")]
public sealed class ExecutionLeaseTests
{
    static NpgsqlDataSource Source() => NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("POSTGRES_TEST_CONNECTION")!);

    [Fact]
    public async Task OnlyOneConcurrentWorkerClaimsAndCompletedDeliveryIsReplayed()
    {
        await using var source=Source(); var store=new CommandStore(source);await store.Initialize();
        var key=Guid.NewGuid().ToString();await store.Accept(key,"payload");
        var claims=await Task.WhenAll(Enumerable.Range(0,8).Select(_=>store.Claim(key,"payload")));
        var owner=Assert.Single(claims,x=>x.Owner is not null).Owner!.Value;
        Assert.All(claims,x=>Assert.Null(x.Result));
        await Assert.ThrowsAsync<ExecutionLeaseLostException>(()=>store.CompleteOwned(key,Guid.NewGuid(),"wrong"));
        Assert.Null(await store.Receipt(key));
        await store.CompleteOwned(key,owner,"SAP-1");
        var replay=await store.Claim(key,"payload");Assert.Null(replay.Owner);Assert.Equal("SAP-1",replay.Result);
        await Assert.ThrowsAsync<InvalidDataException>(()=>store.Claim(key,"changed"));
    }

    [Fact]
    public async Task ExpiredClaimIsReplacedAndOldOwnerCannotCommitOrReleaseNewLease()
    {
        await using var source=Source();var store=new CommandStore(source);await store.Initialize();
        var key=Guid.NewGuid().ToString();await store.Accept(key,"payload");
        var old=(await store.Claim(key,"payload")).Owner!.Value;
        // Simulate a crashed worker and elapsed database clock without sleeping.
        await using(var expire=source.CreateCommand("UPDATE lp_inbox SET lease_until=clock_timestamp()-interval '1 second' WHERE command_key=$1")){
            expire.Parameters.AddWithValue(key);await expire.ExecuteNonQueryAsync();
        }
        var current=(await store.Claim(key,"payload")).Owner!.Value;Assert.NotEqual(old,current);
        await Assert.ThrowsAsync<ExecutionLeaseLostException>(()=>store.CompleteOwned(key,old,"stale"));
        await store.Release(key,old);
        Assert.Null((await store.Claim(key,"payload")).Owner);
        Assert.Null(await store.Receipt(key));
        await store.CompleteOwned(key,current,"reconciled");Assert.Equal("reconciled",await store.Receipt(key));
    }

    [Fact]
    public async Task FailedAttemptReleasesOwnershipForRetryAndUnknownCommandsCannotClaim()
    {
        await using var source=Source();var store=new CommandStore(source);await store.Initialize();
        var key=Guid.NewGuid().ToString();
        await Assert.ThrowsAsync<InvalidDataException>(()=>store.Claim(key,"payload"));
        await store.Accept(key,"payload");var first=(await store.Claim(key,"payload")).Owner!.Value;
        await store.Release(key,first);
        var retry=(await store.Claim(key,"payload")).Owner!.Value;Assert.NotEqual(first,retry);
        await Assert.ThrowsAsync<ExecutionLeaseLostException>(()=>store.CompleteOwned(key,first,"stale"));
        await store.CompleteOwned(key,retry,"done");
    }
}
