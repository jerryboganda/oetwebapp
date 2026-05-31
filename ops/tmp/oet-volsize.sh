#!/usr/bin/env bash
set +e
echo '=== POSTGRES VOLUME SIZES (host fs, no docker daemon) ==='
echo '--- oetwebapp_oet_postgres_data ---'
du -sh /var/lib/docker/volumes/oetwebapp_oet_postgres_data/_data 2>&1
echo '--- oetwebsite_oet_postgres_data ---'
du -sh /var/lib/docker/volumes/oetwebsite_oet_postgres_data/_data 2>&1
echo '=== oetwebapp postgres base dir listing ==='
ls -la /var/lib/docker/volumes/oetwebapp_oet_postgres_data/_data/base 2>&1 | head -20
echo '=== oetwebsite postgres base dir listing ==='
ls -la /var/lib/docker/volumes/oetwebsite_oet_postgres_data/_data/base 2>&1 | head -20
echo '=== STORAGE VOLUME SIZES ==='
echo '--- oetwebapp_oet_learner_storage ---'
du -sh /var/lib/docker/volumes/oetwebapp_oet_learner_storage/_data 2>&1
echo '--- oetwebsite_oet_learner_storage ---'
du -sh /var/lib/docker/volumes/oetwebsite_oet_learner_storage/_data 2>&1
echo '=== DONE ==='
