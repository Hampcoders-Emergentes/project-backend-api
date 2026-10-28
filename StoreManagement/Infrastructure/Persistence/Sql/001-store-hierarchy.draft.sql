-- REVIEW ONLY. Not applied. Exclusive IAM creator ownership approved for MVP.
-- Generated offline from StoreManagementDbContext. For a NEW disposable local database only.
-- Not a migration/baseline for an existing or shared database.

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'store_management') THEN
        CREATE SCHEMA store_management;
    END IF;
END $EF$;


CREATE TABLE store_management.stores (
    "Id" character varying(38) NOT NULL,
    "Name" character varying(120) NOT NULL,
    "Address" character varying(300) NOT NULL,
    "OwnerUserId" character varying(100) NOT NULL,
    CONSTRAINT "PK_stores" PRIMARY KEY ("Id")
);


CREATE TABLE store_management.areas (
    "Id" character varying(37) NOT NULL,
    "StoreId" character varying(38) NOT NULL,
    "Name" character varying(120) NOT NULL,
    CONSTRAINT "PK_areas" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_areas_stores_StoreId" FOREIGN KEY ("StoreId") REFERENCES store_management.stores ("Id") ON DELETE RESTRICT
);


CREATE TABLE store_management.equipment (
    "Id" character varying(42) NOT NULL,
    "AreaId" character varying(37) NOT NULL,
    "Name" character varying(120) NOT NULL,
    CONSTRAINT "PK_equipment" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_equipment_areas_AreaId" FOREIGN KEY ("AreaId") REFERENCES store_management.areas ("Id") ON DELETE RESTRICT
);


CREATE INDEX "IX_areas_StoreId" ON store_management.areas ("StoreId");


CREATE INDEX "IX_equipment_AreaId" ON store_management.equipment ("AreaId");


CREATE INDEX "IX_stores_OwnerUserId_Id" ON store_management.stores ("OwnerUserId", "Id");


