using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using SmartGridSuite.Api.Configuration;
using SmartGridSuite.Api.Controllers;
using SmartGridSuite.Api.Data;
using SmartGridSuite.Api.Data.Entities;
using SmartGridSuite.Api.Services;
using SmartGridSuite.Api.Services.ParentSync;
using SmartGridSuite.Contracts.Dispatcher;
using System.Linq.Expressions;
using System.Reflection;
using System.Xml.Linq;

void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    Console.WriteLine($"PASS: {message}");
}

var databaseName = Guid.NewGuid().ToString();
var databaseRoot = new InMemoryDatabaseRoot();
var options = new DbContextOptionsBuilder<SmartGridDbContext>().UseInMemoryDatabase(databaseName, databaseRoot).Options;
await using var db = new SmartGridDbContext(options);
var now = DateTime.UtcNow;
db.Tickets.AddRange(
    new TicketEntity { Id = 1, Site = "100MR", Notification = "140500001", CurrentWorkOrder = "1234567890", LastActivityAt = now, Notes = "336546122" },
    new TicketEntity { Id = 2, Site = "100MR", Notification = "140500002", CurrentWorkOrder = "1234567891", LastActivityAt = now.AddHours(-1) },
    new TicketEntity { Id = 3, Site = "200IG", Notification = "140500003", CurrentWorkOrder = "1234567892", Summary = "Mention 140500001 and 100MR", Notes = "336546122", LastActivityAt = now });
db.SiteHistory.AddRange(
    new SiteHistoryEntity { HistoryId = 1, SiteId = "100MR", Narrative = "336546122", VisitDate = now },
    new SiteHistoryEntity { HistoryId = 2, SiteId = "200IG", Narrative = "336546122", VisitDate = now },
    new SiteHistoryEntity { HistoryId = 3, SiteId = "100MR", Narrative = "336546122", VisitDate = now, IsDeleted = true });
db.SiteNotes.AddRange(
    new SiteNoteEntity { Id = 1, SiteId = "100MR", NoteText = "336546122", CreatedAt = now },
    new SiteNoteEntity { Id = 2, SiteId = "200IG", NoteText = "unrelated", CreatedAt = now });
await db.SaveChangesAsync();

// Blank Parent DB configuration deliberately exercises fallback without contacting it.
var factory = new ParentDatabaseConnectionFactory(Options.Create(new ParentDatabaseOptions()));
var service = new DeviceLookupService(db, factory);
var site = await service.SearchAsync(" 100MR ", DeviceLookupSearchType.Site);
Check(site.Query == "100MR" && site.RelatedSiteIds.SequenceEqual(new[] { "100MR" }), "Site search trims the identifier and keeps one related site");
Check(site.Tickets.Count == 2 && site.SiteHistory.Count == 1 && site.SiteNotes.Count == 1,
    "Site search returns related tickets/history/notes and excludes deleted history");
Check(site.Warning.Contains("Parent DB"), "Parent failure retains SmartGridSuite results with a warning");
var notification = await service.SearchAsync("140500001", DeviceLookupSearchType.Notification);
Check(notification.Warning == "" && notification.Tickets.Count == 2 && notification.Tickets.All(x => x.Site == "100MR"),
    "Notification matches its identifier, skips Parent DB, and excludes mentions on other sites");
var workOrder = await service.SearchAsync("1234567890", DeviceLookupSearchType.WorkOrder);
Check(workOrder.Warning == "" && workOrder.SiteHistory.Count == 1 && workOrder.SiteNotes.Count == 1,
    "Work Order discovers its site and includes related records without Parent DB");
var serial = await service.SearchAsync("336546122", DeviceLookupSearchType.DeviceSerialNumber);
Check(serial.Tickets.Count == 0 && serial.SiteHistory.Count == 0 && serial.SiteNotes.Count == 0,
    "Focused serial search avoids global ticket/history/note text scans");
var text = await service.SearchAsync("336546122", DeviceLookupSearchType.HistoryText);
Check(text.Warning == "" && text.RelatedSiteIds.Count == 2 && text.Tickets.Count == 3 && text.SiteHistory.Count == 2,
    "Opt-in text search finds historical identifiers, deduplicates rows, and loads both related sites");

using var canceled = new CancellationTokenSource();
canceled.Cancel();
try
{
    await service.SearchAsync("100MR", DeviceLookupSearchType.Site, canceled.Token);
    throw new InvalidOperationException("Canceled request returned success");
}
catch (OperationCanceledException) { Check(true, "Caller cancellation propagates instead of returning success"); }

// Simulate a provider canceling a later category, after tickets have been read.
await using var slowDb = new TimeoutTestDbContext(new DbContextOptionsBuilder<SmartGridDbContext>()
    .UseInMemoryDatabase(databaseName, databaseRoot).AddInterceptors(new CancelHistoryQuery()).Options);
var partial = await new DeviceLookupService(slowDb, factory).SearchAsync("100MR", DeviceLookupSearchType.Site);
Check(partial.Tickets.Count == 2 && partial.SiteHistory.Count == 0 && partial.Warning.Contains("time limit"),
    $"Later query cancellation preserves already-read tickets and identifies incomplete results (tickets={partial.Tickets.Count}, history={partial.SiteHistory.Count}, warning={partial.Warning})");

var controller = new DeviceLookupController(service);
Check((await controller.Search("x", default, (DeviceLookupSearchType)999)).Result is BadRequestObjectResult,
    "Controller rejects undefined search types");
Check((await controller.Search(new string('x', 251), default)).Result is BadRequestObjectResult,
    "Controller rejects oversized identifiers");

// Verify native SQL parameter conversion for numeric SIMs and padded text identifiers.
var metadataField = typeof(DeviceLookupService).GetField("_parentColumnTypes", BindingFlags.NonPublic | BindingFlags.Instance)!;
var metadata = (Dictionary<string, (string SqlType, int? MaxLength)>)metadataField.GetValue(service)!;
metadata["SGC_EQUIP.PMR.ATTSLOT1"] = ("decimal(20,0)", null);
metadata["SGC_EQUIP.PMR.SN"] = ("varchar(12)", 12);
var predicateMethod = typeof(DeviceLookupService).GetMethod("ParentPredicate", BindingFlags.NonPublic | BindingFlags.Instance)!;
var numericPredicate = (string)predicateMethod.Invoke(service, new object[] { "sgc_equip.PMR", "p", new[] { "ATTSlot1" } })!;
var textPredicate = (string)predicateMethod.Invoke(service, new object[] { "sgc_equip.PMR", "p", new[] { "SN" } })!;
Check(numericPredicate.Contains("p.ATTSlot1 = TRY_CONVERT(decimal(20,0), @Query)"),
    "SIM comparison preserves 20-digit precision and converts the parameter rather than the indexed column");
Check(textPredicate.Contains("LEN(@Query) <= 12") && textPredicate.Contains("p.SN = TRY_CONVERT(varchar(12), @Query)"),
    "Text identifier comparison prevents false matches caused by parameter truncation");

var root = FindRepositoryRoot();
XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
var shell = XDocument.Load(Path.Combine(root, "SmartGridSuite.Client/Views/Dispatcher/DispatcherShellWindow.xaml"));
var navs = shell.Descendants(wpf + "ListBox").Where(x => ((string?)x.Attribute(xaml + "Name"))?.StartsWith("NavList") == true).ToList();
var expanded = navs[0].Elements(wpf + "ListBoxItem").Select(x => (string?)x.Attribute("Tag")).ToList();
var collapsed = navs[1].Elements(wpf + "ListBoxItem").Select(x => (string?)x.Attribute("Tag")).ToList();
Check(expanded.SequenceEqual(collapsed) && expanded.Last() == "Device Lookup" && expanded[1] == "Tasks",
    "Expanded and collapsed navigation keep identical ordering with Device Lookup last and Tasks second");
var pane = XDocument.Load(Path.Combine(root, "SmartGridSuite.Client/Views/Dispatcher/Panes/DeviceLookupPaneView.xaml"));
Check(pane.Descendants(wpf + "TabControl").All(x => (string?)x.Attribute("Style") == "{StaticResource InnerTabStyle}") &&
    pane.Descendants(wpf + "TabItem").All(x => (string?)x.Attribute("Style") == "{StaticResource InnerTabItemStyle}") &&
    pane.Descendants(wpf + "DataGrid").All(x => (string?)x.Attribute("Style") == "{StaticResource ThemedDataGridStyle}"),
    "Every result tab and grid uses existing theme-aware styles");
Check(pane.Descendants(wpf + "ComboBoxItem").Select(x => (string?)x.Attribute("Tag"))
    .SequenceEqual(Enum.GetNames<DeviceLookupSearchType>()), "Client search options match all API search types");
Console.WriteLine("Device Lookup offline checks passed; no company database was contacted.");

static string FindRepositoryRoot()
{
    for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        if (File.Exists(Path.Combine(directory.FullName, "SmartGridSuite.sln"))) return directory.FullName;
    throw new InvalidOperationException("Repository root not found");
}

sealed class CancelHistoryQuery : IQueryExpressionInterceptor
{
    public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
    {
        new HistoryQueryVisitor().Visit(queryExpression);
        return queryExpression;
    }

    private sealed class HistoryQueryVisitor : ExpressionVisitor
    {
        protected override Expression VisitExtension(Expression node)
        {
            if (node is EntityQueryRootExpression root && root.EntityType.ClrType == typeof(SiteHistoryEntity))
                throw new OperationCanceledException("Simulated provider timeout");
            return base.VisitExtension(node);
        }
    }
}

// A separate model prevents the earlier successful queries from reusing a
// cached compiled query and bypassing the timeout-injection interceptor.
sealed class TimeoutTestDbContext(DbContextOptions<SmartGridDbContext> options) : SmartGridDbContext(options);
