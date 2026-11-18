#!/bin/bash
# PL/SQL tests: the functional suite, then a two-session SKIP LOCKED check.
# Inside the container:  /opt/tersane-sql/run-tests.sh
set -euo pipefail

CONN="${APP_USER:-tersane}/${APP_USER_PASSWORD:-Tersane_Demo_2026}@//localhost/FREEPDB1"
cd /opt/tersane-sql

echo "→ 90_tests.sql"
sqlplus -s -L "${CONN}" @90_tests.sql

echo "→ concurrency: two sessions claim at the same time"
# Session A queues one message (committed), claims it and keeps the row lock for 6 seconds before rolling back.
sqlplus -s -L "${CONN}" <<'SQL' > /tmp/session-a.log &
WHENEVER SQLERROR EXIT FAILURE ROLLBACK
SET SERVEROUTPUT ON FEEDBACK OFF
DECLARE
    l_ids t_id_list;
BEGIN
    DELETE FROM outbox_messages WHERE idempotency_key = 'concurrency-test';
    INSERT INTO outbox_messages (message_type, aggregate_id, idempotency_key, payload)
    VALUES ('Test', '0', 'concurrency-test', '{}');
    COMMIT;
    l_ids := tersane_integration.claim_outbox('session-a', 10, 60);
    DBMS_OUTPUT.PUT_LINE('session A claimed ' || l_ids.COUNT);
    DBMS_SESSION.SLEEP(6);
    ROLLBACK;
END;
/
EXIT
SQL
sleep 3

# Session B sees the committed row as Pending, but it is locked by A: SKIP LOCKED must skip it, not wait.
sqlplus -s -L "${CONN}" <<'SQL'
WHENEVER SQLERROR EXIT FAILURE ROLLBACK
SET SERVEROUTPUT ON FEEDBACK OFF
DECLARE
    l_ids   t_id_list;
    l_start TIMESTAMP := SYSTIMESTAMP;
BEGIN
    l_ids := tersane_integration.claim_outbox('session-b', 10, 60);
    IF l_ids.COUNT <> 0 OR SYSTIMESTAMP - l_start > INTERVAL '2' SECOND THEN
        RAISE_APPLICATION_ERROR(-20999, 'TEST FAILED: session B got ' || l_ids.COUNT || ' row(s) or waited for the lock');
    END IF;
    DBMS_OUTPUT.PUT_LINE('ok   session B skipped the row locked by session A without waiting (FOR UPDATE SKIP LOCKED)');
    ROLLBACK;
END;
/
EXIT
SQL

wait
cat /tmp/session-a.log
sqlplus -s -L "${CONN}" <<'SQL'
WHENEVER SQLERROR EXIT FAILURE
DELETE FROM outbox_messages WHERE idempotency_key = 'concurrency-test';
COMMIT;
EXIT
SQL
echo "ALL ORACLE CHECKS PASSED"
