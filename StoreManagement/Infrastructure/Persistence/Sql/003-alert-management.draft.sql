-- M3.4 additive draft. Reviewed for NEW disposable local databases only.
-- Apply after 001 and 002. Not a migration/baseline for existing databases.

CREATE SCHEMA alert_management;

CREATE TABLE alert_management.alerts (
    "Id" character varying(70) NOT NULL,
    "BindingId" character varying(40) NOT NULL,
    "ReadingId" character varying(100) NOT NULL,
    "EquipmentId" character varying(42) NOT NULL,
    "OwnerUserId" character varying(100) NOT NULL,
    "AnomalyType" character varying(80) NOT NULL,
    "DetectionSeverity" character varying(30) NOT NULL,
    "Severity" character varying(30) NOT NULL,
    "CapturedAtUtc" timestamp with time zone NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "Status" character varying(30) NOT NULL,
    "AcknowledgedByUserId" character varying(100),
    "AcknowledgedAtUtc" timestamp with time zone,
    CONSTRAINT "PK_alerts" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_alerts_equipment_EquipmentId" FOREIGN KEY ("EquipmentId") REFERENCES store_management.equipment ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_alerts_measurements_BindingId_ReadingId" FOREIGN KEY ("BindingId", "ReadingId") REFERENCES store_management.measurements ("BindingId", "ReadingId") ON DELETE RESTRICT
);

CREATE UNIQUE INDEX "IX_alerts_BindingId_ReadingId" ON alert_management.alerts ("BindingId", "ReadingId");

CREATE INDEX "IX_alerts_EquipmentId_CreatedAtUtc" ON alert_management.alerts ("EquipmentId", "CreatedAtUtc");
