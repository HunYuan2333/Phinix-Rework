"""Final distribution checks; DLL metadata/closure is checked separately by staging."""
from pathlib import Path
import re
import xml.etree.ElementTree as ET
import zipfile


def validate_host_package(archive, client, layout):
    client, layout = Path(client), Path(layout)
    source_about = ET.parse(client / 'About/About.xml').getroot()
    identity = (client / 'About/PublishedFileId.txt').read_text().strip()
    if not re.fullmatch(r'[1-9][0-9]*', identity):
        raise ValueError('Invalid source Workshop identity.')
    with zipfile.ZipFile(archive) as package:
        names = package.namelist()
        if len(names) != len(set(names)) or package.testzip() is not None:
            raise ValueError('Duplicate entries or invalid ZIP CRC.')
        expected = {'phinix-rework/' + p.relative_to(layout).as_posix(): p
                    for p in layout.rglob('*') if p.is_file()}
        if set(names) != set(expected):
            raise ValueError('Distribution ZIP differs from staged file inventory.')
        for name, path in expected.items():
            if package.read(name) != path.read_bytes():
                raise ValueError('Distribution bytes differ: ' + name)
        prefix = 'phinix-rework/'
        required = [prefix + p for p in ['About/About.xml', 'About/PublishedFileId.txt',
                                        'About/Preview.png', 'LoadFolders.xml']]
        if any(name not in expected for name in required):
            raise ValueError('Missing required Workshop/loading metadata.')
        if package.read(prefix + 'About/PublishedFileId.txt').decode().strip() != identity:
            raise ValueError('Workshop upload identity differs from source.')
        about = ET.fromstring(package.read(prefix + 'About/About.xml'))
        if (about.findtext('name') != 'Phinix Rework' or
                about.findtext('packageId') != source_about.findtext('packageId') or
                about.findtext('description').strip() != source_about.findtext('description').strip()):
            raise ValueError('Player-facing metadata differs from source.')
        if [n.text for n in about.findall('supportedVersions/li')] != ['1.6', '1.7']:
            raise ValueError('Missing dual-version support metadata.')
        for field in ['author', 'url', 'modDependencies', 'loadAfter']:
            if ET.tostring(about.find(field)) != ET.tostring(source_about.find(field)):
                raise ValueError('Missing or changed About metadata: ' + field)
        folders = ET.fromstring(package.read(prefix + 'LoadFolders.xml'))
        if [n.tag for n in folders] != ['v1.6', 'v1.7']:
            raise ValueError('Unexpected game loading profiles.')
        for game in ['1.6', '1.7']:
            if [n.text for n in folders.findall('v' + game + '/li')] != ['/', game]:
                raise ValueError('Mixed runtime loading directories.')
            if not any(n.startswith(prefix + game + '/') and n.endswith('/13-PhinixClient.dll') for n in names):
                raise ValueError('Missing Host DLL for ' + game)
        for directory in ['About', 'Defs', 'Languages', 'Textures', 'Themes']:
            for source in (client / directory).rglob('*'):
                if not source.is_file() or source.name in ['About.xml', 'PublishedFileId.txt']:
                    continue
                name = prefix + source.relative_to(client).as_posix()
                if name not in expected or package.read(name) != source.read_bytes():
                    raise ValueError('Missing or stale static resource: ' + name)
        if prefix + 'About/Preview.png' not in expected:
            raise ValueError('Missing Workshop preview.')
    return {'workshopId': identity, 'files': len(expected), 'games': ['1.6', '1.7']}
