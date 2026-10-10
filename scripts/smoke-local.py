#!/usr/bin/env python3
"""Start the compiled local API and exercise its limited synthetic billing path."""
import json
from decimal import Decimal
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
    shutil.copytree(root / "seed", Path(state_dir) / "seed")
    env = dict(os.environ, SEED_PATH=str(Path(state_dir) / "seed"), AUTH_MODE="LocalDemo", STATE_PATH=str(Path(state_dir) / "billing.json"), ASPNETCORE_ENVIRONMENT="Development", ASPNETCORE_URLS=base,
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
            fixed = request("/api/projects/CU33924501P01/fixed-price-preview", {"cutoff": "2026-10-10", "milestoneIds": ["FP-02"]})
            assert fixed["position"]["proposedBill"] == 20000
            assert fixed["position"]["remainingContract"] == 40000
            for selected in [["FP-01"], ["FP-03"], ["unknown"], ["FP-02", "FP-02"]]:
                request("/api/projects/CU33924501P01/fixed-price-preview", {"cutoff": "2026-10-10", "milestoneIds": selected}, expected=400)
            request("/api/projects/not-a-project/wip", expected=404)
            # Choose a synthetic project that has no hard findings in the inherited subset.
            snapshot = next(s for s in (request(f"/api/projects/{p['id']}/wip") for p in projects)
                            if not any(f["severity"] == "BLOCK" for f in s["findings"]))
            p = snapshot["project"]
            commercial = request(f"/api/projects/{p['id']}/commercial-preview")
            assert Decimal(str(commercial["preview"]["gross"])) == Decimal(str(commercial["preview"]["billingNet"])) + Decimal(str(commercial["preview"]["vat"]))
            assert commercial["preview"]["exchangeRate"]["source"].startswith("Synthetic")
            request("/api/projects/not-a-project/commercial-preview", expected=404)
            request(f"/api/projects/{p['id']}/adjustments",
                    {"lineId": snapshot["lines"][0]["id"], "action": "WRITE_OFF",
                     "actor": p["requestor"], "reason": "test", "confirm": False}, expected=400)
            body = {"actor": p["requestor"], "confirm": True,
                    "narrative": "Synthetic test", "idempotencyKey": "smoke-draft", "vatConfirmed": True, "currencyConfirmed": True}
            request(f"/api/projects/{p['id']}/pbd", dict(body, actor="unrelated-user"), expected=403)
            confirms = [f for f in snapshot["findings"] if f["severity"] == "CONFIRM"]
            assert confirms, "Fixture must exercise confirmation gating"
            if confirms:
                request(f"/api/projects/{p['id']}/pbd", body, expected=400)
                request(f"/api/projects/{p['id']}/pbd", dict(body, acknowledgements=[{"findingId": f["id"], "dependencyHash": "stale"} for f in confirms]), expected=400)
            body["acknowledgements"] = [{"findingId": f["id"], "dependencyHash": f["dependencyHash"]} for f in confirms]
            request(f"/api/projects/{p['id']}/pbd", dict(body, vatConfirmed=False), expected=400)
            doc = request(f"/api/projects/{p['id']}/pbd", body)
            assert doc["commercial"]["gross"] == doc["commercial"]["billingNet"] + doc["commercial"]["vat"]
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
            alternate = next(x for x in projects if x["currency"] != x["billingCurrency"])
            alt_wip = request(f"/api/projects/{alternate['id']}/wip")
            alt_body = {"actor": alternate["requestor"], "confirm": True, "narrative": "Synthetic EUR bill",
                        "idempotencyKey": "alternate-draft", "vatConfirmed": True,
                        "acknowledgements": [{"findingId": f["id"], "dependencyHash": f["dependencyHash"]} for f in alt_wip["findings"] if f["severity"] == "CONFIRM"]}
            request(f"/api/projects/{alternate['id']}/pbd", alt_body, expected=400)
            alt_doc = request(f"/api/projects/{alternate['id']}/pbd", dict(alt_body, currencyConfirmed=True))
            assert alt_doc["currency"] == "EUR"
            assert alt_doc["commercial"]["exchangeRate"]["rate"] == 1.15
            assert alt_doc["commercial"]["vat"] == 0
            assert alt_doc["amount"] == request(f"/api/projects/{alternate['id']}/commercial-preview")["preview"]["billingNet"]
            seed_path = Path(env["SEED_PATH"]) / "projects.json"
            original_seed = seed_path.read_text()
            edited_seed = json.loads(original_seed)
            next(x for x in edited_seed if x["id"] == alternate["id"])["email"] = "changed@example.test"
            seed_path.write_text(json.dumps(edited_seed))
            request(f"/api/pbd/{alt_doc['id']}/approve", {"actor": alternate["requestor"], "confirm": True, "stateHash": alt_doc["stateHash"], "version": alt_doc["version"]}, expected=409)
            seed_path.write_text(original_seed)
            adjustment = {"lineId": snapshot["lines"][0]["id"], "action": "WRITE_OFF",
                          "actor": p["requestor"], "reason": "Synthetic partial write-off",
                          "confirm": True, "amount": 1, "eventId": "smoke-adjustment"}
            request(f"/api/projects/{p['id']}/adjustments", dict(adjustment, actor="unrelated-user"), expected=403)
            request(f"/api/projects/{p['id']}/adjustments", adjustment)
            request(f"/api/projects/{p['id']}/adjustments", adjustment)
            updated = request(f"/api/projects/{p['id']}/wip")
            assert updated["total"] == snapshot["total"] - 1
            assert updated["position"]["writeOff"] == 1
            audit = request(f"/api/projects/{p['id']}/adjustments")
            assert len(audit) == 1
            assert audit[0]["eventId"] == adjustment["eventId"]
            assert audit[0]["before"]["bill"] - audit[0]["after"]["bill"] == 1
            assert audit[0]["reason"] == adjustment["reason"]
            assert audit[0]["actor"] == p["requestor"]
            assert audit[0]["provenance"].startswith("Immutable")
            source_path = Path(env["SEED_PATH"]) / "wip-lines.json"
            original_source = source_path.read_text()
            changed_source = json.loads(original_source)
            changed_line = next(x for x in changed_source if x["id"] == adjustment["lineId"])
            changed_line["rate"] += 1
            changed_line["amount"] += 1
            source_path.write_text(json.dumps(changed_source))
            assert request(f"/api/projects/{p['id']}/adjustments")[0] == audit[0]
            source_path.write_text(original_source)
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
            bulk_project = next(x for x in projects if x['id'] != p['id'] and x['id'] != alternate['id'])
            bulk_path = f"/api/projects/{bulk_project['id']}"
            bulk_before = request(bulk_path+'/wip')
            selections = [{'lineId': x['lineId'], 'amount': 1} for x in bulk_before['position']['lines'] if x['bill'] >= 1][:2]
            assert len(selections) == 2
            batch = dict(batchId='94876cd5-d377-4d43-a7b1-d0c73808172d', action='POSTPONE', actor=bulk_project['requestor'], reason='Synthetic batch review', confirm=True, stateHash=bulk_before['financialHash'], items=selections)
            request(bulk_path+'/adjustments/bulk', dict(batch, actor='unrelated-user'), expected=403)
            request(bulk_path+'/adjustments/bulk', dict(batch, confirm=False), expected=400)
            request(bulk_path+'/adjustments/bulk', dict(batch, stateHash='stale'), expected=409)
            request(bulk_path+'/adjustments/bulk', dict(batch, items=[selections[0], dict(selections[1], amount=100000000)]), expected=400)
            assert request(bulk_path+'/wip')['total'] == bulk_before['total']
            assert request(bulk_path+'/adjustments') == []
            assert not request(bulk_path+'/adjustments/bulk', batch)['replayed']
            assert request(bulk_path+'/adjustments/bulk', batch)['replayed']
            request(bulk_path+'/adjustments/bulk', dict(batch, items=selections[:1]), expected=400)
            bulk_after = request(bulk_path+'/wip')
            assert bulk_after['total'] == bulk_before['total'] - 2
            batch_audit = request(bulk_path+'/adjustments')
            assert len(batch_audit) == 2
            assert all(x['before']['bill']-x['after']['bill'] == 1 for x in batch_audit)
            assert all(x['reason'] == batch['reason'] and x['actor'] == batch['actor'] for x in batch_audit)
            print('PASS: atomic bulk adjustments, stale state, authorisation, retry and audit')
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
                    assert request(bulk_path+'/adjustments/bulk', batch)['replayed']
                    assert request(bulk_path+'/wip')['total'] == bulk_after['total']
                    assert request(bulk_path+'/adjustments') == batch_audit
                    restored = request(f"/api/projects/{p['id']}/wip")
                    assert restored["total"] == snapshot["total"] - 2
                    restored_audit = request(f"/api/projects/{p['id']}/adjustments")
                    assert len(restored_audit) == 2
                    assert restored_audit[0] == audit[0]
                    request(f"/api/projects/{p['id']}/adjustments", adjustment)
                    assert request(f"/api/projects/{p['id']}/wip")["total"] == restored["total"]
                    same_doc = request(f"/api/projects/{p['id']}/pbd", body)
                    assert same_doc["id"] == doc["id"]
                    same_post = request(f"/api/pbd/{doc['id']}/post", {"actor": p["requestor"], "confirm": True})
                    assert same_post["mockSapPostingId"] == posted["mockSapPostingId"]
                    request(f"/api/pbd/{fresh['id']}/post", {"actor": p["requestor"], "confirm": True}, expected=409)
                    threshold = 20000 if p["unit"] == "Advisory" else 10000
                    remaining = threshold - restored["position"]["writeOff"]
                    assert remaining > 0
                    # Use a different line to avoid allocations already consumed above.
                    for line in snapshot["lines"][1:]:
                        source = line["quantity"] * line["rate"] if line["type"] == "Time" else line["amount"]
                        amount = min(remaining, source)
                        if amount > 0:
                            request(f"/api/projects/{p['id']}/adjustments", dict(adjustment, lineId=line["id"], amount=amount, eventId="finance-" + line["id"]))
                            remaining -= amount
                        if remaining == 0:
                            break
                    assert remaining == 0, "Finance fixture needs enough WIP"
                    at_boundary = request(f"/api/projects/{p['id']}/wip")
                    assert not at_boundary["financeRequired"]
                    request(f"/api/projects/{p['id']}/adjustments", dict(adjustment, eventId="finance-above", amount=0.01))
                    above = request(f"/api/projects/{p['id']}/wip")
                    assert above["financeRequired"]
                    finance_body = dict(body, idempotencyKey="finance-draft")
                    request(f"/api/projects/{p['id']}/pbd", finance_body, expected=400)
                    finance = {"actor": p["financeApprover"], "confirm": True, "stateHash": above["financialHash"]}
                    request(f"/api/projects/{p['id']}/finance-approval", dict(finance, actor=p["requestor"]), expected=403)
                    request(f"/api/projects/{p['id']}/finance-approval", dict(finance, stateHash="old"), expected=409)
                    request(f"/api/projects/{p['id']}/finance-approval", finance)
                    request(f"/api/projects/{p['id']}/pbd", finance_body)
                    request(f"/api/projects/{p['id']}/adjustments", dict(adjustment, eventId="finance-stale", amount=0.01))
                    request(f"/api/projects/{p['id']}/pbd", dict(finance_body, idempotencyKey="finance-new"), expected=400)
                    before_writeup = request(f"/api/projects/{p['id']}/wip")
                    writeup = {"eventId": "writeup-22000", "amount": 22000, "actor": p["requestor"], "reason": "Synthetic agreed commercial uplift", "confirm": True}
                    request(f"/api/projects/{p['id']}/write-ups", dict(writeup, actor="unrelated-user"), expected=403)
                    request(f"/api/projects/{p['id']}/write-ups", dict(writeup, amount=-1), expected=400)
                    request(f"/api/projects/{p['id']}/write-ups", writeup)
                    request(f"/api/projects/{p['id']}/write-ups", writeup)
                    request(f"/api/projects/{p['id']}/write-ups", dict(writeup, amount=23000), expected=400)
                    uplifted = request(f"/api/projects/{p['id']}/wip")
                    assert Decimal(str(uplifted["total"])) == Decimal(str(before_writeup["total"])) + Decimal("22000")
                    assert uplifted["position"]["writeUp"] == 22000
                    assert abs(uplifted["position"]["revenueImpact"] - (22000 - uplifted["position"]["writeOff"])) < 0.00001
                    request(f"/api/projects/{p['id']}/pbd", dict(body, idempotencyKey="writeup-draft"), expected=400)
                    request(f"/api/projects/{p['id']}/finance-approval", dict(finance, stateHash=uplifted["financialHash"]))
                    uplift_draft = request(f"/api/projects/{p['id']}/pbd", dict(body, idempotencyKey="writeup-draft"))
                    assert uplift_draft["amount"] == uplifted["total"]
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
    for settings in [dict(AUTH_MODE="HostedDemo", DEMO_NETWORK_RESTRICTED="false", COMMAND_STORE_CONNECTION="required"), dict(AUTH_MODE="HostedDemo", DEMO_NETWORK_RESTRICTED="true", COMMAND_STORE_CONNECTION="")]:
        rejected = subprocess.run([dotnet, str(dll)], cwd=root, env=dict(env, **settings), capture_output=True, timeout=10)
        assert rejected.returncode != 0
        assert b"Hosted demo requires explicit restricted or public synthetic access and PostgreSQL persistence" in rejected.stderr
    print("PASS: hosted demo requires explicit access mode and persistent state")
    auth_env = dict(env, ASPNETCORE_ENVIRONMENT="Development", AUTH_MODE="Entra",
                    ENTRA_TENANT_ID="11111111-1111-1111-1111-111111111111", ENTRA_API_AUDIENCE="api://ledgerpilot-test")
    Path(auth_env["STATE_PATH"]).unlink()
    with subprocess.Popen([dotnet, str(dll)], cwd=root, env=auth_env,
                          stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL) as secured:
        try:
            deadline = time.monotonic() + 20
            while True:
                try:
                    urllib.request.urlopen(base + "/health", timeout=2).close()
                    break
                except OSError:
                    if secured.poll() is not None or time.monotonic() >= deadline:
                        raise RuntimeError("Entra-mode startup failed")
                    time.sleep(0.1)
            for path, body in [("/api/projects", None), ("/api/ai/explain", {"userMessage": "test", "projectId": p["id"]}),
                               (f"/api/projects/{p['id']}/pbd", {"actor": p["requestor"], "confirm": True})]:
                req = urllib.request.Request(base + path, data=None if body is None else json.dumps(body).encode(), headers={"Content-Type": "application/json"})
                try:
                    urllib.request.urlopen(req, timeout=5)
                    raise AssertionError("Anonymous Entra-mode access accepted")
                except urllib.error.HTTPError as error:
                    assert error.code == 401
            print("PASS: Entra mode rejects anonymous read, AI and write requests")
        finally:
            secured.terminate()
            secured.wait(timeout=5)
    missing_config = subprocess.run([dotnet, str(dll)], cwd=root,
        env=dict(auth_env, ENTRA_TENANT_ID="", ENTRA_API_AUDIENCE=""), capture_output=True, timeout=10)
    assert missing_config.returncode != 0
    print("PASS: missing Entra configuration rejected")
    shutil.rmtree(state_dir)


if __name__ == "__main__":
    run()
