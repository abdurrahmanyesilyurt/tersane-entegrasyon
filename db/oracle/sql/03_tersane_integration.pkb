WHENEVER SQLERROR EXIT FAILURE ROLLBACK
SET ECHO OFF FEEDBACK OFF

CREATE OR REPLACE PACKAGE BODY tersane_integration AS

    TYPE t_raw_list IS TABLE OF RAW(16);

    -- Private helper (not in the spec).
    PROCEDURE write_log(p_operation VARCHAR2, p_level VARCHAR2, p_message VARCHAR2, p_message_id RAW DEFAULT NULL) IS
    BEGIN
        INSERT INTO integration_logs (operation, log_level, message_id, message)
        VALUES (p_operation, p_level, p_message_id, SUBSTR(p_message, 1, 2000));
    END write_log;

    PROCEDURE submit_request(
        p_request_id      IN  material_requests.id%TYPE,
        p_user            IN  VARCHAR2,
        p_message_id      OUT outbox_messages.message_id%TYPE,
        p_already_queued  OUT NUMBER)
    IS
        l_request  material_requests%ROWTYPE;
        l_key      outbox_messages.idempotency_key%TYPE;
        l_lines    PLS_INTEGER;
        l_payload  CLOB;
    BEGIN
        p_already_queued := 0;

        -- Row lock for the rest of the transaction: two submits of the same request are serialised.
        BEGIN
            SELECT * INTO l_request FROM material_requests WHERE id = p_request_id FOR UPDATE;
        EXCEPTION
            WHEN NO_DATA_FOUND THEN
                RAISE_APPLICATION_ERROR(-20404, 'Malzeme talebi bulunamadı: ' || p_request_id);
        END;

        l_key := 'MaterialRequest.Submit:' || l_request.request_no;

        BEGIN
            SELECT message_id INTO p_message_id FROM outbox_messages WHERE idempotency_key = l_key;
            p_already_queued := 1;
            RETURN;
        EXCEPTION
            WHEN NO_DATA_FOUND THEN NULL; -- not queued yet
        END;

        IF l_request.status <> 'Draft' THEN
            RAISE_APPLICATION_ERROR(-20409, 'Yalnızca taslak durumundaki talepler gönderilebilir (' || l_request.status || ').');
        END IF;

        SELECT COUNT(*) INTO l_lines FROM material_request_lines WHERE request_id = p_request_id;
        IF l_lines = 0 THEN
            RAISE_APPLICATION_ERROR(-20422, 'Talepte en az bir malzeme kalemi olmalı.');
        END IF;

        UPDATE material_requests SET status = 'Submitted', submitted_at = SYSTIMESTAMP WHERE id = p_request_id;

        -- JSON_OBJECT / JSON_ARRAYAGG = T-SQL's FOR JSON PATH. FORMAT JSON keeps the nested array as JSON, not a string.
        SELECT JSON_OBJECT(
                   'materialRequestId' VALUE r.id,
                   'requestNo'         VALUE r.request_no,
                   'hullNo'            VALUE r.hull_no,
                   'requestedBy'       VALUE r.requested_by,
                   'needDate'          VALUE TO_CHAR(r.need_date, 'YYYY-MM-DD'),
                   'lines'             VALUE (SELECT JSON_ARRAYAGG(
                                                         JSON_OBJECT('lineNo'   VALUE l.line_no,
                                                                     'partNo'   VALUE l.part_no,
                                                                     'quantity' VALUE l.quantity,
                                                                     'unit'     VALUE l.unit)
                                                         ORDER BY l.line_no RETURNING CLOB)
                                                FROM material_request_lines l
                                               WHERE l.request_id = r.id) FORMAT JSON
                   RETURNING CLOB)
          INTO l_payload
          FROM material_requests r
         WHERE r.id = p_request_id;

        INSERT INTO outbox_messages (message_type, aggregate_id, idempotency_key, payload)
        VALUES ('MaterialRequest.Submitted', TO_CHAR(p_request_id), l_key, l_payload)
        RETURNING message_id INTO p_message_id;

        write_log('MaterialRequest.Submit', 'Info', l_request.request_no || ' ERP kuyruğuna alındı (' || p_user || ').', p_message_id);
        -- No COMMIT: Oracle keeps the transaction open until the caller commits or rolls back.
    EXCEPTION
        WHEN DUP_VAL_ON_INDEX THEN
            -- Another session queued the same key between our check and our insert.
            SELECT message_id INTO p_message_id FROM outbox_messages WHERE idempotency_key = l_key;
            p_already_queued := 1;
    END submit_request;

    FUNCTION claim_outbox(
        p_worker         IN VARCHAR2,
        p_batch          IN PLS_INTEGER DEFAULT 10,
        p_lease_seconds  IN PLS_INTEGER DEFAULT 60) RETURN t_id_list
    IS
        -- Rows locked by another session are skipped instead of waited for.
        -- Note: "WHERE ROWNUM <= n FOR UPDATE SKIP LOCKED" would limit BEFORE skipping and can return
        -- fewer rows than available; FETCH … LIMIT on the cursor limits after skipping.
        CURSOR c_due IS
            SELECT id
              FROM outbox_messages
             WHERE (status = 'Pending'    AND next_attempt_at <= SYSTIMESTAMP)
                OR (status = 'Processing' AND locked_until    <  SYSTIMESTAMP)
             ORDER BY id
               FOR UPDATE SKIP LOCKED;
        l_ids t_id_list;
    BEGIN
        OPEN c_due;
        FETCH c_due BULK COLLECT INTO l_ids LIMIT p_batch;
        CLOSE c_due; -- the row locks stay until COMMIT/ROLLBACK

        FORALL i IN 1 .. l_ids.COUNT
            UPDATE outbox_messages
               SET status        = 'Processing',
                   locked_by     = p_worker,
                   locked_until  = SYSTIMESTAMP + NUMTODSINTERVAL(p_lease_seconds, 'SECOND'),
                   attempt_count = attempt_count + 1
             WHERE id = l_ids(i);

        RETURN l_ids;
    END claim_outbox;

    FUNCTION requeue(
        p_message_id  IN outbox_messages.message_id%TYPE DEFAULT NULL,
        p_user        IN VARCHAR2) RETURN PLS_INTEGER
    IS
        l_ids     t_id_list;
        l_msg_ids t_raw_list;
    BEGIN
        UPDATE outbox_messages
           SET status = 'Pending', attempt_count = 0, next_attempt_at = SYSTIMESTAMP, locked_by = NULL, locked_until = NULL
         WHERE status = 'Failed'
           AND (p_message_id IS NULL OR message_id = p_message_id)
        RETURNING id, message_id BULK COLLECT INTO l_ids, l_msg_ids;

        UPDATE material_requests r
           SET r.status = 'Submitted'
         WHERE r.status = 'Failed'
           AND TO_CHAR(r.id) IN (SELECT o.aggregate_id
                                   FROM outbox_messages o
                                  WHERE o.id IN (SELECT COLUMN_VALUE FROM TABLE(l_ids)));

        FORALL i IN 1 .. l_msg_ids.COUNT
            INSERT INTO integration_logs (operation, log_level, message_id, message)
            VALUES ('Outbox.Requeue', 'Warning', l_msg_ids(i), 'Mesaj yeniden kuyruğa alındı (' || p_user || ').');

        RETURN l_ids.COUNT;
    END requeue;

    FUNCTION upsert_stock(p_rows IN t_stock_rows) RETURN PLS_INTEGER IS
    BEGIN
        MERGE INTO stock_balances t
        USING (SELECT * FROM TABLE(p_rows)) s
           ON (t.part_no = s.part_no AND t.location_no = s.location_no)
         WHEN MATCHED THEN
              UPDATE SET t.qty_on_hand = s.qty_on_hand, t.qty_reserved = s.qty_reserved,
                         t.erp_changed_at = s.erp_changed_at, t.synced_at = SYSTIMESTAMP
               WHERE t.qty_on_hand <> s.qty_on_hand OR t.qty_reserved <> s.qty_reserved OR t.erp_changed_at < s.erp_changed_at
         WHEN NOT MATCHED THEN
              INSERT (part_no, location_no, qty_on_hand, qty_reserved, erp_changed_at, synced_at)
              VALUES (s.part_no, s.location_no, s.qty_on_hand, s.qty_reserved, s.erp_changed_at, SYSTIMESTAMP);
        RETURN SQL%ROWCOUNT;
    END upsert_stock;

END tersane_integration;
/
SHOW ERRORS PACKAGE BODY tersane_integration

-- sqlplus only warns on compilation errors; turn them into a failure.
DECLARE
    l_errors PLS_INTEGER;
BEGIN
    SELECT COUNT(*) INTO l_errors FROM user_errors WHERE name = 'TERSANE_INTEGRATION';
    IF l_errors > 0 THEN
        RAISE_APPLICATION_ERROR(-20000, 'TERSANE_INTEGRATION has ' || l_errors || ' compilation error(s).');
    END IF;
END;
/
PROMPT package body created
EXIT
