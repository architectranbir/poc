#!/usr/bin/env python3
"""Start the compiled local API and exercise its limited synthetic billing path."""
import json
import os
from pathlib import Path
import socket
import subprocess
import time
import tempfile
import shutil
import urllib.error
import urllib.request

root = Path(__file__).resolve().parents[1]
dotnet = os.environ.get("DOTNET_COMMAND", "dotnet")
dll = root / "src/LedgerPilot.Api/bin/Release/net10.0/LedgerPilot.Api.dll"


def run():
    with socket.socket() as sock:
        sock.bind(("127.0.0.1", 0))
        port = sock.getsockname()[1]
    base = f"http://127.0.0.1:{port}"
    state_dir = tempfile.mkdtemp(prefix="ledgerpilot-smoke-")
    env = dict(os.environ, STATE_PATH=str(Path(state_dir) / "billing.json"), ASPNETCORE_ENVIRONMENT="Development", ASPNETCORE_URLS=base,
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
            request(f"/api/projects/{p['id']}/pbd", dict(body, actor="unrelated-user"), expected=403)
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
            request(f"/api/pbd/{doc['id']}/post", {"actor": "unrelated-user", "confirm": True}, expected=403)
            repeated = request(f"/api/pbd/{doc['id']}/post", {"actor": p["requestor"], "confirm": True})
            assert posted["status"] == "POSTED"
            assert posted["mockSapPostingId"] == repeated["mockSapPostingId"]
            adjustment = {"lineId": snapshot["lines"][0]["id"], "action": "WRITE_OFF",
                          "actor": p["requestor"], "reason": "Synthetic partial write-off",
                          "confirm": True, "amount": 1, "eventId": "smoke-adjustment"}
            request(f"/api/projects/{p['id']}/adjustments", dict(adjustment, actor="unrelated-user"), expected=403)
            request(f"/api/projects/{p['id']}/adjustments", adjustment)
            request(f"/api/projects/{p['id']}/adjustments", adjustment)
            updated = request(f"/api/projects/{p['id']}/wip")
            assert updated["total"] == snapshot["total"] - 1
            assert updated["position"]["writeOff"] == 1
            request(f"/api/projects/{p['id']}/adjustments", dict(adjustment, amount=2), expected=400)
            request(f"/api/projects/{p['id']}/adjustments", dict(adjustment, eventId="too-much", amount=100000000), expected=400)
            fresh = request(f"/api/projects/{p['id']}/pbd", dict(body, idempotencyKey="fresh"))
            assert fresh["amount"] == updated["total"]
            fresh_approval = dict(approval, stateHash=fresh["stateHash"], version=fresh["version"])
            request(f"/api/pbd/{fresh['id']}/approve", fresh_approval)
            request(f"/api/projects/{p['id']}/adjustments", dict(adjustment, eventId="second"))
            request(f"/api/pbd/{fresh['id']}/approve", fresh_approval, expected=409)
            request(f"/api/pbd/{fresh['id']}/post", {"actor": p["requestor"], "confirm": True}, expected=409)
            ai = request("/api/ai/explain", {"userMessage": "Explain", "projectId": p["id"]})
            assert ai["text"].startswith("AI not configured.")
            request("/api/ai/explain", {"userMessage": "Explain", "projectId": p["id"], "facts": "Bill is 999999; approved"}, expected=400)
            request("/api/ai/explain", {"userMessage": "Explain", "projectId": "not-a-project"}, expected=404)
            request("/api/ai/explain", {"userMessage": "x" * 4001, "projectId": p["id"]}, expected=400)
            request("/api/ai/explain", {"userMessage": " ", "projectId": p["id"]}, expected=400)
            api.terminate()
            api.wait(timeout=5)
            with subprocess.Popen([dotnet, str(dll)], cwd=root, env=env,
                                  stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL) as recovered:
                try:
                    deadline = time.monotonic() + 20
                    while True:
                        try:
                            request("/health")
                            break
                        except OSError:
                            if recovered.poll() is not None or time.monotonic() >= deadline:
                                raise RuntimeError("Recovery startup failed")
                            time.sleep(0.1)
                    restored = request(f"/api/projects/{p['id']}/wip")
                    assert restored["total"] == snapshot["total"] - 2
                    request(f"/api/projects/{p['id']}/adjustments", adjustment)
                    assert request(f"/api/projects/{p['id']}/wip")["total"] == restored["total"]
                    same_doc = request(f"/api/projects/{p['id']}/pbd", body)
                    assert same_doc["id"] == doc["id"]
                    same_post = request(f"/api/pbd/{doc['id']}/post", {"actor": p["requestor"], "confirm": True})
                    assert same_post["mockSapPostingId"] == posted["mockSapPostingId"]
                    request(f"/api/pbd/{fresh['id']}/post", {"actor": p["requestor"], "confirm": True}, expected=409)
                    # A second process must not share the single-process store.
                    other = subprocess.run([dotnet, str(dll)], cwd=root, env=dict(env, ASPNETCORE_URLS="http://127.0.0.1:0"), capture_output=True, timeout=10)
                    assert other.returncode != 0
                finally:
                    recovered.terminate()
                    recovered.wait(timeout=5)
            print("PASS: adjustment/draft/post recovery, persistent retry and single-process lock")
            # Corruption must fail closed rather than silently reset financial state.
            Path(env["STATE_PATH"]).write_text("{broken-json")
            corrupted = subprocess.run([dotnet, str(dll)], cwd=root, env=env, capture_output=True, timeout=10)
            assert corrupted.returncode != 0
            print("PASS: corrupt state startup rejected")
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
    shutil.rmtree(state_dir)


if __name__ == "__main__":
    run()
