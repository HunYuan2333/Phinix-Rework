"""Compile/run real client transport/auth/user DLLs with explicit server DLLs; synthetic loopback only."""
import argparse, hashlib, json, subprocess, xml.etree.ElementTree as E
import platform, os, signal
from pathlib import Path
p=argparse.ArgumentParser(description=__doc__)
p.add_argument('--client',type=Path,required=True);p.add_argument('--server',type=Path,required=True);p.add_argument('--output',type=Path,required=True)
p.add_argument("--profile",choices=["transitional","identity"],default="transitional");a=p.parse_args();r=Path(__file__).resolve().parents[2];out=a.output.resolve()
if out.exists() or out.is_relative_to(r.parent):p.error('Use fresh isolated output outside workspace.')
refs={x.name:x for x in a.server.resolve().glob('*.dll') if not x.name.endswith('Tests.dll')}
for name in ['Connections.Client','Authentication.Client','UserManagement.Client']+(['PhinixClient'] if a.profile=='identity' else []):refs[name+'.dll']=a.client.resolve()/(name+'.dll')
if a.profile=='identity':
 # Parent is the actual client cohort; independent production Main loads its own server SDK.
 # Do not silently unify client wire codecs with the server's different pinned Common build.
 for name in ['Google.Protobuf','Utils','Connections','Authentication','UserManagement','ServerTransport','ChatExtension','TradeExtension']:
  refs[name+'.dll']=a.client.resolve()/(name+'.dll')
if not all(x.is_file() for x in refs.values()):p.error('Missing built joint closure.')
out.mkdir(parents=True);project=E.Element('Project',Sdk='Microsoft.NET.Sdk');props=E.SubElement(project,'PropertyGroup')
for k,v in {'TargetFramework':'net10.0','OutputType':'Exe','AssemblyName':'ClientTlsRuntimeTests','EnableDefaultCompileItems':'false','ImplicitUsings':'enable','NuGetAudit':'false'}.items():E.SubElement(props,k).text=v
items=E.SubElement(project,'ItemGroup');source=r/('Tests/P4IdentityJointTests/Program.cs' if a.profile=='identity' else 'Tests/ClientTlsRuntimeTests/Program.cs');E.SubElement(items,'Compile',Include=str(source))
for name,path in refs.items():
 node=E.SubElement(items,'Reference',Include=path.stem);E.SubElement(node,'HintPath').text=str(path);E.SubElement(node,'Private').text='true'
if a.profile=='identity':
 rid=('win-' if platform.system()=='Windows' else 'linux-')+('arm64' if platform.machine().lower() in ['aarch64','arm64'] else 'x64')
 native=a.server.resolve()/'runtimes'/rid/'native'/('e_sqlite3.dll' if platform.system()=='Windows' else 'libe_sqlite3.so')
 if not native.is_file():p.error('Missing actual SQLite native runtime asset.')
 item=E.SubElement(items,'Content',Include=str(native));E.SubElement(item,'Link').text=native.name;E.SubElement(item,'CopyToOutputDirectory').text='PreserveNewest'
 refs[native.name]=native
E.ElementTree(project).write(out/'Probe.csproj',encoding='unicode');(out/'NuGet.Config').write_text('<configuration><packageSources><clear/></packageSources></configuration>')
steps=[]
for name,cmd in [('build',['dotnet','build',str(out/'Probe.csproj'),'-c','Release','-m:1','-nr:false']),('test',['dotnet',str(out/'bin/Release/net10.0/ClientTlsRuntimeTests.dll')]+(['--production-server',str(a.server.resolve()/'PhinixServer.dll')] if a.profile=='identity' else []))]:
 timed_out=False
 with (out/(name+'.log')).open('w') as log:
  process=subprocess.Popen(cmd,stdout=log,stderr=subprocess.STDOUT,start_new_session=True)
  try:process.wait(timeout=180)
  except BaseException as error:
   if os.name=='posix':os.killpg(process.pid,signal.SIGKILL)
   else:subprocess.run(['taskkill','/PID',str(process.pid),'/T','/F'],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
   process.wait()
   if not isinstance(error,subprocess.TimeoutExpired):raise
   timed_out=True
 steps.append({'phase':name,'command':cmd,'exit':process.returncode,'timedOut':timed_out})
 if process.returncode or timed_out:break
m={'passed':len(steps)==2 and all(x['exit']==0 for x in steps),'steps':steps,'inputs':[{'path':str(x),'sha256':hashlib.sha256(x.read_bytes()).hexdigest()} for x in [Path(__file__).resolve(),source,*refs.values()]],'realGame':False,'profile':a.profile}
(out/'manifest.json').write_text(json.dumps(m,indent=2)+'\n');print(json.dumps({'passed':m['passed'],'output':str(out)}));raise SystemExit(0 if m['passed'] else 1)
