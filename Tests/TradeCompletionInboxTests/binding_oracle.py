"""Independent expectation for a synthetic immutable save binding."""
import hashlib
import json
from pathlib import Path
import sys

root = Path(sys.argv[1])
receipt_path, = root.glob('*.receipt')
binding_path, = root.glob('*.binding')
receipt = json.loads(receipt_path.read_bytes())
binding = json.loads(binding_path.read_bytes())
assert receipt['Recipient'] == 'owner' and receipt['TradeId'] == 'new'
assert receipt['Items'][0]['PayloadBytes'] == [23]
assert binding['Schema'] == 1 and binding['SaveIdentity'] == 'a' * 64
assert binding['ReceiptFingerprint'] == receipt['Fingerprint']
assert binding_path.stem == receipt_path.stem
for record in (receipt, binding):
    expected = record['Fingerprint']
    record['Fingerprint'] = None
    assert hashlib.sha256(json.dumps(record, separators=(',', ':'), ensure_ascii=False).encode()).hexdigest() == expected
assert b'/private/' not in binding_path.read_bytes() and b'ClientKey' not in binding_path.read_bytes()
print('BINDING ORACLE passed; literal save identity, receipt hash and opaque byte=23; no game-save or delivery proof')
