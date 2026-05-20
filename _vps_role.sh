#!/bin/bash
docker exec oet-postgres bash -c '
psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "
SELECT \"Role\", \"Part\", COUNT(*)
FROM \"ContentPaperAssets\"
WHERE \"PaperId\" = '\''c5e5f35210ad4f00b4cf0ed45cb1ec5f'\''
GROUP BY \"Role\", \"Part\"
ORDER BY \"Role\", \"Part\";
"
psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "
SELECT column_name, data_type FROM information_schema.columns
WHERE table_name = '\''ContentPaperAssets'\'' AND column_name IN ('\''Role'\'','\''Part'\'');
"
'
