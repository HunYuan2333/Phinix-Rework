"""Run production theme registration/catalog persistence against stub drawing, outside original bin/obj."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import shutil
import xml.etree.ElementTree as ET

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--output', type=Path, required=True)
args = parser.parse_args()
source = Path(__file__).resolve().parent
repo = source.parents[1]
output = args.output.resolve()
workspace = repo.parent
if output.is_relative_to(workspace) or workspace.is_relative_to(output):
    parser.error('Use a directory outside the workspace.')
if output.exists() and any(output.iterdir()):
    parser.error('Use a fresh directory to preserve prior results.')
utils = repo/'Output/phinix-rework/Common/Assemblies/03-Utils.dll'
if not utils.is_file():
    parser.error('The preserved Utils baseline is required.')
output.mkdir(parents=True, exist_ok=True)
reference_dir = output/'references'
reference_dir.mkdir()
protobuf = repo/'Output/phinix-rework/Common/Assemblies/02-Protobuf.dll'
shutil.copy2(utils, reference_dir/'Utils.dll')
shutil.copy2(protobuf, reference_dir/'Protobuf.dll')
project = ET.parse(source/'ThemeRuntimeTests.csproj')
root = project.getroot()
for group in root.findall('ItemGroup'):
    for reference in group.findall('ProjectReference'):
        group.remove(reference)
for item in root.iter('Compile'):
    item.set('Include', str((source/item.get('Include')).resolve()))
items = ET.SubElement(root, 'ItemGroup')
reference = ET.SubElement(items, 'Reference', Include='Utils')
ET.SubElement(reference, 'HintPath').text = str(reference_dir/'Utils.dll')
protobuf_reference = ET.SubElement(items, 'Reference', Include='Protobuf')
ET.SubElement(protobuf_reference, 'HintPath').text = str(reference_dir/'Protobuf.dll')
for path in sorted(source.glob('*.cs')):
    ET.SubElement(items, 'Compile', Include=str(path))
properties = root.find('PropertyGroup')
ET.SubElement(properties, 'EnableDefaultCompileItems').text = 'false'
ET.SubElement(properties, 'NuGetAudit').text = 'false'
project.write(output/'ThemeRuntimeTests.csproj', encoding='unicode')
paths = [source/'ThemeRuntimeTests.csproj', source/'run-isolated.py', utils, protobuf]
paths += [Path(item.get('Include')) for item in root.iter('Compile')]
(output/'inputs.json').write_text(json.dumps([{'path': str(p), 'sha256': hashlib.sha256(p.read_bytes()).hexdigest()} for p in paths], indent=2))
with (output/'results.log').open('w') as log:
    result = subprocess.run(['dotnet', 'run', '--project', str(output/'ThemeRuntimeTests.csproj'), '-c', 'Release'], stdout=log, stderr=subprocess.STDOUT)
print('Theme source/stub drawing probe exit:', result.returncode, 'log:', output/'results.log')
sys.exit(result.returncode)
