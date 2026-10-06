#!/usr/bin/env python3
"""Exercise actual MSBuild distribution cleanup against isolated stale outputs."""
from pathlib import Path
import subprocess
import tempfile
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
LAYOUT = ROOT / "Client/Packaging/ClientPackageLayout.targets"


class ClientPackageLayoutTests(unittest.TestCase):
    def build(self, folder):
        project = ET.Element("Project")
        ET.SubElement(project, "Target", Name="Build")
        ET.SubElement(project, "Import", Project=str(LAYOUT))
        path = folder / "layout.proj"
        ET.ElementTree(project).write(path, encoding="utf-8")
        subprocess.run(["dotnet", "msbuild", str(path), "-t:Build", "-nologo",
                        "-p:PhinixClientPackageRoot=" + str(folder / "output")],
                       check=True, capture_output=True, text=True)

    def test_stale_outputs_removed_without_touching_other_ownership(self):
        with tempfile.TemporaryDirectory(prefix="phinix-package-layout-") as temp:
            folder = Path(temp)
            output = folder / "output"
            extensions = output / "Common/Extensions"
            retired = [
                extensions / name
                for name in (
                    "13-LegacyRedPacketExtension.dll",
                    "14-LegacyRedPacketExtension.Client.dll",
                    "15-LegacyTalentTradeExtension.dll",
                    "16-LegacyTalentTradeExtension.Client.dll",
                    "14-LegacyRedPacketExtension.Client.dll.localization.json",
                    "16-LegacyTalentTradeExtension.Client.dll.localization.json",
                    "16-LegacyTalentTradeExtension.Client.pdb",
                )
            ]
            for owner in ("LegacyRedPacket", "LegacyTalentTrade"):
                retired.append(extensions / "Resources" / owner / "Localization/en-US.json")
                retired.append(output / "Languages/English/Keyed" / (owner + "Extension.xml"))
            kept = [extensions / "09-TradeExtension.dll",
                    extensions / "10-InventoryExtension.dll",
                    extensions / "17-PluginStore.Client.dll",
                    extensions / "OtherPlugin/16-LegacyTalentTradeExtension.Client.dll",
                    extensions / "Resources/OtherPlugin/Localization/en-US.json",
                    output / "Languages/English/Keyed/PluginStore.xml",
                    folder / "player/ManagedExtensions/installed.dll",
                    folder / "player/Saves/old.rws"]
            for path in retired + kept:
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(b"ownership sentinel")
            self.build(folder)
            self.build(folder)
            self.assertTrue(all(not path.exists() for path in retired))
            self.assertTrue(all(path.read_bytes() == b"ownership sentinel" for path in kept))

    def test_empty_output_cleanup_does_not_create_a_package(self):
        with tempfile.TemporaryDirectory(prefix="phinix-package-layout-") as temp:
            folder = Path(temp)
            self.build(folder)
            self.assertFalse((folder / "output").exists())


if __name__ == "__main__":
    unittest.main()
