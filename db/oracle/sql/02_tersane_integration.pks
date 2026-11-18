WHENEVER SQLERROR EXIT FAILURE ROLLBACK
SET ECHO OFF FEEDBACK OFF

-- Package specification: the public contract. The body can change without invalidating callers.
CREATE OR REPLACE PACKAGE tersane_integration AUTHID DEFINER AS

    -- Business errors, same numbers as the T-SQL version (THROW 50404 → -20404).
    e_not_found  EXCEPTION;
    e_not_draft  EXCEPTION;
    e_no_lines   EXCEPTION;
    PRAGMA EXCEPTION_INIT(e_not_found, -20404);
    PRAGMA EXCEPTION_INIT(e_not_draft, -20409);
    PRAGMA EXCEPTION_INIT(e_no_lines, -20422);

    -- Draft → Submitted and the outbox message in the caller's transaction (no COMMIT inside).
    -- Idempotent: a second call returns the already queued message with p_already_queued = 1.
    -- (NUMBER 0/1 instead of BOOLEAN so that ODP.NET / JDBC clients can bind it before 23ai.)
    PROCEDURE submit_request(
        p_request_id      IN  material_requests.id%TYPE,
        p_user            IN  VARCHAR2,
        p_message_id      OUT outbox_messages.message_id%TYPE,
        p_already_queued  OUT NUMBER);

    -- Claims due messages for one worker. FOR UPDATE SKIP LOCKED = SQL Server's UPDLOCK + READPAST.
    FUNCTION claim_outbox(
        p_worker         IN VARCHAR2,
        p_batch          IN PLS_INTEGER DEFAULT 10,
        p_lease_seconds  IN PLS_INTEGER DEFAULT 60) RETURN t_id_list;

    -- "Yeniden işle": failed → pending (one message, or all failed ones when p_message_id is NULL).
    FUNCTION requeue(
        p_message_id  IN outbox_messages.message_id%TYPE DEFAULT NULL,
        p_user        IN VARCHAR2) RETURN PLS_INTEGER;

    -- Set-based upsert from a collection (MERGE … USING TABLE(collection)). Returns rows inserted + updated.
    FUNCTION upsert_stock(p_rows IN t_stock_rows) RETURN PLS_INTEGER;

END tersane_integration;
/
SHOW ERRORS PACKAGE tersane_integration
PROMPT package spec created
EXIT
