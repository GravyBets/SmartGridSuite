-- SmartGridSuite ticket assignee source tracking
-- Run ONCE against the smartgrid_test MySQL database before deploying
-- the corresponding API update. Back up the existing assigned_tech values first.
USE smartgrid_test;

-- Verify the column is absent before running ALTER:
-- SHOW COLUMNS FROM tickets LIKE 'manual_assigned_tech';

ALTER TABLE tickets
    ADD COLUMN manual_assigned_tech VARCHAR(2000) NULL DEFAULT NULL
    AFTER assigned_tech;

-- Do NOT bulk-copy assigned_tech for tickets that have Daily Assignment
-- publications: their existing display text may include stale route assignees.
--
-- After verification, a safe optional legacy backfill for tickets that have
-- NEVER been published to a Daily Assignment is:
--
-- UPDATE tickets AS t
-- SET t.manual_assigned_tech = COALESCE(
--     NULLIF(TRIM(t.assigned_tech), ''), '(Unassigned)')
-- WHERE NOT EXISTS (
--     SELECT 1
--     FROM daily_ticket_assignment_published AS p
--     WHERE p.ticket_id = t.id
-- );
--
-- For legacy tickets with both manual and published Daily Assignments, the
-- original source of each name cannot be recovered from assigned_tech alone.
-- Review those tickets individually and assign manual_assigned_tech explicitly.
