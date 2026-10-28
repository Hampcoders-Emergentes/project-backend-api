-- M3.16 additive draft for review. Do not run against shared/existing environments automatically.
-- Fresh isolated targets use the exported EF model, which already includes this column/index.
-- No backfill: legacy stock without provenance remains NULL and cannot be assigned through HTTP.
ALTER TABLE io_t_devices ADD COLUMN stock_technician_id character varying(60) NULL;
CREATE INDEX i_x_io_t_devices_stock_technician_id ON io_t_devices (stock_technician_id);
