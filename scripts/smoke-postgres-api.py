#!/usr/bin/env python3
"""Real PostgreSQL API acceptance + separate worker, with a file handoff instead of live Azure queue."""
import json, os, socket, subprocess, tempfile, time, urllib.request, urllib.error, shutil, hashlib
from pathlib import Path
root = Path(__file__).resolve().parents[1]
dotnet = os.environ.get('DOTNET_COMMAND', 'dotnet')
assert os.environ.get('COMMAND_STORE_CONNECTION'), 'Dedicated PostgreSQL connection required'
def port():
    with socket.socket() as s:
        s.bind(('127.0.0.1', 0)); return s.getsockname()[1]
def wait(url, process):
    for _ in range(200):
        try:
            urllib.request.urlopen(url, timeout=1).close(); return
        except OSError:
            assert process.poll() is None, 'service exited during startup'
            time.sleep(.1)
    raise AssertionError('service startup timeout')
with tempfile.TemporaryDirectory() as tmp:
    base = f'http://127.0.0.1:{port()}'
    sap = f'http://127.0.0.1:{port()}'
    env = dict(os.environ, AUTH_MODE='LocalDemo', ASPNETCORE_ENVIRONMENT='Development',
               ASPNETCORE_URLS=base, SEED_PATH=str(root/'seed'), STATE_PATH=str(Path(tmp)/'billing.json'),
               FOUNDRY_PROJECT_ENDPOINT='', FOUNDRY_MODEL='', COSMOS_ENDPOINT='', BLOB_CONTAINER='')
    dll = root/'src/LedgerPilot.Api/bin/Release/net10.0/LedgerPilot.Api.dll'
    def start():
        p = subprocess.Popen([dotnet,str(dll)],cwd=root,env=env,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
        wait(base+'/health',p); return p
    def request(path, body=None, expected=200):
        r=urllib.request.Request(base+path,data=None if body is None else json.dumps(body).encode(),headers={'Content-Type':'application/json'})
        try: response=urllib.request.urlopen(r,timeout=10)
        except urllib.error.HTTPError as e: response=e
        with response:
            raw=response.read(); assert response.status==expected,(path,response.status,raw)
            return json.loads(raw) if raw else None
    helpers=[]
    workflow_url=None
    def workflow_status():
        try:
            with urllib.request.urlopen(workflow_url,timeout=5) as response: return json.load(response)
        except urllib.error.HTTPError as e:
            if e.code==404:return None
            raise
    def wait_workflow(states):
        for _ in range(180):
            status=workflow_status()
            if status and status.get('runtimeStatus') in states:return status
            assert not status or status.get('runtimeStatus') not in ['Failed','Terminated'],status
            time.sleep(1)
        raise AssertionError('Durable instance did not reach expected state')
    api=start()
    try:
        projects=request('/api/projects')
        snapshot=next(x for x in (request('/api/projects/'+p['id']+'/wip') for p in projects)
                      if not any(f['severity']=='BLOCK' for f in x['findings']))
        p=snapshot['project']; actor=p['requestor']
        body=dict(actor=actor,confirm=True,narrative='PostgreSQL integration',idempotencyKey='pg-api-test',vatConfirmed=True,currencyConfirmed=True,
                  acknowledgements=[dict(findingId=f['id'],dependencyHash=f['dependencyHash']) for f in snapshot['findings'] if f['severity']=='CONFIRM'])
        draft=request('/api/projects/'+p['id']+'/pbd',body)
        path='/api/pbd/'+draft['id']
        request(path+'/post',dict(actor=actor,confirm=True),409)
        request(path+'/approve',dict(actor=actor,confirm=True,stateHash=draft['stateHash'],version=draft['version']))
        request(path+'/post',dict(actor='unrelated',confirm=True),403)
        pending=request(path+'/post',dict(actor=actor,confirm=True))
        assert pending['status']=='POST_PENDING' and pending['mockSapPostingId'] is None
        assert request(path+'/post',dict(actor=actor,confirm=True))==pending
        assert not Path(env['STATE_PATH']).exists(), 'PostgreSQL mode must not write JSON billing files'
        second=subprocess.run([dotnet,str(dll)],cwd=root,env=dict(env,ASPNETCORE_URLS=f'http://127.0.0.1:{port()}'),capture_output=True,timeout=15)
        assert second.returncode!=0, 'second API writer must fail'
        api.terminate();api.wait(timeout=5);api=start()
        assert request(path)==pending
        if os.environ.get('WORKFLOW_SMOKE')=='true':
            storage=subprocess.Popen(['azurite','--silent','--location',str(Path(tmp)/'azurite')],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
            helpers.append(storage)
            for _ in range(100):
                try:
                    with socket.create_connection(('127.0.0.1',10000),timeout=1):break
                except OSError:
                    assert storage.poll() is None,'Azurite failed';time.sleep(.1)
            workflow_root=Path(tmp)/'workflows'
            shutil.copytree(root/'src/LedgerPilot.Workflows/bin/Release/net10.0',workflow_root)
            (workflow_root/'local.settings.json').write_text(json.dumps({'IsEncrypted':False,'Values':{
                'FUNCTIONS_WORKER_RUNTIME':'dotnet-isolated','AZURE_FUNCTIONS_ENVIRONMENT':'Development',
                'AzureWebJobsStorage':'UseDevelopmentStorage=true','COMMAND_STORE_CONNECTION':os.environ['COMMAND_STORE_CONNECTION']}}))
            workflow_base=f'http://127.0.0.1:{port()}'
            workflow_port=workflow_base.rsplit(':',1)[1]
            workflow_log=open(Path(tmp)/'workflow-host.log','w+')
            def start_host():
                host=subprocess.Popen(['func','start','--port',workflow_port],cwd=workflow_root,env=dict(os.environ),stdout=workflow_log,stderr=subprocess.STDOUT)
                helpers.append(host)
                try:wait(workflow_base+'/admin/host/status',host)
                except Exception:
                    workflow_log.flush();workflow_log.seek(0);print(workflow_log.read()[-12000:]);raise
                return host
            host=start_host()
            instance='posting-'+hashlib.sha256(('post:'+draft['id']).encode()).hexdigest()
            workflow_url=workflow_base+'/runtime/webhooks/durabletask/instances/'+instance+'?taskHub=LedgerPilotPosting'
            wait_workflow(['Running'])
            host.terminate();host.wait(timeout=15)
            host=start_host()
            wait_workflow(['Running'])
        spool=Path(tmp)/'spool';spool.mkdir()
        # Same canonical command generated by API; worker verifies its accepted database record.
        command=dict(Operation='CreateAndPost',CommandKey='post:'+draft['id'],ProjectId=draft['projectId'],Net=draft['amount'],Currency=draft['currency'],StateHash=draft['stateHash'],DocumentId=None)
        (spool/'post.command.json').write_text(json.dumps(command))
        sap_env=dict(env,ASPNETCORE_URLS=sap,SAP_STATE_PATH=str(Path(tmp)/'sap.json'))
        simulator=subprocess.Popen([dotnet,str(root/'src/LedgerPilot.SapSimulator/bin/Release/net10.0/LedgerPilot.SapSimulator.dll')],env=sap_env,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
        try:
            wait(sap+'/health',simulator)
            worker_env=dict(env,DOTNET_ENVIRONMENT='Development',SAP_TRANSPORT='LocalFiles',SAP_COMMAND_PATH=str(spool),SAP_SIMULATOR_URL=sap,SIMULATE_RESPONSE_LOSS='true')
            worker=root/'src/LedgerPilot.SapWorker/bin/Release/net10.0/LedgerPilot.SapWorker.dll'
            # Create committed/response lost, then post committed/response lost, then recover receipt.
            for expected in [1,1,0]:
                result=subprocess.run([dotnet,str(worker)],env=worker_env,capture_output=True,timeout=20)
                assert result.returncode==expected,result.stderr
            if workflow_url:
                workflow=wait_workflow(['Completed'])
                outcome=workflow['output']
                assert outcome.get('status',outcome.get('Status'))=='Posted',outcome
                assert outcome.get('postingReference',outcome.get('PostingReference')),outcome
                print('PASS: real Durable Functions host persisted/replayed pending workflow and observed SAP receipt')
            posted=request(path)
            assert posted['status']=='POSTED' and posted['mockSapPostingId']
            assert request(path+'/post',dict(actor=actor,confirm=True))==posted
            api.terminate();api.wait(timeout=5);api=start()
            assert request(path)==posted
            if workflow_url:
                # Complete another posting while the timer host is stopped. Receipt
                # arrival must not prevent discovery after the host restarts.
                host.terminate();host.wait(timeout=15)
                late=request('/api/projects/'+p['id']+'/pbd',dict(body,idempotencyKey='pg-late-workflow',narrative='Receipt before timer'))
                late_path='/api/pbd/'+late['id']
                request(late_path+'/approve',dict(actor=actor,confirm=True,stateHash=late['stateHash'],version=late['version']))
                request(late_path+'/post',dict(actor=actor,confirm=True))
                late_command=dict(Operation='CreateAndPost',CommandKey='post:'+late['id'],ProjectId=late['projectId'],Net=late['amount'],Currency=late['currency'],StateHash=late['stateHash'],DocumentId=None)
                (spool/'post.command.json').write_text(json.dumps(late_command))
                result=subprocess.run([dotnet,str(worker)],env=dict(worker_env,SIMULATE_RESPONSE_LOSS='false'),capture_output=True,timeout=20)
                assert result.returncode==0,result.stderr
                assert request(late_path)['status']=='POSTED'
                instance='posting-'+hashlib.sha256(('post:'+late['id']).encode()).hexdigest()
                workflow_url=workflow_base+'/runtime/webhooks/durabletask/instances/'+instance+'?taskHub=LedgerPilotPosting'
                host=start_host()
                outcome=wait_workflow(['Completed'])['output']
                assert outcome.get('status',outcome.get('Status'))=='Posted',outcome
                print('PASS: posting completed before timer was discovered and tracked after host restart')
            print('PASS: PostgreSQL API accepted posting, restart, separate worker lost-response recovery and durable SAP receipt')
        finally:
            simulator.terminate();simulator.wait(timeout=5)
    finally:
        api.terminate();api.wait(timeout=5)
        for helper in reversed(helpers):
            if helper.poll() is None:
                helper.terminate();helper.wait(timeout=15)
