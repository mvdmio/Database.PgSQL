--
-- PostgreSQL database schema
-- Generated at 2026-07-11 10:30:45 UTC
-- Migration version: 202505181000 (SimpleTable) [mvdmio.Database.PgSQL.Tests.Integration]
-- Migration version: 202505190000 (SecondaryFollowUp) [mvdmio.Database.PgSQL.Tests.Integration.SecondarySchema]
-- NOTE: deliberately carries a header line for the secondary assembly's scope even though this file's
-- assembly cannot vouch for it — models a schema pulled from a shared database whose migrations table
-- contains other apps' scopes. The migrator must ignore that line with a warning and run the secondary
-- assembly's migrations from zero instead of silently skipping them.
--

CREATE SCHEMA IF NOT EXISTS "mvdmio";
CREATE TABLE IF NOT EXISTS "mvdmio"."migrations" (
   identifier  BIGINT      NOT NULL,
   name        TEXT        NOT NULL,
   executed_at TIMESTAMPTZ NOT NULL,
   PRIMARY KEY (identifier)
);

CREATE TABLE public.simple_table (
    id                    BIGINT NOT NULL,
    required_string_value TEXT   NOT NULL,
    optional_string_value TEXT   NULL,
    PRIMARY KEY (id)
);
