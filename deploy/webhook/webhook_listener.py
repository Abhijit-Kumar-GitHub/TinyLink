#!/usr/bin/env python3
"""TinyLink deploy-only GitHub webhook listener (Python 3 standard library only).

Runs on the traffic node, outside the cluster. It never builds anything: images are built and pushed
from the developer machine by scripts/build-images.sh, tagged with the 7-character commit SHA. On a
verified push to the deploy branch it waits for that tag to exist on Docker Hub, points every
Deployment at it, waits for the rollouts, and rolls all of them back if any rollout fails.

Usage:
  webhook_listener.py serve                 run the HTTP listener (default)
  webhook_listener.py deploy <sha-or-tag>   deploy a tag by hand
  webhook_listener.py rollback              roll every Deployment back one revision

Environment:
  WEBHOOK_SECRET or WEBHOOK_SECRET_FILE   shared secret configured on the GitHub webhook (required for serve)
  WEBHOOK_PORT=9000  DEPLOY_BRANCH=main  IMAGE_REGISTRY=farspawn  K8S_NAMESPACE=tinylink
  KUBECTL=kubectl    IMAGE_WAIT_SECONDS=900  ROLLOUT_TIMEOUT=180s
"""
import hashlib
import hmac
import json
import logging
import os
import re
import subprocess
import sys
import threading
import time
import urllib.error
import urllib.request
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

PORT = int(os.environ.get("WEBHOOK_PORT", "9000"))
BRANCH = os.environ.get("DEPLOY_BRANCH", "main")
REGISTRY = os.environ.get("IMAGE_REGISTRY", "farspawn")
NAMESPACE = os.environ.get("K8S_NAMESPACE", "tinylink")
KUBECTL = os.environ.get("KUBECTL", "kubectl").split()
IMAGE_WAIT_SECONDS = int(os.environ.get("IMAGE_WAIT_SECONDS", "900"))
ROLLOUT_TIMEOUT = os.environ.get("ROLLOUT_TIMEOUT", "180s")
MAX_BODY_BYTES = 1_000_000

# Deployment name (== container name in the manifests) -> Docker Hub repository.
DEPLOYMENTS = {
    "link-service": "tinylink-link",
    "analytics-service": "tinylink-analytics",
    "redirect-service": "tinylink-redirect",
    "operator-console": "tinylink-console",
}

FULL_SHA = re.compile(r"^[0-9a-f]{40}$")
TAG = re.compile(r"^[0-9a-f]{7}$")

log = logging.getLogger("tinylink-webhook")
deploy_lock = threading.Lock()


def verify_signature(secret: bytes, body: bytes, header: str | None) -> bool:
    """GitHub signs the raw body with HMAC-SHA256 in X-Hub-Signature-256."""
    if not header or not header.startswith("sha256="):
        return False
    expected = "sha256=" + hmac.new(secret, body, hashlib.sha256).hexdigest()
    return hmac.compare_digest(expected, header)


def tag_exists(repository: str, tag: str) -> bool:
    url = f"https://hub.docker.com/v2/repositories/{REGISTRY}/{repository}/tags/{tag}"
    try:
        with urllib.request.urlopen(url, timeout=10) as response:
            return response.status == 200
    except urllib.error.HTTPError as error:
        if error.code == 404:
            return False
        raise


def wait_for_images(tag: str) -> bool:
    """The push webhook can arrive before build-images.sh has finished pushing; wait for every image."""
    deadline = time.monotonic() + IMAGE_WAIT_SECONDS
    while True:
        try:
            missing = [repo for repo in DEPLOYMENTS.values() if not tag_exists(repo, tag)]
        except (urllib.error.URLError, TimeoutError) as error:
            log.warning("Docker Hub check failed (%s); retrying", error)
            missing = ["<registry unreachable>"]
        if not missing:
            return True
        if time.monotonic() >= deadline:
            log.error("Gave up after %ss waiting for %s:%s", IMAGE_WAIT_SECONDS, ", ".join(missing), tag)
            return False
        log.info("Waiting for images with tag %s: %s", tag, ", ".join(missing))
        time.sleep(15)


def kubectl(*args: str, check: bool = True) -> subprocess.CompletedProcess:
    # Argument list, never a shell: nothing from the webhook payload is ever interpreted.
    result = subprocess.run([*KUBECTL, "-n", NAMESPACE, *args], capture_output=True, text=True, timeout=300)
    output = (result.stdout + result.stderr).strip()
    if output:
        log.info("kubectl %s: %s", " ".join(args[:2]), output.replace("\n", " | "))
    if check and result.returncode != 0:
        raise RuntimeError(f"kubectl {' '.join(args)} failed with exit code {result.returncode}")
    return result


def current_image(deployment: str) -> str:
    return kubectl("get", f"deployment/{deployment}", "-o", f"jsonpath={{.spec.template.spec.containers[?(@.name=='{deployment}')].image}}").stdout.strip()


def deploy(tag: str, cause: str) -> bool:
    if not TAG.match(tag):
        log.error("Refusing to deploy invalid tag %r", tag)
        return False

    with deploy_lock:
        log.info("Deploying %s (%s)", tag, cause)
        if not wait_for_images(tag):
            return False

        changed = []
        try:
            for deployment, repository in DEPLOYMENTS.items():
                image = f"{REGISTRY}/{repository}:{tag}"
                if current_image(deployment) == image:
                    continue
                kubectl("set", "image", f"deployment/{deployment}", f"{deployment}={image}")
                kubectl("annotate", f"deployment/{deployment}", f"kubernetes.io/change-cause={cause}", "--overwrite")
                changed.append(deployment)

            if not changed:
                log.info("Already running %s; nothing to do", tag)
                return True

            failed = [d for d in changed if kubectl("rollout", "status", f"deployment/{d}", f"--timeout={ROLLOUT_TIMEOUT}", check=False).returncode != 0]
        except Exception:
            log.exception("Deploy of %s failed part-way", tag)
            failed = changed

        if failed:
            # Undo only what this deploy changed, so every service ends up back on the same version.
            log.error("Rollout failed for %s; rolling back %s", ", ".join(failed), ", ".join(changed))
            for deployment in changed:
                kubectl("rollout", "undo", f"deployment/{deployment}", check=False)
            return False

        log.info("Deployed %s to %s", tag, ", ".join(changed))
        return True


def rollback() -> bool:
    ok = True
    for deployment in DEPLOYMENTS:
        ok &= kubectl("rollout", "undo", f"deployment/{deployment}", check=False).returncode == 0
    for deployment in DEPLOYMENTS:
        ok &= kubectl("rollout", "status", f"deployment/{deployment}", f"--timeout={ROLLOUT_TIMEOUT}", check=False).returncode == 0
    return ok


class WebhookHandler(BaseHTTPRequestHandler):
    server_version = "TinyLinkWebhook/1.0"
    secret: bytes = b""

    def do_GET(self) -> None:
        if self.path == "/healthz":
            self._reply(200, "ok")
        else:
            self._reply(404, "not found")

    def do_POST(self) -> None:
        if self.path != "/webhook":
            self._reply(404, "not found")
            return

        length = int(self.headers.get("Content-Length") or 0)
        if length <= 0 or length > MAX_BODY_BYTES:
            self._reply(413, "bad body size")
            return
        body = self.rfile.read(length)

        delivery = self.headers.get("X-GitHub-Delivery", "?")
        if not verify_signature(self.secret, body, self.headers.get("X-Hub-Signature-256")):
            log.warning("Rejected delivery %s from %s: bad signature", delivery, self.client_address[0])
            self._reply(401, "bad signature")
            return

        event = self.headers.get("X-GitHub-Event", "")
        if event == "ping":
            self._reply(200, "pong")
            return
        if event != "push":
            self._reply(202, f"ignored event {event}")
            return

        try:
            payload = json.loads(body)
        except json.JSONDecodeError:
            self._reply(400, "invalid json")
            return

        if payload.get("ref") != f"refs/heads/{BRANCH}" or payload.get("deleted"):
            self._reply(202, f"ignored {payload.get('ref')}")
            return

        sha = str(payload.get("after", ""))
        if not FULL_SHA.match(sha):
            self._reply(400, "invalid commit sha")
            return

        tag = sha[:7]
        message = str((payload.get("head_commit") or {}).get("message", "")).splitlines()[0:1]
        cause = f"github push {tag}: {message[0][:80] if message else ''}".strip()

        # GitHub gives up after 10 s, and image waits/rollouts take minutes: answer now, deploy in the background.
        threading.Thread(target=deploy, args=(tag, cause), daemon=True).start()
        log.info("Accepted delivery %s: deploying %s", delivery, tag)
        self._reply(202, f"deploying {tag}")

    def _reply(self, status: int, text: str) -> None:
        data = (text + "\n").encode()
        self.send_response(status)
        self.send_header("Content-Type", "text/plain; charset=utf-8")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def log_message(self, fmt: str, *args) -> None:
        log.debug("%s %s", self.client_address[0], fmt % args)


def load_secret() -> bytes:
    secret = os.environ.get("WEBHOOK_SECRET", "")
    if not secret and os.environ.get("WEBHOOK_SECRET_FILE"):
        with open(os.environ["WEBHOOK_SECRET_FILE"], encoding="utf-8") as f:
            secret = f.read().strip()
    if len(secret) < 16:
        sys.exit("WEBHOOK_SECRET (or WEBHOOK_SECRET_FILE) must be set to at least 16 characters")
    return secret.encode()


def main() -> None:
    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
    command = sys.argv[1] if len(sys.argv) > 1 else "serve"

    if command == "deploy" and len(sys.argv) == 3:
        sys.exit(0 if deploy(sys.argv[2][:7], f"manual deploy {sys.argv[2][:7]}") else 1)
    if command == "rollback":
        sys.exit(0 if rollback() else 1)
    if command != "serve":
        sys.exit(__doc__)

    WebhookHandler.secret = load_secret()
    server = ThreadingHTTPServer(("0.0.0.0", PORT), WebhookHandler)
    log.info("Listening on :%s for pushes to %s (namespace %s, registry %s)", PORT, BRANCH, NAMESPACE, REGISTRY)
    server.serve_forever()


if __name__ == "__main__":
    main()
