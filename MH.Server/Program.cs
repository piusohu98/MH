using System.Globalization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using MH.Core;
using MH.Core.Contracts;
using MH.Core.Models;
using MH.Server.Data;
using MH.Server.Services;

var builder = WebApplication.CreateBuilder(args);
var databasePath = DatabaseOptions.ResolvePath(builder.Configuration);
var databaseDirectory = System.IO.Path.GetDirectoryName(databasePath);
if (!string.IsNullOrWhiteSpace(databaseDirectory))
{
    Directory.CreateDirectory(databaseDirectory);
}

builder.Services.AddProblemDetails();
builder.Services.AddDbContext<MarketDbContext>(options => options.UseSqlite($"Data Source={databasePath};Cache=Shared;"));
builder.Services.AddScoped<RecommendationPreviewService>();
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

var app = builder.Build();
app.UseExceptionHandler();

await DatabaseInitializer.InitializeAsync(app.Services);

app.MapPost("/api/v1/snapshots", async (SnapshotUploadRequest request, MarketDbContext db, CancellationToken cancellationToken) =>
{
    var serverId = request.ServerId?.Trim();
    var source = request.Source?.Trim();
    var observations = request.Observations;

    if (string.IsNullOrWhiteSpace(serverId)
        || string.IsNullOrWhiteSpace(source)
        || source.Length > 40
        || request.CapturedAtUtc == default
        || observations is null
        || observations.Count is < 1 or > 1000)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Invalid snapshot request",
            detail: "serverId, source, capturedAtUtc, and 1 to 1000 observations are required.");
    }

    var server = await db.Servers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == serverId, cancellationToken);
    if (server is null)
    {
        return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Server not found", detail: serverId);
    }

    var itemIds = observations.Select(x => x.ItemId?.Trim()).ToArray();
    if (itemIds.Any(string.IsNullOrWhiteSpace)
        || itemIds.Any(x => x is not null && x.Length > 80)
        || itemIds.Distinct(StringComparer.Ordinal).Count() != itemIds.Length
        || observations.Any(x => x.Price <= 0 || x.Quantity <= 0))
    {
        return Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Invalid snapshot observations",
            detail: "Each observation needs a unique itemId, positive integer price, and positive integer quantity.");
    }

    var existingItems = await db.Items.AsNoTracking()
        .Where(x => itemIds.Contains(x.Id) && x.CatalogKind == server.CatalogKind)
        .Select(x => x.Id)
        .ToListAsync(cancellationToken);
    if (existingItems.Count != itemIds.Length)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Unknown catalog item",
            detail: "Every observation item must belong to the selected server catalog.");
    }

    var capturedAtUtc = request.CapturedAtUtc.ToUniversalTime();
    var normalizedObservations = observations.Select(observation => new ListingObservation
    {
        SnapshotBatchId = string.Empty,
        ServerId = serverId,
        ItemId = observation.ItemId!.Trim(),
        ObservedAtUtc = (observation.ObservedAtUtc ?? capturedAtUtc).ToUniversalTime(),
        Price = observation.Price,
        Quantity = observation.Quantity,
        IsOcrAnomaly = observation.IsOcrAnomaly
    }).ToList();

    var payloadHash = SnapshotFingerprint.Compute(request);
    var requestedBatchId = request.BatchId?.Trim();
    if (requestedBatchId is { Length: > 120 } or { Length: 0 })
    {
        return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid batchId", detail: "batchId must contain 1 to 120 characters when supplied.");
    }

    var batchId = requestedBatchId ?? payloadHash;
    var existing = await db.SnapshotBatches.AsNoTracking()
        .SingleOrDefaultAsync(x => x.Id == batchId || x.PayloadHash == payloadHash, cancellationToken);
    if (existing is not null)
    {
        if (!string.Equals(existing.PayloadHash, payloadHash, StringComparison.Ordinal))
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Batch id conflict", detail: "batchId already belongs to another payload.");
        }

        return Results.Ok(new SnapshotUploadResponse(existing.Id, true, await db.ListingObservations.CountAsync(x => x.SnapshotBatchId == existing.Id, cancellationToken)));
    }

    var batch = new SnapshotBatch
    {
        Id = batchId,
        ServerId = serverId,
        CapturedAtUtc = capturedAtUtc,
        UploadedAtUtc = DateTimeOffset.UtcNow,
        Source = source,
        PayloadHash = payloadHash,
        CatalogKind = server.CatalogKind
    };
    foreach (var observation in normalizedObservations)
    {
        observation.SnapshotBatchId = batchId;
        batch.Observations.Add(observation);
    }

    db.SnapshotBatches.Add(batch);
    await db.SaveChangesAsync(cancellationToken);
    return Results.Created($"/api/v1/snapshots/{Uri.EscapeDataString(batchId)}", new SnapshotUploadResponse(batchId, false, normalizedObservations.Count));
});

app.MapGet("/api/v1/catalog", async (string? kind, MarketDbContext db, CancellationToken cancellationToken) =>
{
    if (!TryParseCatalogKind(kind, out var catalogKind))
    {
        return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid catalog kind", detail: "kind must be demo or official.");
    }

    var servers = await db.Servers.AsNoTracking().Where(x => x.CatalogKind == catalogKind).OrderBy(x => x.Id).Select(x => new ServerDto(x.Id, x.Name, x.Region, x.CatalogKind, x.CreatedAtUtc)).ToListAsync(cancellationToken);
    var items = await db.Items.AsNoTracking().Where(x => x.CatalogKind == catalogKind).OrderBy(x => x.Id).Select(x => new ItemDto(x.Id, x.Name, x.Category, x.Unit, x.CatalogKind, x.CreatedAtUtc)).ToListAsync(cancellationToken);
    return Results.Ok(new CatalogResponse(catalogKind, servers, items));
});

app.MapGet("/api/v1/markets/{serverId}/{itemId}/events", async (
    string serverId,
    string itemId,
    string? fromUtc,
    string? toUtc,
    string? type,
    MarketDbContext db,
    CancellationToken cancellationToken) =>
{
    if (!TryParseRequiredUtc(fromUtc, out var from)
        || !TryParseRequiredUtc(toUtc, out var to)
        || from >= to
        || to - from > TimeSpan.FromDays(366)
        || !TryParseMarketEventType(type, out var eventType))
    {
        return Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Invalid event parameters",
            detail: "fromUtc and toUtc are required UTC-compatible timestamps no more than 366 days apart; type must be a known market event type when supplied.");
    }

    var market = await db.Servers.AsNoTracking()
        .Where(x => x.Id == serverId)
        .Join(
            db.Items.AsNoTracking().Where(x => x.Id == itemId),
            server => server.CatalogKind,
            item => item.CatalogKind,
            (server, _) => new { server.CatalogKind })
        .SingleOrDefaultAsync(cancellationToken);
    if (market is null)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Market entity not found",
            detail: "serverId or itemId does not exist in a queryable catalog.");
    }

    var eventsQuery = db.Events.AsNoTracking()
        .Where(x => x.ServerId == serverId
            && x.CatalogKind == market.CatalogKind
            && x.EndsAtUtc > x.StartsAtUtc
            && (x.ItemId == null || x.ItemId == itemId)
            && x.StartsAtUtc < to
            && x.EndsAtUtc > from);
    if (eventType.HasValue)
    {
        eventsQuery = eventsQuery.Where(x => x.Type == eventType.Value);
    }

    var events = await eventsQuery
        .OrderBy(x => x.StartsAtUtc)
        .ThenBy(x => x.Id)
        .Select(x => new MarketEventDto(
            x.Id,
            x.ServerId,
            x.ItemId,
            x.Type,
            x.Label,
            x.StartsAtUtc,
            x.EndsAtUtc,
            x.CatalogKind))
        .ToListAsync(cancellationToken);
    return Results.Ok(events);
});

app.MapGet("/api/v1/markets/{serverId}/{itemId}/events/{eventId}/impact", async (
    string serverId,
    string itemId,
    string eventId,
    string? asOfUtc,
    string? windowDays,
    MarketDbContext db,
    CancellationToken cancellationToken) =>
{
    var requestedWindowDays = EventImpactAnalyzer.DefaultWindowDays;
    if (!TryParseRequiredUtc(asOfUtc, out var cutoffUtc)
        || (!string.IsNullOrWhiteSpace(windowDays)
            && (!int.TryParse(windowDays, NumberStyles.Integer, CultureInfo.InvariantCulture, out requestedWindowDays)
                || requestedWindowDays is < EventImpactAnalyzer.MinimumWindowDays or > EventImpactAnalyzer.MaximumWindowDays)))
    {
        return Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Invalid event impact parameters",
            detail: $"asOfUtc is required with an offset; optional windowDays must be an integer between {EventImpactAnalyzer.MinimumWindowDays} and {EventImpactAnalyzer.MaximumWindowDays}.");
    }

    var market = await db.Servers.AsNoTracking()
        .Where(x => x.Id == serverId)
        .Join(
            db.Items.AsNoTracking().Where(x => x.Id == itemId),
            server => server.CatalogKind,
            item => item.CatalogKind,
            (server, _) => new { server.CatalogKind })
        .SingleOrDefaultAsync(cancellationToken);
    if (market is null)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Market entity not found",
            detail: "serverId or itemId does not exist in a queryable catalog.");
    }

    var marketEvent = await db.Events.AsNoTracking()
        .Where(x => x.Id == eventId
            && x.ServerId == serverId
            && x.CatalogKind == market.CatalogKind
            && x.EndsAtUtc > x.StartsAtUtc
            && (x.ItemId == null || x.ItemId == itemId))
        .SingleOrDefaultAsync(cancellationToken);
    if (marketEvent is null)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Event not found",
            detail: "The event does not belong to the requested server and item.");
    }

    var eventStartUtc = marketEvent.StartsAtUtc.ToUniversalTime();
    var eventEndUtc = marketEvent.EndsAtUtc.ToUniversalTime();
    var observationStartUtc = eventStartUtc.AddDays(-requestedWindowDays);
    var observationEndUtc = eventEndUtc.AddDays(requestedWindowDays);
    var observations = await db.ListingObservations.AsNoTracking()
        .Where(x => x.ServerId == serverId
            && x.ItemId == itemId
            && x.ObservedAtUtc >= observationStartUtc
            && x.ObservedAtUtc < observationEndUtc
            && x.ObservedAtUtc <= cutoffUtc)
        .OrderBy(x => x.ObservedAtUtc)
        .ToListAsync(cancellationToken);
    var dailyBars = PriceBarAggregator.Aggregate(observations);
    var analysis = EventImpactAnalyzer.Analyze(marketEvent, dailyBars, cutoffUtc, requestedWindowDays);

    return Results.Ok(new EventImpactResponse(
        ToMarketEventDto(analysis.Event),
        analysis.AsOfUtc,
        analysis.WindowDays,
        analysis.Before,
        analysis.During,
        analysis.After));
});

app.MapGet("/api/v1/markets/{serverId}/{itemId}/series", async (
    string serverId,
    string itemId,
    string? fromUtc,
    string? toUtc,
    MarketDbContext db,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(serverId) || string.IsNullOrWhiteSpace(itemId)
        || !TryParseOptionalUtc(fromUtc, out var from) || !TryParseOptionalUtc(toUtc, out var to)
        || (from.HasValue && to.HasValue && from > to))
    {
        return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid series parameters", detail: "fromUtc and toUtc must be ISO-8601 UTC-compatible timestamps, with fromUtc no later than toUtc.");
    }

    var serverExists = await db.Servers.AsNoTracking().AnyAsync(x => x.Id == serverId, cancellationToken);
    var itemExists = await db.Items.AsNoTracking().AnyAsync(x => x.Id == itemId, cancellationToken);
    if (!serverExists || !itemExists)
    {
        return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Market entity not found", detail: "serverId or itemId does not exist.");
    }

    var observations = await db.ListingObservations.AsNoTracking()
        .Where(x => x.ServerId == serverId && x.ItemId == itemId)
        .OrderBy(x => x.ObservedAtUtc)
        .ToListAsync(cancellationToken);
    var bars = PriceBarAggregator.Aggregate(observations, from, to)
        .Select(x => new PriceBarDto(x.StartUtc, x.EndUtc, x.Open, x.High, x.Low, x.Close, x.Volume, x.HasOcrAnomaly))
        .ToArray();
    return Results.Ok(new MarketSeriesResponse(serverId, itemId, from, to, bars));
});

app.MapGet("/api/v1/markets/{serverId}/{itemId}/indicators", async (
    string serverId,
    string itemId,
    string? asOfUtc,
    MarketDbContext db,
    CancellationToken cancellationToken) =>
{
    if (!TryParseRequiredUtc(asOfUtc, out var cutoffUtc))
    {
        return Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Invalid indicators parameters",
            detail: "asOfUtc is required and must be an ISO-8601 timestamp with an offset.");
    }

    var marketExists = await db.Servers.AsNoTracking()
        .Where(x => x.Id == serverId)
        .Join(
            db.Items.AsNoTracking().Where(x => x.Id == itemId),
            server => server.CatalogKind,
            item => item.CatalogKind,
            (_, _) => 1)
        .AnyAsync(cancellationToken);
    if (!marketExists)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Market entity not found",
            detail: "serverId or itemId does not exist in a queryable catalog.");
    }

    var cutoffDate = DateOnly.FromDateTime(cutoffUtc.UtcDateTime);
    var observationStartUtc = new DateTimeOffset(
        cutoffDate.AddDays(-30).ToDateTime(TimeOnly.MinValue),
        TimeSpan.Zero);
    var observations = await db.ListingObservations.AsNoTracking()
        .Where(x => x.ServerId == serverId
            && x.ItemId == itemId
            && x.ObservedAtUtc >= observationStartUtc
            && x.ObservedAtUtc <= cutoffUtc)
        .OrderBy(x => x.ObservedAtUtc)
        .ToListAsync(cancellationToken);
    var dailyBars = PriceBarAggregator.Aggregate(observations);
    if (!dailyBars.Any(bar => bar.EndUtc <= cutoffUtc))
    {
        var latestHistoricalObservation = await db.ListingObservations.AsNoTracking()
            .Where(x => x.ServerId == serverId
                && x.ItemId == itemId
                && x.ObservedAtUtc < observationStartUtc)
            .OrderByDescending(x => x.ObservedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (latestHistoricalObservation is not null)
        {
            dailyBars = dailyBars
                .Concat(PriceBarAggregator.Aggregate([latestHistoricalObservation]))
                .ToArray();
        }
    }
    var indicators = RobustMarketAnalyzer.Analyze(dailyBars, cutoffUtc);

    return Results.Ok(new MarketIndicatorsResponse(
        serverId,
        itemId,
        indicators.CutoffUtc,
        indicators.RobustMedian7Days,
        indicators.RobustMedian30Days,
        indicators.Mad7Days,
        indicators.Mad30Days,
        indicators.SampleCount7Days,
        indicators.SampleCount30Days,
        indicators.InlierCount7Days,
        indicators.InlierCount30Days,
        indicators.Return7Days,
        indicators.Return30Days,
        indicators.Ewma7Days,
        indicators.Ewma30Days,
        indicators.Volatility7Days,
        indicators.Volatility30Days,
        indicators.VisibleSupplyChange7Days,
        indicators.VisibleSupplyChange30Days,
        indicators.DataAgeHours));
});

app.MapGet("/api/v1/markets/{serverId}/{itemId}/recommendation", async (
    string serverId,
    string itemId,
    string? asOfUtc,
    RecommendationPreviewService previewService,
    CancellationToken cancellationToken) =>
{
    if (!TryParseRequiredUtc(asOfUtc, out var cutoffUtc))
    {
        return Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Invalid recommendation parameters",
            detail: "asOfUtc is required and must be an ISO-8601 timestamp with an offset.");
    }

    var preview = await previewService.BuildAsync(serverId, itemId, cutoffUtc, cancellationToken);
    return preview is null
        ? Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Market entity not found",
            detail: "serverId or itemId does not exist in a queryable catalog.")
        : Results.Ok(preview);
});

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.Run();

static bool TryParseCatalogKind(string? value, out CatalogKind kind)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        kind = CatalogKind.Demo;
        return true;
    }

    return Enum.TryParse(value, ignoreCase: true, out kind) && kind is CatalogKind.Demo or CatalogKind.Official;
}

static bool TryParseMarketEventType(string? value, out MarketEventType? eventType)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        eventType = null;
        return true;
    }

    if (Enum.TryParse<MarketEventType>(value, ignoreCase: true, out var parsed)
        && Enum.IsDefined(parsed))
    {
        eventType = parsed;
        return true;
    }

    eventType = null;
    return false;
}

static MarketEventDto ToMarketEventDto(Event marketEvent)
    => new(
        marketEvent.Id,
        marketEvent.ServerId,
        marketEvent.ItemId,
        marketEvent.Type,
        marketEvent.Label,
        marketEvent.StartsAtUtc.ToUniversalTime(),
        marketEvent.EndsAtUtc.ToUniversalTime(),
        marketEvent.CatalogKind);

static bool TryParseOptionalUtc(string? value, out DateTimeOffset? parsed)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        parsed = null;
        return true;
    }

    if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestamp))
    {
        parsed = timestamp.ToUniversalTime();
        return true;
    }

    parsed = null;
    return false;
}

static bool TryParseRequiredUtc(string? value, out DateTimeOffset parsed)
{
    parsed = default;
    if (string.IsNullOrWhiteSpace(value))
    {
        return false;
    }

    var text = value.Trim();
    var timeSeparatorIndex = text.IndexOfAny(['T', 't']);
    if (timeSeparatorIndex < 0)
    {
        return false;
    }

    var timePart = text[(timeSeparatorIndex + 1)..];
    var hasOffset = timePart.IndexOf('Z') >= 0
        || timePart.IndexOf('z') >= 0
        || timePart.IndexOf('+') >= 0
        || timePart.LastIndexOf('-') >= 0;
    if (!hasOffset
        || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestamp))
    {
        return false;
    }

    parsed = timestamp.ToUniversalTime();
    return true;
}

public partial class Program { }
