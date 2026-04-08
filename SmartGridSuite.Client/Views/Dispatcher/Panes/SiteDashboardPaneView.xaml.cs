using SmartGridSuite.Client.Services;
using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Text.Json;
using System.Globalization;

namespace SmartGridSuite.Client.Views.Dispatcher.Panes
{
    public partial class SiteDashboardPaneView : UserControl
    {
        private readonly ApiClient _api;
        private CancellationTokenSource? _loadCts;

        private sealed record HistoryRowVm(string DateText, string TechsText, string SummaryText);
        private sealed record DashboardFieldVm(string Label, string Value);

        public SiteDashboardPaneView()
            : this(new ApiClient("https://localhost:7140"))
        {
        }

        public SiteDashboardPaneView(ApiClient api)
        {
            InitializeComponent();
            _api = api;
            ResetDisplay();
        }

        private async void LoadButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadAsync();
        }

        private async void SiteIdTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
                return;

            e.Handled = true;
            await LoadAsync();
        }

        private async Task LoadAsync()
        {
            var siteId = (SiteIdTextBox.Text ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(siteId))
            {
                StatusTextBlock.Text = "Enter a site ID first.";
                return;
            }

            _loadCts?.Cancel();
            _loadCts?.Dispose();
            _loadCts = new CancellationTokenSource();

            try
            {
                SetLoadingState(true, $"Loading {siteId}...");
                ResetDisplay(clearStatus: false);

                var dashboard = await _api.GetSiteDashboardAsync(siteId, _loadCts.Token);
                var routeInfo = await _api.GetSiteDashboardRouteInfoAsync(siteId, _loadCts.Token);

                SiteIdValueTextBlock.Text = dashboard?.SiteId ?? siteId;
                DashboardKindValueTextBlock.Text = dashboard?.DashboardKind?.ToString() ?? "Unknown";
                RouteSiteTypeValueTextBlock.Text = routeInfo?.SiteType ?? "Unknown";

                BindDashboardDetails(dashboard);

                BindHistoryPreview(dashboard);

                StatusTextBlock.Text = $"Loaded {SiteIdValueTextBlock.Text}.";
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
            catch (Exception ex)
            {
                ResetDisplay(clearStatus: false);
                StatusTextBlock.Text = $"Load failed: {ex.Message}";
            }
            finally
            {
                SetLoadingState(false, StatusTextBlock.Text);
            }
        }

        private void ResetDisplay(bool clearStatus = true)
        {
            SiteIdValueTextBlock.Text = "—";
            DashboardKindValueTextBlock.Text = "—";
            RouteSiteTypeValueTextBlock.Text = "—";

            DashboardDetailsKindTitleTextBlock.Text = "Dashboard Type";
            DashboardDetailsListView.ItemsSource = null;
            DashboardDetailsListView.Visibility = Visibility.Collapsed;
            DashboardDetailsEmptyTextBlock.Visibility = Visibility.Visible;
            DashboardDetailsEmptyTextBlock.Text = "Load a site to show dashboard details.";

            HistoryListView.ItemsSource = null;
            HistoryListView.Visibility = Visibility.Collapsed;
            HistoryEmptyTextBlock.Visibility = Visibility.Visible;
            HistoryEmptyTextBlock.Text = "No history loaded yet.";

            if (clearStatus)
            {
                StatusTextBlock.Text = "Enter a site ID and load the dashboard shell.";
            }
        }

        private void SetLoadingState(bool isLoading, string statusText)
        {
            LoadButton.IsEnabled = !isLoading;
            SiteIdTextBox.IsEnabled = !isLoading;
            StatusTextBlock.Text = statusText;
        }

        
        //Helpers
        private void BindHistoryPreview(object? dashboard)
        {
            var rows = BuildHistoryRows(dashboard);

            if (rows.Count == 0)
            {
                HistoryListView.ItemsSource = null;
                HistoryListView.Visibility = Visibility.Collapsed;
                HistoryEmptyTextBlock.Visibility = Visibility.Visible;
                HistoryEmptyTextBlock.Text = "No history preview entries were returned for this site.";
                return;
            }

            HistoryListView.ItemsSource = rows;
            HistoryListView.Visibility = Visibility.Visible;
            HistoryEmptyTextBlock.Visibility = Visibility.Collapsed;
        }

        private List<HistoryRowVm> BuildHistoryRows(object? dashboard)
        {
            var result = new List<HistoryRowVm>();

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
                    GetFirstNonEmptyText(item, "DateText", "SiteDateText", "VisitDateText",
                                               "SiteDate", "VisitDate", "Date", "CreatedAt");

                var dateText = FormatHistoryDate(rawDateText);

                var tech1 = GetFirstNonEmptyText(item, "Tech1", "PrimaryTech", "Technician1", "TechName1");
                var tech2 = GetFirstNonEmptyText(item, "Tech2", "SecondaryTech", "Technician2", "TechName2");

                var techs = string.Join(", ", new[] { tech1, tech2 }.Where(x => !string.IsNullOrWhiteSpace(x)));
                if (string.IsNullOrWhiteSpace(techs))
                {
                    techs =
                        GetFirstNonEmptyText(item, "Techs", "TechNames", "Technicians", "AssignedTechs")
                        ?? "—";
                }

                var summary =
                    GetFirstNonEmptyText(item,
                        "Summary",
                        "SummaryText",
                        "Narrative",
                        "WorkSummary",
                        "SiteWorkSummary",
                        "Description",
                        "SiteWork",
                        "site_work",
                        "Work",
                        "WorkPerformed",
                        "Notes",
                        "Resolution")
                    ?? "—";

                result.Add(new HistoryRowVm(dateText, techs, summary));                
            }

            return result;
        }

        private static IEnumerable<object?>? FindHistoryEnumerableRecursive(object source)
        {
            var direct = FindHistoryEnumerableOnUnknown(source);
            if (direct is not null)
                return direct;

            var dataProp = source.GetType().GetProperty(
                "Data",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

            if (dataProp is not null)
            {
                var dataValue = dataProp.GetValue(source);
                var fromData = FindHistoryEnumerableOnUnknown(dataValue);
                if (fromData is not null)
                    return fromData;
            }

            return null;
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

                if (value is JsonElement json)
                {
                    var fromJson = FindHistoryEnumerableFromNamedJsonProperty(prop.Name, json);
                    if (fromJson is not null)
                        return fromJson;

                    continue;
                }

                if (value is string || value is not System.Collections.IEnumerable enumerable)
                    continue;

                var elementType = GetEnumerableElementType(prop.PropertyType);
                if (elementType?.Name == "SiteHistoryPreviewDto")
                    return enumerable.Cast<object?>();

                if (prop.Name.Contains("history", StringComparison.OrdinalIgnoreCase))
                    return enumerable.Cast<object?>();
            }

            return null;
        }

        private static IEnumerable<object?>? FindHistoryEnumerableFromNamedJsonProperty(string propertyName, JsonElement json)
        {
            if (propertyName.Contains("history", StringComparison.OrdinalIgnoreCase) &&
                json.ValueKind == JsonValueKind.Array)
            {
                return json.EnumerateArray()
                           .Select(x => (object?)x.Clone())
                           .ToList();
            }

            return FindHistoryEnumerableInJson(json);
        }

        private static IEnumerable<object?>? FindHistoryEnumerableInJson(JsonElement json)
        {
            if (json.ValueKind == JsonValueKind.Object)
            {
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
                    var nested = FindHistoryEnumerableInJson(prop.Value);
                    if (nested is not null)
                        return nested;
                }
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
            foreach (var name in propertyNames)
            {
                var prop = source.GetType().GetProperty(
                    name,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

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
                var normalizedActual = NormalizePropertyName(prop.Name);

                foreach (var name in propertyNames)
                {
                    var normalizedExpected = NormalizePropertyName(name);

                    if (!string.Equals(normalizedActual, normalizedExpected, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var text = prop.Value.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(text))
                        return text;
                }
            }

            return null;
        }

        private static string NormalizePropertyName(string name)
        {
            return name
                .Replace("_", "")
                .Replace("-", "")
                .Replace(" ", "")
                .Trim();
        }        

        private static string FormatHistoryDate(string? rawDateText)
        {
            if (string.IsNullOrWhiteSpace(rawDateText))
                return "—";

            if (DateTime.TryParse(rawDateText, out var dt))
                return dt.ToString("MM-dd-yyyy");

            return rawDateText.Trim();
        }

        private static string NormalizeToken(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return value
                .Replace("-", "")
                .Replace("_", "")
                .Replace(" ", "")
                .Trim()
                .ToUpperInvariant();
        }

        private void BindDashboardDetails(object? dashboard)
        {
            DashboardDetailsKindTitleTextBlock.Text = GetDashboardKindDisplayName(dashboard);

            var rows = BuildDashboardDetailRows(dashboard);

            if (rows.Count == 0)
            {
                DashboardDetailsListView.ItemsSource = null;
                DashboardDetailsListView.Visibility = Visibility.Collapsed;
                DashboardDetailsEmptyTextBlock.Visibility = Visibility.Visible;
                DashboardDetailsEmptyTextBlock.Text = "No dashboard detail fields were available for this site yet.";
                return;
            }

            DashboardDetailsListView.ItemsSource = rows;
            DashboardDetailsListView.Visibility = Visibility.Visible;
            DashboardDetailsEmptyTextBlock.Visibility = Visibility.Collapsed;
        }

        private List<DashboardFieldVm> BuildDashboardDetailRows(object? dashboard)
        {
            var result = new List<DashboardFieldVm>();

            var data = GetDashboardDataValue(dashboard);
            if (data is null)
                return result;

            if (data is JsonElement json)
                return BuildDashboardDetailRowsFromJson(json);

            return BuildDashboardDetailRowsFromObject(data);
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

        private List<DashboardFieldVm> BuildDashboardDetailRowsFromObject(object source)
        {
            var rows = new List<DashboardFieldVm>();

            var props = source.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var prop in props.OrderBy(p => p.Name))
            {
                if (ShouldSkipDashboardField(prop.Name))
                    continue;

                var value = prop.GetValue(source);
                if (!TryConvertDashboardFieldValue(value, out var text))
                    continue;

                rows.Add(new DashboardFieldVm(HumanizeLabel(prop.Name), text));
            }

            return rows;
        }

        private List<DashboardFieldVm> BuildDashboardDetailRowsFromJson(JsonElement json)
        {
            var rows = new List<DashboardFieldVm>();

            if (json.ValueKind != JsonValueKind.Object)
                return rows;

            foreach (var prop in json.EnumerateObject().OrderBy(p => p.Name))
            {
                if (ShouldSkipDashboardField(prop.Name))
                    continue;

                if (!TryConvertDashboardFieldValue(prop.Value, out var text))
                    continue;

                rows.Add(new DashboardFieldVm(HumanizeLabel(prop.Name), text));
            }

            return rows;
        }

        private static bool ShouldSkipDashboardField(string name)
        {
            var normalized = NormalizeToken(name);

            if (string.IsNullOrWhiteSpace(normalized))
                return true;

            if (normalized.Contains("HISTORY"))
                return true;

            if (normalized == "SITEID" ||
                normalized == "DASHBOARDKIND" ||
                normalized == "KIND" ||
                normalized == "NARRATIVE")
                return true;

            return false;
        }

        private static bool TryConvertDashboardFieldValue(object? value, out string text)
        {
            text = string.Empty;

            if (value is null)
                return false;

            if (value is JsonElement json)
                return TryConvertDashboardFieldJsonValue(json, out text);

            var type = value.GetType();

            if (value is string s)
            {
                s = s.Trim();
                if (string.IsNullOrWhiteSpace(s))
                    return false;

                if (s.Length > 250)
                    return false;

                text = s;
                return true;
            }

            if (value is DateTime dt)
            {
                text = dt.ToString("MM-dd-yyyy", CultureInfo.InvariantCulture);
                return true;
            }

            if (type.IsEnum || value is bool || value is byte || value is sbyte ||
                value is short || value is ushort || value is int || value is uint ||
                value is long || value is ulong || value is float || value is double ||
                value is decimal)
            {
                text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
                return !string.IsNullOrWhiteSpace(text);
            }

            return false;
        }

        private static bool TryConvertDashboardFieldJsonValue(JsonElement value, out string text)
        {
            text = string.Empty;

            switch (value.ValueKind)
            {
                case JsonValueKind.String:
                    {
                        var raw = value.ToString()?.Trim();
                        if (string.IsNullOrWhiteSpace(raw))
                            return false;

                        if (DateTime.TryParse(raw, out var dt))
                        {
                            text = dt.ToString("MM-dd-yyyy", CultureInfo.InvariantCulture);
                            return true;
                        }

                        if (raw.Length > 250)
                            return false;

                        text = raw;
                        return true;
                    }

                case JsonValueKind.Number:
                case JsonValueKind.True:
                case JsonValueKind.False:
                    text = value.ToString();
                    return !string.IsNullOrWhiteSpace(text);

                default:
                    return false;
            }
        }

        private string GetDashboardKindDisplayName(object? dashboard)
        {
            var kindProp = dashboard?.GetType().GetProperty(
                "DashboardKind",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

            var raw = kindProp?.GetValue(dashboard)?.ToString();
            var kind = NormalizeToken(raw);

            return kind switch
            {
                "AMS" => "AMS / MR",
                "AMSMR" => "AMS / MR",
                "MR" => "AMS / MR",
                "DACS" => "DACS",
                "RX" => "RX",
                "IGSD" => "IGSD",
                _ => "Dashboard Type"
            };
        }

        private static string HumanizeLabel(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return raw;

            var text = raw.Replace("_", " ").Replace("-", " ").Trim();

            var chars = new List<char>();
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];

                if (i > 0 &&
                    char.IsUpper(c) &&
                    text[i - 1] != ' ' &&
                    !char.IsUpper(text[i - 1]))
                {
                    chars.Add(' ');
                }

                chars.Add(c);
            }

            var result = new string(chars.ToArray());

            result = result.Replace(" Ip", " IP")
                           .Replace(" Sn", " SN")
                           .Replace(" Gps", " GPS")
                           .Replace(" Rtu", " RTU")
                           .Replace(" Wan", " WAN")
                           .Replace(" Lan", " LAN")
                           .Replace(" Psk", " PSK")
                           .Replace(" Id", " ID");

            return result;
        }
    }
}