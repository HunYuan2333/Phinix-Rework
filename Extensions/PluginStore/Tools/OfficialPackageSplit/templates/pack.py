#!/usr/bin/env python3
"""Build against compile-only references, then use the trusted Phinix packager."""
import argparse
import json
from pathlib import Path
import subprocess
import zipfile


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--phinix-package', type=Path, required=True)
    parser.add_argument('--game-references', type=Path, required=True)
    parser.add_argument('--harmony-references', type=Path)
    parser.add_argument('--packager', type=Path, required=True, help='ManagedPackageTool.dll, built from trusted Phinix source')
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--bundle-output', type=Path)
    parser.add_argument('--display-output', type=Path)
    args = parser.parse_args()
    root = Path(__file__).resolve().parent
    metadata = json.loads((root / 'publication.json').read_text())
    host = args.phinix_package.resolve()
    game = args.game_references.resolve()
    output = args.output.resolve()
    if output.exists() or (args.bundle_output and args.bundle_output.exists()) or (args.display_output and args.display_output.exists()):
        raise ValueError('Immutable output already exists; choose a new path/version.')
    project = root / 'Client' / (metadata['clientAssembly'] + '.csproj')
    build = ['dotnet', 'build', str(project), '-c', 'Release', '-m:1', '-p:BuildInParallel=false',
             '-p:PhinixPackage=' + str(host), '-p:GameReferences=' + str(game)]
    if args.harmony_references:
        build += ['-p:HarmonyReferences=' + str(args.harmony_references.resolve())]
    subprocess.run(build, check=True)
    command = ['dotnet', str(args.packager.resolve()), '--package-id', metadata['packageId'],
               '--name', metadata['name'], '--version', metadata['version'], '--output', str(output)]
    for part, key in [('Contracts', 'contractAssembly'), ('Client', 'clientAssembly')]:
        command += ['--assembly', str(root / part / 'bin/Release/net472' / (metadata[key] + '.dll'))]
    for path in sorted((root / 'Client/Resources').rglob('*.json')):
        command += ['--language-file', str(path)]
    # Only identities are read. None of these references are copied into the ZIP.
    references = list((host / 'Common/Assemblies').glob('*.dll')) + list((host / 'Common/Extensions').glob('*.dll'))
    references += list(game.glob('*.dll'))
    if args.harmony_references:
        references += [args.harmony_references.resolve() / '0Harmony.dll']
    for path in sorted(set(references)):
        command += ['--host-assembly', str(path)]
    if args.bundle_output:
        command += ['--bundle-output', str(args.bundle_output.resolve())]
    if args.display_output:
        command += ['--display-output', str(args.display_output.resolve())]
    subprocess.run(command, check=True)
    with zipfile.ZipFile(output) as archive:
        expected = {'manifest.json'} | {'Assemblies/' + metadata[key] + '.dll' for key in ('clientAssembly', 'contractAssembly')}
        expected |= {'Resources/Localization/' + path.name for path in (root / 'Client/Resources').rglob('*.json')}
        if set(archive.namelist()) != expected:
            raise ValueError('Unexpected package contents; do not publish this candidate.')
    print(json.dumps({'event': 'package.candidate_verified', 'packageId': metadata['packageId'], 'output': str(output)}))


if __name__ == '__main__':
    main()
