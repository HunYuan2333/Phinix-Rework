#!/usr/bin/env python3
"""Build one independent plugin from one fixed Host checkout; package both runtimes under one version."""
import argparse,hashlib,json,os,re,subprocess,tempfile,xml.etree.ElementTree as ET
from pathlib import Path
p=argparse.ArgumentParser(description=__doc__)
for name in ['host','plugin','game16','game17','harmony17','output']:p.add_argument('--'+name,type=Path,required=True)
p.add_argument('--online-restore',action='store_true',help='Allow public pinned NuGet restore for cloud CI.')
p.add_argument('--version',required=True);p.add_argument('--work',type=Path)
a=p.parse_args();host=a.host.resolve();plugin=a.plugin.resolve();output=a.output.resolve()
if not re.fullmatch(r'(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)',a.version):p.error('Use an immutable three-part release version.')
if output.exists():p.error('Output already exists; never replace a published or existing asset.')
work=a.work.resolve() if a.work else Path(tempfile.mkdtemp(prefix='phinix-managed-dual-'))
for protected in [host,plugin,a.game16.resolve(),a.game17.resolve()]:
 if work.is_relative_to(protected) or protected.is_relative_to(work):p.error('Use an isolated work directory outside source and reference trees.')
 if output.is_relative_to(protected):p.error('Output must be outside source and reference trees.')
if work.exists() and any(work.iterdir()):p.error('Use fresh work directory.')
work.mkdir(parents=True,exist_ok=True)
projects=list((plugin/'Client').glob('*.csproj')) if (plugin/'Client').exists() else list((plugin/'example').glob('*.csproj'))
if len(projects)!=1:p.error('Expected one independent plugin client project.')
project=projects[0];props={node.tag.split('}')[-1]:node.text for node in ET.parse(project).getroot().iter() if node.text and not list(node)}
if (plugin/'publication.json').is_file():
 metadata=json.loads((plugin/'publication.json').read_text());id=metadata['packageId'];name=metadata['name'];assembly_names=[metadata['contractAssembly'],metadata['clientAssembly']]
else:
 id=props['PluginPackageId'];name=props['PluginDisplayName'];assembly_names=[props['AssemblyName']]
minimum_host = metadata.get('minimumHostVersion', '0.10.1') if (plugin/'publication.json').is_file() else '0.10.1'
if not re.fullmatch(r'(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)', minimum_host):p.error('Invalid minimumHostVersion.')
env=os.environ.copy();installed=[re.fullmatch(r'(10\.[^ ]+) \[(.*)\]',line) for line in subprocess.check_output(['dotnet','--list-sdks'],text=True).splitlines()];installed=[v for v in installed if v]
if not installed:p.error('A .NET10 SDK is required.')
env['MSBuildSDKsPath']=str(Path(installed[-1].group(2))/installed[-1].group(1)/'Sdks')
def run(args):subprocess.run([str(x) for x in args],env=env,cwd=host,check=True)
restore_args=['--online-restore'] if a.online_restore else []
run(['python3',host/'tools/compatibility/build-graph-isolated.py','--output',work/'build16','--game',a.game16.resolve(),'--plugin-root',plugin,'--plugin-version',a.version,*restore_args])
run(['python3',host/'tools/compatibility/build-net10-graph.py','--output',work/'build17','--game',a.game17.resolve(),'--harmony',a.harmony17.resolve(),'--plugin-project',project,'--plugin-version',a.version,*restore_args])
snapshot=work/'build16/Phinix-Rework';tool_project=snapshot/'Extensions/PluginStore/Tools/ManagedPackageTool/ManagedPackageTool.csproj'
run(['dotnet','build',tool_project,'-c','Release','-p:NuGetAudit=false','-p:BuildInParallel=false','-m:1'])
tool=tool_project.parent/'bin/Release/net10.0/ManagedPackageTool.dll'
audit=work/'metadata';audit.mkdir();(audit/'Audit.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NuGetAudit>false</NuGetAudit></PropertyGroup><ItemGroup><Compile Include="'+str(host/'tools/compatibility/AssemblyTargetAudit.cs')+'" /></ItemGroup></Project>');(audit/'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
run(['dotnet','build',audit/'Audit.csproj','-c','Release','-v:q']);audit_dll=audit/'bin/Release/net10.0/Audit.dll'
def inspect(paths):return json.loads(subprocess.check_output(['dotnet',str(audit_dll),*map(str,paths)],text=True,env=env))
def simple(row):return row['identity'].split(',',1)[0]
variants=[]
for game,tfm in [('1.6','net472'),('1.7','net10.0')]:
 if game=='1.6':
  plugin_build=work/'build16'/plugin.name
  dlls=[next(path for path in plugin_build.rglob(n+'.dll') if 'bin/Release/net472' in path.as_posix()) for n in assembly_names]
  hostfiles=list((snapshot/'Output/phinix-rework').rglob('*.dll'))
 else:
  dlls=[work/'build17'/n/'bin/Release/net10.0'/(n+'.dll') for n in assembly_names]
  assets=json.loads((work/'build17/runtime-assets.json').read_text());hostfiles=[Path(v['source']) for v in assets.values()]
 hosts={simple(row):Path(row['path']) for row in inspect(hostfiles)}
 reference=a.game16.resolve() if game=='1.6' else a.game17.resolve();providers=dict(hosts)
 for path in reference.glob('*.dll'):providers.setdefault(path.stem,path)
 providers['0Harmony']=host/'.nuget/Lib.Harmony.2.3.6/lib/net472/0Harmony.dll' if game=='1.6' else a.harmony17.resolve()
 if game=='1.6':
  nuget=Path(os.environ.get('NUGET_PACKAGES',str(Path.home()/'.nuget/packages')))
  frameworks=sorted((nuget/'microsoft.netframework.referenceassemblies.net472').glob('*/build/.NETFramework/v4.7.2'))
  if not frameworks:p.error('Missing restored net472 targeting pack.')
  for path in list(frameworks[-1].glob('*.dll'))+list((frameworks[-1]/'Facades').glob('*.dll')):providers.setdefault(path.stem,path)
 needed={ref['name'] for row in inspect(dlls) for ref in row['references']}-set(assembly_names)
 refs=[]
 for n in sorted(needed):
  if n not in providers or not providers[n].is_file():raise ValueError('Missing '+game+' provider: '+n)
  refs+=['--host-assembly',str(providers[n])]
 config=work/(game+'-config.json');config.write_text(json.dumps({'compatibility':{'rimWorldVersions':[game],'phinixRange':'>='+minimum_host+' <1.0.0','abstractionsRange':'>=3.0.0 <4.0.0'},'dependencies':[],'externalMods':[]}))
 target=work/(game+'.zip');cmd=['dotnet',tool,'--package-id',id,'--name',name,'--version',a.version,'--output',target,'--config',config,'--target-framework',tfm,'--compile-client-api','3.0.0']
 for path in dlls:cmd+=['--assembly',path]
 resources=plugin/'Client/Resources' if (plugin/'Client/Resources').exists() else plugin/'example/Resources/Localization'
 for path in sorted(resources.rglob('*.json')):cmd+=['--language-file',path]
 run(cmd+refs);run(['dotnet',tool,'--validate',target,'--game-version',game,*refs]);variants+=['--variant',target]
output.parent.mkdir(parents=True,exist_ok=True);run(['dotnet',tool,'--pack-variants',*variants,'--output',output])
for game in ['1.6','1.7']:run(['dotnet',tool,'--validate',output,'--game-version',game])
print(json.dumps({'packageId':id,'version':a.version,'zip':str(output),'sha256':hashlib.sha256(output.read_bytes()).hexdigest(),'games':['1.6','1.7'],'work':str(work)}))
