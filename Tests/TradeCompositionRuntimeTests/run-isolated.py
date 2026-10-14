# Local-only boundary probe. Rebuilds selected consumers against isolated references.
# This is not the release build: other infrastructure dependencies are seeded from Output.
import argparse
import hashlib
import pathlib as P, xml.etree.ElementTree as E, glob, re, subprocess, json, sys, shutil
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--version', choices=['1.6', '1.7'], required=True)
parser.add_argument('--game', type=P.Path, required=True, help='Exact local game reference directory')
parser.add_argument('--harmony', type=P.Path, required=True, help='Exact Harmony DLL for this probe')
parser.add_argument('--output', type=P.Path, required=True, help='Fresh directory outside the workspace/game')
parser.add_argument('--infrastructure', type=P.Path, help='Freshly rebuilt Host Output root; defaults to the preserved original Output baseline')
parser.add_argument('--autofac', type=P.Path, help='Same pinned Autofac, runtime-matched asset; required for 1.7')
args = parser.parse_args()
repo = P.Path(__file__).resolve().parents[2]
root = repo.parent
ver = args.version
base = args.output.resolve()
game = args.game.resolve()
for protected in (root, game, args.harmony.resolve().parent):
    if base.is_relative_to(protected) or protected.is_relative_to(base):
        parser.error('Output must be isolated from workspace, game and Harmony directories.')
if base.exists() and any(base.iterdir()):
    parser.error('Use a fresh output directory; existing probes/baselines are preserved.')
if ver == '1.7' and args.autofac is None:
    parser.error('Supply the net8.0 asset of the existing pinned Autofac package for 1.7.')
for required in (['Assembly-CSharp.dll', 'UnityStubs.dll'] if ver == '1.7' else ['Assembly-CSharp.dll']):
    if not (game/required).is_file():
        parser.error('Missing exact game reference: '+str(game/required))
base.mkdir(parents=True, exist_ok=True)
def fingerprint(path):
    return {'path': str(path), 'sha256': hashlib.sha256(path.read_bytes()).hexdigest()}

projects=['Client/PresentationContracts/ClientPresentation.Contracts.csproj','Client/ClientExtensionAbstractions/ClientExtensionAbstractions.csproj','Client/Composition/Phinix.ClientComposition.csproj','Extensions/Trade/Contracts/TradeExtension.csproj','Extensions/Chat/Contracts/ChatExtension.csproj','Extensions/Inventory/Contracts/InventoryExtension.csproj','Extensions/Inventory/GameAdapter/InventoryExtension.GameAdapter.csproj','Extensions/Inventory/Client/InventoryExtension.Client.csproj','Extensions/Trade/Client/TradeExtension.Client.csproj','Extensions/Chat/Client/ChatExtension.Client.csproj','Client/Source/Client.csproj','Extensions/PluginStore/Client/PluginStore.Client.csproj','../Phinix-Legacy-RedPacket/Contracts/LegacyRedPacketExtension.csproj','../Phinix-Legacy-RedPacket/Client/LegacyRedPacketExtension.Client.csproj','../Phinix-Legacy-TalentTrade/Contracts/LegacyTalentTradeExtension.csproj','../Phinix-Legacy-TalentTrade/Client/LegacyTalentTradeExtension.Client.csproj','../Phinix-Example-Plugin/example/Example.csproj','Tests/TradeCompositionRuntimeTests/TradeCompositionRuntimeTests.csproj','Tests/InventoryGameAdapterRuntimeTests/InventoryGameAdapterRuntimeTests.csproj','Tests/InventoryRuntimeTests/Portable/InventoryRuntimeTests.Portable.csproj','Tests/InventoryCompositionRuntimeTests/InventoryCompositionRuntimeTests.csproj','../Phinix-Example-Plugin/Tests/ExampleLifecycleTests.csproj','Tests/PresentationValuesRuntimeTests/PresentationValuesRuntimeTests.csproj']
infrastructure = args.infrastructure.resolve() if args.infrastructure else repo/'Output'
if not infrastructure.is_dir(): parser.error('Missing infrastructure Output root.')
refs={re.sub(r'^\d+-','',p.stem):p for p in infrastructure.rglob('*.dll')}
for p in game.glob('*.dll'):
 if ver=='1.6' or p.stem in ['Assembly-CSharp','UnityStubs','Waylith','Compat','com.rlabrecque.steamworks.net']: refs[p.stem]=p
refs['0Harmony']=args.harmony.resolve()
if ver=='1.7':
 refs={n:p for n,p in refs.items() if not n.startswith(('System.','Microsoft.Bcl'))}
 refs['Autofac']=args.autofac.resolve()
(base/'input-fingerprints.json').write_text(json.dumps({n: fingerprint(p) for n,p in refs.items()}, indent=2))
(base/'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
results=[]
source_fingerprints={}
for relative in projects:
 original=(repo/relative).resolve(); tree=E.parse(original); elements=list(tree.iter()); tag=lambda e:e.tag.split('}')[-1]
 names=[e.text for e in elements if tag(e)=='AssemblyName']; name=names[0] if names else original.stem
 explicit=[e for e in elements if tag(e)=='Compile' and e.get('Include')]; files=set()
 default=not explicit or (tree.getroot().get('Sdk') and not any(tag(e)=='EnableDefaultCompileItems' and e.text=='false' for e in elements))
 if default: files.update(p for p in original.parent.rglob('*.cs') if not any(s in ('obj','bin','Fixtures') for s in p.relative_to(original.parent).parts))
 for e in explicit:
  for pattern in e.get('Include').split(';'):
   files.update(P.Path(p).resolve() for p in glob.glob(str(original.parent/pattern.replace('\\','/')),recursive=True) if '/obj/' not in p and '/bin/' not in p)
 source_fingerprints[name] = [fingerprint(p) for p in sorted(files)]
 (base/'source-fingerprints.json').write_text(json.dumps(source_fingerprints, indent=2))
 dest=base/name; dest.mkdir(exist_ok=True); pr=E.Element('Project',Sdk='Microsoft.NET.Sdk'); pg=E.SubElement(pr,'PropertyGroup')
 for k,v in {'TargetFramework':'net10.0' if ver=='1.7' else 'net472','AssemblyName':name,'OutputType':'Exe' if 'Tests' in name else 'Library','GenerateAssemblyInfo':'true' if name=='ClientPresentation.Contracts' else 'false','EnableDefaultCompileItems':'false','NuGetAudit':'false','NoWarn':'SYSLIB0003;SYSLIB0050;SYSLIB0051','AllowUnsafeBlocks':'true','Nullable':'disable'}.items(): E.SubElement(pg,k).text=v
 if ver=='1.7': E.SubElement(pg,'DefineConstants').text='PHINIX_RIMWORLD_17'
 ig=E.SubElement(pr,'ItemGroup')
 for p in sorted(files): E.SubElement(ig,'Compile',Include=str(p))
 for n,p in refs.items():
  if n==name: continue
  if name=='ClientPresentation.Contracts' and n not in ('Utils', 'Protobuf'): continue
  ref=E.SubElement(ig,'Reference',Include=n); E.SubElement(ref,'HintPath').text=str(p); E.SubElement(ref,'Private').text='false'
 if ver=='1.6':
  for n in ['System.Net.Http','System.IO.Compression','System.IO.Compression.FileSystem']: E.SubElement(ig,'Reference',Include=n)
 path=dest/(name+'.csproj'); E.ElementTree(pr).write(path,encoding='unicode')
 with (dest/'build.log').open('w') as out: r=subprocess.run(['dotnet','build',str(path),'-c','Release','--nologo','-v:q'],stdout=out,stderr=subprocess.STDOUT)
 print(ver,name,len(files),'exit',r.returncode,flush=True); results.append({'name':name,'sources':len(files),'exit':r.returncode})
 (base/'results.json').write_text(json.dumps(results,indent=2))
 if r.returncode: sys.exit(r.returncode)
 refs[name]=dest/'bin/Release'/('net10.0' if ver=='1.7' else 'net472')/(name+('.exe' if ver=='1.6' and 'Tests' in name else '.dll'))
run=base/'run'; run.mkdir(exist_ok=True)
for n,p in refs.items():
 if not n.startswith('System.') and p.exists(): shutil.copy2(p,run/(n+p.suffix))
if ver=='1.7':
 for p in game.glob('*.dll'):
  if not (run/p.name).exists(): shutil.copy2(p,run/p.name)
# Example's existing probe loads these conventional names explicitly; use fresh identities.
for packaged, name in [('02-Protobuf', 'Protobuf'), ('03-Utils', 'Utils'), ('10-ClientExtensionAbstractions', 'ClientExtensionAbstractions')]:
    shutil.copy2(refs[name], run/(packaged+'.dll'))
# Existing Inventory composition probes inspect the host's conventional relative path.
# Populate that path only inside the isolated run directory with the freshly built Host.
host_probe = run/'Output/phinix-rework/1.6/Assemblies'
host_probe.mkdir(parents=True, exist_ok=True)
shutil.copy2(refs['PhinixClient'], host_probe/'13-PhinixClient.dll')
test_results = []
for test in ('TradeCompositionRuntimeTests', 'InventoryGameAdapterRuntimeTests', 'InventoryRuntimeTests', 'InventoryCompositionRuntimeTests', 'ExampleLifecycleTests', 'PresentationValuesRuntimeTests'):
    (run/(test+'.runtimeconfig.json')).write_text(json.dumps({'runtimeOptions':{'tfm':'net10.0','framework':{'name':'Microsoft.NETCore.App','version':'10.0.0'}}}))
    with (base/(test+'.log')).open('w') as out:
        result=subprocess.run(['dotnet',str(run/(test+('.exe' if ver=='1.6' else '.dll')))],cwd=run,stdout=out,stderr=subprocess.STDOUT)
    print(ver, test, 'exit', result.returncode, flush=True)
    test_results.append({'name': test, 'exit': result.returncode})
(base/'verification.json').write_text(json.dumps({'version': ver, 'compile_projects': len(results), 'tests': test_results, 'scope': 'Selected source consumers plus seeded infrastructure, non-GUI tests on system .NET; no game acceptance.'}, indent=2))
sys.exit(0 if all(test['exit']==0 for test in test_results) else 1)
