#!/usr/bin/env python3
"""Run original Chat/Legacy/Trade scenarios against the actual isolated client graph."""
import argparse,json,subprocess,shutil,xml.etree.ElementTree as E
from pathlib import Path
p=argparse.ArgumentParser(description=__doc__);p.add_argument('--output',type=Path,required=True);p.add_argument('--runtime17',type=Path,required=True);p.add_argument('--game17',type=Path,required=True);a=p.parse_args();root=Path(__file__).resolve().parents[2];out=a.output.resolve();runtime=a.runtime17.resolve()
if out.exists() or out.is_relative_to(root.parent):p.error('Use fresh isolated output.')
out.mkdir(parents=True);results=[]
for suite in ['LegacyTradeRuntimeTests','ChatRegressionTests','TradeOfferWireTests']:
 source=root/'Tests'/suite/(suite+'.csproj');target=out/suite;target.mkdir();project=E.Element('Project',Sdk='Microsoft.NET.Sdk');props=E.SubElement(project,'PropertyGroup')
 for key,value in {'TargetFramework':'net10.0','OutputType':'Exe','AssemblyName':suite,'EnableDefaultCompileItems':'false','NuGetAudit':'false','ImplicitUsings':'disable','NoWarn':'SYSLIB0050'}.items():E.SubElement(props,key).text=value
 items=E.SubElement(project,'ItemGroup');files=list(source.parent.glob('*.cs'))
 for node in E.parse(source).iter():
  if node.tag.split('}')[-1]=='Compile' and node.get('Include'):
   files.append((source.parent/node.get('Include').replace('\\','/')).resolve())
 for file in dict.fromkeys(files):E.SubElement(items,'Compile',Include=str(file))
 refs={f.stem:f for f in runtime.glob('*.dll') if f.name not in ['Joint.dll','mscorlib.dll','netstandard.dll'] and not f.name.startswith('System.')}
 for f in (runtime.parents[2]/'infrastructure').glob('*.dll'):
  if f.name not in ['mscorlib.dll','netstandard.dll'] and not f.name.startswith('System.') and not f.name[0].isdigit():refs[f.stem]=f
 for f in a.game17.resolve().glob('*.dll'):
  if f.name in ['Assembly-CSharp.dll','UnityStubs.dll','0Harmony.dll'] or f.name.startswith('UnityEngine'):refs[f.stem]=f
 for name,file in refs.items():
  entry=E.SubElement(items,'Reference',Include=name);E.SubElement(entry,'HintPath').text=str(file)
 E.ElementTree(project).write(target/'Probe.csproj',encoding='unicode');(target/'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
 for phase,command in [('build',['dotnet','build',str(target/'Probe.csproj'),'-c','Release','-m:1','-nr:false']),('test',['dotnet',str(target/'bin/Release/net10.0'/(suite+'.dll'))])]:
  if phase=='test':
   for relative,entry in json.loads((runtime.parents[2]/'native-assets.json').read_text()).items():
    destination=target/'bin/Release/net10.0'/relative;destination.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(entry['source'],destination)
  with (target/(phase+'.log')).open('w') as log:run=subprocess.run(command,stdout=log,stderr=subprocess.STDOUT,timeout=180)
  results.append({'suite':suite,'phase':phase,'exit':run.returncode});print(suite,phase,run.returncode,flush=True)
  if run.returncode:print((target/(phase+'.log')).read_text()[-6000:]);break
  if phase=='test':print((target/'test.log').read_text(),end='',flush=True)
(out/'results.json').write_text(json.dumps(results,indent=2)+'\n');raise SystemExit(0 if len(results)==6 and all(r['exit']==0 for r in results) else 1)
