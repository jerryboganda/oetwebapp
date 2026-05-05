#!/bin/bash
for i in 1 2 3 4 5 6 7 8 9 10 11 12; do
  sleep 60
  n=$(pgrep -af "compose|buildkitd|docker-buildx" | grep -v pgrep | wc -l)
  echo "[poll $i] procs=$n"
  if [ "$n" -lt 2 ]; then echo DONE; break; fi
done
echo "---LOG---"
tail -30 /root/oetwebsite/deploy.log
echo "---PS---"
docker ps