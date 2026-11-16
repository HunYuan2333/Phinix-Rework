#!/usr/bin/env python3
"""Exercise production storage sources using real SQLite on net10 and net472/Mono."""
import argparse,json,os,shutil,subprocess,xml.etree.ElementTree as E
from pathlib import Path
p=argparse.ArgumentParser(description=__doc__)
p.add_argument('--output',type=Path,required=True)
a=p.parse_args();root=Path(__file__).resolve().parents[2];out=a.output.resolve()
if out.exists() or out.is_relative_to(root.parent):p.error('Use a fresh isolated output.')
out.mkdir(parents=True);results=[]
for framework in ['net10.0','net472']:
 target=out/framework;target.mkdir();project=E.Element('Project',Sdk='Microsoft.NET.Sdk');props=E.SubElement(project,'PropertyGroup')
 for k,v in {'TargetFramework':framework,'OutputType':'Exe','EnableDefaultCompileItems':'false','NuGetAudit':'false','DefineConstants':'PHINIX_RIMWORLD_17' if framework=='net10.0' else ''}.items():E.SubElement(props,k).text=v
 items=E.SubElement(project,'ItemGroup')
 for directory in ['Client/Storage.Contracts','Client/Storage.Sqlite','Tests/ClientStorageRuntimeTests']:
  for source in sorted((root/directory).glob('*.cs')):E.SubElement(items,'Compile',Include=str(source))
 for name,version in [('Microsoft.Data.Sqlite.Core','10.0.13'),('SQLitePCLRaw.provider.e_sqlite3','2.1.13'),('SQLitePCLRaw.lib.e_sqlite3','2.1.13')]:E.SubElement(items,'PackageReference',Include=name,Version='['+version+']')
 E.ElementTree(project).write(target/'Probe.csproj',encoding='unicode')
 (target/'NuGet.Config').write_text('<configuration><packageSources><clear /><add key="local" value="'+str(Path.home()/'.nuget/packages')+'" /></packageSources></configuration>')
 for phase,cmd in [('build',['dotnet','build',str(target/'Probe.csproj'),'-c','Release','-m:1','-nr:false']),('test',(['dotnet'] if framework=='net10.0' else ['mono'])+[str(target/'bin/Release'/framework/('Probe.dll' if framework=='net10.0' else 'Probe.exe'))])]:
  with (target/(phase+'.log')).open('w') as log:r=subprocess.run(cmd,stdout=log,stderr=subprocess.STDOUT,timeout=180)
  results.append({'framework':framework,'phase':phase,'exit':r.returncode});print(framework,phase,r.returncode,flush=True)
  if r.returncode:print((target/(phase+'.log')).read_text()[-5000:]);break
 if r.returncode == 0:
  # Run the same real SQLite work in the exact distribution path relation.
  sdk=target/'bin/Release'/framework;package=target/'packaged';assemblies=package/'Assemblies';assemblies.mkdir(parents=True)
  for source in sdk.iterdir():
   if source.is_file():shutil.copy2(source,assemblies/source.name)
  shutil.copytree(sdk/'runtimes',package/'Native/runtimes')
  cmd=(['dotnet'] if framework=='net10.0' else ['mono'])+[str(assemblies/('Probe.dll' if framework=='net10.0' else 'Probe.exe'))]
  with (target/'packaged-test.log').open('w') as log:r=subprocess.run(cmd,stdout=log,stderr=subprocess.STDOUT,timeout=180)
  results.append({'framework':framework,'phase':'packaged-test','exit':r.returncode});print(framework,'packaged-test',r.returncode,flush=True)
  if r.returncode:print((target/'packaged-test.log').read_text()[-5000:])
(out/'results.json').write_text(json.dumps(results,indent=2)+'\n')
raise SystemExit(0 if len(results)==6 and all(r['exit']==0 for r in results) else 1)
