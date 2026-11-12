using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Hampcoders.Electrolink.API.StoreManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStoreOperationsAlertRoutingReportsAndAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.EnsureSchema(
                name: "analytics");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastEdgeSyncAtUtc",
                schema: "store_management",
                table: "stores",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "LeakageCurrentAmps",
                schema: "store_management",
                table: "measurements",
                type: "real",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "alert_dispatches",
                schema: "alert_management",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(41)", maxLength: 41, nullable: false),
                    AlertId = table.Column<string>(type: "character varying(70)", maxLength: 70, nullable: false),
                    StoreId = table.Column<string>(type: "character varying(38)", maxLength: 38, nullable: false),
                    Channel = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Destination = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alert_dispatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_alert_dispatches_alerts_AlertId",
                        column: x => x.AlertId,
                        principalSchema: "alert_management",
                        principalTable: "alerts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "audit_log_entries",
                schema: "audit",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    StoreId = table.Column<string>(type: "character varying(38)", maxLength: 38, nullable: true),
                    Action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Resource = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    HttpMethod = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    StatusCode = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IpAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    TimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_log_entries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "energy_reports",
                schema: "analytics",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(39)", maxLength: 39, nullable: false),
                    StoreId = table.Column<string>(type: "character varying(38)", maxLength: 38, nullable: false),
                    RequestedByUserId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Format = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    FileName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Content = table.Column<byte[]>(type: "bytea", nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    FromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ToUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    GeneratedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_energy_reports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_energy_reports_stores_StoreId",
                        column: x => x.StoreId,
                        principalSchema: "store_management",
                        principalTable: "stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "notification_preferences",
                schema: "store_management",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(42)", maxLength: 42, nullable: false),
                    StoreId = table.Column<string>(type: "character varying(38)", maxLength: 38, nullable: false),
                    Channel = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Destination = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    RecipientRole = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    RecipientName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    UrgencyLevels = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_preferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_notification_preferences_stores_StoreId",
                        column: x => x.StoreId,
                        principalSchema: "store_management",
                        principalTable: "stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_alerts_Status_Severity_CreatedAtUtc",
                schema: "alert_management",
                table: "alerts",
                columns: new[] { "Status", "Severity", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_alert_dispatches_AlertId_Channel_Destination",
                schema: "alert_management",
                table: "alert_dispatches",
                columns: new[] { "AlertId", "Channel", "Destination" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_alert_dispatches_Status_NextAttemptAtUtc",
                schema: "alert_management",
                table: "alert_dispatches",
                columns: new[] { "Status", "NextAttemptAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_entries_StoreId_TimestampUtc",
                schema: "audit",
                table: "audit_log_entries",
                columns: new[] { "StoreId", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_entries_UserId_TimestampUtc",
                schema: "audit",
                table: "audit_log_entries",
                columns: new[] { "UserId", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_energy_reports_ExpiresAtUtc",
                schema: "analytics",
                table: "energy_reports",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_energy_reports_StoreId_GeneratedAtUtc",
                schema: "analytics",
                table: "energy_reports",
                columns: new[] { "StoreId", "GeneratedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_notification_preferences_StoreId",
                schema: "store_management",
                table: "notification_preferences",
                column: "StoreId");

            // Alerts now use the alert vocabulary (e.g. CURRENT_LEAKAGE) instead of detector names.
            migrationBuilder.Sql("UPDATE alert_management.alerts SET \"AnomalyType\" = CASE \"AnomalyType\" WHEN 'VoltageSpike' THEN 'OVERVOLTAGE' WHEN 'VoltageSag' THEN 'VOLTAGE_SAG' WHEN 'SustainedOverconsumption' THEN 'OVERCONSUMPTION' WHEN 'ShortCircuitRisk' THEN 'SHORT_CIRCUIT_RISK' WHEN 'PowerFactorDegradation' THEN 'LOW_POWER_FACTOR' WHEN 'AbnormalFrequency' THEN 'ABNORMAL_FREQUENCY' WHEN 'ConnectionLost' THEN 'CONNECTION_LOST' WHEN 'UnusualConsumptionPattern' THEN 'UNUSUAL_CONSUMPTION' WHEN 'Overcurrent' THEN 'OVERCURRENT' WHEN 'CurrentLeakage' THEN 'CURRENT_LEAKAGE' ELSE \"AnomalyType\" END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE alert_management.alerts SET \"AnomalyType\" = CASE \"AnomalyType\" WHEN 'OVERVOLTAGE' THEN 'VoltageSpike' WHEN 'VOLTAGE_SAG' THEN 'VoltageSag' WHEN 'OVERCONSUMPTION' THEN 'SustainedOverconsumption' WHEN 'SHORT_CIRCUIT_RISK' THEN 'ShortCircuitRisk' WHEN 'LOW_POWER_FACTOR' THEN 'PowerFactorDegradation' WHEN 'ABNORMAL_FREQUENCY' THEN 'AbnormalFrequency' WHEN 'CONNECTION_LOST' THEN 'ConnectionLost' WHEN 'UNUSUAL_CONSUMPTION' THEN 'UnusualConsumptionPattern' WHEN 'OVERCURRENT' THEN 'Overcurrent' WHEN 'CURRENT_LEAKAGE' THEN 'CurrentLeakage' ELSE \"AnomalyType\" END;");

            migrationBuilder.DropTable(
                name: "alert_dispatches",
                schema: "alert_management");

            migrationBuilder.DropTable(
                name: "audit_log_entries",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "energy_reports",
                schema: "analytics");

            migrationBuilder.DropTable(
                name: "notification_preferences",
                schema: "store_management");

            migrationBuilder.DropIndex(
                name: "IX_alerts_Status_Severity_CreatedAtUtc",
                schema: "alert_management",
                table: "alerts");

            migrationBuilder.DropColumn(
                name: "LastEdgeSyncAtUtc",
                schema: "store_management",
                table: "stores");

            migrationBuilder.DropColumn(
                name: "LeakageCurrentAmps",
                schema: "store_management",
                table: "measurements");
        }
    }
}
