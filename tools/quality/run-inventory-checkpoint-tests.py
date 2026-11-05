#!/usr/bin/env python3
"""Isolated production-source inventory checkpoint regressions; synthetic files only."""
import argparse, json, subprocess, hashlib, xml.etree.ElementTree as E
from pathlib import Path
p=argparse.ArgumentParser(description=__doc__)
p.add_argument('--references',type=Path,required=True)
p.add_argument('--output',type=Path,required=True)
p.add_argument('--legacy-capacity',action='store_true')
p.add_argument('--legacy-read',type=Path)
a=p.parse_args();r=Path(__file__).resolve().parents[2];out=a.output.resolve()
if out.is_relative_to(r.parent) or out.exists():p.error('Use a fresh output outside the workspace.')
out.mkdir(parents=True)
sources=[r/'Extensions/Inventory/Client/InventoryLedger.cs',r/'Tests/InventoryCheckpointTests/CapacityRegression.cs']
if a.legacy_capacity or a.legacy_read:
 old=out/'LegacyInventoryJournal.cs';old.write_bytes(subprocess.check_output(['git','-C',str(r),'show','6fe404e:Extensions/Inventory/Client/InventoryJournal.cs']));sources.append(old)
 entry=out/'Main.cs'
 if a.legacy_read:
  entry.write_text('using System;using System.IO;class MainEntry{static void Main(string[] args){string p=Path.Combine(Path.GetTempPath(),"phinix-legacy-reader-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(p);try{string file=Path.Combine(p,"compacted.journal");File.Copy(args[0],file);using(var j=new Phinix.InventoryExtension.Client.InventoryJournal(file,CapacityRegression.State(8))){if(j.Faulted||!j.HasPendingRecovery||j.PendingState.Sequence!=9||j.ApproveRecovery().Sequence!=9||!j.TryAppend(CapacityRegression.State(10)))throw new Exception("Old reader cannot safely consume compacted format");}Console.WriteLine("LEGACY READER RESULT passed; original published journal reader, new compacted anchor and future record, explicit approval preserved");}finally{Directory.Delete(p,true);}}}')
 else:
  entry.write_text('using System;using System.IO;class MainEntry{static void Main(){string p=Path.Combine(Path.GetTempPath(),"phinix-legacy-capacity-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(p);try{CapacityRegression.Run(p);}finally{Directory.Delete(p,true);}}}')
 sources.append(entry)
else:sources += [r/'Extensions/Inventory/Client/InventoryJournal.cs',r/'Extensions/Inventory/Client/InventorySaveCheckpoint.cs',r/'Extensions/Inventory/Client/InventoryJournalCompatibility.cs',r/'Tests/InventoryCheckpointTests/Program.cs',r/'Tests/InventoryRuntimeTests/InventoryJournalScenarios.cs']
refs=[a.references.resolve()/n for n in ['Utils.dll','Google.Protobuf.dll','InventoryExtension.dll']]
project=E.Element('Project',Sdk='Microsoft.NET.Sdk');props=E.SubElement(project,'PropertyGroup')
for k,v in {'TargetFramework':'net10.0','OutputType':'Exe','EnableDefaultCompileItems':'false','NuGetAudit':'false','AssemblyName':'InventoryCheckpointTests'}.items():E.SubElement(props,k).text=v
items=E.SubElement(project,'ItemGroup')
for f in sources:E.SubElement(items,'Compile',Include=str(f))
for f in refs:
 node=E.SubElement(items,'Reference',Include=f.stem);E.SubElement(node,'HintPath').text=str(f)
E.ElementTree(project).write(out/'Probe.csproj',encoding='unicode')
(out/'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
commands=[['dotnet','build',str(out/'Probe.csproj'),'-c','Release','-m:1','-nr:false'],['dotnet',str(out/'bin/Release/net10.0/InventoryCheckpointTests.dll')]]
if a.legacy_read: commands[1].append(str(a.legacy_read.resolve()))
elif not a.legacy_capacity: commands[1].extend([str(r/'Tests/InventoryCheckpointTests/Fixtures'),str(out/'artifacts')])
results=[]
for phase,cmd in zip(['build','test'],commands):
 with (out/(phase+'.log')).open('w') as f:run=subprocess.run(cmd,stdout=f,stderr=subprocess.STDOUT,timeout=240)
 results.append({'phase':phase,'command':cmd,'exit':run.returncode})
 if run.returncode:break
m={'legacyNegativeControl':a.legacy_capacity,'results':results,'passed':len(results)==2 and all(x['exit']==0 for x in results),'inputs':[{'path':str(f),'sha256':hashlib.sha256(f.read_bytes()).hexdigest()} for f in sources+refs+sorted((r/'Tests/InventoryCheckpointTests/Fixtures').glob('*'))],'seed':'fixed schedule; no random asset state','realGameValidated':False}
(out/'manifest.json').write_text(json.dumps(m,indent=2)+'\n');print(json.dumps({'passed':m['passed'],'output':str(out)}));raise SystemExit(0 if m['passed'] else 1)
