--
-- PostgreSQL database schema (secondary test assembly, local environment)
-- Generated at 2026-02-18 10:30:45 UTC
-- Migration version: 202505181200 (SecondaryTableLocal)
--

CREATE SCHEMA IF NOT EXISTS "mvdmio";
CREATE TABLE IF NOT EXISTS "mvdmio"."migrations" (
   identifier  BIGINT      NOT NULL,
   name        TEXT        NOT NULL,
   executed_at TIMESTAMPTZ NOT NULL,
   PRIMARY KEY (identifier)
);

CREATE TABLE public.secondary_table_local (
    id                    BIGINT NOT NULL,
    description           TEXT   NOT NULL,
    PRIMARY KEY (id)
);
