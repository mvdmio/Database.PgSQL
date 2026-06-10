--
-- PostgreSQL database schema (secondary test assembly, legacy header)
-- Generated at 2026-02-18 10:30:45 UTC
-- Migration version: 202505181100 (SecondaryTable)
-- NOTE: deliberately kept in the legacy scope-less header format; see the primary assembly's
-- schema.legacymulti.sql for the multi-assembly legacy-header scenario this exercises.
--

CREATE SCHEMA IF NOT EXISTS "mvdmio";
CREATE TABLE IF NOT EXISTS "mvdmio"."migrations" (
   identifier  BIGINT      NOT NULL,
   name        TEXT        NOT NULL,
   executed_at TIMESTAMPTZ NOT NULL,
   PRIMARY KEY (identifier)
);

CREATE TABLE public.secondary_table (
    id                    BIGINT NOT NULL,
    description           TEXT   NOT NULL,
    PRIMARY KEY (id)
);
