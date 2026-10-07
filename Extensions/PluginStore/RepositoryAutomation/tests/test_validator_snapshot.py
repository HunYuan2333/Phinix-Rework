import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('validator_snapshot', ROOT / 'scripts/validator_snapshot.py')
snapshot = importlib.util.module_from_spec(spec); spec.loader.exec_module(snapshot)


class ValidatorSnapshotTests(unittest.TestCase):
    def fixture(self, temporary):
        root, source = Path(temporary) / 'index', Path(temporary) / 'main'
        target = root / 'Validator/Production/Test.cs'; target.parent.mkdir(parents=True)
        origin = source / 'Extensions/PluginStore/Client/Test.cs'; origin.parent.mkdir(parents=True)
        target.write_bytes(b'old'); origin.write_bytes(b'old')
        manifest = {'schemaVersion': 1, 'files': [{'source': origin.relative_to(source).as_posix(),
                    'snapshot': target.relative_to(root).as_posix(), 'sha256': snapshot.hashlib.sha256(b'old').hexdigest()}]}
        (root / 'Validator/production-provenance.json').write_text(json.dumps(manifest))
        return root, source, target, origin

    def test_checked_in_snapshot_integrity(self):
        self.assertGreater(snapshot.check(ROOT), 0)

    def test_main_source_consistency_when_source_checkout_is_available(self):
        source = next((parent for parent in ROOT.parents if (parent / 'Common/Utils/Utils.csproj').is_file()), None)
        if source is None:
            self.skipTest('Standalone index has no main source checkout; integrity is checked separately')
        self.assertGreater(snapshot.check(ROOT, source), 0)

    def test_source_drift_is_detected_and_explicit_refresh_is_repeatable(self):
        with tempfile.TemporaryDirectory() as temporary:
            root, source, target, origin = self.fixture(temporary)
            origin.write_bytes(b'new')
            with self.assertRaisesRegex(ValueError, 'SnapshotSourceChanged'):
                snapshot.check(root, source)
            self.assertEqual(snapshot.refresh(root, source), 1)
            self.assertEqual(target.read_bytes(), b'new')
            self.assertEqual(snapshot.refresh(root, source), 1)

    def test_modified_or_undeclared_snapshot_is_rejected(self):
        with tempfile.TemporaryDirectory() as temporary:
            root, source, target, origin = self.fixture(temporary)
            target.write_bytes(b'tampered')
            with self.assertRaisesRegex(ValueError, 'SnapshotDigestMismatch'):
                snapshot.refresh(root, source)
            target.write_bytes(b'old'); (target.parent / 'Unexpected.cs').write_bytes(b'new')
            with self.assertRaisesRegex(ValueError, 'SnapshotFileSetMismatch'):
                snapshot.check(root)

    def test_path_escape_and_source_link_fail_before_mutation(self):
        with tempfile.TemporaryDirectory() as temporary:
            root, source, target, origin = self.fixture(temporary)
            origin.unlink(); origin.symlink_to(target)
            with self.assertRaisesRegex(ValueError, 'SnapshotLinkRejected'):
                snapshot.refresh(root, source)
            self.assertEqual(target.read_bytes(), b'old')
            path = root / 'Validator/production-provenance.json'; manifest = json.loads(path.read_text())
            manifest['files'][0]['source'] = 'Extensions/PluginStore/Client/../../../secret.cs'
            path.write_text(json.dumps(manifest))
            with self.assertRaisesRegex(ValueError, 'SnapshotPathRejected'):
                snapshot.check(root)

    def test_missing_later_source_does_not_partially_refresh(self):
        with tempfile.TemporaryDirectory() as temporary:
            root, source, target, origin = self.fixture(temporary)
            extra = target.parent / 'Missing.cs'; extra.write_bytes(b'old')
            path = root / 'Validator/production-provenance.json'; manifest = json.loads(path.read_text())
            manifest['files'].append({'source': 'Extensions/PluginStore/Client/Missing.cs',
                                     'snapshot': extra.relative_to(root).as_posix(), 'sha256': snapshot.hashlib.sha256(b'old').hexdigest()})
            path.write_text(json.dumps(manifest)); origin.write_bytes(b'new')
            with self.assertRaises(FileNotFoundError):
                snapshot.refresh(root, source)
            self.assertEqual(target.read_bytes(), b'old')
