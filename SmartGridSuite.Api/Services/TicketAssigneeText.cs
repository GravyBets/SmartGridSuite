using System.Text.RegularExpressions;

namespace SmartGridSuite.Api.Services
{
    /// <summary>
    /// Formats the legacy ticket AssignedTech display field. Published work-list
    /// membership is separate from this cumulative association with a ticket.
    /// </summary>
    public static class TicketAssigneeText
    {
        public static string Merge(string? existing, string? additional)
        {
            var names = Parse(existing);

            foreach (var name in Parse(additional))
            {
                if (!names.Contains(name, StringComparer.OrdinalIgnoreCase))
                    names.Add(name);
            }

            if (names.Count == 0)
                return "(Unassigned)";

            if (names.Count == 1)
                return names[0];

            if (names.Count == 2)
                return names[0] + " & " + names[1];

            return string.Join(", ", names.Take(names.Count - 1)) +
                   " & " + names[^1];
        }

        /// <summary>
        /// An Edit Ticket form submits the combined visible assignees. Changes to
        /// that text are MANUAL deltas; unchanged crew members must not be turned
        /// into permanent manual assignments simply because another name was added.
        /// NULL manual provenance is legacy: preserve the old display to avoid
        /// silently deleting pre-migration direct assignments.
        /// </summary>
        public static string ApplyManualDelta(
            string? previousManual,
            string? previousVisible,
            string? requestedVisible)
        {
            var visibleBefore = Parse(previousVisible);
            var visibleAfter = Parse(requestedVisible);
            var manual = previousManual == null
                ? new List<string>(visibleBefore)
                : Parse(previousManual);

            manual.RemoveAll(name =>
                !visibleAfter.Contains(name, StringComparer.OrdinalIgnoreCase));

            foreach (var name in visibleAfter)
            {
                if (!visibleBefore.Contains(name, StringComparer.OrdinalIgnoreCase) &&
                    !manual.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    manual.Add(name);
                }
            }

            return Merge(string.Empty, string.Join(" & ", manual));
        }

        private static List<string> Parse(string? value)
        {
            var text = (value ?? "").Trim();

            if (text.Length == 0 ||
                text.Equals("(Unassigned)", StringComparison.OrdinalIgnoreCase))
                return new List<string>();

            return Regex.Split(text, @"\s*(?:,|&)\s*")
                .Select(name => name.Trim())
                .Where(name => name.Length > 0 &&
                    !name.Equals("(Unassigned)", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
