using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hampcoders.Electrolink.API.StoreManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddKitchenOperationsEnergyPlanningAndCompliance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "LayoutHeight",
                schema: "store_management",
                table: "stores",
                type: "double precision",
                nullable: false,
                defaultValue: 600.0);

            migrationBuilder.AddColumn<double>(
                name: "LayoutWidth",
                schema: "store_management",
                table: "stores",
                type: "double precision",
                nullable: false,
                defaultValue: 1000.0);

            migrationBuilder.AddColumn<bool>(
                name: "PanelSurfaceMoistureDetected",
                schema: "store_management",
                table: "measurements",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "RelativeHumidityPercent",
                schema: "store_management",
                table: "measurements",
                type: "real",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "CoordinateX",
                schema: "store_management",
                table: "equipment",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "CoordinateY",
                schema: "store_management",
                table: "equipment",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Height",
                schema: "store_management",
                table: "equipment",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Width",
                schema: "store_management",
                table: "equipment",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ZIndex",
                schema: "store_management",
                table: "equipment",
                type: "integer",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ReadingId",
                schema: "alert_management",
                table: "alerts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "BindingId",
                schema: "alert_management",
                table: "alerts",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AddColumn<DateTime>(
                name: "AssignedAtUtc",
                schema: "alert_management",
                table: "alerts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssignedByUserId",
                schema: "alert_management",
                table: "alerts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssignedTechnicianId",
                schema: "alert_management",
                table: "alerts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssignedTechnicianName",
                schema: "alert_management",
                table: "alerts",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                schema: "alert_management",
                table: "alerts",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OnDutyWorkerCode",
                schema: "alert_management",
                table: "alerts",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OnDutyWorkerName",
                schema: "alert_management",
                table: "alerts",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReportCategory",
                schema: "alert_management",
                table: "alerts",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReportedByWorkerCode",
                schema: "alert_management",
                table: "alerts",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReportedByWorkerName",
                schema: "alert_management",
                table: "alerts",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResolutionNotes",
                schema: "alert_management",
                table: "alerts",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ResolvedAtUtc",
                schema: "alert_management",
                table: "alerts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResolvedByUserId",
                schema: "alert_management",
                table: "alerts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                schema: "alert_management",
                table: "alerts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "TELEMETRY");

            migrationBuilder.CreateTable(
                name: "compliance_signatures",
                schema: "store_management",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(42)", maxLength: 42, nullable: false),
                    StoreId = table.Column<string>(type: "character varying(38)", maxLength: 38, nullable: false),
                    SignerName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    SignerTitle = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ImagePng = table.Column<byte[]>(type: "bytea", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    WidthPx = table.Column<int>(type: "integer", nullable: false),
                    HeightPx = table.Column<int>(type: "integer", nullable: false),
                    SignedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SignedByUserId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_compliance_signatures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_compliance_signatures_stores_StoreId",
                        column: x => x.StoreId,
                        principalSchema: "store_management",
                        principalTable: "stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "equipment_protocols",
                schema: "store_management",
                columns: table => new
                {
                    EquipmentId = table.Column<string>(type: "character varying(42)", maxLength: 42, nullable: false),
                    ShutdownSteps = table.Column<string[]>(type: "text[]", nullable: false),
                    EmergencyIsolationSteps = table.Column<string[]>(type: "text[]", nullable: false),
                    safe_to_clean_max_current_amps = table.Column<float>(type: "real", nullable: false),
                    safe_to_clean_max_reading_age_min = table.Column<int>(type: "integer", nullable: false),
                    safe_to_clean_requires_no_critical_alerts = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_equipment_protocols", x => x.EquipmentId);
                    table.ForeignKey(
                        name: "FK_equipment_protocols_equipment_EquipmentId",
                        column: x => x.EquipmentId,
                        principalSchema: "store_management",
                        principalTable: "equipment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "maintenance_certificates",
                schema: "store_management",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(37)", maxLength: 37, nullable: false),
                    StoreId = table.Column<string>(type: "character varying(38)", maxLength: 38, nullable: false),
                    EquipmentId = table.Column<string>(type: "character varying(42)", maxLength: 42, nullable: false),
                    TechnicianName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    CertificationNumber = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    WorkDescription = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IssuedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DocumentFileUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RegisteredByUserId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RegisteredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastExpiryNoticeDays = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_maintenance_certificates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_maintenance_certificates_equipment_EquipmentId",
                        column: x => x.EquipmentId,
                        principalSchema: "store_management",
                        principalTable: "equipment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_maintenance_certificates_stores_StoreId",
                        column: x => x.StoreId,
                        principalSchema: "store_management",
                        principalTable: "stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ohs_reports",
                schema: "store_management",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(42)", maxLength: 42, nullable: false),
                    StoreId = table.Column<string>(type: "character varying(38)", maxLength: 38, nullable: false),
                    RequestedByUserId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FileName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Content = table.Column<byte[]>(type: "bytea", nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    FromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ToUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    GeneratedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ComplianceScore = table.Column<double>(type: "double precision", nullable: false),
                    SignatureId = table.Column<string>(type: "character varying(42)", maxLength: 42, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ohs_reports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ohs_reports_stores_StoreId",
                        column: x => x.StoreId,
                        principalSchema: "store_management",
                        principalTable: "stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "panel_pause_sessions",
                schema: "store_management",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(38)", maxLength: 38, nullable: false),
                    StoreId = table.Column<string>(type: "character varying(38)", maxLength: 38, nullable: false),
                    PanelId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    StartedByUserId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EndReason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    OverridingAlertId = table.Column<string>(type: "character varying(70)", maxLength: 70, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_panel_pause_sessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_panel_pause_sessions_stores_StoreId",
                        column: x => x.StoreId,
                        principalSchema: "store_management",
                        principalTable: "stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "shift_handover_logs",
                schema: "store_management",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(41)", maxLength: 41, nullable: false),
                    StoreId = table.Column<string>(type: "character varying(38)", maxLength: 38, nullable: false),
                    WorkerId = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    WorkerName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ShiftDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EquipmentSnapshotsJson = table.Column<string>(type: "jsonb", nullable: false),
                    GridStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ActiveCriticalAlerts = table.Column<int>(type: "integer", nullable: false),
                    ActiveNonCriticalAlerts = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RecordedByUserId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ManagerNotified = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shift_handover_logs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_shift_handover_logs_stores_StoreId",
                        column: x => x.StoreId,
                        principalSchema: "store_management",
                        principalTable: "stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "staff_shift_assignments",
                schema: "store_management",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(38)", maxLength: 38, nullable: false),
                    StoreId = table.Column<string>(type: "character varying(38)", maxLength: 38, nullable: false),
                    WorkerCode = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    WorkerName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Role = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    StartsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_staff_shift_assignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_staff_shift_assignments_stores_StoreId",
                        column: x => x.StoreId,
                        principalSchema: "store_management",
                        principalTable: "stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "store_energy_budgets",
                schema: "analytics",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(39)", maxLength: 39, nullable: false),
                    StoreId = table.Column<string>(type: "character varying(38)", maxLength: 38, nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Month = table.Column<int>(type: "integer", nullable: false),
                    TargetKWh = table.Column<double>(type: "double precision", nullable: false),
                    AlertThresholdPercentage = table.Column<double>(type: "double precision", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_store_energy_budgets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_store_energy_budgets_stores_StoreId",
                        column: x => x.StoreId,
                        principalSchema: "store_management",
                        principalTable: "stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "store_opening_hours",
                schema: "analytics",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(38)", maxLength: 38, nullable: false),
                    StoreId = table.Column<string>(type: "character varying(38)", maxLength: 38, nullable: false),
                    DayOfWeek = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    OpenTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    CloseTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_store_opening_hours", x => x.Id);
                    table.ForeignKey(
                        name: "FK_store_opening_hours_stores_StoreId",
                        column: x => x.StoreId,
                        principalSchema: "store_management",
                        principalTable: "stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "weekly_safety_checklists",
                schema: "store_management",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(42)", maxLength: 42, nullable: false),
                    StoreId = table.Column<string>(type: "character varying(38)", maxLength: 38, nullable: false),
                    InspectorStaffId = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    InspectorName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    WeekStartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ItemsJson = table.Column<string>(type: "jsonb", nullable: false),
                    Score = table.Column<double>(type: "double precision", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Observations = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_weekly_safety_checklists", x => x.Id);
                    table.ForeignKey(
                        name: "FK_weekly_safety_checklists_stores_StoreId",
                        column: x => x.StoreId,
                        principalSchema: "store_management",
                        principalTable: "stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_compliance_signatures_StoreId_SignedAtUtc",
                schema: "store_management",
                table: "compliance_signatures",
                columns: new[] { "StoreId", "SignedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_certificates_EquipmentId_CertificationNumber",
                schema: "store_management",
                table: "maintenance_certificates",
                columns: new[] { "EquipmentId", "CertificationNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_certificates_ExpiresAtUtc",
                schema: "store_management",
                table: "maintenance_certificates",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_certificates_StoreId_ExpiresAtUtc",
                schema: "store_management",
                table: "maintenance_certificates",
                columns: new[] { "StoreId", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ohs_reports_StoreId_GeneratedAtUtc",
                schema: "store_management",
                table: "ohs_reports",
                columns: new[] { "StoreId", "GeneratedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_panel_pause_sessions_StoreId_StartedAtUtc",
                schema: "store_management",
                table: "panel_pause_sessions",
                columns: new[] { "StoreId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_shift_handover_logs_StoreId_RecordedAtUtc",
                schema: "store_management",
                table: "shift_handover_logs",
                columns: new[] { "StoreId", "RecordedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_staff_shift_assignments_StoreId_StartsAtUtc_EndsAtUtc",
                schema: "store_management",
                table: "staff_shift_assignments",
                columns: new[] { "StoreId", "StartsAtUtc", "EndsAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_store_energy_budgets_StoreId_Year_Month",
                schema: "analytics",
                table: "store_energy_budgets",
                columns: new[] { "StoreId", "Year", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_store_opening_hours_StoreId_DayOfWeek",
                schema: "analytics",
                table: "store_opening_hours",
                columns: new[] { "StoreId", "DayOfWeek" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_weekly_safety_checklists_StoreId_WeekStartDate",
                schema: "store_management",
                table: "weekly_safety_checklists",
                columns: new[] { "StoreId", "WeekStartDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "compliance_signatures",
                schema: "store_management");

            migrationBuilder.DropTable(
                name: "equipment_protocols",
                schema: "store_management");

            migrationBuilder.DropTable(
                name: "maintenance_certificates",
                schema: "store_management");

            migrationBuilder.DropTable(
                name: "ohs_reports",
                schema: "store_management");

            migrationBuilder.DropTable(
                name: "panel_pause_sessions",
                schema: "store_management");

            migrationBuilder.DropTable(
                name: "shift_handover_logs",
                schema: "store_management");

            migrationBuilder.DropTable(
                name: "staff_shift_assignments",
                schema: "store_management");

            migrationBuilder.DropTable(
                name: "store_energy_budgets",
                schema: "analytics");

            migrationBuilder.DropTable(
                name: "store_opening_hours",
                schema: "analytics");

            migrationBuilder.DropTable(
                name: "weekly_safety_checklists",
                schema: "store_management");

            migrationBuilder.DropColumn(
                name: "LayoutHeight",
                schema: "store_management",
                table: "stores");

            migrationBuilder.DropColumn(
                name: "LayoutWidth",
                schema: "store_management",
                table: "stores");

            migrationBuilder.DropColumn(
                name: "PanelSurfaceMoistureDetected",
                schema: "store_management",
                table: "measurements");

            migrationBuilder.DropColumn(
                name: "RelativeHumidityPercent",
                schema: "store_management",
                table: "measurements");

            migrationBuilder.DropColumn(
                name: "CoordinateX",
                schema: "store_management",
                table: "equipment");

            migrationBuilder.DropColumn(
                name: "CoordinateY",
                schema: "store_management",
                table: "equipment");

            migrationBuilder.DropColumn(
                name: "Height",
                schema: "store_management",
                table: "equipment");

            migrationBuilder.DropColumn(
                name: "Width",
                schema: "store_management",
                table: "equipment");

            migrationBuilder.DropColumn(
                name: "ZIndex",
                schema: "store_management",
                table: "equipment");

            migrationBuilder.DropColumn(
                name: "AssignedAtUtc",
                schema: "alert_management",
                table: "alerts");

            migrationBuilder.DropColumn(
                name: "AssignedByUserId",
                schema: "alert_management",
                table: "alerts");

            migrationBuilder.DropColumn(
                name: "AssignedTechnicianId",
                schema: "alert_management",
                table: "alerts");

            migrationBuilder.DropColumn(
                name: "AssignedTechnicianName",
                schema: "alert_management",
                table: "alerts");

            migrationBuilder.DropColumn(
                name: "Description",
                schema: "alert_management",
                table: "alerts");

            migrationBuilder.DropColumn(
                name: "OnDutyWorkerCode",
                schema: "alert_management",
                table: "alerts");

            migrationBuilder.DropColumn(
                name: "OnDutyWorkerName",
                schema: "alert_management",
                table: "alerts");

            migrationBuilder.DropColumn(
                name: "ReportCategory",
                schema: "alert_management",
                table: "alerts");

            migrationBuilder.DropColumn(
                name: "ReportedByWorkerCode",
                schema: "alert_management",
                table: "alerts");

            migrationBuilder.DropColumn(
                name: "ReportedByWorkerName",
                schema: "alert_management",
                table: "alerts");

            migrationBuilder.DropColumn(
                name: "ResolutionNotes",
                schema: "alert_management",
                table: "alerts");

            migrationBuilder.DropColumn(
                name: "ResolvedAtUtc",
                schema: "alert_management",
                table: "alerts");

            migrationBuilder.DropColumn(
                name: "ResolvedByUserId",
                schema: "alert_management",
                table: "alerts");

            migrationBuilder.DropColumn(
                name: "Source",
                schema: "alert_management",
                table: "alerts");

            migrationBuilder.AlterColumn<string>(
                name: "ReadingId",
                schema: "alert_management",
                table: "alerts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "BindingId",
                schema: "alert_management",
                table: "alerts",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldNullable: true);
        }
    }
}
