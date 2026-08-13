using MH.Core.Models;

namespace MH.Core.Simulation;

public static class DemoGenerator
{
    public const int DefaultSeed = 20260813;
    public const int HistoryDays = 180;
    public const int ItemCount = 24;
    public const string ServerId = "demo-server-01";

    private static readonly string[] Categories = ["Ore", "Herb", "Cloth", "Food", "Tool", "Gem"];
    private static readonly int[] BasePrices = [120, 185, 260, 340, 430, 560, 720, 900];
    private static readonly int[] CaptureHoursUtc = [2, 8, 14, 20];

    public static DemoDataSet Generate(int seed = DefaultSeed)
    {
        var startUtc = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var server = new Server
        {
            Id = ServerId,
            Name = "DEMO Market Server",
            Region = "DEMO",
            CatalogKind = CatalogKind.Demo,
            CreatedAtUtc = startUtc
        };

        var items = Enumerable.Range(0, ItemCount)
            .Select(index => new Item
            {
                Id = $"demo-item-{index + 1:00}",
                Name = $"DEMO {Categories[index % Categories.Length]} {index + 1:00}",
                Category = Categories[index % Categories.Length],
                Unit = "piece",
                CatalogKind = CatalogKind.Demo,
                CreatedAtUtc = startUtc
            })
            .ToArray();

        var snapshots = new List<DemoSnapshot>(HistoryDays * CaptureHoursUtc.Length);
        var events = new List<Event>(HistoryDays * 2 + 20);

        for (var day = 0; day < HistoryDays; day++)
        {
            var date = startUtc.AddDays(day);
            var holiday = day % 30 == 20;
            var supplyChange = day % 21 == 7;

            if (holiday)
            {
                events.Add(new Event
                {
                    Id = $"demo-holiday-{day:000}",
                    ServerId = ServerId,
                    Type = MarketEventType.Holiday,
                    Label = "DEMO Festival",
                    StartsAtUtc = date,
                    EndsAtUtc = date.AddDays(1),
                    CatalogKind = CatalogKind.Demo
                });
            }

            if (supplyChange)
            {
                events.Add(new Event
                {
                    Id = $"demo-supply-{day:000}",
                    ServerId = ServerId,
                    Type = MarketEventType.SupplyChange,
                    Label = day / 21 % 2 == 0 ? "DEMO Supply Shortage" : "DEMO Supply Surplus",
                    StartsAtUtc = date,
                    EndsAtUtc = date.AddDays(7),
                    CatalogKind = CatalogKind.Demo
                });
            }

            events.Add(new Event
            {
                Id = $"demo-daylight-{day:000}",
                ServerId = ServerId,
                Type = MarketEventType.DayNight,
                Label = "DEMO Day/Night Cycle",
                StartsAtUtc = date,
                EndsAtUtc = date.AddDays(1),
                CatalogKind = CatalogKind.Demo
            });

            for (var slot = 0; slot < CaptureHoursUtc.Length; slot++)
            {
                var capturedAtUtc = date.AddHours(CaptureHoursUtc[slot]);
                var batchId = $"demo-snapshot-{seed}-{day:000}-{slot}";
                var observations = items.Select((item, itemIndex) =>
                {
                    var anomaly = IsAnomaly(seed, day, slot, itemIndex);
                    var price = CalculatePrice(seed, day, slot, itemIndex, holiday, supplyChange, anomaly);
                    var quantity = CalculateQuantity(seed, day, slot, itemIndex, supplyChange);
                    return new ListingObservation
                    {
                        SnapshotBatchId = batchId,
                        ServerId = ServerId,
                        ItemId = item.Id,
                        ObservedAtUtc = capturedAtUtc,
                        Price = price,
                        Quantity = quantity,
                        IsOcrAnomaly = anomaly
                    };
                }).ToArray();

                var batch = new SnapshotBatch
                {
                    Id = batchId,
                    ServerId = ServerId,
                    CapturedAtUtc = capturedAtUtc,
                    UploadedAtUtc = capturedAtUtc,
                    Source = "simulation",
                    PayloadHash = $"demo-payload-{seed}-{day:000}-{slot}",
                    CatalogKind = CatalogKind.Demo,
                    Observations = observations.ToList()
                };
                snapshots.Add(new DemoSnapshot(batch, observations));
            }
        }

        return new DemoDataSet(server, items, snapshots, events);
    }

    private static int CalculatePrice(
        int seed,
        int day,
        int slot,
        int itemIndex,
        bool holiday,
        bool supplyChange,
        bool anomaly)
    {
        var basePrice = BasePrices[itemIndex % BasePrices.Length] + itemIndex * 17;
        var trend = (day * (itemIndex % 5 + 1)) / 4;
        var season = ((day + itemIndex * 3) % 28 - 14) * (itemIndex % 3 + 1);
        var dayNight = slot switch
        {
            0 => -18,
            1 => 8,
            2 => 24,
            _ => -6
        } * (itemIndex % 4 + 1);
        var holidayEffect = holiday ? 32 + itemIndex % 5 * 9 : 0;
        var supplyEffect = supplyChange ? (itemIndex % 2 == 0 ? 55 : -38) : 0;
        var noise = StableNoise(seed, day, slot, itemIndex, 41) % 31 - 15;
        var anomalyEffect = anomaly ? 140 + itemIndex * 3 : 0;
        return Math.Max(1, basePrice + trend + season + dayNight + holidayEffect + supplyEffect + noise + anomalyEffect);
    }

    private static int CalculateQuantity(int seed, int day, int slot, int itemIndex, bool supplyChange)
    {
        var baseline = 24 + (itemIndex * 7 % 45);
        var variation = StableNoise(seed, day, slot, itemIndex, 73) % 19;
        var supplyEffect = supplyChange ? (itemIndex % 2 == 0 ? -8 : 12) : 0;
        return Math.Max(1, baseline + variation + supplyEffect);
    }

    private static bool IsAnomaly(int seed, int day, int slot, int itemIndex)
        => (StableNoise(seed, day, slot, itemIndex, 97) % 113) == 0;

    private static int StableNoise(int seed, int day, int slot, int itemIndex, int salt)
    {
        unchecked
        {
            var value = (uint)seed;
            value ^= (uint)(day + 1) * 0x9E3779B9u;
            value ^= (uint)(slot + 1) * 0x85EBCA6Bu;
            value ^= (uint)(itemIndex + 1) * 0xC2B2AE35u;
            value ^= (uint)salt * 0x27D4EB2Du;
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            value ^= value >> 16;
            return (int)(value & 0x7FFFFFFF);
        }
    }
}
