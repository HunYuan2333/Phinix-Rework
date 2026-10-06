import base64
import copy
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'scripts'))
import bot
import admission
import publisher

EXAMPLE = json.loads((ROOT / 'tests/fixtures/managed-submission.json').read_text())
VALIDATOR = ROOT / 'Validator/bin/Release/net10.0/Validator.dll'
HEAD = 'a' * 40
FINGERPRINT = bot.digest(bot.encode(EXAMPLE))
BODY = '### Candidate JSON\n' + json.dumps(EXAMPLE)
STATIC = dict(schemaVersion=1, packageId=EXAMPLE['package']['id'], version='1.3.0',
              sha256=EXAMPLE['package']['artifact']['sha256'], files=4, assemblies=[])
CONTEXT = dict(GITHUB_REPOSITORY=bot.INDEX, GITHUB_REF='refs/heads/main', GITHUB_EVENT_NAME='workflow_dispatch',
               GITHUB_RUN_ATTEMPT='1', GITHUB_RUN_ID='123', GITHUB_SHA=HEAD,
               GITHUB_ACTOR='Owner', GITHUB_ACTOR_ID='7', GITHUB_TRIGGERING_ACTOR='Owner')


def bundle():
    scope = admission.policy(EXAMPLE['package'])
    review = dict(schemaVersion=1, sourceId=bot.SOURCE, candidateSha256=FINGERPRINT,
        policySha256=bot.digest(bot.encode(scope)), static=copy.deepcopy(STATIC), staticSha256=bot.digest(bot.encode(STATIC)),
        issueNumber=4, issueBodySha256=bot.digest(BODY.encode()), issueUpdatedAt='date', approvedAt='date',
        approval=dict(actor='Owner', actorId='7', runId='123', trustedCommit=HEAD, workflow=admission.WORKFLOW, attempt=1))
    return copy.deepcopy(EXAMPLE), review, scope


class FakeApi:
    def __init__(self):
        self.issues = [dict(state='open', body=BODY, updated_at='date')]
        self.role = 'admin'; self.head = HEAD; self.writes = []; self.run = None

    def json(self, path, method='GET', data=None):
        if method != 'GET':
            self.writes.append((path, method, data))
            return dict(sha='b' * 40)
        if path == admission.PREFIX:
            return dict(full_name=bot.INDEX, id=int(admission.REPOSITORY_ID), owner=dict(id=int(admission.OWNER_ID)), default_branch='main', private=False)
        if '/collaborators/' in path:
            return dict(role_name=self.role, user=dict(type='User', login='Owner', id=7))
        if path.endswith('/git/ref/heads/main'):
            return dict(object=dict(sha=self.head))
        if '/issues/' in path:
            value = self.issues[0]
            if len(self.issues) > 1: self.issues.pop(0)
            return value
        if '/actions/runs/' in path:
            return self.run
        raise AssertionError(path)


class AdmissionTests(unittest.TestCase):
    def prepare(self, api, context=None, fingerprint=FINGERPRINT):
        with tempfile.TemporaryDirectory() as temporary, patch.dict(os.environ, context or CONTEXT, clear=True), \
                patch.object(admission, 'inspect', return_value=STATIC):
            root = Path(temporary) / 'out'
            args = SimpleNamespace(issue_number=4, candidate_sha256=fingerprint, output=root, validator=VALIDATOR)
            admission.prepare(args, api)
            return admission.read_bundle(root)

    def test_exact_version_evidence_and_manual_policy(self):
        candidate, review, scope = self.prepare(FakeApi())
        self.assertEqual(candidate, EXAMPLE)
        self.assertEqual(review['approval']['actorId'], '7')
        self.assertEqual(review['candidateSha256'], FINGERPRINT)
        self.assertEqual(scope['mode'], 'manual-only')
        self.assertEqual(scope['origin']['repositoryId'], '1403380030')

    def test_wrong_candidate_requires_a_new_approval(self):
        with self.assertRaisesRegex(bot.Rejected, 'CandidateFingerprintMismatch'):
            self.prepare(FakeApi(), fingerprint='0' * 64)

    def test_writer_or_bot_cannot_approve(self):
        api = FakeApi(); api.role = 'write'
        with self.assertRaisesRegex(bot.Rejected, 'ReviewerUnauthorized'): self.prepare(api)

    def test_approval_cannot_be_rerun_by_another_actor_or_on_a_branch(self):
        for changes in [dict(GITHUB_REF='refs/heads/evil'), dict(GITHUB_RUN_ATTEMPT='2'),
                        dict(GITHUB_TRIGGERING_ACTOR='Other'), dict(GITHUB_EVENT_NAME='issues')]:
            with self.assertRaisesRegex(bot.Rejected, 'ApprovalContextRejected'):
                self.prepare(FakeApi(), dict(CONTEXT, **changes))

    def test_issue_changed_while_validating_rejects(self):
        api = FakeApi(); api.issues.append(dict(state='open', body=BODY + ' edited', updated_at='new'))
        with self.assertRaisesRegex(bot.Rejected, 'SubmissionChanged'): self.prepare(api)

    def test_changed_main_rejects_before_inspection(self):
        api = FakeApi(); api.head = 'b' * 40
        with self.assertRaisesRegex(bot.Rejected, 'TrustedHeadChanged'): self.prepare(api)

    def test_tampered_policy_or_static_report_cannot_be_used(self):
        for position, key, value in [(2, 'mode', 'automatic'), (1, 'staticSha256', '0' * 64), (1, 'sourceId', 'evil')]:
            values = list(bundle()); values[position][key] = value
            with tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                for name, item in zip(('candidate.json', 'review.json', 'policy.json'), values):
                    (root / name).write_bytes(bot.encode(item))
                with self.assertRaises(bot.Rejected): admission.read_bundle(root)


class PublicationTests(unittest.TestCase):
    def test_success_response_lost_retry_is_read_only(self):
        raw = bot.encode(dict(schemaVersion=3, sourceId=bot.SOURCE, snapshotId=HEAD, packages=[EXAMPLE['package']]))
        artifact = dict(repository=bot.INDEX, repositoryId=admission.REPOSITORY_ID, ownerId=admission.OWNER_ID,
                        releaseId='10', assetId='11', assetName='catalog.json')
        published, stable = publisher.envelopes(HEAD, raw, artifact)
        api = FakeApi(); api.head = 'b' * 40; original = api.json
        def get(path, method='GET', data=None):
            if '/git/commits/' in path:
                return dict(parents=[dict(sha=HEAD)], message='Publish catalog v3 ' + HEAD)
            if '/contents/' in path:
                value = stable if '/stable.json?' in path else published
                return dict(type='file', encoding='base64', content=base64.b64encode(value).decode())
            return original(path, method, data)
        api.json = get; api.verify_origin = lambda _: None
        api.download = lambda artifact, path: path.write_bytes(raw)
        publisher.completed_retry(api, HEAD, api.head)
        self.assertEqual(api.writes, [])
        with self.assertRaisesRegex(bot.Rejected, 'TrustedHeadChanged'):
            publisher.completed_retry(api, 'c' * 40, api.head)

    def test_real_approval_chain_requires_exact_merged_metadata(self):
        candidate, review, scope = bundle(); package = candidate['package']
        names = admission.paths(package, FINGERPRINT)
        api = FakeApi()
        run = dict(event='workflow_dispatch', conclusion='success', status='completed', path=admission.WORKFLOW,
            head_branch='main', head_sha=HEAD, run_attempt=1, display_title='Admit #4 ' + FINGERPRINT,
            actor=dict(login='Owner', id=7, type='User'))
        pull = dict(number=9, merged=True, base=dict(ref='main', repo=dict(id=int(admission.REPOSITORY_ID))),
            head=dict(sha='b' * 40, repo=dict(id=int(admission.REPOSITORY_ID))), merged_by=dict(login='Owner', id=7))
        changes = [dict(filename=name, status='added') for name in names]
        content = dict(zip(names, map(bot.encode, (candidate, review, scope))))
        def get(path, *args):
            if '/actions/runs/' in path: return run
            if '/compare/' in path: return dict(status='ahead')
            if '/collaborators/' in path: return dict(role_name='admin', user=dict(type='User', login='Owner', id=7))
            if '/pulls?' in path: return [dict(number=9)]
            if path.endswith('/pulls/9'): return pull
            if '/pulls/9/files?' in path: return changes
            if '/contents/' in path:
                name = path.split('/contents/', 1)[1].split('?', 1)[0]
                return dict(type='file', encoding='base64', content=base64.b64encode(content[name]).decode())
            raise AssertionError(path)
        api.json = get
        publisher.approval_proof(api, package, review, scope, candidate, 'c' * 40)
        pull['merged'] = False
        with self.assertRaisesRegex(bot.Rejected, 'AdmissionPrNotMerged'):
            publisher.approval_proof(api, package, review, scope, candidate, 'c' * 40)
        pull['merged'] = True; changes.append(dict(filename='.github/workflows/evil.yml', status='added'))
        with self.assertRaisesRegex(bot.Rejected, 'AdmissionPrScopeRejected'):
            publisher.approval_proof(api, package, review, scope, candidate, 'c' * 40)
        changes.pop(); content[names[0]] += b' '
        with self.assertRaisesRegex(bot.Rejected, 'AdmissionPrContentChanged'):
            publisher.approval_proof(api, package, review, scope, candidate, 'c' * 40)

    def test_replaced_release_asset_is_never_clobbered(self):
        raw = b'catalog'; api = FakeApi()
        def get(path, *args):
            if '/releases?' in path:
                return [dict(id=1, tag_name='catalog-v3-' + HEAD, draft=False, prerelease=False,
                    assets=[dict(id=2, name='catalog.json', state='uploaded', size=len(raw), digest='sha256:' + '0' * 64)])]
            raise AssertionError(path)
        api.json = get
        with patch.object(publisher.subprocess, 'run') as upload:
            with self.assertRaisesRegex(bot.Rejected, 'PublicationAssetConflict'): publisher.release_asset(api, HEAD, raw)
            upload.assert_not_called()

    def test_partial_upload_retry_verifies_bytes_before_publishing_release(self):
        raw = b'catalog'; api = FakeApi(); order = []
        release = dict(id=1, tag_name='catalog-v3-' + HEAD, target_commitish=HEAD, draft=True, prerelease=False,
            assets=[dict(id=2, name='catalog.json', state='uploaded', size=len(raw), digest='sha256:' + bot.digest(raw))])
        def get(path, method='GET', data=None):
            if '/releases?' in path: return [release]
            if method == 'PATCH': order.append('release-visible'); return dict(release, draft=False)
            raise AssertionError(path)
        api.json = get
        def download(artifact, path): order.append('bytes'); path.write_bytes(raw)
        api.download = download; api.verify_origin = lambda _: order.append('origin')
        with patch.object(publisher.subprocess, 'run') as upload:
            result = publisher.release_asset(api, HEAD, raw); upload.assert_not_called()
        self.assertEqual(result['assetId'], '2')
        self.assertEqual(order, ['bytes', 'release-visible', 'origin'])

    def test_accepted_version_cannot_change_or_disappear(self):
        records = [bundle()]; locks = publisher.locks_for(records)
        publisher.continuity(locks, locks)
        with self.assertRaisesRegex(bot.Rejected, 'AcceptedVersionChanged'): publisher.continuity(locks, {})
        changed = {key: raw + b' ' for key, raw in locks.items()}
        with self.assertRaisesRegex(bot.Rejected, 'AcceptedVersionChanged'): publisher.continuity(locks, changed)
        with self.assertRaisesRegex(bot.Rejected, 'AcceptedVersionConflict'): publisher.locks_for(records + records)

    def test_no_labels_comments_or_forged_run_are_approval(self):
        api = FakeApi(); candidate, review, scope = bundle()
        api.run = dict(event='issues', conclusion='success', status='completed', path=admission.WORKFLOW,
            head_branch='main', head_sha=HEAD, run_attempt=1, display_title='arbitrary', actor=dict(login='Owner', id=7, type='User'))
        with self.assertRaisesRegex(bot.Rejected, 'ApprovalProofRejected'):
            publisher.approval_proof(api, candidate['package'], review, scope, candidate, HEAD)
        self.assertEqual(api.writes, [])

    def test_metadata_hashes_bind_the_actual_uploaded_catalog(self):
        raw = bot.encode(dict(schemaVersion=3, sourceId=bot.SOURCE, snapshotId=HEAD, packages=[EXAMPLE['package']]))
        published, stable = publisher.envelopes(HEAD, raw, dict(repository=bot.INDEX, repositoryId=admission.REPOSITORY_ID,
            ownerId=admission.OWNER_ID, releaseId='10', assetId='11', assetName='catalog.json'))
        entry = bot.strict_json(stable)
        self.assertEqual(entry['catalogSha256'], bot.digest(raw))
        self.assertEqual(entry['publishedSha256'], bot.digest(published))
        self.assertEqual(entry['publishedSizeBytes'], len(published))
        self.assertEqual(entry['catalogSchemaVersion'], 3)

    def test_publisher_failure_and_concurrent_main_leave_pointer_untouched(self):
        candidate, review, scope = bundle(); records = [(candidate, review, scope)]
        artifact = dict(repository=bot.INDEX, repositoryId=admission.REPOSITORY_ID, ownerId=admission.OWNER_ID,
                        releaseId='10', assetId='11', assetName='catalog.json')
        for failure in ('validation', 'upload', 'race'):
            api = FakeApi(); api.verify_origin = lambda _: None
            with tempfile.TemporaryDirectory() as temporary, patch.dict(os.environ, CONTEXT, clear=True):
                root = Path(temporary); (root / 'source.json').write_bytes(bot.encode(dict(schemaVersion=1, sourceId=bot.SOURCE, repository=bot.INDEX)))
                args = SimpleNamespace(root=root, validator=VALIDATOR, check_only=False)
                def validate(*_):
                    if failure == 'validation': raise bot.Rejected('ValidationFailed')
                def upload(*_):
                    if failure == 'upload': raise bot.Rejected('UploadFailed')
                    return artifact
                def write_commit(*_):
                    if failure == 'race': api.head = 'c' * 40
                    return 'b' * 40
                with patch.object(publisher, 'collect', return_value=(records, publisher.locks_for(records), {})), \
                     patch.object(publisher, 'validator', side_effect=validate), \
                     patch.object(publisher, 'release_asset', side_effect=upload), \
                     patch.object(publisher, 'commit', side_effect=write_commit):
                    with self.assertRaises(bot.Rejected): publisher.publish(args, api)
            self.assertFalse(any('/git/refs/heads/main' in path for path, _, _ in api.writes))

    def test_success_moves_stable_once_after_release_and_checks(self):
        records = [bundle()]; order = []
        api = FakeApi(); api.verify_origin = lambda _: order.append('origin')
        artifact = dict(repository=bot.INDEX, repositoryId=admission.REPOSITORY_ID, ownerId=admission.OWNER_ID,
                        releaseId='10', assetId='11', assetName='catalog.json')
        with tempfile.TemporaryDirectory() as temporary, patch.dict(os.environ, CONTEXT, clear=True):
            root = Path(temporary); (root / 'source.json').write_bytes(bot.encode(dict(schemaVersion=1, sourceId=bot.SOURCE, repository=bot.INDEX)))
            args = SimpleNamespace(root=root, validator=VALIDATOR, check_only=False)
            def release(*_): order.append('release'); return artifact
            def write_commit(api, parent, changes, message):
                order.append('commit'); self.assertIn('stable.json', changes)
                self.assertIn('published/' + HEAD + '.json', changes); self.assertEqual(parent, HEAD)
                return 'b' * 40
            with patch.object(publisher, 'collect', return_value=(records, publisher.locks_for(records), {})), \
                 patch.object(publisher, 'validator'), patch.object(publisher, 'release_asset', side_effect=release), \
                 patch.object(publisher, 'commit', side_effect=write_commit): publisher.publish(args, api)
        self.assertEqual(order, ['release', 'origin', 'commit'])
        self.assertEqual(len(api.writes), 1)
        self.assertEqual(api.writes[0][2], dict(sha='b' * 40, force=False))

    def test_input_symlink_rejected(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary); (root / 'packages').mkdir()
            (root / 'data.json').write_text('{}'); (root / 'packages/a.json').symlink_to(root / 'data.json')
            with self.assertRaisesRegex(bot.Rejected, 'InputPathRejected'): publisher.files(root, 'packages')

    def test_dependency_and_module_closure_with_actual_validator(self):
        self.assertTrue(VALIDATOR.exists(), 'Build the trusted validator before running tests')
        for kind in ('valid', 'missing', 'optional-missing', 'module-missing', 'cycle'):
            package = copy.deepcopy(EXAMPLE['package'])
            if kind in ('missing', 'optional-missing', 'cycle'):
                package['manifest']['dependencies'] = [dict(packageId=package['id'] if kind == 'cycle' else 'missing.pkg',
                    versionRange='>=1.0.0 <2.0.0', optional=kind == 'optional-missing')]
            if kind == 'module-missing': package['manifest']['modules'][0]['dependsOn'] = ['missing.module']
            with tempfile.TemporaryDirectory() as temporary:
                path = Path(temporary) / 'catalog.json'
                path.write_bytes(bot.encode(dict(schemaVersion=3, sourceId=bot.SOURCE, snapshotId=HEAD, packages=[package])))
                result = subprocess.run(['dotnet', str(VALIDATOR), 'publication', bot.SOURCE, str(path)], capture_output=True, timeout=30)
            self.assertEqual(result.returncode == 0, kind in ('valid', 'optional-missing'), (kind, result.stderr))


    def test_configured_host_module_profiles_with_actual_validator(self):
        self.assertTrue(VALIDATOR.exists())
        for kind in ('valid', 'absent', 'outside-range', 'partial-range', 'unknown-module', 'host-cycle', 'module-shadow', 'assembly-shadow', 'assembly-alias-shadow', 'ambiguous', 'unknown-field', 'duplicate-field'):
            package = copy.deepcopy(EXAMPLE['package'])
            package['manifest']['modules'][0]['dependsOn'] = ['sample.host.feature']
            profile = dict(schemaVersion=1, profiles=[dict(phinixRange='>=0.9.7 <1.0.0',
                assemblies=[dict(name='Sample.Host.Library', sha256='a' * 64)],
                modules=[dict(id='sample.host.feature', dependsOn=[])])])
            if kind == 'outside-range': profile['profiles'][0]['phinixRange'] = '>=1.0.0 <2.0.0'
            if kind == 'partial-range': profile['profiles'][0]['phinixRange'] = '>=0.9.7 <0.9.8'
            if kind == 'unknown-module': package['manifest']['modules'][0]['dependsOn'] = ['unknown.host.module']
            if kind == 'host-cycle':
                profile['profiles'][0]['modules'][0]['dependsOn'] = ['sample.host.other']
                profile['profiles'][0]['modules'].append(dict(id='sample.host.other', dependsOn=['sample.host.feature']))
            if kind == 'module-shadow': profile['profiles'][0]['modules'].append(dict(id=package['manifest']['modules'][0]['id'], dependsOn=[]))
            if kind == 'assembly-shadow': profile['profiles'][0]['assemblies'][0]['name'] = package['manifest']['assemblies'][0]['name']
            if kind == 'assembly-alias-shadow': package['manifest']['assemblies'][0]['path'] = 'Assemblies/Sample.Host.Library.dll'
            if kind == 'ambiguous': profile['profiles'].append(copy.deepcopy(profile['profiles'][0]))
            if kind == 'unknown-field': profile['extra'] = True
            with tempfile.TemporaryDirectory() as temporary:
                path = Path(temporary) / 'catalog.json'
                path.write_bytes(bot.encode(dict(schemaVersion=3, sourceId=bot.SOURCE, snapshotId=HEAD, packages=[package])))
                config = Path(temporary) / 'host-module-profiles.json'
                raw = bot.encode(profile)
                if kind == 'duplicate-field': raw = raw.replace(b'"schemaVersion":1', b'"schemaVersion":1,"schemaVersion":1')
                config.write_bytes(raw)
                args = ['dotnet', str(VALIDATOR), 'publication', bot.SOURCE, str(path)]
                if kind != 'absent': args.append(str(config))
                result = subprocess.run(args, capture_output=True, timeout=30)
            self.assertEqual(result.returncode == 0, kind == 'valid', (kind, result.stderr))


if __name__ == '__main__': unittest.main()
