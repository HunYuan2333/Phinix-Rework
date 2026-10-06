#!/usr/bin/env python3
"""Check a pinned validator snapshot; refresh only from an explicit trusted source tree."""
import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import tempfile

ROOT = Path(__file__).resolve().parents[1]
SOURCE_PREFIXES = ('Common/Utils/Framework/ManagedExtensions/', 'Extensions/PluginStore/Client/')


def unique(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError('SnapshotDuplicateField')
        result[key] = value
    return result


def bounded(path):
    if path.stat().st_size > 2 * 1024 * 1024:
        raise ValueError('SnapshotFileLimit')
    return path.read_bytes()


def path_in(root, relative, prefixes):
    if not isinstance(relative, str) or '\\' in relative:
        raise ValueError('SnapshotPathRejected')
    path = PurePosixPath(relative)
    if path.is_absolute() or '..' in path.parts or path.as_posix() != relative or not relative.startswith(prefixes) or path.suffix != '.cs':
        raise ValueError('SnapshotPathRejected')
    full = root / relative
    current = full
    while current != root:
        if current.is_symlink():
            raise ValueError('SnapshotLinkRejected')
        current = current.parent
    return full


def inspect(root, source_root=None):
    root = Path(root).resolve()
    manifest_path = root / 'Validator/production-provenance.json'
    if manifest_path.is_symlink():
        raise ValueError('SnapshotLinkRejected')
    manifest = json.loads(bounded(manifest_path), object_pairs_hook=unique)
    if manifest.get('schemaVersion') != 1 or not isinstance(manifest.get('files'), list) or not 1 <= len(manifest['files']) <= 64:
        raise ValueError('SnapshotManifestRejected')
    sources, snapshots, content = set(), set(), []
    for record in manifest['files']:
        if set(record) != {'source', 'snapshot', 'sha256'}:
            raise ValueError('SnapshotRecordRejected')
        source, snapshot = record['source'], record['snapshot']
        target = path_in(root, snapshot, ('Validator/Production/',))
        # Validate source paths even when the standalone index has no main source checkout.
        origin = path_in(Path(source_root).resolve() if source_root else root, source, SOURCE_PREFIXES)
        if source in sources or snapshot in snapshots:
            raise ValueError('SnapshotDuplicateFile')
        sources.add(source); snapshots.add(snapshot)
        frozen = bounded(target)
        if hashlib.sha256(frozen).hexdigest() != record['sha256']:
            raise ValueError('SnapshotDigestMismatch: ' + snapshot)
        if source_root:
            content.append((record, target, bounded(origin)))
    actual = {p.relative_to(root).as_posix() for p in (root / 'Validator/Production').rglob('*.cs')}
    if actual != snapshots:
        raise ValueError('SnapshotFileSetMismatch')
    return manifest, manifest_path, content


def check(root, source_root=None):
    manifest, _, content = inspect(root, source_root)
    for record, _, raw in content:
        if hashlib.sha256(raw).hexdigest() != record['sha256']:
            raise ValueError('SnapshotSourceChanged: ' + record['source'])
    return len(manifest['files'])


def atomic(path, raw):
    descriptor, name = tempfile.mkstemp(prefix='.snapshot-', dir=path.parent)
    try:
        with os.fdopen(descriptor, 'wb') as stream:
            stream.write(raw); stream.flush(); os.fsync(stream.fileno())
        os.replace(name, path)
    finally:
        if os.path.exists(name):
            os.unlink(name)


def refresh(root, source_root):
    # Read and validate the complete source set before replacing any frozen file.
    manifest, path, content = inspect(root, source_root)
    for record, target, raw in content:
        atomic(target, raw)
        record['sha256'] = hashlib.sha256(raw).hexdigest()
    atomic(path, (json.dumps(manifest, ensure_ascii=False, indent=2) + '\n').encode())
    return check(root, source_root)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=('check', 'refresh'))
    parser.add_argument('--source-root', type=Path)
    args = parser.parse_args()
    if args.action == 'refresh' and args.source_root is None:
        parser.error('refresh requires --source-root; candidate code is never a source tree')
    try:
        count = refresh(ROOT, args.source_root) if args.action == 'refresh' else check(ROOT, args.source_root)
        print(json.dumps({'event': 'validator.snapshot_checked', 'files': count, 'sourceCompared': args.source_root is not None}))
    except (ValueError, OSError) as error:
        parser.exit(1, str(error) + '\n')


if __name__ == '__main__':
    main()
