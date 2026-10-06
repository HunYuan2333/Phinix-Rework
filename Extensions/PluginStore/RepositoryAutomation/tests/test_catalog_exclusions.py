import copy
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

from test_admission_publication import bundle
import bot
import publisher


class ExclusionTests(unittest.TestCase):
    def apply(self, policy, records=None, locked=True):
        records = records or [bundle()]
        locks = publisher.locks_for(records) if locked else {}
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            if policy is not None:
                (root / 'catalog-exclusions.json').write_bytes(bot.encode(policy))
            return publisher.player_records(root, records, locks), locks

    def policy(self):
        return dict(schemaVersion=1, packageIds=[bundle()[0]['package']['id']], reason='Developer fixture cleanup')

    def test_exclusion_removes_listing_but_keeps_locks_and_records(self):
        records = [bundle()]; original = copy.deepcopy(records)
        visible, locks = self.apply(self.policy(), records)
        self.assertEqual(visible, [])
        self.assertEqual(records, original)
        self.assertEqual(locks, publisher.locks_for(original))

    def test_no_policy_and_empty_policy_preserve_normal_packages(self):
        self.assertEqual(self.apply(None)[0], [bundle()])
        policy = self.policy(); policy['packageIds'] = []
        self.assertEqual(self.apply(policy)[0], [bundle()])

    def test_unknown_or_unpublished_exclusions_are_rejected(self):
        policy = self.policy(); policy['packageIds'] = ['unknown.package']
        with self.assertRaisesRegex(bot.Rejected, 'CatalogExclusionUnknownPackage'):
            self.apply(policy)
        with self.assertRaisesRegex(bot.Rejected, 'CatalogExclusionUnpublished'):
            self.apply(self.policy(), locked=False)

    def test_invalid_policy_is_rejected_before_publication(self):
        for key, value in [('schemaVersion', True), ('reason', ' '), ('packageIds', ['BAD']),
                           ('packageIds', [bundle()[0]['package']['id']] * 2), ('extra', True)]:
            policy = self.policy(); policy[key] = value
            with self.assertRaisesRegex(bot.Rejected, 'CatalogExclusionRejected'):
                self.apply(policy)

    def test_empty_visible_catalog_is_valid(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / 'catalog.json'
            path.write_bytes(bot.encode(dict(schemaVersion=3, sourceId=bot.SOURCE, snapshotId='a'*40, packages=[])))
            from test_admission_publication import VALIDATOR
            publisher.validator(type('Args', (), {'validator': VALIDATOR})(), ['publication', bot.SOURCE, str(path)])

    def test_exclusion_cannot_hide_broken_remaining_dependency_closure(self):
        from test_admission_publication import EXAMPLE, VALIDATOR
        record = copy.deepcopy(EXAMPLE['package'])
        record['manifest']['dependencies'] = [dict(packageId='missing.package', versionRange='>=1.0.0 <2.0.0', optional=False)]
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / 'catalog.json'
            path.write_bytes(bot.encode(dict(schemaVersion=3, sourceId=bot.SOURCE, snapshotId='a'*40, packages=[record])))
            with self.assertRaises(bot.Rejected):
                publisher.validator(type('Args', (), {'validator': VALIDATOR})(), ['publication', bot.SOURCE, str(path)])
