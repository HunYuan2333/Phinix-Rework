#!/usr/bin/env python3
"""Build/run production error reporting boundaries on net10 and net472/Mono, outside the source tree."""
import argparse
import os
import json
import subprocess
import xml.etree.ElementTree as E
from pathlib import Path

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('--output', type=Path, required=True)
a = p.parse_args()
root = Path(__file__).resolve().parents[2]
out = a.output.resolve()
if out.exists() or out.is_relative_to(root.parent):
    p.error('Use a fresh isolated output.')
out.mkdir(parents=True)
source = root / 'Tests/ClientErrorRuntimeTests/ClientErrorRuntimeTests.csproj'
includes = list(source.parent.glob('*.cs')) + [
    (source.parent / item.attrib['Include']).resolve()
    for item in E.parse(source).iter('Compile')
]
results = []
for framework in ['net10.0', 'net472']:
    target = out / framework
    target.mkdir()
    project = E.Element('Project', Sdk='Microsoft.NET.Sdk')
    props = E.SubElement(project, 'PropertyGroup')
    for key, value in {'TargetFramework': framework, 'OutputType': 'Exe', 'LangVersion': '7.3',
                       'EnableDefaultCompileItems': 'false', 'NuGetAudit': 'false'}.items():
        E.SubElement(props, key).text = value
    items = E.SubElement(project, 'ItemGroup')
    for path in includes:
        E.SubElement(items, 'Compile', Include=str(path))
    E.ElementTree(project).write(target / 'Probe.csproj', encoding='unicode')
    (target / 'NuGet.Config').write_text('<configuration><packageSources><clear /><add key="local" value="' + str(Path.home() / '.nuget/packages') + '" /></packageSources></configuration>')
    for phase, cmd in [
        ('build', ['dotnet', 'build', str(target / 'Probe.csproj'), '-c', 'Release', '-m:1', '-nr:false']),
        ('test', (['dotnet'] if framework == 'net10.0' else ['mono']) + [str(target / 'bin/Release' / framework / ('Probe.dll' if framework == 'net10.0' else 'Probe.exe'))])
    ]:
        with (target / (phase + '.log')).open('w') as log:
            result = subprocess.run(cmd, stdout=log, stderr=subprocess.STDOUT, timeout=180,env=dict(os.environ,PHINIX_ERROR_SAMPLE_DIRECTORY=str(target / "reports")))
        results.append({'framework': framework, 'phase': phase, 'exit': result.returncode})
        print(framework, phase, result.returncode, flush=True)
        if result.returncode:
            print((target / (phase + '.log')).read_text()[-6000:])
            break
        if phase == 'test':
            print((target / 'test.log').read_text(), end='', flush=True)
(out / 'results.json').write_text(json.dumps(results, indent=2) + '\n')
raise SystemExit(0 if len(results) == 4 and all(row['exit'] == 0 for row in results) else 1)
