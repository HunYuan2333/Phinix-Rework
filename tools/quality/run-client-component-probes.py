"""Run existing client component probes against an explicit built DLL closure.
Game DLLs are compile/runtime references in an isolated output, never a release.
"""
import argparse
import hashlib
import json
import subprocess
import sys
import xml.etree.ElementTree as E
from pathlib import Path

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('--assemblies', type=Path, required=True)
p.add_argument('--game', type=Path, required=True)
p.add_argument('--output', type=Path, required=True)
p.add_argument('--harmony', type=Path, help='Explicit compile/runtime Harmony reference for constructor IL inspection')
p.add_argument('--suite', choices=['trade-composition', 'legacy-trade', 'inventory-runtime', 'inventory-composition', 'inventory-checkpoint', 'store-composition', 'store-usability', 'theme-composition', 'transport-lifetime', 'credential-lifetime', 'queue-completion', 'chat-regression'], required=True)
a = p.parse_args()
r = Path(__file__).resolve().parents[2]
out, assemblies, game = a.output.resolve(), a.assemblies.resolve(), a.game.resolve()
if out.is_relative_to(r.parent) or out.is_relative_to(game) or game.is_relative_to(out) or out.is_relative_to(assemblies) or assemblies.is_relative_to(out):
    p.error('Use a separate isolated output outside the source workspace, assemblies and game references.')
if out.exists() and any(out.iterdir()):
    p.error('Use a fresh output.')
suites = {
    'trade-composition': ('TradeCompositionRuntimeTests', 'Tests/TradeCompositionRuntimeTests'),
    'legacy-trade': ('LegacyTradeRuntimeTests', 'Tests/LegacyTradeRuntimeTests'),
    'chat-regression': ('ChatRegressionTests', 'Tests/ChatRegressionTests'),
    'queue-completion': ('QueueCompletionRuntimeTests', 'Tests/QueueCompletionRuntimeTests'),
    'credential-lifetime': ('ClientCredentialLifetimeTests', 'Tests/ClientCredentialLifetimeTests'),
    'transport-lifetime': ('ClientTransportLifetimeTests', 'Tests/ClientTransportLifetimeTests'),
    'theme-composition': ('ThemePresentationCompositionRuntimeTests', 'Tests/ThemePresentationCompositionRuntimeTests'),
    'store-composition': ('StoreCompositionRuntimeTests', 'Tests/StoreCompositionRuntimeTests'),
    'store-usability': ('StoreUsabilityRuntimeTests', 'Tests/StoreUsabilityRuntimeTests'),
    'inventory-checkpoint': ('InventoryCheckpointIntegrationTests', 'Tests/InventoryCheckpointIntegrationTests'),
    'inventory-runtime': ('InventoryRuntimeTests', 'Tests/InventoryRuntimeTests'),
    'inventory-composition': ('InventoryCompositionRuntimeTests', 'Tests/InventoryCompositionRuntimeTests'),
}
name, directory = suites[a.suite]
files = sorted((r / directory).glob('*.cs'))
refs = sorted(assemblies.glob('*.dll'))
# Explicit game reference allowlist: never reference its System.* framework DLLs.
refs += [game / f for f in ['Assembly-CSharp.dll', 'UnityStubs.dll', 'UnityEngine.dll', 'UnityEngine.SharedInternalsModule.dll', 'UnityEngine.ImageConversionModule.dll', 'UnityEngine.CoreModule.dll', 'UnityEngine.IMGUIModule.dll', 'UnityEngine.InputLegacyModule.dll', 'UnityEngine.TextRenderingModule.dll', 'Waylith.dll', 'Compat.dll', 'com.rlabrecque.steamworks.net.dll', '0Harmony.dll'] if (game / f).is_file()]
if not files or not refs or not (assemblies / 'TradeExtension.Client.dll').is_file():
    p.error('Missing sources or built client closure.')
if a.harmony:
    if not a.harmony.is_file():
        p.error('Missing explicit Harmony reference.')
    refs.append(a.harmony.resolve())
# Reject duplicate identities represented by the same filename from two directories.
by_name = {}
for f in refs:
    if f.name in by_name and by_name[f.name].read_bytes() != f.read_bytes():
        p.error('Conflicting reference: ' + f.name)
    by_name[f.name] = f
refs = sorted(by_name.values())
out.mkdir(parents=True, exist_ok=True)
project = E.Element('Project', Sdk='Microsoft.NET.Sdk')
props = E.SubElement(project, 'PropertyGroup')
for k, v in {'TargetFramework': 'net10.0', 'OutputType': 'Exe', 'AssemblyName': name, 'EnableDefaultCompileItems': 'false', 'NuGetAudit': 'false', 'LangVersion': 'latest'}.items():
    E.SubElement(props, k).text = v
items = E.SubElement(project, 'ItemGroup')
for f in files:
    E.SubElement(items, 'Compile', Include=str(f))
for f in refs:
    node = E.SubElement(items, 'Reference', Include=f.stem)
    E.SubElement(node, 'HintPath').text = str(f)
    E.SubElement(node, 'Private').text = 'true'
path = out / 'Probe.csproj'
E.ElementTree(project).write(path, encoding='unicode')
(out / 'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
commands = [['dotnet', 'build', str(path), '-c', 'Release', '-m:1', '-nr:false', '-p:BuildInParallel=false', '-p:NuGetAudit=false'], ['dotnet', str(out / 'bin/Release/net10.0' / (name + '.dll'))]]
if a.suite == 'store-usability':
    commands[1].extend([str(assemblies),str(r)])
if a.suite == 'trade-wire':
    commands[1].append(str(out / 'wire-fixtures'))
results = []
for phase, cmd in zip(['build', 'test'], commands):
    with (out / (phase + '.log')).open('w') as log:
        run = subprocess.run(cmd, stdout=log, stderr=subprocess.STDOUT, timeout=180)
    results.append({'phase': phase, 'command': cmd, 'exit': run.returncode})
    if run.returncode:
        break
manifest = {'suite': a.suite, 'results': results, 'passed': len(results) == 2 and all(x['exit'] == 0 for x in results), 'inputs': [{'path': str(f), 'sha256': hashlib.sha256(f.read_bytes()).hexdigest()} for f in files + refs], 'realGameValidated': False}
(out / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
print(json.dumps({'suite': a.suite, 'passed': manifest['passed'], 'output': str(out)}))
sys.exit(0 if manifest['passed'] else 1)
