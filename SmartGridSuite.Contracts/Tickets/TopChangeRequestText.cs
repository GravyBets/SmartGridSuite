namespace SmartGridSuite.Contracts.Tickets;

public static class TopChangeRequestText
{
    public static string TopSector(string? top, string? sector)
    {
        var name = (top ?? "").Trim().TrimEnd('-');
        var suffix = (sector ?? "").Trim().TrimStart('-');
        if (name.Length == 0) return suffix;
        if (suffix.Length == 0) return name;
        return suffix.StartsWith(name + "-", StringComparison.OrdinalIgnoreCase)
            ? suffix : $"{name}-{suffix}";
    }

    // Prefer sector VIP, then IP A, then IP B. A missing IP must never produce a guessed subnet.
    public static string BaseIp(params string?[] addresses)
    {
        foreach (var address in addresses)
        {
            var parts = (address ?? "").Trim().Split('.');
            if (parts.Length != 4 || parts.Any(x => x.Length == 0 ||
                x.Any(c => c < '0' || c > '9') || !byte.TryParse(x, out _))) continue;
            return $"{byte.Parse(parts[0])}.{byte.Parse(parts[1])}.{byte.Parse(parts[2])}.xxx";
        }
        return "";
    }

    public static string Format(TopChangeDto request, string? baseIp)
    {
        var ip = string.IsNullOrWhiteSpace(baseIp) ? "[unavailable — verify sector IP]" : baseIp;
        return $"Need to change the TOP site is going to.\n\n" +
            $"Site: {request.Site}\n\n" +
            $"Current TOP: {TopSector(request.OldTop, request.OldSector)}\n" +
            $"Current IP: {request.OldIp}\n\n" +
            $"Requested TOP: {TopSector(request.NewTop, request.NewSector)} (Base IP: {ip})\n\n" +
            "Please provide IP and update tunnels, if applicable.";
    }
}
