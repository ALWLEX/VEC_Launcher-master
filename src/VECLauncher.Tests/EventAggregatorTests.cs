using VECLauncher.Services;

namespace VECLauncher.Tests;

public class EventAggregatorTests
{
    [Fact]
    public void Publish_InvokesSubscribers()
    {
        var ea = new EventAggregator();
        var received = false;
        ea.Subscribe<string>(_ => received = true);
        ea.Publish("hello");
        Assert.True(received);
    }

    [Fact]
    public void Publish_PassesEventToSubscriber()
    {
        var ea = new EventAggregator();
        string? received = null;
        ea.Subscribe<string>(s => received = s);
        ea.Publish("test");
        Assert.Equal("test", received);
    }

    [Fact]
    public void Subscribe_ReturnsDisposable()
    {
        var ea = new EventAggregator();
        var count = 0;
        var sub = ea.Subscribe<string>(_ => count++);
        ea.Publish("a");
        Assert.Equal(1, count);
        sub.Dispose();
        ea.Publish("b");
        Assert.Equal(1, count); // not incremented
    }

    [Fact]
    public void MultipleSubscribers_AllNotified()
    {
        var ea = new EventAggregator();
        var count = 0;
        ea.Subscribe<int>(_ => count++);
        ea.Subscribe<int>(_ => count++);
        ea.Subscribe<int>(_ => count++);
        ea.Publish(42);
        Assert.Equal(3, count);
    }

    [Fact]
    public void Clear_RemovesAllSubscriptions()
    {
        var ea = new EventAggregator();
        var count = 0;
        ea.Subscribe<string>(_ => count++);
        ea.Publish("a");
        Assert.Equal(1, count);
        ea.Clear();
        ea.Publish("b");
        Assert.Equal(1, count); // not incremented
    }

    [Fact]
    public void DifferentEventTypes_DontInterfere()
    {
        var ea = new EventAggregator();
        var stringReceived = false;
        var intReceived = false;
        ea.Subscribe<string>(_ => stringReceived = true);
        ea.Subscribe<int>(_ => intReceived = true);
        ea.Publish("hello");
        Assert.True(stringReceived);
        Assert.False(intReceived);
    }

    [Fact]
    public void Publish_NoSubscribers_DoesNotThrow()
    {
        var ea = new EventAggregator();
        var ex = Record.Exception(() => ea.Publish("test"));
        Assert.Null(ex);
    }

    [Fact]
    public void DomainEvents_Work()
    {
        var ea = new EventAggregator();
        AccountChangedEvent? received = null;
        ea.Subscribe<AccountChangedEvent>(e => received = e);
        
        var evt = new AccountChangedEvent(null, IsLogout: true);
        ea.Publish(evt);
        
        Assert.NotNull(received);
        Assert.Null(received!.Account);
        Assert.True(received.IsLogout);
    }
}
