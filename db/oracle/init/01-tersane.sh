#!/bin/bash
# Runs once, on the first start of the Oracle Free container (gvenzl/oracle-free image convention):
# creates the schema and the package as APP_USER, then runs the PL/SQL tests.
set -euo pipefail

CONN="${APP_USER}/${APP_USER_PASSWORD}@//localhost/FREEPDB1"
cd /opt/tersane-sql

for f in 01_schema.sql 02_tersane_integration.pks 03_tersane_integration.pkb; do
  echo "→ ${f}"
  sqlplus -s -L "${CONN}" @"${f}"
done

bash /opt/tersane-sql/run-tests.sh
