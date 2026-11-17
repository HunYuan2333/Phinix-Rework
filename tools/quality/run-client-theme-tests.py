#!/usr/bin/env python3
"""Run existing theme and editor source suites against explicit built shared assemblies."""
import argparse,json,subprocess,xml.etree.ElementTree as E
from pathlib import Path
p=argparse.ArgumentParser(description=__doc__)
p.add_argument('--output',type=Path,required=True)
p.add_argument('--runtime17',type=Path,required=True)
a=p.parse_args();root=Path(__file__).resolve().parents[2];out=a.output.resolve();runtime=a.runtime17.resolve()
if out.exists() or out.is_relative_to(root.parent):p.error('Use fresh isolated output.')
for name in ['Utils','Google.Protobuf']:
 if not (runtime/(name+'.dll')).is_file():p.error('Missing actual shared assembly: '+name)
out.mkdir(parents=True);results=[]
for suite in ['ThemeRuntimeTests','ThemeEditorUsabilityRuntimeTests']:
 source=root/'Tests'/suite/(suite+'.csproj');target=out/suite;target.mkdir();project=E.Element('Project',Sdk='Microsoft.NET.Sdk');props=E.SubElement(project,'PropertyGroup')
 for key,value in {'TargetFramework':'net10.0','OutputType':'Exe','EnableDefaultCompileItems':'false','NuGetAudit':'false','ImplicitUsings':'disable'}.items():E.SubElement(props,key).text=value
 items=E.SubElement(project,'ItemGroup')
 files=list(source.parent.glob('*.cs'))+[(source.parent/item.attrib['Include'].replace('\\','/')).resolve() for item in E.parse(source).iter('Compile')]
 for path in dict.fromkeys(files):E.SubElement(items,'Compile',Include=str(path))
 for name in ['Utils','Google.Protobuf']:
  reference=E.SubElement(items,'Reference',Include=name);E.SubElement(reference,'HintPath').text=str(runtime/(name+'.dll'))
 E.ElementTree(project).write(target/'Probe.csproj',encoding='unicode');(target/'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
 for phase,command in [('build',['dotnet','build',str(target/'Probe.csproj'),'-c','Release','-m:1','-nr:false']),('test',['dotnet',str(target/'bin/Release/net10.0/Probe.dll')])]:
  with (target/(phase+'.log')).open('w') as log:run=subprocess.run(command,stdout=log,stderr=subprocess.STDOUT,timeout=180)
  results.append({'suite':suite,'phase':phase,'exit':run.returncode});print(suite,phase,run.returncode,flush=True)
  if run.returncode:print((target/(phase+'.log')).read_text()[-5000:]);break
  if phase=='test':print((target/'test.log').read_text(),end='',flush=True)
(out/'results.json').write_text(json.dumps(results,indent=2)+'\n')
raise SystemExit(0 if len(results)==4 and all(r['exit']==0 for r in results) else 1)
