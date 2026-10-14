"""Run the existing window boundary checks against stubs, with all output isolated."""
import argparse
from pathlib import Path
import subprocess
import sys
import xml.etree.ElementTree as ET

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--output', type=Path, required=True)
args = parser.parse_args()
source = Path(__file__).resolve().parent
output = args.output.resolve()
workspace = source.parents[1].parent
if output.is_relative_to(workspace) or workspace.is_relative_to(output):
    parser.error('Use an isolated output directory outside the workspace.')
if output.exists() and any(output.iterdir()):
    parser.error('Use a fresh output directory to preserve previous checks.')
output.mkdir(parents=True, exist_ok=True)
project = ET.parse(source/'WindowBoundaryRuntimeTests.csproj')
root = project.getroot()
for item in root.iter('Compile'):
    item.set('Include', str((source/item.get('Include')).resolve()))
items = ET.SubElement(root, 'ItemGroup')
for path in sorted(source.glob('*.cs')):
    ET.SubElement(items, 'Compile', Include=str(path))
properties = root.find('PropertyGroup')
ET.SubElement(properties, 'EnableDefaultCompileItems').text = 'false'
ET.SubElement(properties, 'NuGetAudit').text = 'false'
project.write(output/'WindowBoundaryRuntimeTests.csproj', encoding='unicode')
with (output/'results.log').open('w') as log:
    result = subprocess.run(['dotnet', 'run', '--project', str(output/'WindowBoundaryRuntimeTests.csproj'), '-c', 'Release'], stdout=log, stderr=subprocess.STDOUT)
print('Stub window probe exit:', result.returncode, 'log:', output/'results.log')
sys.exit(result.returncode)
