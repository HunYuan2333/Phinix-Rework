#!/usr/bin/env python3
"""Run existing Phase35 source tests against a built net10 shared graph, outside repository outputs."""
import argparse
import os
from pathlib import Path
import subprocess
import xml.etree.ElementTree as E

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('--output', type=Path, required=True)
p.add_argument('--runtime17', type=Path, required=True)
a = p.parse_args()
root = Path(__file__).resolve().parents[2]
out = a.output.resolve()
runtime = a.runtime17.resolve()
if out.exists() or out.is_relative_to(root.parent):
    p.error('Use fresh isolated output.')
for name in ['Utils', 'Connections', 'Authentication', 'Google.Protobuf']:
    if not (runtime / (name + '.dll')).is_file():
        p.error('Build the net10 graph first: missing ' + name)
out.mkdir(parents=True)
source = root / 'Tests/Phase35ClientRuntimeTests/Phase35ClientRuntimeTests.csproj'
project = E.Element('Project', Sdk='Microsoft.NET.Sdk')
props = E.SubElement(project, 'PropertyGroup')
for key, value in {'TargetFramework': 'net10.0', 'OutputType': 'Exe', 'EnableDefaultCompileItems': 'false', 'NuGetAudit': 'false'}.items():
    E.SubElement(props, key).text = value
items = E.SubElement(project, 'ItemGroup')
for path in list(source.parent.glob('*.cs')) + [(source.parent / entry.attrib['Include'].replace('\\', '/')).resolve() for entry in E.parse(source).iter('Compile')]:
    E.SubElement(items, 'Compile', Include=str(path))
for name in ['Utils', 'Connections', 'Authentication', 'Google.Protobuf']:
    entry = E.SubElement(items, 'Reference', Include=name)
    E.SubElement(entry, 'HintPath').text = str(runtime / (name + '.dll'))
entry = E.SubElement(items, 'Reference', Include='LiteNetLib')
E.SubElement(entry, 'HintPath').text = str(root / 'Dependencies/Phinix.Common/libs/netstandard2.0/LiteNetLib.dll')
E.ElementTree(project).write(out / 'Probe.csproj', encoding='unicode')
(out / 'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
env = dict(os.environ)
env['PHINIX_TEST_ROOT'] = str(root)
with (out / 'test.log').open('w') as log:
    result = subprocess.run(['dotnet', 'run', '--project', str(out / 'Probe.csproj'), '-c', 'Release'], env=env, stdout=log, stderr=subprocess.STDOUT, timeout=180)
print((out / 'test.log').read_text(), end='')
raise SystemExit(result.returncode)
