using System.Text.RegularExpressions;

namespace SmartGridSuite.Client.Models.Dispatcher
{
    // Pure parsing/clipboard formatting of the equipment lines already extracted
    // from a single selected technician write-up. No dedupe or historical lookup.
    public static class DispatchEquipmentSerialExtractor
    {
        public static List<DispatchEquipmentChangeLine> Extract(string? equipmentLines)
        {
            var rows = new List<DispatchEquipmentChangeLine>();
            foreach (var line in (equipmentLines ?? string.Empty).Split(
                         new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                var cleaned = line.Trim();
                if (cleaned.Length == 0)
                    continue;

                var match = Regex.Match(
                    cleaned,
                    @"^(?:Found|Left|Bad From Stock)\s+.+?\s+SN:\s*(.+)$",
                    RegexOptions.IgnoreCase);

                var serial = match.Success ? match.Groups[1].Value.Trim() : "";
                if (serial.Equals("(not recorded)", StringComparison.OrdinalIgnoreCase))
                    serial = "";

                rows.Add(new DispatchEquipmentChangeLine
                {
                    DisplayText = cleaned,
                    SerialNumber = serial
                });
            }

            return rows;
        }

        public static string CopyAll(IEnumerable<DispatchEquipmentChangeLine> lines) =>
            string.Join(
                Environment.NewLine,
                lines.Where(x => x.HasSerialNumber).Select(x => x.SerialNumber));
    }
}
