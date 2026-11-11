using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hampcoders.Electrolink.API.StoreManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "alert_management");

            migrationBuilder.EnsureSchema(
                name: "store_management");

            migrationBuilder.CreateTable(
                name: "stores",
                schema: "store_management",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(38)", maxLength: 38, nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    OwnerUserId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stores", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "areas",
                schema: "store_management",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(37)", maxLength: 37, nullable: false),
                    StoreId = table.Column<string>(type: "character varying(38)", maxLength: 38, nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_areas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_areas_stores_StoreId",
                        column: x => x.StoreId,
                        principalSchema: "store_management",
                        principalTable: "stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "equipment",
                schema: "store_management",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(42)", maxLength: 42, nullable: false),
                    AreaId = table.Column<string>(type: "character varying(37)", maxLength: 37, nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Thresholds_NominalVoltage = table.Column<float>(type: "real", nullable: false),
                    Thresholds_MaxConsumptionWatts = table.Column<float>(type: "real", nullable: false),
                    Thresholds_MaxCurrentAmps = table.Column<float>(type: "real", nullable: false),
                    Thresholds_MinPowerFactor = table.Column<float>(type: "real", nullable: false),
                    Thresholds_NominalFrequency = table.Column<float>(type: "real", nullable: false),
                    Thresholds_DisconnectionThresholdMin = table.Column<int>(type: "integer", nullable: false),
                    Thresholds_NormalLimitAmps = table.Column<float>(type: "real", nullable: false),
                    Thresholds_AlertLimitAmps = table.Column<float>(type: "real", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_equipment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_equipment_areas_AreaId",
                        column: x => x.AreaId,
                        principalSchema: "store_management",
                        principalTable: "areas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "device_bindings",
                schema: "store_management",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    EquipmentId = table.Column<string>(type: "character varying(42)", maxLength: 42, nullable: false),
                    OwnerUserId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DeviceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SerialNumber = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EdgeDeviceId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LegacyPropertyId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AuthorizedProfileId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LinkedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_bindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_device_bindings_equipment_EquipmentId",
                        column: x => x.EquipmentId,
                        principalSchema: "store_management",
                        principalTable: "equipment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "measurements",
                schema: "store_management",
                columns: table => new
                {
                    BindingId = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ReadingId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CapturedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TimestampBasis = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Voltage = table.Column<float>(type: "real", nullable: false),
                    Current = table.Column<float>(type: "real", nullable: false),
                    PowerFactor = table.Column<float>(type: "real", nullable: false),
                    Frequency = table.Column<float>(type: "real", nullable: false),
                    Source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    AnomalyType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Severity = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_measurements", x => new { x.BindingId, x.ReadingId });
                    table.ForeignKey(
                        name: "FK_measurements_device_bindings_BindingId",
                        column: x => x.BindingId,
                        principalSchema: "store_management",
                        principalTable: "device_bindings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "alerts",
                schema: "alert_management",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(70)", maxLength: 70, nullable: false),
                    BindingId = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ReadingId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EquipmentId = table.Column<string>(type: "character varying(42)", maxLength: 42, nullable: false),
                    OwnerUserId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AnomalyType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    DetectionSeverity = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Severity = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CapturedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    AcknowledgedByUserId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AcknowledgedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alerts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_alerts_equipment_EquipmentId",
                        column: x => x.EquipmentId,
                        principalSchema: "store_management",
                        principalTable: "equipment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_alerts_measurements_BindingId_ReadingId",
                        columns: x => new { x.BindingId, x.ReadingId },
                        principalSchema: "store_management",
                        principalTable: "measurements",
                        principalColumns: new[] { "BindingId", "ReadingId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_alerts_BindingId_ReadingId",
                schema: "alert_management",
                table: "alerts",
                columns: new[] { "BindingId", "ReadingId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_alerts_EquipmentId_CreatedAtUtc",
                schema: "alert_management",
                table: "alerts",
                columns: new[] { "EquipmentId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_areas_StoreId",
                schema: "store_management",
                table: "areas",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_device_bindings_DeviceId",
                schema: "store_management",
                table: "device_bindings",
                column: "DeviceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_device_bindings_EdgeDeviceId",
                schema: "store_management",
                table: "device_bindings",
                column: "EdgeDeviceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_device_bindings_EquipmentId",
                schema: "store_management",
                table: "device_bindings",
                column: "EquipmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_equipment_AreaId",
                schema: "store_management",
                table: "equipment",
                column: "AreaId");

            migrationBuilder.CreateIndex(
                name: "IX_measurements_BindingId_CapturedAtUtc",
                schema: "store_management",
                table: "measurements",
                columns: new[] { "BindingId", "CapturedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_stores_OwnerUserId_Id",
                schema: "store_management",
                table: "stores",
                columns: new[] { "OwnerUserId", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alerts",
                schema: "alert_management");

            migrationBuilder.DropTable(
                name: "measurements",
                schema: "store_management");

            migrationBuilder.DropTable(
                name: "device_bindings",
                schema: "store_management");

            migrationBuilder.DropTable(
                name: "equipment",
                schema: "store_management");

            migrationBuilder.DropTable(
                name: "areas",
                schema: "store_management");

            migrationBuilder.DropTable(
                name: "stores",
                schema: "store_management");
        }
    }
}
