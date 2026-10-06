import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('bot', ROOT / 'scripts/bot.py')
bot = importlib.util.module_from_spec(spec); spec.loader.exec_module(bot)
EXAMPLE = json.loads((ROOT / 'tests/fixtures/managed-submission.json').read_text())
VALIDATOR = ROOT / 'Validator/bin/Release/net10.0/Validator.dll'


class IntakeTests(unittest.TestCase):
    def reject(self, code, call):
        with self.assertRaisesRegex(bot.Rejected, '^' + code + '$'):
            call()

    def test_issue_form_and_extra_notes(self):
        body = '### Candidate JSON\n\n```json\n' + json.dumps(EXAMPLE) + '\n```\n\n### Notes\nhello'
        self.assertEqual(bot.candidate_body(body), EXAMPLE['package'])

    def test_missing_or_ambiguous_section(self):
        for text in ['no json', '### Candidate JSON\n{}\n### Candidate JSON\n{}']:
            self.reject('MissingCandidateJson', lambda: bot.candidate_body(text))

    def test_duplicate_keys_and_nonfinite_numbers(self):
        self.reject('DuplicateField', lambda: bot.strict_json(b'{"a":1,"a":2}'))
        self.reject('InvalidJson', lambda: bot.strict_json(b'{"a":NaN}'))

    def test_candidate_limits(self):
        self.reject('IssueBodyLimit', lambda: bot.candidate_body('x' * 65537))
        self.reject('DocumentLimit', lambda: bot.strict_json(b'x' * (bot.MAX_JSON + 1)))

    def test_invalid_repository_ids_and_private_route(self):
        for value in ['../../local', 'https://evil.example/a', 'owner/repo.git', 'a/b?x=1']:
            data = copy.deepcopy(EXAMPLE); data['package']['artifact']['repository'] = value
            self.reject('InvalidRepository', lambda: bot.submission(data))
        for value in ['0', '01', '-1', '18446744073709551616', 1]:
            data = copy.deepcopy(EXAMPLE); data['package']['artifact']['assetId'] = value
            self.reject('InvalidOriginId', lambda: bot.submission(data))
        data = copy.deepcopy(EXAMPLE); data['package']['management'] = 'rimworld-mod'
        self.reject('UnsupportedRoute', lambda: bot.submission(data))

    def test_redirect_does_not_reach_untrusted_hosts(self):
        api = bot.GitHub()
        for url in ['http://api.github.com/a', 'https://evil.example/a', 'https://127.0.0.1/a',
                    'https://api.github.com@evil.example/a', 'https://release-assets.githubusercontent.com/other',
                    'https://api.github.com:444/a']:
            with self.assertRaises(bot.Rejected):
                api.request(url, binary=True)

    def test_authorization_is_not_sent_to_asset_cdn(self):
        seen = []
        class Opener:
            def open(self, request, timeout):
                seen.append(request); return object()
        api = bot.GitHub(); api.opener = Opener()
        with patch.dict('os.environ', {'GH_TOKEN': 'test-only-secret'}):
            api.request('https://api.github.com/repos/a/b')
            api.request('https://release-assets.githubusercontent.com/github-production-release-asset/file', binary=True)
        self.assertIn('Authorization', dict(seen[0].header_items()))
        self.assertNotIn('Authorization', dict(seen[1].header_items()))

    def test_prerelease_and_repository_transfer_rejected(self):
        a = EXAMPLE['package']['artifact']
        repo = {'private': False, 'visibility': 'public', 'full_name': a['repository'], 'id': int(a['repositoryId']), 'owner': {'id': int(a['ownerId'])}}
        api = bot.GitHub(); api.json = lambda _: dict(repo, id=999)
        self.reject('OriginRepositoryMismatch', lambda: api.verify_origin(a))
        responses = iter([repo, {'id': int(a['releaseId']), 'draft': False, 'prerelease': True, 'tag_name': a['tag']}])
        api.json = lambda _: next(responses)
        self.reject('OriginReleaseMismatch', lambda: api.verify_origin(a))

    def test_changed_issue_cannot_receive_stale_report(self):
        body = '### Candidate JSON\n' + json.dumps(EXAMPLE)
        report = {'schemaVersion': 1, 'issueNumber': 7, 'status': 'passed', 'code': 'StaticCandidateVerified',
                  'issueUpdatedAt': 'old', 'issueBodySha256': hashlib.sha256(body.encode()).hexdigest(),
                  'candidateSha256': 'a' * 64}
        class Api:
            def json(self, path, method='GET', data=None):
                self_test.assertEqual(method, 'GET')
                return {'state': 'open', 'updated_at': 'new', 'body': body}
        self_test = self
        with tempfile.TemporaryDirectory() as tmp, patch.object(bot, 'GitHub', Api):
            path = Path(tmp) / 'report.json'; path.write_bytes(bot.encode(report))
            args = type('Args', (), {'report': path})()
            self.reject('SubmissionChanged', lambda: bot.post(args))

    def test_catalog_validator_rejects_unknown_fields_and_duplicate_versions(self):
        self.assertTrue(VALIDATOR.is_file(), 'Build the validator before running tests.')
        catalog = {'schemaVersion': 3, 'sourceId': bot.SOURCE, 'snapshotId': 'a' * 40, 'packages': [EXAMPLE['package']]}
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / 'catalog.json'
            def run(value):
                path.write_bytes(bot.encode(value))
                return subprocess.run(['dotnet', str(VALIDATOR), 'catalog', bot.SOURCE, str(path)], capture_output=True).returncode
            self.assertEqual(run(catalog), 0)
            bad = copy.deepcopy(catalog); bad['unexpected'] = True
            self.assertNotEqual(run(bad), 0)
            bad = copy.deepcopy(catalog); bad['packages'].append(copy.deepcopy(bad['packages'][0]))
            self.assertNotEqual(run(bad), 0)
            bad = copy.deepcopy(catalog); bad['packages'][0]['artifact']['tag'] = 'v9.9.9'
            self.assertNotEqual(run(bad), 0)

    def test_invalid_format_mentions_author_with_help_and_error_label(self):
        body = '### Candidate JSON\n{bad json}'
        report = dict(schemaVersion=1, issueNumber=7, status='rejected', code='InvalidJson', issueUpdatedAt='date',
                      issueBodySha256=bot.digest(body.encode()))
        writes = []
        class Api:
            def json(self, path, method='GET', data=None):
                if method == 'GET':
                    return dict(state='open', updated_at='date', body=body, user=dict(login='Submitter'))
                writes.append((path, data)); return {}
        with tempfile.TemporaryDirectory() as temporary, patch.object(bot, 'GitHub', Api):
            path = Path(temporary) / 'report.json'; path.write_bytes(bot.encode(report))
            self.assertEqual(bot.post(type('Args', (), dict(report=path))()), 0)
        self.assertIn('@Submitter', writes[0][1]['body'])
        self.assertIn('JSON 语法', writes[0][1]['body'])
        self.assertIn('Submission example', writes[0][1]['body'])
        self.assertTrue(writes[1][0].endswith('/labels'))
        self.assertEqual(writes[1][1], dict(labels=['plugin-error']))

    def test_format_hints_distinguish_envelope_fence_origin_and_limits(self):
        self.assertIn('### Candidate JSON', bot.failure_hint('MissingCandidateJson'))
        self.assertIn('代码块', bot.failure_hint('InvalidCandidateFence'))
        self.assertIn('schemaVersion', bot.failure_hint('SubmissionEnvelope'))
        self.assertIn('字符串', bot.failure_hint('InvalidOriginId'))
        self.assertIn('大小限制', bot.failure_hint('PayloadLimit'))


if __name__ == '__main__':
    unittest.main()
