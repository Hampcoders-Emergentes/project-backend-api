-- Additive draft for review. Do not run against shared/existing environments automatically.
-- Fresh targets get this table from the EF model through EnsureCreated, which never alters an existing database.
-- Existing databases need it before deploying: without it the Stripe webhook answers 500 and Stripe keeps retrying.
CREATE TABLE IF NOT EXISTS sp_processed_webhook_events (
    event_id character varying(255) NOT NULL,
    event_type character varying(255) NOT NULL,
    received_at timestamp with time zone NOT NULL,
    CONSTRAINT p_k_sp_processed_webhook_events PRIMARY KEY (event_id)
);
