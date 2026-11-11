using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace Hampcoders.Electrolink.API.Shared.Infrastructure.Persistence.EFC.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:postgis", ",,");

            migrationBuilder.CreateTable(
                name: "analytics_technician_metrics",
                columns: table => new
                {
                    metrics_id = table.Column<string>(type: "text", nullable: false),
                    technician_id = table.Column<string>(type: "text", nullable: false),
                    period_start = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    period_end = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_services_count = table.Column<int>(type: "integer", nullable: false),
                    iot_services_count = table.Column<int>(type: "integer", nullable: false),
                    average_rating = table.Column<decimal>(type: "numeric", nullable: false),
                    total_revenue_amount = table.Column<decimal>(type: "numeric", nullable: false),
                    total_revenue_currency = table.Column<string>(type: "text", nullable: false),
                    average_response_time = table.Column<TimeSpan>(type: "interval", nullable: false),
                    client_retention_rate = table.Column<decimal>(type: "numeric", nullable: false),
                    last_calculated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_analytics_technician_metrics", x => x.metrics_id);
                });

            migrationBuilder.CreateTable(
                name: "component_types",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "varchar(100)", nullable: false),
                    description = table.Column<string>(type: "varchar(500)", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_component_types", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "components",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "varchar(100)", nullable: false),
                    description = table.Column<string>(type: "varchar(500)", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_components", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "io_t_devices",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    serial_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    api_key_hash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    firmware_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    stock_technician_id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    assigned_property_id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    installation_request_id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    installed_by_technician_id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    installed_by_staff_member_id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    installed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    connection_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    last_reading_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    maintenance_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    expected_return_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_io_t_devices", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "iot_anomaly_records",
                columns: table => new
                {
                    anomaly_id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    device_id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    property_id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    owner_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    owner_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    anomaly_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    detection_layer = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    trigger_reading_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    detected_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    acknowledged_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    related_suggestion_id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    edge_alert_payload = table.Column<string>(type: "jsonb", nullable: true),
                    auto_relay_activated = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_iot_anomaly_records", x => x.anomaly_id);
                });

            migrationBuilder.CreateTable(
                name: "iot_device_reading_streams",
                columns: table => new
                {
                    stream_id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    device_id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    property_id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    owner_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    owner_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    stream_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    last_received_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    threshold_nominal_voltage = table.Column<float>(type: "real", nullable: false),
                    threshold_max_consumption_watts = table.Column<float>(type: "real", nullable: false),
                    threshold_max_current_amps = table.Column<float>(type: "real", nullable: false),
                    threshold_min_power_factor = table.Column<float>(type: "real", nullable: false),
                    threshold_nominal_frequency = table.Column<float>(type: "real", nullable: false),
                    threshold_disconnection_min = table.Column<int>(type: "integer", nullable: false),
                    threshold_normal_limit_amps = table.Column<float>(type: "real", nullable: false),
                    threshold_alert_limit_amps = table.Column<float>(type: "real", nullable: false),
                    consecutive_anomaly_count = table.Column<int>(type: "integer", nullable: false),
                    consecutive_normal_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_iot_device_reading_streams", x => x.stream_id);
                });

            migrationBuilder.CreateTable(
                name: "iot_relay_control_commands",
                columns: table => new
                {
                    command_id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    device_id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    property_id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    target_relay_state = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    requested_by = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    authorization_source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    service_request_id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    issued_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    sent_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    acknowledged_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    executed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    retry_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_iot_relay_control_commands", x => x.command_id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    processed_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    error = table.Column<string>(type: "text", nullable: true),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    dead_lettered_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pending_cloudinary_deletions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_id = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    folder = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    retry_count = table.Column<int>(type: "integer", nullable: false),
                    max_retries = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_pending_cloudinary_deletions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "profiles",
                columns: table => new
                {
                    profile_id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    business_role = table.Column<string>(type: "text", nullable: true),
                    subscription_tier = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    first_name = table.Column<string>(type: "text", nullable: true),
                    last_name = table.Column<string>(type: "text", nullable: true),
                    phone_number = table.Column<string>(type: "text", nullable: true),
                    dni = table.Column<string>(type: "text", nullable: true),
                    date_of_birth = table.Column<DateOnly>(type: "date", nullable: true),
                    street = table.Column<string>(type: "text", nullable: true),
                    number = table.Column<string>(type: "text", nullable: true),
                    district = table.Column<string>(type: "text", nullable: true),
                    city = table.Column<string>(type: "text", nullable: true),
                    country = table.Column<string>(type: "text", nullable: true),
                    postal_code = table.Column<string>(type: "text", nullable: true),
                    photo_public_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    photo_provider_id = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    photo_uploaded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_profiles", x => x.profile_id);
                });

            migrationBuilder.CreateTable(
                name: "properties",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    owner_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    owner_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    street = table.Column<string>(type: "text", nullable: false),
                    number = table.Column<string>(type: "text", nullable: false),
                    district = table.Column<string>(type: "text", nullable: false),
                    city = table.Column<string>(type: "text", nullable: false),
                    country = table.Column<string>(type: "text", nullable: false),
                    postal_code = table.Column<string>(type: "text", nullable: false),
                    latitude = table.Column<double>(type: "double precision", nullable: false),
                    longitude = table.Column<double>(type: "double precision", nullable: false),
                    accuracy = table.Column<int>(type: "integer", nullable: true),
                    source = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    main_photo_provider_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    property_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    installed_device_ids = table.Column<string>(type: "text", nullable: false),
                    has_active_iot_monitoring = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_properties", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "property_portfolios",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    owner_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    owner_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_property_portfolios", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    user_id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    expires_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    revoked_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    replaced_by_token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_refresh_tokens", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "service_assignments",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    request_id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    technician_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    staff_member_id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    recipe_snapshot = table.Column<string>(type: "jsonb", nullable: true),
                    matching_criteria = table.Column<string>(type: "jsonb", nullable: true),
                    matching_score = table.Column<string>(type: "jsonb", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    failure_reason = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    retry_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_service_assignments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "service_cancellation_requests",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    execution_id = table.Column<string>(type: "text", nullable: false),
                    cancelled_by = table.Column<string>(type: "text", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    request_reassignment = table.Column<bool>(type: "boolean", nullable: false),
                    requested_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_service_cancellation_requests", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "service_catalogs",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    technician_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_service_catalogs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "service_executions",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    assignment_id = table.Column<string>(type: "text", nullable: false),
                    request_id = table.Column<string>(type: "text", nullable: false),
                    technician_id = table.Column<string>(type: "text", nullable: false),
                    owner_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    owner_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    property_id = table.Column<string>(type: "text", nullable: false),
                    recipe_snapshot = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    scheduled_date_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    service_type = table.Column<string>(type: "text", nullable: false),
                    device_id = table.Column<string>(type: "text", nullable: true),
                    relay_state = table.Column<string>(type: "text", nullable: true),
                    timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    technical_report_content = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    technical_report_findings = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    technical_report_recommendations = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    technical_report_iot_findings = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    technical_report_version = table.Column<int>(type: "integer", nullable: false),
                    is_priority = table.Column<bool>(type: "boolean", nullable: false),
                    is_staff_technician = table.Column<bool>(type: "boolean", nullable: false),
                    no_show_detected_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    wait_extended_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    evaluation_window_expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    evaluation_window_expired = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_service_executions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "service_requests",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    client_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    client_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    property_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    geolocation_lat = table.Column<double>(type: "numeric(9,6)", nullable: true),
                    geolocation_lon = table.Column<double>(type: "numeric(9,6)", nullable: true),
                    geolocation_accuracy = table.Column<int>(type: "integer", nullable: true),
                    geolocation_source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    selected_recipe_id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    requested_category = table.Column<int>(type: "integer", nullable: true),
                    selected_technician_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    assigned_staff_member_id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    assignment_type = table.Column<int>(type: "integer", nullable: true),
                    recipe_snapshot = table.Column<string>(type: "jsonb", nullable: true),
                    assignment_id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    preferences = table.Column<string>(type: "jsonb", nullable: true),
                    is_priority = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    requires_io_t_certified_technician = table.Column<bool>(type: "boolean", nullable: false),
                    subscription_id = table.Column<string>(type: "text", nullable: true),
                    user_id = table.Column<string>(type: "text", nullable: true),
                    required_device_count = table.Column<int>(type: "integer", nullable: false),
                    installation_deadline_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_service_requests", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "service_suggestions",
                columns: table => new
                {
                    suggestion_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    client_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    profile_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    property_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    anomaly_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    device_id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    anomaly_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    viewed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    converted_to_request_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    converted_to_request_id = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_service_suggestions", x => x.suggestion_id);
                });

            migrationBuilder.CreateTable(
                name: "sp_processed_webhook_events",
                columns: table => new
                {
                    event_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    event_type = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    received_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_sp_processed_webhook_events", x => x.event_id);
                });

            migrationBuilder.CreateTable(
                name: "sp_subscriptions",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    user_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    profile_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    business_role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    plan_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    billing_cycle = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    cancel_at_period_end = table.Column<bool>(type: "boolean", nullable: false),
                    stripe_customer_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    stripe_subscription_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    period_start = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    period_end = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    grace_period_ends_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    monthly_requests_used = table.Column<int>(type: "integer", nullable: true, defaultValue: 0),
                    monthly_requests_limit = table.Column<int>(type: "integer", nullable: true, defaultValue: 2),
                    installation_service_request_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    installation_deadline_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    active_device_count = table.Column<int>(type: "integer", nullable: true),
                    price_per_device = table.Column<int>(type: "integer", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_sp_subscriptions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "technician_inventories",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    technician_id = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_technician_inventories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    email = table.Column<string>(type: "text", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: false),
                    role = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    password_reset_token = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    password_reset_token_expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "iot_readings",
                columns: table => new
                {
                    reading_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    stream_id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    voltage = table.Column<float>(type: "real", nullable: false),
                    current = table.Column<float>(type: "real", nullable: false),
                    power_factor = table.Column<float>(type: "real", nullable: false),
                    frequency = table.Column<float>(type: "real", nullable: false),
                    source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_iot_readings", x => x.reading_id);
                    table.ForeignKey(
                        name: "fk_iot_readings_stream",
                        column: x => x.stream_id,
                        principalTable: "iot_device_reading_streams",
                        principalColumn: "stream_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "companies",
                columns: table => new
                {
                    company_id = table.Column<string>(type: "text", nullable: false),
                    profile_id = table.Column<string>(type: "text", nullable: false),
                    company_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    company_tax_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    company_industry = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    company_size = table.Column<string>(type: "text", nullable: false),
                    company_website = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    billing_street = table.Column<string>(type: "text", nullable: false),
                    billing_number = table.Column<string>(type: "text", nullable: false),
                    billing_district = table.Column<string>(type: "text", nullable: false),
                    billing_city = table.Column<string>(type: "text", nullable: false),
                    billing_country = table.Column<string>(type: "text", nullable: false),
                    billing_postal_code = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_companies", x => x.company_id);
                    table.ForeignKey(
                        name: "f_k_companies_profiles_profile_id",
                        column: x => x.profile_id,
                        principalTable: "profiles",
                        principalColumn: "profile_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "home_owners",
                columns: table => new
                {
                    homeowner_id = table.Column<string>(type: "text", nullable: false),
                    profile_id = table.Column<string>(type: "text", nullable: false),
                    preferred_contact_time = table.Column<string>(type: "text", nullable: false),
                    sms_notifications = table.Column<bool>(type: "boolean", nullable: false),
                    email_notifications = table.Column<bool>(type: "boolean", nullable: false),
                    push_notifications = table.Column<bool>(type: "boolean", nullable: false),
                    name = table.Column<string>(type: "text", nullable: true),
                    relationship = table.Column<string>(type: "text", nullable: true),
                    phone_number = table.Column<string>(type: "text", nullable: true),
                    average_rating = table.Column<double>(type: "double precision", nullable: false, defaultValue: 0.0),
                    active_service_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_home_owners", x => x.homeowner_id);
                    table.ForeignKey(
                        name: "f_k_home_owners_profiles_profile_id",
                        column: x => x.profile_id,
                        principalTable: "profiles",
                        principalColumn: "profile_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "technicians",
                columns: table => new
                {
                    technician_id = table.Column<string>(type: "text", nullable: false),
                    profile_id = table.Column<string>(type: "text", nullable: false),
                    experience_years = table.Column<int>(type: "integer", nullable: false),
                    service_area_lat = table.Column<double>(type: "numeric(9,6)", nullable: false),
                    service_area_lon = table.Column<double>(type: "numeric(9,6)", nullable: false),
                    service_area_radius_km = table.Column<double>(type: "numeric(6,2)", nullable: false),
                    service_area_geom = table.Column<Geometry>(type: "geometry(Polygon, 4326)", nullable: false),
                    is_iot_certified = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    iot_cert_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    iot_cert_issuer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    iot_cert_date_obtained = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    iot_cert_expiration = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    iot_cert_credential_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    iot_cert_credential_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    about_me = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    average_rating = table.Column<double>(type: "double precision", nullable: false, defaultValue: 0.0),
                    active_service_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_technicians", x => x.technician_id);
                    table.ForeignKey(
                        name: "f_k_technicians_profiles_profile_id",
                        column: x => x.profile_id,
                        principalTable: "profiles",
                        principalColumn: "profile_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "property_photos",
                columns: table => new
                {
                    provider_id = table.Column<string>(type: "text", nullable: false),
                    property_id = table.Column<string>(type: "text", nullable: false),
                    public_url = table.Column<string>(type: "text", nullable: false),
                    uploaded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_property_photos", x => new { x.property_id, x.provider_id });
                    table.ForeignKey(
                        name: "f_k_property_photos_properties_property_id",
                        column: x => x.property_id,
                        principalTable: "properties",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "portfolio_entries",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    portfolio_id = table.Column<string>(type: "text", nullable: false),
                    property_id = table.Column<string>(type: "text", nullable: false),
                    nickname = table.Column<string>(type: "text", nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
                    occupancy_status = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_portfolio_entries", x => x.id);
                    table.ForeignKey(
                        name: "f_k_portfolio_entries_property_portfolios_portfolio_id",
                        column: x => x.portfolio_id,
                        principalTable: "property_portfolios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "service_recipes",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    catalog_id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    technician_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    service_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    service_description = table.Column<string>(type: "text", nullable: false),
                    service_category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    estimated_duration_min = table.Column<int>(type: "integer", nullable: false),
                    materials_estimate = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    labor_cost = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    total_price = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    prerequisites = table.Column<string>(type: "jsonb", nullable: false),
                    deliverables = table.Column<string>(type: "jsonb", nullable: false),
                    warranty_months = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    requires_io_t_certification = table.Column<bool>(type: "boolean", nullable: false),
                    times_requested = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_service_recipes", x => x.id);
                    table.ForeignKey(
                        name: "f_k_service_recipes_service_catalogs_catalog_id",
                        column: x => x.catalog_id,
                        principalTable: "service_catalogs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "component_usage_records",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    execution_id = table.Column<string>(type: "text", nullable: false),
                    component_type_id = table.Column<string>(type: "text", nullable: false),
                    component_type_name = table.Column<string>(type: "text", nullable: false),
                    quantity_used = table.Column<int>(type: "integer", nullable: false),
                    quantity_reserved = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_component_usage_records", x => x.id);
                    table.ForeignKey(
                        name: "f_k_component_usage_records_service_executions_execution_id",
                        column: x => x.execution_id,
                        principalTable: "service_executions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "relay_action_records",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    execution_id = table.Column<string>(type: "text", nullable: false),
                    device_id = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    issued_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    executed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_relay_action_records", x => x.id);
                    table.ForeignKey(
                        name: "f_k_relay_action_records_service_executions_execution_id",
                        column: x => x.execution_id,
                        principalTable: "service_executions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "service_evaluations",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    execution_id = table.Column<string>(type: "text", nullable: false),
                    reviewer_id = table.Column<string>(type: "text", nullable: false),
                    reviewed_id = table.Column<string>(type: "text", nullable: false),
                    reviewer_role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    rating = table.Column<int>(type: "integer", nullable: false),
                    comment = table.Column<string>(type: "text", nullable: true),
                    categories_json = table.Column<string>(type: "text", nullable: false),
                    submitted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_service_evaluations", x => x.id);
                    table.ForeignKey(
                        name: "f_k_service_evaluations_service_executions_execution_id",
                        column: x => x.execution_id,
                        principalTable: "service_executions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "work_photos",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    execution_id = table.Column<string>(type: "text", nullable: false),
                    photo_type = table.Column<string>(type: "text", nullable: false),
                    photo_url = table.Column<string>(type: "text", nullable: false),
                    provider_id = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    thumbnail_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    format = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    taken_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_work_photos", x => x.id);
                    table.ForeignKey(
                        name: "f_k_work_photos_service_executions_execution_id",
                        column: x => x.execution_id,
                        principalTable: "service_executions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sp_payment_records",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    subscription_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    stripe_invoice_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    stripe_payment_intent_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    amount = table.Column<int>(type: "integer", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    processed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_sp_payment_records", x => x.id);
                    table.ForeignKey(
                        name: "f_k_sp_payment_records_sp_subscriptions_subscription_id",
                        column: x => x.subscription_id,
                        principalTable: "sp_subscriptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "component_reservations",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    technician_inventory_id = table.Column<string>(type: "text", nullable: false),
                    service_id = table.Column<string>(type: "text", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_consumed = table.Column<bool>(type: "boolean", nullable: false),
                    is_released = table.Column<bool>(type: "boolean", nullable: false),
                    consumed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    released_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_component_reservations", x => x.id);
                    table.ForeignKey(
                        name: "f_k_component_reservations_technician_inventories_technician_in~",
                        column: x => x.technician_inventory_id,
                        principalTable: "technician_inventories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "component_stocks",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    technician_inventory_id = table.Column<string>(type: "text", nullable: false),
                    component_id = table.Column<string>(type: "text", nullable: false),
                    component_type_id = table.Column<string>(type: "text", nullable: false),
                    quantity_available = table.Column<int>(type: "integer", nullable: false),
                    reserved_quantity = table.Column<int>(type: "integer", nullable: false),
                    alert_threshold = table.Column<int>(type: "integer", nullable: false),
                    last_updated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_component_stocks", x => x.id);
                    table.ForeignKey(
                        name: "f_k_component_stocks_technician_inventories_technician_inventor~",
                        column: x => x.technician_inventory_id,
                        principalTable: "technician_inventories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "technician_specialties",
                columns: table => new
                {
                    technician_id = table.Column<string>(type: "text", nullable: false),
                    specialty = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_technician_specialties", x => new { x.technician_id, x.specialty });
                    table.ForeignKey(
                        name: "f_k_technician_specialties_technicians_technician_id",
                        column: x => x.technician_id,
                        principalTable: "technicians",
                        principalColumn: "technician_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "component_requirement_items",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    component_type_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    component_type_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    is_required = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    recipe_id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_component_requirement_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_requirements_recipes",
                        column: x => x.recipe_id,
                        principalTable: "service_recipes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "reservation_items",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    reservation_id = table.Column<string>(type: "text", nullable: false),
                    component_id = table.Column<string>(type: "text", nullable: false),
                    component_type_id = table.Column<string>(type: "text", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_reservation_items", x => x.id);
                    table.ForeignKey(
                        name: "f_k_reservation_items_component_reservations_reservation_id",
                        column: x => x.reservation_id,
                        principalTable: "component_reservations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "i_x_companies_profile_id",
                table: "companies",
                column: "profile_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_component_requirement_items_recipe_id",
                table: "component_requirement_items",
                column: "recipe_id");

            migrationBuilder.CreateIndex(
                name: "i_x_component_reservations_technician_inventory_id",
                table: "component_reservations",
                column: "technician_inventory_id");

            migrationBuilder.CreateIndex(
                name: "i_x_component_stocks_technician_inventory_id",
                table: "component_stocks",
                column: "technician_inventory_id");

            migrationBuilder.CreateIndex(
                name: "i_x_component_usage_records_execution_id",
                table: "component_usage_records",
                column: "execution_id");

            migrationBuilder.CreateIndex(
                name: "i_x_components_is_active",
                table: "components",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "i_x_components_name",
                table: "components",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_home_owners_profile_id",
                table: "home_owners",
                column: "profile_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_io_t_devices_serial_number",
                table: "io_t_devices",
                column: "serial_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_io_t_devices_stock_technician_id",
                table: "io_t_devices",
                column: "stock_technician_id");

            migrationBuilder.CreateIndex(
                name: "idx_arm_iot_devices_property",
                table: "io_t_devices",
                column: "assigned_property_id");

            migrationBuilder.CreateIndex(
                name: "idx_arm_iot_devices_status",
                table: "io_t_devices",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "i_x_iot_anomaly_records_device_id",
                table: "iot_anomaly_records",
                column: "device_id");

            migrationBuilder.CreateIndex(
                name: "i_x_iot_anomaly_records_device_id_status_anomaly_type",
                table: "iot_anomaly_records",
                columns: new[] { "device_id", "status", "anomaly_type" });

            migrationBuilder.CreateIndex(
                name: "i_x_iot_anomaly_records_property_id",
                table: "iot_anomaly_records",
                column: "property_id");

            migrationBuilder.CreateIndex(
                name: "i_x_iot_anomaly_records_status",
                table: "iot_anomaly_records",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "i_x_iot_device_reading_streams_device_id",
                table: "iot_device_reading_streams",
                column: "device_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_iot_readings_stream_id",
                table: "iot_readings",
                column: "stream_id");

            migrationBuilder.CreateIndex(
                name: "i_x_iot_readings_timestamp",
                table: "iot_readings",
                column: "timestamp");

            migrationBuilder.CreateIndex(
                name: "i_x_iot_relay_control_commands_device_id",
                table: "iot_relay_control_commands",
                column: "device_id");

            migrationBuilder.CreateIndex(
                name: "i_x_iot_relay_control_commands_status",
                table: "iot_relay_control_commands",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "i_x_portfolio_entries_portfolio_id",
                table: "portfolio_entries",
                column: "portfolio_id");

            migrationBuilder.CreateIndex(
                name: "ix_properties_owner_client_id",
                table: "properties",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "i_x_refresh_tokens_token_hash",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_refresh_tokens_user_id",
                table: "refresh_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "i_x_relay_action_records_execution_id",
                table: "relay_action_records",
                column: "execution_id");

            migrationBuilder.CreateIndex(
                name: "i_x_reservation_items_reservation_id",
                table: "reservation_items",
                column: "reservation_id");

            migrationBuilder.CreateIndex(
                name: "i_x_service_catalogs_technician_id",
                table: "service_catalogs",
                column: "technician_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_service_evaluations_execution_id",
                table: "service_evaluations",
                column: "execution_id");

            migrationBuilder.CreateIndex(
                name: "i_x_service_recipes_catalog_id",
                table: "service_recipes",
                column: "catalog_id");

            migrationBuilder.CreateIndex(
                name: "ix_suggestions_client_id",
                table: "service_suggestions",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_suggestions_status_expires",
                table: "service_suggestions",
                columns: new[] { "status", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_sp_payment_records_subscription",
                table: "sp_payment_records",
                column: "subscription_id");

            migrationBuilder.CreateIndex(
                name: "uix_sp_payment_records_invoice",
                table: "sp_payment_records",
                column: "stripe_invoice_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sp_subscriptions_status",
                table: "sp_subscriptions",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_sp_subscriptions_stripe_cust",
                table: "sp_subscriptions",
                column: "stripe_customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_sp_subscriptions_stripe_sub",
                table: "sp_subscriptions",
                column: "stripe_subscription_id");

            migrationBuilder.CreateIndex(
                name: "uix_sp_subscriptions_profile",
                table: "sp_subscriptions",
                column: "profile_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uix_sp_subscriptions_user",
                table: "sp_subscriptions",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_technicians_profile_id",
                table: "technicians",
                column: "profile_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_technicians_service_area_geom",
                table: "technicians",
                column: "service_area_geom")
                .Annotation("Npgsql:IndexMethod", "GIST");

            migrationBuilder.CreateIndex(
                name: "i_x_users_email",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_work_photos_execution_id",
                table: "work_photos",
                column: "execution_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "analytics_technician_metrics");

            migrationBuilder.DropTable(
                name: "companies");

            migrationBuilder.DropTable(
                name: "component_requirement_items");

            migrationBuilder.DropTable(
                name: "component_stocks");

            migrationBuilder.DropTable(
                name: "component_types");

            migrationBuilder.DropTable(
                name: "component_usage_records");

            migrationBuilder.DropTable(
                name: "components");

            migrationBuilder.DropTable(
                name: "home_owners");

            migrationBuilder.DropTable(
                name: "io_t_devices");

            migrationBuilder.DropTable(
                name: "iot_anomaly_records");

            migrationBuilder.DropTable(
                name: "iot_readings");

            migrationBuilder.DropTable(
                name: "iot_relay_control_commands");

            migrationBuilder.DropTable(
                name: "outbox_messages");

            migrationBuilder.DropTable(
                name: "pending_cloudinary_deletions");

            migrationBuilder.DropTable(
                name: "portfolio_entries");

            migrationBuilder.DropTable(
                name: "property_photos");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "relay_action_records");

            migrationBuilder.DropTable(
                name: "reservation_items");

            migrationBuilder.DropTable(
                name: "service_assignments");

            migrationBuilder.DropTable(
                name: "service_cancellation_requests");

            migrationBuilder.DropTable(
                name: "service_evaluations");

            migrationBuilder.DropTable(
                name: "service_requests");

            migrationBuilder.DropTable(
                name: "service_suggestions");

            migrationBuilder.DropTable(
                name: "sp_payment_records");

            migrationBuilder.DropTable(
                name: "sp_processed_webhook_events");

            migrationBuilder.DropTable(
                name: "technician_specialties");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "work_photos");

            migrationBuilder.DropTable(
                name: "service_recipes");

            migrationBuilder.DropTable(
                name: "iot_device_reading_streams");

            migrationBuilder.DropTable(
                name: "property_portfolios");

            migrationBuilder.DropTable(
                name: "properties");

            migrationBuilder.DropTable(
                name: "component_reservations");

            migrationBuilder.DropTable(
                name: "sp_subscriptions");

            migrationBuilder.DropTable(
                name: "technicians");

            migrationBuilder.DropTable(
                name: "service_executions");

            migrationBuilder.DropTable(
                name: "service_catalogs");

            migrationBuilder.DropTable(
                name: "technician_inventories");

            migrationBuilder.DropTable(
                name: "profiles");
        }
    }
}
