-- Rebuilds every derived table from the raw messages: observations, tracks, their links and revisions, alerts.
-- Use after a parser / correlation change to re-run the pipeline over what has already been collected.
-- The raw messages themselves are kept; the Worker's sweeper re-queues them in arrival order within ~10 s
-- (ProcessingOptions:PendingPollInterval, 200 per poll) and processing is deterministic without an LLM key.
--
--   docker exec -i puluj-pg psql -U puluj -d puluj -f - < scripts/reprocess.sql
--
-- Track ids change; anything that stored one (a bookmark, a screenshot) will not match afterwards.
BEGIN;
TRUNCATE threat_track_observations, threat_track_revisions, threat_tracks, observations, air_alerts, processing_errors
    RESTART IDENTITY CASCADE;
UPDATE raw_messages
SET processing_status = 0, attempts = 0, processed_at = NULL
WHERE processing_status IN (1, 3); -- Processed and Skipped; Failed ones are left alone (they would fail again)
COMMIT;
SELECT processing_status, count(*) FROM raw_messages GROUP BY 1 ORDER BY 1;
