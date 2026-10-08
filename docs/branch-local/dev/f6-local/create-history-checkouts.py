"""Create local F6 history candidates from accepted F5 sources; never mutate source refs."""
from pathlib import Path
import argparse,subprocess,json,hashlib,shutil,os
p=argparse.ArgumentParser();p.add_argument('--snapshot-lab',required=True,type=Path);p.add_argument('--output',required=True,type=Path);a=p.parse_args()
src=Path.cwd().resolve();snapshot=a.snapshot_lab.resolve();out=a.output.resolve()
if not (src/'Phinix.sln').exists() or out.exists():raise SystemExit('Run from original repository root and choose a new output directory.')
out.mkdir();(out/'logs').mkdir();manifest={'originalHead':subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip(),'snapshotLab':str(snapshot),'repositories':{}}
common_url='https://github.com/HunYuan2333/Phinix-Common.git';proto_url='https://github.com/protocolbuffers/protobuf.git'
def run(args,cwd=None,name=None):
 r=subprocess.run(args,cwd=cwd,text=True,stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
 if name:(out/'logs'/name).write_text(r.stdout)
 if r.returncode:raise RuntimeError(str(args)+'\n'+r.stdout[-3000:])
 return r.stdout.strip()
def files(repo):return [x for x in run(['git','ls-files','-z'],cwd=repo).split('\0') if x]
def commit(repo,msg):
 run(['git','add','.'],cwd=repo);run(['git','-c','commit.gpgsign=false','commit','-m',msg],cwd=repo);return run(['git','rev-parse','HEAD'],cwd=repo)
for name,side,paths in [
 ('Phinix-Common','Shared',['Common','Extensions/Chat/Contracts','Extensions/Trade/Contracts','libs','Dependencies/protobuf','.gitmodules','Directory.Build.props','Directory.Build.targets','nuget.config','Tests/ExtensionLoaderRuntimeTests']),
 ('Phinix-Rework','ClientFull',None),
 ('Phinix-Server','ServerFull',['Server','Extensions/Chat/Server','Extensions/Trade/Server','Common/Connections/NetServer.cs','Dockerfile','.dockerignore','docker-compose.yml','.github/workflows/docker.yml','Directory.Build.props','Directory.Build.targets','nuget.config'])]:
 repo=out/name
 run(['git','clone','--no-hardlinks','--no-tags','--single-branch','--branch','dev',str(src),str(repo)],name=name+'-clone.log')
 run(['git','config','user.name','Phinix migration candidate'],cwd=repo);run(['git','config','user.email','local-migration@invalid.example'],cwd=repo)
 if paths:
  script='git read-tree --empty; git ls-tree -r "$GIT_COMMIT" -- '+' '.join(paths)+' | git update-index --index-info'
  if side=='Shared':script+='; git update-index --force-remove -- Common/Connections/NetClient.cs Common/Connections/NetServer.cs Common/Authentication/ClientAuthenticator.cs Common/UserManagement/ClientUserManager.cs'
  run(['git','filter-branch','--force','--prune-empty','--index-filter',script,'--','HEAD'],cwd=repo,name=name+'-history.log')
 ancestry=run(['git','rev-parse','HEAD'],cwd=repo)
 # Replace only this new local candidate's tracked tip tree, preserving history below it.
 run(['git','rm','-r','--ignore-unmatch','--','.'],cwd=repo)
 frozen=snapshot/('Shared' if side=='Shared' else 'replay/'+side)
 hashes={}
 for rel in files(frozen):
  source=frozen/rel
  if not source.is_file():continue
  if rel=='.gitmodules':continue
  dest=repo/rel;dest.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(source,dest);hashes[rel]=hashlib.sha256(source.read_bytes()).hexdigest()
 (repo/'.gitmodules').write_text('')
 run(['git','add','.gitmodules'],cwd=repo)
 if side=='Shared':
  # Preserve the accepted nested protobuf gitlink exactly, with its canonical public URL.
  mirror=snapshot/'protobuf.git'
  run(['git','-c','protocol.file.allow=always','submodule','add',str(mirror),'Dependencies/protobuf'],cwd=repo)
  pinned=run(['git','rev-parse','HEAD'],cwd=frozen/'Dependencies/protobuf')
  run(['git','checkout','--detach',pinned],cwd=repo/'Dependencies/protobuf')
  (repo/'.gitmodules').write_text('[submodule "Dependencies/protobuf"]\n\tpath = Dependencies/protobuf\n\turl = '+proto_url+'\n')
  run(['git','config','submodule.Dependencies/protobuf.url',str(mirror)],cwd=repo)
 else:
  shared=out/'Phinix-Common'
  run(['git','-c','protocol.file.allow=always','submodule','add',str(shared),'Dependencies/Phinix.Common'],cwd=repo)
  # Canonical URLs are committed; local build acquisition uses explicit URL overrides.
  (repo/'.gitmodules').write_text('[submodule "Dependencies/Phinix.Common"]\n\tpath = Dependencies/Phinix.Common\n\turl = '+common_url+'\n')
  run(['git','config','submodule.Dependencies/Phinix.Common.url',str(shared)],cwd=repo)
  run(['git','-c','protocol.file.allow=always','-c','url.'+str(snapshot/'protobuf.git')+'.insteadOf='+proto_url,'submodule','update','--init','--recursive'],cwd=repo)
  if side=='ServerFull':
   # Prepare the existing publisher in its new sole-owner candidate; do not execute or publish it.
   for rel in ['.github/workflows/docker.yml','docker-compose.yml']:
    if (src/rel).is_file():dest=repo/rel;dest.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(src/rel,dest)
  if side=='ClientFull':
   (repo/'Phinix.sln').write_bytes((repo/'PhinixClient.sln').read_bytes())
   for rel in ['.github/scripts/check-artifacts.ps1','.github/scripts/test-client-package-layout.py']:
    dest=repo/rel;dest.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(src/rel,dest)
 # Artifact checks belong to one endpoint and do not require a neighboring checkout.
 artifact=(src/'.github/scripts/check-artifacts.ps1').read_text()
 if side=='ClientFull':
  (repo/'.github/scripts/check-artifacts.ps1').write_text("param([switch]$IncludeClient = $true)\n\n$ErrorActionPreference = 'Stop'\n$required = @()\n$artifactRoots = @()\n"+artifact[artifact.index('$compositionRuntimeNames = @('):])
 elif side=='ServerFull':
  path=repo/'.github/scripts/check-artifacts.ps1';path.parent.mkdir(parents=True,exist_ok=True);path.write_text(artifact)
 # Stable constraints travel with their actual sources; branch working notes stay in original source.
 for rel in ['docs/Design-Philosophy.md','docs/设计哲学.md','docs/Compatibility-Boundaries.md','docs/Inventory.md']:
  if (src/rel).is_file():dest=repo/rel;dest.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(src/rel,dest)
 role='game-independent common/contracts' if side=='Shared' else 'RimWorld client and client plugins' if side=='ClientFull' else 'server and server plugins'
 (repo/'AGENTS.md').write_text('# '+name+'\n\nOwns '+role+'. Read docs/Design-Philosophy.md and docs/Compatibility-Boundaries.md before changing boundaries or recovery. Preserve plugin parity, authoritative acknowledgements, all-or-nothing item delivery and main-thread game dispatch. Preserve dirty changes. Never commit credentials, server data/logs, GameDlls or build output. Do not edit vendored protobuf SDK pins/source for compatibility. Shared consumers pin exact gitlinks, never update --remote during normal acquisition. No additional Phinix NuGet SDK channel.\n')
 (repo/'README.md').write_text('# '+name+'\n\nLocal F6 migration candidate from '+manifest['originalHead']+'. Owns '+role+'.\n\nPreserves '+('original client history' if side=='ClientFull' else 'filtered relevant dev ancestry')+'; legacy tags/other branches are not republished here. Canonical submodule URLs are prepared; new remote repositories/commits are not yet published. Use explicit local URL overrides for rehearsal. Existing deployment names, wire identities and persistent formats are unchanged. Root licensing/notice policy requires an explicit publication decision; no new license has been invented.\n\n正式迁移本地候选：保留相关历史，子模块固定提交，远端尚未发布。构建需要递归获取；客户端自行提供合法游戏编译引用，禁止入 Git 或安装包。未更改网络/存档/物品归属规则。根许可证与发布说明仍需明确，不擅自编造许可证。\n')
 run(['git','remote','set-url','origin','https://github.com/HunYuan2333/'+name+'.git'],cwd=repo)
 head=commit(repo,'refactor: prepare '+name+' migration from accepted F5 snapshot')
 manifest['repositories'][name]={'commit':head,'historyParent':ancestry,'historyCommits':int(run(['git','rev-list','--count','HEAD'],cwd=repo)),'overlayInputHashes':hashes,'origin':run(['git','remote','get-url','origin'],cwd=repo)}
 (out/'manifest.json').write_text(json.dumps(manifest,indent=2));print(name,head,'ready',flush=True)
# Record every nested gitlink; never silently publish local-only URLs or unfiltered backup refs.
for name,row in manifest['repositories'].items():
 repo=out/name;row['gitlinks']=run(['git','submodule','status','--recursive'],cwd=repo)
 assert not run(['git','status','--porcelain'],cwd=repo),name
 if name=='Phinix-Rework':assert run(['git','merge-base','--is-ancestor',manifest['originalHead'],'HEAD'],cwd=repo)==''
(out/'manifest.json').write_text(json.dumps(manifest,indent=2));print('Local history candidates complete:',out)
