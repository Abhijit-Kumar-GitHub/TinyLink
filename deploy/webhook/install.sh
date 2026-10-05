#!/usr/bin/env bash
# Installs the webhook listener as a systemd service on the k3s traffic node. Run as root:
#   sudo ./install.sh
# The webhook secret comes from AWS SSM Parameter Store (SecureString /tinylink/webhook-secret, read
# with the instance role), or from the WEBHOOK_SECRET environment variable if set.
set -euo pipefail
cd "$(dirname "$0")"

SSM_PARAMETER="${SSM_PARAMETER:-/tinylink/webhook-secret}"
INSTALL_DIR=/opt/tinylink-webhook

id tinylink-webhook >/dev/null 2>&1 || useradd --system --no-create-home --shell /usr/sbin/nologin tinylink-webhook
install -d -m 0750 -o root -g tinylink-webhook "$INSTALL_DIR"
install -m 0644 webhook_listener.py "$INSTALL_DIR/webhook_listener.py"

./make-kubeconfig.sh https://127.0.0.1:6443 "$INSTALL_DIR/kubeconfig"
chown root:tinylink-webhook "$INSTALL_DIR/kubeconfig"
chmod 0640 "$INSTALL_DIR/kubeconfig"

if [[ -z "${WEBHOOK_SECRET:-}" ]]; then
  WEBHOOK_SECRET="$(aws ssm get-parameter --name "$SSM_PARAMETER" --with-decryption --query Parameter.Value --output text)"
fi
umask 077
printf 'WEBHOOK_SECRET=%s\nKUBECTL=/usr/local/bin/kubectl\n' "$WEBHOOK_SECRET" > /etc/tinylink-webhook.env
chown root:tinylink-webhook /etc/tinylink-webhook.env
chmod 0640 /etc/tinylink-webhook.env

install -m 0644 tinylink-webhook.service /etc/systemd/system/tinylink-webhook.service
systemctl daemon-reload
systemctl enable --now tinylink-webhook
systemctl --no-pager status tinylink-webhook | head -n 5
