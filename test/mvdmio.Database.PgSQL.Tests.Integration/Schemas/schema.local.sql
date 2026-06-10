--
-- PostgreSQL database schema
-- Generated at 2026-02-18 10:30:45 UTC
-- Migration version: 202505181000 (SimpleTable)
-- NOTE: deliberately kept in the legacy scope-less header format, so the schema-first tests
-- exercise the upgrade path where the backfill heals the resulting scope-less baseline row.
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
