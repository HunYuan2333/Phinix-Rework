#!/usr/bin/env python3
"""Use the public Phinix packager; never publish or access credentials."""
import argparse
from pathlib import Path
import subprocess
import xml.etree.ElementTree as ET

p = argparse.ArgumentParser()
p.add_argument('--phinix-root', type=Path, required=True)
p.add_argument('--game-references', type=Path, required=True)
p.add_argument('--output', type=Path, required=True)
p.add_argument('--bundle-output', type=Path)
a = p.parse_args()
host = a.phinix_root.resolve(); game = a.game_references.resolve()
root = Path(__file__).resolve().parent
version = ET.parse(root/'Example.csproj').getroot().findtext('PropertyGroup/Version')
if not version:
    raise SystemExit('Example.csproj must declare Version.')
subprocess.run(['dotnet', 'build', str(root/'Example.csproj'), '-c', 'Release', '-m:1', '-p:BuildInParallel=false', '-p:PhinixRoot='+str(host), '-p:GameReferences='+str(game)], check=True)
tool = host/'Extensions/PluginStore/Tools/ManagedPackageTool'
subprocess.run(['dotnet', 'build', str(tool/'ManagedPackageTool.csproj'), '-c', 'Release', '-m:1', '-p:BuildInParallel=false'], check=True)
command = ['dotnet', str(tool/'bin/Release/net10.0/ManagedPackageTool.dll'), '--assembly', str(root/'bin/Release/net472/Phinix.Example.Basic.dll'), '--package-id', 'phinix.example.basic', '--name', 'Phinix Example Plugin', '--version', version, '--output', str(a.output.resolve())]
for locale in ['en-US', 'zh-CN']:
    command += ['--language-file', str(root/'Resources/Localization'/ (locale+'.json'))]
for name in ['mscorlib.dll', 'Assembly-CSharp.dll', 'UnityEngine.CoreModule.dll', 'UnityEngine.TextRenderingModule.dll', 'UnityEngine.IMGUIModule.dll']:
    command += ['--host-assembly', str(game/name)]
command += ['--host-assembly', str(host/'Common/Utils/bin/Release/net472/Utils.dll'), '--host-assembly', str(host/'Client/ClientExtensionAbstractions/bin/Release/ClientExtensionAbstractions.dll')]
if a.bundle_output:
    command += ['--bundle-output', str(a.bundle_output.resolve())]
subprocess.run(command, check=True)
