#!/usr/bin/env bash
set -euo pipefail
timeout 60 docker exec -i oet-postgres psql -U oet_with_dr_hesham -d oet_with_dr_hesham -P pager=off <<'SQL'
select "Email","Role","CreatedAt" from "Users" order by "CreatedAt";
SQL
