#!/usr/bin/env python3
"""Bootstrap-only empty catalog builder. Not the production package validator."""
import argparse
import hashlib
import json
from pathlib import Path
import re


def build(snapshot, output):
    root = Path(__file__).resolve().parents[1]
    if not re.fullmatch(r"[0-9a-f]{40}", snapshot) or snapshot == "0" * 40:
        raise ValueError("snapshot must be a full, nonzero lowercase Git commit SHA")
    source = json.loads((root / "source.json").read_text(encoding="utf-8"))
    if source != {"schemaVersion": 1, "sourceId": "phinix.official",
                  "repository": "HunYuan2333/Phinix-Plugin-Index"}:
        raise ValueError("unexpected bootstrap source configuration")
    if list((root / "packages").rglob("*.json")):
        raise ValueError("bootstrap cannot process package records; use the future full validator")
    payload = (json.dumps({"schemaVersion": 1, "sourceId": source["sourceId"],
                          "snapshotId": snapshot, "packages": []},
                         ensure_ascii=False, indent=2) + "\n").encode("utf-8")
    destination = Path(output).resolve()
    destination.mkdir(parents=True, exist_ok=True)
    catalog = destination / "catalog.json"
    checksum = destination / "catalog.json.sha256"
    if catalog.exists() or checksum.exists():
        raise FileExistsError("output exists; choose a fresh output directory")
    catalog.write_bytes(payload)
    digest = hashlib.sha256(payload).hexdigest()
    checksum.write_text(digest + "  catalog.json\n", encoding="utf-8")
    print("Generated empty catalog: " + digest)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--snapshot", required=True)
    parser.add_argument("--output", default=str(Path(__file__).resolve().parents[1] / "dist"))
    args = parser.parse_args()
    build(args.snapshot, args.output)
