#!/usr/bin/env python3
"""Refresh explicit resource hashes for the two optional legacy client plugins."""
import argparse
import hashlib
import json
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="Fail if a declaration needs updating")
    args = parser.parse_args()
    repository = Path(__file__).resolve().parents[2]
    stale = []
    for package in ("LegacyRedPacket", "LegacyTalentTrade"):
        root = repository / "Extensions" / package / "Client"
        resources = []
        for path in sorted((root / "Resources" / package / "Localization").glob("*.json")):
            data = path.read_bytes()
            resources.append({"path": path.relative_to(root).as_posix(), "length": len(data),
                              "sha256": hashlib.sha256(data).hexdigest()})
        if not resources:
            raise ValueError(f"Missing language resources: {package}")
        value = {"schemaVersion": 1, "assemblyName": package + "Extension.Client",
                 "resources": resources,
                 "localization": {"defaultLocale": "en-US", "files": [r["path"] for r in resources]}}
        expected = json.dumps(value, indent=2) + "\n"
        target = root / "localization.json"
        if not target.exists() or target.read_text(encoding="utf-8") != expected:
            stale.append(package)
            if not args.check:
                target.write_text(expected, encoding="utf-8")
    if args.check and stale:
        parser.exit(1, "Stale declarations: " + ", ".join(stale) + "\n")
    print("Optional plugin localization declarations verified." if args.check else "Optional plugin localization declarations refreshed.")


if __name__ == "__main__":
    main()
