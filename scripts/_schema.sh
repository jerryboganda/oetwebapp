#!/bin/bash
echo === LQ ===
docker exec oet-postgres psql -U oet_learner oet_learner -c '\d "ListeningQuestions"'
echo === LP ===
docker exec oet-postgres psql -U oet_learner oet_learner -c '\d "ListeningParts"'
echo === LE ===
docker exec oet-postgres psql -U oet_learner oet_learner -c '\d "ListeningExtracts"'
