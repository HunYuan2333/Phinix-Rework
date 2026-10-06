#!/usr/bin/env python3
"""One human label approval; trusted static checks, evidence PR and automatic merge."""
import argparse
import base64
import os
from pathlib import Path
import re
import subprocess
import tempfile
from datetime import datetime, timezone

from bot import INDEX, SOURCE, ERROR_LABEL, GitHub, Rejected, require, strict_json, encode, digest, candidate_body, inspect
from admission import PREFIX, REPOSITORY_ID, OWNER_ID, event, maintainer, index, policy, paths, read_bundle, commit

LABEL = 'plugin-approved'
WORKFLOW = '.github/workflows/plugin-label-admission.yml'
BOT_ID = '41898282'


def payload():
    path = Path(os.environ['GITHUB_EVENT_PATH'])
    require(path.stat().st_size <= 2 * 1024 * 1024, 'EventLimit')
    return strict_json(path.read_bytes())


def label_event(api, number, event_id=None):
    """Bounded per-issue history binds event identity to this issue, not another issue."""
    relevant = []
    for page in range(1, 5):
        items = api.json(PREFIX + '/issues/' + str(number) + '/events?per_page=100&page=' + str(page))
        require(type(items) is list, 'LabelHistoryRejected')
        relevant.extend(item for item in items if item.get('event') in ('labeled', 'unlabeled') and
                        item.get('label', {}).get('name') == LABEL)
        if len(items) < 100:
            break
    else:
        raise Rejected('LabelHistoryLimit')
    if event_id is None:
        require(relevant, 'ApprovalLabelMissing')
        # API returns chronological history. Numeric IDs also make ordering explicit.
        return max(relevant, key=lambda item: int(item['id']))
    matches = [item for item in relevant if str(item.get('id')) == event_id]
    require(len(matches) == 1, 'ApprovalLabelEventMissing')
    return matches[0]


def event_matches(item, approval):
    require(item.get('event') == 'labeled' and item.get('label', {}).get('name') == LABEL and
            str(item.get('id')) == approval['labelEventId'] and
            item.get('created_at') == approval['labelCreatedAt'] and
            item.get('actor', {}).get('type') == 'User' and
            item.get('actor', {}).get('login') == approval['actor'] and
            str(item.get('actor', {}).get('id')) == approval['actorId'], 'ApprovalLabelEventMismatch')


def current_approval(api, review):
    current = api.json(PREFIX + '/issues/' + str(review['issueNumber']))
    require(current.get('state') == 'open' and 'pull_request' not in current and
            digest((current.get('body') or '').encode()) == review['issueBodySha256'], 'SubmissionChanged')
    require(any(label.get('name') == LABEL for label in current.get('labels', [])), 'ApprovalLabelMissing')
    event_matches(label_event(api, review['issueNumber']), review['approval'])


def context(api):
    require(os.environ.get('GITHUB_REPOSITORY') == INDEX and os.environ.get('GITHUB_REF') == 'refs/heads/main' and
            os.environ.get('GITHUB_EVENT_NAME') == 'issues' and os.environ.get('GITHUB_RUN_ATTEMPT') == '1' and
            os.environ.get('GITHUB_TRIGGERING_ACTOR') == os.environ.get('GITHUB_ACTOR'), 'ApprovalContextRejected')
    data = payload(); issue = data.get('issue', {}); sender = data.get('sender', {})
    require(data.get('action') == 'labeled' and data.get('label', {}).get('name') == LABEL and
            str(data.get('repository', {}).get('id')) == REPOSITORY_ID and
            data.get('repository', {}).get('full_name') == INDEX and
            str(data.get('repository', {}).get('owner', {}).get('id')) == OWNER_ID and
            type(issue.get('number')) is int and issue['number'] > 0 and
            issue.get('state') == 'open' and 'pull_request' not in issue and
            sender.get('type') == 'User' and sender.get('login') == os.environ.get('GITHUB_ACTOR') and
            str(sender.get('id')) == os.environ.get('GITHUB_ACTOR_ID'), 'ApprovalContextRejected')
    actor_id = maintainer(api, sender['login'], str(sender['id']))
    approval = dict(actor=sender['login'], actorId=actor_id, runId=os.environ['GITHUB_RUN_ID'],
                    trustedCommit=os.environ['GITHUB_SHA'], workflow=WORKFLOW, attempt=1,
                    method='issue-label', label=LABEL, reuseExisting=False)
    item = label_event(api, issue['number'])
    approval.update(labelEventId=str(item['id']), labelCreatedAt=item['created_at'])
    require(item['created_at'] == issue.get('updated_at'), 'ApprovalEventStale')
    event_matches(item, approval)
    return issue, approval


def ancestry(api, parent, trusted):
    require(re.fullmatch(r'[0-9a-f]{40}', trusted), 'TrustedHeadChanged')
    require(api.json(PREFIX + '/compare/' + trusted + '...' + parent).get('status') in ('ahead', 'identical'),
            'ApprovalAncestryRejected')


def receipt_path(review):
    run = review['approval']['runId']
    require(re.fullmatch(r'[1-9][0-9]*', run), 'ApprovalProofRejected')
    return 'label-approvals/' + run + '.json'


def read_at(api, name, ref):
    item = api.json(PREFIX + '/contents/' + name + '?ref=' + ref)
    require(item.get('type') == 'file' and item.get('encoding') == 'base64', 'ApprovalRecordMismatch')
    raw = base64.b64decode(item['content'], validate=False)
    require(len(raw) <= 2 * 1024 * 1024, 'DocumentLimit')
    return raw


def reuse(api, parent, package, fingerprint, candidate, scope):
    tree = api.json(PREFIX + '/git/trees/' + parent + '?recursive=1')
    require(tree.get('truncated') is False, 'IndexTreeLimit')
    names = paths(package, fingerprint); present = {entry['path'] for entry in tree['tree']}
    key = digest(package['id'].encode())
    lock = 'publication-locks/' + key + '/' + digest(package['manifest']['version'].encode()) + '.json'
    if lock in present:
        value = strict_json(read_at(api, lock, parent))
        require(value == dict(schemaVersion=1, packageId=package['id'], version=package['manifest']['version'],
            candidateSha256=fingerprint, artifactSha256=package['artifact']['sha256']), 'AcceptedVersionChanged')
        require(all(name in present for name in names) and read_at(api, names[0], parent) == encode(candidate) and
                read_at(api, names[2], parent) == encode(scope), 'AcceptedVersionChanged')
        return True
    require(all(name not in present for name in names), 'CandidateAlreadyRecorded')
    # Also reject conflicting pending records for the same ID/version, before opening a PR.
    pending = [name for name in present if name.startswith('packages/' + key + '/') and name.endswith('.json')]
    require(len(pending) <= 8, 'PublicationRecordLimit')
    for name in pending:
        other = strict_json(read_at(api, name, parent))['package']
        require(other['manifest']['version'] != package['manifest']['version'], 'AcceptedVersionConflict')
    return False


def prepare(args, api):
    issue, approval = context(api)
    parent = index(api); ancestry(api, parent, approval['trustedCommit'])
    body = issue.get('body') or ''; candidate = dict(schemaVersion=1, package=candidate_body(body))
    package = candidate['package']; fingerprint = digest(encode(candidate)); scope = policy(package)
    review = dict(schemaVersion=1, sourceId=SOURCE, candidateSha256=fingerprint, policySha256=digest(encode(scope)),
        issueNumber=issue['number'], issueBodySha256=digest(body.encode()), issueUpdatedAt=issue['updated_at'],
        approvedAt=datetime.now(timezone.utc).isoformat(), approval=approval)
    current_approval(api, review)
    event('label_admission.started', issueNumber=issue['number'], candidateSha256=fingerprint,
          labelEventId=approval['labelEventId'])
    with tempfile.TemporaryDirectory() as temporary:
        static = inspect(args, package, Path(temporary), api)
    review.update(static=static, staticSha256=digest(encode(static)))
    approval['reuseExisting'] = reuse(api, parent, package, fingerprint, candidate, scope)
    import source_updates
    update_policy = source_updates.default_policy(candidate, review)
    if update_policy is not None:
        tree = api.json(PREFIX + '/git/trees/' + parent + '?recursive=1')
        require(tree.get('truncated') is False, 'IndexTreeLimit')
        if not any(p.get('path') == source_updates.policy_path(package['id']) for p in tree['tree']):
            approval['includeUpdatePolicy'] = True
    current_approval(api, review)
    args.output.mkdir(parents=True, exist_ok=False)
    for name, value in (('candidate.json', candidate), ('review.json', review), ('policy.json', scope)):
        (args.output / name).write_bytes(encode(value))
    event('label_admission.prepared', issueNumber=issue['number'], reuseExisting=approval['reuseExisting'])


def expected_files(candidate, review, scope):
    validate_record(review)
    result = {receipt_path(review): encode(review)}
    if not review['approval']['reuseExisting']:
        result.update(dict(zip(paths(candidate['package'], review['candidateSha256']), map(encode, (candidate, review, scope)))))
    if review['approval'].get('includeUpdatePolicy'):
        import source_updates
        value = source_updates.default_policy(candidate, review)
        require(value is not None, 'UpdatePolicyRejected')
        result[source_updates.policy_path(candidate['package']['id'])] = encode(value)
    return result


def validate_record(review):
    approval = review['approval']
    require(approval.get('workflow') == WORKFLOW and approval.get('method') == 'issue-label' and
            approval.get('label') == LABEL and approval.get('attempt') == 1 and
            type(approval.get('reuseExisting')) is bool and
            re.fullmatch(r'[1-9][0-9]*', approval.get('labelEventId', '')) and
            re.fullmatch(r'[1-9][0-9]*', approval.get('runId', '')) and
            re.fullmatch(r'[0-9a-f]{40}', approval.get('trustedCommit', '')) and
            isinstance(approval.get('labelCreatedAt'), str), 'ApprovalProofRejected')
    require('includeUpdatePolicy' not in approval or approval['includeUpdatePolicy'] is True, 'ApprovalProofRejected')


def bot_identity(user):
    require(user.get('type') == 'Bot' and user.get('login') == 'github-actions[bot]' and
            str(user.get('id')) == BOT_ID, 'AdmissionBotMismatch')


def pr_content(api, pull, expected):
    require(pull.get('base', {}).get('ref') == 'main' and
            str(pull.get('base', {}).get('repo', {}).get('id')) == REPOSITORY_ID and
            str(pull.get('head', {}).get('repo', {}).get('id')) == REPOSITORY_ID, 'AdmissionPrScopeRejected')
    bot_identity(pull.get('user', {}))
    changes = api.json(PREFIX + '/pulls/' + str(pull['number']) + '/files?per_page=100')
    require(type(changes) is list and len(changes) == len(expected) and
            {item.get('filename') for item in changes} == set(expected) and
            all(item.get('status') == 'added' for item in changes), 'AdmissionPrScopeRejected')
    for name, raw in expected.items():
        require(read_at(api, name, pull['head']['sha']) == raw, 'AdmissionPrContentChanged')


def admit(args, api):
    candidate, review, scope = read_bundle(args.input); issue, approval = context(api)
    validate_record(review)
    require(review['approval']['runId'] == approval['runId'] and
            review['approval']['trustedCommit'] == approval['trustedCommit'] and
            review['approval']['labelEventId'] == approval['labelEventId'] and
            review['issueNumber'] == issue['number'] and
            review['issueBodySha256'] == digest((issue.get('body') or '').encode()), 'ApprovalContextRejected')
    parent = index(api); ancestry(api, parent, approval['trustedCommit'])
    require(reuse(api, parent, candidate['package'], review['candidateSha256'], candidate, scope) ==
            review['approval']['reuseExisting'], 'ApprovalRecordMismatch')
    expected = expected_files(candidate, review, scope)
    if approval.get('includeUpdatePolicy'):
        import source_updates
        tree = api.json(PREFIX + '/git/trees/' + parent + '?recursive=1')
        require(tree.get('truncated') is False and not any(p.get('path') == source_updates.policy_path(candidate['package']['id']) for p in tree['tree']), 'UpdatePolicyChanged')
    sha = commit(api, parent, expected, 'Approve labeled plugin candidate ' + review['candidateSha256'])
    branch = 'codex/admission-' + approval['runId']
    api.json(PREFIX + '/git/refs', 'POST', dict(ref='refs/heads/' + branch, sha=sha))
    pull = api.json(PREFIX + '/pulls', 'POST', dict(head=branch, base='main',
        title='Admit ' + candidate['package']['id'] + ' ' + candidate['package']['manifest']['version'],
        body='Maintainer approved Issue #' + str(issue['number']) + ' with `' + LABEL + '`.\n\n'
             'Candidate SHA-256: `' + review['candidateSha256'] + '`. Label event: ' + approval['labelEventId'] +
             '. Trusted ZIP/PE checks passed without executing plugin code.\n\n'
             'This metadata-only evidence PR is merged automatically; publication rechecks the evidence.'))
    event('label_admission.pr_created', issueNumber=issue['number'], number=pull['number'], url=pull['html_url'], head=sha)
    Path(os.environ['GITHUB_STEP_SUMMARY']).write_text('Admission PR: ' + pull['html_url'] + '\n', encoding='utf-8')
    pull = api.json(PREFIX + '/pulls/' + str(pull['number']))
    require(pull['head']['sha'] == sha, 'AdmissionPrContentChanged')
    pr_content(api, pull, expected)
    current_approval(api, review); require(index(api) == parent, 'PublicationHeadChanged')
    result = api.json(PREFIX + '/pulls/' + str(pull['number']) + '/merge', 'PUT', dict(sha=sha, merge_method='squash'))
    require(result.get('merged') is True, 'AdmissionMergeFailed')
    event('label_admission.merged', issueNumber=issue['number'], number=pull['number'], commit=result['sha'])


def proof(api, candidate, review, scope, snapshot):
    validate_record(review); approval = review['approval']
    run = api.json(PREFIX + '/actions/runs/' + approval['runId'])
    require(run.get('event') == 'issues' and run.get('path') == WORKFLOW and run.get('head_branch') == 'main' and
            run.get('status') == 'completed' and run.get('conclusion') == 'success' and run.get('run_attempt') == 1 and
            run.get('head_sha') == approval['trustedCommit'] and
            run.get('display_title') == 'Label admission #' + str(review['issueNumber']) and
            run.get('actor', {}).get('type') == 'User' and run.get('actor', {}).get('login') == approval['actor'] and
            str(run.get('actor', {}).get('id')) == approval['actorId'], 'ApprovalProofRejected')
    maintainer(api, approval['actor'], approval['actorId']); ancestry(api, snapshot, approval['trustedCommit'])
    event_matches(label_event(api, review['issueNumber'], approval['labelEventId']), approval)
    pulls = api.json(PREFIX + '/pulls?state=closed&per_page=2&head=HunYuan2333:codex%2Fadmission-' + approval['runId'])
    require(type(pulls) is list and len(pulls) == 1, 'AdmissionPrMissing')
    pull = api.json(PREFIX + '/pulls/' + str(pulls[0]['number']))
    require(pull.get('merged') is True, 'AdmissionPrNotMerged')
    bot_identity(pull.get('merged_by', {}))
    pr_content(api, pull, expected_files(candidate, review, scope))


def upstream(api):
    data = payload(); run = data.get('workflow_run', {})
    require(data.get('action') == 'completed' and str(data.get('repository', {}).get('id')) == REPOSITORY_ID and
            run.get('event') == 'issues' and run.get('head_branch') == 'main' and
            run.get('conclusion') == 'success' and run.get('status') == 'completed' and run.get('run_attempt') == 1 and
            re.fullmatch(r'[1-9][0-9]*', str(run.get('id', ''))), 'PublicationTriggerRejected')
    actual = api.json(PREFIX + '/actions/runs/' + str(run['id']))
    require(actual.get('path') == WORKFLOW and str(actual.get('head_repository', {}).get('id')) == REPOSITORY_ID and
            all(actual.get(key) == run.get(key) for key in ('id', 'event', 'head_sha', 'head_branch',
                'conclusion', 'status', 'run_attempt')) and
            all(actual.get('actor', {}).get(key) == run.get('actor', {}).get(key) for key in ('id', 'login', 'type')),
            'PublicationTriggerRejected')
    maintainer(api, actual.get('actor', {}).get('login'), str(actual.get('actor', {}).get('id')))
    return str(run['id'])


def published_issue(api, number):
    """A successful job alone is not enough to close an issue: bind its published receipt."""
    run = upstream(api); parent = index(api)
    review_raw = read_at(api, 'label-approvals/' + run + '.json', parent); review = strict_json(review_raw)
    validate_record(review)
    require(review['issueNumber'] == number and review['approval']['runId'] == run, 'NotificationContextRejected')
    lock = strict_json(read_at(api, 'approval-locks/' + run + '.json', parent))
    require(lock == dict(schemaVersion=1, runId=run, candidateSha256=review['candidateSha256'],
                         reviewSha256=digest(review_raw)), 'PublicationReceiptNotLocked')
    stable = strict_json(read_at(api, 'stable.json', parent))
    require(stable.get('sourceId') == SOURCE and
            read_at(api, 'label-approvals/' + run + '.json', stable['snapshotId']) == review_raw,
            'PublicationReceiptNotLocked')
    issue = api.json(PREFIX + '/issues/' + str(number))
    require('pull_request' not in issue and digest((issue.get('body') or '').encode()) == review['issueBodySha256'],
            'SubmissionChanged')
    return issue


def feedback(api, stage, success):
    """Best effort notifications; a comment failure never changes committed approval/publication."""
    number = None
    verified_context = False
    try:
        data = payload()
        if stage == 'admission':
            if data.get('action') != 'labeled' or data.get('label', {}).get('name') != LABEL: return
            number = data['issue']['number']
        else:
            title = data.get('workflow_run', {}).get('display_title', '')
            match = re.fullmatch(r'Label admission #([1-9][0-9]*)', title)
            if not match: return
            number = int(match.group(1))
        require(type(number) is int and number > 0 and str(data.get('repository', {}).get('id')) == REPOSITORY_ID,
                'NotificationContextRejected')
        verified_context = True
        link = 'https://github.com/' + INDEX + '/actions/runs/' + os.environ['GITHUB_RUN_ID']
        if stage == 'admission' and success:
            text = '批准标签已验证，静态复核及元数据 PR 自动合入完成。接下来自动发布，无需再次审批。'
        elif stage == 'publication' and success:
            issue = published_issue(api, number)
            text = '自动复核和发布已完成，正式索引已更新。申请自动关闭，无需再次审批。 / Published successfully; this submission is automatically closed.'
        else:
            text = '自动' + ('准入' if stage == 'admission' else '发布') + '未完成；请查看运行中的拒绝原因。商店 stable 指针未由失败任务更新。'
        issue_path = PREFIX + '/issues/' + str(number)
        if not success:
            api.json(issue_path + '/labels', 'POST', dict(labels=[ERROR_LABEL]))
        api.json(issue_path + '/comments', 'POST', dict(body=text + '\n\n[运行日志 / Run log](' + link + ')'))
        if stage == 'publication' and success:
            if any(label.get('name') == ERROR_LABEL for label in issue.get('labels', [])):
                api.json(issue_path + '/labels/' + ERROR_LABEL, 'DELETE')
            latest = api.json(issue_path)
            require('pull_request' not in latest and (latest.get('body') or '') == (issue.get('body') or ''), 'SubmissionChanged')
            if latest.get('state') == 'open':
                api.json(issue_path, 'PATCH', dict(state='closed', state_reason='completed'))
            event('label_admission.issue_closed', issueNumber=number, approvalRunId=str(data['workflow_run']['id']))
    except (Rejected, OSError, KeyError, TypeError, ValueError) as error:
        reason = str(error) if isinstance(error, Rejected) and re.fullmatch(r'[A-Za-z0-9]+', str(error)) else 'NotificationUnavailable'
        event('label_admission.feedback_failed', stage=stage, reason=reason)
        if verified_context and type(number) is int and number > 0:
            try:
                api.json(PREFIX + '/issues/' + str(number) + '/labels', 'POST', dict(labels=[ERROR_LABEL]))
                api.json(PREFIX + '/issues/' + str(number) + '/comments', 'POST', dict(body=
                    '回报或关闭申请未完成 / Notification or closure incomplete. Code: `' + reason + '`.\n\n'
                    '[运行日志 / Run log](https://github.com/' + INDEX + '/actions/runs/' + os.environ['GITHUB_RUN_ID'] + ')'))
            except (Rejected, OSError, KeyError, TypeError, ValueError):
                pass


def main():
    parser = argparse.ArgumentParser(description=__doc__); commands = parser.add_subparsers(dest='command', required=True)
    command = commands.add_parser('prepare'); command.add_argument('--validator', type=Path, required=True)
    command.add_argument('--output', type=Path, required=True)
    command = commands.add_parser('admit'); command.add_argument('--input', type=Path, required=True)
    command = commands.add_parser('feedback'); command.add_argument('--stage', choices=('admission', 'publication'), required=True)
    command.add_argument('--success', choices=('true', 'false'), required=True)
    args = parser.parse_args(); api = GitHub(max_calls=96, timeout=480)
    try:
        if args.command == 'prepare': prepare(args, api)
        elif args.command == 'admit': admit(args, api)
        else: feedback(api, args.stage, args.success == 'true')
    except (Rejected, OSError, KeyError, TypeError, ValueError, subprocess.TimeoutExpired) as error:
        event('label_admission.rejected', reason=str(error) if isinstance(error, Rejected) else 'InvalidApprovalData')
        raise SystemExit(1)


if __name__ == '__main__': main()
