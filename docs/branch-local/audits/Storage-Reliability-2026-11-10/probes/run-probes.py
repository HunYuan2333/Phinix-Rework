#!/usr/bin/env python3
"""Compile the current production operation owner with synthetic boundaries; no game data."""
from pathlib import Path
import argparse
import hashlib
import json
import shutil
import subprocess
import xml.etree.ElementTree as E

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('--output', type=Path, required=True)
a = p.parse_args()
here = Path(__file__).resolve().parent
root = here.parents[4]
out = a.output.resolve()
if out.exists() or out.is_relative_to(root.parent):
    p.error('Use a fresh output outside the workspace.')
out.mkdir(parents=True)
source = root / 'Extensions/Trade/Client/TradeOfferOperations.cs'
shutil.copy(here / 'Probe.cs', out / 'Probe.cs')
project = E.Element('Project', Sdk='Microsoft.NET.Sdk')
props = E.SubElement(project, 'PropertyGroup')
for k, v in {'TargetFramework': 'net10.0', 'OutputType': 'Exe', 'NuGetAudit': 'false'}.items():
    E.SubElement(props, k).text = v
E.SubElement(E.SubElement(project, 'ItemGroup'), 'Compile', Include=str(source))
E.ElementTree(project).write(out / 'Probe.csproj', encoding='unicode')
(out / 'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
with (out / 'result.txt').open('w') as log:
    result = subprocess.run(['dotnet', 'run', '--project', str(out / 'Probe.csproj'), '-c', 'Release'],
                            stdout=log, stderr=subprocess.STDOUT, timeout=120)
(out / 'manifest.json').write_text(json.dumps({'exit': result.returncode,
    'source': str(source), 'source_sha256': hashlib.sha256(source.read_bytes()).hexdigest(),
    'synthetic': True, 'realGameValidated': False}, indent=2) + '\n')
print((out / 'result.txt').read_text())
raise SystemExit(result.returncode)
