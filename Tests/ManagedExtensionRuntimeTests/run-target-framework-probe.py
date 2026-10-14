"""Build a real net10 fixture and exercise inspection, preflight and package CLI."""
import argparse
import json
import subprocess
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path

p = argparse.ArgumentParser(description=__doc__)
for name in ['utils', 'test-assembly', 'package-tool', 'sdk16', 'output']:
    p.add_argument('--' + name, type=Path, required=True)
a = p.parse_args()
repo = Path(__file__).resolve().parents[2]
out = a.output.resolve()
if out.is_relative_to(repo) or any(path.resolve().is_relative_to(out) for path in
                                  [a.utils, a.test_assembly, a.package_tool, a.sdk16]):
    p.error('Use output isolated from source and input assemblies.')
if out.exists() and any(out.iterdir()):
    p.error('Choose fresh output.')
out.mkdir(parents=True, exist_ok=True)
(out / 'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
pr = ET.Element('Project', Sdk='Microsoft.NET.Sdk')
props = ET.SubElement(pr, 'PropertyGroup')
for key, value in {'TargetFramework': 'net10.0', 'AssemblyName': 'Fixture.Managed.Provider',
                   'EnableDefaultCompileItems': 'false', 'NuGetAudit': 'false'}.items():
    ET.SubElement(props, key).text = value
items = ET.SubElement(pr, 'ItemGroup')
ET.SubElement(items, 'Compile', Include=str(repo / 'Tests/ManagedExtensionRuntimeTests/Fixtures/Provider/Provider.cs'))
ref = ET.SubElement(items, 'Reference', Include='Utils')
ET.SubElement(ref, 'HintPath').text = str(a.utils.resolve())
ET.SubElement(ref, 'Private').text = 'false'
ET.ElementTree(pr).write(out / 'Fixture.csproj', encoding='unicode')
results = []


def run(label, cmd, expected=0, contains=None):
    result = subprocess.run(cmd, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    (out / (label + '.log')).write_text(result.stdout)
    results.append({'check': label, 'exit': result.returncode, 'command': cmd})
    (out / 'results.json').write_text(json.dumps(results, indent=2))
    if (result.returncode == 0) != (expected == 0) or contains and contains not in result.stdout:
        raise RuntimeError(label + ': unexpected outcome; see log')


run('fixture-build', ['dotnet', 'build', str(out / 'Fixture.csproj'), '-c', 'Release', '--nologo'])
fixture = out / 'bin/Release/net10.0/Fixture.Managed.Provider.dll'
run('real-tfm-preflight', ['dotnet', str(a.test_assembly.resolve()), 'target-framework', str(fixture)], contains='assertions passed')
config = {'compatibility': {'rimWorldVersions': ['1.7'], 'phinixRange': '>=0.9.7 <1.0.0',
                            'abstractionsRange': '>=1.3.0 <2.0.0'}}
(out / 'config17.json').write_text(json.dumps(config))
package = out / 'net10.zip'
base = ['dotnet', str(a.package_tool.resolve()), '--assembly', str(fixture),
        '--package-id', 'test.provider', '--name', 'TFM probe', '--version', '1.0.0']
run('pack-net10', base + ['--target-framework', 'net10.0', '--config', str(out / 'config17.json'), '--output', str(package)])
with zipfile.ZipFile(package) as archive:
    manifest = json.loads(archive.read('manifest.json'))
    assert manifest['targetFramework'] == 'net10.0'
    assert archive.testzip() is None
run('validate-17', ['dotnet', str(a.package_tool.resolve()), '--validate', str(package), '--game-version', '1.7'], contains='"targetFramework":"net10.0"')
run('reject-16', ['dotnet', str(a.package_tool.resolve()), '--validate', str(package), '--game-version', '1.6'], 1, 'incompatible')
run('reject-unknown', ['dotnet', str(a.package_tool.resolve()), '--validate', str(package), '--game-version', '1.8'], 1, 'incompatible')
run('reject-profile', base + ['--target-framework', 'net472', '--config', str(out / 'config17.json'), '--output', str(out / 'bad-profile.zip')], 1, 'do not match')
config['compatibility']['rimWorldVersions'] = ['1.6']
(out / 'config16.json').write_text(json.dumps(config))
run('reject-false-tfm', base + ['--config', str(out / 'config16.json'), '--output', str(out / 'bad-tfm.zip')], 1, 'AssemblyTargetFrameworkMismatch')
helper = a.test_assembly.resolve().parent / 'Fixtures/Fixture.Managed.Helper.dll'
run('reject-mixed-tfm', base + ['--assembly', str(helper), '--target-framework', 'net10.0', '--config', str(out / 'config17.json'), '--output', str(out / 'mixed.zip')], 1, 'AssemblyTargetFrameworkMismatch')
run('reject-mixed-sdk', base + ['--host-assembly', str(a.sdk16.resolve()), '--target-framework', 'net10.0', '--config', str(out / 'config17.json'), '--output', str(out / 'mixed-sdk.zip')], 1, 'client SDK target framework differs')
run('reject-unsupported-tfm', base + ['--target-framework', 'net9.0', '--config', str(out / 'config17.json'), '--output', str(out / 'unsupported.zip')], 1, 'UnsupportedTargetFramework')
assert not any((out / name).exists() for name in ['bad-profile.zip', 'bad-tfm.zip', 'mixed.zip', 'mixed-sdk.zip', 'unsupported.zip'])
print('Managed TFM probe:', len(results), 'commands verified; ZIP CRC/manifest and rejection-before-write verified. Logs:', out)
