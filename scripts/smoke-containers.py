#!/usr/bin/env python3
import hashlib,json,subprocess,time,urllib.request,urllib.error
base='http://127.0.0.1:8080'
from pathlib import Path
function_headers={'x-functions-key':json.loads(Path('.container-secrets/host.json').read_text())['masterKey']['value']}
def request(path,body=None):
    r=urllib.request.Request(base+path,data=None if body is None else json.dumps(body).encode(),headers={'Content-Type':'application/json'})
    with urllib.request.urlopen(r,timeout=10) as response:return json.load(response)
def wait_url(url):
    for _ in range(180):
        try:
            with urllib.request.urlopen(urllib.request.Request(url,headers=function_headers if ':7071/' in url else {}),timeout=3) as response:
                body=response.read()
                if '/admin/host/status' not in url or json.loads(body).get('state')=='Running':return body
        except OSError:time.sleep(1)
    raise AssertionError('Container did not become ready: '+url)
wait_url(base+'/health')
wait_url('http://127.0.0.1:5173')
wait_url('http://127.0.0.1:7071/admin/host/status')
projects=request('/api/projects')
snapshot=next(x for x in (request('/api/projects/'+p['id']+'/wip') for p in projects) if not any(f['severity']=='BLOCK' for f in x['findings']))
p=snapshot['project'];actor=p['requestor']
body=dict(actor=actor,confirm=True,narrative='Container end-to-end',idempotencyKey='containers',vatConfirmed=True,currencyConfirmed=True,
          acknowledgements=[dict(findingId=f['id'],dependencyHash=f['dependencyHash']) for f in snapshot['findings'] if f['severity']=='CONFIRM'])
draft=request('/api/projects/'+p['id']+'/pbd',body);path='/api/pbd/'+draft['id']
request(path+'/approve',dict(actor=actor,confirm=True,stateHash=draft['stateHash'],version=draft['version']))
assert request(path+'/post',dict(actor=actor,confirm=True))['status']=='POST_PENDING'
instance='posting-'+hashlib.sha256(('post:'+draft['id']).encode()).hexdigest()
url='http://127.0.0.1:7071/runtime/webhooks/durabletask/instances/'+instance+'?taskHub=LedgerPilotPosting'
def workflow_status():
    try:
        with urllib.request.urlopen(urllib.request.Request(url,headers=function_headers),timeout=5) as r:return json.load(r)
    except urllib.error.HTTPError as e:
        if e.code in (404,500,503):return None
        raise
for _ in range(120):
    status=workflow_status()
    if status and status['runtimeStatus']=='Running':break
    time.sleep(1)
else:raise AssertionError('Container Durable workflow did not start')
subprocess.run(['docker','compose','up','-d','worker'],check=True)
for _ in range(90):
    posted=request(path)
    if posted['status']=='POSTED':break
    time.sleep(1)
else:raise AssertionError('Container worker did not post')
assert posted['mockSapPostingId']
for _ in range(90):
    status=workflow_status()
    if status and status['runtimeStatus']=='Completed':break
    assert not status or status['runtimeStatus']!='Failed',status
    time.sleep(1)
else:raise AssertionError('Container Durable workflow did not complete')
assert request(path+'/post',dict(actor=actor,confirm=True))==posted
subprocess.run(['docker','compose','restart','api','worker'],check=True)
wait_url(base+'/health');assert request(path)==posted
print('PASS: packaged React server, API, PostgreSQL, SAP worker/simulator and Durable containers; posting and restart recovery')
