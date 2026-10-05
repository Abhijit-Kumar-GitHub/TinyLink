#!/usr/bin/env bash
# Creates the local two-node k3d cluster that mirrors the AWS layout, then deploys the local overlay.
#   server node = role "traffic" (EC2 node 1: ingress, link, redirect)
#   agent node  = role "ops"     (EC2 node 2: analytics, console, monitoring)
# Short links: http://localhost:8088/<code>   Console: http://console.localhost:8088
# Needs: k3d, kubectl, the dev SQL container (docker start tinylink-sql), k8s/overlays/local/secrets.env
set -euo pipefail
cd "$(dirname "$0")/.."

if ! k3d cluster list tinylink >/dev/null 2>&1; then
  # --api-port on 127.0.0.1: on Windows the default host.docker.internal address is often unreachable.
  k3d cluster create tinylink \
    --servers 1 --agents 1 \
    --api-port 127.0.0.1:6550 \
    -p "8088:80@loadbalancer" \
    --k3s-node-label "tinylink.io/role=traffic@server:0" \
    --k3s-node-label "tinylink.io/role=ops@agent:0" \
    --wait
fi

kubectl config use-context k3d-tinylink
kubectl apply -k k8s/overlays/local
for d in link-service analytics-service redirect-service operator-console; do
  kubectl -n tinylink rollout status "deploy/$d" --timeout=300s
done
kubectl -n tinylink get pods -o wide
