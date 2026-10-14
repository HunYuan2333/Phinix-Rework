# Host store compatibility version correction

The 1.7 installation report failed with `CandidateHostIncompatible` during planning, before any package bytes were downloaded. The installed Host DLL metadata reported product 0.10.1 but its `Client.CompatibilityVersion` constant remained 0.9.7. Official API 3 plugin manifests require Host >=0.10.1.

Correct the Host capability declaration to 0.10.1 and release product 0.10.2. Preserve CLR assembly identity 0.9.7.0 and API 3.0; no plugin reissue or contract change is required. The packaging audit now reads the actual compiled constant, rather than treating product or CLR versions as evidence of store compatibility.

Validation:

- Source and actual installed DLL metadata agree on the original mismatch.
- Both 1.6/net472 and 1.7/net10.0 graphs compile with product 0.10.2 and capability 0.10.1.
- Managed runtime regression: 3396 assertions passed.
- Real published RedPacket 1.2.6, TalentTrade 1.2.3 and Example 1.1.2 ZIPs: the original capability reproduces rejection; the rebuilt capability passes 37 install/profile/restart/removal assertions per package.
- Distribution metadata tests: four passed. Workshop identity remains 3735269431.
- Local dual Host ZIP SHA256: `1f28fa8fb1f150cf213f38955b1ff6f9edfe9e1c5d4b0a1137c1791ebda2340e`.

Game installation acceptance remains pending. A separate startup `StoreReleaseNoticeFailed` warning is not evidence of the install failure's cause and remains to be diagnosed independently. Raw player logs and build artifacts are not included in source.
