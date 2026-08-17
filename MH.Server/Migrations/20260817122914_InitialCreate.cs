using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MH.Server.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Items",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", nullable: false),
                    Unit = table.Column<string>(type: "TEXT", nullable: false),
                    CatalogKind = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Items", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Servers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Region = table.Column<string>(type: "TEXT", nullable: false),
                    CatalogKind = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Servers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Events",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ServerId = table.Column<string>(type: "TEXT", nullable: false),
                    ItemId = table.Column<string>(type: "TEXT", nullable: true),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Label = table.Column<string>(type: "TEXT", nullable: false),
                    StartsAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    EndsAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    CatalogKind = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Events_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Events_Servers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "Servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Recommendations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ServerId = table.Column<string>(type: "TEXT", nullable: false),
                    ItemId = table.Column<string>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", nullable: false),
                    ConfidencePercent = table.Column<int>(type: "INTEGER", nullable: false),
                    Rationale = table.Column<string>(type: "TEXT", nullable: false),
                    GeneratedAtUtc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Recommendations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Recommendations_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Recommendations_Servers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "Servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ServerIndexes",
                columns: table => new
                {
                    ServerId = table.Column<string>(type: "TEXT", nullable: false),
                    ActiveItemCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastObservedAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    CoveragePercent = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerIndexes", x => x.ServerId);
                    table.ForeignKey(
                        name: "FK_ServerIndexes_Servers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "Servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SnapshotBatches",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ServerId = table.Column<string>(type: "TEXT", nullable: false),
                    CapturedAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    UploadedAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", nullable: false),
                    PayloadHash = table.Column<string>(type: "TEXT", nullable: false),
                    CatalogKind = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SnapshotBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SnapshotBatches_Servers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "Servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TradeJournals",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ServerId = table.Column<string>(type: "TEXT", nullable: false),
                    ItemId = table.Column<string>(type: "TEXT", nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    UnitPrice = table.Column<int>(type: "INTEGER", nullable: false),
                    Side = table.Column<string>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: false),
                    OccurredAtUtc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeJournals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TradeJournals_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TradeJournals_Servers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "Servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ListingObservations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SnapshotBatchId = table.Column<string>(type: "TEXT", nullable: false),
                    ServerId = table.Column<string>(type: "TEXT", nullable: false),
                    ItemId = table.Column<string>(type: "TEXT", nullable: false),
                    ObservedAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    Price = table.Column<int>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    IsOcrAnomaly = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListingObservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ListingObservations_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ListingObservations_Servers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "Servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ListingObservations_SnapshotBatches_SnapshotBatchId",
                        column: x => x.SnapshotBatchId,
                        principalTable: "SnapshotBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Events_ItemId",
                table: "Events",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Events_ServerId_ItemId_StartsAtUtc",
                table: "Events",
                columns: new[] { "ServerId", "ItemId", "StartsAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Items_CatalogKind_Category",
                table: "Items",
                columns: new[] { "CatalogKind", "Category" });

            migrationBuilder.CreateIndex(
                name: "IX_ListingObservations_ItemId",
                table: "ListingObservations",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ListingObservations_ServerId_ItemId_ObservedAtUtc",
                table: "ListingObservations",
                columns: new[] { "ServerId", "ItemId", "ObservedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ListingObservations_SnapshotBatchId_ItemId",
                table: "ListingObservations",
                columns: new[] { "SnapshotBatchId", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Recommendations_ItemId",
                table: "Recommendations",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Recommendations_ServerId_ItemId_GeneratedAtUtc",
                table: "Recommendations",
                columns: new[] { "ServerId", "ItemId", "GeneratedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ServerIndexes_LastObservedAtUtc",
                table: "ServerIndexes",
                column: "LastObservedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Servers_CatalogKind",
                table: "Servers",
                column: "CatalogKind");

            migrationBuilder.CreateIndex(
                name: "IX_SnapshotBatches_PayloadHash",
                table: "SnapshotBatches",
                column: "PayloadHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SnapshotBatches_ServerId_CapturedAtUtc",
                table: "SnapshotBatches",
                columns: new[] { "ServerId", "CapturedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TradeJournals_ItemId",
                table: "TradeJournals",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_TradeJournals_ServerId_ItemId_OccurredAtUtc",
                table: "TradeJournals",
                columns: new[] { "ServerId", "ItemId", "OccurredAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Events");

            migrationBuilder.DropTable(
                name: "ListingObservations");

            migrationBuilder.DropTable(
                name: "Recommendations");

            migrationBuilder.DropTable(
                name: "ServerIndexes");

            migrationBuilder.DropTable(
                name: "TradeJournals");

            migrationBuilder.DropTable(
                name: "SnapshotBatches");

            migrationBuilder.DropTable(
                name: "Items");

            migrationBuilder.DropTable(
                name: "Servers");
        }
    }
}
