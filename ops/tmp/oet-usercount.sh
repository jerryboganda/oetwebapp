#!/usr/bin/env bash
set -e
docker exec oet-postgres psql -U oet_with_dr_hesham -d oet_with_dr_hesham -tA -c "select count(*) from \"Users\";"
