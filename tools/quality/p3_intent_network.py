#!/usr/bin/env python3
"""Isolated published server and actual client DLL journal/query recovery, synthetic identities only."""
import argparse,base64,hashlib,json,re,socket,sqlite3,subprocess,tempfile,time,xml.etree.ElementTree as ET
from pathlib import Path

def local(e):return e.tag.rsplit('}',1)[-1]
def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
 p=argparse.ArgumentParser(description=__doc__)
 for name in ['server','config-writer','probe','client','output']:p.add_argument('--'+name,type=Path,required=True)
 a=p.parse_args();out=a.output.resolve();out.mkdir(parents=True,exist_ok=False)
 result={'passed':False,'profile':'Framework v2 synthetic peers + actual client intent/query components; not RimWorld','faultIds':['client.intent.response-not-applied','server.kill-after-journaled-offer'],'phases':[],'inputs':{k:{'path':str(getattr(a,k).resolve()),'sha256':digest(getattr(a,k))} for k in ['server','config_writer','probe','client']}}
 try:
  with tempfile.TemporaryDirectory(prefix='phinix-p3-intent-joint-') as tmp:
   work=Path(tmp);state=work/'synthetic-private.json';journal=work/'intent-journal'
   subprocess.run(['dotnet',str(a.config_writer.resolve()),'--write-config',str(work/'server.conf')],check=True,capture_output=True,timeout=10)
   with socket.socket(socket.AF_INET,socket.SOCK_DGRAM) as reservation:
    reservation.bind(('127.0.0.1',0));port=reservation.getsockname()[1]
   config=ET.parse(work/'server.conf');next(e for e in config.getroot().iter() if local(e)=='Port').text=str(port);config.write(work/'server.conf',encoding='utf-8',xml_declaration=True)
   for phase in ['initial','recover','verified']:
    log_path=out/(phase+'-server.txt')
    with log_path.open('w') as log:
     host=subprocess.Popen(['dotnet',str(a.server.resolve())],cwd=work,stdin=subprocess.PIPE,stdout=log,stderr=subprocess.STDOUT,text=True)
     try:
      deadline=time.monotonic()+12
      while not re.search(r'listening on 0\.0\.0\.0:'+str(port),log_path.read_text()):
       if host.poll() is not None or time.monotonic()>deadline:raise RuntimeError('Startup deadline')
       time.sleep(.05)
      command=['dotnet',str(a.probe.resolve()),'--intent-probe',str(port),phase,str(state),str(a.client.resolve()),str(journal)]
      with tempfile.TemporaryFile() as capture:
       peer=subprocess.Popen(command,cwd=work,stdout=capture,stderr=subprocess.STDOUT)
       try:peer.wait(timeout=45)
       except BaseException:peer.kill();peer.wait();raise
       capture.seek(0);text=capture.read().decode(errors='replace')
      (out/(phase+'-client.txt')).write_text(text)
      assert peer.returncode==0 and 'CLIENT INTENT NETWORK RESULT passed;' in text
      private=json.loads(state.read_text());database,=(work/'framework-extensions').rglob('trade-state.bin.sqlite3')
      with sqlite3.connect(database.as_uri()+'?mode=ro',uri=True) as db:
       assert db.execute('PRAGMA integrity_check').fetchone()==('ok',)
       assert db.execute('SELECT count(*) FROM operations WHERE principal=?',(private['Principals'][0],)).fetchone()==(2,)
       ledger=ET.fromstring(db.execute("SELECT value FROM state WHERE id='trade-ledger-v1'").fetchone()[0]);active=next(e for e in ledger if local(e)=='ActiveTrades')
       assert len(active)==1 and next(e for e in active[0] if local(e)=='TradeId').text==private['Trade']
       assert next(e for e in active[0] if local(e)=='SnapshotVersion').text=='2'
       payloads=[e for e in active[0].iter() if local(e)=='PayloadBytes'];assert len(payloads)==1 and base64.b64decode(payloads[0].text)==bytes([17,0,255])
       for intent_id in private['IntentIds']:assert db.execute('SELECT count(*) FROM operations WHERE principal=? AND operation=?',(private['Principals'][0],intent_id)).fetchone()==(1,)
       assert db.execute("SELECT count(*) FROM operations WHERE operation='never-sent'").fetchone()==(0,)
      assert all(k not in text and k not in log_path.read_text() for k in private['Keys'])
      if phase=='initial':host.kill();host.wait(timeout=5)
      else:
       host.stdin.write('stop\n');host.stdin.flush();host.wait(timeout=10);assert host.returncode==0
      result['phases'].append({'phase':phase,'passed':True,'serverExitCode':host.returncode,'clientExitCode':peer.returncode,'committedUserOperations':2,'queryWrites':0,'clientDurableResults':len(list(journal.glob('*.query')))})
     finally:
      if host.poll() is None:host.kill();host.wait(timeout=5)
      host.stdin.close()
  result['passed']=True;print('CLIENT P3 JOINT RESULT passed; real SQLite/UDP/actual client DLL/restart/original result/no replay; no game save claim');return 0
 except Exception as e:result['errorType']=type(e).__name__;print('CLIENT P3 JOINT RESULT failed; '+type(e).__name__);return 1
 finally:
  result['outputs']={f.name:digest(f) for f in out.glob('*.txt')};(out/'manifest.json').write_text(json.dumps(result,indent=2)+'\n')
if __name__=='__main__':raise SystemExit(main())
