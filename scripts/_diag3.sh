#!/bin/bash
USER=$(docker exec oet-postgres bash -c 'echo $POSTGRES_USER')
DB=$(docker exec oet-postgres bash -c 'echo $POSTGRES_DB')
PID=db92cbeee3f54b6d895d7b2e640d24ad
echo "=== Extracts difficulty ==="
docker exec oet-postgres psql -U "$USER" -d "$DB" -c "select p.\"PartCode\", e.\"DisplayOrder\", e.\"DifficultyRating\" from \"ListeningExtracts\" e join \"ListeningParts\" p on p.\"Id\"=e.\"ListeningPartId\" where p.\"PaperId\"='$PID' order by 1,2;"
echo "=== Questions difficulty ==="
docker exec oet-postgres psql -U "$USER" -d "$DB" -c "select \"DifficultyLevel\", count(*) from \"ListeningQuestions\" where \"PaperId\"='$PID' group by 1;"
