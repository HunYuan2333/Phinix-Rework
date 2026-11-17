#!/usr/bin/env python3
"""Exercise real shared SDK/loader/registry and compiled Host pipeline boundaries, without launching the game."""
import argparse
import json
import subprocess
import xml.etree.ElementTree as E
from pathlib import Path

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('--output', type=Path, required=True)
for key in ['runtime16', 'runtime17', 'game16', 'game17']:
    p.add_argument('--' + key, type=Path, required=True)
a = p.parse_args()
root = Path(__file__).resolve().parents[2]
out = a.output.resolve()
if out.exists() or out.is_relative_to(root.parent):
    p.error('Use fresh isolated output.')
out.mkdir(parents=True)
results = []
for framework, runtime, game in [('net10.0', a.runtime17.resolve(), a.game17.resolve()), ('net472', a.runtime16.resolve(), a.game16.resolve())]:
    if not (runtime / 'PhinixClient.dll').is_file():
        p.error('Build the actual Host graph first: ' + str(runtime))
    target = out / framework
    target.mkdir()
    project = E.Element('Project', Sdk='Microsoft.NET.Sdk')
    props = E.SubElement(project, 'PropertyGroup')
    for k, v in {'TargetFramework': framework, 'OutputType': 'Exe', 'EnableDefaultCompileItems': 'false', 'LangVersion': '7.3', 'NuGetAudit': 'false'}.items():
        E.SubElement(props, k).text = v
    items = E.SubElement(project, 'ItemGroup')
    for source in ['Tests/ClientErrorBoundaryRuntimeTests/Program.cs', 'Tests/ClientErrorBoundaryRuntimeTests/ConnectionBoundaries.cs', 'Tests/ClientErrorBoundaryRuntimeTests/ControlBoundaries.cs', 'Tests/ClientErrorBoundaryRuntimeTests/SettingsBoundaries.cs', 'Client/Source/UI/NicknameEditState.cs', 'Client/Source/Framework/ClientErrorService.cs']:
        E.SubElement(items, 'Compile', Include=str(root / source))
    for name in ['Utils', 'ClientExtensionAbstractions', 'Google.Protobuf', 'Connections.Client', 'Connections', 'Authentication.Client', 'Authentication', 'UserManagement']:
        reference = E.SubElement(items, 'Reference', Include=name)
        E.SubElement(reference, 'HintPath').text = str(runtime / (name + '.dll'))
    pinned = root / 'Dependencies/Phinix.Common/libs' / ('netstandard2.0' if framework == 'net10.0' else 'net35') / 'LiteNetLib.dll'
    reference = E.SubElement(items, 'Reference', Include='LiteNetLib')
    E.SubElement(reference, 'HintPath').text = str(pinned)
    E.ElementTree(project).write(target / 'Probe.csproj', encoding='unicode')
    (target / 'NuGet.Config').write_text('<configuration><packageSources><clear /><add key="local" value="' + str(Path.home() / '.nuget/packages') + '" /></packageSources></configuration>')
    for phase, cmd in [
        ('build', ['dotnet', 'build', str(target / 'Probe.csproj'), '-c', 'Release', '-m:1', '-nr:false']),
        ('test', (['dotnet'] if framework == 'net10.0' else ['mono']) + [str(target / 'bin/Release' / framework / ('Probe.dll' if framework == 'net10.0' else 'Probe.exe')), str(runtime), str(game)])
    ]:
        with (target / (phase + '.log')).open('w') as log:
            run = subprocess.run(cmd, stdout=log, stderr=subprocess.STDOUT, timeout=180)
        results.append({'framework': framework, 'phase': phase, 'exit': run.returncode})
        print(framework, phase, run.returncode, flush=True)
        if run.returncode:
            print((target / (phase + '.log')).read_text()[-6000:])
            break
        if phase == 'test':
            print((target / 'test.log').read_text(), end='', flush=True)
(out / 'results.json').write_text(json.dumps(results, indent=2) + '\n')
raise SystemExit(0 if len(results) == 4 and all(row['exit'] == 0 for row in results) else 1)
