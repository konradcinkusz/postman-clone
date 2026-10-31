using System.Reflection;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using PostmanClone.Web.Api;
using PostmanClone.Web.Components.Shared;

namespace PostmanClone.Web.Tests.Components;

/// <summary>
/// The status strip's lifecycle: when a circuit closes before the first after-render callback runs, the callback must
/// return quietly instead of reading the token of a disposed source (WP07 follow-up, WP18b).
/// </summary>
public sealed class StatusStripTests
{
    [Fact]
    public async Task An_after_render_that_arrives_after_disposal_returns_quietly_and_reads_no_backend()
    {
        using var context = new BunitContext();
        var reader = new CountingStatusReader();
        context.Services.AddSingleton<IBackendStatusReader>(reader);
        var cut = context.Render<StatusStrip>();
        cut.WaitForState(() => reader.Calls >= 1);

        // The circuit closes: the component is disposed before the renderer delivers its after-render callback.
        ((IDisposable)cut.Instance).Dispose();
        var callsBefore = reader.Calls;

        // OnAfterRenderAsync is protected; the renderer calls it, so the test calls it the same way.
        var onAfterRender = typeof(StatusStrip).GetMethod(
            "OnAfterRenderAsync", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
        var task = (Task)onAfterRender.Invoke(cut.Instance, [true])!;

        await task.WaitAsync(Xunit.TestContext.Current.CancellationToken);
        Assert.True(task.IsCompletedSuccessfully);
        Assert.Equal(callsBefore, reader.Calls);
    }

    private sealed class CountingStatusReader : IBackendStatusReader
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task<IReadOnlyList<BackendStatus>> ReadAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            IReadOnlyList<BackendStatus> statuses = [new BackendStatus("authservice", "healthy")];
            return Task.FromResult(statuses);
        }
    }
}
