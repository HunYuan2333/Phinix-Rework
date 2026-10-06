#!/usr/bin/env python3
"""Read-only CI checks. Does not load candidate DLLs or contact legacy services."""
import hashlib
import json
from pathlib import Path
import re
import xml.etree.ElementTree as ET


def check(root):
    metadata = json.loads((root / 'publication.json').read_text())
    manifest = json.loads((root / 'source-manifest.json').read_text())
    for name, digest in manifest['files'].items():
        path = root / name
        if path.is_symlink() or not path.resolve().is_relative_to(root.resolve()):
            raise ValueError('Invalid source path: ' + name)
        if hashlib.sha256(path.read_bytes()).hexdigest() != digest:
            raise ValueError('Snapshot changed; refresh its source manifest: ' + name)
    expected_sources = set()
    for part in ('Contracts', 'Client'):
        project, = (root / part).glob('*.csproj')
        tree = ET.parse(project).getroot()
        for item in tree.findall('./ItemGroup/Compile'):
            paths = list((root / part).glob(item.attrib['Include']))
            if not paths:
                raise ValueError('Missing compile source: ' + item.attrib['Include'])
            expected_sources.update(p.relative_to(root).as_posix() for p in paths)
        for item in tree.findall('./ItemGroup/ProjectReference'):
            target = (project.parent / item.attrib['Include']).resolve()
            if not target.is_relative_to(root.resolve()) or not target.is_file():
                raise ValueError('Project references must remain inside the standalone repository.')
        for item in tree.findall('./ItemGroup/Reference'):
            if item.findtext('Private') != 'false':
                raise ValueError('Shared/game references must be compile-only.')
    actual_sources = {p.relative_to(root).as_posix() for part in ('Client', 'Contracts') for p in (root / part).rglob('*.cs') if 'obj' not in p.parts and 'bin' not in p.parts}
    if actual_sources != expected_sources:
        raise ValueError('Compile allowlist differs from the owned source files.')
    for name in expected_sources:
        data = (root / name).read_bytes()
        for match in re.finditer(rb'(?i)\b(\w*(?:ApiKey|Password|AccessToken|Secret))\s*=\s*"([^"\r\n]+)"', data):
            permitted = (metadata['packageId'] == 'phinix.legacy.redpacket'
                         and metadata['legacyClientKeyPreserved'] is True
                         and name == 'Client/RedPacketRelay.cs' and match.group(1) == b'RelayApiKey')
            if not permitted:
                raise ValueError('Embedded credential: ' + name)
    companion = json.loads((root / 'Client/localization.json').read_text())
    for resource in companion['resources']:
        path = root / 'Client' / resource['path']
        data = path.read_bytes()
        if len(data) != resource['length'] or hashlib.sha256(data).hexdigest() != resource['sha256']:
            raise ValueError('Language declaration mismatch: ' + resource['path'])
        language = json.loads(data)
        if not language['display']['name'] or not language['display']['summary'] or not language['strings']:
            raise ValueError('Incomplete language resource: ' + resource['path'])
    forbidden = [p for p in root.rglob('*') if p.is_file() and '.git' not in p.parts and 'bin' not in p.parts and 'obj' not in p.parts and 'artifacts' not in p.parts and (p.suffix.lower() in ('.dll', '.pdb', '.zip', '.log') or p.name.endswith('.local.json'))]
    if forbidden:
        raise ValueError('Build/runtime data in source snapshot: ' + forbidden[0].name)
    print(json.dumps({'event': 'source.checked', 'packageId': metadata['packageId'],
                      'sourceFiles': len(expected_sources), 'languages': len(companion['resources']),
                      'legacyClientKeyPreserved': metadata['legacyClientKeyPreserved']}))


if __name__ == '__main__':
    check(Path(__file__).resolve().parent)
