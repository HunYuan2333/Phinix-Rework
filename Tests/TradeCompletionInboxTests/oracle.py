"""Independent literal oracle for the synthetic receipt exported by --oracle."""
import hashlib
import json
from pathlib import Path
import sys

raw = Path(sys.argv[1]).read_bytes()
assert len(raw) <= 4 * 1024 * 1024
record = json.loads(raw)
assert record['Schema'] == 1
assert record['Endpoint'] == '127.0.0.1:16200'
assert record['Recipient'] == 'a' and record['OtherParty'] == 'b'
assert record['TradeId'] == 'kill' and record['Completed'] is True
assert len(record['Items']) == 1
assert record['Items'][0]['CodecId'] == 'synthetic.unavailable-codec'
assert record['Items'][0]['PayloadBytes'] == [23]
expected = record['Fingerprint']
record['Fingerprint'] = None
canonical = json.dumps(record, separators=(',', ':'), ensure_ascii=False).encode('utf-8')
assert hashlib.sha256(canonical).hexdigest() == expected
assert b'SessionId' not in raw and b'ClientKey' not in raw
print(json.dumps({'passed': True, 'oracle': 'independent json/hashlib',
                  'faultId': 'committed-receipt-held', 'opaqueByte': 23,
                  'scope': 'synthetic file evidence, no game asset claim'}))
