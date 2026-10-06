# Server image publishing

GitHub Actions has one workflow: `Build and publish server image` in
`.github/workflows/docker.yml`. It compiles the dedicated server inside the
Dockerfile and pushes the runtime image to `hunyuan23333/phinix-rework` on Docker
Hub. It does not build the RimWorld client, run regression harnesses, or upload
client/server ZIP artifacts.

## Triggers and tags

| Trigger | Docker tags |
| --- | --- |
| Push to `dev` | `dev`, `sha-<full commit SHA>` |
| Push to `main` | `latest`, `sha-<full commit SHA>` |
| Push a version tag such as `v1.2.3` | `1.2.3`, `sha-<full commit SHA>` |
| Manual dispatch | Same tagging rules for the selected ref; always a SHA tag |

Pull requests do not trigger publishing. Production `latest` is only updated
from `main`; publishing a development build or version tag does not update it.
Publishing uses the existing repository secrets `DOCKER_HUB_USERNAME` and
`DOCKER_HUB_TOKEN`. Their presence does not prove that the token still has push
permission; the login/push steps verify that when the workflow runs.

The workflow checks out submodules, sets up Docker Buildx, and uses GitHub Actions
build caching. Action versions are pinned to commits. The image platform is
`linux/amd64`.

## Image contents and persistence

The build stage pins .NET SDK `10.0.401` and publishes
`Server/Server.csproj` in Release configuration. Root `Directory.Build.props`
and `Directory.Build.targets` are copied into the build context.
`MSBuildSDKsPath` selects that SDK without changing protobuf's vendored
`global.json`. No RimWorld assemblies, Harmony restore, or protocol compiler
installation is needed for this Linux server build.

The publish output must contain the server entry point, runtime metadata,
LiteNetLib, and the four official Chat/Trade DLLs under `Extensions/`.
The Dockerfile fails if these files are missing or game/Unity reference DLLs
are present. Official extensions still use the ordinary discovery path.

The final image uses the .NET 10 runtime, stores application files under `/app`,
and starts the server with `/data` as its working directory. Persist `/data`
for configuration, credentials, user databases, logs, and extension state.
The exposed server port is `16200/udp`; optional user extensions can be mounted
under `/app/UserExtensions` as shown in `docker-compose.yml`.

## Local validation

```sh
docker build -t phinix-rework:local .
docker run --rm -i --network none phinix-rework:local
```

Enter `exit` to shut down the validation container. Regression harnesses and
client builds remain available as manual developer checks in `AGENTS.md`.
A successful image build verifies compilation and packaging; it does not prove
in-game behavior or a successful Docker Hub push.
