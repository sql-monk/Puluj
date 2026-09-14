-- Rebuilds every derived table from the raw messages: targets, tracks, their links and revisions, alerts, source
-- statistics. Use after a parser / correlation / linker change to re-run the pipeline over what has already been
-- collected. The raw messages themselves are kept; the Worker's sweeper re-queues them in publication order (oldest
-- first, whichever channel they came from) and drains the backlog without waiting between batches; processing is
-- deterministic without an LLM key. The same thing from the admin panel: POST /api/admin/ops/reprocess.
--
--   docker exec -i puluj-pg psql -U puluj -d puluj -f - < scripts/reprocess.sql
--
-- Track ids change; anything that stored one (a bookmark, a screenshot) will not match afterwards.
BEGIN;
TRUNCATE track_targets, target_track_revisions, target_tracks, targets, air_alerts, processing_errors, source_daily_stats, source_copies
    RESTART IDENTITY CASCADE;
UPDATE raw_messages
SET processing_status = 0, attempts = 0, processed_at = NULL
WHERE processing_status <> 0; -- Failed ones too: a parser fix is one of the reasons to be here
COMMIT;
SELECT processing_status, count(*) FROM raw_messages GROUP BY 1 ORDER BY 1;
