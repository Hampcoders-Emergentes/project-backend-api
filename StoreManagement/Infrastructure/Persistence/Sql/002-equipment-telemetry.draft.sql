-- REVIEW ONLY. Not applied. Additive M3.2 after reviewed M3.1 in a NEW disposable local database.
-- Not a baseline/migration for existing, shared or productive databases.

CREATE TABLE store_management.device_bindings (
    "Id" character varying(40) NOT NULL,
    "EquipmentId" character varying(42) NOT NULL,
    "OwnerUserId" character varying(100) NOT NULL,
    "DeviceId" character varying(100) NOT NULL,
    "SerialNumber" character varying(200) NOT NULL,
    "EdgeDeviceId" character varying(200) NOT NULL,
    "LegacyPropertyId" character varying(100) NOT NULL,
    "AuthorizedProfileId" character varying(100) NOT NULL,
    "LinkedAtUtc" timestamp with time zone NOT NULL,
    "Thresholds_NominalVoltage" real NOT NULL,
    "Thresholds_MaxConsumptionWatts" real NOT NULL,
    "Thresholds_MaxCurrentAmps" real NOT NULL,
    "Thresholds_MinPowerFactor" real NOT NULL,
    "Thresholds_NominalFrequency" real NOT NULL,
    "Thresholds_DisconnectionThresholdMin" integer NOT NULL,
    "Thresholds_NormalLimitAmps" real NOT NULL,
    "Thresholds_AlertLimitAmps" real NOT NULL,
    "ThresholdProvenance" character varying(100) NOT NULL,
    CONSTRAINT "PK_device_bindings" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_device_bindings_equipment_EquipmentId" FOREIGN KEY ("EquipmentId") REFERENCES store_management.equipment ("Id") ON DELETE RESTRICT
);

CREATE TABLE store_management.measurements (
    "BindingId" character varying(40) NOT NULL,
    "ReadingId" character varying(100) NOT NULL,
    "CapturedAtUtc" timestamp with time zone NOT NULL,
    "ReceivedAtUtc" timestamp with time zone NOT NULL,
    "TimestampBasis" character varying(30) NOT NULL,
    "Voltage" real NOT NULL,
    "Current" real NOT NULL,
    "PowerFactor" real NOT NULL,
    "Frequency" real NOT NULL,
    "Source" character varying(30) NOT NULL,
    "AnomalyType" character varying(80),
    "Severity" character varying(30),
    CONSTRAINT "PK_measurements" PRIMARY KEY ("BindingId", "ReadingId"),
    CONSTRAINT "FK_measurements_device_bindings_BindingId" FOREIGN KEY ("BindingId") REFERENCES store_management.device_bindings ("Id") ON DELETE RESTRICT
);

CREATE UNIQUE INDEX "IX_device_bindings_DeviceId" ON store_management.device_bindings ("DeviceId");

CREATE UNIQUE INDEX "IX_device_bindings_EdgeDeviceId" ON store_management.device_bindings ("EdgeDeviceId");

CREATE UNIQUE INDEX "IX_device_bindings_EquipmentId" ON store_management.device_bindings ("EquipmentId");

CREATE INDEX "IX_measurements_BindingId_CapturedAtUtc" ON store_management.measurements ("BindingId", "CapturedAtUtc");
