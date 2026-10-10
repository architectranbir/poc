#!/usr/bin/env python3
"""HTTP contracts and restart recovery against the separate simulator process."""
import concurrent.futures
import json
import os
from pathlib import Path
import socket
import subprocess
import tempfile
import time
import urllib.error
import urllib.request

root = Path(__file__).resolve().parents[1]
dotnet = os.environ.get('DOTNET_COMMAND', 'dotnet')
dll = root / 'src/LedgerPilot.SapSimulator/bin/Release/net10.0/LedgerPilot.SapSimulator.dll'
with tempfile.TemporaryDirectory(prefix='ledgerpilot-sap-') as state:
    with socket.socket() as sock:
        sock.bind(('127.0.0.1', 0))
        port = sock.getsockname()[1]
    base = f'http://127.0.0.1:{port}'
    env = dict(os.environ, ASPNETCORE_ENVIRONMENT='Development', ASPNETCORE_URLS=base, SAP_STATE_PATH=str(Path(state) / 'sap.json'))
    def request(path, body=None, expected=200):
        req = urllib.request.Request(base + path, data=None if body is None else json.dumps(body).encode(), headers={'Content-Type': 'application/json'})
        try:
            response = urllib.request.urlopen(req, timeout=5)
        except urllib.error.HTTPError as error:
            response = error
        with response:
            assert response.status == expected, (path, response.status, response.read())
            raw = response.read()
            return json.loads(raw) if raw else None
    created = None
    posted = None
    command = {'commandKey': 'create-one', 'projectId': 'SYNTHETIC-1', 'net': 123.45, 'currency': 'GBP', 'stateHash': 'approved-state'}
    for restart in range(2):
        with subprocess.Popen([dotnet, str(dll)], cwd=root, env=env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL) as api:
            try:
                deadline = time.monotonic() + 20
                while True:
                    try:
                        request('/health')
                        break
                    except OSError:
                        if api.poll() is not None or time.monotonic() >= deadline:
                            raise RuntimeError('Simulator startup failed')
                        time.sleep(.1)
                with concurrent.futures.ThreadPoolExecutor(max_workers=8) as pool:
                    replies = list(pool.map(lambda _: request('/documents', command), range(8)))
                assert len({x['id'] for x in replies}) == 1
                current = replies[0]
                if created is not None:
                    assert current['id'] == created['id']
                created = current
                assert request('/commands/create-one')['id'] == created['id']
                request('/documents', dict(command, net=1), expected=409)
                request('/documents', dict(command, commandKey='negative', net=-1), expected=400)
                request('/documents/not-found', expected=404)
                post_path = f"/documents/{created['id']}/post"
                request(post_path, {'commandKey': 'post-one', 'stateHash': 'stale'}, expected=409)
                with concurrent.futures.ThreadPoolExecutor(max_workers=8) as pool:
                    replies = list(pool.map(lambda _: request(post_path, {'commandKey': 'post-one', 'stateHash': command['stateHash']}), range(8)))
                assert len({x['postingReference'] for x in replies}) == 1
                assert replies[0]['status'] == 'POSTED'
                if posted is not None:
                    assert posted['postingReference'] == replies[0]['postingReference']
                posted = replies[0]
                request(post_path, {'commandKey': 'different-post', 'stateHash': command['stateHash']}, expected=409)
                if restart == 0:
                    spool = Path(state) / 'commands'
                    spool.mkdir()
                    worker_dll = root / 'src/LedgerPilot.SapWorker/bin/Release/net10.0/LedgerPilot.SapWorker.dll'
                    worker_env = dict(env, DOTNET_ENVIRONMENT='Development', SAP_SIMULATOR_URL=base, SAP_COMMAND_PATH=str(spool), SIMULATE_RESPONSE_LOSS='true')
                    create_path = spool / '01.command.json'
                    worker_create = {'operation': 'Create', 'commandKey': 'worker-create', 'projectId': 'WORKER-1', 'net': 900, 'currency': 'GBP', 'stateHash': 'worker-state'}
                    create_path.write_text(json.dumps(worker_create))
                    def worker(expected):
                        result = subprocess.run([dotnet, str(worker_dll)], env=worker_env, capture_output=True, timeout=20)
                        assert result.returncode == expected, result.stderr
                    worker(1)  # Source committed but returned a simulated lost response.
                    assert not Path(str(create_path) + '.result.json').exists()
                    source_doc = request('/commands/worker-create')
                    worker(0)  # Fresh process reconciles instead of creating another document.
                    create_result_path = Path(str(create_path) + '.result.json')
                    receipt = json.loads(create_result_path.read_text())
                    assert receipt['Response']['id'] == source_doc['id']
                    post_command_path = spool / '02.command.json'
                    post_command_path.write_text(json.dumps(dict(worker_create, operation='Post', commandKey='worker-post', documentId=source_doc['id'])))
                    worker(1)
                    posted_source = request(f"/documents/{source_doc['id']}")
                    assert posted_source['status'] == 'POSTED'
                    worker(0)
                    post_receipt_path = Path(str(post_command_path) + '.result.json')
                    post_receipt = post_receipt_path.read_text()
                    assert json.loads(post_receipt)['Response']['postingReference'] == posted_source['postingReference']
                    worker(0)
                    assert post_receipt_path.read_text() == post_receipt
                    print('PASS: worker lost-response reconciliation and durable receipts')
            finally:
                api.terminate()
                api.wait(timeout=5)
    print('PASS: separate simulator concurrent create/post, conflict checks and restart reconciliation')
