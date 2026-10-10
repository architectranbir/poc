using LedgerPilot.Workflows;
using Microsoft.DurableTask;
using Moq;

namespace LedgerPilot.Workflows.Tests;
public sealed class PostingWorkflowTests
{
    static Mock<TaskOrchestrationContext> Context(string key)
    {
        var context=new Mock<TaskOrchestrationContext>();
        context.Setup(x=>x.GetInput<string>()).Returns(key);
        return context;
    }
    [Fact]
    public async Task ExistingReceiptCompletesWithoutTimer()
    {
        var context=Context("post:1");
        context.SetupGet(x=>x.CurrentUtcDateTime).Returns(new DateTime(2026,10,10,0,0,0,DateTimeKind.Utc));
        context.Setup(x=>x.CallActivityAsync<PostingOutcome?>(It.IsAny<TaskName>(),"post:1",It.IsAny<TaskOptions>()))
            .ReturnsAsync(new PostingOutcome("post:1","Posted","SAP-1"));
        var result=await PostingWorkflow.TrackPosting(context.Object);
        Assert.Equal("SAP-1",result.PostingReference);
        context.Verify(x=>x.CreateTimer(It.IsAny<DateTime>(),It.IsAny<CancellationToken>()),Times.Never);
    }
    [Fact]
    public async Task PendingReceiptUsesDurableTimerAndThenCompletes()
    {
        var context=Context("post:2");var now=new DateTime(2026,10,10,0,0,0,DateTimeKind.Utc);
        context.SetupGet(x=>x.CurrentUtcDateTime).Returns(()=>now);
        context.SetupSequence(x=>x.CallActivityAsync<PostingOutcome?>(It.IsAny<TaskName>(),"post:2",It.IsAny<TaskOptions>()))
            .ReturnsAsync((PostingOutcome?)null).ReturnsAsync(new PostingOutcome("post:2","Posted","SAP-2"));
        context.Setup(x=>x.CreateTimer(It.IsAny<DateTime>(),It.IsAny<CancellationToken>()))
            .Callback<DateTime,CancellationToken>((at,_)=>{Assert.Equal(now.AddSeconds(30),at);now=at;}).Returns(Task.CompletedTask);
        Assert.Equal("Posted",(await PostingWorkflow.TrackPosting(context.Object)).Status);
        context.Verify(x=>x.CreateTimer(It.IsAny<DateTime>(),It.IsAny<CancellationToken>()),Times.Once);
    }
    [Fact]
    public async Task MissingReceiptTimesOutWithoutChangingFinancialWork()
    {
        var context=Context("post:3");var start=new DateTime(2026,10,10,0,0,0,DateTimeKind.Utc);var now=start;
        context.SetupGet(x=>x.CurrentUtcDateTime).Returns(()=>now);
        context.Setup(x=>x.CallActivityAsync<PostingOutcome?>(It.IsAny<TaskName>(),"post:3",It.IsAny<TaskOptions>())).ReturnsAsync((PostingOutcome?)null);
        context.Setup(x=>x.CreateTimer(It.IsAny<DateTime>(),It.IsAny<CancellationToken>()))
            .Callback<DateTime,CancellationToken>((at,_)=>now=at).Returns(Task.CompletedTask);
        var result=await PostingWorkflow.TrackPosting(context.Object);
        Assert.Equal("ReconciliationRequired",result.Status);Assert.Null(result.PostingReference);
        Assert.Equal(start.AddMinutes(30),now);
        context.Verify(x=>x.CreateTimer(It.IsAny<DateTime>(),It.IsAny<CancellationToken>()),Times.Exactly(60));
        context.Verify(x=>x.CallActivityAsync<PostingOutcome?>(It.Is<TaskName>(n=>n.Name!=nameof(PostingActivities.ReadPostingReceipt)),It.IsAny<object>(),It.IsAny<TaskOptions>()),Times.Never);
    }
    [Fact]
    public async Task ActivityFailureWaitsAndThenRecovers()
    {
        var context=Context("post:4");var now=new DateTime(2026,10,10,0,0,0,DateTimeKind.Utc);
        context.SetupGet(x=>x.CurrentUtcDateTime).Returns(()=>now);
        context.SetupSequence(x=>x.CallActivityAsync<PostingOutcome?>(It.IsAny<TaskName>(),"post:4",It.IsAny<TaskOptions>()))
            .ThrowsAsync(new TaskFailedException("ReadPostingReceipt",1,new IOException("database unavailable")))
            .ReturnsAsync(new PostingOutcome("post:4","Posted","SAP-4"));
        context.Setup(x=>x.CreateTimer(It.IsAny<DateTime>(),It.IsAny<CancellationToken>()))
            .Callback<DateTime,CancellationToken>((at,_)=>now=at).Returns(Task.CompletedTask);
        Assert.Equal("SAP-4",(await PostingWorkflow.TrackPosting(context.Object)).PostingReference);
        context.Verify(x=>x.CreateTimer(It.IsAny<DateTime>(),It.IsAny<CancellationToken>()),Times.Once);
    }
    [Fact]
    public async Task EmptyInputFailsBeforeAnyActivity()
    {
        var context=Context("");
        await Assert.ThrowsAsync<ArgumentException>(()=>PostingWorkflow.TrackPosting(context.Object));
        context.Verify(x=>x.CallActivityAsync<PostingOutcome?>(It.IsAny<TaskName>(),It.IsAny<object>(),It.IsAny<TaskOptions>()),Times.Never);
    }
    [Fact]
    public void InstanceIdsAreStableAndSeparateCommands()
    {
        Assert.Equal(PostingActivities.InstanceId("post:1"),PostingActivities.InstanceId("post:1"));
        Assert.NotEqual(PostingActivities.InstanceId("post:1"),PostingActivities.InstanceId("post:2"));
        Assert.Matches("^posting-[0-9a-f]{64}$",PostingActivities.InstanceId("post:1"));
    }
}
