namespace SmartGridSuite.Api.Services;

// Pure selection rules, shared with the no-database regression checks.
internal static class DailyAssignmentRolloverSelection
{
    // Caller supplies snapshots ordered newest first and filters lifecycle/status
    // eligibility separately. Never collapse independent crew assignments by ticket.
    internal static IEnumerable<T> LatestPerAssignment<T>(
        IEnumerable<T> newestFirst,
        Func<T, ulong?> sourceAssignmentId)
    {
        return newestFirst
            .Where(x => sourceAssignmentId(x).HasValue)
            .GroupBy(sourceAssignmentId)
            .Select(group => group.First());
    }

    // Call only AFTER all eligible source lifecycles have been advanced.
    // Truck context is deliberately not part of a technician-owned route key.
    internal static IEnumerable<T> OnePerDestination<T>(
        IEnumerable<T> newestFirst,
        Func<T, long> ticketId,
        Func<T, string> targetKey)
    {
        return newestFirst
            .GroupBy(x => (TicketId: ticketId(x), TargetKey: targetKey(x)))
            .Select(group => group.First());
    }
}
