#!/usr/bin/env bash
# kubectl access to the AWS cluster through SSM Session Manager: no SSH, no open port 6443, works from
# any IP (e.g. a mobile hotspot). Needs the AWS CLI + Session Manager plugin and AWS_PROFILE=tinylink.
#   bash scripts/aws-kube.sh kubeconfig   fetch the k3s admin kubeconfig -> ~/.kube/tinylink-aws.yaml
#   bash scripts/aws-kube.sh tunnel       forward localhost:16443 -> k3s API (leave it running)
# Then:  kubectl --kubeconfig ~/.kube/tinylink-aws.yaml get nodes
set -euo pipefail

LOCAL_PORT=16443
KUBECONFIG_OUT="$HOME/.kube/tinylink-aws.yaml"

traffic_node() {
  aws ec2 describe-instances \
    --filters Name=tag:Name,Values=tinylink-traffic Name=instance-state-name,Values=running \
    --query 'Reservations[0].Instances[0].InstanceId' --output text
}

case "${1:-}" in
  kubeconfig)
    id="$(traffic_node)"
    cmd="$(aws ssm send-command --instance-ids "$id" --document-name AWS-RunShellScript \
      --parameters 'commands=["cat /etc/rancher/k3s/k3s.yaml"]' --query Command.CommandId --output text)"
    aws ssm wait command-executed --command-id "$cmd" --instance-id "$id"
    mkdir -p "$(dirname "$KUBECONFIG_OUT")"
    umask 077
    # The cert covers 127.0.0.1, so point the client at the local end of the tunnel.
    aws ssm get-command-invocation --command-id "$cmd" --instance-id "$id" \
      --query StandardOutputContent --output text \
      | sed -e "s#https://127.0.0.1:6443#https://127.0.0.1:${LOCAL_PORT}#" -e "s#: default\$#: tinylink-aws#" \
      > "$KUBECONFIG_OUT"
    echo "Wrote $KUBECONFIG_OUT (admin credentials: keep private)"
    ;;
  tunnel)
    id="$(traffic_node)"
    echo "Forwarding localhost:${LOCAL_PORT} -> ${id}:6443 (Ctrl+C to stop)"
    exec aws ssm start-session --target "$id" --document-name AWS-StartPortForwardingSession \
      --parameters "{\"portNumber\":[\"6443\"],\"localPortNumber\":[\"${LOCAL_PORT}\"]}"
    ;;
  *)
    sed -n '2,7p' "$0"
    exit 1
    ;;
esac
