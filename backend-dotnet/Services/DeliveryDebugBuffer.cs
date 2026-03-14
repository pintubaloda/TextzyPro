using System.Collections.Concurrent;

namespace Textzy.Api.Services;

public sealed record DeliveryDebugSample(
    DateTime AtUtc,
    Guid TenantId,
    string TenantSlug,
    string Kind,
    string CorrelationId,
    double DurationMs,
    string Detail);

/// <summary>
/// Lightweight in-memory ring buffer for recent delivery timing samples.
/// Intended for production debugging without DB migrations.
/// </summary>
public sealed class DeliveryDebugBuffer
{
    private readonly object _gate = new();
    private readonly int _capacity;
    private readonly Queue<DeliveryDebugSample> _items;

    public DeliveryDebugBuffer(int capacity = 500)
    {
        _capacity = Math.Clamp(capacity, 100, 5000);
        _items = new Queue<DeliveryDebugSample>(_capacity);
    }

    public void Add(DeliveryDebugSample sample)
    {
        lock (_gate)
        {
            while (_items.Count >= _capacity) _items.Dequeue();
            _items.Enqueue(sample);
        }
    }

    public IReadOnlyList<DeliveryDebugSample> Snapshot(Guid tenantId, int take = 200)
    {
        take = Math.Clamp(take, 1, 1000);
        lock (_gate)
        {
            return _items
                .Where(x => tenantId == Guid.Empty || x.TenantId == tenantId)
                .TakeLast(take)
                .ToArray();
        }
    }
}

