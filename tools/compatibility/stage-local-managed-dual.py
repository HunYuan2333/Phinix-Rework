"""Stage a dual-game Host and separate, dual-runtime managed plugin ZIPs locally.

The Host deliberately has no independent plugin copies; install these ZIPs with
local import so ordinary bundled assembly occupancy cannot mask the managed path.
Inputs are independently verified per-game payloads from the current API3 cohort.
No catalog, game directory, release pin or player data is modified.
"""
import argparse
import hashlib
import json
import subprocess
import zipfile
import xml.etree.ElementTree as ET
from pathlib import Path

p = argparse.ArgumentParser(description=__doc__)
for name in ['host16', 'graph17', 'game16', 'game17', 'audit', 'packager', 'validation', 'output']:
    p.add_argument('--' + name, type=Path, required=True)
a = p.parse_args()
repo = Path(__file__).resolve().parents[2]
out = a.output.resolve()
if out.is_relative_to(repo) or any(out.is_relative_to(v.resolve()) or v.resolve().is_relative_to(out)
                                  for k, v in vars(a).items() if k != 'output'):
    p.error('Choose isolated output outside repository and inputs.')
if out.exists() and any(out.iterdir()):
    p.error('Existing candidates are immutable.')
subprocess.run(['python3', str(repo / 'tools/compatibility/stage-versioned-host.py'),
                *[arg for name in ['host16', 'graph17', 'game16', 'game17', 'audit']
                  for arg in ['--' + name, str(getattr(a, name))]], '--output', str(out)], check=True)
records = []
for package in ['phinix.legacy.redpacket', 'phinix.legacy.talent-trade', 'phinix.example.basic']:
    target = out / (package + '-dual-local.zip')
    report = subprocess.check_output(['dotnet', str(a.packager), '--pack-variants',
        '--variant', str(a.validation / '1.6' / package / 'validation.zip'),
        '--variant', str(a.validation / '1.7' / package / 'validation.zip'),
        '--output', str(target)], text=True)
    records.append(json.loads(report))
    for game in ['1.6', '1.7']:
        report = subprocess.check_output(['dotnet', str(a.packager), '--validate', str(target),
                                         '--game-version', game], text=True)
        (out / (package + '-' + game + '-validate.json')).write_text(report)
host = out / 'phinix-rework-analysis'
if any((host / game / 'Extensions' / package['packageId']).exists()
       for game in ['1.6', '1.7'] for package in records):
    raise RuntimeError('Independent plugin must not also be bundled.')
about = ET.parse(host / 'About/About.xml')
about.getroot().find('name').text = 'Phinix Rework'
versions = about.getroot().find('supportedVersions')
versions.clear()
for game in ['1.6', '1.7']:
    ET.SubElement(versions, 'li').text = game
about.getroot().find('description').text = ET.parse(Path(__file__).resolve().parents[2] / 'Client/About/About.xml').getroot().findtext('description')
about.write(host / 'About/About.xml', encoding='utf-8', xml_declaration=True)
archive = out / 'phinix-rework-dual-managed-local-host.zip'
with zipfile.ZipFile(archive, 'x', compression=zipfile.ZIP_DEFLATED) as z:
    for path in sorted(host.rglob('*')):
        if path.is_file():
            info = zipfile.ZipInfo(str(path.relative_to(host.parent)), (2026, 10, 14, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            z.writestr(info, path.read_bytes())
(out / 'dual-packages.json').write_text(json.dumps(records, indent=2))
(out / 'LOCAL-TEST.txt').write_text('Local API3 test only; no publication. Host has no independent plugin bundles.\n'
    'Install redpacket/talent-trade/example separately through local ZIP import; restart to activate.\n'
    'Each ZIP owns 1.6/net472 and 1.7/net10.0 payloads under one package/version.\n'
    'Use disposable game/profile/save data; runtime activation and business behavior still require game tests.\n')
print(json.dumps({'hostZip': str(archive), 'hostSha256': hashlib.sha256(archive.read_bytes()).hexdigest(),
                  'plugins': records}, indent=2))
