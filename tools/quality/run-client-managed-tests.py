#!/usr/bin/env python3
"""Run the unchanged managed suite/fixtures using explicit built shared DLLs, outside SDK project restore chains."""
import argparse,json,subprocess,xml.etree.ElementTree as E
from pathlib import Path
p=argparse.ArgumentParser(description=__doc__)
p.add_argument('--output',type=Path,required=True)
p.add_argument('--runtime16',type=Path,required=True)
p.add_argument('--runtime17',type=Path,required=True)
a=p.parse_args();root=Path(__file__).resolve().parents[2];out=a.output.resolve();test=root/'Tests/ManagedExtensionRuntimeTests'
if out.exists() or out.is_relative_to(root.parent):p.error('Use fresh isolated output.')
for runtime in [a.runtime16,a.runtime17]:
 if not (runtime/'Utils.dll').is_file():p.error('Build both actual shared graphs first.')
out.mkdir(parents=True);(out/'NuGet.Config').write_text('<configuration><packageSources><clear /><add key="local" value="'+str(Path.home()/'.nuget/packages')+'" /></packageSources></configuration>')
fixture_paths={path.resolve():out/'Fixtures'/path.parent.name/'Fixture.csproj' for path in (test/'Fixtures').rglob('*.csproj')}
for source,destination in fixture_paths.items():
 original=E.parse(source);project=E.Element('Project',Sdk='Microsoft.NET.Sdk');props=E.SubElement(project,'PropertyGroup')
 for key,value in {'TargetFramework':'net472','EnableDefaultCompileItems':'false','NuGetAudit':'false','LangVersion':'7.3'}.items():E.SubElement(props,key).text=value
 for key in ['AssemblyName','AssemblyVersion']:
  node=original.find('.//'+key)
  if node is not None:E.SubElement(props,key).text=node.text
 items=E.SubElement(project,'ItemGroup');explicit=list(original.iter('Compile'));files=[(source.parent/e.attrib['Include']).resolve() for e in explicit] if explicit else list(source.parent.glob('*.cs'))
 for file in files:E.SubElement(items,'Compile',Include=str(file))
 for dep in original.iter('ProjectReference'):
  path=(source.parent/dep.attrib['Include']).resolve()
  if path.name=='Utils.csproj':
   ref=E.SubElement(items,'Reference',Include='Utils');E.SubElement(ref,'HintPath').text=str(a.runtime16.resolve()/'Utils.dll')
  else:E.SubElement(items,'ProjectReference',Include=str(fixture_paths[path]))
 destination.parent.mkdir(parents=True);E.ElementTree(project).write(destination,encoding='unicode')
project=E.Element('Project',Sdk='Microsoft.NET.Sdk');props=E.SubElement(project,'PropertyGroup')
for key,value in {'TargetFramework':'net10.0','OutputType':'Exe','AssemblyName':'ManagedExtensionRuntimeTests','EnableDefaultCompileItems':'false','NuGetAudit':'false','LangVersion':'7.3'}.items():E.SubElement(props,key).text=value
items=E.SubElement(project,'ItemGroup');original=E.parse(test/'ManagedExtensionRuntimeTests.csproj')
for file in list(test.glob('*.cs'))+[(test/e.attrib['Include']).resolve() for e in original.iter('Compile') if 'Include' in e.attrib]:E.SubElement(items,'Compile',Include=str(file))
for name in ['Utils','Google.Protobuf']:
 ref=E.SubElement(items,'Reference',Include=name);E.SubElement(ref,'HintPath').text=str(a.runtime17.resolve()/(name+'.dll'))
for entry in original.iter('PackageReference'):E.SubElement(items,'PackageReference',**entry.attrib)
for entry in original.iter('None'):
 attrs=dict(entry.attrib);attrs['Include']=str((test/attrs['Include']).resolve());E.SubElement(items,'None',**attrs)
for name in ['Plugin','Provider','HostUpgrade']:E.SubElement(items,'ProjectReference',Include=str(out/'Fixtures'/name/'Fixture.csproj'),ReferenceOutputAssembly='false')
target=E.SubElement(project,'Target',Name='CopyFixtures',AfterTargets='Build')
for folder,name,dest in [('Plugin','Plugin','Fixtures'),('Helper','Helper','Fixtures'),('Provider','Provider','Fixtures'),('HostUpgrade','Helper','Fixtures/HostUpgrade')]:
 E.SubElement(target,'Copy',SourceFiles=str(out/'Fixtures'/folder/'bin/Release/net472'/('Fixture.Managed.'+name+'.dll')),DestinationFolder='$(TargetDir)'+dest)
E.ElementTree(project).write(out/'Probe.csproj',encoding='unicode');results=[]
for phase,cmd in [('build',['dotnet','build',str(out/'Probe.csproj'),'-c','Release','-m:1','-nr:false']),('test',['dotnet',str(out/'bin/Release/net10.0/ManagedExtensionRuntimeTests.dll')])]:
 with (out/(phase+'.log')).open('w') as log:run=subprocess.run(cmd,stdout=log,stderr=subprocess.STDOUT,timeout=180)
 results.append({'phase':phase,'exit':run.returncode});print(phase,run.returncode,flush=True)
 if run.returncode:print((out/(phase+'.log')).read_text()[-5000:]);break
 if phase=='test':print((out/'test.log').read_text(),end='',flush=True)
(out/'results.json').write_text(json.dumps(results,indent=2)+'\n')
raise SystemExit(0 if len(results)==2 and all(r['exit']==0 for r in results) else 1)
