#!/usr/bin/env python3
"""Run production Inventory/Trade storage and operation code against real SQLite; no game mutation."""
import argparse,json,shutil,subprocess,xml.etree.ElementTree as E
from pathlib import Path
p=argparse.ArgumentParser(description=__doc__);p.add_argument('--graph',type=Path,required=True);p.add_argument('--game',type=Path,required=True);p.add_argument('--output',type=Path,required=True)
a=p.parse_args();r=Path(__file__).resolve().parents[2];out=a.output.resolve();graph=a.graph.resolve()
if out.exists() or out.is_relative_to(r.parent):p.error('Use a fresh isolated output.')
out.mkdir(parents=True)
project=E.Element('Project',Sdk='Microsoft.NET.Sdk');props=E.SubElement(project,'PropertyGroup')
for k,v in {'TargetFramework':'net10.0','OutputType':'Exe','EnableDefaultCompileItems':'false','NuGetAudit':'false','NoWarn':'CS0436'}.items():E.SubElement(props,k).text=v
items=E.SubElement(project,'ItemGroup')
files=[str(file.relative_to(r)) for file in sorted((r/'Tests/SqliteBusinessRuntimeTests').glob('*.cs'))]
files+=['Extensions/Trade/Client/TradeDiagnostics.cs','Extensions/Inventory/GameAdapter/InventoryDiagnostics.cs','Extensions/Inventory/Client/InventoryLedger.cs','Extensions/Inventory/Client/InventoryDatabaseStore.cs']
files+=['Extensions/Trade/Client/'+name+'.cs' for name in ['TradeSqliteRecords','TradeIntentJournal','TradeIntentSettlement','TradeOperationQuery','TradeCompletionInbox','TradeDeliveryReceipts','TradeOfferOperations','TradeIntentSettlementCoordinator','TradeRecoveryPump','TradeLifetimeCleanup']]
for file in files:E.SubElement(items,'Compile',Include=str(r/file))
references={f.stem:f for f in (graph/'infrastructure').glob('*.dll') if f.name!='TradeExtension.Client.dll'}
for name in ['Assembly-CSharp.dll','UnityStubs.dll','UnityEngine.dll','UnityEngine.CoreModule.dll','UnityEngine.IMGUIModule.dll','UnityEngine.TextRenderingModule.dll']:
 f=a.game.resolve()/name
 if f.is_file():references[f.stem]=f
for name,path in references.items():
 node=E.SubElement(items,'Reference',Include=name);E.SubElement(node,'HintPath').text=str(path)
E.ElementTree(project).write(out/'Probe.csproj',encoding='unicode');(out/'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
results=[]
for phase,command in [('build',['dotnet','build',str(out/'Probe.csproj'),'-c','Release','-m:1','-nr:false']),('test',['dotnet',str(out/'bin/Release/net10.0/Probe.dll')])]:
 if phase=='test':
  for relative,entry in json.loads((graph/'native-assets.json').read_text()).items():
   destination=out/'bin/Release/net10.0'/relative;destination.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(entry['source'],destination)

 with (out/(phase+'.log')).open('w') as log:result=subprocess.run(command,stdout=log,stderr=subprocess.STDOUT,timeout=180)
 results.append({'phase':phase,'exit':result.returncode});print(phase,result.returncode,flush=True)
 if result.returncode:print((out/(phase+'.log')).read_text()[-6000:]);break
(out/'results.json').write_text(json.dumps(results,indent=2)+'\n')
raise SystemExit(0 if len(results)==2 and all(result['exit']==0 for result in results) else 1)
