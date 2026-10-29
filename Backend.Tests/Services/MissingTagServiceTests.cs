using Backend.Models;
using Backend.Services;
using DualView.Shared.Models.DTO;
using DualView.Shared.Models.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Backend.Tests.Services;

public class MissingTagServiceTests
{
    [Fact]
    public async Task AliasUpdates_WaitForPendingTagApplication()
    {
        var database = Substitute.For<IDatabaseService>();
        var parser = Substitute.For<ITagParser>();
        var appEvents = Substitute.For<IAppEvents>();
        var notifier = Substitute.For<IEntityUpdateNotifier>();
        var applicationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishApplication = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondUpdateStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var updatesCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int updateCount = 0;
        database.GetTagAsync(1).Returns(_ =>
        {
            if (Interlocked.Increment(ref updateCount) == 2)
                secondUpdateStarted.TrySetResult();

            return Task.FromResult<Tag?>(new Tag("real tag", TagCategory.DescribeCharacterObject) { Id = 1 });
        });
        database.GetTagAliasesAsync(1).Returns(Task.FromResult(new List<string> { "alias", }));
        parser.ParseTag(Arg.Any<string>()).Returns(Task.FromResult<AppliedTag?>(new AppliedTag(1)));
        database.AddParsedAppliedTagsToMediaAsync(Arg.Any<List<long>>(), Arg.Any<List<AppliedTagDTO>>())
            .Returns(async _ =>
            {
                applicationStarted.TrySetResult();
                await finishApplication.Task;
            });
        notifier.NotifyMissingTagsUpdated().Returns(_ =>
        {
            if (Volatile.Read(ref updateCount) == 1)
                updatesCompleted.TrySetResult();

            return Task.CompletedTask;
        });

        using var provider = new ServiceCollection()
            .AddSingleton(database)
            .AddSingleton(parser)
            .AddSingleton(notifier)
            .BuildServiceProvider();
        using var service = new MissingTagService(provider.GetRequiredService<IServiceScopeFactory>(), appEvents,
            Substitute.For<ILogger<MissingTagService>>());
        await service.ReportTagAsync("alias", MissingTagTarget.MediaFile, 10);

        appEvents.TagUpdated += Raise.Event<IAppEvents.TagUpdatedEventHandler>(1L);
        await applicationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        appEvents.TagUpdated += Raise.Event<IAppEvents.TagUpdatedEventHandler>(1L);

        try
        {
            var delay = Task.Delay(150);
            Assert.Same(delay, await Task.WhenAny(secondUpdateStarted.Task, delay));
        }
        finally
        {
            finishApplication.TrySetResult();
        }

        await updatesCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await secondUpdateStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(await service.GetMissingTagsAsync());
        await database.Received(1)
            .AddParsedAppliedTagsToMediaAsync(Arg.Any<List<long>>(), Arg.Any<List<AppliedTagDTO>>());
    }
}
