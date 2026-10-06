import importlib.util
import sys
sys.dont_write_bytecode = True
from pathlib import Path
from types import SimpleNamespace
import json
import tempfile
root=Path(__file__).resolve().parents[2]
script=root/'docs/branch-local/dev/plugin-store/index-repository-bootstrap/scripts/build-repository-metadata.py'
spec=importlib.util.spec_from_file_location('metadata_generator', script)
m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
base=root/'docs/branch-local/dev/plugin-store/index-repository-bootstrap'
vectors=root/'docs/branch-local/dev/plugin-store/metadata-protocol-v1'
with tempfile.TemporaryDirectory(prefix='phinix-metadata-') as tmp:
    output=Path(tmp)
    args=SimpleNamespace(catalog=base/'catalog.json', source=base/'source.json', output=output, repository_id='1402564805', owner_id='64630568', release_id='1', asset_id='1')
    m.build(args);m.build(args)
    for fixture in [vectors/'stable.json', *list((vectors/'published').glob('*.json'))]:
        assert (output/fixture.relative_to(vectors)).read_bytes()==fixture.read_bytes()
    stable=(output/'stable.json').read_bytes()
    args.release_id='2'
    try:m.build(args)
    except ValueError as e:assert 'refusing to overwrite' in str(e)
    else:raise AssertionError('immutable snapshot overwritten')
    assert (output/'stable.json').read_bytes()==stable
    assert not list(output.rglob('*.tmp'))
    for value in ['0','01','18446744073709551616','-1','1.2']:
        try:m.positive_id(value)
        except Exception:pass
        else:raise AssertionError('bad ID accepted')
    assert m.positive_id('18446744073709551615')=='18446744073709551615'
    catalog=json.loads((base/'catalog.json').read_text());catalog['sourceId']='wrong.source'
    args.catalog=output/'bad-catalog.json';args.catalog.write_text(json.dumps(catalog))
    try:m.build(args)
    except ValueError:pass
    else:raise AssertionError('wrong source accepted')
    assert (output/'stable.json').read_bytes()==stable
print('Metadata generator: fixed vectors, repeatability, immutable descriptor, pointer preservation and identity limits passed.')
