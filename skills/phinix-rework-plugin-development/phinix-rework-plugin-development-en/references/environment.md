# Environment and version identification

Read before building, when capabilities/tools are uncertain, or before release. First consult [evidence and status](basis.md) for this skill's capability prerequisites.

## Identify the target

Obtain from existing context or the user: installed host and RimWorld versions, public DLL assembly versions/provenance/digests, and plugin source/tool versions. “Abstractions 1.9” establishes the DI contract version, not that sideload UI or tools have shipped. Inspect the target version's Quickstart, tool options, and actual public interfaces. If an older host lacks Compose/sideloading, stop that loop: offer an upgrade to a confirmed supporting version or a maintainer-verified fixed development snapshot. If the user must keep the old host, assess its actual legacy entry/testing route separately; do not claim new features exist there.

With source repositories, read applicable repository/parent AGENTS first, then inspect read-only:

```sh
git branch --show-current
git rev-parse HEAD
git status --short --untracked-files=all
git diff --stat
git diff --cached --stat
git ls-files --stage Dependencies/Phinix.Common
git -C Dependencies/Phinix.Common rev-parse HEAD
git -C Dependencies/Phinix.Common status --short --untracked-files=all
git -C Dependencies/Phinix.Common ls-files --stage Dependencies/protobuf
```

Read relevant diffs/untracked source as needed. Prioritize the Common submodule actually referenced by the client, not a presumed identical sibling checkout. Also verify protobuf HEAD/status. Do not reset, overwrite by branch switching, or clean untracked files. Installed release DLLs may differ from source HEAD; record them separately. Verify current remote state with read-only queries when needed; on failure report unknown, rather than calling origin/main current.

With Git/network access, acquire a pinned host into a safe separate source directory, check out the verified full commit, then use `git submodule update --init --recursive` for that commit's gitlinks, including protobuf. Do not follow floating branches or overwrite a dirty checkout. Without Git/network, reuse only verified fixed snapshots or existing references. A plain source ZIP may omit submodules; verify provenance, commit, and completeness.

## Check by stage

| Stage | Required now | Practical route if missing |
| --- | --- | --- |
| Understand requirements | Existing documentation/source | Design offline; list unknown capabilities |
| Compile plugin | Suitable .NET SDK/MSBuild, public host DLLs, game/Unity and net472 mscorlib references; Harmony references only if used | Reuse legally installed/built host assemblies. Stop before compilation if required DLLs are absent; guide preparation from the user's own game/legal references |
| Use Example pack.py | Python 3 and compilation prerequisites | Verify `python3 --version` or Windows `py -3 --version`; without Python use independent `dotnet build` then ManagedPackageTool directly, without invented pack flags |
| Acquire pinned source | Git; network for initial acquisition | Verify `git --version`; without Git use a verified maintainer snapshot containing pinned submodules, not guessed APIs from incomplete ZIPs |
| Tools/preflight | Current tool needs .NET 10; plugin targets net472 | Verify `dotnet --list-sdks`/`--list-runtimes`, or reuse trusted built tools; do not change the tool target to the game plugin target |
| Formal publication | Git, gh authentication, GitHub/relevant endpoints | Only now check `gh --version`, `gh auth status`, and reachability. Authorized browser publication can replace gh; its absence must not block local development |

Use official acquisition guidance: [Python](https://www.python.org/downloads/), [Git](https://git-scm.com/downloads), [.NET](https://dotnet.microsoft.com/download), and [gh installation/authentication](https://cli.github.com/manual/). Guide platform-appropriate installation and verification; do not install, change system settings, or log in without authorization. On Windows use PowerShell argument arrays, single-line commands, or backtick continuation, not POSIX backslash continuation. Pass paths with spaces as one argument. Example paths are replaceable inputs, not maintainer-specific locations.

First-time .NET restore may require network access. Offline operation requires complete caches, SDKs, and pinned references. Do not edit vendored protobuf SDK pins; use the documented process-local SDK-path alternative for the relevant version. There is no additional Phinix NuGet SDK or offline downloader.

The three maintainer plugin main workflows use private compilation references and `BUILD_REFERENCES_TOKEN`. Third parties provide their own legal references and secure retrieval. Do not require access to that private repository or request its token; never read, print, or collect credentials. Game, Unity, host, and Harmony DLLs are compilation-only and must not enter plugin ZIPs or public reference assets.
