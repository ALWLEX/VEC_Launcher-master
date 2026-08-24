using System.Collections.Concurrent;

namespace VECLauncher.Services;

/// <summary>
/// Lightweight EventAggregator for decoupled communication between ViewModels.
/// Publish events from one VM, subscribe in another — no direct references needed.
/// </summary>
public sealed class EventAggregator
{
    private readonly ConcurrentDictionary<Type, List<Delegate>> _handlers = new();

    /// <summary>
    /// Publish an event to all subscribers.
    /// </summary>
    public void Publish<TEvent>(TEvent evt)
    {
        if (_handlers.TryGetValue(typeof(TEvent), out var handlers))
        {
            foreach (var handler in handlers.ToArray())
            {
                ((Action<TEvent>)handler).Invoke(evt);
            }
        }
    }

    /// <summary>
    /// Subscribe to an event. Returns an IDisposable that unsubscribes when disposed.
    /// </summary>
    public IDisposable Subscribe<TEvent>(Action<TEvent> handler)
    {
        var list = _handlers.GetOrAdd(typeof(TEvent), _ => new List<Delegate>());
        lock (list) { list.Add(handler); }

        return new Subscription(() =>
        {
            lock (list) { list.Remove(handler); }
        });
    }

    /// <summary>
    /// Removes all subscriptions. Call on shutdown.
    /// </summary>
    public void Clear()
    {
        _handlers.Clear();
    }

    private sealed class Subscription(Action remove) : IDisposable
    {
        public void Dispose() => remove();
    }
}

// ── Domain Events ──

/// <summary>Raised when the active account changes (login, logout, switch).</summary>
public sealed record AccountChangedEvent(
    Models.MinecraftAccount? Account,
    bool IsLogout);

/// <summary>Raised when an instance is selected or changed.</summary>
public sealed record InstanceSelectedEvent(
    Models.GameInstance? Instance);

/// <summary>Raised when settings are saved.</summary>
public sealed record SettingsSavedEvent(
    LauncherSettings Settings);

/// <summary>Raised when a game session starts or stops.</summary>
public sealed record GameSessionChangedEvent(
    bool IsRunning,
    string? InstanceName);
