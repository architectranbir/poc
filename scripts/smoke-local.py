#!/usr/bin/env python3
"""Start the compiled local API and exercise its limited synthetic billing path."""
import json
import os
from pathlib import Path
import socket
import subprocess
import time
import urllib.error
import urllib.request

root = Path(__file__).resolve().parents[1]
dotnet = os.environ.get("DOTNET_COMMAND", "dotnet")
dll = root / "src/Copily.Api/bin/Release/net10.0/Copily.Api.dll"


def run():
    with socket.socket() as sock:
        sock.bind(("127.0.0.1", 0))
        port = sock.getsockname()[1]
    base = f"http://127.0.0.1:{port}"
    env = dict(os.environ, ASPNETCORE_ENVIRONMENT="Development", ASPNETCORE_URLS=base,
               FOUNDRY_PROJECT_ENDPOINT="", FOUNDRY_MODEL="", BLOB_CONTAINER="", COSMOS_ENDPOINT="")
    with subprocess.Popen([dotnet, str(dll)], cwd=root, env=env,
                          stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL) as api:
        def request(path, body=None, expected=200):
            data = None if body is None else json.dumps(body).encode()
            req = urllib.request.Request(base + path, data=data,
                                         headers={"Content-Type": "application/json"})
            try:
                response = urllib.request.urlopen(req, timeout=5)
            except urllib.error.HTTPError as error:
                response = error
            with response:
                assert response.status == expected, (path, response.status, response.read())
                raw = response.read()
                return json.loads(raw) if raw else None

        try:
            deadline = time.monotonic() + 20
            while True:
                try:
                    request("/health")
                    break
                except (OSError, AssertionError):
                    if api.poll() is not None or time.monotonic() >= deadline:
                        raise RuntimeError("API did not start")
                    time.sleep(0.1)
            projects = request("/api/projects")
            assert len(projects) == 5
            request("/api/projects/not-a-project/wip", expected=404)
            # Choose a synthetic project that has no hard findings in the inherited subset.
            snapshot = next(s for s in (request(f"/api/projects/{p['id']}/wip") for p in projects)
                            if not any(f["severity"] == "BLOCK" for f in s["findings"]))
            p = snapshot["project"]
            request(f"/api/projects/{p['id']}/adjustments",
                    {"lineId": snapshot["lines"][0]["id"], "action": "WRITE_OFF",
                     "actor": p["requestor"], "reason": "test", "confirm": False}, expected=400)
            body = {"actor": p["requestor"], "confirm": True,
                    "narrative": "Synthetic test", "idempotencyKey": "smoke-draft"}
            doc = request(f"/api/projects/{p['id']}/pbd", body)
            again = request(f"/api/projects/{p['id']}/pbd", body)
            assert doc["id"] == again["id"]
            request(f"/api/pbd/{doc['id']}/post", {"actor": p["requestor"], "confirm": True}, expected=409)
            approval = {"actor": "unrelated-user", "confirm": True,
                        "stateHash": doc["stateHash"], "version": doc["version"]}
            request(f"/api/pbd/{doc['id']}/approve", approval, expected=403)
            approval["actor"] = p["requestor"]
            request(f"/api/pbd/{doc['id']}/approve", approval)
            posted = request(f"/api/pbd/{doc['id']}/post", {"actor": p["requestor"], "confirm": True})
            repeated = request(f"/api/pbd/{doc['id']}/post", {"actor": p["requestor"], "confirm": True})
            assert posted["status"] == "POSTED"
            assert posted["mockSapPostingId"] == repeated["mockSapPostingId"]
            ai = request("/api/ai/explain", {"userMessage": "Explain", "projectId": p["id"]})
            assert ai["text"].startswith("AI not configured.")
            print("PASS: local API smoke (synthetic subset only; no live model or durable workflow)")
        finally:
            api.terminate()
            try:
                api.wait(timeout=5)
            except subprocess.TimeoutExpired:
                api.kill()

    env["ASPNETCORE_ENVIRONMENT"] = "Production"
    result = subprocess.run([dotnet, str(dll)], cwd=root, env=env,
                            capture_output=True, timeout=10)
    assert result.returncode != 0
    assert b"Local development baseline only" in result.stderr
    print("PASS: non-Development startup rejected")


if __name__ == "__main__":
    run()
