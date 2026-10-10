#!/usr/bin/env python3
import json,time,urllib.request
base='http://localhost:8088'
for attempt in range(60):
    try:
        with urllib.request.urlopen(base+'/health',timeout=3) as response:assert json.load(response)['status']=='ok'
        break
    except OSError:time.sleep(1)
else:raise AssertionError('Production hosted demo did not start')
with urllib.request.urlopen(base,timeout=5) as response:
    assert 'id="root"' in response.read().decode()
with urllib.request.urlopen(base+'/api/projects',timeout=5) as response:
    assert len(json.load(response))==5
print('PASS: production-mode demo serves React and billing API without Entra configuration')
