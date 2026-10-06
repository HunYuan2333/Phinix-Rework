import base64
import copy
import os
from pathlib import Path
import tempfile
import sys
from types import SimpleNamespace
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'scripts'))

import bot
import admission
import label_admission as labels
import publisher
from test_admission_publication import bundle, FakeApi, CONTEXT, HEAD, BODY, EXAMPLE, STATIC, FINGERPRINT, VALIDATOR

DATE = '2026-10-05T14:00:00Z'


def webhook():
    return dict(action='labeled', label=dict(name=labels.LABEL), repository=dict(full_name=bot.INDEX,
        id=int(admission.REPOSITORY_ID), owner=dict(id=int(admission.OWNER_ID))),
        sender=dict(type='User', login='Owner', id=7),
        issue=dict(number=4, state='open', body=BODY, updated_at=DATE))


def label_bundle(reuse=False):
    candidate, review, scope = bundle()
    review['approval'].update(workflow=labels.WORKFLOW, method='issue-label', label=labels.LABEL,
        labelEventId='100', labelCreatedAt=DATE, reuseExisting=reuse)
    return candidate, review, scope


class LabelApi(FakeApi):
    def __init__(self):
        super().__init__()
        self.issues[0].update(number=4, labels=[dict(name=labels.LABEL)], updated_at=DATE)
        self.events = [dict(id=100, event='labeled', label=dict(name=labels.LABEL), created_at=DATE,
                            actor=dict(type='User', login='Owner', id=7))]
        self.compare = 'identical'; self.content = {}; self.changes = []; self.tree = []
        self.pull = dict(number=9, html_url='https://github.com/' + bot.INDEX + '/pull/9', merged=False,
            user=dict(type='Bot', login='github-actions[bot]', id=int(labels.BOT_ID)),
            merged_by=dict(type='Bot', login='github-actions[bot]', id=int(labels.BOT_ID)),
            head=dict(sha='b' * 40, repo=dict(id=int(admission.REPOSITORY_ID))),
            base=dict(ref='main', repo=dict(id=int(admission.REPOSITORY_ID))))

    def json(self, path, method='GET', data=None):
        if method != 'GET':
            self.writes.append((path, method, data))
            if path.endswith('/pulls'): return copy.deepcopy(self.pull)
            if path.endswith('/merge'): return dict(merged=True, sha='c' * 40)
            return dict(sha='b' * 40)
        if '/events?' in path: return self.events
        if '/compare/' in path: return dict(status=self.compare)
        if '/git/trees/' in path: return dict(truncated=False, tree=self.tree)
        if '/contents/' in path:
            name = path.split('/contents/', 1)[1].split('?', 1)[0]
            return dict(type='file', encoding='base64', content=base64.b64encode(self.content[name]).decode())
        if '/pulls?' in path: return [dict(number=9)]
        if '/pulls/9/files?' in path: return self.changes
        if path.endswith('/pulls/9'): return self.pull
        return super().json(path, method, data)

    def files(self, values):
        self.content = values
        self.changes = [dict(filename=name, status='added') for name in values]


class LabelAdmissionTests(unittest.TestCase):
    def context(self, api, data=None, env=None):
        with patch.dict(os.environ, dict(CONTEXT, GITHUB_EVENT_NAME='issues', **(env or {})), clear=True), \
             patch.object(labels, 'payload', return_value=data or webhook()):
            return labels.context(api)

    def test_event_identity_body_and_maintainer_are_bound(self):
        issue, approval = self.context(LabelApi())
        self.assertEqual(issue['body'], BODY)
        self.assertEqual(approval['labelEventId'], '100')
        self.assertEqual(approval['actorId'], '7')

    def test_non_maintainer_bot_unrelated_label_and_rerun_rejected(self):
        cases = []
        api = LabelApi(); api.role = 'write'; cases.append((api, webhook(), {}))
        data = webhook(); data['sender']['type'] = 'Bot'; cases.append((LabelApi(), data, {}))
        data = webhook(); data['label']['name'] = 'bug'; cases.append((LabelApi(), data, {}))
        cases.append((LabelApi(), webhook(), dict(GITHUB_RUN_ATTEMPT='2')))
        cases.append((LabelApi(), webhook(), dict(GITHUB_TRIGGERING_ACTOR='Other')))
        for api, data, env in cases:
            with self.assertRaises(bot.Rejected): self.context(api, data, env)
            self.assertEqual(api.writes, [])

    def test_old_label_event_or_other_actor_cannot_approve_current_body(self):
        for change in ('stale', 'actor'):
            api = LabelApi()
            if change == 'stale': api.events[0]['created_at'] = 'old'
            else: api.events[0]['actor']['id'] = 99
            with self.assertRaises(bot.Rejected): self.context(api)

    def test_removal_readdition_edit_and_issue_close_invalidate_pending_approval(self):
        _, review, _ = label_bundle()
        for change in ('removed', 'readded', 'edited', 'closed'):
            api = LabelApi()
            if change == 'removed': api.issues[0]['labels'] = []
            elif change == 'readded': api.events.append(dict(api.events[0], id=102))
            elif change == 'edited': api.issues[0]['body'] += '\nchanged'
            else: api.issues[0]['state'] = 'closed'
            with self.assertRaises(bot.Rejected): labels.current_approval(api, review)

    def test_history_is_bounded_and_historical_proof_does_not_use_current_label(self):
        api = LabelApi(); api.events.append(dict(api.events[0], id=101, event='unlabeled'))
        _, review, _ = label_bundle()
        labels.event_matches(labels.label_event(api, 4, '100'), review['approval'])
        api.events = [dict(id=i, event='commented') for i in range(100)]
        with self.assertRaisesRegex(bot.Rejected, 'LabelHistoryLimit'): labels.label_event(api, 4)

    def test_prepare_captures_webhook_candidate_then_rechecks_current_body(self):
        api = LabelApi()
        with tempfile.TemporaryDirectory() as temporary, patch.dict(os.environ, dict(CONTEXT, GITHUB_EVENT_NAME='issues'), clear=True), \
             patch.object(labels, 'payload', return_value=webhook()), patch.object(labels, 'inspect', return_value=STATIC):
            output = Path(temporary) / 'out'
            labels.prepare(SimpleNamespace(output=output, validator=VALIDATOR), api)
            candidate, review, scope = admission.read_bundle(output)
            self.assertEqual(candidate, EXAMPLE)
            self.assertEqual(review['candidateSha256'], FINGERPRINT)
            self.assertFalse(review['approval']['reuseExisting'])
            self.assertTrue(review['approval']['includeUpdatePolicy'])
            self.assertTrue(any(p.startswith('update-policies/') for p in labels.expected_files(candidate, review, scope)))
        api = LabelApi(); api.issues[0]['body'] += ' edited'
        with tempfile.TemporaryDirectory() as temporary, patch.dict(os.environ, dict(CONTEXT, GITHUB_EVENT_NAME='issues'), clear=True), \
             patch.object(labels, 'payload', return_value=webhook()), patch.object(labels, 'inspect') as inspect:
            with self.assertRaisesRegex(bot.Rejected, 'SubmissionChanged'):
                labels.prepare(SimpleNamespace(output=Path(temporary) / 'out', validator=VALIDATOR), api)
            inspect.assert_not_called()

    def test_published_candidate_is_receipt_only_and_never_rewrites_accepted_version(self):
        candidate, review, scope = label_bundle(True); api = LabelApi()
        names = admission.paths(candidate['package'], FINGERPRINT)
        lock, raw = next(iter(publisher.locks_for([(candidate, review, scope)]).items()))
        api.content = {names[0]: bot.encode(candidate), names[2]: bot.encode(scope), lock: raw}
        api.tree = [dict(path=name) for name in (*names, lock)]
        self.assertTrue(labels.reuse(api, HEAD, candidate['package'], FINGERPRINT, candidate, scope))
        self.assertEqual(set(labels.expected_files(candidate, review, scope)), {'label-approvals/123.json'})
        with self.assertRaisesRegex(bot.Rejected, 'AcceptedVersionChanged'):
            labels.reuse(api, HEAD, candidate['package'], '0' * 64, candidate, scope)

    def test_auto_merge_checks_exact_pr_head_scope_and_latest_approval(self):
        for failure in (None, 'workflow', 'head', 'edited'):
            candidate, review, scope = label_bundle(); api = LabelApi(); api.files(labels.expected_files(candidate, review, scope))
            if failure == 'workflow': api.changes.append(dict(filename='.github/workflows/evil.yml', status='added'))
            if failure == 'head': api.pull['head']['sha'] = 'd' * 40
            if failure == 'edited': api.issues[0]['body'] += ' changed'
            with tempfile.TemporaryDirectory() as temporary, patch.dict(os.environ, dict(CONTEXT, GITHUB_EVENT_NAME='issues',
                    GITHUB_STEP_SUMMARY=str(Path(temporary) / 'summary')), clear=True), \
                 patch.object(labels, 'payload', return_value=webhook()), patch.object(labels, 'commit', return_value='b' * 40):
                root = Path(temporary)
                for name, value in zip(('candidate.json', 'review.json', 'policy.json'), (candidate, review, scope)):
                    (root / name).write_bytes(bot.encode(value))
                if failure:
                    with self.assertRaises(bot.Rejected): labels.admit(SimpleNamespace(input=root), api)
                else: labels.admit(SimpleNamespace(input=root), api)
            merges = [data for path, method, data in api.writes if path.endswith('/merge')]
            self.assertEqual(merges, [] if failure else [dict(sha='b' * 40, merge_method='squash')])

    def test_bot_merged_pr_is_only_valid_for_successful_label_workflow(self):
        candidate, review, scope = label_bundle(); api = LabelApi()
        api.run = dict(event='issues', path=labels.WORKFLOW, head_branch='main', status='completed', conclusion='success',
            run_attempt=1, head_sha=HEAD, display_title='Label admission #4', actor=dict(type='User', login='Owner', id=7))
        api.pull['merged'] = True; api.files(labels.expected_files(candidate, review, scope))
        publisher.approval_proof(api, candidate['package'], review, scope, candidate, HEAD)
        for change in ('failed', 'rerun', 'actor', 'merger', 'content'):
            altered = copy.deepcopy(api)
            if change == 'failed': altered.run['conclusion'] = 'failure'
            elif change == 'rerun': altered.run['run_attempt'] = 2
            elif change == 'actor': altered.run['actor']['id'] = 99
            elif change == 'merger': altered.pull['merged_by']['id'] = 99
            else: altered.content['label-approvals/123.json'] += b' '
            with self.assertRaises(bot.Rejected): labels.proof(altered, candidate, review, scope, HEAD)

    def test_receipts_require_proof_lock_continuity_and_matching_trigger(self):
        records = [bundle()]; candidate, review, scope = label_bundle(True); api = LabelApi()
        with tempfile.TemporaryDirectory() as temporary, patch.object(labels, 'proof') as proof:
            root = Path(temporary); (root / 'label-approvals').mkdir()
            (root / 'label-approvals/123.json').write_bytes(bot.encode(review))
            args = SimpleNamespace(root=root)
            locks, old, pending = publisher.label_receipts(args, api, HEAD, records, publisher.locks_for(records), '123')
            self.assertEqual(pending, [review]); proof.assert_called_once()
            with self.assertRaisesRegex(bot.Rejected, 'PublicationTriggerRejected'):
                publisher.label_receipts(args, api, HEAD, records, publisher.locks_for(records), '999')
            (root / 'approval-locks').mkdir(); (root / 'approval-locks/123.json').write_bytes(locks['approval-locks/123.json'])
            api.issues[0]['labels'] = []
            self.assertEqual(publisher.label_receipts(args, api, HEAD, records, publisher.locks_for(records), '123')[2], [])
            (root / 'label-approvals/123.json').unlink()
            with self.assertRaisesRegex(bot.Rejected, 'AcceptedVersionChanged'):
                publisher.label_receipts(args, api, HEAD, records, publisher.locks_for(records), None)

    def test_workflow_run_must_be_verified_and_have_its_own_receipt(self):
        api = LabelApi()
        api.run = dict(id=123, event='issues', path=labels.WORKFLOW, head_branch='main', status='completed',
            conclusion='success', run_attempt=1, head_sha=HEAD, head_repository=dict(id=int(admission.REPOSITORY_ID)),
            actor=dict(type='User', login='Owner', id=7))
        data = dict(action='completed', repository=dict(id=int(admission.REPOSITORY_ID)), workflow_run=copy.deepcopy(api.run))
        with patch.object(labels, 'payload', return_value=data):
            self.assertEqual(labels.upstream(api), '123')
            api.run['event'] = 'pull_request'
            with self.assertRaisesRegex(bot.Rejected, 'PublicationTriggerRejected'): labels.upstream(api)

    def test_comment_failure_cannot_turn_successful_commit_into_failure(self):
        api = LabelApi()
        with patch.object(labels, 'payload', return_value=webhook()), patch.dict(os.environ, CONTEXT, clear=True), \
             patch.object(api, 'json', side_effect=bot.Rejected('Denied')):
            labels.feedback(api, 'admission', True)

    def test_failed_admission_or_publication_marks_error_and_keeps_issue_open(self):
        for stage in ('admission', 'publication'):
            api = LabelApi(); data = webhook() if stage == 'admission' else dict(webhook(),
                workflow_run=dict(display_title='Label admission #4', id=123))
            with patch.object(labels, 'payload', return_value=data), patch.dict(os.environ, CONTEXT, clear=True):
                labels.feedback(api, stage, False)
            self.assertEqual(api.writes[0][2], dict(labels=['plugin-error']))
            self.assertFalse(any(method == 'PATCH' for _, method, _ in api.writes))
            self.assertTrue(any(path.endswith('/comments') for path, _, _ in api.writes))

    def test_success_closes_only_after_verifying_published_receipt(self):
        api = LabelApi(); api.issues[0]['labels'].append(dict(name='plugin-error'))
        data = dict(webhook(), workflow_run=dict(display_title='Label admission #4', id=123))
        with patch.object(labels, 'payload', return_value=data), patch.dict(os.environ, CONTEXT, clear=True), \
             patch.object(labels, 'published_issue', return_value=api.issues[0]) as verified:
            labels.feedback(api, 'publication', True)
        verified.assert_called_once_with(api, 4)
        self.assertEqual(api.writes[-1], (admission.PREFIX + '/issues/4', 'PATCH', dict(state='closed', state_reason='completed')))
        self.assertTrue(any(method == 'DELETE' and path.endswith('/plugin-error') for path, method, _ in api.writes))
        api = LabelApi()
        with patch.object(labels, 'payload', return_value=data), patch.dict(os.environ, CONTEXT, clear=True), \
             patch.object(labels, 'published_issue', side_effect=bot.Rejected('PublicationReceiptNotLocked')):
            labels.feedback(api, 'publication', True)
        self.assertFalse(any(method == 'PATCH' for _, method, _ in api.writes))
        self.assertEqual(api.writes[0][2], dict(labels=['plugin-error']))

    def test_publication_closure_requires_lock_stable_snapshot_and_unchanged_body(self):
        candidate, review, scope = label_bundle(True); api = LabelApi(); raw = bot.encode(review)
        api.content = {'label-approvals/123.json': raw, 'approval-locks/123.json': bot.encode(dict(schemaVersion=1,
            runId='123', candidateSha256=FINGERPRINT, reviewSha256=bot.digest(raw))),
            'stable.json': bot.encode(dict(sourceId=bot.SOURCE, snapshotId=HEAD))}
        with patch.object(labels, 'upstream', return_value='123'):
            self.assertEqual(labels.published_issue(api, 4)['state'], 'open')
            api.issues[0]['body'] += ' edit'
            with self.assertRaisesRegex(bot.Rejected, 'SubmissionChanged'): labels.published_issue(api, 4)
            api.issues[0]['body'] = BODY
            api.content['approval-locks/123.json'] = bot.encode(dict(schemaVersion=1, runId='123',
                candidateSha256=FINGERPRINT, reviewSha256='0' * 64))
            with self.assertRaisesRegex(bot.Rejected, 'PublicationReceiptNotLocked'): labels.published_issue(api, 4)

    def test_label_publication_commits_receipt_lock_and_rechecks_after_upload(self):
        for revoked in (False, True):
            records = [label_bundle()]; candidate, review, scope = records[0]; api = LabelApi()
            with tempfile.TemporaryDirectory() as temporary, patch.dict(os.environ, CONTEXT, clear=True):
                root = Path(temporary); (root / 'source.json').write_bytes(bot.encode(dict(schemaVersion=1,
                    sourceId=bot.SOURCE, repository=bot.INDEX)))
                (root / 'label-approvals').mkdir(); (root / 'label-approvals/123.json').write_bytes(bot.encode(review))
                args = SimpleNamespace(root=root, validator=VALIDATOR, check_only=False)
                def release(*_):
                    if revoked: api.issues[0]['labels'] = []
                    return dict(repository=bot.INDEX, repositoryId=admission.REPOSITORY_ID,
                        ownerId=admission.OWNER_ID, releaseId='10', assetId='11', assetName='catalog.json')
                def create(api, parent, changes, message):
                    self.assertIn('approval-locks/123.json', changes)
                    self.assertIn('label-approvals/123.json', labels.expected_files(candidate, review, scope))
                    return 'b' * 40
                api.verify_origin = lambda _: None
                with patch.object(publisher, 'collect', return_value=(records, publisher.locks_for(records), {})), \
                     patch.object(publisher, 'validator'), patch.object(publisher, 'release_asset', side_effect=release), \
                     patch.object(publisher, 'commit', side_effect=create):
                    if revoked:
                        with self.assertRaisesRegex(bot.Rejected, 'ApprovalLabelMissing'): publisher.publish(args, api)
                    else: publisher.publish(args, api)
                self.assertEqual(len(api.writes), 0 if revoked else 1)


if __name__ == '__main__': unittest.main()
