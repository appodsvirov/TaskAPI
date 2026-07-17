using System.Collections.Immutable;

namespace TaskConsumer;

public sealed class TaskEventStore
{
    private const int MaxEvents = 100;
    private readonly object _syncRoot = new();
    private readonly LinkedList<ConsumedTaskEvent> _events = new();

    public event Action? Changed;

    public IReadOnlyList<ConsumedTaskEvent> GetLatest()
    {
        lock (_syncRoot)
        {
            return _events.ToImmutableArray();
        }
    }

    public void Add(ConsumedTaskEvent taskEvent)
    {
        lock (_syncRoot)
        {
            _events.AddFirst(taskEvent);

            while (_events.Count > MaxEvents)
            {
                _events.RemoveLast();
            }
        }

        Changed?.Invoke();
    }
}
