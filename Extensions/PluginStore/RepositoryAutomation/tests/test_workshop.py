import copy
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'scripts'))
import bot
import catalog
import admission
import label_admission as labels
import publisher
import source_updates
from test_admission_publication import bundle, FakeApi, CONTEXT, HEAD, VALIDATOR, EXAMPLE
from test_label_admission import LabelApi, webhook, DATE

ROOT = Path(__file__).resolve().parents[1]
WORKSHOP = json.loads((ROOT / 'examples/workshop-submission.json').read_text())
BODY = '### Candidate JSON\n```json\n' + json.dumps(WORKSHOP, ensure_ascii=False) + '\n```\n\n### Notes\nListing test only.'


def workshop_bundle(candidate=None):
    candidate = copy.deepcopy(candidate or WORKSHOP)
    _, review, _ = bundle()
    scope = admission.policy(candidate['package'])
    static = bot.workshop_static(candidate['package'])
    review.update(candidateSha256=bot.digest(bot.encode(candidate)), policySha256=bot.digest(bot.encode(scope)),
                  static=static, staticSha256=bot.digest(bot.encode(static)), issueBodySha256=bot.digest(BODY.encode()),
                  approvedAt='2026-10-07T06:00:00+00:00')
    return candidate, review, scope


def write_records(root, values):
    candidate, review, scope = values
    for name, value in zip(admission.paths(candidate['package'], review['candidateSha256']), values):
        path = root / name; path.parent.mkdir(parents=True, exist_ok=True); path.write_bytes(bot.encode(value))


class WorkshopTests(unittest.TestCase):
    def test_form_accepts_metadata_without_dll_release(self):
        self.assertEqual(bot.candidate_body(BODY), WORKSHOP['package'])
        self.assertNotIn('artifact', WORKSHOP['package'])
        self.assertNotIn('manifest', WORKSHOP['package'])
        self.assertIn('仅供测试', WORKSHOP['package']['summary'])

    def test_workshop_cannot_smuggle_payload_or_unknown_fields(self):
        for field in ('manifest', 'artifact', 'localization', 'version', 'downloadUrl', 'script'):
            value = copy.deepcopy(WORKSHOP); value['package'][field] = {}
            with self.assertRaisesRegex(bot.Rejected, 'WorkshopFieldsRejected'):
                bot.submission(value)

    def test_invalid_ids_compatibility_and_text_rejected(self):
        cases = [('workshopId', value) for value in ('0', '01', '-1', '1?x', '18446744073709551616', 123)]
        cases += [('rimWorldVersions', value) for value in ([], ['01.6'], ['1.6', '1.6'], ['1.6.0'], '1.6')]
        cases += [('tags', ['test', 'test']), ('tags', ['Bad Tag']), ('name', ''), ('summary', 'x' * 1025),
                  ('rimWorldPackageId', '../mod'), ('management', 'phinix-dll'), ('state', 'withdrawn')]
        for key, value in cases:
            candidate = copy.deepcopy(WORKSHOP); candidate['package'][key] = value
            with self.subTest(key=key, value=value), self.assertRaises(bot.Rejected): bot.submission(candidate)

    def test_actual_validator_checks_listing_without_origin_requests(self):
        api = Mock()
        with tempfile.TemporaryDirectory() as folder:
            report = bot.inspect(SimpleNamespace(validator=VALIDATOR), WORKSHOP['package'], Path(folder), api)
        self.assertEqual(report, bot.workshop_static(WORKSHOP['package']))
        self.assertEqual(report['scope'], 'listing-metadata-only')
        self.assertNotIn('sha256', report); self.assertNotIn('assemblies', report)
        api.verify_origin.assert_not_called(); api.download.assert_not_called(); api.json.assert_not_called()

    def test_catalog_projection_needs_no_zip_and_uses_actual_validator(self):
        self.assertEqual(catalog.project(WORKSHOP, None, VALIDATOR), WORKSHOP)
        with self.assertRaisesRegex(ValueError, 'UnexpectedPayload'):
            catalog.project(WORKSHOP, Path('unused.zip'), VALIDATOR)
        with tempfile.TemporaryDirectory() as folder:
            record = Path(folder) / 'candidate.json'; record.write_bytes(bot.encode(WORKSHOP))
            raw = catalog.build([record], bot.SOURCE, HEAD, VALIDATOR)
            self.assertEqual(json.loads(raw)['packages'], [WORKSHOP['package']])

    def test_intake_report_is_listing_only(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'candidate.json'; path.write_bytes(bot.encode(WORKSHOP))
            args = SimpleNamespace(input=path, output=Path(folder) / 'out', validator=VALIDATOR)
            with patch.object(bot, 'GitHub') as origin:
                self.assertEqual(bot.check(args), 0)
            report = json.loads((args.output / 'report.json').read_bytes())
            self.assertEqual(report['code'], 'WorkshopListingVerified')
            self.assertEqual(report['scope'], 'listing-metadata-only')
            self.assertNotIn('artifactSha256', report); self.assertNotIn('version', report)
            origin.return_value.download.assert_not_called()

    def test_report_comment_never_claims_code_review_or_source_monitoring(self):
        report = dict(schemaVersion=1, status='passed', code='WorkshopListingVerified', channel='steam-workshop',
                      issueNumber=4, issueUpdatedAt='date', issueBodySha256=bot.digest(BODY.encode()),
                      candidateSha256=bot.digest(bot.encode(WORKSHOP)))
        api = FakeApi(); api.issues[0]['body'] = BODY
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'report.json'; path.write_bytes(bot.encode(report))
            with patch.object(bot, 'GitHub', return_value=api): bot.post(SimpleNamespace(report=path))
        comment = api.writes[0][2]['body']
        self.assertIn('不代表 Mod 代码审核', comment)
        self.assertNotIn('自动版本规则', comment)

    def test_manual_evidence_binds_workshop_identity_and_metadata(self):
        candidate, review, scope = workshop_bundle()
        self.assertEqual(scope['mode'], 'manual-only')
        self.assertEqual(scope['origin']['workshopId'], candidate['package']['workshopId'])
        self.assertIsNone(source_updates.default_policy(candidate, review))
        for change in (None, 'workshopId', 'static', 'scope'):
            values = copy.deepcopy([candidate, review, scope])
            if change == 'workshopId': values[0]['package']['workshopId'] = '123'
            if change == 'static':
                values[1]['static']['scope'] = 'dll-verified'
                values[1]['staticSha256'] = bot.digest(bot.encode(values[1]['static']))
            if change == 'scope': values[2]['mode'] = 'automatic'
            with tempfile.TemporaryDirectory() as folder:
                root = Path(folder)
                for name, value in zip(('candidate.json', 'review.json', 'policy.json'), values): (root / name).write_bytes(bot.encode(value))
                if change is None: self.assertEqual(admission.read_bundle(root), values)
                else:
                    with self.assertRaises(bot.Rejected): admission.read_bundle(root)

    def test_workshop_cannot_claim_automatic_dll_source_approval(self):
        candidate, review, scope = workshop_bundle()
        review['approval'].update(workflow=source_updates.WORKFLOW, method='approved-source',
                                 baseCandidateSha256='a'*64, updatePolicySha256='b'*64)
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder)
            for name,value in zip(('candidate.json','review.json','policy.json'),(candidate,review,scope)):
                (root/name).write_bytes(bot.encode(value))
            with self.assertRaisesRegex(bot.Rejected,'WorkshopPolicyRejected'): admission.read_bundle(root)

    def test_label_prepare_retains_human_approval_without_binary_update_policy(self):
        api = LabelApi(); api.issues[0]['body'] = BODY
        event = webhook(); event['issue']['body'] = BODY
        with tempfile.TemporaryDirectory() as folder, patch.dict(os.environ, dict(CONTEXT, GITHUB_EVENT_NAME='issues'), clear=True), patch.object(labels, 'payload', return_value=event):
            out = Path(folder) / 'out'; labels.prepare(SimpleNamespace(output=out, validator=VALIDATOR), api)
            candidate, review, scope = admission.read_bundle(out)
            self.assertEqual(candidate, WORKSHOP)
            self.assertEqual(review['approval']['actor'], 'Owner')
            self.assertNotIn('includeUpdatePolicy', review['approval'])
            self.assertEqual(len(labels.expected_files(candidate, review, scope)), 4)
            self.assertEqual(api.writes, [])

    def test_workshop_locks_are_content_bound_without_fake_asset_versions(self):
        candidate, review, scope = workshop_bundle()
        lock, raw = next(iter(publisher.locks_for([(candidate, review, scope)]).items()))
        value = json.loads(raw)
        self.assertIn('workshop-', lock)
        self.assertNotIn('artifactSha256', value); self.assertNotIn('version', value)
        changed = copy.deepcopy(candidate); changed['package']['summary'] += ' Updated listing.'
        c, r, p = workshop_bundle(changed)
        fresh = publisher.locks_for([(candidate, review, scope), (c, r, p)])
        self.assertEqual(len(fresh), 2); publisher.continuity({lock: raw}, fresh)

    def test_reusing_exact_listing_and_new_metadata_keeps_origin_fixed(self):
        candidate, review, scope = workshop_bundle(); fingerprint = review['candidateSha256']
        names = admission.paths(candidate['package'], fingerprint)
        lock, raw = next(iter(publisher.locks_for([(candidate, review, scope)]).items()))
        api = LabelApi(); api.content = {names[0]: bot.encode(candidate), names[2]: bot.encode(scope), lock: raw}
        api.tree = [dict(path=name) for name in (*names, lock)]
        self.assertTrue(labels.reuse(api, HEAD, candidate['package'], fingerprint, candidate, scope))
        changed = copy.deepcopy(candidate); changed['package']['summary'] += ' Updated listing.'
        fingerprint = bot.digest(bot.encode(changed))
        self.assertFalse(labels.reuse(api, HEAD, changed['package'], fingerprint, changed, scope))
        changed['package']['workshopId'] = '123'; fingerprint = bot.digest(bot.encode(changed))
        with self.assertRaisesRegex(bot.Rejected, 'WorkshopIdentityChanged'):
            labels.reuse(api, HEAD, changed['package'], fingerprint, changed, scope)

    def test_only_latest_manually_approved_listing_is_visible_history_retained(self):
        old = workshop_bundle(); changed = copy.deepcopy(WORKSHOP); changed['package']['summary'] += ' New metadata.'
        new = workshop_bundle(changed); new[1]['approvedAt'] = '2026-10-07T07:00:00+00:00'
        dll = bundle()
        self.assertEqual(publisher.workshop_revisions([new, dll, old]), [dll, new])
        self.assertEqual(publisher.workshop_revisions([old, new]), [new])
        self.assertEqual(len(publisher.locks_for([old, new])), 2)
        changed['package']['workshopId'] = '123'
        with self.assertRaisesRegex(bot.Rejected, 'WorkshopIdentityChanged'):
            publisher.workshop_revisions([old, workshop_bundle(changed)])

    def test_publication_mixed_routes_actual_validator(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'catalog.json'
            path.write_bytes(bot.encode(dict(schemaVersion=3, sourceId=bot.SOURCE, snapshotId=HEAD,
                                            packages=[WORKSHOP['package'], EXAMPLE['package']])))
            result = subprocess.run(['dotnet', str(VALIDATOR), 'publication', bot.SOURCE, str(path)], capture_output=True, timeout=30)
            self.assertEqual(result.returncode, 0, result.stderr)

    def test_actual_collect_and_check_only_publication_never_download_workshop(self):
        values = workshop_bundle(); api = FakeApi(); api.issues[0]['body'] = BODY
        with tempfile.TemporaryDirectory() as folder, patch.dict(os.environ, CONTEXT, clear=True), patch.object(publisher, 'approval_proof', return_value=False):
            root = Path(folder); write_records(root, values)
            (root / 'source.json').write_bytes(bot.encode(dict(schemaVersion=1, sourceId=bot.SOURCE, repository=bot.INDEX)))
            args = SimpleNamespace(root=root, validator=VALIDATOR, check_only=True)
            records, locks, old = publisher.collect(args, api, HEAD)
            self.assertEqual(records, [values]); self.assertEqual(len(locks), 1)
            with patch.object(publisher, 'release_asset') as release:
                publisher.publish(args, api)
            release.assert_not_called(); self.assertEqual(api.writes, [])

    def test_workshop_publication_orders_release_before_atomic_pointer(self):
        values = workshop_bundle(); api = FakeApi(); api.issues[0]['body'] = BODY; order=[]
        artifact = dict(repository=bot.INDEX, repositoryId=admission.REPOSITORY_ID, ownerId=admission.OWNER_ID,
                        releaseId='10', assetId='11', assetName='catalog.json')
        with tempfile.TemporaryDirectory() as folder, patch.dict(os.environ, CONTEXT, clear=True), patch.object(publisher, 'approval_proof', return_value=False):
            root = Path(folder); write_records(root, values)
            (root / 'source.json').write_bytes(bot.encode(dict(schemaVersion=1, sourceId=bot.SOURCE, repository=bot.INDEX)))
            def release(api, snapshot, raw):
                order.append('release'); self.assertEqual(json.loads(raw)['packages'], [WORKSHOP['package']]); return artifact
            def commit(api, parent, changes, message):
                order.append('commit'); self.assertIn('stable.json', changes)
                self.assertTrue(any(p.startswith('publication-locks/') for p in changes)); return 'b'*40
            with patch.object(publisher, 'release_asset', side_effect=release), patch.object(publisher, 'commit', side_effect=commit):
                publisher.publish(SimpleNamespace(root=root, validator=VALIDATOR, check_only=False), api)
        self.assertEqual(order, ['release','commit'])
        self.assertEqual(api.writes[-1][2], dict(sha='b'*40, force=False))


if __name__ == '__main__': unittest.main()
