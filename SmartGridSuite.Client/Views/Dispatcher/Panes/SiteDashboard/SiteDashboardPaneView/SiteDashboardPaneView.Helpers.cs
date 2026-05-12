using SmartGridSuite.Client.Views.Dispatcher.Panes.SiteDashboard;
using SmartGridSuite.Contracts.SiteDashboard;
using System.Collections;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace SmartGridSuite.Client.Views.Dispatcher.Panes
{
    public partial class SiteDashboardPaneView
    {
        private List<SiteDashboardHistoryRowViewModel> BuildHistoryRows(object? dashboard)
        {
            var result = new List<SiteDashboardHistoryRowViewModel>();

            if (dashboard is null)
                return result;

            var historyItems = FindHistoryEnumerableRecursive(dashboard);
            if (historyItems is null)
                return result;

            foreach (var item in historyItems)
            {
                if (item is null)
                    continue;

                var rawDateText =
                    GetFirstNonEmptyText(item, "VisitDate", "SiteDate", "Date", "CreatedAt");

                var narrative =
                    GetFirstNonEmptyText(item, "Narrative", "Summary", "Notes", "SiteWork")
                    ?? string.Empty;

                var issue =
                    GetFirstNonEmptyText(item, "IssueText", "issueText", "issue_text", "Issue", "SiteIssue", "Site Issue", "Problem")
                    ?? "Other";

                var tech1 =
                    GetFirstNonEmptyText(item, "PrimaryTech", "Tech1", "Technician1")
                    ?? "—";

                var tech2 =
                    GetFirstNonEmptyText(item, "SecondaryTech", "Tech2", "Technician2")
                    ?? "—";

                result.Add(new SiteDashboardHistoryRowViewModel(
                    FormatHistoryDate(rawDateText),
                    tech1,
                    tech2,
                    issue,
                    narrative));
            }

            return result;
        }

        private string BuildTopInfoSummary(object? dashboard)
        {
            var lines = new List<string>();

            AddLine(lines, "Site Status", GetDashboardDataFieldText(dashboard, "SiteStatus", "Status"));
            AddLine(lines, "TOP VIP", GetDashboardDataFieldText(dashboard, "TopVip", "TopVIP"));
            AddLine(lines, "TOP IP A", GetDashboardDataFieldText(dashboard, "TopIpA", "TopIPA"));
            AddLine(lines, "TOP IP B", GetDashboardDataFieldText(dashboard, "TopIpB", "TopIPB"));

            return lines.Count == 0
                ? "No TOP fields were returned for this site yet."
                : string.Join(Environment.NewLine, lines);
        }

        private string BuildTopAccessTitle(object? dashboard)
        {
            var topName = GetDashboardDataFieldText(dashboard, "TopName", "AssociatedTop", "Top") ?? string.Empty;
            var topDescription = GetDashboardDataFieldText(dashboard, "TopDescription", "TopDescr", "ProductionTop") ?? string.Empty;
            var topSector = GetDashboardDataFieldText(dashboard, "TopSector", "Sector") ?? string.Empty;

            var cleanTopName = topName.Replace("_", "-").Trim();
            var cleanSector = topSector.Trim();
            var cleanDescription = topDescription.Trim();

            var left = cleanTopName;

            if (!string.IsNullOrWhiteSpace(cleanSector))
                left = string.IsNullOrWhiteSpace(left) ? cleanSector : $"{left}-{cleanSector}";

            if (string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(cleanDescription))
                return "TOP Access";

            if (string.IsNullOrWhiteSpace(cleanDescription))
                return left;

            if (string.IsNullOrWhiteSpace(left))
                return $"({cleanDescription})";

            return $"{left} ({cleanDescription})";
        }

        private string BuildEquipmentSummary(SiteDashboardResponseDto? dashboard)
        {
            var sb = new StringBuilder();

            static void AddLine(StringBuilder builder, string label, string? value)
            {
                if (string.IsNullOrWhiteSpace(value))
                    return;

                var trimmed = value.Trim();
                if (trimmed == "—" || trimmed.Equals("N/A", StringComparison.OrdinalIgnoreCase))
                    return;

                if (builder.Length > 0)
                    builder.AppendLine();

                builder.Append(label);
                builder.Append(": ");
                builder.Append(trimmed);
            }

            // Enclosure
            AddLine(sb, "Enclosure Model", GetDashboardDataFieldText(
                dashboard,
                "EnclosureModel"));

            AddLine(sb, "Enclosure SN", GetDashboardDataFieldText(
                dashboard,
                "EnclosureSerialNumber"));

            // Primary communications
            AddLine(sb, "Primary Type", GetDashboardDataFieldText(
                dashboard,
                "PrimaryCommType",
                "PrimaryCommunicationsType"));

            AddLine(sb, "Primary Model", GetDashboardDataFieldText(
                dashboard,
                "PrimaryModel"));

            AddLine(sb, "Primary SN", GetDashboardDataFieldText(
                dashboard,
                "PrimaryCommsIdentifier",
                "PrimaryCommunicationsIdentifier",
                "RadioSN",
                "RadioSn"));

            // Secondary communications
            AddLine(sb, "Secondary Type", GetDashboardDataFieldText(
                dashboard,
                "SecondaryCommType",
                "SecondaryCommunicationsType"));

            AddLine(sb, "Secondary Model", GetDashboardDataFieldText(
                dashboard,
                "SecondaryModel"));

            AddLine(sb, "Secondary SN", GetDashboardDataFieldText(
                dashboard,
                "SecondaryCommsIdentifier",
                "SecondaryCommunicationsIdentifier"));

            // Antenna
            AddLine(sb, "Antenna SN", GetDashboardDataFieldText(
                dashboard,
                "AntennaSerialNumber"));

            // Site Hardware / Access Hardware
            AddLine(sb, "Cyberlock SN", GetDashboardDataFieldText(
                dashboard,
                "CyberlockSerialNumber"));

            // Access & Security
            AddLine(sb, "Tunnel PSK", GetDashboardDataFieldText(
                dashboard,
                "TunnelPsk"));

            AddLine(sb, "Secondary WiFi SSID", GetDashboardDataFieldText(
                dashboard,
                "SecondaryCommsSsid",
                "SecondarySsid"));

            AddLine(sb, "Secondary WiFi Password", GetDashboardDataFieldText(
                dashboard,
                "SecondaryCommsPassword",
                "SecondaryPassword"));

            AddLine(sb, "Primary WiFi SSID", GetDashboardDataFieldText(
                dashboard,
                "PrimaryCommsSsid",
                "PrimarySsid"));

            AddLine(sb, "Primary WiFi Password", GetDashboardDataFieldText(
                dashboard,
                "PrimaryCommsPassword",
                "PrimaryPassword"));

            return sb.Length == 0 ? "—" : sb.ToString();
        }

        private static IEnumerable<object?>? FindHistoryEnumerableRecursive(object source)
        {
            var direct = FindHistoryEnumerableOnUnknown(source);
            if (direct is not null)
                return direct;

            var dataProp = source.GetType().GetProperty(
                "Data",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

            if (dataProp is null)
                return null;

            var dataValue = dataProp.GetValue(source);
            return FindHistoryEnumerableOnUnknown(dataValue);
        }

        private static IEnumerable<object?>? FindHistoryEnumerableOnUnknown(object? value)
        {
            if (value is null)
                return null;

            if (value is JsonElement json)
                return FindHistoryEnumerableInJson(json);

            return FindHistoryEnumerableOnObject(value);
        }

        private static IEnumerable<object?>? FindHistoryEnumerableOnObject(object source)
        {
            var props = source.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var prop in props)
            {
                var value = prop.GetValue(source);

                if (value is string || value is not IEnumerable enumerable)
                    continue;

                var elementType = GetEnumerableElementType(prop.PropertyType);

                if (elementType?.Name == "SiteHistoryPreviewDto" ||
                    prop.Name.Contains("history", StringComparison.OrdinalIgnoreCase))
                {
                    return enumerable.Cast<object?>();
                }
            }

            foreach (var prop in props)
            {
                var value = prop.GetValue(source);
                if (value is not JsonElement json)
                    continue;

                var nested = FindHistoryEnumerableInJson(json);
                if (nested is not null)
                    return nested;
            }

            return null;
        }

        private static IEnumerable<object?>? FindHistoryEnumerableInJson(JsonElement json)
        {
            if (json.ValueKind != JsonValueKind.Object)
                return null;

            foreach (var prop in json.EnumerateObject())
            {
                if (prop.Name.Contains("history", StringComparison.OrdinalIgnoreCase) &&
                    prop.Value.ValueKind == JsonValueKind.Array)
                {
                    return prop.Value.EnumerateArray()
                        .Select(x => (object?)x.Clone())
                        .ToList();
                }
            }

            foreach (var prop in json.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Object)
                    continue;

                var nested = FindHistoryEnumerableInJson(prop.Value);
                if (nested is not null)
                    return nested;
            }

            return null;
        }

        private static Type? GetEnumerableElementType(Type type)
        {
            if (type.IsArray)
                return type.GetElementType();

            if (type.IsGenericType)
            {
                var args = type.GetGenericArguments();
                if (args.Length == 1)
                    return args[0];
            }

            var enumerableInterface = type.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType &&
                                     i.GetGenericTypeDefinition() == typeof(IEnumerable<>));

            return enumerableInterface?.GetGenericArguments().FirstOrDefault();
        }

        private static string? GetFirstNonEmptyText(object source, params string[] propertyNames)
        {
            if (source is JsonElement json)
                return FirstNonEmptyJsonProperty(json, propertyNames);

            return FirstNonEmptyObjectProperty(source, propertyNames);
        }

        private static string? FirstNonEmptyObjectProperty(object source, params string[] propertyNames)
        {
            var props = source.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var candidate in propertyNames)
            {
                var normalizedCandidate = NormalizeToken(candidate);

                var prop = props.FirstOrDefault(p => NormalizeToken(p.Name) == normalizedCandidate);
                if (prop is null)
                    continue;

                var value = prop.GetValue(source);
                if (value is null)
                    continue;

                var text = value.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(text))
                    return text;
            }

            return null;
        }

        private static string? FirstNonEmptyJsonProperty(JsonElement json, params string[] propertyNames)
        {
            if (json.ValueKind != JsonValueKind.Object)
                return null;

            foreach (var prop in json.EnumerateObject())
            {
                var normalizedActual = NormalizeToken(prop.Name);

                foreach (var candidate in propertyNames)
                {
                    if (normalizedActual != NormalizeToken(candidate))
                        continue;

                    var text = prop.Value.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(text))
                        return text;
                }
            }

            return null;
        }

        private static string NormalizeToken(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return value
                .Replace("_", "")
                .Replace("-", "")
                .Replace(" ", "")
                .Trim()
                .ToUpperInvariant();
        }

        private static object? GetDashboardDataValue(object? dashboard)
        {
            if (dashboard is null)
                return null;

            var dataProp = dashboard.GetType().GetProperty(
                "Data",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

            return dataProp?.GetValue(dashboard);
        }

        private static string? GetObjectPropertyText(object? source, params string[] propertyNames)
        {
            if (source is null)
                return null;

            return GetFirstNonEmptyText(source, propertyNames);
        }

        private string? GetDashboardDataFieldText(object? dashboard, params string[] candidateNames)
        {
            var data = GetDashboardDataValue(dashboard);
            if (data is null)
                return null;

            if (data is JsonElement json)
                return GetDashboardDataFieldTextFromJson(json, candidateNames);

            return GetDashboardDataFieldTextFromObject(data, candidateNames);
        }

        private string? GetDashboardDataFieldTextFromObject(object source, params string[] candidateNames)
        {
            var props = source.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var candidate in candidateNames)
            {
                var normalizedCandidate = NormalizeToken(candidate);

                var prop = props.FirstOrDefault(p => NormalizeToken(p.Name) == normalizedCandidate);
                if (prop is null)
                    continue;

                var value = prop.GetValue(source);
                if (value is null)
                    continue;

                var text = value.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(text))
                    return text;
            }

            return null;
        }

        private string? GetDashboardDataFieldTextFromJson(JsonElement json, params string[] candidateNames)
        {
            if (json.ValueKind != JsonValueKind.Object)
                return null;

            foreach (var candidate in candidateNames)
            {
                var normalizedCandidate = NormalizeToken(candidate);

                foreach (var prop in json.EnumerateObject())
                {
                    if (NormalizeToken(prop.Name) != normalizedCandidate)
                        continue;

                    var text = prop.Value.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(text))
                        return text;
                }
            }

            return null;
        }

        private string? BuildFullAddress(object? dashboard)
        {
            var direct = GetDashboardDataFieldText(
                dashboard,
                "FullAddress",
                "Address",
                "StreetAddress",
                "FormattedAddress");

            if (!string.IsNullOrWhiteSpace(direct))
                return direct;

            var streetNo = GetDashboardDataFieldText(dashboard, "StreetNo");
            var streetName = GetDashboardDataFieldText(dashboard, "StreetName");
            var city = GetDashboardDataFieldText(dashboard, "City");
            var state = GetDashboardDataFieldText(dashboard, "State", "StateCode");
            var zip = GetDashboardDataFieldText(dashboard, "Zip", "ZipCode");

            var line1 = string.Join(" ", new[] { streetNo, streetName }
                .Where(x => !string.IsNullOrWhiteSpace(x)));

            var cityState = string.Join(", ", new[] { city, state }
                .Where(x => !string.IsNullOrWhiteSpace(x)));

            var line2 = string.Join(" ", new[] { cityState, zip }
                .Where(x => !string.IsNullOrWhiteSpace(x)));

            var combined = string.Join("  ", new[] { line1, line2 }
                .Where(x => !string.IsNullOrWhiteSpace(x)));

            return string.IsNullOrWhiteSpace(combined) ? null : combined;
        }

        private string? BuildCoordinateSummary(object? dashboard)
        {
            var latitude = GetDashboardDataFieldText(dashboard, "Latitude", "Lat");
            var longitude = GetDashboardDataFieldText(dashboard, "Longitude", "Lon", "Lng");

            if (string.IsNullOrWhiteSpace(latitude) && string.IsNullOrWhiteSpace(longitude))
                return null;

            if (string.IsNullOrWhiteSpace(latitude))
                return longitude;

            if (string.IsNullOrWhiteSpace(longitude))
                return latitude;

            return $"{latitude}, {longitude}";
        }

        private static string FormatHistoryDate(string? rawDateText)
        {
            if (string.IsNullOrWhiteSpace(rawDateText))
                return "—";

            if (DateTime.TryParse(rawDateText, out var dt))
                return dt.ToString("MM-dd-yyyy");

            return rawDateText.Trim();
        }

        private static T? DeserializeDashboardData<T>(SiteDashboardResponseDto? dashboard)
            where T : class
        {
            if (dashboard?.Data is JsonElement json &&
                json.ValueKind != JsonValueKind.Null &&
                json.ValueKind != JsonValueKind.Undefined)
            {
                return JsonSerializer.Deserialize<T>(json.GetRawText(), _dashboardJsonOptions);
            }

            if (dashboard?.Data is T typed)
                return typed;

            return null;
        }

        private static readonly JsonSerializerOptions _dashboardJsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private static string DashIfEmpty(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();
        }

        private static string BuildAddress(string? streetNo, string? streetName, string? city, string? stateCode, string? zipCode)
        {
            var line1 = string.Join(" ", new[] { streetNo, streetName }
                .Where(x => !string.IsNullOrWhiteSpace(x)));

            var cityState = string.Join(", ", new[] { city, stateCode }
                .Where(x => !string.IsNullOrWhiteSpace(x)));

            var line2 = string.Join(" ", new[] { cityState, zipCode }
                .Where(x => !string.IsNullOrWhiteSpace(x)));

            var combined = string.Join("  ", new[] { line1, line2 }
                .Where(x => !string.IsNullOrWhiteSpace(x)));

            return string.IsNullOrWhiteSpace(combined) ? "—" : combined;
        }

        private static string BuildCoordinates(decimal? latitude, decimal? longitude)
        {
            if (!latitude.HasValue && !longitude.HasValue)
                return "—";

            if (!latitude.HasValue)
                return longitude!.Value.ToString();

            if (!longitude.HasValue)
                return latitude.Value.ToString();

            return $"{latitude.Value}, {longitude.Value}";
        }

        private static void AddLine(List<string> lines, string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                lines.Add($"{label}: {value}");
        }
    }
}