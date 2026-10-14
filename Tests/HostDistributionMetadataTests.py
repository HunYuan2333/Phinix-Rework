"""Package metadata/resources tests. Sentinel DLL bytes do not verify CLR behavior."""
import importlib.util
from pathlib import Path
import shutil
import tempfile
import unittest
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('host_package', ROOT / 'tools/compatibility/host_package.py')
module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)


class DistributionMetadataTests(unittest.TestCase):
    def fixture(self, root):
        layout = root / 'layout'; layout.mkdir()
        for folder in ['About', 'Defs', 'Languages', 'Textures', 'Themes']:
            shutil.copytree(ROOT / 'Client' / folder, layout / folder)
        about = ET.parse(layout / 'About/About.xml'); versions = about.getroot().find('supportedVersions'); versions.clear()
        for game in ['1.6', '1.7']:
            ET.SubElement(versions, 'li').text = game
            dll = layout / game / 'Assemblies/13-PhinixClient.dll'; dll.parent.mkdir(parents=True); dll.write_bytes(b'sentinel')
        about.write(layout / 'About/About.xml', encoding='utf-8')
        (layout / 'LoadFolders.xml').write_text('<loadFolders><v1.6><li>/</li><li>1.6</li></v1.6><v1.7><li>/</li><li>1.7</li></v1.7></loadFolders>')
        return layout

    def verify(self, root, layout):
        archive = root / 'package.zip'
        with zipfile.ZipFile(archive, 'w') as package:
            for p in layout.rglob('*'):
                if p.is_file(): package.write(p, 'phinix-rework/' + p.relative_to(layout).as_posix())
        return module.validate_host_package(archive, ROOT / 'Client', layout)

    def test_correct_identity_resources_and_loading_profiles(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary); result = self.verify(root, self.fixture(root))
            self.assertEqual(result['workshopId'], '3735269431')

    def test_missing_identity_preview_theme_or_runtime_rejected(self):
        for path in ['About/PublishedFileId.txt', 'About/Preview.png', 'Themes/default.xml', '1.7/Assemblies/13-PhinixClient.dll']:
            with self.subTest(path=path), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary); layout = self.fixture(root); (layout / path).unlink()
                with self.assertRaises(ValueError): self.verify(root, layout)

    def test_wrong_workshop_id_rejected(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary); layout = self.fixture(root)
            (layout / 'About/PublishedFileId.txt').write_text('1234567890')
            with self.assertRaisesRegex(ValueError, 'identity'): self.verify(root, layout)

    def test_mixed_runtime_loading_rejected(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary); layout = self.fixture(root)
            p = layout / 'LoadFolders.xml'; p.write_text(p.read_text().replace('<li>1.7</li>', '<li>1.6</li>'))
            with self.assertRaisesRegex(ValueError, 'Mixed runtime'): self.verify(root, layout)


if __name__ == '__main__': unittest.main()
