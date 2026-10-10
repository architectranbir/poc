#!/usr/bin/env python3
"""Generate credentials for the localhost-only synthetic Functions container."""
import json,secrets
from pathlib import Path
folder=Path('.container-secrets');folder.mkdir(exist_ok=True,mode=0o700)
path=folder/'host.json'
if not path.exists():
    path.write_text(json.dumps({'masterKey':{'name':'master','value':secrets.token_urlsafe(48),'encrypted':False},'functionKeys':[],'systemKeys':[]}))
    path.chmod(0o600)
