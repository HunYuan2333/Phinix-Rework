"""Provision ignored compilation references from explicit private inputs, recording hashes."""
from pathlib import Path
import argparse,zipfile,shutil,json,hashlib
p=argparse.ArgumentParser();p.add_argument('--client',type=Path,required=True);p.add_argument('--game-references',type=Path,required=True);p.add_argument('--harmony-package',type=Path,required=True);p.add_argument('--nuget-cache',type=Path,required=True)
a=p.parse_args();root=a.client.resolve();manifest={}
if not (root/'PhinixClient.sln').exists():raise SystemExit('Client target must be a local full-consumer checkout.')
def unpack(package,folder):
 target=root/'.nuget'/folder;target.mkdir(parents=True,exist_ok=True)
 with zipfile.ZipFile(package) as z:
  if any(not (target/n).resolve().is_relative_to(target.resolve()) for n in z.namelist()):raise ValueError('Package path escapes reference directory')
  z.extractall(target)
 manifest[str(package.resolve())]=hashlib.sha256(package.read_bytes()).hexdigest()
unpack(a.harmony_package,'Lib.Harmony.2.3.6')
for name,version,folder in [('system.memory','4.5.3','System.Memory.4.5.3'),('system.runtime.compilerservices.unsafe','4.5.2','System.Runtime.CompilerServices.Unsafe.4.5.2')]:
 unpack(a.nuget_cache/name/version/(name+'.'+version+'.nupkg'),folder)
shutil.copytree(a.game_references,root/'GameDlls',dirs_exist_ok=True)
for game in a.game_references.rglob('*.dll'):manifest[str(game.resolve())]=hashlib.sha256(game.read_bytes()).hexdigest()
(root/'.nuget/private-reference-manifest.json').write_text(json.dumps(manifest,indent=2))
print('Provisioned',len(manifest),'hashed private inputs; keep .nuget and GameDlls outside Git/distribution.')
