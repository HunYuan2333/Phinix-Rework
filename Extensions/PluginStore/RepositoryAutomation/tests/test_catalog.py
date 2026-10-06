import copy
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('catalog', ROOT / 'scripts/catalog.py')
catalog = importlib.util.module_from_spec(spec); spec.loader.exec_module(catalog)
EXAMPLE = json.loads((ROOT / 'tests/fixtures/managed-submission.json').read_text())
VALIDATOR = ROOT / 'Validator/bin/Release/net10.0/Validator.dll'
FIXTURES = ROOT / 'tests/fixtures/localization-display.json'


class CatalogTests(unittest.TestCase):
    def test_shared_language_fixtures_reach_the_trusted_validator(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / 'candidate.json'
            for fixture in json.loads(FIXTURES.read_text())['cases']:
                record = copy.deepcopy(EXAMPLE)
                record['package']['localization'] = fixture['localization']
                path.write_bytes(json.dumps(record, ensure_ascii=True).encode('utf-8'))
                if fixture['code']:
                    with self.assertRaises(ValueError):
                        catalog.build([path], 'test.source', 'a' * 40, VALIDATOR)
                else:
                    value = json.loads(catalog.build([path], 'test.source', 'a' * 40, VALIDATOR))
                    self.assertEqual(value['schemaVersion'], 3)

    def test_duplicate_versions_fail_before_an_output_is_written(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / 'candidate.json'; path.write_bytes(catalog.encode(EXAMPLE))
            with self.assertRaisesRegex(ValueError, 'TrustedValidationRejected'):
                catalog.build([path, path], 'test.source', 'a' * 40, VALIDATOR)

    def test_old_top_level_display_is_rejected(self):
        with tempfile.TemporaryDirectory() as temporary:
            record = copy.deepcopy(EXAMPLE); record['package']['name'] = 'Old display'
            path = Path(temporary) / 'candidate.json'; path.write_bytes(catalog.encode(record))
            with self.assertRaisesRegex(ValueError, 'TrustedValidationRejected'):
                catalog.build([path], 'test.source', 'a' * 40, VALIDATOR)

    def test_outputs_are_immutable_and_prior_pointer_is_untouched(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / 'catalog.json'; pointer = Path(temporary) / 'stable.json'
            pointer.write_bytes(b'previous pointer'); catalog.write_new(path, b'first catalog')
            with self.assertRaises(FileExistsError):
                catalog.write_new(path, b'changed catalog')
            self.assertEqual(path.read_bytes(), b'first catalog')
            self.assertEqual(pointer.read_bytes(), b'previous pointer')
            self.assertFalse(list(Path(temporary).glob('.catalog-*')))

    def test_duplicate_json_and_symlink_input_are_rejected(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / 'candidate.json'; path.write_bytes(b'{"a":1,"a":2}')
            with self.assertRaisesRegex(ValueError, 'DuplicateField'):
                catalog.read(path)
            link = Path(temporary) / 'link.json'; link.symlink_to(path)
            with self.assertRaisesRegex(ValueError, 'InputLimit'):
                catalog.read(link)

    def test_payload_hash_is_checked_before_zip_or_language_reads(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / 'payload.zip'; path.write_bytes(b'not a zip')
            record = copy.deepcopy(EXAMPLE); record['package']['artifact']['sizeBytes'] = path.stat().st_size
            with self.assertRaisesRegex(ValueError, 'PayloadDigestMismatch'):
                catalog.project(record, path, VALIDATOR)


if __name__ == '__main__':
    unittest.main()
