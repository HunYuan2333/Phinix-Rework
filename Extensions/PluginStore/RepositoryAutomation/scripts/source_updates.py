#!/usr/bin/env python3
"""A3: explicit maintainer policy, fixed release discovery, trusted evidence and publication."""
import argparse
import copy
from datetime import datetime, timezone
import os
from pathlib import Path
import re
import subprocess
import tempfile
import urllib.parse
import zipfile

from bot import SOURCE, INDEX, GitHub, Rejected, require, strict_json, encode, digest, inspect, is_workshop
from admission import PREFIX, REPOSITORY_ID, WORKFLOW as MANUAL_WORKFLOW, event, maintainer, index, paths, read_bundle, commit, policy
import catalog
import label_admission as labels

WORKFLOW = '.github/workflows/plugin-source-updates.yml'
TITLE = 'Source update admission'
MAX_POLICIES = 32


def version(text):
    require(type(text) is str and re.fullmatch(r'(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)', text) and len(text) <= 32, 'UpdateVersionRejected')
    return tuple(map(int, text.split('.')))


def policy_path(package_id):
    require(type(package_id) is str and re.fullmatch(r'[a-z0-9]+(?:[._-][a-z0-9]+)*', package_id) and len(package_id) <= 128, 'UpdatePolicyRejected')
    return 'update-policies/' + digest(package_id.encode()) + '.json'


def validate_policy(value):
    require(type(value) is dict and set(value) == {'schemaVersion', 'packageId', 'baseCandidateSha256', 'mode', 'assetPrefix'} and
            type(value['schemaVersion']) is int and value['schemaVersion'] == 1 and value['mode'] == 'same-major' and
            type(value['baseCandidateSha256']) is str and re.fullmatch(r'[0-9a-f]{64}', value['baseCandidateSha256']) and
            type(value['assetPrefix']) is str and re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9._-]{0,99}-', value['assetPrefix']), 'UpdatePolicyRejected')
    policy_path(value['packageId'])
    return value


def default_policy(candidate, review):
    p = candidate['package']
    if is_workshop(p):
        return None
    text = p['manifest']['version']; name = p['artifact']['assetName']
    # Existing accepted records stay unchanged. Standard stable asset names allow
    # a new first-time label approval to carry its explicit source-update scope.
    if not re.fullmatch(r'(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)', text) or not name.endswith(text + '.zip'):
        return None
    prefix = name[:-len(text + '.zip')]
    if not re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9._-]{0,99}-', prefix):
        return None
    return validate_policy(dict(schemaVersion=1, packageId=p['id'], baseCandidateSha256=review['candidateSha256'], mode='same-major', assetPrefix=prefix))


def base_names(value):
    prefix = digest(value['packageId'].encode()) + '/' + value['baseCandidateSha256'] + '.json'
    return ['packages/' + prefix, 'reviews/' + prefix, 'policies/' + prefix]


def baseline(value, raws, lock):
    with tempfile.TemporaryDirectory() as temporary:
        folder = Path(temporary)
        for name, raw in zip(('candidate.json', 'review.json', 'policy.json'), raws):
            (folder / name).write_bytes(raw)
        candidate, review, scope = read_bundle(folder)
    p = candidate['package']
    require(p['id'] == value['packageId'] and review['candidateSha256'] == value['baseCandidateSha256'] and
            review['approval']['workflow'] in (MANUAL_WORKFLOW, labels.WORKFLOW) and
            lock == dict(schemaVersion=1, packageId=p['id'], version=p['manifest']['version'],
                         candidateSha256=review['candidateSha256'], artifactSha256=p['artifact']['sha256']), 'UpdateBaselineRejected')
    require(p['artifact']['assetName'] == value['assetPrefix'] + p['manifest']['version'] + '.zip', 'UpdateAssetPolicyRejected')
    version(p['manifest']['version'])
    return candidate, review, scope


def lock_path(package):
    return 'publication-locks/' + digest(package['id'].encode()) + '/' + digest(package['manifest']['version'].encode()) + '.json'


def permitted(value, base, candidate):
    old, new = base['package'], candidate['package']
    require(new['id'] == value['packageId'] and policy(old) == policy(new), 'UpdateScopeChanged')
    a, b = version(old['manifest']['version']), version(new['manifest']['version'])
    require(b > a and b[0] == a[0], 'UpdateVersionOutsidePolicy')
    require(new['author'] == old['author'] and new['license'] == old['license'] and new['tags'] == old['tags'] and
            new['state'] == 'active' and new['manifest']['targetFramework'] == old['manifest']['targetFramework'] and
            sorted(new['manifest']['compatibility']['rimWorldVersions']) == sorted(old['manifest']['compatibility']['rimWorldVersions']) and
            sorted(m['packageId'] for m in new['manifest']['externalMods']) == sorted(m['packageId'] for m in old['manifest']['externalMods']) and
            new['artifact']['assetName'] == value['assetPrefix'] + new['manifest']['version'] + '.zip', 'UpdateScopeChanged')


def tag_commit(api, prefix, tag):
    obj = api.json(prefix + '/git/ref/tags/' + urllib.parse.quote(tag, safe='')).get('object', {})
    for _ in range(5):
        require(re.fullmatch(r'[0-9a-f]{40}', obj.get('sha', '')), 'InvalidTag')
        if obj.get('type') == 'commit':
            return obj['sha']
        require(obj.get('type') == 'tag', 'InvalidTag')
        obj = api.json(prefix + '/git/tags/' + obj['sha']).get('object', {})
    raise Rejected('InvalidTag')


def fetch_new(api, origin, target):
    # The newly discovered asset has no accepted hash yet. Bind API identity/size,
    # hash its first bytes, then inspect() independently downloads against that hash.
    url = 'https://api.github.com/repos/' + origin['repository'] + '/releases/assets/' + origin['assetId']
    for hop in range(6):
        with api.request(url, accept='application/octet-stream', binary=True) as response:
            if response.code == 200:
                require(response.headers.get('Content-Encoding') in (None, 'identity') and response.headers.get('Content-Range') is None and
                        response.headers.get_content_type() in ('application/octet-stream', 'application/zip'), 'OriginResponseType')
                raw = api.consume(response, origin['sizeBytes']); require(len(raw) == origin['sizeBytes'], 'PayloadSizeMismatch')
                target.write_bytes(raw)
                return digest(raw)
            require(hop < 5 and response.headers.get('Location'), 'OriginRedirectLimit')
            url = urllib.parse.urljoin(url, response.headers['Location'])
    raise Rejected('OriginRedirectLimit')


def discover(args, api, value, base, accepted):
    old = base['package']; origin = old['artifact']; prefix = '/repos/' + origin['repository']
    repo = api.json(prefix)
    require(repo.get('private') is False and repo.get('full_name') == origin['repository'] and
            str(repo.get('id')) == origin['repositoryId'] and str(repo.get('owner', {}).get('id')) == origin['ownerId'], 'OriginRepositoryMismatch')
    releases = api.json(prefix + '/releases?per_page=50')
    require(type(releases) is list and len(releases) <= 50, 'UpdateReleaseLimit')
    choices = []
    for release in releases:
        tag = release.get('tag_name', ''); text = tag[1:] if tag.startswith('v') else tag
        if release.get('draft') is False and release.get('prerelease') is False and re.fullmatch(r'(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)', text):
            v = version(text)
            if v > max(accepted):
                choices.append((v, text, release))
    if not choices:
        return None
    _, text, release = max(choices, key=lambda x: x[0])
    require(version(text)[0] == version(old['manifest']['version'])[0], 'UpdateVersionOutsidePolicy')
    require(type(release.get('id')) is int and release['id'] > 0, 'InvalidOriginId')
    assets = release.get('assets'); require(type(assets) is list and len(assets) <= 128, 'UpdateAssetLimit')
    matches = [a for a in assets if a.get('name') == value['assetPrefix'] + text + '.zip']
    require(len(matches) == 1, 'UpdateAssetMissing')
    asset = matches[0]
    require(type(asset.get('id')) is int and asset['id'] > 0 and type(asset.get('size')) is int and 0 < asset['size'] <= 128 * 1024 * 1024 and asset.get('state') == 'uploaded', 'OriginAssetMismatch')
    source = tag_commit(api, prefix, release['tag_name'])
    require(api.json(prefix + '/compare/' + origin['sourceCommit'] + '...' + source).get('status') == 'ahead', 'UpdateSourceAncestryRejected')
    candidate = copy.deepcopy(base); p = candidate['package']
    artifact = p['artifact']
    artifact.update(sourceCommit=source, tag=release['tag_name'], releaseId=str(release['id']), assetId=str(asset['id']), assetName=asset['name'], sizeBytes=asset['size'])
    with tempfile.TemporaryDirectory() as temporary:
        payload = Path(temporary) / 'new.zip'
        artifact['sha256'] = fetch_new(api, artifact, payload)
        require(asset.get('digest') in (None, 'sha256:' + artifact['sha256']), 'OriginDigestMismatch')
        with zipfile.ZipFile(payload) as archive:
            entries = archive.infolist(); require(len(entries) <= 4096 and len({e.filename for e in entries}) == len(entries), 'ArchivePathConflict')
            entry = archive.getinfo('manifest.json'); require(0 < entry.file_size <= 512 * 1024, 'DocumentLimit')
            raw = archive.read(entry); p['manifest'] = strict_json(raw); artifact['manifestSha256'] = digest(raw)
        require(p['manifest']['version'] == text, 'UpdateVersionRejected')
        permitted(value, base, candidate)
        try:
            candidate = catalog.project(candidate, payload, args.validator)
        except ValueError as error:
            raise Rejected(str(error)) from None
    permitted(value, base, candidate)
    with tempfile.TemporaryDirectory() as temporary:
        static = inspect(args, candidate['package'], Path(temporary), api)
    return candidate, static


def receipt_path(review):
    return 'source-update-approvals/' + review['approval']['runId'] + '.json'


def expected_files(candidate, review, scope):
    result = dict(zip(paths(candidate['package'], review['candidateSha256']), map(encode, (candidate, review, scope))))
    result[receipt_path(review)] = encode(review)
    return result


def scan(args, api):
    require(os.environ.get('GITHUB_REPOSITORY') == INDEX and os.environ.get('GITHUB_REF') == 'refs/heads/main' and
            os.environ.get('GITHUB_EVENT_NAME') in ('schedule', 'workflow_dispatch') and os.environ.get('GITHUB_RUN_ATTEMPT') == '1', 'UpdateContextRejected')
    actor = os.environ.get('GITHUB_ACTOR'); actor_id = maintainer(api, actor, os.environ.get('GITHUB_ACTOR_ID'))
    parent = index(api); require(parent == os.environ.get('GITHUB_SHA'), 'TrustedHeadChanged')
    import publisher
    policies = publisher.files(args.root, 'update-policies'); accepted = publisher.files(args.root, 'publication-locks')
    require(len(policies) <= MAX_POLICIES, 'UpdateSourceLimit')
    packages = publisher.files(args.root, 'packages'); args.output.mkdir(parents=True, exist_ok=False)
    reviews = publisher.files(args.root, 'reviews')
    last_update = {}
    for raw in reviews.values():
        review = strict_json(raw)
        # Permanent reviews, rather than a mutable cursor, provide fair source rotation.
        key = review['approval'].get('baseCandidateSha256', review['candidateSha256'])
        last_update[key] = max(last_update.get(key, ''), review['approvedAt'])
    exclusions_path = args.root / 'catalog-exclusions.json'
    excluded = set(strict_json(exclusions_path.read_bytes())['packageIds']) if exclusions_path.exists() else set()
    errors = []; found = None
    for name, raw in sorted(policies.items(), key=lambda item:(last_update.get(strict_json(item[1])['baseCandidateSha256'], ''), item[0])):
        value = validate_policy(strict_json(raw)); require(name == policy_path(value['packageId']), 'UpdatePolicyRejected')
        if value['packageId'] in excluded:
            continue
        try:
            raws = [(args.root / p).read_bytes() for p in base_names(value)]
            base_package = strict_json(raws[0])['package']
            base, base_review, _ = baseline(value, raws, strict_json(accepted[lock_path(base_package)]))
            # An unfinished prior candidate must be resolved before another version is admitted.
            known = [strict_json(p)['package'] for p in packages.values() if strict_json(p)['package']['id'] == value['packageId']]
            require(all(lock_path(p) in accepted for p in known), 'PendingUpdatePublication')
            latest = max(known, key=lambda p:version(p['manifest']['version']))
            api.verify_origin(latest['artifact'])
            result = discover(args, api, value, base, [version(p['manifest']['version']) for p in known])
            if result is None:
                continue
            require(len(packages) < publisher.MAX_RECORDS, 'PublicationRecordLimit')
            candidate, static = result; fingerprint = digest(encode(candidate)); scope = policy(candidate['package'])
            approval = dict(actor=actor, actorId=actor_id, runId=os.environ['GITHUB_RUN_ID'], trustedCommit=parent, workflow=WORKFLOW, attempt=1,
                            method='approved-source', baseCandidateSha256=value['baseCandidateSha256'], updatePolicySha256=digest(encode(value)))
            review = dict(schemaVersion=1, sourceId=SOURCE, candidateSha256=fingerprint, policySha256=digest(encode(scope)), static=static, staticSha256=digest(encode(static)),
                          issueNumber=base_review['issueNumber'], issueBodySha256=base_review['issueBodySha256'], issueUpdatedAt=base_review['issueUpdatedAt'], approvedAt=datetime.now(timezone.utc).isoformat(), approval=approval)
            for file, record in (('candidate.json', candidate), ('review.json', review), ('policy.json', scope)):
                (args.output / file).write_bytes(encode(record))
            read_bundle(args.output); found = fingerprint
            break  # One evidence PR per run; failures in other sources do not prevent this one.
        except (Rejected, ValueError, KeyError, TypeError, OSError, zipfile.BadZipFile, RuntimeError, subprocess.TimeoutExpired):
            import sys
            failure = sys.exc_info()[1]; code = str(failure) if isinstance(failure, Rejected) and re.fullmatch(r'[A-Za-z][A-Za-z0-9]{0,79}', str(failure)) else 'UpdateDataRejected'
            errors.append(dict(packageId=value['packageId'], code=code))
            event('updates.source_rejected', packageId=value['packageId'], reason=code)
    (args.output / 'report.json').write_bytes(encode(dict(schemaVersion=1, candidateSha256=found, errors=errors)))
    event('updates.scan_complete', changed=found is not None, errors=len(errors), checkOnly=args.check_only)
    if os.environ.get('GITHUB_OUTPUT'):
        with open(os.environ['GITHUB_OUTPUT'], 'a') as stream:
            stream.write('changed=' + str(found is not None and not args.check_only).lower() + '\n')


def propose(args, api):
    candidate, review, scope = read_bundle(args.input); a = review['approval']
    require(a['workflow'] == WORKFLOW and a['runId'] == os.environ.get('GITHUB_RUN_ID') and a['trustedCommit'] == os.environ.get('GITHUB_SHA'), 'UpdateContextRejected')
    parent = index(api); labels.ancestry(api, parent, a['trustedCommit'])
    current_policy = validate_policy(strict_json(labels.read_at(api, policy_path(candidate['package']['id']), parent)))
    require(digest(encode(current_policy)) == a['updatePolicySha256'], 'UpdatePolicyChanged')
    expected = expected_files(candidate, review, scope)
    tree = api.json(PREFIX + '/git/trees/' + parent + '?recursive=1')
    require(tree.get('truncated') is False and all(x.get('path') not in expected for x in tree['tree']), 'CandidateAlreadyRecorded')
    api.verify_origin(candidate['package']['artifact'])
    sha = commit(api, parent, expected, 'Approve source update ' + review['candidateSha256'])
    branch = 'codex/source-update-' + a['runId']; api.json(PREFIX + '/git/refs', 'POST', dict(ref='refs/heads/' + branch, sha=sha))
    pull = api.json(PREFIX + '/pulls', 'POST', dict(head=branch, base='main', title='Update ' + candidate['package']['id'] + ' ' + candidate['package']['manifest']['version'],
                   body='Automatic version inside the explicit approved-source policy. Fixed candidate ' + review['candidateSha256'] + '; static ZIP/PE checks passed. No plugin code executed.'))
    pull = api.json(PREFIX + '/pulls/' + str(pull['number'])); require(pull['head']['sha'] == sha, 'AdmissionPrContentChanged')
    labels.pr_content(api, pull, expected); require(index(api) == parent, 'PublicationHeadChanged')
    require(api.json(PREFIX + '/pulls/' + str(pull['number']) + '/merge', 'PUT', dict(sha=sha, merge_method='squash')).get('merged') is True, 'AdmissionMergeFailed')
    event('updates.admitted', number=pull['number'], url=pull['html_url'], candidateSha256=review['candidateSha256'])
    if os.environ.get('GITHUB_STEP_SUMMARY'):
        Path(os.environ['GITHUB_STEP_SUMMARY']).write_text('Update evidence PR: ' + pull['html_url'] + '\n')


def proof(api, candidate, review, scope, snapshot, accepted):
    a = review['approval']; require(a['workflow'] == WORKFLOW and a['method'] == 'approved-source' and type(a['attempt']) is int and a['attempt'] == 1 and
        re.fullmatch(r'[1-9][0-9]*', a['runId']) and re.fullmatch(r'[0-9a-f]{40}', a['trustedCommit']), 'ApprovalProofRejected')
    value = validate_policy(strict_json(labels.read_at(api, policy_path(candidate['package']['id']), a['trustedCommit'])))
    require(digest(encode(value)) == a['updatePolicySha256'] and value['baseCandidateSha256'] == a['baseCandidateSha256'], 'UpdatePolicyChanged')
    raws = [labels.read_at(api, name, a['trustedCommit']) for name in base_names(value)]
    base_package = strict_json(raws[0])['package']
    base, original, base_scope = baseline(value, raws, strict_json(labels.read_at(api, lock_path(base_package), a['trustedCommit'])))
    permitted(value, base, candidate)
    require(review['issueNumber'] == original['issueNumber'] and review['issueBodySha256'] == original['issueBodySha256'], 'UpdateBaselineRejected')
    import publisher
    publisher.approval_proof(api, base_package, original, base_scope, base, snapshot, True)
    if not accepted:
        current = validate_policy(strict_json(labels.read_at(api, policy_path(candidate['package']['id']), snapshot)))
        require(encode(current) == encode(value), 'UpdatePolicyChanged')
    run = api.json(PREFIX + '/actions/runs/' + a['runId'])
    require(run.get('event') in ('schedule', 'workflow_dispatch') and run.get('path') == WORKFLOW and run.get('display_title') == TITLE and
            run.get('head_branch') == 'main' and run.get('head_sha') == a['trustedCommit'] and run.get('conclusion') == 'success' and
            run.get('status') == 'completed' and run.get('run_attempt') == 1 and str(run.get('head_repository', {}).get('id')) == REPOSITORY_ID and
            run.get('actor', {}).get('type') == 'User' and run['actor']['login'] == a['actor'] and str(run['actor']['id']) == a['actorId'], 'ApprovalProofRejected')
    maintainer(api, a['actor'], a['actorId']); labels.ancestry(api, snapshot, a['trustedCommit'])
    pulls = api.json(PREFIX + '/pulls?state=closed&per_page=2&head=HunYuan2333:codex%2Fsource-update-' + a['runId'])
    require(type(pulls) is list and len(pulls) == 1, 'AdmissionPrMissing')
    pull = api.json(PREFIX + '/pulls/' + str(pulls[0]['number'])); require(pull.get('merged') is True, 'AdmissionPrNotMerged')
    labels.bot_identity(pull.get('merged_by', {})); labels.pr_content(api, pull, expected_files(candidate, review, scope))


def upstream(api):
    data = labels.payload(); run = data.get('workflow_run', {})
    require(data.get('action') == 'completed' and str(data.get('repository', {}).get('id')) == REPOSITORY_ID and
            run.get('event') in ('schedule', 'workflow_dispatch') and run.get('head_branch') == 'main' and run.get('status') == 'completed' and run.get('conclusion') == 'success' and run.get('run_attempt') == 1 and
            re.fullmatch(r'[1-9][0-9]*', str(run.get('id', ''))), 'PublicationTriggerRejected')
    actual = api.json(PREFIX + '/actions/runs/' + str(run.get('id')))
    require(actual.get('path') == WORKFLOW and actual.get('display_title') == TITLE and str(actual.get('head_repository', {}).get('id')) == REPOSITORY_ID and
            all(actual.get(k) == run.get(k) for k in ('id', 'event', 'head_sha', 'head_branch', 'status', 'conclusion', 'run_attempt')) and
            all(actual.get('actor', {}).get(k) == run.get('actor', {}).get(k) for k in ('id', 'login', 'type')), 'PublicationTriggerRejected')
    maintainer(api, actual['actor']['login'], str(actual['actor']['id']))
    jobs = api.json(PREFIX + '/actions/runs/' + str(run['id']) + '/jobs?per_page=100')
    propose = [j for j in jobs.get('jobs', []) if j.get('name') == 'propose']
    require(len(propose) == 1 and propose[0].get('conclusion') in ('success', 'skipped'), 'PublicationTriggerRejected')
    return str(run['id']) if propose[0]['conclusion'] == 'success' else None


def report(args, api):
    result = strict_json((args.input / 'report.json').read_bytes())
    require(type(result) is dict and set(result) == {'schemaVersion', 'candidateSha256', 'errors'} and result['schemaVersion'] == 1 and
            type(result['errors']) is list and len(result['errors']) <= MAX_POLICIES, 'UpdateReportRejected')
    if getattr(args, 'admission_failed', False):
        candidate, review, _ = read_bundle(args.input)
        require(result['candidateSha256'] == review['candidateSha256'], 'UpdateReportRejected')
        result['errors'].append(dict(packageId=candidate['package']['id'], code='UpdateAdmissionFailed'))
    for error in result['errors']:
        policy_path(error['packageId']); require(type(error['code']) is str and re.fullmatch(r'[A-Za-z][A-Za-z0-9]{0,79}', error['code']), 'UpdateReportRejected')
        title = 'Update blocked: ' + error['packageId']
        issues = api.json(PREFIX + '/issues?state=open&per_page=100')
        matches = [i for i in issues if i.get('title') == title and 'pull_request' not in i]
        require(len(matches) <= 1, 'UpdateReportAmbiguous')
        body = 'Automatic source update paused.\n\nPackage: `' + error['packageId'] + '`\nCode: `' + error['code'] + '`\n\n' + \
               'Previous published versions are retained. Correct the release, or review a changed policy.\n\n' + \
               '[Run log](https://github.com/' + INDEX + '/actions/runs/' + os.environ['GITHUB_RUN_ID'] + ')'
        if matches:
            api.json(PREFIX + '/issues/' + str(matches[0]['number']), 'PATCH', dict(body=body))
        else:
            api.json(PREFIX + '/issues', 'POST', dict(title=title, body=body, labels=['plugin-error']))


def feedback(args, api):
    run = upstream(api)
    if run is None:
        return
    parent = index(api); review = strict_json(labels.read_at(api, 'source-update-approvals/' + run + '.json', parent))
    require(review['approval']['runId'] == run and review['approval']['workflow'] == WORKFLOW, 'ApprovalRecordMismatch')
    package = strict_json(labels.read_at(api, 'packages/' + digest(validate_policy_id(review, api, parent).encode()) + '/' + review['candidateSha256'] + '.json', parent))['package']
    if args.success:
        lock = strict_json(labels.read_at(api, 'source-update-locks/' + run + '.json', parent))
        require(lock == dict(schemaVersion=1, runId=run, candidateSha256=review['candidateSha256'], reviewSha256=digest(encode(review))), 'UpdatePublicationUnproven')
        issues = api.json(PREFIX + '/issues?state=open&per_page=100')
        for issue in issues:
            if issue.get('title') == 'Update blocked: ' + package['id'] and 'pull_request' not in issue:
                api.json(PREFIX + '/issues/' + str(issue['number']), 'PATCH', dict(state='closed', state_reason='completed'))
                for label in issue.get('labels', []):
                    if label.get('name') == 'plugin-error':
                        api.json(PREFIX + '/issues/' + str(issue['number']) + '/labels/plugin-error', 'DELETE')
    else:
        with tempfile.TemporaryDirectory() as temporary:
            folder = Path(temporary); (folder / 'report.json').write_bytes(encode(dict(schemaVersion=1, candidateSha256=review['candidateSha256'], errors=[dict(packageId=package['id'], code='UpdatePublicationFailed')])))
            original = args.input; args.input = folder
            try:
                report(args, api)
            finally:
                args.input = original


def validate_policy_id(review, api, parent):
    # Resolve by the immutable per-run record, not arbitrary input from a report job.
    tree = api.json(PREFIX + '/git/trees/' + parent + '?recursive=1')
    require(tree.get('truncated') is False, 'IndexTreeLimit')
    matches = [p['path'] for p in tree['tree'] if p.get('type') == 'blob' and p.get('path', '').startswith('packages/') and p['path'].endswith('/' + review['candidateSha256'] + '.json')]
    require(len(matches) == 1, 'ApprovalRecordMissing')
    candidate = strict_json(labels.read_at(api, matches[0], parent)); p = candidate['package']; policy_path(p['id'])
    require(matches[0] == paths(p, review['candidateSha256'])[0] and digest(encode(candidate)) == review['candidateSha256'], 'ApprovalRecordMismatch')
    return p['id']


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command', choices=['scan', 'propose', 'report', 'feedback']); parser.add_argument('--root', type=Path, default=Path('.'))
    parser.add_argument('--validator', type=Path); parser.add_argument('--output', type=Path); parser.add_argument('--input', type=Path); parser.add_argument('--check-only', action='store_true')
    parser.add_argument('--success', choices=['true', 'false'], default='false'); parser.add_argument('--admission-failed', action='store_true')
    args = parser.parse_args()
    args.success = args.success == 'true'
    try:
        api = GitHub(max_calls=512, timeout=1200)
        {'scan': scan, 'propose': propose, 'report': report, 'feedback': feedback}[args.command](args, api)
    except (Rejected, OSError, KeyError, ValueError, TypeError, subprocess.TimeoutExpired) as error:
        event('updates.rejected', reason=str(error) if isinstance(error, Rejected) else 'UpdateDataRejected'); raise SystemExit(1)


if __name__ == '__main__':
    main()
