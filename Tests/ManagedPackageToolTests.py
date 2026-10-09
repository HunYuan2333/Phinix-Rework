"""Exercise the built CLI using real managed fixtures; no network or game execution."""
import json
from pathlib import Path
import subprocess
import tempfile
import zipfile

root = Path(__file__).resolve().parents[1]
tool = root / 'Extensions/PluginStore/Tools/ManagedPackageTool/bin/Release/net10.0/ManagedPackageTool.dll'
fixtures = root / 'Tests/ManagedExtensionRuntimeTests/bin/Release/net10.0/Fixtures'
count = 0

def run(args, success=True):
    global count
    result = subprocess.run(['dotnet', str(tool)] + args, text=True, capture_output=True)
    assert (result.returncode == 0) == success, result.stdout + result.stderr
    count += 1
    return result

with tempfile.TemporaryDirectory(prefix='phinix-packager-') as folder:
    work = Path(folder)
    config = {
        'compatibility': {'rimWorldVersions': ['1.6'], 'phinixRange': '>=0.9.7 <1.0.0', 'abstractionsRange': '>=1.4.0 <2.0.0'},
        'dependencies': [{'packageId': 'test.provider', 'versionRange': '>=1.0.0 <2.0.0', 'optional': False}],
        'externalMods': [{'packageId': 'test.external'}],
    }
    config_path = work / 'config.json'
    config_path.write_text(json.dumps(config), encoding='utf-8')
    archive = work / 'valid.zip'
    args = ['--assembly', str(fixtures / 'Fixture.Managed.Plugin.dll'), '--assembly', str(fixtures / 'Fixture.Managed.Helper.dll'),
            '--package-id', 'test.package', '--name', 'Tool test', '--version', '1.0.0', '--config', str(config_path), '--output', str(archive)]
    run(args)
    with zipfile.ZipFile(archive) as zip_file:
        manifest = json.loads(zip_file.read('manifest.json'))
    assert manifest['compatibility'] == config['compatibility'] and manifest['dependencies'] == config['dependencies'] and manifest['externalMods'] == config['externalMods']
    count += 1
    report = json.loads(run(['--validate', str(archive)]).stdout)
    assert report['staticValidation'] == 'passed' and report['hostReferencesChecked'] is False and report['dependencies'] == config['dependencies']
    count += 1
    run(['--validate', str(archive), '--host-assembly', str(fixtures / 'Fixture.Managed.Plugin.dll')], success=False)
    bad = work / 'bad.zip'
    bad.write_bytes(b'not a zip')
    assert 'InvalidArchive' in run(['--validate', str(bad)], success=False).stderr
    count += 1
    with zipfile.ZipFile(work / 'unsafe.zip', 'w') as zip_file:
        zip_file.writestr('../escape', b'invalid')
    assert 'UnsafeArchivePath' in run(['--validate', str(work / 'unsafe.zip')], success=False).stderr
    count += 1
    for field, value in [('unknown', True), ('dependencies', [{'packageId': 'test.provider', 'versionRange': 'invalid', 'optional': False}]), ('externalMods', [{'packageId': '../unsafe'}])]:
        changed = dict(config)
        changed[field] = value
        config_path.write_text(json.dumps(changed), encoding='utf-8')
        target = work / (field + '.zip')
        run(args[:-1] + [str(target)], success=False)
        assert not target.exists()
        count += 1
    config_path.write_text('{"compatibility":{},"compatibility":{}}', encoding='utf-8')
    run(args[:-1] + [str(work / 'duplicate.zip')], success=False)
print(f'Managed package CLI passed: {count} checks.')
