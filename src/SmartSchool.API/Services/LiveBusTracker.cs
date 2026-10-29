using System.Collections.Concurrent;
using SmartSchool.Core.DTOs;

namespace SmartSchool.API.Services;

/// <summary>
/// Har bus ki aakhri maloom location memory mein rakhta hai (DB change nahi).
/// Driver ka har GPS ping yahan update hota hai, taake admin/parent map khulte hi
/// active bus ki position mil jaye, agle ping ka intezar na karna pade.
/// Server restart par khali ho jata hai; agla ping phir bhar deta hai.
/// </summary>
public sealed class LiveBusTracker
{
    private readonly ConcurrentDictionary<int, BusLocationUpdate> _last = new();

    public void Update(BusLocationUpdate update) => _last[update.BusId] = update;

    public void Remove(int busId) => _last.TryRemove(busId, out _);

    /// <summary>Sirf wo buses jo abhi active trip par hain (trip band ho to purani location nahi dikhti).</summary>
    public IReadOnlyList<BusLocationUpdate> Snapshot(IEnumerable<int> activeBusIds)
    {
        var list = new List<BusLocationUpdate>();
        foreach (var busId in activeBusIds.Distinct())
        {
            if (_last.TryGetValue(busId, out var loc)) list.Add(loc);
        }
        return list;
    }
}
