#!/usr/bin/env bash
docker logs oet-api --since 5m 2>&1 \
  | grep -iE 'manwara|sending email|email sent|SmtpEmailSender' \
  | tail -10
