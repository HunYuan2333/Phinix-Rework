#!/usr/bin/env python3
"""Run the complete original PluginStore source suite with isolated metadata fixtures."""
import argparse,json,subprocess,xml.etree.ElementTree as E
from pathlib import Path
p=argparse.ArgumentParser(description=__doc__);p.add_argument('--output',type=Path,required=True);p.add_argument('--runtime16',type=Path,required=True);p.add_argument('--runtime17',type=Path,required=True);a=p.parse_args();root=Path(__file__).resolve().parents[2];test=root/'Tests/PluginStoreRuntimeTests';out=a.output.resolve()
if out.exists() or out.is_relative_to(root.parent):p.error('Use fresh isolated output.')
out.mkdir(parents=True);(out/'NuGet.Config').write_text('<configuration><packageSources><clear /><add key="local" value="'+str(Path.home()/'.nuget/packages')+'" /></packageSources></configuration>')
for name,framework in [('ManagedPayload','net10.0'),('ManagedAdapterPayload','net472')]:
 source=test/'Fixtures'/name;original=E.parse(source/(name+'.csproj'));project=E.Element('Project',Sdk='Microsoft.NET.Sdk');props=E.SubElement(project,'PropertyGroup')
 for key,value in {'TargetFramework':framework,'AssemblyName':'Fixture.Plugin','AssemblyVersion':'1.2.3.4','LangVersion':'latest','EnableDefaultCompileItems':'false','NuGetAudit':'false'}.items():E.SubElement(props,key).text=value
 items=E.SubElement(project,'ItemGroup');files=list(source.glob('*.cs'))+[(source/n.attrib['Include']).resolve() for n in original.iter('Compile')]
 for file in dict.fromkeys(files):E.SubElement(items,'Compile',Include=str(file))
 if name=='ManagedAdapterPayload':
  reference=E.SubElement(items,'Reference',Include='Utils');E.SubElement(reference,'HintPath').text=str(a.runtime16.resolve()/'Utils.dll')
 dest=out/name;dest.mkdir();E.ElementTree(project).write(dest/'Fixture.csproj',encoding='unicode')
original=E.parse(test/'PluginStoreRuntimeTests.csproj');project=E.Element('Project',Sdk='Microsoft.NET.Sdk');props=E.SubElement(project,'PropertyGroup')
for key,value in {'TargetFramework':'net10.0','OutputType':'Exe','AssemblyName':'PluginStoreRuntimeTests','LangVersion':'7.3','EnableDefaultCompileItems':'false','NuGetAudit':'false','NoWarn':'CS0436'}.items():E.SubElement(props,key).text=value
items=E.SubElement(project,'ItemGroup')
for file in list(test.glob('*.cs'))+[(test/n.attrib['Include']).resolve() for n in original.iter('Compile') if n.get('Include')]:E.SubElement(items,'Compile',Include=str(file))
for name in ['Utils','Google.Protobuf']:
 reference=E.SubElement(items,'Reference',Include=name);E.SubElement(reference,'HintPath').text=str(a.runtime17.resolve()/(name+'.dll'))
for node in original.iter('PackageReference'):E.SubElement(items,'PackageReference',**node.attrib)
for node in original.iter('None'):
 attrs=dict(node.attrib)
 if 'Link' not in attrs:attrs['Link']=str(Path(attrs['Include']).parent/'%(Filename)%(Extension)')
 attrs['Include']=str((test/attrs['Include']).resolve());E.SubElement(items,'None',**attrs)
for name in ['ManagedPayload','ManagedAdapterPayload']:E.SubElement(items,'ProjectReference',Include=str(out/name/'Fixture.csproj'),ReferenceOutputAssembly='false')
target=E.SubElement(project,'Target',Name='CopyFixtures',AfterTargets='Build')
for name,framework,dest in [('ManagedPayload','net10.0','Fixtures'),('ManagedAdapterPayload','net472','AdapterFixtures')]:E.SubElement(target,'Copy',SourceFiles=str(out/name/'bin/Release'/framework/'Fixture.Plugin.dll'),DestinationFolder='$(TargetDir)'+dest)
E.ElementTree(project).write(out/'Probe.csproj',encoding='unicode');results=[]
for phase,command in [('build',['dotnet','build',str(out/'Probe.csproj'),'-c','Release','-m:1','-nr:false']),('test',['dotnet',str(out/'bin/Release/net10.0/PluginStoreRuntimeTests.dll')])]:
 with (out/(phase+'.log')).open('w') as log:run=subprocess.run(command,stdout=log,stderr=subprocess.STDOUT,timeout=240)
 results.append({'phase':phase,'exit':run.returncode});print(phase,run.returncode,flush=True)
 if run.returncode:print((out/(phase+'.log')).read_text()[-7000:]);break
 if phase=='test':print((out/'test.log').read_text(),end='',flush=True)
(out/'results.json').write_text(json.dumps(results,indent=2)+'\n');raise SystemExit(0 if len(results)==2 and all(row['exit']==0 for row in results) else 1)
