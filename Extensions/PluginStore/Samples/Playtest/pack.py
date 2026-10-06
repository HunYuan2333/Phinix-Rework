#!/usr/bin/env python3
"""Create a reproducible test ZIP with exactly the declared DLL and package resources."""
import argparse
from pathlib import Path
import zipfile

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--assembly', type=Path, required=True)
parser.add_argument('--output', type=Path, required=True)
args = parser.parse_args()
source = Path(__file__).resolve().parent
files = [(p.relative_to(source / 'Package').as_posix(), p) for p in (source / 'Package').rglob('*') if p.is_file()]
files.append(('Assemblies/Phinix.Store.Playtest.dll', args.assembly))
args.output.parent.mkdir(parents=True, exist_ok=True)
with zipfile.ZipFile(args.output, 'x') as archive:
    for name, path in sorted(files):
        if path.is_symlink():
            raise ValueError('Package resources must be regular files')
        entry = zipfile.ZipInfo(name, date_time=(2026, 10, 4, 0, 0, 0))
        entry.compress_type = zipfile.ZIP_DEFLATED
        entry.create_system = 3
        entry.external_attr = 0o100644 << 16
        archive.writestr(entry, path.read_bytes())
print(args.output)
