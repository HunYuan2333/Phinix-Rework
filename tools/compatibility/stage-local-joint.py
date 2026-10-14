"""Create a local two-game test cohort using the ordinary bundled-module loader.

No store catalog, release pin, installed package or game directory is changed.
Per-game ZIPs here are private validation intermediates, not distributed assets.
"""
import argparse
import hashlib
import json
import subprocess
import xml.etree.ElementTree as ET
from pathlib import Path

p = argparse.ArgumentParser(description=__doc__)
for name in ['build16', 'graph17', 'game16', 'game17', 'harmony16', 'harmony17', 'framework16', 'audit', 'packager', 'output']:
    p.add_argument('--' + name, type=Path, required=True)
a = p.parse_args()
repo = Path(__file__).resolve().parents[2]
out = a.output.resolve()
if out.is_relative_to(repo) or any(out.is_relative_to(path.resolve()) or path.resolve().is_relative_to(out)
                                 for key, path in vars(a).items() if key != 'output'):
    p.error('Use output isolated from repository and inputs.')
if out.exists() and any(out.iterdir()):
    p.error('Choose fresh output; previous candidates are immutable.')


def run(command, log):
    with log.open('w') as stream:
        result = subprocess.run(list(map(str, command)), stdout=stream, stderr=subprocess.STDOUT)
    if result.returncode:
        raise RuntimeError('Local candidate check failed; see ' + str(log))


def inspect(files):
    return json.loads(subprocess.check_output(['dotnet', str(a.audit), *map(str, files)], text=True))


host16 = a.build16 / 'Phinix-Rework/Output/phinix-rework'
out.parent.mkdir(parents=True, exist_ok=True)
run(['python', repo / 'tools/compatibility/stage-versioned-host.py', '--host16', host16,
     '--graph17', a.graph17, '--game16', a.game16, '--game17', a.game17, '--audit', a.audit,
     '--output', out], out.parent / (out.name + '-host-stage.log'))
package = out / 'phinix-rework-analysis'
cohort = [
    ('Phinix-Legacy-RedPacket', 'phinix.legacy.redpacket', 'Red packets',
     {'LegacyRedPacketExtension': 'Contracts', 'LegacyRedPacketExtension.Client': 'Client'}, 'Client/Resources'),
    ('Phinix-Legacy-TalentTrade', 'phinix.legacy.talent-trade', 'Talent trade',
     {'LegacyTalentTradeExtension': 'Contracts', 'LegacyTalentTradeExtension.Client': 'Client'}, 'Client/Resources'),
    ('Phinix-Example-Plugin', 'phinix.example.basic', 'Phinix Rework Example',
     {'Phinix.Example.Basic': 'example'}, 'example/Resources/Localization'),
]
records = []
for game in ['1.6', '1.7']:
    tfm = 'net472' if game == '1.6' else 'net10.0'
    game_refs = a.game16 if game == '1.6' else a.game17
    base = package / game
    # Only the Host and bundled extension roots are shared identities. The
    # independent plugins being packaged must never be supplied as Host-owned.
    host_files = list((base / 'Assemblies').glob('*.dll')) + list((base / 'Extensions').glob('*.dll'))
    hosts = {row['identity'].split(',', 1)[0]: Path(row['path']) for row in inspect(host_files)}
    for repository, package_id, name, projects, resource_folder in cohort:
        local = out / 'validation' / game / package_id
        local.mkdir(parents=True)
        dlls = [a.build16 / repository / part / 'bin/Release/net472' / (assembly + '.dll')
                if game == '1.6' else a.graph17 / assembly / 'bin/Release/net10.0' / (assembly + '.dll')
                for assembly, part in projects.items()]
        rows = inspect(dlls)
        own = set(projects)
        refs = {r['name'] for row in rows for r in row['references']} - own
        refs.add('ClientExtensionAbstractions')
        references = []
        for name_ref in sorted(refs):
            candidates = [hosts.get(name_ref), game_refs / (name_ref + '.dll')]
            if name_ref == '0Harmony':
                candidates.insert(0, a.harmony16 if game == '1.6' else a.harmony17)
            if game == '1.6':
                candidates += [a.framework16 / (name_ref + '.dll'), a.framework16 / 'Facades' / (name_ref + '.dll')]
            source = next((path for path in candidates if path is not None and path.is_file()), None)
            if source is None:
                raise RuntimeError('Missing local reference: ' + name_ref)
            references += ['--host-assembly', str(source)]
        config = {'compatibility': {'rimWorldVersions': [game], 'phinixRange': '>=0.9.7 <1.0.0',
                                    'abstractionsRange': '>=3.0.0 <4.0.0'}}
        (local / 'config.json').write_text(json.dumps(config, indent=2))
        bundle = base / 'Extensions' / package_id
        command = ['dotnet', a.packager, '--package-id', package_id, '--name', name,
                   '--version', '0.0.0', '--target-framework', tfm, '--compile-client-api', '3.0.0',
                   '--config', local / 'config.json', '--output', local / 'validation.zip',
                   '--bundle-output', bundle]
        for dll in dlls:
            command += ['--assembly', dll]
        for language in sorted((repo.parent / repository / resource_folder).rglob('*.json')):
            command += ['--language-file', language]
        run(command + references, local / 'pack.log')
        run(['dotnet', a.packager, '--validate', local / 'validation.zip', '--game-version', game] + references,
            local / 'validate.log')
        records.append({'game': game, 'packageId': package_id, 'targetFramework': tfm,
                        'version': '0.0.0', 'scope': 'local validation only',
                        'manifest': json.loads((local / 'validate.log').read_text())})
metadata = inspect(sorted(package.rglob('*.dll')))
for game in ['1.6', '1.7']:
    selected = [row for row in metadata if Path(row['path']).is_relative_to(package / game)]
    names = {row['identity'].split(',', 1)[0]: row for row in selected}
    if len(names) != len(selected):
        raise RuntimeError('Duplicate assembly identity in ' + game)
    for row in selected:
        for ref in row['references']:
            dependency = names.get(ref['name'])
            if dependency and ref['name'] in ['ClientExtensionAbstractions', 'ClientPresentation.Contracts']:
                if ref['version'] != '3.0.0.0':
                    raise RuntimeError('Old API reference in local cohort: ' + row['identity'])
about = ET.parse(package / 'About/About.xml')
about.getroot().find('name').text = 'Phinix Rework (Local Test)'
versions = about.getroot().find('supportedVersions')
versions.clear()
for game in ['1.6', '1.7']:
    ET.SubElement(versions, 'li').text = game
about.write(package / 'About/About.xml', encoding='utf-8', xml_declaration=True)
files = {str(path.relative_to(package)): hashlib.sha256(path.read_bytes()).hexdigest()
         for path in sorted(package.rglob('*')) if path.is_file()}
(out / 'joint-assembly-audit.json').write_text(json.dumps(metadata, indent=2))
(out / 'joint-payload-checks.json').write_text(json.dumps(records, indent=2))
(out / 'candidate-fingerprints.json').write_text(json.dumps(files, indent=2))
(out / 'STATUS.txt').write_text('Local API3 test cohort. Two game folders, same contracts, ordinary bundled discovery.\n'
                              'Independent plugins rebuilt locally; no store publication or release pin update.\n'
                              'GPU/input/runtime binding/business/save behavior have not passed game tests.\n')
print('Local joint cohort staged:', len(files), 'files;', len(records), 'per-game payload checks. Path:', package)
