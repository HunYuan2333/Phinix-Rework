from pathlib import Path
import subprocess,json,shutil,re,os,hashlib
import argparse
parser=argparse.ArgumentParser(description="Extend a new F5-A lab with full local consumers; never modify source Git history.")
parser.add_argument('--lab',required=True,type=Path)
lab=parser.parse_args().lab.resolve();src=Path.cwd();shared=lab/'Shared'
if not (src/'Phinix.sln').is_file():raise SystemExit('Run from the source repository root.')
if not (shared/'.git').exists() or any((lab/n).exists() for n in ['ClientFull','ServerFull']):raise SystemExit('Requires an unused F5-A lab with Shared and no full consumers.')
info={'sourceHead':subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip(),'lab':str(lab),'inputs':{}}
def run(*args,cwd=None):
 r=subprocess.run(args,cwd=cwd,text=True,stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
 if r.returncode:raise RuntimeError(str(args)+'\n'+r.stdout)
 return r.stdout.strip()
def copy(rel,dest):
 data=(src/rel).read_bytes();dest.parent.mkdir(parents=True,exist_ok=True);dest.write_bytes(data);info['inputs'][rel]=hashlib.sha256(data).hexdigest()
def tracked(*paths):return [p for p in run('git','ls-files','-z','--',*paths,cwd=src).split('\0') if p]
def commit(repo,msg):
 run('git','add','.',cwd=repo);run('git','-c','commit.gpgsign=false','commit','-m',msg,cwd=repo);return run('git','rev-parse','HEAD',cwd=repo)
def neutralize(plugin):
 folder=shared/'Extensions'/plugin/'Contracts'
 for rel in tracked('Extensions/'+plugin+'/Contracts'):
  if rel.endswith('.csproj'):continue
  copy(rel,shared/rel)
 p=folder/(plugin+'DomainContracts.cs');s=p.read_text();blocks=re.findall(r'#if NET472\n(.*?)#endif',s,re.S)
 assert len(blocks)==2
 game=blocks[1];s=re.sub(r'#if NET472\n.*?#endif','',s,flags=re.S);p.write_text(s)
 assert 'using Verse;' not in s and 'using PhinixClient.Framework;' not in s
 (folder/(plugin+'Extension.csproj')).write_text('''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFrameworks>net472;net10.0</TargetFrameworks><AssemblyName>'''+plugin+'''Extension</AssemblyName><GenerateAssemblyInfo>false</GenerateAssemblyInfo></PropertyGroup><ItemGroup><ProjectReference Include="../../../Dependencies/protobuf/csharp/src/Google.Protobuf/Google.Protobuf.csproj"/><ProjectReference Include="../../../Common/Utils/Utils.csproj"/><ProjectReference Include="../../../Common/UserManagement/UserManagement.csproj"/></ItemGroup></Project>''')
 return game
parts={plugin:neutralize(plugin) for plugin in ['Chat','Trade']}
info['sharedCommit']=commit(shared,'test(f5): separate neutral contracts from client game interfaces')
for side in ['ClientFull','ServerFull']:
 repo=lab/side;assert not repo.exists();repo.mkdir();run('git','init','-b','main',str(repo))
 run('git','config','user.name','Phinix local rehearsal',cwd=repo);run('git','config','user.email','local-rehearsal@invalid.example',cwd=repo)
 (repo/'.gitignore').write_text('**/bin/\n**/obj/\n*.user\n*.log\nOutput/\nGameDlls/\n.nuget/\n')
 for rel in ['Directory.Build.props','Directory.Build.targets','nuget.config']:copy(rel,repo/rel)
 if side=='ClientFull':
  files=tracked('Client','.nuget')
  files += [p for p in tracked('Extensions') if '/Client/' in p or '/Package/' in p or p.startswith(('Extensions/Inventory/Contracts/','Extensions/LegacyAdapter/Contracts/'))]
 else:
  files=tracked('Server')+[p for p in tracked('Extensions/Chat/Server','Extensions/Trade/Server')]
 for rel in files:copy(rel,repo/rel)
 run('git','-c','protocol.file.allow=always','submodule','add',str(shared),'Dependencies/Phinix.Common',cwd=repo)
 run('git','-c','protocol.file.allow=always','submodule','update','--init','--recursive',cwd=repo)
 if side=='ClientFull':
  endpoints=[('Common/Connections/NetClient.cs','Client/Common/Connections.Client/NetClient.cs'),('Common/Authentication/ClientAuthenticator.cs','Client/Common/Authentication.Client/ClientAuthenticator.cs'),('Common/UserManagement/ClientUserManager.cs','Client/Common/UserManagement.Client/ClientUserManager.cs')]
 else:endpoints=[('Common/Connections/NetServer.cs','Server/Connections.Server/NetServer.cs')]
 moved={}
 for origin,dest in endpoints:copy(origin,repo/dest);moved[(src/origin).resolve()]=(repo/dest).resolve()
 # Rewrite only declared source/project/library paths, based on original ownership.
 for p in list(repo.rglob('*.csproj')):
  if 'Dependencies' in p.relative_to(repo).parts:continue
  original=src/p.relative_to(repo);s=p.read_text()
  def rewrite(m):
   value=m.group(2)
   if '$(' in value or '@(' in value:return m.group(0)
   target=(original.parent/value.replace('\\','/')).resolve()
   replacement=None
   try:rel=target.relative_to(src).as_posix()
   except ValueError:return m.group(0)
   if target in moved:replacement=moved[target]
   elif rel.startswith(('Common/','Dependencies/protobuf/','libs/')):replacement=repo/'Dependencies/Phinix.Common'/rel
   elif side=='ServerFull' and rel.startswith(('Extensions/Chat/Contracts/','Extensions/Trade/Contracts/')):replacement=repo/'Dependencies/Phinix.Common'/rel
   if replacement is None:return m.group(0)
   return m.group(1)+os.path.relpath(replacement,p.parent).replace(os.sep,'/')+m.group(3)
  s=re.sub(r'((?:Include|Remove)="|<HintPath>)([^"<>]+)("|</HintPath>)',rewrite,s)
  # Endpoint file now lives in this project's own directory; SDK discovers it.
  for origin,dest in endpoints:
   if p.parent==(repo/dest).parent:
    s=re.sub(r'<Compile Include="'+re.escape(Path(dest).name)+r'">\s*<Link>.*?</Link>\s*</Compile>','',s,flags=re.S)
  p.write_text(s)
 if side=='ClientFull':
  for plugin,body in parts.items():
   folder=repo/'Extensions'/plugin/'Contracts';folder.mkdir(parents=True,exist_ok=True)
   imports='using System;\nusing System.Collections.Generic;\nusing UserManagement;\nusing Utils;\nusing Utils.Framework;\n'
   imports+= 'using PhinixClient;\nusing PhinixClient.Framework;\n' if plugin=='Chat' else 'using Phinix.TradeExtension;\nusing Verse;\nusing Thing = Verse.Thing;\n'
   (folder/('Client'+plugin+'Contracts.cs')).write_text(imports+'namespace Phinix.'+plugin+'Extension.Client\n{\n'+body+'\n}\n')
   target='../../../Dependencies/Phinix.Common/Extensions/'+plugin+'/Contracts/'
   game='<Reference Include="Assembly-CSharp"><HintPath>$(SolutionDir)GameDlls/Assembly-CSharp.dll</HintPath><Private>false</Private></Reference><Reference Include="UnityEngine"><HintPath>$(SolutionDir)GameDlls/UnityEngine.dll</HintPath><Private>false</Private></Reference>' if plugin=='Trade' else ''
   (folder/(plugin+'Extension.csproj')).write_text('''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net472</TargetFramework><AssemblyName>'''+plugin+'''Extension</AssemblyName><GenerateAssemblyInfo>false</GenerateAssemblyInfo><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><Compile Include="Client'''+plugin+'''Contracts.cs"/><Compile Include="'''+target+'''**/*.cs" Exclude="'''+target+'''**/obj/**/*.cs;'''+target+'''**/bin/**/*.cs"/><ProjectReference Include="../../../Client/ClientExtensionAbstractions/ClientExtensionAbstractions.csproj"/><ProjectReference Include="../../../Dependencies/Phinix.Common/Common/Utils/Utils.csproj"/><ProjectReference Include="../../../Dependencies/Phinix.Common/Common/UserManagement/UserManagement.csproj"/><ProjectReference Include="../../../Dependencies/Phinix.Common/Dependencies/protobuf/csharp/src/Google.Protobuf/Google.Protobuf.csproj"/>'''+game+'''</ItemGroup><Target Name="CopyClientContract" AfterTargets="Build"><Copy SourceFiles="$(TargetDir)$(AssemblyName).dll" DestinationFiles="$(SolutionDir)Output/phinix-rework/Common/Extensions/'''+('08' if plugin=='Chat' else '09')+'-'+plugin+'''Extension.dll"/></Target></Project>''')
  # Filter the old solution to the actual client graph, preserving Release 1.6 mapping.
  sln=(src/'Phinix.sln').read_text();removed=[]
  def project(m):
   block=m.group(0);match=re.search(r' = "[^"]+", "([^"]+)", "(\{[^}]+\})"',block)
   if not match:return block
   path,guid=match.groups();relative=path.replace('\\','/')
   if relative.startswith('Server/') or '/Server/' in relative:removed.append(guid);return ''
   if relative.startswith(('Common/','Dependencies/protobuf/')):block=block.replace('"'+path+'"','"Dependencies\\Phinix.Common\\'+path+'"')
   return block
  sln=re.sub(r'Project\([^\n]+\n.*?EndProject\n',project,sln,flags=re.S)
  sln='\n'.join(line for line in sln.split('\n') if not any(guid in line for guid in removed))
  (repo/'PhinixClient.sln').write_text(sln)
 else:
  # Contract outputs are produced within Shared; endpoint plugin outputs remain owned here.
  p=repo/'Server/Server.csproj';s=p.read_text()
  s=s.replace('$(_ExtensionsBase)\\Chat\\Contracts\\$(_ExtensionsConfigBin)', '$(MSBuildThisFileDirectory)../Dependencies/Phinix.Common/Extensions/Chat/Contracts/$(_ExtensionsConfigBin)').replace('$(_ExtensionsBase)\\Trade\\Contracts\\$(_ExtensionsConfigBin)', '$(MSBuildThisFileDirectory)../Dependencies/Phinix.Common/Extensions/Trade/Contracts/$(_ExtensionsConfigBin)')
  p.write_text(s)
 (repo/'README.md').write_text('# '+side+' local F5 rehearsal\n\nPins Shared '+info['sharedCommit']+'. Production names/protocols remain unchanged. Sources and private build references are separate; not a public release.\n')
 info[side+'Commit']=commit(repo,'test(f5): isolate complete '+side+' consumer graph')
 fresh=lab/'fresh'/side;run('git','-c','protocol.file.allow=always','clone','--recurse-submodules',str(repo),str(fresh))
 if side=='ClientFull':
  shutil.copytree(src/'GameDlls',fresh/'GameDlls',ignore=shutil.ignore_patterns('bin','obj'))
  shutil.copytree(src/'.nuget',fresh/'.nuget')
  info['privateReferences']={p.relative_to(src).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in (src/'.nuget').rglob('*.dll')}
(lab/'f5b-manifest.json').write_text(json.dumps(info,indent=2));print(json.dumps({k:v for k,v in info.items() if k!='inputs'},indent=2))
