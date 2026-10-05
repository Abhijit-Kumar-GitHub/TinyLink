#!/usr/bin/env bash
# Writes a kubeconfig that authenticates as the webhook-deployer ServiceAccount (least privilege).
#   make-kubeconfig.sh <api-server-url> <output-file> [kubectl context to read the token with]
# On the node:  make-kubeconfig.sh https://127.0.0.1:6443 /opt/tinylink-webhook/kubeconfig
set -euo pipefail
SERVER="$1"
OUT="$2"
CONTEXT_ARGS=()
[[ $# -ge 3 ]] && CONTEXT_ARGS=(--context "$3")

secret_field() {
  kubectl "${CONTEXT_ARGS[@]}" -n tinylink get secret webhook-deployer-token -o "jsonpath={.data.$1}"
}

TOKEN="$(secret_field token | base64 -d)"
CA="$(secret_field 'ca\.crt')"
[[ -n "$TOKEN" && -n "$CA" ]] || { echo "webhook-deployer-token not populated yet" >&2; exit 1; }

umask 077
cat > "$OUT" <<EOF
apiVersion: v1
kind: Config
clusters:
  - name: tinylink
    cluster:
      server: ${SERVER}
      certificate-authority-data: ${CA}
users:
  - name: webhook-deployer
    user:
      token: ${TOKEN}
contexts:
  - name: webhook-deployer
    context:
      cluster: tinylink
      user: webhook-deployer
      namespace: tinylink
current-context: webhook-deployer
EOF
echo "Wrote $OUT"
