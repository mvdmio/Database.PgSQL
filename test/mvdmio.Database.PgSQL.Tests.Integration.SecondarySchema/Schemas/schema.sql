--
-- PostgreSQL database schema (secondary test assembly)
-- Generated at 2026-02-18 10:30:45 UTC
-- Migration version: 202505181100 (SecondaryTable)
--

-- Exercises the realistic db-pull output where every exported schema
-- file re-declares the migrations table with IF NOT EXISTS.
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
