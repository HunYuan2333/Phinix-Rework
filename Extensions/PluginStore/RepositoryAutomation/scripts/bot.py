#!/usr/bin/env python3
"""A1 intake/static reports only. Never approves, installs or publishes candidates."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import time
import urllib.error
import urllib.parse
import urllib.request

SOURCE = 'phinix.official'
INDEX = 'HunYuan2333/Phinix-Plugin-Index'
MAX_JSON = 2 * 1024 * 1024
MAX_PACKAGE = 128 * 1024 * 1024
HEADING = '### Candidate JSON'
ERROR_LABEL = 'plugin-error'


class Rejected(Exception):
    pass


def require(condition, code):
    if not condition:
        raise Rejected(code)


def strict_json(raw):
    require(0 < len(raw) <= MAX_JSON, 'DocumentLimit')
    def fields(pairs):
        result = {}
        for key, value in pairs:
            require(key not in result, 'DuplicateField')
            result[key] = value
        return result
    try:
        return json.loads(raw.decode('utf-8'), object_pairs_hook=fields,
                          parse_constant=lambda _: (_ for _ in ()).throw(Rejected('InvalidJson')))
    except (ValueError, UnicodeError, RecursionError):
        raise Rejected('InvalidJson') from None


def encode(value):
    return (json.dumps(value, sort_keys=True, ensure_ascii=False, separators=(',', ':')) + '\n').encode('utf-8')


def digest(raw):
    return hashlib.sha256(raw).hexdigest()


def candidate_body(body):
    require(isinstance(body, str) and len(body.encode('utf-8')) <= 65536, 'IssueBodyLimit')
    require(body.count(HEADING) == 1, 'MissingCandidateJson')
    section = body.split(HEADING, 1)[1].strip()
    section = re.split(r'\n### ', section, maxsplit=1)[0].strip()
    if section.startswith('```'):
        lines = section.splitlines()
        require(lines[0] in ('```json', '```') and lines[-1] == '```', 'InvalidCandidateFence')
        section = '\n'.join(lines[1:-1])
    return submission(strict_json(section.encode('utf-8')))


def is_workshop(package):
    return package.get('channel') == 'steam-workshop'


def revision(package):
    return 'workshop' if is_workshop(package) else package['manifest']['version']


def workshop_static(package):
    return dict(schemaVersion=1, packageId=package['id'], channel='steam-workshop',
                workshopId=package['workshopId'], rimWorldPackageId=package['rimWorldPackageId'],
                listingSha256=digest(encode(package)), scope='listing-metadata-only', files=0)


def static_matches(package, report):
    if is_workshop(package):
        return report == workshop_static(package)
    return (report.get('packageId') == package['id'] and report.get('version') == revision(package) and
            report.get('sha256') == package['artifact']['sha256'])


def publication_lock(package, fingerprint):
    key = digest(package['id'].encode())
    if is_workshop(package):
        # Approved metadata revisions are immutable; Steam owns content versions.
        slot = 'workshop-' + fingerprint
        value = dict(schemaVersion=1, packageId=package['id'], channel='steam-workshop',
                     workshopId=package['workshopId'], candidateSha256=fingerprint,
                     listingSha256=digest(encode(package)))
    else:
        slot = digest(revision(package).encode())
        value = dict(schemaVersion=1, packageId=package['id'], version=revision(package),
                     candidateSha256=fingerprint, artifactSha256=package['artifact']['sha256'])
    return 'publication-locks/' + key + '/' + slot + '.json', value


def inspection_notice(package):
    return ('Workshop listing metadata checked only; no mod code, files or future Steam updates were inspected. '
            '仅校验工坊收录元数据，未检查 Mod 代码、文件或后续 Steam 更新。' if is_workshop(package) else
            'Trusted ZIP/PE checks passed without executing plugin code.')


def workshop_submission(package):
    required = {'id', 'name', 'author', 'license', 'summary', 'tags', 'state', 'channel', 'management',
                'rimWorldPackageId', 'workshopId', 'rimWorldVersions'}
    require(set(package) == required, 'WorkshopFieldsRejected')
    require(package['management'] == 'rimworld-mod' and package['state'] == 'active', 'UnsupportedRoute')
    for key in ('id', 'rimWorldPackageId'):
        require(type(package[key]) is str and len(package[key]) <= 128 and
                re.fullmatch(r'[a-z0-9]+(?:[._-][a-z0-9]+)*', package[key]), 'InvalidIdentifier')
    for key, maximum in (('name', 160), ('author', 160), ('license', 128), ('summary', 1024)):
        require(type(package[key]) is str and 0 < len(package[key]) <= maximum and package[key].strip(), 'InvalidText')
    require(type(package['workshopId']) is str and re.fullmatch(r'[1-9][0-9]{0,19}', package['workshopId']) and
            int(package['workshopId']) <= 2**64 - 1, 'InvalidWorkshopId')
    tags = package['tags']; versions = package['rimWorldVersions']
    require(type(tags) is list and len(tags) <= 8 and all(type(t) is str and len(t) <= 32 and
            re.fullmatch(r'[a-z0-9]+(?:[._-][a-z0-9]+)*', t) for t in tags) and len(set(tags)) == len(tags), 'InvalidTags')
    require(type(versions) is list and 0 < len(versions) <= 16 and all(type(v) is str and len(v) <= 32 and
            re.fullmatch(r'(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)', v) for v in versions) and
            len(set(versions)) == len(versions), 'InvalidCompatibility')
    return package


def submission(value):
    require(type(value) is dict and set(value) == {'schemaVersion', 'package'} and
            type(value['schemaVersion']) is int and value['schemaVersion'] == 1, 'SubmissionEnvelope')
    package = value['package']
    require(type(package) is dict, 'InvalidPackage')
    if is_workshop(package):
        return workshop_submission(package)
    require(package.get('channel') == 'github-release' and package.get('management') == 'phinix-dll' and
            package.get('state') == 'active', 'UnsupportedRoute')
    artifact = package.get('artifact')
    require(type(artifact) is dict, 'InvalidArtifact')
    require(isinstance(artifact.get('repository'), str) and re.fullmatch(
        r'[A-Za-z0-9][A-Za-z0-9-]{0,38}/[A-Za-z0-9][A-Za-z0-9._-]{0,99}', artifact['repository']), 'InvalidRepository')
    require(not artifact['repository'].lower().endswith('.git'), 'InvalidRepository')
    for key in ('repositoryId', 'ownerId', 'releaseId', 'assetId'):
        require(isinstance(artifact.get(key), str) and re.fullmatch(r'[1-9][0-9]{0,19}', artifact[key]) and
                int(artifact[key]) <= 2**64 - 1, 'InvalidOriginId')
    require(isinstance(artifact.get('sourceCommit'), str) and
            re.fullmatch(r'[0-9a-f]{40}', artifact['sourceCommit']), 'InvalidCommit')
    require(type(artifact.get('sizeBytes')) is int and 0 < artifact['sizeBytes'] <= MAX_PACKAGE, 'PayloadLimit')
    return package


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


class GitHub:
    def __init__(self, max_calls=24, timeout=240):
        require(1 <= max_calls <= 512 and 1 <= timeout <= 1800, 'OriginBudget')
        self.opener = urllib.request.build_opener(NoRedirect())
        self.deadline = time.monotonic() + timeout
        self.max_calls = max_calls
        self.calls = 0

    def request(self, url, accept='application/vnd.github+json', method='GET', data=None, binary=False):
        self.calls += 1
        parsed = urllib.parse.urlsplit(url)
        api = parsed.hostname == 'api.github.com'
        require(parsed.scheme == 'https' and parsed.port in (None, 443) and not parsed.username and not parsed.password,
                'OriginUrlRejected')
        require(api or binary and parsed.hostname == 'release-assets.githubusercontent.com' and
                parsed.path.startswith('/github-production-release-asset/'), 'OriginRedirectRejected')
        require(self.calls <= self.max_calls and time.monotonic() < self.deadline, 'OriginBudget')
        headers = {'Accept': accept, 'User-Agent': 'Phinix-Index-Bot/0.1', 'Accept-Encoding': 'identity'}
        if api:
            headers['X-GitHub-Api-Version'] = '2022-11-28'
            token = os.environ.get('GH_TOKEN')
            if token:
                headers['Authorization'] = 'Bearer ' + token
        if data is not None:
            headers['Content-Type'] = 'application/json'
        request = urllib.request.Request(url, data=data, headers=headers, method=method)
        try:
            return self.opener.open(request, timeout=min(15, max(1, self.deadline - time.monotonic())))
        except urllib.error.HTTPError as error:
            if binary and error.code in (301, 302, 303, 307, 308):
                return error
            error.close()
            raise Rejected('OriginHttp' + str(error.code)) from None
        except (OSError, urllib.error.URLError):
            raise Rejected('OriginUnavailable') from None

    def consume(self, response, limit):
        chunks = []; size = 0
        length = response.headers.get('Content-Length')
        require(length is None or length.isdecimal() and int(length) <= limit, 'OriginLengthLimit')
        while True:
            require(time.monotonic() < self.deadline, 'OriginTotalTimeout')
            block = response.read(min(65536, limit + 1 - size))
            if not block:
                break
            size += len(block); require(size <= limit, 'OriginDocumentLimit'); chunks.append(block)
        require(length is None or size == int(length), 'OriginLengthMismatch')
        return b''.join(chunks)

    def json(self, path, method='GET', data=None):
        require(path.startswith('/repos/'), 'ApiPathRejected')
        with self.request('https://api.github.com' + path, method=method,
                          data=None if data is None else encode(data)) as response:
            return strict_json(self.consume(response, MAX_JSON))

    def verify_origin(self, artifact):
        prefix = '/repos/' + artifact['repository']
        repo = self.json(prefix)
        require(repo.get('private') is False and repo.get('visibility') == 'public' and
                repo.get('full_name') == artifact['repository'] and str(repo.get('id')) == artifact['repositoryId'] and
                str(repo.get('owner', {}).get('id')) == artifact['ownerId'], 'OriginRepositoryMismatch')
        release = self.json(prefix + '/releases/' + artifact['releaseId'])
        require(str(release.get('id')) == artifact['releaseId'] and release.get('draft') is False and
                release.get('prerelease') is False and release.get('tag_name') == artifact['tag'], 'OriginReleaseMismatch')
        ref = self.json(prefix + '/git/ref/tags/' + urllib.parse.quote(artifact['tag'], safe=''))
        for _ in range(5):
            obj = ref.get('object', {})
            if obj.get('type') != 'tag':
                break
            require(isinstance(obj.get('sha'), str) and re.fullmatch(r'[0-9a-f]{40}', obj['sha']), 'InvalidTag')
            ref = self.json(prefix + '/git/tags/' + obj['sha'])
        require(ref.get('object', {}).get('type') == 'commit' and
                ref['object'].get('sha') == artifact['sourceCommit'], 'OriginCommitMismatch')
        commit = self.json(prefix + '/git/commits/' + artifact['sourceCommit'])
        require(commit.get('sha') == artifact['sourceCommit'], 'SourceUnavailable')
        tree = self.json(prefix + '/git/trees/' + artifact['sourceCommit'] + '?recursive=1')
        require(tree.get('truncated') is False and isinstance(tree.get('tree'), list) and
                any(x.get('type') == 'blob' and x.get('path', '').endswith('.cs') for x in tree['tree']), 'SourceUnavailable')
        asset = self.json(prefix + '/releases/assets/' + artifact['assetId'])
        def matches(value):
            return str(value.get('id')) == artifact['assetId'] and value.get('name') == artifact['assetName'] and \
                type(value.get('size')) is int and value['size'] == artifact['sizeBytes']
        require(matches(asset) and asset.get('state') == 'uploaded' and
                any(matches(x) for x in release.get('assets', [])), 'OriginAssetMismatch')
        require(asset.get('digest') in (None, 'sha256:' + artifact['sha256']), 'OriginDigestMismatch')

    def download(self, artifact, target):
        url = 'https://api.github.com/repos/' + artifact['repository'] + '/releases/assets/' + artifact['assetId']
        for hop in range(6):
            with self.request(url, accept='application/octet-stream', binary=True) as response:
                if response.code == 200:
                    require(response.headers.get('Content-Encoding') in (None, 'identity') and
                            response.headers.get('Content-Range') is None and
                            response.headers.get_content_type() in ('application/octet-stream', 'application/zip'), 'OriginResponseType')
                    raw = self.consume(response, artifact['sizeBytes'])
                    require(len(raw) == artifact['sizeBytes'] and digest(raw) == artifact['sha256'], 'PayloadDigestMismatch')
                    target.write_bytes(raw)
                    return
                require(hop < 5 and response.headers.get('Location'), 'OriginRedirectLimit')
                url = urllib.parse.urljoin(url, response.headers['Location'])
        raise Rejected('OriginRedirectLimit')


def validator(args, command):
    result = subprocess.run(['dotnet', str(args.validator)] + command, capture_output=True, timeout=120)
    # Only trusted validator output; never print candidate source, tokens or signed URLs.
    if result.returncode != 0:
        code = re.fullmatch(r'Rejected: ([A-Za-z0-9]+)\s*', result.stderr.decode('utf-8', errors='replace'))
        raise Rejected(code.group(1) if code else 'StaticValidationFailed')


def inspect(args, package, out, api):
    catalog = {'schemaVersion': 3, 'sourceId': SOURCE, 'snapshotId': digest(encode(package))[:40] if is_workshop(package) else package['artifact']['sourceCommit'], 'packages': [package]}
    catalog_path = out / 'catalog.json'; catalog_path.write_bytes(encode(catalog))
    validator(args, ['catalog', SOURCE, str(catalog_path)])
    if is_workshop(package):
        report = workshop_static(package)
        (out / 'static.json').write_bytes(encode(report))
        return report
    api.verify_origin(package['artifact'])
    payload = out / 'payload.zip'
    try:
        api.download(package['artifact'], payload)
        validator(args, ['payload', SOURCE, str(catalog_path), str(payload), package['id'],
                         package['manifest']['version'], str(out / 'static.json')])
    finally:
        payload.unlink(missing_ok=True)
    return strict_json((out / 'static.json').read_bytes())


def check(args):
    out = args.output; out.mkdir(parents=True, exist_ok=False)
    api = GitHub(); issue = None
    report = {'schemaVersion': 1, 'status': 'rejected', 'code': 'NotChecked', 'scope': 'static-candidate-only'}
    try:
        if args.input:
            package = submission(strict_json(args.input.read_bytes()))
        else:
            require(args.issue_number > 0, 'InvalidIssueNumber')
            issue = api.json('/repos/' + INDEX + '/issues/' + str(args.issue_number))
            require('pull_request' not in issue and issue.get('state') == 'open', 'InvalidSubmissionIssue')
            body = issue.get('body') or ''
            report.update(issueNumber=args.issue_number, issueUpdatedAt=issue['updated_at'], issueBodySha256=digest(body.encode('utf-8')))
            package = candidate_body(body)
        normalized = {'schemaVersion': 1, 'package': package}
        fingerprint = digest(encode(normalized)); report['candidateSha256'] = fingerprint
        (out / 'candidate.json').write_bytes(encode(normalized))
        static = inspect(args, package, out, api)
        if issue:
            current = api.json('/repos/' + INDEX + '/issues/' + str(args.issue_number))
            require(current.get('updated_at') == issue['updated_at'] and
                    digest((current.get('body') or '').encode('utf-8')) == report['issueBodySha256'], 'SubmissionChanged')
        report.update(status='passed', packageId=static['packageId'], files=static['files'])
        if is_workshop(package):
            report.update(code='WorkshopListingVerified', channel='steam-workshop',
                          scope='listing-metadata-only', workshopId=static['workshopId'], listingSha256=static['listingSha256'])
        else:
            report.update(code='StaticCandidateVerified', version=static['version'], artifactSha256=static['sha256'])
    except Rejected as error:
        report['code'] = str(error)
    except (OSError, ValueError, KeyError, TypeError, subprocess.TimeoutExpired, RecursionError):
        report['code'] = 'CheckUnavailable'
    finally:
        (out / 'report.json').write_bytes(encode(report))
    print(json.dumps({'status': report['status'], 'code': report['code']}, ensure_ascii=False))
    return 0 if report['status'] == 'passed' else 1


def failure_hint(code):
    if code == 'MissingCandidateJson':
        return '申请正文必须且只能有一个 `### Candidate JSON` 段。 / Include exactly one `### Candidate JSON` section.'
    if code == 'InvalidCandidateFence':
        return 'JSON 代码块须以 ```json 开始、以 ``` 结束。 / Close the JSON code fence correctly.'
    if code in ('InvalidJson', 'DuplicateField', 'SubmissionEnvelope', 'InvalidPackage'):
        return ('请检查 JSON 语法、重复字段；最外层只能包含 `schemaVersion: 1` 和 `package`。 / '
                'Check JSON syntax and duplicate fields; the envelope must contain only schemaVersion 1 and package.')
    if code in ('InvalidOriginId', 'InvalidRepository', 'InvalidArtifact', 'InvalidCommit'):
        return ('请核对 GitHub 仓库及公开 Release/资产身份，数字 ID 必须写成字符串，源码 commit 使用完整 40 位哈希。 / '
                'Check repository/release/asset identity, string numeric IDs and the full 40-character source commit.')
    if code in ('IssueBodyLimit', 'DocumentLimit', 'PayloadLimit'):
        return '正文、JSON 或 ZIP 超过大小限制，请缩减后重新提交。 / Reduce the oversized submission/document/ZIP.'
    if code == 'UnsupportedRoute':
        return 'DLL 使用 github-release / phinix-dll；工坊使用 steam-workshop / rimworld-mod，申请状态须为 active。 / Choose the managed DLL or Workshop listing route.'
    if code in ('WorkshopFieldsRejected', 'InvalidWorkshopId', 'InvalidIdentifier', 'InvalidText', 'InvalidTags', 'InvalidCompatibility'):
        return ('请核对工坊名称、简介、作者、标签、游戏版本、Mod packageId 和纯数字字符串工坊 ID；'
                '工坊申请不包含 manifest、artifact 或本地化文件。 / Check the Workshop metadata; omit DLL payload fields.')
    if code in ('CheckUnavailable', 'OriginUnavailable', 'OriginBudget'):
        return '检查服务或上游暂不可用，维护者可重试并查看运行日志。 / A check/origin is unavailable; retry and inspect the run log.'
    return ('请按错误代码核对 v3 包清单、文件摘要、程序集、本地化及固定 GitHub 资产；修正申请会重新检查。 / '
            'Use the code to check the v3 manifest, file hashes, assemblies, localization and fixed asset. Edits trigger a new check.')


def post(args):
    report = strict_json(args.report.read_bytes())
    number = report.get('issueNumber')
    require(type(number) is int and number > 0, 'ReportHasNoIssue')
    require(report.get('status') in ('passed', 'rejected') and
            re.fullmatch(r'[A-Za-z0-9]+', report.get('code', '')), 'InvalidReport')
    api = GitHub(); prefix = '/repos/' + INDEX + '/issues/' + str(number)
    issue = api.json(prefix)
    require('pull_request' not in issue and issue.get('state') == 'open' and
            issue.get('updated_at') == report.get('issueUpdatedAt') and
            digest((issue.get('body') or '').encode('utf-8')) == report.get('issueBodySha256'), 'SubmissionChanged')
    fingerprint = report.get('candidateSha256')
    require(fingerprint is None or re.fullmatch(r'[0-9a-f]{64}', fingerprint), 'InvalidFingerprint')
    body = ('### Phinix candidate check / 候选检查\n\n' +
            (('工坊收录信息校验通过 / Workshop listing metadata checks passed.' if report.get('channel') == 'steam-workshop' else '静态检查通过 / Static checks passed.') if report['status'] == 'passed' else '检查未通过 / Check did not pass.') +
            '\n\nCode: `' + report['code'] + '`\n\nCandidate SHA-256: `' + (fingerprint or 'unavailable') +
            '`\n\n本报告不代表首次批准、源码与 DLL 一致性证明或游戏验收。当前 A1 只检查与报告，不自动上架。' +
            '\nThis report is not first-time approval, proof of source/binary correspondence, or in-game acceptance. A1 does not publish packages.')
    if report['status'] == 'rejected':
        author = issue.get('user', {}).get('login', '')
        mention = '@' + author + ' ' if re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9-]{0,38}', author) else ''
        body += ('\n\n' + mention + failure_hint(report['code']) +
                 '\n\n[申请示例 / Submission example](https://github.com/' + INDEX + '/blob/main/examples/managed-submission.json)')
        run = os.environ.get('GITHUB_RUN_ID', '')
        if re.fullmatch(r'[1-9][0-9]*', run):
            body += '\n\n[运行日志 / Run log](https://github.com/' + INDEX + '/actions/runs/' + run + ')'
    elif report.get('channel') == 'steam-workshop':
        body += '\n\n仅检查收录元数据，不代表 Mod 代码审核。订阅、下载和更新由 Steam 管理。 / Listing metadata only; Steam manages subscriptions, downloads and updates.'
    else:
        body += ('\n\n维护者批准后，标准版本资产名可启用同一作者/仓库、相同程序集/模块/依赖范围内的同主版本自动检查。'
                 '身份、范围及主版本变化须重新审核；客户端不会自动更新。 / '
                 'Approval may enroll standard versioned assets in same-major source monitoring within the fixed origin and assembly/module/dependency scope. '
                 'Changed scope requires review; clients never update automatically.\n\n'
                 '[自动版本规则 / Source update policy](https://github.com/' + INDEX + '/blob/main/SourceUpdates.md)')
    api.json(prefix + '/comments', method='POST', data={'body': body})
    if report['status'] == 'rejected':
        api.json(prefix + '/labels', method='POST', data={'labels': [ERROR_LABEL]})
    print('Posted a report for the unchanged submission.')
    return 0


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest='command', required=True)
    p = commands.add_parser('check'); p.add_argument('--validator', type=Path, required=True)
    p.add_argument('--output', type=Path, required=True)
    group = p.add_mutually_exclusive_group(required=True)
    group.add_argument('--input', type=Path); group.add_argument('--issue-number', type=int)
    p = commands.add_parser('post'); p.add_argument('--report', type=Path, required=True)
    args = parser.parse_args()
    try:
        return check(args) if args.command == 'check' else post(args)
    except Rejected as error:
        print('Rejected: ' + str(error)); return 1


if __name__ == '__main__':
    raise SystemExit(main())
