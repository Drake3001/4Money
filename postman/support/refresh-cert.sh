#!/usr/bin/env bash
set -euo pipefail
umask 077
mkdir -p .cache/banking-refresh
openssl req -x509 -newkey rsa:2048 -nodes -days 14 \
  -keyout .cache/banking-refresh/localhost.key \
  -out .cache/banking-refresh/localhost.crt \
  -subj '/CN=localhost' -addext 'subjectAltName=DNS:localhost,IP:127.0.0.1' \
  -addext 'basicConstraints=critical,CA:TRUE' 2>/dev/null
printf '%s\n' 'Local certificate created in .cache/banking-refresh (trust only in the test process).'
