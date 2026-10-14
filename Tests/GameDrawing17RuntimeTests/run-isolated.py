import argparse,subprocess,xml.etree.ElementTree as E
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--game',type=Path,required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args();source=Path(__file__).resolve().parent;out=a.output.resolve();game=a.game.resolve()
if out.is_relative_to(source.parents[2]) or out.is_relative_to(game):p.error('Use fresh isolated output.')
if out.exists() and any(out.iterdir()):p.error('Existing output is preserved.')
out.mkdir(parents=True);project=E.Element('Project',Sdk='Microsoft.NET.Sdk');props=E.SubElement(project,'PropertyGroup')
for k,v in {'OutputType':'Exe','TargetFramework':'net10.0','EnableDefaultCompileItems':'false','NuGetAudit':'false','DefineConstants':'PHINIX_RIMWORLD_17'}.items():E.SubElement(props,k).text=v
items=E.SubElement(project,'ItemGroup')
for f in [source/'Program.cs',source/'../../Client/ClientExtensionAbstractions/Game/Drawing/ClientGameDrawing17.cs']:E.SubElement(items,'Compile',Include=str(f.resolve()))
for name in ['UnityStubs','Waylith']:
 r=E.SubElement(items,'Reference',Include=name);E.SubElement(r,'HintPath').text=str(game/(name+'.dll'));E.SubElement(r,'Private').text='false'
E.ElementTree(project).write(out/'probe.csproj',encoding='unicode');(out/'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
with (out/'build.log').open('w') as log:result=subprocess.run(['dotnet','build',str(out/'probe.csproj'),'-c','Release','-v:q'],stdout=log,stderr=subprocess.STDOUT)
print('Native API compile exit:',result.returncode,flush=True)
if result.returncode:raise SystemExit(result.returncode)
run=out/'bin/Release/net10.0';(run/'probe.deps.json').unlink(missing_ok=True)
for f in game.glob('*.dll'):
 if not (run/f.name).exists():(run/f.name).symlink_to(f)
with (out/'results.log').open('w') as log:result=subprocess.run(['dotnet',str(run/'probe.dll')],stdout=log,stderr=subprocess.STDOUT)
print('Native API whitebox exit:',result.returncode,'log',out/'results.log');raise SystemExit(result.returncode)
