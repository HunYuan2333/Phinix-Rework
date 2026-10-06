#!/usr/bin/env python3
"""Export an allowlisted official plugin, without the main checkout or binaries."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[4]
TEMPLATES = Path(__file__).resolve().parent / 'templates'
PACKAGES = {
    'redpacket': ('LegacyRedPacket', 'phinix.legacy.redpacket', 'Red packets'),
    'talent': ('LegacyTalentTrade', 'phinix.legacy.talent-trade', 'Talent trade'),
}
HOST_REFERENCES = {
    'Utils': 'Common/Assemblies/03-Utils.dll',
    'UserManagement': 'Common/Assemblies/08-UserManagement.dll',
    'ClientExtensionAbstractions': 'Common/Assemblies/10-ClientExtensionAbstractions.dll',
    'TradeExtension': 'Common/Extensions/09-TradeExtension.dll',
    'InventoryExtension': 'Common/Extensions/10-InventoryExtension.dll',
    'TradeExtension.Client': 'Common/Extensions/12-TradeExtension.Client.dll',
}


def read_project(path):
    tree = ET.parse(path).getroot()
    for element in tree.iter():
        element.tag = element.tag.split('}')[-1]
    return tree


def checked_source(path, owner, preserve_legacy_client_key=False):
    if path.is_symlink() or not path.resolve().is_relative_to(owner.resolve()):
        raise ValueError('Source is outside its owner: ' + path.name)
    data = path.read_bytes()
    if path.suffix == '.cs':
        # Report only filenames; never echo a credential or matched line.
        pattern = rb'(?i)\b(\w*(?:ApiKey|Password|AccessToken|Secret))\s*=\s*"([^"\r\n]+)"'
        for match in re.finditer(pattern, data):
            # The user requires this legacy public-client interoperability parameter
            # to remain unchanged. This exception covers one field in one owner;
            # it never permits unrelated credentials or prints the field value.
            permitted = (preserve_legacy_client_key and owner.name == 'LegacyRedPacket'
                         and path.relative_to(owner).as_posix() == 'Client/RedPacketRelay.cs'
                         and match.group(1) == b'RelayApiKey')
            if not permitted:
                raise ValueError('Embedded credential blocks export: ' + path.name)
    return data


def project_xml(tree, client, assembly_name, own_contract):
    project = ET.Element('Project', {'Sdk': 'Microsoft.NET.Sdk'})
    props = ET.SubElement(project, 'PropertyGroup')
    for name, value in {
        'TargetFramework': 'net472', 'LangVersion': '7.3',
        'EnableDefaultCompileItems': 'false', 'GenerateAssemblyInfo': 'false',
        'AssemblyName': assembly_name,
    }.items():
        ET.SubElement(props, name).text = value
    sources = ET.SubElement(project, 'ItemGroup')
    compile_items = tree.findall('./ItemGroup/Compile')
    if compile_items:
        for item in compile_items:
            ET.SubElement(sources, 'Compile', {'Include': item.attrib['Include'].replace('\\', '/')})
    else:
        ET.SubElement(sources, 'Compile', {'Include': '*.cs'})
    references = ET.SubElement(project, 'ItemGroup')
    for item in tree.findall('./ItemGroup/Reference'):
        name = item.attrib['Include'].split(',')[0]
        ref = ET.SubElement(references, 'Reference', {'Include': name})
        if name.startswith('UnityEngine') or name == 'Assembly-CSharp':
            ET.SubElement(ref, 'HintPath').text = '$(GameReferences)/' + name + '.dll'
        elif name == '0Harmony':
            ET.SubElement(ref, 'HintPath').text = '$(HarmonyReferences)/0Harmony.dll'
        ET.SubElement(ref, 'Private').text = 'false'
    for item in tree.findall('./ItemGroup/ProjectReference'):
        reference_path = Path(item.attrib['Include'].replace('\\', '/'))
        name = reference_path.stem
        if client and name == own_contract:
            ref = ET.SubElement(references, 'ProjectReference', {'Include': '../Contracts/' + own_contract + '.csproj'})
        else:
            if name not in HOST_REFERENCES:
                raise ValueError('Unknown shared project reference: ' + name)
            ref = ET.SubElement(references, 'Reference', {'Include': name})
            ET.SubElement(ref, 'HintPath').text = '$(PhinixPackage)/' + HOST_REFERENCES[name]
        ET.SubElement(ref, 'Private').text = 'false'
    ET.indent(project, space='  ')
    return ET.tostring(project, encoding='utf-8', xml_declaration=True) + b'\n'


def export(kind, destination, preserve_legacy_client_key=False, source_visibility='public'):
    if source_visibility not in ('public', 'private'):
        raise ValueError('Invalid source visibility.')
    owner_name, package_id, name = PACKAGES[kind]
    owner = ROOT / 'Extensions' / owner_name
    # Validate the whole source allowlist before creating a destination.
    files = {}
    for part in ('Contracts', 'Client'):
        source_dir = owner / part
        project_path, = source_dir.glob('*.csproj')
        tree = read_project(project_path)
        assembly_name = tree.findtext('./PropertyGroup/AssemblyName')
        items = tree.findall('./ItemGroup/Compile')
        paths = [source_dir / item.attrib['Include'].replace('\\', '/') for item in items] if items else list(source_dir.glob('*.cs'))
        if not paths:
            raise ValueError('Empty source allowlist: ' + part)
        for path in paths:
            files[path.relative_to(owner).as_posix()] = checked_source(path, owner, preserve_legacy_client_key)
        files[part + '/' + project_path.name] = project_xml(tree, part == 'Client', assembly_name, owner_name + 'Extension')
    for path in sorted((owner / 'Client' / 'Resources').rglob('*.json')):
        files[path.relative_to(owner).as_posix()] = checked_source(path, owner)
    files['Client/localization.json'] = checked_source(owner / 'Client/localization.json', owner)
    for path in sorted(TEMPLATES.rglob('*')):
        if path.is_file() and '__pycache__' not in path.parts and path.suffix != '.pyc':
            files[path.relative_to(TEMPLATES).as_posix()] = path.read_bytes()
    files['publication.json'] = (json.dumps({
        'schemaVersion': 1, 'packageId': package_id, 'name': name, 'version': '1.0.0',
        'ownerDirectory': owner_name, 'contractAssembly': owner_name + 'Extension',
        'clientAssembly': owner_name + 'Extension.Client',
        'moduleId': 'builtin.legacy-redpacket' if kind == 'redpacket' else 'builtin.legacy-talent-trade',
        'status': 'candidate-not-admitted',
        'sourceVisibility': source_visibility,
        'legacyClientKeyPreserved': kind == 'redpacket' and preserve_legacy_client_key,
    }, indent=2) + '\n').encode()
    # Candidate provenance records file hashes, not absolute local paths or game data.
    files['source-manifest.json'] = (json.dumps({
        'schemaVersion': 1, 'packageId': package_id,
        'files': {name: hashlib.sha256(data).hexdigest() for name, data in sorted(files.items())},
    }, indent=2) + '\n').encode()
    if destination.exists():
        raise ValueError('Destination already exists; choose a new snapshot directory.')
    destination.mkdir(parents=True)
    try:
        for name, data in sorted(files.items()):
            target = destination / name
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(data)
    except Exception:
        shutil.rmtree(destination)
        raise
    return {'packageId': package_id, 'files': len(files), 'destination': str(destination)}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('package', choices=PACKAGES)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--source-visibility', choices=('public', 'private'), default='public')
    parser.add_argument('--preserve-legacy-client-key', action='store_true',
                        help='Preserve the user-authorized RedPacket legacy client parameter; no other credentials.')
    arguments = parser.parse_args()
    try:
        print(json.dumps(export(arguments.package, arguments.output.resolve(), arguments.preserve_legacy_client_key, arguments.source_visibility)))
    except (ValueError, OSError) as error:
        raise SystemExit(str(error))
