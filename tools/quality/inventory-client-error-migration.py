#!/usr/bin/env python3
"""Track migration against the immutable assessment baseline; never overwrite that baseline."""
import argparse
import csv
import hashlib
import json
from pathlib import Path

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--require-stage-complete',choices=['E1','E2','E3','E4','E5'])
args=parser.parse_args()
root = Path(__file__).resolve().parents[2]
baseline = root / 'docs/branch-local/audits/Client-Error-Observability-2026-11-15/source-inventory.json'
out = root / 'docs/branch-local/dev/Client-Error-Reporting-2026-11-15'
out.mkdir(exist_ok=True)
data = json.loads(baseline.read_text())
decisions_path = out / 'migration-decisions.json'
decisions = {}
if decisions_path.exists():
    reviewed = json.loads(decisions_path.read_text())
    if reviewed['baseline_inventory_sha256'] != hashlib.sha256(baseline.read_bytes()).hexdigest():
        raise SystemExit('Assessment baseline changed; review migration decisions before regenerating.')
    decisions = {(r['path'], r['baseline_line'], r['kind']): r for r in reviewed['decisions']}
    baseline_keys = {(r['path'], r['line'], r['kind']) for r in data['sites']}
    if not decisions.keys() <= baseline_keys:
        raise SystemExit('Migration decision refers to a missing baseline site.')
hashes = {f['path']: f['sha256'] for f in data['files']}
rows = []
for site in data['sites']:
    path = site['path']
    stage = 'E2'
    if path.startswith('Extensions/'):
        stage = 'E5' if '/Tools/' in path or '/Samples/' in path else 'E4'
    elif path.startswith(('Client/Common/', 'Client/Storage.', 'Dependencies/Phinix.Common/Common/Connections/', 'Dependencies/Phinix.Common/Common/UserManagement/')):
        stage = 'E3'
    elif path.startswith(('Client/Source/Settings.', 'Client/Source/LegacySettings.', 'Client/Source/UI/UiTheme.', 'Client/Source/UI/Nickname', 'Client/Source/UI/Theme')):
        stage = 'E3'
    current = hashlib.sha256((root / path).read_bytes()).hexdigest()
    status = 'pending-review'
    disposition = 'Classify at source and follow downstream reporting before changing behavior.'
    if path == 'Client/Source/Framework/ClientMainThreadDispatcher.cs' and site['kind'] == 'catch' and site['line'] == 127:
        stage, status = 'E1', 'implemented'
        disposition = 'Report original callback exception before returning Failed; keep queue isolation; retain complete fallback log.'
    if path == 'Client/Source/Client.cs' and site['kind'] == 'catch' and site['line'] == 540:
        stage, status = 'E1', 'implemented'
        disposition = 'Capture startup failure before cleanup; retain cleanup failure separately; preserve isolated startup-failure policy.'
    decision = decisions.get((path, site['line'], site['kind']))
    if decision:
        stage, status, disposition = decision['stage'], decision['status'], decision['disposition']
    rows.append([path, site['line'], site['kind'], stage, status, hashes[path], current != hashes[path], disposition])
with (out / 'migration-ledger.tsv').open('w') as stream:
    writer = csv.writer(stream, delimiter='\t', lineterminator='\n')
    writer.writerow(['path', 'baseline_line', 'kind', 'stage', 'status', 'baseline_sha256', 'source_changed_since_assessment', 'disposition'])
    writer.writerows(rows)
print('Migration ledger:', len(rows), 'baseline sites;', sum(row[4] == 'implemented' for row in rows), 'implemented sites;', sum(row[4].startswith('reviewed-') for row in rows), 'reviewed dispositions; remaining sites retain explicit pending status.')

if args.require_stage_complete:
    pending=[row for row in rows if row[3]==args.require_stage_complete and row[4]=="pending-review"]
    if pending: raise SystemExit(str(len(pending))+" positions still pending in "+args.require_stage_complete)
    print(args.require_stage_complete+" baseline review complete; no pending positions.")
