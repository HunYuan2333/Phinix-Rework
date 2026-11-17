#!/usr/bin/env python3
"""Build Relay sample consumers and exercise merged admission/diagnostics and real UDP guards."""
import argparse,json,subprocess,shutil,xml.etree.ElementTree as E
from pathlib import Path
p=argparse.ArgumentParser(description=__doc__)
for name in ['output','runtime16','runtime17','game17']:p.add_argument('--'+name,type=Path,required=True)
a=p.parse_args();root=Path(__file__).resolve().parents[2];out=a.output.resolve()
if out.exists() or out.is_relative_to(root.parent):p.error('Use fresh isolated output.')
out.mkdir(parents=True);results=[]
for name,framework,runtime,sources,execute in [
 ('sample16','net472',a.runtime16,[root/'Samples/CommandRelay/ResearchRelayModule.cs'],False),
 ('sample17','net10.0',a.runtime17,[root/'Samples/CommandRelay/ResearchRelayModule.cs'],False),
 ('example','net10.0',a.runtime17,list((root.parent/'Phinix-Example-Plugin/Tests').glob('*.cs')),True),
 ('integration','net10.0',a.runtime17,list((root/'Tests/RelayIntegrationRuntimeTests').glob('*.cs')),True),
 ('udp-lifetime','net10.0',a.runtime17,list((root/'Tests/ClientTransportLifetimeTests').glob('*.cs')),True)]:
 target=out/name;target.mkdir();project=E.Element('Project',Sdk='Microsoft.NET.Sdk');props=E.SubElement(project,'PropertyGroup')
 for k,v in {'TargetFramework':framework,'OutputType':'Exe' if execute else 'Library','AssemblyName':name.replace('-','_'),'EnableDefaultCompileItems':'false','NuGetAudit':'false','LangVersion':'latest'}.items():E.SubElement(props,k).text=v
 items=E.SubElement(project,'ItemGroup')
 for src in sources:E.SubElement(items,'Compile',Include=str(src))
 runtime=runtime.resolve();refs={f.stem:f for f in runtime.glob('*.dll') if f.name not in ['Joint.dll','netstandard.dll','mscorlib.dll'] and not f.name.startswith('System.')}
 if framework=='net10.0':
  for f in (runtime.parents[2]/'infrastructure').glob('*.dll'):
   if not f.name.startswith('System.') and not f.name[0].isdigit():refs[f.stem]=f
  for f in a.game17.resolve().glob('*.dll'):
   if f.name in ['Assembly-CSharp.dll','UnityStubs.dll','0Harmony.dll'] or f.name.startswith('UnityEngine'):refs[f.stem]=f
 else:refs={k:v for k,v in refs.items() if k in ['Utils','ClientExtensionAbstractions','ClientPresentation.Contracts','Google.Protobuf']}
 for ref,file in refs.items():
  node=E.SubElement(items,'Reference',Include=ref);E.SubElement(node,'HintPath').text=str(file)
 E.ElementTree(project).write(target/'Probe.csproj',encoding='unicode');(target/'NuGet.Config').write_text('<configuration><packageSources><clear /><add key="local" value="'+str(Path.home()/'.nuget/packages')+'" /></packageSources></configuration>')
 commands=[('build',['dotnet','build',str(target/'Probe.csproj'),'-c','Release','-m:1','-nr:false'])]
 if execute:commands.append(('test',['dotnet',str(target/'bin/Release'/framework/(name.replace('-','_')+'.dll'))]))
 for phase,command in commands:
  if phase=='test' and name=='example':
   for file,alias in [('Google.Protobuf','02-Protobuf'),('Utils','03-Utils'),('ClientPresentation.Contracts','09-ClientPresentation.Contracts'),('UserManagement','08-UserManagement'),('ClientExtensionAbstractions','10-ClientExtensionAbstractions')]:
    shutil.copy2(runtime/(file+'.dll'),target/'bin/Release/net10.0'/(alias+'.dll'))
  with (target/(phase+'.log')).open('w') as log:run=subprocess.run(command,stdout=log,stderr=subprocess.STDOUT,timeout=180)
  results.append({'suite':name,'phase':phase,'exit':run.returncode});print(name,phase,run.returncode,flush=True)
  if run.returncode:print((target/(phase+'.log')).read_text()[-6000:]);break
  if phase=='test':print((target/'test.log').read_text(),end='',flush=True)
(out/'results.json').write_text(json.dumps(results,indent=2)+'\n');raise SystemExit(0 if len(results)==8 and all(r['exit']==0 for r in results) else 1)
