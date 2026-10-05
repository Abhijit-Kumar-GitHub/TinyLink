#!/usr/bin/env bash
# Builds every service image tagged with the current git commit SHA (plus 'latest').
#   scripts/build-images.sh          build only
#   scripts/build-images.sh --push   build and push to Docker Hub (run 'docker login' first)
# The SHA tag is what the webhook deployer sets on the Kubernetes Deployments.
set -euo pipefail
cd "$(dirname "$0")/.."

REGISTRY="${REGISTRY:-farspawn}"
TAG="$(git rev-parse --short HEAD)"
PUSH="${1:-}"

if [[ -n "$(git status --porcelain --untracked-files=no)" && "${ALLOW_DIRTY:-}" != 1 ]]; then
  echo "Uncommitted changes: image tag $TAG would not match its commit. Commit first, or set ALLOW_DIRTY=1." >&2
  exit 1
fi

for entry in link:LinkService analytics:AnalyticsService redirect:RedirectService console:OperatorConsole; do
  name="${entry%%:*}"
  project="${entry##*:}"
  image="$REGISTRY/tinylink-$name"

  echo "==> $image:$TAG"
  # EC2 t3 instances are x86_64; pin the platform so a build on ARM hardware still runs there.
  docker build --platform linux/amd64 -f "src/$project/Dockerfile" -t "$image:$TAG" -t "$image:latest" .

  if [[ "$PUSH" == "--push" ]]; then
    docker push "$image:$TAG"
    docker push "$image:latest"
  fi
done

echo "Done: tag $TAG"
