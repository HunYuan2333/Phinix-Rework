import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import xml.etree.ElementTree as ET

DIRECTORY = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('official_export', DIRECTORY / 'export.py')
exporter = importlib.util.module_from_spec(spec)
spec.loader.exec_module(exporter)


class ExportTests(unittest.TestCase):
    def test_redpacket_needs_explicit_legacy_parameter_exception(self):
        with tempfile.TemporaryDirectory() as folder:
            target = Path(folder) / 'source'
            with self.assertRaisesRegex(ValueError, 'Embedded credential blocks export: RedPacketRelay.cs'):
                exporter.export('redpacket', target)
            self.assertFalse(target.exists(), 'No partially exported source on a rejected credential.')

    def test_exception_does_not_allow_other_fields_or_other_owners(self):
        with tempfile.TemporaryDirectory() as folder:
            owner = Path(folder) / 'LegacyRedPacket'
            source = owner / 'Client/RedPacketRelay.cs'
            source.parent.mkdir(parents=True)
            source.write_text('private const string RelayApiKey = "test-placeholder";')
            exporter.checked_source(source, owner, True)
            source.write_text('private const string AccessToken = "must-not-be-exported";')
            with self.assertRaisesRegex(ValueError, '^Embedded credential blocks export: RedPacketRelay.cs$'):
                exporter.checked_source(source, owner, True)
            other = owner / 'Client/Other.cs'
            other.write_text('private const string RelayApiKey = "must-not-be-exported";')
            with self.assertRaises(ValueError):
                exporter.checked_source(other, owner, True)

    def test_both_snapshots_are_owned_and_portable(self):
        for kind in exporter.PACKAGES:
            with self.subTest(kind=kind), tempfile.TemporaryDirectory() as folder:
                target = Path(folder) / 'source'
                exporter.export(kind, target, preserve_legacy_client_key=kind == 'redpacket')
                checker_spec = importlib.util.spec_from_file_location('check_' + kind, target / 'check-source.py')
                checker = importlib.util.module_from_spec(checker_spec)
                checker_spec.loader.exec_module(checker)
                checker.check(target)
                for project in target.glob('*/*.csproj'):
                    tree = ET.parse(project).getroot()
                    for reference in tree.findall('./ItemGroup/ProjectReference'):
                        self.assertTrue((project.parent / reference.attrib['Include']).resolve().is_relative_to(target))
                    self.assertNotIn('SolutionDir', project.read_text())
                self.assertEqual(len(list(target.glob('Client/Resources/*/Localization/*.json'))), 2)
                self.assertFalse(list(target.rglob('*.dll')))
                with self.assertRaises(ValueError):
                    exporter.export(kind, target, True)
                manifest = json.loads((target / 'source-manifest.json').read_text())
                source_name = next(name for name in manifest['files'] if name.endswith('.cs'))
                with (target / source_name).open('a') as stream:
                    stream.write('\n// altered snapshot\n')
                with self.assertRaisesRegex(ValueError, 'Snapshot changed'):
                    checker.check(target)

    def test_owned_symlink_is_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            owner = Path(folder) / 'owner'
            owner.mkdir()
            outside = Path(folder) / 'outside.cs'
            outside.write_text('// outside')
            alias = owner / 'alias.cs'
            alias.symlink_to(outside)
            with self.assertRaises(ValueError):
                exporter.checked_source(alias, owner)


if __name__ == '__main__':
    unittest.main()
