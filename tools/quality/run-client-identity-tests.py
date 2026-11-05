"""Isolated SV-45 file/kill tests; optionally test the actual built Authentication.Client assembly."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import signal
import subprocess
import xml.etree.ElementTree as ET

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--output', type=Path, required=True)
parser.add_argument('--authentication-assembly', type=Path)
args = parser.parse_args()
repo = Path(__file__).resolve().parents[2]
out = args.output.resolve()
if out.is_relative_to(repo) or repo.is_relative_to(out) or (out.exists() and any(out.iterdir())):
    parser.error('Use a fresh isolated output directory.')
out.mkdir(parents=True, exist_ok=True)
test = repo / 'Tests/ClientIdentityCredentialTests/Program.cs'
product = repo / 'Client/Common/Authentication.Client/ServerIdentityCredentials.cs'
project = ET.Element('Project', Sdk='Microsoft.NET.Sdk')
properties = ET.SubElement(project, 'PropertyGroup')
for key, value in {'TargetFramework': 'net10.0', 'OutputType': 'Exe', 'AssemblyName': 'ClientIdentityCredentialTests',
                   'EnableDefaultCompileItems': 'false', 'ImplicitUsings': 'enable', 'Nullable': 'enable',
                   'DefineConstants': 'PHINIX_RIMWORLD_17', 'NuGetAudit': 'false'}.items():
    ET.SubElement(properties, key).text = value
items = ET.SubElement(project, 'ItemGroup')
ET.SubElement(items, 'Compile', Include=str(test))
inputs = [test, product, Path(__file__)]
if args.authentication_assembly:
    assembly = args.authentication_assembly.resolve(strict=True)
    ET.SubElement(ET.SubElement(items, 'Reference', Include='Authentication.Client'), 'HintPath').text = str(assembly)
    inputs.append(assembly)
else:
    ET.SubElement(items, 'Compile', Include=str(product))
ET.ElementTree(project).write(out / 'Tests.csproj', encoding='unicode')
(out / 'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
commands = [('build', ['dotnet', 'build', str(out / 'Tests.csproj'), '-c', 'Release', '-m:1', '-nr:false']),
            ('test', ['dotnet', str(out / 'bin/Release/net10.0/ClientIdentityCredentialTests.dll')])]
results = []
for phase, command in commands:
    with (out / (phase + '.log')).open('w') as log:
        process = subprocess.Popen(command, cwd=repo, stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
        timed_out = False
        try:
            process.wait(timeout=120 if phase == 'build' else 60)
        except BaseException as error:
            os.killpg(process.pid, signal.SIGKILL)
            process.wait()
            if not isinstance(error, subprocess.TimeoutExpired):
                raise
            timed_out = True
    results.append({'phase': phase, 'command': command, 'exit': process.returncode, 'timedOut': timed_out})
    if process.returncode or timed_out:
        break
passed = len(results) == 2 and all(row['exit'] == 0 and not row['timedOut'] for row in results)
test_result = None
if passed:
    test_result = json.loads((out / 'test.log').read_text())
    passed = test_result.get('passed') is True
manifest = {'passed': passed, 'results': results, 'testResult': test_result, 'actualAssembly': bool(args.authentication_assembly),
            'inputs': {str(path): hashlib.sha256(path.read_bytes()).hexdigest() for path in inputs}}
(out / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
print(json.dumps({'passed': passed, 'actualAssembly': bool(args.authentication_assembly), 'manifest': str(out / 'manifest.json')}))
raise SystemExit(0 if passed else 1)
