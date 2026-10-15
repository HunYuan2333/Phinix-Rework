#!/usr/bin/env python3
"""Build a complete local dual-version release candidate without installing or publishing."""
import argparse
import json
import os
from pathlib import Path
import re
import subprocess
import zipfile
import hashlib
import xml.etree.ElementTree as ET
from host_package import validate_host_package

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('--game16', type=Path, required=True)
p.add_argument('--game17', type=Path, required=True)
p.add_argument('--harmony17', type=Path, required=True)
p.add_argument('--output', type=Path, required=True)
a = p.parse_args()
repo = Path(__file__).resolve().parents[2]
out = a.output.resolve()
protected = [repo.parent, a.game16.resolve(), a.game17.resolve(), a.harmony17.resolve().parent]
if any(out.is_relative_to(path) or path.is_relative_to(out) for path in protected):
    p.error('Choose a fresh output directory outside the workspace and game references, for example /tmp/phinix-dual-build-01.')
if out.exists() and any(out.iterdir()):
    p.error('Output exists; choose a new directory to preserve previous builds.')
for path in [a.game16 / 'Assembly-CSharp.dll', a.game17 / 'Assembly-CSharp.dll', a.game17 / 'UnityStubs.dll', a.harmony17]:
    if not path.is_file():
        p.error('Missing compile-only reference: ' + str(path))
# Scope SDK selection to this build. Never edit the vendored protobuf SDK pins.
env = os.environ.copy()
sdks = subprocess.check_output(['dotnet', '--list-sdks'], text=True)
installed = [re.fullmatch(r'(10\.[^ ]+) \[(.*)\]', line) for line in sdks.splitlines()]
installed = [m for m in installed if m]
if not installed:
    p.error('Install a .NET 10 SDK before building both runtimes.')
selected = installed[-1]
env['MSBuildSDKsPath'] = str(Path(selected.group(2)) / selected.group(1) / 'Sdks')
out.mkdir(parents=True, exist_ok=True)
commands = []
def run(args):
    command = [str(value) for value in args]
    commands.append(command)
    (out / 'commands.json').write_text(json.dumps(commands, indent=2))
    subprocess.run(command, cwd=repo, env=env, check=True)

run(['python3', repo / 'tools/compatibility/build-graph-isolated.py', '--game', a.game16.resolve(), '--output', out / 'build16', '--plugins', '--test-locks'])
run(['python3', repo / 'tools/compatibility/build-net10-graph.py', '--game', a.game17.resolve(), '--harmony', a.harmony17.resolve(), '--output', out / 'build17'])
audit = out / 'metadata-audit'
audit.mkdir()
project = ET.Element('Project', Sdk='Microsoft.NET.Sdk')
properties = ET.SubElement(project, 'PropertyGroup')
for key, value in {'TargetFramework': 'net10.0', 'OutputType': 'Exe', 'EnableDefaultCompileItems': 'false', 'NuGetAudit': 'false'}.items():
    ET.SubElement(properties, key).text = value
items = ET.SubElement(project, 'ItemGroup')
ET.SubElement(items, 'Compile', Include=str(repo / 'tools/compatibility/AssemblyTargetAudit.cs'))
ET.ElementTree(project).write(audit / 'Audit.csproj', encoding='unicode')
(audit / 'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
run(['dotnet', 'build', audit / 'Audit.csproj', '-c', 'Release', '-v:q'])
run(['python3', repo / 'tools/compatibility/stage-versioned-host.py', '--host16', out / 'build16/Phinix-Rework/Output/phinix-rework', '--graph17', out / 'build17', '--game16', a.game16.resolve(), '--game17', a.game17.resolve(), '--harmony17', a.harmony17.resolve(), '--audit', audit / 'bin/Release/net10.0/Audit.dll', '--output', out / 'staged'])
host = out / 'staged/phinix-rework-analysis'
about = ET.parse(host / 'About/About.xml')
root = about.getroot()
root.find('name').text = 'Phinix Rework'
versions = root.find('supportedVersions')
versions.clear()
for game in ['1.6', '1.7']:
    ET.SubElement(versions, 'li').text = game
root.find('description').text = ET.parse(repo / 'Client/About/About.xml').getroot().findtext('description')
about.write(host / 'About/About.xml', encoding='utf-8', xml_declaration=True)
# Analysis staging strips Workshop identity; the distribution package updates the existing item.
published_id = (repo / 'Client/About/PublishedFileId.txt').read_text().strip()
if not re.fullmatch(r'[1-9][0-9]*', published_id):
    raise RuntimeError('Invalid Workshop upload identity in source About directory.')
(host / 'About/PublishedFileId.txt').write_text(published_id + '\n')
archive = out / 'phinix-rework-dual-release-candidate.zip'
with zipfile.ZipFile(archive, 'x', compression=zipfile.ZIP_DEFLATED) as z:
    for file in sorted(host.rglob('*')):
        if file.is_file():
            z.write(file, str(Path('phinix-rework') / file.relative_to(host)))
with zipfile.ZipFile(archive) as z:
    if z.testzip() is not None:
        raise RuntimeError('ZIP CRC failure.')
package_check = validate_host_package(archive, repo / 'Client', host)
(out / 'package-audit.json').write_text(json.dumps(package_check, indent=2))
result = {'hostZip': str(archive), 'sha256': hashlib.sha256(archive.read_bytes()).hexdigest(), 'games': ['1.6', '1.7'], 'clientApi': '3.0.0', 'scope': 'local release candidate; no publication or game acceptance', 'independentPluginZips': 'Not bundled; installed from separate dual-runtime plugin ZIPs.'}
(out / 'result.json').write_text(json.dumps(result, indent=2))
print(json.dumps(result, indent=2))
