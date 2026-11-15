using RoninVitals.Modules;
using Xunit;

namespace RoninVitals.Tests;

public class LeaveGuardTests
{
    private sealed class FakeScreen : ICanLeave
    {
        private readonly bool _answer;
        public FakeScreen(bool answer) => _answer = answer;
        public Task<bool> CanLeaveAsync() => Task.FromResult(_answer);
    }

    private sealed class ThrowingScreen : ICanLeave
    {
        public Task<bool> CanLeaveAsync() => throw new InvalidOperationException("broken screen");
    }

    private sealed class FaultedScreen : ICanLeave
    {
        public Task<bool> CanLeaveAsync() => Task.FromException<bool>(new InvalidOperationException("broken screen"));
    }

    private sealed class CancelledScreen : ICanLeave
    {
        public Task<bool> CanLeaveAsync() => Task.FromCanceled<bool>(new CancellationToken(true));
    }

    [Fact]
    public async Task CanLeaveAsync_IsTrue_ForNull()
    {
        Assert.True(await LeaveGuard.CanLeaveAsync(null));
    }

    [Fact]
    public async Task CanLeaveAsync_IsTrue_ForAViewThatIsNotAGuard()
    {
        Assert.True(await LeaveGuard.CanLeaveAsync(new object()));
    }

    [Fact]
    public async Task CanLeaveAsync_ReturnsTheGuardsAnswer_False()
    {
        Assert.False(await LeaveGuard.CanLeaveAsync(new FakeScreen(false)));
    }

    [Fact]
    public async Task CanLeaveAsync_ReturnsTheGuardsAnswer_True()
    {
        Assert.True(await LeaveGuard.CanLeaveAsync(new FakeScreen(true)));
    }

    [Fact]
    public async Task CanLeaveAsync_IsTrue_WhenTheGuardThrows()
    {
        Assert.True(await LeaveGuard.CanLeaveAsync(new ThrowingScreen()));
    }

    [Fact]
    public async Task CanLeaveAsync_IsTrue_WhenTheGuardReturnsAFaultedTask()
    {
        Assert.True(await LeaveGuard.CanLeaveAsync(new FaultedScreen()));
    }

    [Fact]
    public async Task CanLeaveAsync_IsTrue_WhenTheGuardIsCancelled()
    {
        Assert.True(await LeaveGuard.CanLeaveAsync(new CancelledScreen()));
    }
}
