-- PL/SQL tests for tersane_integration. Everything runs in one transaction and is rolled back.
WHENEVER SQLERROR EXIT FAILURE ROLLBACK
SET SERVEROUTPUT ON FEEDBACK OFF

DECLARE
    l_request   NUMBER;
    l_empty     NUMBER;
    l_msg       RAW(16);
    l_msg2      RAW(16);
    l_again     NUMBER;
    l_ids       t_id_list;
    l_count     PLS_INTEGER;
    l_qty       NUMBER;
    l_no        VARCHAR2(20);
    l_status    VARCHAR2(20);

    PROCEDURE check_that(p_ok BOOLEAN, p_what VARCHAR2) IS
    BEGIN
        IF p_ok IS NULL OR NOT p_ok THEN
            RAISE_APPLICATION_ERROR(-20999, 'TEST FAILED: ' || p_what);
        END IF;
        DBMS_OUTPUT.PUT_LINE('ok   ' || p_what);
    END check_that;
BEGIN
    INSERT INTO material_requests (hull_no, requested_by, need_date)
    VALUES ('NB-1042', 'test', TRUNC(SYSDATE) + 10)
    RETURNING id, request_no INTO l_request, l_no;
    INSERT INTO material_request_lines (request_id, line_no, part_no, quantity, unit) VALUES (l_request, 1, 'ST-AH36-08', 12.5, 'AD');
    INSERT INTO material_request_lines (request_id, line_no, part_no, quantity, unit) VALUES (l_request, 2, 'PR-HP160-9', 120, 'M');
    INSERT INTO material_requests (hull_no, requested_by, need_date)
    VALUES ('NB-1042', 'test', TRUNC(SYSDATE) + 10)
    RETURNING id INTO l_empty;

    check_that(REGEXP_LIKE(l_no, '^MT-[0-9]{4}-[0-9]{5}$'), 'request number comes from a sequence and a virtual column (' || l_no || ')');

    tersane_integration.submit_request(l_request, 'test', l_msg, l_again);
    check_that(l_again = 0, 'submit queues an outbox message');

    SELECT status INTO l_status FROM material_requests WHERE id = l_request;
    check_that(l_status = 'Submitted', 'status changes in the same transaction');

    SELECT JSON_VALUE(payload, '$.lines[0].quantity' RETURNING NUMBER) INTO l_qty FROM outbox_messages WHERE message_id = l_msg;
    check_that(l_qty = 12.5, 'payload is real JSON built with JSON_OBJECT / JSON_ARRAYAGG');

    tersane_integration.submit_request(l_request, 'test', l_msg2, l_again);
    check_that(l_again = 1 AND l_msg2 = l_msg, 'second submit is idempotent (same message)');

    BEGIN
        tersane_integration.submit_request(l_empty, 'test', l_msg2, l_again);
        check_that(FALSE, 'empty request must be rejected');
    EXCEPTION
        WHEN tersane_integration.e_no_lines THEN
            check_that(TRUE, 'empty request raises ORA-20422 (named exception)');
    END;

    BEGIN
        tersane_integration.submit_request(-1, 'test', l_msg2, l_again);
        check_that(FALSE, 'unknown request must be rejected');
    EXCEPTION
        WHEN tersane_integration.e_not_found THEN
            check_that(TRUE, 'unknown request raises ORA-20404');
    END;

    l_ids := tersane_integration.claim_outbox('worker-1', 10, 60);
    check_that(l_ids.COUNT = 1, 'claim returns the due message');
    l_ids := tersane_integration.claim_outbox('worker-2', 10, 60);
    check_that(l_ids.COUNT = 0, 'a claimed (leased) message is not handed out again');

    UPDATE outbox_messages SET status = 'Failed', last_error = 'ERP 503' WHERE message_id = l_msg;
    UPDATE material_requests SET status = 'Failed' WHERE id = l_request;
    l_count := tersane_integration.requeue(l_msg, 'operator');
    SELECT status INTO l_status FROM material_requests WHERE id = l_request;
    check_that(l_count = 1 AND l_status = 'Submitted', 'requeue puts the failed message and the request back');
    check_that(tersane_integration.requeue(l_msg, 'operator') = 0, 'requeue of a non-failed message does nothing');

    l_count := tersane_integration.upsert_stock(t_stock_rows(
        t_stock_row('ST-AH36-08', 'SAC-SAHA', 64, 12, TIMESTAMP '2026-11-18 10:00:00'),
        t_stock_row('PR-HP160-9', 'PROFIL-01', 820, 140, TIMESTAMP '2026-11-18 10:00:00')));
    check_that(l_count = 2, 'stock MERGE from a collection inserts two rows');
    l_count := tersane_integration.upsert_stock(t_stock_rows(
        t_stock_row('ST-AH36-08', 'SAC-SAHA', 64, 12, TIMESTAMP '2026-11-18 10:00:00')));
    check_that(l_count = 0, 'replaying the same stock row changes nothing');
    l_count := tersane_integration.upsert_stock(t_stock_rows(
        t_stock_row('ST-AH36-08', 'SAC-SAHA', 50, 12, TIMESTAMP '2026-11-18 10:05:00')));
    check_that(l_count = 1, 'a changed quantity updates exactly one row');

    check_that('' IS NULL, 'Oracle treats the empty string as NULL (unlike SQL Server)');

    ROLLBACK;
    DBMS_OUTPUT.PUT_LINE('ALL PL/SQL TESTS PASSED');
END;
/
EXIT
