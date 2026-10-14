namespace OrderApi.Worker;

/// <summary>
/// Bounded memory of the message ids already delivered by e-mail. RabbitMQ is at-least-once: a message whose ack was
/// lost is delivered again and would send a second e-mail. This only covers redeliveries seen by the same worker
/// process; it is a cheap mitigation, not a distributed exactly-once guarantee.
/// </summary>
public sealed class RecentMessageIds(int capacity)
{
    private readonly Lock _gate = new();
    private readonly HashSet<string> _ids = [];
    private readonly Queue<string> _order = new();

    public bool Contains(string id)
    {
        lock (_gate)
        {
            return _ids.Contains(id);
        }
    }

    public void Add(string id)
    {
        lock (_gate)
        {
            if (!_ids.Add(id))
            {
                return;
            }

            _order.Enqueue(id);
            while (_order.Count > capacity)
            {
                _ids.Remove(_order.Dequeue());
            }
        }
    }
}
