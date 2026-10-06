#!/usr/bin/env bash
# Fills k8s/overlays/aws/generated/ from Terraform outputs and SSM Parameter Store.
#   AWS_PROFILE=tinylink bash scripts/aws-overlay.sh
# Nothing here is committed: generated/ is gitignored, and secrets go straight from SSM into the file.
set -euo pipefail
cd "$(dirname "$0")/.."

TF="terraform -chdir=infra/aws"
EIP="$($TF output -raw elastic_ip)"
RDS="$($TF output -raw rds_address)"
DB_USER="tinylinkadmin"

ssm() { aws ssm get-parameter --with-decryption --name "/tinylink/$1" --query Parameter.Value --output text; }

DB_PASSWORD="$(ssm db/master-password)"
OUT=k8s/overlays/aws/generated
mkdir -p "$OUT"
umask 077

# TLS to RDS; TrustServerCertificate because the images don't carry the RDS CA bundle.
conn() { echo "Server=${RDS},1433;Database=$1;User Id=${DB_USER};Password=${DB_PASSWORD};Encrypt=True;TrustServerCertificate=True"; }

cat > "$OUT/secrets.env" <<EOF
ApiKey=$(ssm api-key)
LinkServiceDb=$(conn LinkServiceDb)
AnalyticsServiceDb=$(conn AnalyticsServiceDb)
OperatorPasswordHash=$(ssm operator-password-hash)
GrafanaAdminPassword=$(ssm grafana-admin-password)
GrafanaSqlHost=${RDS}:1433
GrafanaSqlUser=${DB_USER}
GrafanaSqlPassword=${DB_PASSWORD}
EOF

cat > "$OUT/config.env" <<EOF
Console__ShortUrlBase=http://${EIP}/
EOF

cat > "$OUT/ingress-hosts.yaml" <<EOF
- op: replace
  path: /spec/rules/0/host
  value: console.${EIP}.nip.io
- op: replace
  path: /spec/rules/1/host
  value: grafana.${EIP}.nip.io
EOF

echo "Wrote $OUT (EIP ${EIP}, RDS ${RDS})"
echo "Short links: http://${EIP}/<code>"
echo "Console:     http://console.${EIP}.nip.io"
echo "Grafana:     http://grafana.${EIP}.nip.io"
