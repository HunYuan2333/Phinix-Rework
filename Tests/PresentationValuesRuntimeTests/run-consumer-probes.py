"""Run current plugin reflection/Store probes against an already isolated joint build."""
import argparse
from pathlib import Path
import subprocess
import xml.etree.ElementTree as ET
import json

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--references', type=Path, required=True)
parser.add_argument('--output', type=Path, required=True)
args = parser.parse_args()
repo = Path(__file__).resolve().parents[2]
workspace = repo.parent
references = args.references.resolve()
output = args.output.resolve()
if output.is_relative_to(workspace) or workspace.is_relative_to(output) or output == references or output.is_relative_to(references):
    parser.error('Use fresh output outside workspace and isolated references.')
if output.exists() and any(output.iterdir()):
    parser.error('Existing probe output is preserved; choose a fresh directory.')
for name in ('ClientPresentation.Contracts', 'ClientExtensionAbstractions', 'LegacyRedPacketExtension.Client', 'LegacyTalentTradeExtension.Client', 'PluginStore.Client'):
    if not (references/(name+'.dll')).is_file():
        parser.error('Missing jointly built reference: '+name)
output.mkdir(parents=True, exist_ok=True)
sources = [workspace/'Phinix-Legacy-RedPacket/Tests/RedPacketPresentationRuntimeTests', workspace/'Phinix-Legacy-TalentTrade/Tests/TalentPresentationRuntimeTests', repo/'Tests/StoreCompositionRuntimeTests']
results = []
for source in sources:
    name = source.name
    destination = output/name
    destination.mkdir()
    project = ET.Element('Project', Sdk='Microsoft.NET.Sdk')
    props = ET.SubElement(project, 'PropertyGroup')
    for key, value in {'TargetFramework':'net10.0', 'OutputType':'Exe', 'EnableDefaultCompileItems':'false', 'NuGetAudit':'false', 'AssemblyName':name}.items():
        ET.SubElement(props, key).text = value
    items = ET.SubElement(project, 'ItemGroup')
    for path in sorted(source.glob('*.cs')):
        ET.SubElement(items, 'Compile', Include=str(path))
    for path in sorted(references.glob('*.dll')):
        if path.name[0].isdigit() or path.name.startswith('System.'):
            continue
        ref = ET.SubElement(items, 'Reference', Include=path.stem)
        ET.SubElement(ref, 'HintPath').text = str(path)
        ET.SubElement(ref, 'Private').text = 'false'
    project_path = destination/(name+'.csproj')
    ET.ElementTree(project).write(project_path, encoding='unicode')
    (destination/'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
    with (destination/'build.log').open('w') as log:
        build = subprocess.run(['dotnet','build',str(project_path),'-c','Release','-v:q'],stdout=log,stderr=subprocess.STDOUT)
    print(name, 'compile exit', build.returncode, flush=True)
    result = {'name':name, 'compile_exit':build.returncode}
    if build.returncode == 0:
        run = destination/'bin/Release/net10.0'
        # Resolve the jointly built DLLs from this probe directory, like the main isolated harness.
        (run/(name+'.deps.json')).unlink(missing_ok=True)
        for path in references.glob('*.dll'):
            target = run/path.name
            if not target.exists():
                target.symlink_to(path)
        with (destination/'results.log').open('w') as log:
            execution = subprocess.run(['dotnet',str(run/(name+'.dll'))],cwd=run,stdout=log,stderr=subprocess.STDOUT)
        result['test_exit'] = execution.returncode
        print(name, 'test exit', execution.returncode, flush=True)
    results.append(result)
(output/'verification.json').write_text(json.dumps(results, indent=2))
raise SystemExit(0 if all(r.get('test_exit') == 0 for r in results) else 1)
