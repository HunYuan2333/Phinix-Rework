"""Stage and inspect two isolated Host builds; never publish or install the result.

The net472 package supplies the maintained filenames and static resources. The
net10 graph must contain matching Host/bundled-extension identities. Independent
plugins are deliberately not made into managed packages by this tool.
"""
import argparse
import hashlib
import json
import shutil
import subprocess
import xml.etree.ElementTree as ET
from pathlib import Path


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def metadata(paths, audit):
    return json.loads(subprocess.check_output(
        ['dotnet', str(audit), *map(str, paths)], text=True))


def identity_name(row):
    return row['identity'].split(',', 1)[0]


def identity_version(row):
    return row['identity'].split('Version=', 1)[1].split(',', 1)[0]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for option in ['host16', 'graph17', 'game16', 'game17', 'audit', 'output']:
        parser.add_argument('--' + option, type=Path, required=True)
    parser.add_argument('--harmony17',type=Path,help='The external Harmony DLL used by the isolated 1.7 graph.')
    args = parser.parse_args()
    repo = Path(__file__).resolve().parents[2]
    output = args.output.resolve()
    inputs = [getattr(args, key).resolve() for key in
              ['host16', 'graph17', 'game16', 'game17', 'audit']]
    if args.harmony17:
        inputs.append(args.harmony17.resolve())
        fingerprints=json.loads((args.graph17/'reference-fingerprints.json').read_text())
        if fingerprints.get(str(args.harmony17.resolve()))!=digest(args.harmony17):
            parser.error('Harmony must match the exact reference used by the 1.7 graph.')
    if output.is_relative_to(repo) or any(
            output.is_relative_to(path) or path.is_relative_to(output) for path in inputs):
        parser.error('Output must be isolated from repository and inputs.')
    if output.exists() and any(output.iterdir()):
        parser.error('Choose fresh output; existing output is never replaced.')
    net16_files = sorted((args.host16 / 'Common').rglob('*.dll'))
    net16_files += sorted((args.host16 / '1.6/Assemblies').glob('*.dll'))
    if not net16_files:
        parser.error('Missing complete net472 package.')
    rows16 = metadata(net16_files, args.audit)
    by16 = {identity_name(row): row for row in rows16}
    if len(by16) != len(rows16):
        raise ValueError('Duplicate identity names in 1.6 input.')
    asset_path = args.graph17 / 'runtime-assets-final.json'
    if not asset_path.exists():
        asset_path = args.graph17 / 'runtime-assets.json'
    assets = json.loads(asset_path.read_text())
    fingerprint_path = args.graph17 / 'source-fingerprints-final.json'
    if not fingerprint_path.exists():
        fingerprint_path = args.graph17 / 'source-fingerprints.json'
    for entries in json.loads(fingerprint_path.read_text()).values():
        for entry in entries:
            if digest(Path(entry['path'])) != entry['sha256']:
                raise ValueError('Source changed since graph build: ' + entry['path'])
    for entry in assets.values():
        if digest(Path(entry['source'])) != entry['sha256']:
            raise ValueError('Stale graph output: ' + entry['source'])
    rows17 = metadata([Path(entry['source']) for entry in assets.values()], args.audit)
    by17 = {identity_name(row): row for row in rows17}
    if len(by17) != len(rows17):
        raise ValueError('Duplicate identity names in 1.7 graph.')
    # Product/CLR identities do not establish the version actually used by store planning.
    for game, rows in [('1.6', by16), ('1.7', by17)]:
        version = rows['PhinixClient'].get('compatibilityVersion')
        if version is None or tuple(map(int, version.split('.'))) < (0, 10, 1):
            raise ValueError('Host reports an obsolete store compatibility version for ' + game)
    graph = json.loads((args.graph17 / 'graph.json').read_text())
    source_names = {node['name'] for node in graph}
    host_names = set(by16) & source_names
    if not {'PhinixClient', 'ClientExtensionAbstractions', 'ClientPresentation.Contracts'} <= host_names:
        raise ValueError('Missing Host or public contracts.')
    # Both Verse loaders sort filenames, LoadFrom, then immediately GetTypes.
    # Numbered filenames cannot be discovered by CLR's ordinary identity probe
    # before being loaded. SDK interface implementations require contracts first.
    contracts_file = Path(by16['ClientPresentation.Contracts']['path']).name
    sdk_file = Path(by16['ClientExtensionAbstractions']['path']).name
    if contracts_file >= sdk_file:
        raise ValueError('Neutral contracts must sort before SDK for Verse type scanning.')
    if any(by16[name]['identity'] != by17[name]['identity'] for name in host_names):
        raise ValueError('Source identity differs between game builds.')
    # The vendored protobuf project intentionally targets net45 in the original
    # 1.6 build; preserve that baseline rather than changing its source/pins.
    if any(by16[name]['framework'] != ('.NETFramework,Version=v4.5' if name ==
                                      'Google.Protobuf' else '.NETFramework,Version=v4.7.2') or
           by17[name]['framework'] != '.NETCoreApp,Version=v10.0' for name in host_names):
        raise ValueError('Unexpected source target framework.')
    neutral = by17['ClientPresentation.Contracts']['references']
    if any(r['name'] in ['Assembly-CSharp', 'UnityStubs', 'ClientExtensionAbstractions',
                         'PhinixClient'] or r['name'].startswith('UnityEngine') for r in neutral):
        raise ValueError('Neutral contracts reference game/Host assembly.')
    # Select the dependency closure starting from Host and bundled modules. Never
    # accidentally bundle the three independent plugins from the joint graph.
    selected17 = set(host_names)
    pending = list(selected17)
    while pending:
        name = pending.pop()
        for reference in by17[name]['references']:
            dependency = reference['name']
            if dependency in by17 and dependency not in selected17:
                selected17.add(dependency)
                pending.append(dependency)
    if selected17 & (source_names - host_names):
        raise ValueError('Host closure acquired independent plugin dependency.')
    for version, game, selected in [('1.6', args.game16, set(by16)),
                                     ('1.7', args.game17, selected17)]:
        forbidden = {p.stem for p in game.glob('*.dll')}
        # Harmony is supplied by its own Mod, never by the Host package.
        forbidden |= {'0Harmony', 'Assembly-CSharp', 'UnityStubs', 'Waylith', 'Compat'}
        if selected & forbidden:
            raise ValueError('Game-provided DLL in ' + version + ': ' + str(selected & forbidden))
    output.mkdir(parents=True, exist_ok=True)
    package = output / 'phinix-rework-analysis'
    package.mkdir()
    for directory in ['About', 'Defs', 'Languages', 'Textures', 'Themes']:
        source = args.host16 / directory
        if source.is_dir():
            shutil.copytree(source, package / directory)
    # This is an analysis layout, not a release/support declaration.
    (package / 'About/PublishedFileId.txt').unlink(missing_ok=True)
    root = ET.Element('loadFolders')
    for version in ['1.6', '1.7']:
        entry = ET.SubElement(root, 'v' + version)
        ET.SubElement(entry, 'li').text = '/'
        ET.SubElement(entry, 'li').text = version
    ET.ElementTree(root).write(package / 'LoadFolders.xml', encoding='utf-8', xml_declaration=True)
    copied = []
    for row in rows16:
        source = Path(row['path'])
        relative = source.relative_to(args.host16)
        if relative.parts[0] == 'Common':
            relative = Path('1.6', *relative.parts[1:])
        destination = package / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, destination)
        copied.append({'version': '1.6', 'path': str(relative), 'sha256': digest(destination)})
    for name in sorted(selected17):
        row = by17[name]
        old = by16.get(name)
        if old:
            relative = Path(old['path']).relative_to(args.host16)
            relative = Path('1.7', *relative.parts[1:])
        else:
            relative = Path('1.7/Assemblies', name + '.dll')
        destination = package / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(row['path'], destination)
        copied.append({'version': '1.7', 'path': str(relative), 'sha256': digest(destination)})
    audit_rows = metadata(sorted(package.rglob('*.dll')), args.audit)
    closure = []
    for version in ['1.6', '1.7']:
        rows = [row for row in audit_rows if Path(row['path']).is_relative_to(package / version)]
        lookup = {identity_name(row): row for row in rows}
        for row in rows:
            for reference in row['references']:
                dependency = lookup.get(reference['name'])
                matches = dependency is not None and identity_version(dependency) == reference['version']
                if dependency and reference['name'] in source_names and not matches:
                    raise ValueError('Internal reference version mismatch: ' + row['identity'] +
                                     ' -> ' + str(reference))
                closure.append({'game': version, 'consumer': identity_name(row),
                                'reference': reference, 'scope': 'package' if dependency else 'external',
                                'versionMatches': matches if dependency else None})
    (output / 'assembly-audit.json').write_text(json.dumps(audit_rows, indent=2))
    (output / 'reference-closure.json').write_text(json.dumps(closure, indent=2))
    external17 = [entry for entry in closure if entry['game'] == '1.7' and entry['scope'] == 'external']
    external_names = {entry['reference']['name'] for entry in external17}
    external_paths={name:(args.harmony17 if name=='0Harmony' and args.harmony17 else args.game17/(name+'.dll')) for name in external_names}
    missing = sorted(name for name,path in external_paths.items() if not path.is_file())
    if missing:
        raise ValueError('1.7 game/runtime reference unavailable: ' + str(missing))
    external_rows = metadata([external_paths[name] for name in sorted(external_names)], args.audit)
    lookup = {identity_name(row): row for row in external_rows}
    differences = [dict(entry, actual=lookup[entry['reference']['name']]['identity'])
                   for entry in external17 if identity_version(lookup[entry['reference']['name']]) !=
                   entry['reference']['version']]
    (output / 'external-audit-17.json').write_text(json.dumps(
        {'metadata': external_rows, 'missing': missing, 'versionDifferences': differences}, indent=2))
    (output / 'layout-fingerprints.json').write_text(json.dumps(copied, indent=2))
    (output / 'STATUS.txt').write_text(
        'Analysis layout only; not installed, published or game-tested.\n'
        'Both game target frameworks and source identities were inspected. External\n'
        'game/runtime and explicit Harmony references were located; version differences\n'
        'are recorded, not waived. This does not establish game runtime acceptance.\n')
    print('Versioned Host layout:', len(by16), 'DLLs for 1.6;', len(selected17),
          'DLLs for 1.7;', len(host_names), 'source identities matched. Output:', output)


if __name__ == '__main__':
    main()
