"""Check the old cohort fails and the new cohort succeeds in fresh loader processes."""
import argparse
import json
import subprocess
import xml.etree.ElementTree as ET
from pathlib import Path

p = argparse.ArgumentParser(description=__doc__)
for name in ['old', 'fixed', 'game16', 'game17', 'output']:
    p.add_argument('--' + name, type=Path, required=True)
a = p.parse_args()
repo = Path(__file__).resolve().parents[2]
out = a.output.resolve()
if out.is_relative_to(repo) or any(path.resolve().is_relative_to(out) for key,path in vars(a).items() if key!='output'):
    p.error('Use isolated output.')
if out.exists() and any(out.iterdir()):
    p.error('Choose fresh output.')
out.mkdir(parents=True,exist_ok=True)
source = Path(__file__).resolve().parent / 'Program.cs'
subprocess.run(['mcs','-out:'+str(out/'probe.exe'),str(source)],check=True)
project = ET.Element('Project',Sdk='Microsoft.NET.Sdk')
props = ET.SubElement(project,'PropertyGroup')
for k,v in {'OutputType':'Exe','TargetFramework':'net10.0','EnableDefaultCompileItems':'false','NuGetAudit':'false'}.items():
    ET.SubElement(props,k).text=v
ET.SubElement(ET.SubElement(project,'ItemGroup'),'Compile',Include=str(source))
ET.ElementTree(project).write(out/'probe.csproj',encoding='unicode')
(out/'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
with (out/'build.log').open('w') as log:
    subprocess.run(['dotnet','build',str(out/'probe.csproj'),'-c','Release','--nologo'],stdout=log,stderr=subprocess.STDOUT,check=True)
results=[]
for version,game in [('1.6',a.game16),('1.7',a.game17)]:
    for label,package,expected in [('old',a.old,1),('fixed',a.fixed,0)]:
        exe=['mono',str(out/'probe.exe')] if version=='1.6' else ['dotnet',str(out/'bin/Release/net10.0/probe.dll')]
        command=exe+[str(package/version),str(game),str(repo/'Extensions/PluginStore/Client/Assets/maintainers.json')]
        r=subprocess.run(command,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True)
        (out/(version+'-'+label+'.log')).write_text(r.stdout)
        if (r.returncode==0)!=(expected==0):
            raise RuntimeError('Unexpected '+version+' '+label+' loader result; see log.')
        if label=='old' and 'ClientPresentation.Contracts' not in r.stdout:
            raise RuntimeError('Old fixture did not fail for the intended contracts dependency.')
        results.append({'game':version,'case':label,'exit':r.returncode,'runtime':'system Mono' if version=='1.6' else 'system .NET10'})
(out/'results.json').write_text(json.dumps(results,indent=2))
print('Load-order regression: old failed/new passed on both system runtimes; game-native runtime testing remains pending.')
