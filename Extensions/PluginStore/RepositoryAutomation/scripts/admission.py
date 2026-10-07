#!/usr/bin/env python3
"""A2 exact-version approval records and metadata-only PRs. No stable publication."""
import argparse
import os
from pathlib import Path
import re
import subprocess
import tempfile
from datetime import datetime, timezone

from bot import INDEX, SOURCE, GitHub, Rejected, require, strict_json, encode, digest, candidate_body, inspect, is_workshop, revision, static_matches, inspection_notice

REPOSITORY_ID = '1402564805'
OWNER_ID = '64630568'
PREFIX = '/repos/' + INDEX
WORKFLOW = '.github/workflows/plugin-admission.yml'
sequence = 0


def event(title, **fields):
    global sequence
    sequence += 1
    print(encode(dict(schemaVersion=1, component='index-bot', event=title, sequence=sequence,
                     time=datetime.now(timezone.utc).isoformat(), runId=os.environ.get('GITHUB_RUN_ID'), **fields)).decode(), end='', flush=True)


def maintainer(api, login, identity=None):
    require(isinstance(login, str) and re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9-]{0,38}', login), 'InvalidReviewer')
    permission = api.json(PREFIX + '/collaborators/' + login + '/permission')
    require(permission.get('role_name') in ('admin', 'maintain') and
            permission.get('user', {}).get('type') == 'User' and
            permission['user'].get('login') == login and
            (identity is None or str(permission['user'].get('id')) == identity), 'ReviewerUnauthorized')
    return str(permission['user']['id'])


def index(api):
    repo = api.json(PREFIX)
    require(repo.get('full_name') == INDEX and str(repo.get('id')) == REPOSITORY_ID and
            str(repo.get('owner', {}).get('id')) == OWNER_ID and repo.get('default_branch') == 'main' and
            repo.get('private') is False, 'IndexIdentityMismatch')
    return api.json(PREFIX + '/git/ref/heads/main')['object']['sha']


def policy(package):
    if is_workshop(package):
        return dict(schemaVersion=1, packageId=package['id'], mode='manual-only',
                    channel='steam-workshop', management='rimworld-mod',
                    origin=dict(workshopId=package['workshopId'], rimWorldPackageId=package['rimWorldPackageId']))
    artifact = package['artifact']; manifest = package['manifest']
    return dict(schemaVersion=1, packageId=package['id'], mode='manual-only',
                origin={key: artifact[key] for key in ('repository', 'repositoryId', 'ownerId')},
                channel=package['channel'], management=package['management'],
                assemblyNames=sorted(a['name'] for a in manifest['assemblies']),
                moduleIds=sorted(m['id'] for m in manifest['modules']),
                dependencyIds=sorted(d['packageId'] for d in manifest['dependencies']))


def paths(package, fingerprint):
    key = digest(package['id'].encode('utf-8'))
    return ('packages/' + key + '/' + fingerprint + '.json',
            'reviews/' + key + '/' + fingerprint + '.json',
            'policies/' + key + '/' + fingerprint + '.json')


def prepare(args, api):
    require(os.environ.get('GITHUB_REPOSITORY') == INDEX and os.environ.get('GITHUB_REF') == 'refs/heads/main' and
            os.environ.get('GITHUB_EVENT_NAME') == 'workflow_dispatch' and
            os.environ.get('GITHUB_RUN_ATTEMPT') == '1' and
            os.environ.get('GITHUB_TRIGGERING_ACTOR') == os.environ.get('GITHUB_ACTOR'), 'ApprovalContextRejected')
    require(re.fullmatch(r'[0-9a-f]{64}', args.candidate_sha256) and args.issue_number > 0, 'InvalidApprovalInput')
    event('admission.started', issueNumber=args.issue_number, candidateSha256=args.candidate_sha256)
    head = index(api)
    require(head == os.environ.get('GITHUB_SHA'), 'TrustedHeadChanged')
    actor = os.environ['GITHUB_ACTOR']; actor_id = maintainer(api, actor, os.environ.get('GITHUB_ACTOR_ID'))
    issue_path = PREFIX + '/issues/' + str(args.issue_number)
    issue = api.json(issue_path)
    require(issue.get('state') == 'open' and 'pull_request' not in issue, 'InvalidSubmissionIssue')
    body = issue.get('body') or ''; package = candidate_body(body)
    candidate = dict(schemaVersion=1, package=package)
    require(digest(encode(candidate)) == args.candidate_sha256, 'CandidateFingerprintMismatch')
    event('admission.candidate_started', issueNumber=args.issue_number, packageId=package['id'], version=revision(package))
    with tempfile.TemporaryDirectory() as temporary:
        static = inspect(args, package, Path(temporary), api)
    current = api.json(issue_path)
    require(current.get('state') == 'open' and 'pull_request' not in current and
            (current.get('body') or '') == body, 'SubmissionChanged')
    scope = policy(package)
    record = dict(schemaVersion=1, sourceId=SOURCE, candidateSha256=args.candidate_sha256,
                  policySha256=digest(encode(scope)), static=static, staticSha256=digest(encode(static)),
                  issueNumber=args.issue_number, issueBodySha256=digest(body.encode('utf-8')),
                  issueUpdatedAt=current['updated_at'], approvedAt=datetime.now(timezone.utc).isoformat(),
                  approval=dict(actor=actor, actorId=actor_id, runId=os.environ['GITHUB_RUN_ID'],
                                trustedCommit=head, workflow=WORKFLOW, attempt=1))
    args.output.mkdir(parents=True, exist_ok=False)
    for name, value in (('candidate.json', candidate), ('review.json', record), ('policy.json', scope)):
        (args.output / name).write_bytes(encode(value))
    event('admission.prepared', candidateSha256=args.candidate_sha256, issueNumber=args.issue_number,
          packageId=package['id'], version=revision(package))


def read_bundle(root):
    values = [strict_json((root / name).read_bytes()) for name in ('candidate.json', 'review.json', 'policy.json')]
    candidate, review, scope = values; package = candidate['package']
    require(set(candidate) == {'schemaVersion', 'package'} and type(candidate['schemaVersion']) is int and
            candidate['schemaVersion'] == 1 and set(review) == {'schemaVersion', 'sourceId', 'candidateSha256',
            'policySha256', 'static', 'staticSha256', 'issueNumber', 'issueBodySha256', 'issueUpdatedAt',
            'approvedAt', 'approval'} and type(review['schemaVersion']) is int and review['schemaVersion'] == 1 and
            type(review['issueNumber']) is int and review['issueNumber'] > 0 and
            set(review['approval']) in (
                {'actor', 'actorId', 'runId', 'trustedCommit', 'workflow', 'attempt'},
                {'actor', 'actorId', 'runId', 'trustedCommit', 'workflow', 'attempt',
                 'method', 'label', 'labelEventId', 'labelCreatedAt', 'reuseExisting'},
                {'actor', 'actorId', 'runId', 'trustedCommit', 'workflow', 'attempt',
                 'method', 'label', 'labelEventId', 'labelCreatedAt', 'reuseExisting', 'includeUpdatePolicy'},
                {'actor', 'actorId', 'runId', 'trustedCommit', 'workflow', 'attempt',
                 'method', 'baseCandidateSha256', 'updatePolicySha256'}), 'ApprovalRecordMismatch')
    fingerprint = digest(encode(candidate))
    require(not is_workshop(package) or (review['approval']['workflow'] in (WORKFLOW, '.github/workflows/plugin-label-admission.yml') and
            'includeUpdatePolicy' not in review['approval']), 'WorkshopPolicyRejected')
    require((review['approval']['workflow'] == WORKFLOW and len(review['approval']) == 6) or
            (review['approval']['workflow'] == '.github/workflows/plugin-label-admission.yml' and
             len(review['approval']) in (11, 12)) or
            (review['approval']['workflow'] == '.github/workflows/plugin-source-updates.yml' and
             len(review['approval']) == 9 and review['approval']['method'] == 'approved-source' and
             re.fullmatch(r'[0-9a-f]{64}', review['approval']['baseCandidateSha256']) and
             re.fullmatch(r'[0-9a-f]{64}', review['approval']['updatePolicySha256'])), 'ApprovalRecordMismatch')
    require(review['candidateSha256'] == fingerprint and review['sourceId'] == SOURCE and
            review['policySha256'] == digest(encode(scope)) and scope == policy(package) and
            review['staticSha256'] == digest(encode(review['static'])) and
            static_matches(package, review['static']), 'ApprovalRecordMismatch')
    return values


def commit(api, parent, files, message):
    base_tree = api.json(PREFIX + '/git/commits/' + parent)['tree']['sha']
    tree = api.json(PREFIX + '/git/trees', 'POST', dict(base_tree=base_tree, tree=[
        dict(path=path, mode='100644', type='blob', content=raw.decode('utf-8')) for path, raw in files.items()]))
    return api.json(PREFIX + '/git/commits', 'POST', dict(message=message, tree=tree['sha'], parents=[parent]))['sha']


def open_pr(args, api):
    candidate, review, scope = read_bundle(args.input)
    require(review['approval']['runId'] == os.environ.get('GITHUB_RUN_ID') and
            review['approval']['trustedCommit'] == os.environ.get('GITHUB_SHA'), 'ApprovalContextRejected')
    parent = index(api); require(parent == review['approval']['trustedCommit'], 'TrustedHeadChanged')
    maintainer(api, review['approval']['actor'], review['approval']['actorId'])
    current = api.json(PREFIX + '/issues/' + str(review['issueNumber']))
    require(current.get('state') == 'open' and 'pull_request' not in current and
            digest((current.get('body') or '').encode()) == review['issueBodySha256'], 'SubmissionChanged')
    package = candidate['package']; fingerprint = review['candidateSha256']
    names = paths(package, fingerprint)
    tree = api.json(PREFIX + '/git/trees/' + parent + '?recursive=1')
    require(tree.get('truncated') is False and all(entry.get('path') not in names for entry in tree['tree']), 'CandidateAlreadyRecorded')
    sha = commit(api, parent, dict(zip(names, map(encode, (candidate, review, scope)))),
                 'Approve fixed plugin candidate ' + fingerprint)
    branch = 'codex/admission-' + review['approval']['runId']
    api.json(PREFIX + '/git/refs', 'POST', dict(ref='refs/heads/' + branch, sha=sha))
    # Exactly three metadata files. Do not check out candidate code or change workflows.
    pull = api.json(PREFIX + '/pulls', 'POST', dict(head=branch, base='main',
        title='Admit ' + package['id'] + ' ' + revision(package),
        body='Exact candidate SHA-256: `' + fingerprint + '`\n\nIssue #' + str(review['issueNumber']) +
             '. ' + inspection_notice(package) + '\n\n'
             'Review the candidate, permanent static report and manual-only identity policy. '
             'Merge approves this version for the separately invoked controlled publisher. '
             'No stable pointer or automatic update scope changes in this PR.'))
    event('admission.pr_created', number=pull['number'], url=pull['html_url'], head=sha, candidateSha256=fingerprint)
    Path(os.environ['GITHUB_STEP_SUMMARY']).write_text('Admission PR: ' + pull['html_url'] + '\n', encoding='utf-8')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest='command', required=True)
    p = commands.add_parser('prepare'); p.add_argument('--issue-number', type=int, required=True)
    p.add_argument('--candidate-sha256', required=True); p.add_argument('--validator', type=Path, required=True)
    p.add_argument('--output', type=Path, required=True)
    p = commands.add_parser('pr'); p.add_argument('--input', type=Path, required=True)
    args = parser.parse_args()
    try:
        (prepare if args.command == 'prepare' else open_pr)(args, GitHub(max_calls=48, timeout=480))
    except (Rejected, OSError, KeyError, TypeError, ValueError, subprocess.TimeoutExpired) as error:
        event('admission.rejected', reason=str(error) if isinstance(error, Rejected) else 'InvalidApprovalData')
        raise SystemExit(1)


if __name__ == '__main__':
    main()
