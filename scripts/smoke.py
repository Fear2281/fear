"""End-to-end API checks. Runs an isolated server and uses temporary data only."""
import http.cookiejar
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import time
import urllib.error
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
BASE = 'http://127.0.0.1:5081'
DOTNET = os.environ.get('DOTNET') or shutil.which('dotnet') or '/workspace/.dotnet/dotnet'
DLL = ROOT / 'bin/Debug/net8.0/Encore.dll'
passed = 0

class Client:
    def __init__(self):
        self.opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
    def request(self, path, method='GET', body=None, expected=200, headers=None):
        global passed
        req = urllib.request.Request(BASE+path, method=method,
            data=json.dumps(body).encode() if body is not None else None,
            headers={'Content-Type':'application/json', **(headers or {})})
        try:
            with self.opener.open(req, timeout=5) as response:
                code, raw = response.status, response.read()
        except urllib.error.HTTPError as response:
            code, raw = response.code, response.read()
        assert code == expected, f'{method} {path}: expected {expected}, got {code}: {raw[:200]!r}'
        passed += 1
        try: return json.loads(raw)
        except (ValueError, UnicodeDecodeError): return raw

with tempfile.TemporaryDirectory(prefix='encore-smoke-') as directory:
    env = {**os.environ,'ASPNETCORE_ENVIRONMENT':'Development','ENCORE_DATA_DIR':directory,
           'ENCORE_DJ_PASSWORD':'test-only-password','DOTNET_CLI_HOME':directory}
    log = open(Path(directory)/'server.log','w')
    process = None
    def start():
        global process
        process = subprocess.Popen([DOTNET,str(DLL),'--urls',BASE],cwd=ROOT,env=env,stdout=log,stderr=subprocess.STDOUT)
        for _ in range(100):
            if process.poll() is not None: raise RuntimeError('Server exited during startup')
            try:
                with urllib.request.urlopen(BASE+'/api/health',timeout=1) as r:
                    if r.status == 200: return
            except (urllib.error.URLError,TimeoutError): time.sleep(.1)
        raise RuntimeError('Server startup timeout')
    def stop():
        if process and process.poll() is None:
            process.terminate()
            try: process.wait(timeout=5)
            except subprocess.TimeoutExpired: process.kill(); process.wait()
    try:
        start()
        guest, other, dj = Client(), Client(), Client()
        assert guest.request('/api/health')['modules'] == 2
        guest.request('/api/dj/state',expected=401)
        dj.request('/api/dj/login','POST',{'password':'wrong'},401)
        dj.request('/api/dj/login','POST',{'password':'test-only-password'})
        tables = dj.request('/api/dj/tables')
        assert len(tables) == 8 and len({t['token'] for t in tables}) == 8
        guest.request('/table/invalid',expected=404)
        guest.request('/api/guest/demo','POST')
        assert guest.request('/api/guest/session')['table'] == 1
        other.request('/table/'+tables[1]['token'])
        assert other.request('/api/guest/session')['table'] == 2
        assert len(guest.request('/api/catalog?query=sinatra')) == 1
        assert all(t['genre'] == 'Jazz' for t in guest.request('/api/catalog?genre=Jazz'))
        assert guest.request('/api/catalog?query=explicit') == []
        first = guest.request('/api/orders','POST',{'trackId':1})['id']
        guest.request('/api/orders','POST',{'trackId':1},409)
        second = guest.request('/api/orders','POST',{'trackId':2})['id']
        guest.request('/api/orders','POST',{'trackId':8},409)
        guest.request('/api/orders','POST',{'trackId':999},404)
        third = other.request('/api/orders','POST',{'trackId':4})['id']
        guest.request('/api/orders/'+first+'/vote','POST',expected=409)
        guest.request('/api/orders/'+third+'/vote','POST')
        other.request('/api/orders/'+first+'/vote','POST')
        # The eleventh guest write from this IP must be rejected.
        guest.request('/api/orders/'+third+'/vote','POST',expected=429)
        queue = guest.request('/api/queue')['queue']
        assert len(queue)==3 and queue[0]['votes']==1
        assert queue[1]['waitSeconds']==queue[0]['track']['duration']
        guest.request('/api/dj/orders/'+first+'/approve','POST',expected=401)
        dj.request('/api/dj/orders/'+first+'/play','POST',expected=409)
        dj.request('/api/dj/orders/'+first+'/approve','POST')
        dj.request('/api/dj/orders/'+first+'/play','POST')
        dj.request('/api/dj/orders/'+second+'/approve','POST')
        dj.request('/api/dj/orders/'+second+'/play','POST',expected=409)
        assert dj.request('/api/dj/state')['playing']['id']==first
        assert b'<svg' in dj.request('/api/dj/tables/1/qr')
        dj.request('/api/dj/tables/999/qr',expected=404)
        dj.request('/api/dj/settings','PUT',{'tableLimit':0,'maxDuration':360,'mood':'Вечерний джаз'},400)
        settings={'tableLimit':2,'maxDuration':360,'mood':'Тихий ужин','accepting':False,'allowExplicit':True}
        dj.request('/api/dj/settings','PUT',settings)
        assert len(guest.request('/api/catalog?query=explicit'))==1
        dj.request('/api/dj/settings','PUT',settings,403,{'Origin':'https://other.example'})
        csv=dj.request('/api/dj/export')
        assert csv.startswith(b'\xef\xbb\xbf') and b'Frank Sinatra' in csv
        # Restart also verifies persistence and resets IP limiter for the next checks.
        stop(); start()
        guest,other,dj=Client(),Client(),Client()
        dj.request('/api/dj/login','POST',{'password':'test-only-password'})
        restored=dj.request('/api/dj/state')
        assert restored['stats']['total']==3 and restored['settings']['mood']=='Тихий ужин'
        guest.request('/table/'+tables[0]['token'])
        other.request('/table/'+tables[1]['token'])
        guest.request('/api/orders','POST',{'trackId':8},409)
        guest.request('/api/orders/'+third+'/vote','POST',expected=409)
        dj.request('/api/dj/orders/'+first+'/complete','POST')
        dj.request('/api/dj/orders/'+first+'/play','POST',expected=409)
        dj.request('/api/dj/orders/'+second+'/skip','POST')
        dj.request('/api/dj/orders/'+third+'/reject','POST')
        snapshot=dj.request('/api/dj/state')
        assert snapshot['queue']==[] and snapshot['playing'] is None
        assert snapshot['stats']['completed']==1 and len(snapshot['history'])==3
        settings['accepting']=True
        settings['allowExplicit']=False
        settings['maxDuration']=120
        dj.request('/api/dj/settings','PUT',settings)
        assert guest.request('/api/catalog')==[]
        guest.request('/api/orders','POST',{'trackId':1},409)
        dj.request('/api/dj/logout','POST')
        dj.request('/api/dj/state',expected=401)
        print(f'PASS: {passed} HTTP checks; moderation, voting, limits, access control, QR, CSV and restart persistence verified.')
    finally:
        stop(); log.close()
