"""Migrate tests into an existing local F5-B lab. Source repository stays untouched."""
from pathlib import Path
import argparse,subprocess,hashlib,json,re,os
parser=argparse.ArgumentParser();parser.add_argument('--lab',required=True,type=Path)
lab=parser.parse_args().lab.resolve();src=Path.cwd();shared=lab/'Shared';client=lab/'ClientFull';server=lab/'ServerFull'
assert (src/'Phinix.sln').exists() and all((r/'.git').exists() for r in (shared,client,server))
if (client/'Tests/Phase35ClientRuntimeTests').exists():raise SystemExit('Requires an unused F5-B lab; preserve existing local test edits.')
manifest={'sourceHead':subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip(),'inputs':{},'cases':{}}
def run(*args,cwd=None):
 r=subprocess.run(args,cwd=cwd,text=True,stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
 if r.returncode:raise RuntimeError(str(args)+'\n'+r.stdout)
 return r.stdout.strip()
def copy(rel,repo):
 p=repo/rel;p.parent.mkdir(parents=True,exist_ok=True);data=(src/rel).read_bytes();p.write_bytes(data);manifest['inputs'][rel]=hashlib.sha256(data).hexdigest()
def tracked(*names):return [p for p in run('git','ls-files','-z','--',*names).split('\0') if p]
for rel in sorted(set(tracked('Tests')+['Tests/ClientCompositionRuntimeCopy.targets'])):
 if rel.startswith('Tests/Phase35RuntimeTests/'):continue
 repo=shared if rel.startswith('Tests/ExtensionLoaderRuntimeTests/') else client
 copy(rel,repo)
# Store JSON fixtures remain test data owned by the client consumer.
for rel in tracked('docs/branch-local/dev/plugin-store'):
 if rel.endswith('.json'):copy(rel,client)
# Rewrite static project/source/data links by original ownership; do not alter scenario logic.
for repo in (shared,client):
 for p in (repo/'Tests').rglob('*.csproj'):
  original=src/p.relative_to(repo);s=p.read_text()
  def rewrite(m):
   raw=m.group(2)
   if '$(' in raw or '@(' in raw:return m.group(0)
   target=(original.parent/raw.replace('\\','/')).resolve()
   try:rel=target.relative_to(src).as_posix()
   except ValueError:return m.group(0)
   if rel.startswith(('Common/','Dependencies/protobuf/','libs/')):
    destination=(repo if repo==shared else repo/'Dependencies/Phinix.Common')/rel
    return m.group(1)+os.path.relpath(destination,p.parent).replace(os.sep,'/')+m.group(3)
   return m.group(0)
  p.write_text(re.sub(r'((?:Include|Remove)="|<HintPath>)([^"<>]+)("|</HintPath>)',rewrite,s))
original=(src/'Tests/Phase35RuntimeTests/Program.cs').read_text()
def method(name):
 start=re.search(r'^    private static [^\n]+\b'+name+r'\(',original,re.M).start()
 end=re.search(r'^    }',original[start:],re.M).end()+start
 return original[start:end]+'\n'
original_cases=re.findall(r'^            (Assert\w+)\(\);',original,re.M)
core=['AssertDisabledSettingRecoveryEntries','AssertExtensionStorageCannotEscapeRoot','AssertApiOwnerRevocationAndProviderPolicy','AssertStructuredExtensionLoggerPreservesContext']
client_cases=['AssertLegacyApisRemoved','AssertClientKeyGenerationStillWorks','AssertExtensionDependencyValidationAndLifecycle','AssertClientExtensionRuntimeLifecycle','AssertClientEnvironmentCaptureRequiresMainThread','AssertExtensionManagementWindowLifecycle','AssertClientLinkOpening']
server_cases=[n for n in original_cases if n not in core+client_cases]+['AssertLegacyApisRemoved']
assert len(original_cases)==18 and set(original_cases)==set(core+client_cases+server_cases)
manifest['cases']={'original':original_cases,'Shared':core,'ClientFull':client_cases,'ServerFull':server_cases,'relocatedSourceAssertions':['Client/NetClient Abort','Server/NetServer Abort','Client/ClientAuthenticator RNGCryptoServiceProvider']}
assertion=method('Assert').replace('        if (!condition)','        assertions++;\n        if (!condition)')
api_fixture=original[original.index('    [ExtensionApiContract(AllowMultipleProviders = false)]'):original.index('    [PhinixExtension("tests.base")]')]
client_fixtures=original[original.index('    private static readonly List<string> LifecycleEvents'):original.index('    private sealed class PipelineTestHarness')].replace(api_fixture,'')
server_fixtures=original[original.index('    private sealed class DelegateServerInboundMessageInterceptor'):original.index('    private static readonly List<string> LifecycleEvents')]+original[original.index('    private sealed class PipelineTestHarness'):original.rfind('\n}')]
common_usings='using System;\nusing System.IO;\nusing System.Linq;\nusing System.Collections.Generic;\nusing Google.Protobuf;\nusing Utils;\nusing Utils.Framework;\nusing System.Threading;\n'
client_project=(src/'Tests/Phase35RuntimeTests/Phase35RuntimeTests.csproj').read_text()
for side,repo,cases in [('Shared',shared,core),('ClientFull',client,client_cases),('ServerFull',server,server_cases)]:
 folder=repo/'Tests'/('Phase35'+('Core' if side=='Shared' else 'Client' if side=='ClientFull' else 'Server')+'RuntimeTests');folder.mkdir(parents=True,exist_ok=True)
 main='    private static int assertions;\n    private static int Main() { try {\n'+''.join('        '+n+'();\n' for n in cases)+'        Console.WriteLine("PASS '+side+' '+str(len(cases))+' cases; " + assertions + " assertions"); return 0;\n    } catch(Exception error) { Console.Error.WriteLine(error); return 1; } }\n'
 bodies='';extras=[]
 for name in cases:
  if name in ['AssertDisabledSettingRecoveryEntries','AssertClientExtensionRuntimeLifecycle','AssertClientLinkOpening']:continue
  body=method(name)
  if name=='AssertLegacyApisRemoved':
   if side=='ClientFull':
    body=body.replace('Path.Combine(repoRoot, "Common", "Connections", "NetClient.cs")','Path.Combine(repoRoot, "Client", "Common", "Connections.Client", "NetClient.cs")').replace('Path.Combine(repoRoot, "Common", "Authentication", "ClientAuthenticator.cs")','Path.Combine(repoRoot, "Client", "Common", "Authentication.Client", "ClientAuthenticator.cs")')
    body='\n'.join(l for l in body.split('\n') if '"NetServer.cs"' not in l)
   else:
    body=body.replace('Path.Combine(repoRoot, "Common", "Connections", "NetServer.cs")','Path.Combine(repoRoot, "Server", "Connections.Server", "NetServer.cs")')
    body='\n'.join(l for l in body.split('\n') if '"NetClient.cs"' not in l and '"ClientAuthenticator.cs"' not in l)
  bodies+=body
 if side=='Shared':
  extras=['DisabledSettingRecoveryTests.cs'];bodies+=api_fixture
  project='<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><ProjectReference Include="../../Common/Utils/Utils.csproj"/></ItemGroup></Project>'
  usings=common_usings
 elif side=='ClientFull':
  extras=['ClientExtensionRuntimeTests.cs','ClientLinkServiceTests.cs'];bodies+=method('ReadCredentialStore')+method('AssertState')+client_fixtures
  project=client_project
  project=re.sub(r'\s*<ProjectReference Include="[^\"]*ServerRuntime[^\"]*"\s*/>','',project)
  project=project.replace('..\\..\\Common\\Connections\\NetClient.cs','..\\..\\Client\\Common\\Connections.Client\\NetClient.cs').replace('..\\..\\Common\\Authentication\\ClientAuthenticator.cs','..\\..\\Client\\Common\\Authentication.Client\\ClientAuthenticator.cs')
  project=project.replace('..\\..\\Common\\','..\\..\\Dependencies\\Phinix.Common\\Common\\').replace('..\\..\\libs\\','..\\..\\Dependencies\\Phinix.Common\\libs\\')
  usings=common_usings+'using Authentication;\nusing Connections;\nusing PhinixClient.Framework;\n'
 else:
  bodies+=server_fixtures
  project='<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><ProjectReference Include="../../Server/ServerRuntime/ServerRuntime.csproj"/></ItemGroup></Project>'
  usings=common_usings+'using ServerRuntime;\n'
 if side!='Shared':
  marker='"PhinixClient.sln"' if side=='ClientFull' else '"Server", "Server.csproj"'
  bodies+=method('AssertFileDoesNotContain')+method('GetRepositoryRoot').replace('"Phinix.sln"',marker)
 for name in extras:
  rel='Tests/Phase35RuntimeTests/'+name;data=(src/rel).read_bytes();(folder/name).write_bytes(data);manifest['inputs'][rel]=hashlib.sha256(data).hexdigest()
 (folder/'Program.cs').write_text(usings+'internal static partial class Program\n{\n'+main+bodies+assertion+'}\n')
 (folder/(folder.name+'.csproj')).write_text(project)
manifest['inputs']['Tests/Phase35RuntimeTests/Program.cs']=hashlib.sha256((src/'Tests/Phase35RuntimeTests/Program.cs').read_bytes()).hexdigest()
# Baseline preserves original entry order; only reports counts and explicitly locates original source.
base=lab/'Phase35Baseline';base.mkdir(exist_ok=True)
(base/'Program.cs').write_text(original.replace(method('Assert'),assertion).replace('    private static int Main()', '    private static int assertions;\n    private static int Main()').replace('Console.WriteLine("All framework runtime tests passed.");','Console.WriteLine("PASS baseline 18 cases; " + assertions + " assertions");').replace('string repoRoot = GetRepositoryRoot();','string repoRoot = @"'+str(src)+'";'))
for name in ['ClientExtensionRuntimeTests.cs','ClientLinkServiceTests.cs','DisabledSettingRecoveryTests.cs']:(base/name).write_bytes((src/'Tests/Phase35RuntimeTests'/name).read_bytes())
project=client_project
project=re.sub(r'((?:Include)="|<HintPath>)([^"<>]+)("|</HintPath>)',lambda m:m.group(1)+str((src/'Tests/Phase35RuntimeTests'/m.group(2).replace('\\','/')).resolve())+m.group(3),project)
(base/'Phase35Baseline.csproj').write_text(project)
# Standalone server image context owns endpoint sources and the pinned Shared checkout.
copy('Dockerfile',server);copy('.dockerignore',server)
p=server/'Dockerfile';p.write_text(p.read_text().replace('COPY Common/ ./Common/\n','').replace('COPY libs/ ./libs/\n',''))
with (server/'.dockerignore').open('a') as f:f.write('\nTests/\n')
manifest['inputs']['Tests/Phase35RuntimeTests/Phase35RuntimeTests.csproj']=hashlib.sha256((src/'Tests/Phase35RuntimeTests/Phase35RuntimeTests.csproj').read_bytes()).hexdigest()
for repo in (shared,client,server):
 run('git','add','Tests','docs' if repo==client else 'Tests',cwd=repo)
 if repo==server:run('git','add','Dockerfile','.dockerignore',cwd=repo)
 manifest[repo.name+'Commit']=run('git','-c','commit.gpgsign=false','commit','-m','test(f5): migrate runtime checks by endpoint ownership',cwd=repo)
 manifest[repo.name+'Commit']=run('git','rev-parse','HEAD',cwd=repo)
# Consumers must now pin the shared tests snapshot, not a floating branch.
for repo in (client,server):
 run('git','-c','protocol.file.allow=always','submodule','update','--remote','Dependencies/Phinix.Common',cwd=repo)
 run('git','add','Dependencies/Phinix.Common',cwd=repo)
 run('git','-c','commit.gpgsign=false','commit','-m','test(f5): pin shared runtime test snapshot',cwd=repo)
 manifest[repo.name+'Commit']=run('git','rev-parse','HEAD',cwd=repo)
(lab/'f5c-manifest.json').write_text(json.dumps(manifest,indent=2));print(json.dumps({k:v for k,v in manifest.items() if k not in ['inputs']},indent=2))
