using MH.Core.Models;

namespace MH.Core.Simulation;

public sealed record DemoSnapshot(
    SnapshotBatch Batch,
    IReadOnlyList<ListingObservation> Observations);

public sealed record DemoDataSet(
    Server Server,
    IReadOnlyList<Item> Items,
    IReadOnlyList<DemoSnapshot> Snapshots,
    IReadOnlyList<Event> Events);
