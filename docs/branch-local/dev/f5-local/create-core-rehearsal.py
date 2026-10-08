from pathlib import Path
import subprocess,tempfile,json,shutil,hashlib,os
source=Path.cwd().resolve()
if not (source / "Phinix.sln").is_file():
 raise SystemExit("Run from the Phinix-Rework repository root.")
lab=Path(tempfile.mkdtemp(prefix='phinix-f5-local-20261008-'))
(lab/'logs').mkdir();(lab/'empty-feed').mkdir()
manifest={'lab':str(lab),'sourceHead':subprocess.check_output(['git','rev-parse','HEAD'],cwd=source,text=True).strip(),'scope':'F5-A core shared snapshot and minimal endpoint consumers; no full host extraction','inputs':{}}
def run(*args,cwd=None):
 result=subprocess.run(args,cwd=cwd,text=True,stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
 if result.returncode:raise RuntimeError(str(args)+'\n'+result.stdout)
 return result.stdout.strip()
def init(path):
 path.mkdir();run('git','init','-b','main',str(path))
 run('git','config','user.name','Phinix local rehearsal',cwd=path);run('git','config','user.email','local-rehearsal@invalid.example',cwd=path)
 (path/'.gitignore').write_text('**/bin/\n**/obj/\n*.user\n*.log\nOutput/\n')
def commit(path,message):
 run('git','add','.',cwd=path);run('git','-c','commit.gpgsign=false','commit','-m',message,cwd=path)
 return run('git','rev-parse','HEAD',cwd=path)
def copy(relative,dest):
 data=(source/relative).read_bytes();dest.parent.mkdir(parents=True,exist_ok=True);dest.write_bytes(data)
 manifest['inputs'][relative]=hashlib.sha256(data).hexdigest()
shared=lab/'Shared';init(shared)
endpoint_files={'Common/Connections/NetClient.cs','Common/Connections/NetServer.cs','Common/Authentication/ClientAuthenticator.cs','Common/UserManagement/ClientUserManager.cs'}
files=run('git','ls-files','-z','--','Common','libs','Directory.Build.props','Directory.Build.targets',cwd=source).split('\0')
for relative in files:
 if relative and relative not in endpoint_files:copy(relative,shared/relative)
(shared/'README.md').write_text('# Local Shared rehearsal\n\nCore Common only, from source '+manifest['sourceHead']+'. Endpoint source belongs to consumer projects. Chat/Trade contracts, host packaging, mixed tests and Docker remain later F5 batches. No GitHub remote.\n')
# Copy the vendor Git object store from disk, preserving its exact commit without hardlink dependency.
run('git','clone','--bare','--no-hardlinks',str(source/'Dependencies/protobuf'),str(lab/'protobuf.git'))
proto=run('git','rev-parse','HEAD',cwd=source/'Dependencies/protobuf');manifest['protobuf']=proto
run('git','-c','protocol.file.allow=always','submodule','add',str(lab/'protobuf.git'),'Dependencies/protobuf',cwd=shared)
run('git','checkout','--detach',proto,cwd=shared/'Dependencies/protobuf')
manifest['sharedCommit']=commit(shared,'test(f5): snapshot endpoint-neutral core and pin local protobuf')
for side,framework in [('ClientProbe','net472'),('ServerProbe','net10.0')]:
 repo=lab/side;init(repo)
 shutil.copy2(shared/'Directory.Build.props',repo/'Directory.Build.props');shutil.copy2(shared/'Directory.Build.targets',repo/'Directory.Build.targets')
 run('git','-c','protocol.file.allow=always','submodule','add',str(shared),'Dependencies/Phinix.Common',cwd=repo)
 run('git','-c','protocol.file.allow=always','submodule','update','--init','--recursive',cwd=repo)
 networking=repo/'Networking';networking.mkdir()
 endpoint='NetClient' if side=='ClientProbe' else 'NetServer'
 copy('Common/Connections/'+endpoint+'.cs',networking/(endpoint+'.cs'))
 assembly='Connections.Client' if side=='ClientProbe' else 'Connections.Server'
 lib='net35' if side=='ClientProbe' else 'netstandard2.0'
 (networking/'Networking.csproj').write_text('''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>'''+framework+'''</TargetFramework><AssemblyName>'''+assembly+'''</AssemblyName><LangVersion>7.3</LangVersion><GenerateAssemblyInfo>false</GenerateAssemblyInfo></PropertyGroup><ItemGroup><ProjectReference Include="../Dependencies/Phinix.Common/Common/Connections/Connections.csproj"/><ProjectReference Include="../Dependencies/Phinix.Common/Common/Utils/Utils.csproj"/><Reference Include="LiteNetLib"><HintPath>../Dependencies/Phinix.Common/libs/'''+lib+'''/LiteNetLib.dll</HintPath><Private>true</Private></Reference></ItemGroup></Project>''')
 copy(('Client/Common/Connections.Client' if side=='ClientProbe' else 'Server/Connections.Server')+'/AssemblyInfo.cs',networking/'AssemblyInfo.cs')
 (repo/'Probe.csproj').write_text('''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>'''+framework+'''</TargetFramework><LangVersion>7.3</LangVersion></PropertyGroup><ItemGroup><Compile Remove="Dependencies/**/*.cs;Networking/**/*.cs"/><ProjectReference Include="Networking/Networking.csproj"/><ProjectReference Include="Dependencies/Phinix.Common/Common/Authentication/Authentication.csproj"/><ProjectReference Include="Dependencies/Phinix.Common/Common/UserManagement/UserManagement.csproj"/></ItemGroup></Project>''')
 code='''using System;using System.Linq;using System.Reflection;using Google.Protobuf;using Phinix.Framework;using Utils.Framework;
internal static class Program {
static int assertions;static void Check(bool value,string reason){assertions++;if(!value)throw new Exception(reason);}
static int Main(){try{
Check(FrameworkProtocol.Version==2,"Protocol identity");
var packet=new FrameworkMessagePacket {Header=new FrameworkHeader {FrameworkVersion=2,TypeId="f5.probe",PacketId="fixed-id"},PayloadBytes=ByteString.CopyFromUtf8("roundtrip")};
var decoded=FrameworkMessagePacket.Parser.ParseFrom(packet.ToByteArray());Check(packet.Equals(decoded),"Protobuf roundtrip");
Check(typeof(NetMarker).Assembly==typeof(Program).Assembly,"Consumer identity");
var host=new ExtensionHostContext();host.AddService<IExtensionDiscoveryPolicy>(new Discovery());
var disabled=PhinixExtensionRegistry.DiscoverExtensions(host,new Disabled());Check(ProbeModule.Constructions==0&&disabled.Modules.Count==0,"Disabled no construction");
var found=PhinixExtensionRegistry.DiscoverExtensions(host);Check(found.Modules.Count==1&&ProbeModule.Constructions==1,"One discovery");
Check(found.ApiRegistry.ResolveAll<NetMarker>().Count==1,"Published API");PhinixExtensionRegistry.ActivateExtensions(found,host);Check(ProbeModule.Activations==1,"Activate once");
PhinixExtensionRegistry.ShutdownExtensions(found,host);PhinixExtensionRegistry.ShutdownExtensions(found,host);Check(ProbeModule.Stops==1,"Stop once");Check(found.ApiRegistry.ResolveAll<NetMarker>().Count==0,"API revoked");
ENDPOINT
foreach(var assembly in new[]{typeof(FrameworkProtocol).Assembly,typeof(Connections.NetCommon).Assembly,typeof(Authentication.ClientAuthenticatorPlaceholder).Assembly}){}
Console.WriteLine("PASS "+assertions+" assertions; "+typeof(FrameworkProtocol).Assembly.FullName);return 0;
}catch(Exception error){Console.Error.WriteLine(error);return 1;}}
sealed class Discovery:IExtensionDiscoveryPolicy{public bool ShouldScanAssembly(Assembly a){return a==typeof(Program).Assembly;}public bool ShouldDiscoverType(Type t){return t==typeof(ProbeModule);}}
sealed class Disabled:IExtensionActivationPolicy{public System.Collections.Generic.IReadOnlyCollection<string> DisabledExtensions{get{return new[]{"f5.probe"};}}public bool ShouldActivate(string id,out string reason){reason="disabled";return false;}}
}
public sealed class NetMarker{}
[PhinixExtension("f5.probe")]public sealed class ProbeModule:IPhinixExtensionModule,IActivatablePhinixExtensionModule{public static int Constructions,Activations,Stops;public ProbeModule(){Constructions++;}public string ExtensionId{get{return "f5.probe";}}public void Register(IExtensionBuilder b){b.RegisterApi<NetMarker>(new NetMarker());}public void Activate(ExtensionHostContext h){Activations++;}public void Shutdown(ExtensionHostContext h){Stops++;}}
'''
 code=code.replace('foreach(var assembly in new[]{typeof(FrameworkProtocol).Assembly,typeof(Connections.NetCommon).Assembly,typeof(Authentication.ClientAuthenticatorPlaceholder).Assembly}){}','')
 endpointcode='var endpoint=new Connections.NetClient();using(endpoint){Check(!endpoint.Connected,"Passive client endpoint");}' if side=='ClientProbe' else 'var endpoint=new Connections.NetServer(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback,0),2);using(endpoint){Check(!endpoint.Listening,"Passive server endpoint");}'
 endpointcode+='\nbool repeatedDisposeFailed=false;try{endpoint.Dispose();}catch(NullReferenceException){repeatedDisposeFailed=true;}Check(repeatedDisposeFailed,"Existing repeated endpoint Dispose limitation is recorded");'
 (repo/'Program.cs').write_text(code.replace('ENDPOINT',endpointcode))
 (repo/'README.md').write_text('# '+side+'\n\nLocal minimal consumer pinned to Shared '+manifest['sharedCommit']+'. This is not the full Phinix client/server.\n')
 manifest[side+'Commit']=commit(repo,'test(f5): consume pinned shared source with isolated '+framework+' endpoint probe')
 fresh=lab/'fresh'/side;fresh.parent.mkdir(exist_ok=True)
 run('git','-c','protocol.file.allow=always','clone','--recurse-submodules',str(repo),str(fresh))
 assert run('git','rev-parse','HEAD',cwd=fresh/'Dependencies/Phinix.Common')==manifest['sharedCommit']
 assert run('git','rev-parse','HEAD',cwd=fresh/'Dependencies/Phinix.Common/Dependencies/protobuf')==proto
manifest['sourceDigest']=hashlib.sha256(json.dumps(manifest['inputs'],sort_keys=True).encode()).hexdigest()
(lab/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2))
Path('/tmp/phinix-f5-active-lab.txt').write_text(str(lab))
(lab/'net10.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net10.0','framework':{'name':'Microsoft.NETCore.App','version':'10.0.0'}}},indent=2))
print(json.dumps({k:v for k,v in manifest.items() if k!='inputs'},indent=2))
