#!/usr/bin/env python3
"""Local protocol draft generator. Does not approve packages or publish to GitHub."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import tempfile


def load(path, limit):
    raw = path.read_bytes()
    if not 0 < len(raw) <= limit:
        raise ValueError(f'{path}: document size outside limits')
    def fields(pairs):
        result = {}
        for key, value in pairs:
            if key in result:
                raise ValueError(f'duplicate field: {key}')
            result[key] = value
        return result
    return raw, json.loads(raw.decode('utf-8'), object_pairs_hook=fields,
                           parse_constant=lambda _: (_ for _ in ()).throw(ValueError('non-JSON number')))


def digest(raw):
    return hashlib.sha256(raw).hexdigest()


def encode(value):
    return (json.dumps(value, ensure_ascii=False, separators=(',', ':')) + '\n').encode('utf-8')


def atomic_write(path, raw, immutable=False):
    path.parent.mkdir(parents=True, exist_ok=True)
    if immutable and path.exists():
        if path.read_bytes() != raw:
            raise ValueError(f'refusing to overwrite published snapshot: {path}')
        return
    handle, temporary = tempfile.mkstemp(dir=path.parent, prefix=path.name + '.', suffix='.tmp')
    try:
        with os.fdopen(handle, 'wb') as stream:
            stream.write(raw)
            stream.flush()
            os.fsync(stream.fileno())
        if immutable:
            # Atomic create-if-absent; another publisher cannot overwrite this snapshot.
            os.link(temporary, path)
            os.unlink(temporary)
        else:
            os.replace(temporary, path)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)


def positive_id(value):
    if not re.fullmatch(r'[1-9][0-9]{0,19}', value) or int(value) > 2**64 - 1:
        raise argparse.ArgumentTypeError('expected a canonical nonzero UInt64 decimal string')
    return value


def build(args):
    catalog_raw, catalog = load(args.catalog, 2 * 1024 * 1024)
    _, source = load(args.source, 16 * 1024)
    if set(source) != {'schemaVersion', 'sourceId', 'repository'} or type(source['schemaVersion']) is not int or source['schemaVersion'] != 1:
        raise ValueError('source.json must have exactly schemaVersion=1, sourceId and repository')
    if not isinstance(source['sourceId'], str) or len(source['sourceId']) > 128 or not re.fullmatch(r'[a-z0-9]+(?:[._-][a-z0-9]+)*', source['sourceId']):
        raise ValueError('invalid source ID')
    if not isinstance(source['repository'], str) or not re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9-]{0,38}/[A-Za-z0-9][A-Za-z0-9._-]{0,99}', source['repository']) or source['repository'].lower().endswith('.git'):
        raise ValueError('repository must be owner/repo')
    if set(catalog) != {'schemaVersion', 'sourceId', 'snapshotId', 'packages'} or type(catalog['schemaVersion']) is not int or catalog['schemaVersion'] not in (1, 3) or catalog['sourceId'] != source['sourceId']:
        raise ValueError('catalog source/envelope mismatch')
    if not isinstance(catalog['snapshotId'], str) or not re.fullmatch(r'[0-9a-f]{40}', catalog['snapshotId']):
        raise ValueError('snapshot must be a fixed input commit')
    if not isinstance(catalog['packages'], list) or len(catalog['packages']) > 1024:
        raise ValueError('invalid packages collection')
    # Detailed records/payload approval remain the trusted publisher's responsibility.
    shared = dict(schemaVersion=1, sourceId=source['sourceId'], snapshotId=catalog['snapshotId'],
                  catalogSchemaVersion=catalog['schemaVersion'], catalogSha256=digest(catalog_raw), catalogSizeBytes=len(catalog_raw))
    published = dict(shared, repository=source['repository'], repositoryId=args.repository_id,
                     ownerId=args.owner_id, releaseId=args.release_id, assetId=args.asset_id, assetName='catalog.json')
    published_raw = encode(published)
    stable_raw = encode(dict(shared, publishedSha256=digest(published_raw), publishedSizeBytes=len(published_raw)))
    atomic_write(args.output / 'published' / (catalog['snapshotId'] + '.json'), published_raw, immutable=True)
    # Pointer is written last; this does not check the live release asset or publish the files.
    atomic_write(args.output / 'stable.json', stable_raw)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--catalog', type=Path, required=True)
    parser.add_argument('--source', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    for name in ('repository-id', 'owner-id', 'release-id', 'asset-id'):
        parser.add_argument('--' + name, type=positive_id, required=True)
    try:
        build(parser.parse_args())
    except (ValueError, OSError, KeyError, TypeError) as exc:
        parser.exit(1, str(exc) + '\n')


if __name__ == '__main__':
    main()
