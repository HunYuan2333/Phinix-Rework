---
name: phinix-rework-plugin-development-en
description: Develop or troubleshoot Phinix Rework client managed plugins and data-only theme packages, including public APIs, dual-runtime packaging, local installation and Index delivery. Use for tabs, actions, themes and plugin build issues; not as a replacement for full RimWorld Mod or server development.
---

# Phinix Rework plugin and theme development

Read the references needed for the requested feature. Actual source, compile references and delivered packages take precedence. Check historical records, working-tree versions, remote commits and player installations separately.

## Choose references

| Task | Reference |
| --- | --- |
| Choose a DLL plugin, theme package or RimWorld Mod | [Requirements and routes](references/routes.md) |
| Find signatures and assembly ownership | [API quick reference](references/api-cheat-sheet.md) |
| Author theme XML, package and install it | [Theme packages](references/theme-package.md) |
| Build 1.6/net472 and 1.7/net10.0 | [Dual-runtime builds](references/dual-runtime.md) |
| Check versions, references and tools | [Environment and versions](references/environment.md) |
| Organize features, state and external boundaries | [Engineering](references/engineering.md) |
| Compose, dependency injection and cleanup | [Composition and ownership](references/composition.md) |
| Presentation, actions, messages and inventory | [Public capabilities](references/capabilities.md) |
| Create a project, preflight and install locally | [Local loop](references/local-loop.md) |
| Diagnose failures and bound acceptance claims | [Verification and recovery](references/verification.md) |
| Local distribution, Releases and Index | [Publication](references/publication.md) |
| Adopt or troubleshoot automated releases | [Actions](references/actions.md) |
| Separate source state from historical validation | [Evidence](references/basis.md), [validation record](references/validation.md) |

## Minimal workflow

- **Managed plugin**: read the API and composition references. Copy the project, C# and configuration from `templates/minimal-plugin/` into an independent plugin's `example/` directory; change package/module/assembly identities. The entry point is `Compose(IExtensionBuilder builder)`. Follow the dual-runtime reference, install locally, restart the game and verify behavior. This template is a minimal native-tab example; use the current Example for a full public presentation session.
- **Theme package**: copy `templates/theme-package/`. Edit `packageId`, name, author, version and compatibility range in `theme-config.json`. Author floating-point RGBA in `<PhinixTheme version="2">`, or use version 3 as documented. Run `--pack-theme CONFIG OUTPUT.zip` and `--validate-theme ZIP`. Installation, selection and application are separate operations. Data themes need no runtime variants.
- **Delivery**: preserve the user's chosen local, manual Release or automated route. If unspecified, first provide a locally verifiable artifact. DLL policy scanning and maintainer theme publication are different workflows; do not promise that any Release automatically appears in the store.

Use the relevant reference for CLI arguments and output fields. Do not guess success messages, command options or API overloads.

## Preserve boundaries

- Host supplies the same public presentation, theme, action and confirmation facilities to all plugins. Plugins retain business, item and protocol authority. Do not depend on Host internals or add an official-only renderer.
- Do not dispose borrowed services. Clean up owned scopes, sessions, registration handles and subscriptions at their actual lifecycle boundaries. Closing a view does not stop a module or cancel business work.
- Callback, Task and queue completion are not business confirmation. Preserve sent-but-unknown item operations; do not automatically refund or resend them.
- Access game objects only on the game main thread in a valid save context. Reject failed batch conversions while retaining the input; never silently drop undecodable items.
- Preserve existing changes and exact submodule gitlinks. Do not alter vendored protobuf pins to bypass build failures. Keep game-version differences in narrow plugin adapters when required, not scattered through business code.
- Do not distribute game, Harmony or Host core DLLs, private references or credentials. Development, verification and local packaging do not authorize pushes, releases, Index admission, deployment or formal-save changes. Honor existing user authorization.

When required references or permissions are missing, identify the blocked step and continue independent work. Report source inspection, build, white-box and game results separately, with pending items explicit. A quiet log does not establish full compatibility.
