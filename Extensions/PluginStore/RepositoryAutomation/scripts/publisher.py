#!/usr/bin/env python3
"""Controlled A4: revalidate merged A2 records, immutable release, atomic stable-last commit."""
import argparse
import base64
import os
from pathlib import Path
import re
import subprocess
import tempfile

from bot import SOURCE, INDEX, GitHub, Rejected, require, strict_json, encode, digest, inspect, validator
from admission import PREFIX, REPOSITORY_ID, OWNER_ID, WORKFLOW, event, maintainer, index, paths, read_bundle, commit
import label_admission
import source_updates

MAX_RECORDS = 256


def files(root, directory):
    result = {}
    folder = root / directory
    if not folder.exists():
        return result
    require(folder.is_dir() and not folder.is_symlink(), 'InputPathRejected')
    for path in folder.rglob('*'):
        require(not path.is_symlink(), 'InputPathRejected')
        if path.is_file():
            if path.name == 'README.md':
                continue
            require(path.suffix == '.json' and path.stat().st_size <= 2 * 1024 * 1024, 'InputPathRejected')
            require(len(result) < MAX_RECORDS, 'PublicationRecordLimit')
            result[path.relative_to(root).as_posix()] = path.read_bytes()
    return result


def record_bundle(candidate_raw, review_raw, policy_raw):
    with tempfile.TemporaryDirectory() as temporary:
        root = Path(temporary)
        for name, raw in zip(('candidate.json', 'review.json', 'policy.json'), (candidate_raw, review_raw, policy_raw)):
            (root / name).write_bytes(raw)
        return read_bundle(root)


def approval_proof(api, package, review, scope, candidate, snapshot, accepted=False):
    # Accepted decisions have permanent content-bound evidence. Actions run/PR
    # retention and a former maintainer leaving must not invalidate those versions.
    if accepted:
        try:
            saved = strict_json(label_admission.read_at(api, proof_path(review), snapshot))
        except Rejected as error:
            if str(error) != 'OriginHttp404':
                raise
        else:
            require(saved == proof_record(candidate, review, scope), 'ApprovalProofLockChanged')
            return True
    approval = review['approval']
    if approval.get('workflow') == source_updates.WORKFLOW:
        source_updates.proof(api, candidate, review, scope, snapshot, accepted)
        return
    if approval.get('workflow') == label_admission.WORKFLOW:
        label_admission.proof(api, candidate, review, scope, snapshot)
        return
    require(type(approval['attempt']) is int and approval['attempt'] == 1 and approval['workflow'] == WORKFLOW and
            re.fullmatch(r'[1-9][0-9]*', approval['runId']) and
            re.fullmatch(r'[0-9a-f]{40}', approval['trustedCommit']), 'ApprovalProofRejected')
    run = api.json(PREFIX + '/actions/runs/' + approval['runId'])
    title = 'Admit #' + str(review['issueNumber']) + ' ' + review['candidateSha256']
    require(run.get('event') == 'workflow_dispatch' and run.get('conclusion') == 'success' and
            run.get('status') == 'completed' and run.get('path') == WORKFLOW and run.get('head_branch') == 'main' and
            run.get('head_sha') == approval['trustedCommit'] and run.get('run_attempt') == 1 and
            run.get('display_title') == title and run.get('actor', {}).get('login') == approval['actor'] and
            str(run.get('actor', {}).get('id')) == approval['actorId'] and
            run.get('actor', {}).get('type') == 'User', 'ApprovalProofRejected')
    maintainer(api, approval['actor'], approval['actorId'])
    compare = api.json(PREFIX + '/compare/' + approval['trustedCommit'] + '...' + snapshot)
    require(compare.get('status') in ('ahead', 'identical'), 'ApprovalAncestryRejected')
    pulls = api.json(PREFIX + '/pulls?state=closed&per_page=2&head=HunYuan2333:codex%2Fadmission-' + approval['runId'])
    require(type(pulls) is list and len(pulls) == 1, 'AdmissionPrMissing')
    pull = api.json(PREFIX + '/pulls/' + str(pulls[0]['number']))
    require(pull.get('merged') is True and pull.get('base', {}).get('ref') == 'main' and
            str(pull.get('base', {}).get('repo', {}).get('id')) == REPOSITORY_ID and
            str(pull.get('head', {}).get('repo', {}).get('id')) == REPOSITORY_ID, 'AdmissionPrNotMerged')
    merger = pull.get('merged_by', {})
    maintainer(api, merger.get('login'), str(merger.get('id')))
    changes = api.json(PREFIX + '/pulls/' + str(pull['number']) + '/files?per_page=100')
    names = paths(package, review['candidateSha256'])
    require(len(changes) == 3 and {c.get('filename') for c in changes} == set(names) and
            all(c.get('status') == 'added' for c in changes), 'AdmissionPrScopeRejected')
    for name, value in zip(names, (candidate, review, scope)):
        info = api.json(PREFIX + '/contents/' + name + '?ref=' + pull['head']['sha'])
        require(info.get('type') == 'file' and info.get('encoding') == 'base64' and
                base64.b64decode(info['content'], validate=False) == encode(value), 'AdmissionPrContentChanged')


def proof_path(review):
    return 'approval-proof-locks/' + review['candidateSha256'] + '.json'


def proof_record(candidate, review, scope):
    return dict(schemaVersion=1, candidateSha256=digest(encode(candidate)), reviewSha256=digest(encode(review)), scopeSha256=digest(encode(scope)))


def locks_for(records):
    result = {}
    for candidate, review, scope in records:
        package = candidate['package']; key = digest(package['id'].encode())
        path = 'publication-locks/' + key + '/' + digest(package['manifest']['version'].encode()) + '.json'
        raw = encode(dict(schemaVersion=1, packageId=package['id'], version=package['manifest']['version'],
                          candidateSha256=review['candidateSha256'], artifactSha256=package['artifact']['sha256']))
        require(path not in result, 'AcceptedVersionConflict')
        result[path] = raw
    return result


def continuity(old, new):
    # Published versions cannot disappear or change, including their display metadata.
    require(all(path in new and raw == new[path] for path, raw in old.items()), 'AcceptedVersionChanged')


def player_records(root, records, old_locks):
    """Maintainer-owned listing exclusions; never alter accepted bytes or historical locks."""
    path = root / 'catalog-exclusions.json'
    if not path.exists():
        return records
    require(path.is_file() and not path.is_symlink() and path.stat().st_size <= 16384, 'CatalogExclusionRejected')
    raw = path.read_bytes(); policy = strict_json(raw)
    require(type(policy) is dict and set(policy) == {'schemaVersion', 'packageIds', 'reason'} and
            type(policy['schemaVersion']) is int and policy['schemaVersion'] == 1 and
            type(policy['packageIds']) is list and len(policy['packageIds']) <= 32 and
            all(type(v) is str and len(v) <= 128 and re.fullmatch(r'[a-z0-9]+(?:[._-][a-z0-9]+)*', v)
                for v in policy['packageIds']) and len(set(policy['packageIds'])) == len(policy['packageIds']) and
            type(policy['reason']) is str and 1 <= len(policy['reason'].strip()) <= 1024, 'CatalogExclusionRejected')
    ids = set(policy['packageIds']); known = {c['package']['id'] for c, _, _ in records}
    require(ids <= known, 'CatalogExclusionUnknownPackage')
    published = {strict_json(v)['candidateSha256'] for v in old_locks.values()}
    require(all(r['candidateSha256'] in published for c, r, _ in records if c['package']['id'] in ids),
            'CatalogExclusionUnpublished')
    visible = [r for r in records if r[0]['package']['id'] not in ids]
    event('publication.listing_exclusions', policySha256=digest(raw), excludedPackages=len(ids), visibleVersions=len(visible))
    return visible


def collect(args, api, snapshot):
    candidates = files(args.root, 'packages'); reviews = files(args.root, 'reviews'); scopes = files(args.root, 'policies')
    require(candidates and len(candidates) <= MAX_RECORDS, 'NoApprovedPackages')
    records = []; expected_reviews = set(); expected_scopes = set()
    for name, raw in sorted(candidates.items()):
        candidate = strict_json(raw); package = candidate['package']; fingerprint = digest(encode(candidate))
        package_path, review_path, scope_path = paths(package, fingerprint)
        require(name == package_path and review_path in reviews and scope_path in scopes, 'ApprovalRecordMissing')
        candidate, review, scope = record_bundle(raw, reviews[review_path], scopes[scope_path])
        records.append((candidate, review, scope)); expected_reviews.add(review_path); expected_scopes.add(scope_path)
    require(set(reviews) == expected_reviews and set(scopes) == expected_scopes, 'OrphanApprovalRecord')
    locks = locks_for(records); old_locks = files(args.root, 'publication-locks'); continuity(old_locks, locks)
    published = {strict_json(v)['candidateSha256'] for v in old_locks.values()}
    require(sum(c['package']['artifact']['sizeBytes'] for c, r, _ in records if r['candidateSha256'] not in published) <= 512 * 1024 * 1024, 'PublicationPayloadLimit')
    for candidate, review, scope in records:
        package = candidate['package']
        event('publication.approval_started', packageId=package['id'], candidateSha256=review['candidateSha256'])
        permanent = approval_proof(api, package, review, scope, candidate, snapshot, review['candidateSha256'] in published)
        if permanent:
            event('publication.accepted_evidence_verified', packageId=package['id'], candidateSha256=review['candidateSha256'])
            continue
        if review['candidateSha256'] not in {strict_json(v)['candidateSha256'] for v in old_locks.values()}:
            current = api.json(PREFIX + '/issues/' + str(review['issueNumber']))
            require('pull_request' not in current and
                    digest((current.get('body') or '').encode()) == review['issueBodySha256'], 'SubmissionChanged')
        event('publication.candidate_started', packageId=package['id'], version=package['manifest']['version'],
              candidateSha256=review['candidateSha256'])
        with tempfile.TemporaryDirectory() as temporary:
            actual = inspect(args, package, Path(temporary), api)
        require(encode(actual) == encode(review['static']), 'StaticReportChanged')
        event('publication.candidate_verified', packageId=package['id'], version=package['manifest']['version'])
    return records, locks, old_locks


def label_receipts(args, api, snapshot, records, old_locks, trigger):
    receipts = files(args.root, 'label-approvals'); old = files(args.root, 'approval-locks')
    approved = {r['candidateSha256']: (c, r, s) for c, r, s in records}
    published = {strict_json(raw)['candidateSha256'] for raw in old_locks.values()}
    locks = {}; pending = []; seen = set()
    for name, raw in sorted(receipts.items()):
        review = strict_json(raw); label_admission.validate_record(review)
        require(name == label_admission.receipt_path(review), 'ApprovalRecordMismatch')
        require(review['candidateSha256'] in approved, 'OrphanApprovalRecord')
        candidate, original, scope = approved[review['candidateSha256']]
        record_bundle(encode(candidate), raw, encode(scope))
        require(review['static'] == original['static'], 'StaticReportChanged')
        if review['approval']['reuseExisting']:
            require(review['candidateSha256'] in published, 'AcceptedVersionChanged')
            label_admission.proof(api, candidate, review, scope, snapshot)
        else:
            require(review == original, 'ApprovalRecordMismatch')
        run = review['approval']['runId']; seen.add(run)
        lock = 'approval-locks/' + run + '.json'
        locks[lock] = encode(dict(schemaVersion=1, runId=run, candidateSha256=review['candidateSha256'],
                                  reviewSha256=digest(raw)))
        if lock not in old:
            label_admission.current_approval(api, review); pending.append(review)
    continuity(old, locks)
    require(all(label_admission.receipt_path(r) in receipts for _, r, _ in records
                if r['approval'].get('workflow') == label_admission.WORKFLOW), 'ApprovalRecordMissing')
    require(trigger is None or trigger in seen, 'PublicationTriggerRejected')
    return locks, old, pending


def release_asset(api, snapshot, raw):
    tag = 'catalog-v3-' + snapshot
    releases = api.json(PREFIX + '/releases?per_page=100')
    found = [r for r in releases if r.get('tag_name') == tag]
    require(len(found) <= 1, 'PublicationReleaseConflict')
    release = found[0] if found else api.json(PREFIX + '/releases', 'POST', dict(tag_name=tag,
        target_commitish=snapshot, name=tag, draft=True, prerelease=False,
        body='Controlled catalog v3. Input commit: ' + snapshot + '. Catalog SHA-256: ' + digest(raw)))
    require(release.get('prerelease') is False and (not release.get('draft') or release.get('target_commitish') == snapshot), 'PublicationReleaseConflict')
    assets = release.get('assets', [])
    require(len(assets) <= 1 and all(a.get('name') == 'catalog.json' for a in assets), 'PublicationAssetConflict')
    if not assets:
        require(release.get('draft') is True, 'PublicationAssetMissing')
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / 'catalog.json'; path.write_bytes(raw)
            result = subprocess.run(['gh', 'release', 'upload', tag, str(path), '--repo', INDEX], capture_output=True, timeout=120)
            require(result.returncode == 0, 'PublicationUploadFailed')
        release = api.json(PREFIX + '/releases/' + str(release['id'])); assets = release.get('assets', [])
    require(len(assets) == 1 and assets[0].get('name') == 'catalog.json' and assets[0].get('state') == 'uploaded' and
            assets[0].get('size') == len(raw) and assets[0].get('digest') in (None, 'sha256:' + digest(raw)), 'PublicationAssetConflict')
    artifact = dict(repository=INDEX, repositoryId=REPOSITORY_ID, ownerId=OWNER_ID, sourceCommit=snapshot,
                    tag=tag, releaseId=str(release['id']), assetId=str(assets[0]['id']), assetName='catalog.json',
                    sha256=digest(raw), sizeBytes=len(raw))
    with tempfile.TemporaryDirectory() as temporary:
        payload = Path(temporary) / 'catalog.json'; api.download(artifact, payload)
        require(payload.read_bytes() == raw, 'PublicationAssetMismatch')
    event('publication.release_verified', snapshotId=snapshot, releaseId=artifact['releaseId'], assetId=artifact['assetId'], sha256=digest(raw))
    if release['draft']:
        api.json(PREFIX + '/releases/' + artifact['releaseId'], 'PATCH', dict(draft=False))
    api.verify_origin(artifact)
    return artifact


def envelopes(snapshot, raw, artifact):
    shared = dict(schemaVersion=1, sourceId=SOURCE, snapshotId=snapshot, catalogSchemaVersion=3,
                  catalogSha256=digest(raw), catalogSizeBytes=len(raw))
    published = encode(dict(shared, **{key: artifact[key] for key in ('repository', 'repositoryId', 'ownerId', 'releaseId', 'assetId', 'assetName')}))
    stable = encode(dict(shared, publishedSha256=digest(published), publishedSizeBytes=len(published)))
    return published, stable


def completed_retry(api, snapshot, current):
    """A lost success response may be retried with the original workflow SHA."""
    pointer_commit = api.json(PREFIX + '/git/commits/' + current)
    require([p.get('sha') for p in pointer_commit.get('parents', [])] == [snapshot] and
            pointer_commit.get('message') == 'Publish catalog v3 ' + snapshot, 'TrustedHeadChanged')
    def read(name):
        item = api.json(PREFIX + '/contents/' + name + '?ref=' + current)
        require(item.get('type') == 'file' and item.get('encoding') == 'base64', 'PublicationRetryRejected')
        return base64.b64decode(item['content'], validate=False)
    stable_raw = read('stable.json'); stable = strict_json(stable_raw)
    require(stable.get('sourceId') == SOURCE and stable.get('snapshotId') == snapshot and
            stable.get('catalogSchemaVersion') == 3, 'PublicationRetryRejected')
    published_raw = read('published/' + snapshot + '.json'); published = strict_json(published_raw)
    require(digest(published_raw) == stable.get('publishedSha256') and len(published_raw) == stable.get('publishedSizeBytes') and
            all(published.get(key) == stable.get(key) for key in ('schemaVersion', 'sourceId', 'snapshotId',
                'catalogSchemaVersion', 'catalogSha256', 'catalogSizeBytes')) and
            published.get('repository') == INDEX and published.get('repositoryId') == REPOSITORY_ID and
            published.get('ownerId') == OWNER_ID, 'PublicationRetryRejected')
    artifact = dict(repository=INDEX, repositoryId=REPOSITORY_ID, ownerId=OWNER_ID, sourceCommit=snapshot,
                    tag='catalog-v3-' + snapshot, releaseId=published['releaseId'], assetId=published['assetId'],
                    assetName='catalog.json', sha256=stable['catalogSha256'], sizeBytes=stable['catalogSizeBytes'])
    api.verify_origin(artifact)
    with tempfile.TemporaryDirectory() as temporary:
        path = Path(temporary) / 'catalog.json'; api.download(artifact, path)
        catalog = strict_json(path.read_bytes())
        require(catalog.get('snapshotId') == snapshot and catalog.get('sourceId') == SOURCE and
                catalog.get('schemaVersion') == 3, 'PublicationRetryRejected')
    require(index(api) == current, 'PublicationHeadChanged')
    event('publication.already_complete', snapshotId=snapshot, commit=current)


def publish(args, api):
    require(os.environ.get('GITHUB_REPOSITORY') == INDEX and os.environ.get('GITHUB_REF') == 'refs/heads/main' and
            os.environ.get('GITHUB_EVENT_NAME') in ('workflow_dispatch', 'workflow_run'), 'PublicationContextRejected')
    source_event = os.environ.get('GITHUB_EVENT_NAME') == 'workflow_run' and label_admission.payload().get('workflow_run', {}).get('display_title') == source_updates.TITLE
    source_trigger = source_updates.upstream(api) if source_event else None
    if source_event and source_trigger is None:
        event('publication.no_source_updates'); return
    trigger = label_admission.upstream(api) if os.environ.get('GITHUB_EVENT_NAME') == 'workflow_run' and not source_event else None
    snapshot = os.environ.get('GITHUB_SHA', '')
    require(re.fullmatch(r'[0-9a-f]{40}', snapshot), 'TrustedHeadChanged')
    event('publication.started', snapshotId=snapshot, checkOnly=args.check_only)
    if trigger is None and not source_event:
        maintainer(api, os.environ.get('GITHUB_TRIGGERING_ACTOR'), None)
    current = index(api)
    if current != snapshot:
        completed_retry(api, snapshot, current); return
    source = strict_json((args.root / 'source.json').read_bytes())
    require(source == dict(schemaVersion=1, sourceId=SOURCE, repository=INDEX), 'IndexIdentityMismatch')
    records, locks, old_locks = collect(args, api, snapshot)
    proof_locks = {proof_path(r): encode(proof_record(c, r, s)) for c, r, s in records}
    old_proof_locks = files(args.root, 'approval-proof-locks'); continuity(old_proof_locks, proof_locks)
    approval_locks, old_approval_locks, pending = label_receipts(args, api, snapshot, records, old_locks, trigger)
    receipts = files(args.root, 'source-update-approvals'); old_update_locks = files(args.root, 'source-update-locks'); update_locks = {}
    for name, raw in receipts.items():
        review = strict_json(raw); run = review['approval']['runId']
        require(review['approval']['workflow'] == source_updates.WORKFLOW and name == source_updates.receipt_path(review) and
                any(review == r for _, r, _ in records), 'ApprovalRecordMismatch')
        update_locks['source-update-locks/' + run + '.json'] = encode(dict(schemaVersion=1, runId=run, candidateSha256=review['candidateSha256'], reviewSha256=digest(raw)))
    require(all(source_updates.receipt_path(r) in receipts for _, r, _ in records if r['approval']['workflow'] == source_updates.WORKFLOW), 'ApprovalRecordMissing')
    require(source_trigger is None or 'source-update-approvals/' + source_trigger + '.json' in receipts, 'PublicationTriggerRejected')
    continuity(old_update_locks, update_locks)
    visible = player_records(args.root, records, old_locks)
    raw = encode(dict(schemaVersion=3, sourceId=SOURCE, snapshotId=snapshot,
                      packages=[c['package'] for c, _, _ in sorted(visible, key=lambda r: (r[0]['package']['id'], r[0]['package']['manifest']['version']))]))
    require(len(raw) <= 2 * 1024 * 1024, 'DocumentLimit')
    with tempfile.TemporaryDirectory() as temporary:
        path = Path(temporary) / 'catalog.json'; path.write_bytes(raw)
        closure_args = ['publication', SOURCE, str(path)]
        host_profile = args.root / 'host-module-profiles.json'
        if host_profile.exists():
            require(not host_profile.is_symlink() and host_profile.is_file(), 'HostProfilePathRejected')
            require(host_profile.read_bytes() == label_admission.read_at(api, 'host-module-profiles.json', snapshot), 'HostProfileChanged')
            closure_args.append(str(host_profile))
        validator(args, closure_args)
    event('publication.catalog_verified', snapshotId=snapshot, sha256=digest(raw), packages=len(visible), bytes=len(raw))
    if args.check_only:
        event('publication.check_complete', snapshotId=snapshot); return
    artifact = release_asset(api, snapshot, raw)
    published, stable = envelopes(snapshot, raw, artifact)
    published_path = 'published/' + snapshot + '.json'
    existing = args.root / published_path
    require(not existing.exists() or existing.read_bytes() == published, 'PublishedSnapshotChanged')
    changes = {published_path: published, 'stable.json': stable}
    changes.update({path: data for path, data in locks.items() if path not in old_locks})
    changes.update({path: data for path, data in approval_locks.items() if path not in old_approval_locks})
    changes.update({path: data for path, data in update_locks.items() if path not in old_update_locks})
    changes.update({path: data for path, data in proof_locks.items() if path not in old_proof_locks})
    for candidate, review, scope in records:
        if review['candidateSha256'] not in {strict_json(v)['candidateSha256'] for v in old_locks.values()}:
            api.verify_origin(candidate['package']['artifact'])
            current = api.json(PREFIX + '/issues/' + str(review['issueNumber']))
            require('pull_request' not in current and
                    digest((current.get('body') or '').encode()) == review['issueBodySha256'], 'SubmissionChanged')
    for review in pending:
        label_admission.current_approval(api, review)
    sha = commit(api, snapshot, changes, 'Publish catalog v3 ' + snapshot)
    # The pointer, description and new locks become visible in one Git commit.
    # Reject stale input; force=false prevents two siblings from replacing each other.
    require(index(api) == snapshot, 'PublicationHeadChanged')
    api.json(PREFIX + '/git/refs/heads/main', 'PATCH', dict(sha=sha, force=False))
    event('publication.stable_committed', snapshotId=snapshot, commit=sha, sha256=digest(raw))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=Path('.'))
    parser.add_argument('--validator', type=Path, required=True)
    parser.add_argument('--check-only', action='store_true')
    args = parser.parse_args()
    try:
        publish(args, GitHub(max_calls=512, timeout=1500))
    except (Rejected, OSError, KeyError, TypeError, ValueError, subprocess.TimeoutExpired) as error:
        event('publication.rejected', reason=str(error) if isinstance(error, Rejected) else 'InvalidPublicationData')
        raise SystemExit(1)


if __name__ == '__main__':
    main()
