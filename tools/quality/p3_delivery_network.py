#!/usr/bin/env python3
"""Actual client receipt components + isolated server/UDP/SQLite; synthetic checkpoint, no game proof."""
import argparse
import base64
import hashlib
import json
from pathlib import Path
import re
import socket
import sqlite3
import subprocess
import tempfile
import time
import xml.etree.ElementTree as ET


def local(e): return e.tag.rsplit('}', 1)[-1]
def digest(p): return hashlib.sha256(p.read_bytes()).hexdigest()


def oracle(database, phase):
    with sqlite3.connect(database.as_uri()+'?mode=ro', uri=True) as db:
        assert db.execute('PRAGMA integrity_check').fetchone() == ('ok',)
        ledger = ET.fromstring(db.execute("SELECT value FROM state WHERE id='trade-ledger-v1'").fetchone()[0])
        assert len(next(e for e in ledger if local(e) == 'ActiveTrades')) == 0
        archives = db.execute("SELECT value FROM state WHERE id LIKE 'trade.archive.%'").fetchall()
        if phase == 'initial':
            assert not archives
            rows = next(e for e in ledger if local(e) == 'PendingCompletionNotifications')
            assert sorted(next(e for e in n if local(e) == 'DeliveryState').text for n in rows) == ['Confirmed', 'OutcomeUnknown']
            assert sorted(next(e for e in n if local(e) == 'ReceiptKind').text for n in rows) == ['InboxStored', 'InboxStored']
            evidence = ledger
        else:
            assert len(archives) == 1
            evidence = ET.fromstring(archives[0][0])
            rows = next(e for e in evidence if local(e) == 'Deliveries')
            assert len(rows) == 2 and all(next(e for e in n if local(e) == 'DeliveryState').text == 'Confirmed' for n in rows)
            assert sorted(next(e for e in n if local(e) == 'ReceiptKind').text for n in rows) == ['InboxStored', 'SavedInventory']
        payloads = [base64.b64decode(e.text) for e in evidence.iter() if local(e) == 'PayloadBytes' and e.text]
        assert payloads and all(value == bytes([17, 0, 255]) for value in payloads)
        return db.execute('SELECT count(*) FROM operations').fetchone()[0]


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--outcome', choices=['cancelled', 'completed'], default='cancelled')
    for name in ['server', 'config-writer', 'probe', 'client', 'output']:
        p.add_argument('--'+name, type=Path, required=True)
    a = p.parse_args()
    out = a.output.resolve(); out.mkdir(parents=True, exist_ok=False)
    result = {'passed': False, 'profile': 'FrameworkV2/DurableInboxV1', 'outcome': a.outcome, 'syntheticCheckpointOnly': True,
              'faultIds': ['client.receipt.response-not-applied', 'server.kill-after-inbox-receipt', 'client.saved-receipt.response-not-applied'],
              'seed': None, 'phases': [], 'inputs': {k: {'path': str(getattr(a, k).resolve()), 'sha256': digest(getattr(a, k))} for k in ['server', 'config_writer', 'probe', 'client']}}
    try:
        with tempfile.TemporaryDirectory(prefix='phinix-p3-delivery-joint-') as tmp:
            work = Path(tmp); state = work/'synthetic-private.json'; inbox = work/'inbox'
            subprocess.run(['dotnet', str(a.config_writer.resolve()), '--write-config', str(work/'server.conf')], check=True, capture_output=True, timeout=10)
            with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as reservation:
                reservation.bind(('127.0.0.1', 0)); port = reservation.getsockname()[1]
            config = ET.parse(work/'server.conf')
            next(e for e in config.getroot().iter() if local(e) == 'Port').text = str(port)
            config.write(work/'server.conf', encoding='utf-8', xml_declaration=True)
            for phase in ['initial', 'recover', 'verified']:
                server_log = out/(phase+'-server.txt')
                with server_log.open('w') as log:
                    host = subprocess.Popen(['dotnet', str(a.server.resolve())], cwd=work, stdin=subprocess.PIPE, stdout=log, stderr=subprocess.STDOUT, text=True)
                    try:
                        deadline = time.monotonic()+12
                        while not re.search(r'listening on 0\.0\.0\.0:'+str(port), server_log.read_text()):
                            if host.poll() is not None or time.monotonic() > deadline:
                                raise RuntimeError('Server readiness deadline')
                            time.sleep(.05)
                        wire_phase = ('completed-' if a.outcome == 'completed' else '')+phase
                        command = ['dotnet', str(a.probe.resolve()), '--delivery-probe', str(port), wire_phase, str(state), str(a.client.resolve()), str(inbox)]
                        with tempfile.TemporaryFile() as capture:
                            peer = subprocess.Popen(command, cwd=work, stdout=capture, stderr=subprocess.STDOUT)
                            try:
                                peer.wait(timeout=45)
                            except BaseException:
                                peer.kill(); peer.wait(); raise
                            capture.seek(0); text = capture.read().decode(errors='replace')
                        (out/(phase+'-client.txt')).write_text(text)
                        assert peer.returncode == 0 and 'CLIENT DELIVERY NETWORK RESULT passed;' in text
                        private = json.loads(state.read_text())
                        database, = (work/'framework-extensions').rglob('trade-state.bin.sqlite3')
                        operations = oracle(database, phase)
                        if phase == 'verified':
                            assert operations == result['phases'][-1]['operations'], 'Duplicate receipt changed operation ledger'
                        assert all(key not in text and key not in server_log.read_text() for key in private['Keys'])
                        if phase == 'initial':
                            host.kill(); host.wait(timeout=5)
                        else:
                            host.stdin.write('stop\n'); host.stdin.flush(); host.wait(timeout=10)
                            assert host.returncode == 0
                        result['phases'].append({'phase': phase, 'passed': True, 'serverExitCode': host.returncode,
                                                 'clientExitCode': peer.returncode, 'operations': operations,
                                                 'localReceipts': len(list(inbox.rglob('*.receipt'))), 'localCheckpoints': len(list(inbox.rglob('*.checkpoint')))})
                    finally:
                        if host.poll() is None:
                            host.kill(); host.wait(timeout=5)
                        host.stdin.close()
        result['passed'] = True
        print('CLIENT DELIVERY JOINT RESULT passed; actual receipt/inbox DLL/UDP/SQLite/kill/restart/recipient/replay; synthetic checkpoint, no game save proof')
        return 0
    except Exception as error:
        result['errorType'] = type(error).__name__
        print('CLIENT DELIVERY JOINT RESULT failed; '+type(error).__name__)
        return 1
    finally:
        result['outputs'] = {f.name: digest(f) for f in out.glob('*.txt')}
        (out/'manifest.json').write_text(json.dumps(result, indent=2)+'\n')


if __name__ == '__main__':
    raise SystemExit(main())
