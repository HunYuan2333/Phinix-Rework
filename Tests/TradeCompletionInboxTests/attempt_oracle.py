"""Literal independent expectation after SIGKILL at delivery-attempt-held."""
import hashlib
import json
from pathlib import Path
import sys

root = Path(sys.argv[1])
receipt_path, = root.glob('*.receipt')
attempt_path, = root.glob('*.attempt')
receipt = json.loads(receipt_path.read_bytes())
attempt = json.loads(attempt_path.read_bytes())
assert receipt['Recipient'] == 'a' and receipt['TradeId'] == 'attempt-kill'
assert receipt['Items'][0]['PayloadBytes'] == [23]
assert attempt['Schema'] == 1 and attempt['State'] == 'Attempted'
assert attempt['Mode'] == 'Inventory'
assert attempt['ReceiptFingerprint'] == receipt['Fingerprint']
for record in (receipt, attempt):
    expected = record['Fingerprint']
    record['Fingerprint'] = None
    assert hashlib.sha256(json.dumps(record, separators=(',', ':'), ensure_ascii=False).encode()).hexdigest() == expected
assert not list(root.glob('*.result')), 'Killed attempt must not acquire a fabricated result'
assert attempt_path.stem == receipt_path.stem
assert b'ClientKey' not in attempt_path.read_bytes() and b'SessionId' not in attempt_path.read_bytes()
print('ATTEMPT ORACLE passed; exact opaque byte=23, Attempted, no result/ACK; independent json/hashlib')
