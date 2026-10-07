#!/usr/bin/env python3
"""Project package-owned language files and build validated v3 catalog drafts. No remote writes or approvals."""
import argparse
import copy
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile
import zipfile


def require(condition, code):
    if not condition:
        raise ValueError(code)


def read(path, maximum=2 * 1024 * 1024):
    require(path.is_file() and not path.is_symlink() and path.stat().st_size <= maximum, 'InputLimit')
    raw = path.read_bytes()
    def fields(pairs):
        result = {}
        for key, value in pairs:
            require(key not in result, 'DuplicateField'); result[key] = value
        return result
    return json.loads(raw.decode('utf-8'), object_pairs_hook=fields,
                      parse_constant=lambda _: (_ for _ in ()).throw(ValueError('InvalidJson')))


def encode(value):
    return (json.dumps(value, ensure_ascii=False, separators=(',', ':'), allow_nan=False) + '\n').encode('utf-8')


def validate(validator, command):
    result = subprocess.run(['dotnet', str(validator)] + command, capture_output=True, timeout=120)
    require(result.returncode == 0, 'TrustedValidationRejected')


def write_new(path, raw):
    path.parent.mkdir(parents=True, exist_ok=True)
    handle, temporary = tempfile.mkstemp(dir=path.parent, prefix='.catalog-')
    try:
        with os.fdopen(handle, 'wb') as stream:
            stream.write(raw); stream.flush(); os.fsync(stream.fileno())
        os.link(temporary, path)  # Never replace an existing output, even under a race.
    finally:
        os.unlink(temporary)


def project(candidate, payload, validator):
    require(type(candidate) is dict and set(candidate) == {'schemaVersion', 'package'} and type(candidate['schemaVersion']) is int and candidate['schemaVersion'] == 1, 'SubmissionEnvelope')
    package = copy.deepcopy(candidate['package'])
    if package.get('channel') == 'steam-workshop':
        require(payload is None, 'UnexpectedPayload')
        with tempfile.TemporaryDirectory() as temporary:
            record = {'schemaVersion': 3, 'sourceId': 'author.draft', 'snapshotId': hashlib.sha256(encode(package)).hexdigest()[:40], 'packages': [package]}
            path = Path(temporary) / 'catalog.json'; path.write_bytes(encode(record))
            validate(validator, ['catalog', 'author.draft', str(path)])
        return {'schemaVersion': 1, 'package': package}
    require(package.get('channel') == 'github-release' and package.get('management') == 'phinix-dll', 'UnsupportedRoute')
    require('name' not in package and 'summary' not in package, 'UnexpectedField')
    require(payload is not None, 'PayloadRequired')
    artifact = package['artifact']; manifest = package['manifest']
    require(payload.is_file() and not payload.is_symlink() and payload.stat().st_size == artifact['sizeBytes'] and 0 < artifact['sizeBytes'] <= 128 * 1024 * 1024, 'PayloadSizeMismatch')
    digest = hashlib.sha256()
    with payload.open('rb') as stream:
        for chunk in iter(lambda: stream.read(65536), b''):
            digest.update(chunk)
    require(digest.hexdigest() == artifact['sha256'], 'PayloadDigestMismatch')
    declaration = manifest.get('localization')
    require(type(declaration) is dict and type(declaration.get('files')) is list and 0 < len(declaration['files']) <= 16, 'MissingLanguageFile')
    resources = {f['path']: f for f in manifest['resources']}
    translations = {}; total = 0
    with zipfile.ZipFile(payload) as archive, tempfile.TemporaryDirectory() as temporary:
        require(len(archive.infolist()) <= 4096, 'ArchiveLimit')
        names = [f.filename for f in archive.infolist()]
        require(len(set(names)) == len(names), 'ArchivePathConflict')
        for name in declaration['files']:
            require(name in resources and name in names, 'InvalidLocalizationResource')
            entry = archive.getinfo(name); resource = resources[name]
            require(0 < entry.file_size == resource['length'] <= 128 * 1024, 'LocalizationLimit')
            total += entry.file_size; require(total <= 1024 * 1024, 'LocalizationLimit')
            raw = archive.read(entry); require(hashlib.sha256(raw).hexdigest() == resource['sha256'], 'LocalizationDigestMismatch')
            language_path = Path(temporary) / 'language.json'; language_path.write_bytes(raw)
            language = read(language_path, 128 * 1024)
            require(type(language['schemaVersion']) is int and language['schemaVersion'] == 1 and isinstance(language['locale'], str) and re.fullmatch(r'[A-Za-z]{2,3}(?:-[A-Za-z]{4})?(?:-(?:[A-Za-z]{2}|[0-9]{3}))?', language['locale']), 'InvalidLocale')
            parts = language['locale'].split('-'); locale = '-'.join([parts[0].lower()] + [part.title() if len(part) == 4 else part.upper() for part in parts[1:]])
            require(locale == Path(name).stem and locale not in translations, 'LocalizationLocaleMismatch')
            translations[locale] = language['display']
        localization = {'translations': dict(sorted(translations.items()))}
        if 'defaultLocale' in declaration:
            localization['defaultLocale'] = declaration['defaultLocale']
        package['localization'] = localization
        catalog = {'schemaVersion': 3, 'sourceId': 'author.draft', 'snapshotId': artifact['sourceCommit'], 'packages': [package]}
        path = Path(temporary) / 'catalog.json'; path.write_bytes(encode(catalog))
        validate(validator, ['payload', 'author.draft', str(path), str(payload), package['id'], manifest['version'], str(Path(temporary) / 'report.json')])
    return {'schemaVersion': 1, 'package': package}


def build(records, source, snapshot, validator):
    require(re.fullmatch(r'[a-z0-9]+(?:[._-][a-z0-9]+)*', source) and len(source) <= 128 and re.fullmatch(r'[0-9a-f]{40}', snapshot), 'InvalidIdentity')
    require(len(records) <= 1024, 'CollectionLimit')
    packages = []
    for path in records:
        value = read(path)
        require(type(value) is dict and set(value) == {'schemaVersion', 'package'} and type(value['schemaVersion']) is int and value['schemaVersion'] == 1, 'SubmissionEnvelope')
        packages.append(value['package'])
    require(all(type(p) is dict and isinstance(p.get('id'), str) and (p.get('manifest') is None or type(p['manifest']) is dict) for p in packages), 'InvalidPackage')
    packages.sort(key=lambda p: (p['id'], (p.get('manifest') or {}).get('version', 'workshop')))
    raw = encode({'schemaVersion': 3, 'sourceId': source, 'snapshotId': snapshot, 'packages': packages})
    require(len(raw) <= 2 * 1024 * 1024, 'DocumentLimit')
    with tempfile.TemporaryDirectory() as temporary:
        path = Path(temporary) / 'catalog.json'; path.write_bytes(raw)
        validate(validator, ['catalog', source, str(path)])
    return raw


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest='command', required=True)
    p = commands.add_parser('project'); p.add_argument('--candidate', type=Path, required=True); p.add_argument('--zip', type=Path)
    p = commands.add_parser('build'); p.add_argument('--record', type=Path, action='append', default=[]); p.add_argument('--source-id', required=True); p.add_argument('--snapshot', required=True)
    for p in commands.choices.values():
        p.add_argument('--validator', type=Path, required=True); p.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    try:
        raw = encode(project(read(args.candidate), args.zip, args.validator)) if args.command == 'project' else build(args.record, args.source_id, args.snapshot, args.validator)
        write_new(args.output, raw)
        print(json.dumps({'schemaVersion': 3, 'sha256': hashlib.sha256(raw).hexdigest(), 'sizeBytes': len(raw)}))
    except (ValueError, OSError, KeyError, TypeError, zipfile.BadZipFile, subprocess.TimeoutExpired):
        parser.exit(1, 'CatalogDraftRejected\n')


if __name__ == '__main__':
    main()
