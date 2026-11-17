#!/usr/bin/env python3
"""Run the existing presentation source suite with explicit shared DLLs, error contracts and game fakes."""
import argparse,json,subprocess,xml.etree.ElementTree as E
from pathlib import Path
p=argparse.ArgumentParser(description=__doc__);p.add_argument('--output',type=Path,required=True);p.add_argument('--runtime17',type=Path,required=True);a=p.parse_args()
root=Path(__file__).resolve().parents[2];out=a.output.resolve();runtime=a.runtime17.resolve();source=root/'Tests/PresentationRuntimeTests/PresentationRuntimeTests.csproj'
if out.exists() or out.is_relative_to(root.parent):p.error('Use fresh isolated output.')
out.mkdir(parents=True);project=E.Element('Project',Sdk='Microsoft.NET.Sdk');props=E.SubElement(project,'PropertyGroup')
for key,value in {'TargetFramework':'net10.0','OutputType':'Exe','EnableDefaultCompileItems':'false','NuGetAudit':'false','ImplicitUsings':'disable','NoWarn':'CS0436'}.items():E.SubElement(props,key).text=value
items=E.SubElement(project,'ItemGroup')
for file in list(source.parent.glob('*.cs'))+[(source.parent/e.attrib['Include'].replace('\\','/')).resolve() for e in E.parse(source).iter('Compile') if 'Include' in e.attrib]:E.SubElement(items,'Compile',Include=str(file))

for name in ['Utils','Google.Protobuf']:
 path=runtime/(name+'.dll')
 if not path.is_file():p.error('Build actual SDK first: '+str(path))
 entry=E.SubElement(items,'Reference',Include=name);E.SubElement(entry,'HintPath').text=str(path)
E.ElementTree(project).write(out/'Probe.csproj',encoding='unicode');(out/'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>');results=[]
for phase,command in [('build',['dotnet','build',str(out/'Probe.csproj'),'-c','Release','-m:1','-nr:false']),('test',['dotnet',str(out/'bin/Release/net10.0/Probe.dll')])]:
 with (out/(phase+'.log')).open('w') as log:run=subprocess.run(command,stdout=log,stderr=subprocess.STDOUT,timeout=180)
 results.append({'phase':phase,'exit':run.returncode});print(phase,run.returncode,flush=True)
 if run.returncode:print((out/(phase+'.log')).read_text()[-6500:]);break
 if phase=='test':print((out/'test.log').read_text(),end='',flush=True)
(out/'results.json').write_text(json.dumps(results,indent=2)+'\n');raise SystemExit(0 if len(results)==2 and all(r['exit']==0 for r in results) else 1)
