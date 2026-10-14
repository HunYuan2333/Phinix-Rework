"""Snapshot and build the real 1.6 client graph without writing source bin/obj/Output.
Game references are compile-only. This is not a release or a game acceptance check.
"""
import argparse, hashlib, json, os, shutil, subprocess
from pathlib import Path
p=argparse.ArgumentParser(description=__doc__)
p.add_argument('--output',type=Path,required=True)
p.add_argument('--game',type=Path,required=True)
p.add_argument('--plugin-root',type=Path,help='Build only this standalone plugin checkout; no sibling repositories required.')
p.add_argument('--plugin-version',help='Override generated plugin version for an immutable release.')
p.add_argument('--plugins',action='store_true',help='Build all three maintained plugins against this snapshot, without packing.')
p.add_argument('--test-locks',action='store_true',help='Restore the three locked test graphs too.')
p.add_argument('--refresh-locks',action='store_true',help='Refresh only the isolated snapshot; never change source locks.')
a=p.parse_args();repo=Path(__file__).resolve().parents[2];out=a.output.resolve();game=a.game.resolve()
if out.is_relative_to(repo) or repo.is_relative_to(out):p.error('Output must be outside the repository.')
if out.exists() and any(out.iterdir()):p.error('Use fresh output; previous snapshots are preserved.')
if not (game/'Assembly-CSharp.dll').is_file():p.error('Missing game Assembly-CSharp.dll.')
out.mkdir(parents=True);snapshot=out/'Phinix-Rework';snapshot.mkdir()
ignored=shutil.ignore_patterns('.git','.codex','.agents','bin','obj','Output','GameDlls','.nuget','__pycache__','*.log')
for name in ['Client','Extensions','Dependencies','Tests']:
 shutil.copytree(repo/name,snapshot/name,ignore=ignored)
for name in ['PhinixClient.sln','Directory.Build.props','Directory.Build.targets']:
 shutil.copy2(repo/name,snapshot/name)
# Test protocol fixtures are owned documentation, not player/runtime data.
protocol=Path('docs/branch-local/dev/plugin-store/managed-store-protocol-v3')
(snapshot/protocol).parent.mkdir(parents=True,exist_ok=True)
shutil.copytree(repo/protocol,snapshot/protocol)
(snapshot/'GameDlls').symlink_to(game,target_is_directory=True)
(snapshot/'.nuget').symlink_to(repo/'.nuget',target_is_directory=True)
(out/'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
def digest(path):return hashlib.sha256(path.read_bytes()).hexdigest()
inputs={str(path.relative_to(snapshot)):digest(path) for path in snapshot.rglob('*') if path.is_file() and path.suffix in ('.cs','.csproj','.props','.targets','.sln','.json') and 'GameDlls' not in path.parts and '.nuget' not in path.parts}
(out/'source-fingerprints.json').write_text(json.dumps(inputs,indent=2))
(out/'reference-fingerprints.json').write_text(json.dumps({str(path):digest(path) for path in game.glob('*.dll')},indent=2))
env=dict(os.environ);sdk=subprocess.check_output(['dotnet','--version'],text=True).strip()
# Use the installed SDK for project evaluation, without changing vendored global.json.
sdkroot=Path(shutil.which('dotnet')).resolve().parent/'sdk'/sdk/'Sdks'
env['MSBuildSDKsPath']=str(sdkroot)
base=['-p:Configuration=Release 1.6','-p:BuildInParallel=false','-p:NuGetAudit=false','-p:MSBuildEnableWorkloadResolver=false','-m:1']
commands=[('restore',['dotnet','restore','PhinixClient.sln',*base,'-p:RestoreLockedMode='+('false' if a.refresh_locks else 'true')]),('build',['dotnet','build','PhinixClient.sln','--no-restore',*base])]
if a.test_locks:
 for name in ['ManagedExtensionRuntimeTests','PluginStoreRuntimeTests','ClientCompositionRuntimeTests']:
  commands.insert(-1,('restore-'+name,['dotnet','restore','Tests/'+name+'/'+name+'.csproj',*base,'-p:RestoreLockedMode='+('false' if a.refresh_locks else 'true')]))
plugin_roots=[repo.parent/name for name in ['Phinix-Legacy-RedPacket','Phinix-Legacy-TalentTrade','Phinix-Example-Plugin']] if a.plugins else []
if a.plugin_root:plugin_roots.append(a.plugin_root.resolve())
for source in plugin_roots:
 plugin=source.name
 shutil.copytree(source,out/plugin,ignore=ignored)
 candidates=list((out/plugin/'Client').glob('*.csproj')) if (out/plugin/'Client').exists() else list((out/plugin/'example').glob('*.csproj'))
 if len(candidates)!=1:p.error('Expected one standalone plugin client project: '+str(source))
 command=['dotnet','build',str(candidates[0]),'-c','Release','-p:BuildInParallel=false','-p:NuGetAudit=false','-p:PhinixRoot='+str(snapshot),'-p:PhinixPackage='+str(snapshot/'Output/phinix-rework'),'-p:GameReferences='+str(game),'-p:HarmonyReferences='+str(repo/'.nuget/Lib.Harmony.2.3.6/lib/net472'),'-m:1']
 if a.plugin_version:command+=['-p:Version='+a.plugin_version,'-p:AssemblyVersion='+a.plugin_version+'.0']
 commands.append(('build-'+plugin,command))
results=[]
for name,cmd in commands:
 with (out/(name+'.log')).open('w') as log:r=subprocess.run(cmd,cwd=snapshot,env=env,stdout=log,stderr=subprocess.STDOUT)
 results.append({'phase':name,'command':cmd,'exit':r.returncode});print(name,'exit',r.returncode,'log',out/(name+'.log'),flush=True)
 (out/'results.json').write_text(json.dumps(results,indent=2))
 if r.returncode:raise SystemExit(r.returncode)
print('Real client graph built inside snapshot; no game acceptance or release.')
