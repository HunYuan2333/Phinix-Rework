"""Compile the complete maintained client source graph for 1.7 in isolated SDK projects.
Preserves source project/package dependencies; no old Output infrastructure seed or release.
"""
import argparse,glob,hashlib,json,re,subprocess,xml.etree.ElementTree as E
from pathlib import Path
p=argparse.ArgumentParser(description=__doc__);p.add_argument('--game',type=Path,required=True);p.add_argument('--harmony',type=Path,required=True);p.add_argument('--output',type=Path,required=True)
p.add_argument('--plugin-project',type=Path,action='append',help='Explicit standalone consumer project; replaces default three sibling consumers.')
p.add_argument('--plugin-version',help='Override generated client version for an immutable plugin release.')
a=p.parse_args();repo=Path(__file__).resolve().parents[2];out=a.output.resolve();game=a.game.resolve()
if out.is_relative_to(repo.parent) or game.is_relative_to(out) or out.is_relative_to(game):p.error('Use output outside workspace and game references.')
if out.exists() and any(out.iterdir()):p.error('Use fresh output.')
for name in ['Assembly-CSharp.dll','UnityStubs.dll']:
 if not (game/name).is_file():p.error('Missing 1.7 reference: '+name)
out.mkdir(parents=True);(out/'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
tag=lambda x:x.tag.split('}')[-1]
roots=[repo/'Client/Source/Client.csproj',repo/'Extensions/Chat/Client/ChatExtension.Client.csproj',repo/'Extensions/Trade/Client/TradeExtension.Client.csproj',repo/'Extensions/Inventory/Client/InventoryExtension.Client.csproj',repo/'Extensions/LegacyAdapter/Client/PhinixLegacyAdapter.Client.csproj',repo/'Extensions/PluginStore/Client/PluginStore.Client.csproj',repo.parent/'Phinix-Legacy-RedPacket/Client/LegacyRedPacketExtension.Client.csproj',repo.parent/'Phinix-Legacy-TalentTrade/Client/LegacyTalentTradeExtension.Client.csproj',repo.parent/'Phinix-Example-Plugin/example/Example.csproj']
if a.plugin_project:roots=roots[:6]+[path.resolve() for path in a.plugin_project]
nodes={}
def collect(path):
 path=path.resolve()
 if path in nodes:return
 tree=E.parse(path);els=list(tree.iter());props={tag(x):x.text for x in els if x.text and tag(x) not in ['Project','ItemGroup','PropertyGroup'] and not list(x)}
 name=props.get('AssemblyName',path.stem);nodes[path]={'name':name,'tree':tree,'props':props,'deps':[]}
 for x in els:
  if tag(x)=='ProjectReference':
   dep=(path.parent/x.get('Include').replace('\\','/')).resolve();nodes[path]['deps'].append(dep);collect(dep)
for path in roots:collect(path)
byname={v['name']:k for k,v in nodes.items()}
# DLL-based references in separately maintained plugins become explicit source project edges.
for path,node in nodes.items():
 for x in node['tree'].getroot().iter():
  if tag(x)=='Reference':
   name=x.get('Include').split(',')[0]
   if name in byname and byname[name]!=path and byname[name] not in node['deps']:node['deps'].append(byname[name])
project_paths={k:out/v['name']/(v['name']+'.csproj') for k,v in nodes.items()};fingerprints={};graph=[]
for original,node in nodes.items():
 name=node['name'];pr=E.Element('Project',Sdk='Microsoft.NET.Sdk');pg=E.SubElement(pr,'PropertyGroup');props=node['props'];sdk=bool(node['tree'].getroot().get('Sdk'));generate=props.get('GenerateAssemblyInfo','true' if sdk else 'false')
 values={'DefineConstants':'PHINIX_RIMWORLD_17','TargetFramework':'net10.0','AssemblyName':name,'EnableDefaultCompileItems':'false','GenerateAssemblyInfo':generate,'AllowUnsafeBlocks':'true','LangVersion':'latest','NuGetAudit':'false','IsTrimmable':'false','RestorePackagesWithLockFile':'true'}
 for key in ['RootNamespace','Version','VersionPrefix','AssemblyVersion','FileVersion','InformationalVersion','SignAssembly']:
  if props.get(key) and '$(' not in props[key]:values[key]=props[key]
 if props.get('AssemblyOriginatorKeyFile'):values['AssemblyOriginatorKeyFile']=str((original.parent/props['AssemblyOriginatorKeyFile'].replace('\\','/')).resolve())
 if a.plugin_version and a.plugin_project and original in [path.resolve() for path in a.plugin_project]:values.update(Version=a.plugin_version,AssemblyVersion=a.plugin_version+'.0')
 for key,value in values.items():E.SubElement(pg,key).text=value
 items=E.SubElement(pr,'ItemGroup');explicit=[x for x in node['tree'].getroot().iter() if tag(x)=='Compile' and x.get('Include')];files=set()
 if not explicit or (sdk and props.get('EnableDefaultCompileItems')!='false'):
  files.update(x for x in original.parent.rglob('*.cs') if not any(s in ['bin','obj','Fixtures'] for s in x.relative_to(original.parent).parts))
 for x in explicit:
  for pattern in x.get('Include').split(';'):
   files.update(Path(f).resolve() for f in glob.glob(str(original.parent/pattern.replace('\\','/')),recursive=True) if '/obj/' not in f and '/bin/' not in f)
 for f in sorted(files):E.SubElement(items,'Compile',Include=str(f))
 # Preserve explicitly owned resources and logical names, not just C# sources.
 # The Store reads its maintainer registry through GetManifestResourceStream.
 for x in node['tree'].getroot().iter():
  if tag(x)=='EmbeddedResource' and x.get('Include'):
   for pattern in x.get('Include').split(';'):
    for filename in sorted(glob.glob(str(original.parent/pattern.replace('\\','/')),recursive=True)):
     resource=Path(filename).resolve()
     if not resource.is_file():continue
     item=E.SubElement(items,'EmbeddedResource',Include=str(resource))
     for child in x:item.append(E.fromstring(E.tostring(child)))
     fingerprints.setdefault(name+' resources',[]).append({'path':str(resource),'sha256':hashlib.sha256(resource.read_bytes()).hexdigest()})
 fingerprints[name]=[{'path':str(f),'sha256':hashlib.sha256(f.read_bytes()).hexdigest()} for f in sorted(files)]
 for dep in node['deps']:E.SubElement(items,'ProjectReference',Include=str(project_paths[dep]))
 for x in node['tree'].getroot().iter():
  if tag(x)=='PackageReference':
   attrib={k:v for k,v in x.attrib.items() if k!='Condition'};item=E.SubElement(items,'PackageReference',attrib)
   for child in x:item.append(E.fromstring(E.tostring(child)))
 # Game references are never copied locally. Framework assemblies come from the SDK.
 natives=[game/'Assembly-CSharp.dll',game/'UnityStubs.dll',game/'Waylith.dll',game/'Compat.dll',game/'com.rlabrecque.steamworks.net.dll',a.harmony.resolve()]
 if name in ['Connections','Connections.Client']:natives.append(repo/'Dependencies/Phinix.Common/libs/netstandard2.0/LiteNetLib.dll')
 if name!='Google.Protobuf' and name not in ['Utils','Connections','Authentication','UserManagement','Connections.Client','Authentication.Client','UserManagement.Client','ClientPresentation.Contracts']:
  for f in natives:
   if f.is_file():
    item=E.SubElement(items,'Reference',Include=f.stem);E.SubElement(item,'HintPath').text=str(f);E.SubElement(item,'Private').text='false'
 elif name in ['Connections','Connections.Client']:
  f=natives[-1];item=E.SubElement(items,'Reference',Include=f.stem);E.SubElement(item,'HintPath').text=str(f);E.SubElement(item,'Private').text='false'
 dest=project_paths[original];dest.parent.mkdir();E.ElementTree(pr).write(dest,encoding='unicode');graph.append({'name':name,'original':str(original),'project':str(dest),'dependencies':[nodes[d]['name'] for d in node['deps']]})
(out/'source-fingerprints.json').write_text(json.dumps(fingerprints,indent=2));(out/'graph.json').write_text(json.dumps(graph,indent=2));(out/'reference-fingerprints.json').write_text(json.dumps({str(f):hashlib.sha256(f.read_bytes()).hexdigest() for f in [game/'Assembly-CSharp.dll',game/'UnityStubs.dll',a.harmony.resolve()]},indent=2))
umbrella=E.Element('Project',Sdk='Microsoft.NET.Sdk');pg=E.SubElement(umbrella,'PropertyGroup');E.SubElement(pg,'TargetFramework').text='net10.0';E.SubElement(pg,'NuGetAudit').text='false';E.SubElement(pg,'EnableDefaultCompileItems').text='false';items=E.SubElement(umbrella,'ItemGroup')
for root in roots:E.SubElement(items,'ProjectReference',Include=str(project_paths[root.resolve()]))
path=out/'Joint.csproj';E.ElementTree(umbrella).write(path,encoding='unicode')
with (out/'build.log').open('w') as log:r=subprocess.run(['dotnet','build',str(path),'-c','Release','-p:BuildInParallel=false','-p:NuGetAudit=false','-m:1'],stdout=log,stderr=subprocess.STDOUT)
(out/'results.json').write_text(json.dumps({'exit':r.returncode,'source_projects':len(nodes),'scope':'Complete source dependency graph in generated net10 projects; original targets/packaging are not executed.'},indent=2));print('net10 source graph:',len(nodes),'projects, exit',r.returncode,'log',out/'build.log')
if r.returncode==0:
 import shutil
 stage=out/'infrastructure';stage.mkdir();assets_manifest={}
 for node in graph:
  name=node['name'];dll=out/name/'bin/Release/net10.0'/(name+'.dll');filename='02-Protobuf.dll' if name=='Google.Protobuf' else name+'.dll'
  shutil.copy2(dll,stage/filename);assets_manifest[filename]={'source':str(dll),'sha256':hashlib.sha256(dll.read_bytes()).hexdigest()}
  assets=json.loads((out/name/'obj/project.assets.json').read_text())
  for entries in assets['targets'].values():
   for library,entry in entries.items():
    if entry.get('type')!='package':continue
    info=assets['libraries'][library]
    for relative in entry.get('runtime',{}):
     filename=Path(relative).name
     if not filename.endswith('.dll') or (game/filename).is_file():continue
     candidates=[Path(folder)/info['path']/relative for folder in assets['packageFolders']]
     source=next((x for x in candidates if x.is_file()),None)
     if source is None:raise RuntimeError('Missing resolved runtime asset '+library+'/'+relative)
     digest=hashlib.sha256(source.read_bytes()).hexdigest()
     if filename in assets_manifest and assets_manifest[filename]['sha256']!=digest:raise RuntimeError('Conflicting runtime asset '+filename)
     shutil.copy2(source,stage/filename);assets_manifest[filename]={'source':str(source),'sha256':digest,'package':library}
 source=repo/'Dependencies/Phinix.Common/libs/netstandard2.0/LiteNetLib.dll';shutil.copy2(source,stage/source.name);assets_manifest[source.name]={'source':str(source),'sha256':hashlib.sha256(source.read_bytes()).hexdigest()}
 (out/'runtime-assets.json').write_text(json.dumps(assets_manifest,indent=2))
raise SystemExit(r.returncode)
