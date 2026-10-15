#!/usr/bin/env bash
set -euo pipefail
umask 077

: "${TF_PLAN_DIR:?Missing private plan directory}"
: "${GITHUB_OUTPUT:?Missing GitHub step output file}"
: "${GITHUB_STEP_SUMMARY:?Missing GitHub job summary file}"

status=0
terraform plan -input=false -lock-timeout=5m -no-color \
  -detailed-exitcode -out="$TF_PLAN_DIR/deployment.tfplan" || status=$?
case "$status" in
  0|2) ;;
  *) echo "::error::Terraform plan failed; apply is disabled."; exit "$status" ;;
esac

terraform show -json "$TF_PLAN_DIR/deployment.tfplan" \
  | python3 scripts/check-plan.py --summary "$GITHUB_STEP_SUMMARY"

if [[ "$status" == 2 ]]; then
  echo "has_changes=true" >> "$GITHUB_OUTPUT"
else
  echo "has_changes=false" >> "$GITHUB_OUTPUT"
  echo "No infrastructure or output changes; apply will be skipped." >> "$GITHUB_STEP_SUMMARY"
fi
