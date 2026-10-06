import base64
import copy
import json
import os
from pathlib import Path
import tempfile
import io
import zipfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

from test_admission_publication import bundle, FakeApi as AdmissionFakeApi, CONTEXT, HEAD, VALIDATOR
import admission
import bot
import publisher
import source_updates as updates

# No source binary is executed; scan tests isolate network origin checks.
class FakeApi(AdmissionFakeApi):
    def verify_origin(self, artifact):
        pass


def configured():
    candidate, review, scope = bundle()
    p = candidate['package']; v = p['manifest']['version']
    prefix = p['artifact']['assetName'][:-len(v + '.zip')]
    value = dict(schemaVersion=1, packageId=p['id'], baseCandidateSha256=review['candidateSha256'], mode='same-major', assetPrefix=prefix)
    return candidate, review, scope, value


def next_candidate(base):
    result = copy.deepcopy(base); p = result['package']; old = p['manifest']['version']
    p['manifest']['version'] = '1.3.1'; p['artifact']['tag'] = 'v1.3.1'; p['artifact']['assetName'] = p['artifact']['assetName'].replace(old, '1.3.1')
    p['artifact']['sourceCommit'] = 'b' * 40
    return result


def write_inputs(root):
    candidate, review, scope, value = configured()
    for name, record in zip(admission.paths(candidate['package'], review['candidateSha256']), (candidate, review, scope)):
        path = root / name; path.parent.mkdir(parents=True, exist_ok=True); path.write_bytes(bot.encode(record))
    for name, raw in publisher.locks_for([(candidate, review, scope)]).items():
        path = root / name; path.parent.mkdir(parents=True, exist_ok=True); path.write_bytes(raw)
    path = root / updates.policy_path(value['packageId']); path.parent.mkdir(); path.write_bytes(bot.encode(value))
    return candidate, review, scope, value


class SourceUpdateTests(unittest.TestCase):
    def test_policy_rejects_extra_fields_wrong_mode_and_paths(self):
        value = configured()[3]
        self.assertEqual(updates.validate_policy(value), value)
        for key, change in [('mode', 'anything'), ('packageId', '../source'), ('assetPrefix', '../asset-'), ('baseCandidateSha256', 'wrong'), ('schemaVersion', True)]:
            bad = dict(value); bad[key] = change
            with self.assertRaises(bot.Rejected):
                updates.validate_policy(bad)
        bad = dict(value, extra=True)
        with self.assertRaises(bot.Rejected): updates.validate_policy(bad)

    def test_only_stable_three_part_versions(self):
        for value in ['v1.0.0', '01.0.0', '1.0', '1.0.0-rc.1', '1.0.0+build', '../1.0.0']:
            with self.assertRaises(bot.Rejected): updates.version(value)
        self.assertGreater(updates.version('1.10.0'), updates.version('1.9.0'))

    def test_scoped_version_and_display_changes_are_allowed(self):
        base, _, _, value = configured(); candidate = next_candidate(base)
        candidate['package']['localization']['translations']['en-US']['changelog'] = 'New release'
        updates.permitted(value, base, candidate)

    def test_major_origin_module_dependency_and_external_mod_changes_pause(self):
        base, _, _, value = configured()
        changes = [lambda p: p['artifact'].update(ownerId='99'), lambda p: p['artifact'].update(repository='other/repo'),
                   lambda p: p['manifest']['modules'][0].update(id='other.module'),
                   lambda p: p['manifest']['dependencies'].append(dict(packageId='new.dep', versionRange='1.0.0', optional=True)),
                   lambda p: p['manifest']['externalMods'].append(dict(packageId='other.mod')),
                   lambda p: p['manifest']['compatibility'].update(rimWorldVersions=['1.7']),
                   lambda p: p['manifest'].update(version='2.0.0'), lambda p: p.update(author='other')]
        for change in changes:
            candidate = next_candidate(base); change(candidate['package'])
            with self.assertRaises(bot.Rejected): updates.permitted(value, base, candidate)

    def test_baseline_requires_published_human_approval(self):
        base, review, scope, value = configured(); lock = bot.strict_json(next(iter(publisher.locks_for([(base, review, scope)]).values())))
        self.assertEqual(updates.baseline(value, list(map(bot.encode, (base, review, scope))), lock)[0], base)
        lock['candidateSha256'] = '0' * 64
        with self.assertRaises(bot.Rejected): updates.baseline(value, list(map(bot.encode, (base, review, scope))), lock)

    def test_no_change_scan_never_writes_github(self):
        with tempfile.TemporaryDirectory() as temporary, patch.dict(os.environ, CONTEXT, clear=True), patch.object(updates, 'discover', return_value=None):
            root = Path(temporary); write_inputs(root); api = FakeApi()
            updates.scan(SimpleNamespace(root=root, output=root/'out', check_only=False, validator=VALIDATOR), api)
            self.assertEqual(api.writes, [])
            self.assertEqual(bot.strict_json((root/'out/report.json').read_bytes())['errors'], [])

    def test_candidate_is_exactly_bound_to_policy_run_and_original_issue(self):
        with tempfile.TemporaryDirectory() as temporary, patch.dict(os.environ, CONTEXT, clear=True):
            root = Path(temporary); base, original, _, value = write_inputs(root); candidate = next_candidate(base)
            static = dict(original['static'], version='1.3.1')
            with patch.object(updates, 'discover', return_value=(candidate, static)):
                updates.scan(SimpleNamespace(root=root, output=root/'out', check_only=False, validator=VALIDATOR), FakeApi())
            saved, review, scope = admission.read_bundle(root/'out')
            self.assertEqual(saved, candidate)
            self.assertEqual(review['issueNumber'], original['issueNumber'])
            self.assertEqual(review['approval']['updatePolicySha256'], bot.digest(bot.encode(value)))
            self.assertEqual(review['approval']['workflow'], updates.WORKFLOW)
            self.assertEqual(len(updates.expected_files(saved, review, scope)), 4)

    def test_source_error_is_structured_and_keeps_original_records(self):
        with tempfile.TemporaryDirectory() as temporary, patch.dict(os.environ, CONTEXT, clear=True), patch.object(updates, 'discover', side_effect=bot.Rejected('UpdateScopeChanged')):
            root=Path(temporary); base, review, _, _=write_inputs(root); path=root/admission.paths(base['package'], review['candidateSha256'])[0]; before=path.read_bytes()
            updates.scan(SimpleNamespace(root=root, output=root/'out', check_only=False, validator=VALIDATOR), FakeApi())
            self.assertEqual(path.read_bytes(), before)
            self.assertEqual(bot.strict_json((root/'out/report.json').read_bytes())['errors'][0]['code'], 'UpdateScopeChanged')

    def test_excluded_developer_fixture_never_contacts_author(self):
        with tempfile.TemporaryDirectory() as temporary, patch.dict(os.environ, CONTEXT, clear=True), patch.object(updates, 'discover') as discover:
            root=Path(temporary); base, _, _, _=write_inputs(root)
            (root/'catalog-exclusions.json').write_bytes(bot.encode(dict(schemaVersion=1, packageIds=[base['package']['id']], reason='fixture')))
            updates.scan(SimpleNamespace(root=root, output=root/'out', check_only=False, validator=VALIDATOR), FakeApi())
            discover.assert_not_called()

    def test_draft_prerelease_and_accepted_versions_send_no_asset_request(self):
        base, _, _, value=configured()
        class Api:
            def json(self, path):
                if '/releases?' in path:
                    return [dict(tag_name='v1.3.0',draft=False,prerelease=False), dict(tag_name='v1.3.1',draft=True,prerelease=False), dict(tag_name='v1.3.2',draft=False,prerelease=True)]
                return dict(private=False, full_name=base['package']['artifact']['repository'], id=int(base['package']['artifact']['repositoryId']), owner=dict(id=int(base['package']['artifact']['ownerId'])))
        self.assertIsNone(updates.discover(SimpleNamespace(validator=VALIDATOR), Api(), value, base, [(1,3,0)]))

    def test_changed_policy_stops_pending_admission_before_writes(self):
        with tempfile.TemporaryDirectory() as temporary, patch.dict(os.environ, CONTEXT, clear=True):
            root=Path(temporary); base, original, _, _=write_inputs(root); candidate=next_candidate(base)
            with patch.object(updates,'discover',return_value=(candidate,dict(original['static'],version='1.3.1'))):
                updates.scan(SimpleNamespace(root=root,output=root/'out',check_only=False,validator=VALIDATOR), FakeApi())
            api=FakeApi()
            with patch.object(updates.labels,'read_at',return_value=bot.encode(dict(configured()[3],assetPrefix='changed-'))), patch.object(updates.labels,'ancestry'):
                with self.assertRaisesRegex(bot.Rejected,'UpdatePolicyChanged'):
                    updates.propose(SimpleNamespace(input=root/'out'),api)
            self.assertEqual(api.writes,[])

    def test_saved_proof_cannot_be_rebound_to_another_review(self):
        base, review, scope, _=configured(); saved=publisher.proof_record(base,review,scope)
        with patch.object(publisher.label_admission,'read_at',return_value=bot.encode(saved)):
            publisher.approval_proof(None,base['package'],review,scope,base,HEAD,True)
            changed=copy.deepcopy(review);changed['approvedAt']='modified'
            with self.assertRaisesRegex(bot.Rejected,'ApprovalProofLockChanged'):
                publisher.approval_proof(None,base['package'],changed,scope,base,HEAD,True)

    def test_accepted_content_bound_proof_survives_expired_actions_without_redownloading(self):
        with tempfile.TemporaryDirectory() as temporary:
            root=Path(temporary); candidate, review, scope, _=write_inputs(root)
            with patch.object(publisher.label_admission,'read_at',return_value=bot.encode(publisher.proof_record(candidate,review,scope))), patch.object(publisher,'inspect') as inspect:
                records, locks, previous=publisher.collect(SimpleNamespace(root=root),None,HEAD)
                self.assertEqual(len(records),1);self.assertEqual(locks,previous);inspect.assert_not_called()

    def test_unpublished_candidate_cannot_skip_fresh_static_verification(self):
        with tempfile.TemporaryDirectory() as temporary:
            root=Path(temporary); candidate, review, scope, _=write_inputs(root)
            for path in (root/'publication-locks').rglob('*.json'): path.unlink()
            with patch.object(publisher,'approval_proof',return_value=None) as proof, patch.object(publisher,'inspect',return_value=review['static']) as inspect:
                publisher.collect(SimpleNamespace(root=root),FakeApi(),HEAD)
                self.assertFalse(proof.call_args.args[-1]);self.assertEqual(inspect.call_count,1)

    def test_error_report_reuses_one_tracking_issue(self):
        class Api:
            def __init__(self): self.writes=[]
            def json(self,path,method='GET',data=None):
                if method=='GET': return [dict(number=22,title='Update blocked: phinix.example.basic')]
                self.writes.append((path,method,data));return {}
        with tempfile.TemporaryDirectory() as temporary, patch.dict(os.environ,CONTEXT,clear=True):
            root=Path(temporary);(root/'report.json').write_bytes(bot.encode(dict(schemaVersion=1,candidateSha256=None,errors=[dict(packageId='phinix.example.basic',code='UpdateScopeChanged')])))
            api=Api();updates.report(SimpleNamespace(input=root),api)
            self.assertEqual(len(api.writes),1);self.assertEqual(api.writes[0][1],'PATCH')

    def test_discovery_binds_tag_origin_first_hash_and_second_inspection(self):
        base, original, _, value=configured(); next=next_candidate(base); output=io.BytesIO()
        manifest=bot.encode(next['package']['manifest'])
        with zipfile.ZipFile(output,'w') as archive: archive.writestr('manifest.json',manifest)
        raw=output.getvalue()
        class Api:
            def json(self,path):
                if '/releases?' in path: return [dict(id=17,tag_name='v1.3.1',draft=False,prerelease=False,assets=[dict(id=18,name=value['assetPrefix']+'1.3.1.zip',size=len(raw),state='uploaded',digest='sha256:'+bot.digest(raw))])]
                if '/git/ref/' in path: return dict(object=dict(type='commit',sha='b'*40))
                if '/compare/' in path: return dict(status='ahead')
                a=base['package']['artifact'];return dict(private=False,full_name=a['repository'],id=int(a['repositoryId']),owner=dict(id=int(a['ownerId'])))
        def fetch(api,origin,path): path.write_bytes(raw);return bot.digest(raw)
        with patch.object(updates,'fetch_new',side_effect=fetch), patch.object(updates.catalog,'project',side_effect=lambda c,p,v:c), patch.object(updates,'inspect',return_value=dict(original['static'],version='1.3.1')) as inspect:
            candidate, static=updates.discover(SimpleNamespace(validator=VALIDATOR),Api(),value,base,[(1,3,0)])
            self.assertEqual(candidate['package']['artifact']['sha256'],bot.digest(raw))
            self.assertEqual(candidate['package']['artifact']['manifestSha256'],bot.digest(manifest))
            self.assertEqual(candidate['package']['artifact']['sourceCommit'],'b'*40)
            self.assertEqual(candidate['package']['artifact']['assetId'],'18')
            inspect.assert_called_once()

    def test_source_proof_checks_finished_run_policy_and_exact_pr(self):
        with tempfile.TemporaryDirectory() as temporary,patch.dict(os.environ,CONTEXT,clear=True):
            root=Path(temporary);base, original, _, value=write_inputs(root); candidate=next_candidate(base)
            with patch.object(updates,'discover',return_value=(candidate,dict(original['static'],version='1.3.1'))):
                updates.scan(SimpleNamespace(root=root,output=root/'out',check_only=False,validator=VALIDATOR),FakeApi())
            candidate, review, scope=admission.read_bundle(root/'out')
            run=dict(event='workflow_dispatch',path=updates.WORKFLOW,display_title=updates.TITLE,head_branch='main',head_sha=HEAD,conclusion='success',status='completed',run_attempt=1,
                     head_repository=dict(id=int(admission.REPOSITORY_ID)),actor=dict(type='User',login='Owner',id=7))
            class Api(FakeApi):
                def json(self,path,method='GET',data=None):
                    if '/actions/runs/' in path: return self.run
                    if '/pulls?' in path: return [dict(number=33)]
                    if path.endswith('/pulls/33'): return dict(merged=True,merged_by=dict(type='Bot',login='github-actions[bot]',id=int(updates.labels.BOT_ID)))
                    return super().json(path,method,data)
            api=Api();api.run=run
            def read(api,name,ref): return (root/name).read_bytes()
            with patch.object(updates.labels,'read_at',side_effect=read),patch.object(publisher,'approval_proof') as base_proof,patch.object(updates.labels,'ancestry'),patch.object(updates.labels,'pr_content') as pr:
                updates.proof(api,candidate,review,scope,'c'*40,False)
                self.assertTrue(base_proof.call_args.args[-1]);self.assertEqual(len(pr.call_args.args[-1]),4)
                for key,bad in [('run_attempt',2),('conclusion','failure'),('path','.github/workflows/other.yml'),('head_sha','d'*40)]:
                    api.run=dict(run,**{key:bad})
                    with self.assertRaises(bot.Rejected): updates.proof(api,candidate,review,scope,'c'*40,False)

    def test_no_admission_upstream_skips_publication(self):
        run=dict(id=123,event='schedule',head_branch='main',head_sha=HEAD,status='completed',conclusion='success',run_attempt=1,actor=dict(type='User',login='Owner',id=7))
        actual=dict(run,path=updates.WORKFLOW,display_title=updates.TITLE,head_repository=dict(id=int(admission.REPOSITORY_ID)))
        class Api(FakeApi):
            def json(self,path,method='GET',data=None):
                if '/jobs?' in path:return dict(jobs=[dict(name='propose',conclusion='skipped')])
                if '/actions/runs/' in path:return actual
                return super().json(path,method,data)
        with patch.object(updates.labels,'payload',return_value=dict(action='completed',repository=dict(id=int(admission.REPOSITORY_ID)),workflow_run=run)):
            self.assertIsNone(updates.upstream(Api()))

    def test_success_feedback_requires_permanent_publication_receipt(self):
        base, original, _, _=configured(); review=copy.deepcopy(original);review['approval'].update(runId='123',workflow=updates.WORKFLOW)
        package=base['package'];lock=dict(schemaVersion=1,runId='123',candidateSha256=review['candidateSha256'],reviewSha256=bot.digest(bot.encode(review)))
        class Api(FakeApi):
            def json(self,path,method='GET',data=None):
                if method!='GET':self.writes.append((path,method,data));return {}
                if '/issues?' in path:return [dict(number=22,title='Update blocked: '+package['id'],labels=[dict(name='plugin-error')])]
                return super().json(path,method,data)
        def read(api,name,ref):
            if name.startswith('source-update-approvals/'):return bot.encode(review)
            if name.startswith('source-update-locks/'):return bot.encode(lock)
            return bot.encode(base)
        api=Api()
        with patch.object(updates,'upstream',return_value='123'),patch.object(updates.labels,'read_at',side_effect=read),patch.object(updates,'validate_policy_id',return_value=package['id']):
            updates.feedback(SimpleNamespace(success=True,input=None),api)
            self.assertEqual([x[1] for x in api.writes],['PATCH','DELETE'])
            api.writes=[];lock['candidateSha256']='0'*64
            with self.assertRaises(bot.Rejected):updates.feedback(SimpleNamespace(success=True,input=None),api)
            self.assertEqual(api.writes,[])
