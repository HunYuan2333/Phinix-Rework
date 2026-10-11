I want to redesign the UX and UI of Phinix Rework, but do NOT modify implementation yet.

First, perform a comprehensive audit of the current codebase, current UI/UX, existing theme system, and the new Figma design.

Visual reference:

https://www.figma.com/design/Mqxq3scgyAjpLFXbGJv78n/Phinix-Rework-Redesign?node-id=1-4&t=FHRwtAkrEoAjF7ah-1

The Figma frame is the current visual direction, but it is NOT permission to invent new product behavior.

Phinix Rework is fundamentally a public chat room inside RimWorld, with additional existing modules/features such as trade, inventory, store, plugins, legacy red packet functionality, and other plugin-provided UI.

Important current constraints:
- no user avatars
- no user title system
- no emoji/reaction system
- no file attachments
- no private messages
- no full message search
- member/user search is more relevant than message search
- do not invent room/channel hierarchies that do not exist
- preserve old-client compatibility
- preserve current server/protocol behavior unless explicitly marked as future work
- preserve current plugin architecture

Important existing infrastructure:
Phinix already has a theme loader implemented for the current legacy UI.

Do NOT replace, bypass, or duplicate the existing theme loader.

The new UI should evolve from the existing theme infrastructure.

## Part 1 — UX Audit

Map the current major user flows, including at least:

- opening and using the main chat
- sending messages
- browsing members
- searching/filtering members
- trade flow
- inventory flow
- store flow
- plugin-related flow
- settings access
- client-side settings
- connected server / room settings
- connection / reconnect / error handling
- empty states
- loading states
- permission/validation/error states
- legacy functionality

For each major flow, document:

1. User goal
2. Current entry point
3. Current steps
4. Friction points
5. Confusing or hidden actions
6. Redundant steps
7. UX inconsistencies
8. Technical constraints
9. Whether the issue is:
   - UI-only
   - client-logic dependent
   - plugin-architecture dependent
   - server/protocol dependent
   - backward-compatibility sensitive
10. Low-risk UX improvements
11. Medium-risk improvements
12. High-risk / future improvements

Also identify places where the UI exposes implementation details rather than user intent.

Do not propose speculative social features just because Discord/Telegram have them.

## Part 2 — UI Architecture Audit

Inspect the existing UI abstraction and component structure.

Identify:
- existing reusable UI abstractions
- existing design-system-like components
- duplicated rendering logic
- layout code that is tightly coupled to business logic
- components that can directly support the new design
- components that need extension
- components that should be replaced
- places where visual state and business state are mixed
- scaling / resolution / localization risks

Compare the current implementation against the Figma design.

Produce a visual gap analysis covering:
- spacing
- typography
- rounded corners / radii
- component hierarchy
- navigation
- message layout
- member panel
- composer
- buttons
- tabs
- cards
- special message content
- borders / dividers
- surfaces
- hover / selected / disabled / focused states

Do not implement anything.

## Part 3 — Theme Loader Audit

Evaluate the current theme loader as infrastructure for the new UI.

Do not create a parallel styling system.

Identify:
- current theme tokens
- tokens that are still usable as-is
- tokens that should be generalized
- tokens that are too legacy-specific
- missing tokens required by the new UI
- compatibility concerns with existing user themes

Evaluate whether the new design requires tokens such as:

- background
- surface
- surfaceElevated
- surfaceHover
- surfaceSelected
- border
- divider
- textPrimary
- textSecondary
- textMuted
- accent
- success
- warning
- danger
- composerBackground
- memberPanelBackground
- cardBackground

Also evaluate whether these should be themeable or fixed design-system constants:

- radius
- spacing
- control heights
- border widths
- typography scale
- animation timing

Prefer backward-compatible evolution of the existing theme format.

If old themes are missing new fields, propose a safe fallback/derivation strategy rather than breaking them.

For example:
- derive surface from background
- derive hover/selected from surface/accent
- fall back to built-in defaults when new tokens are absent

Assess whether a theme schema/version field is needed.

## Part 4 — Technical Feasibility

Account for the actual runtime:

- RimWorld
- Unity / Verse UI / IMGUI
- current project .NET / Mono constraints
- no WebView
- no CSS
- avoid blur-heavy or glassmorphism effects
- preserve performance
- avoid per-frame allocations where practical
- reuse textures / GUIStyles / cached drawing assets

Classify visual features as:
- straightforward
- feasible with custom drawing / 9-slice / texture assets
- feasible but not worth the complexity
- should be avoided

## Deliverable

Produce a structured audit report with:

1. Current UX map
2. UX problem list ranked by severity
3. Current UI architecture assessment
4. Figma-to-current-UI gap analysis
5. Theme loader assessment
6. Theme migration / compatibility risks
7. Technical feasibility notes
8. Quick wins
9. Medium-scope improvements
10. Long-term / protocol-dependent improvements
11. Open questions that genuinely require product decisions

Do NOT modify code yet.

Do NOT generate a roadmap yet beyond rough categorization.

The goal of this phase is to understand the system accurately before planning implementation.

---

## Additional context — current implementation and non-negotiable extension boundaries

This section supplements the original prompt. Keep the original audit-only scope: inspect, compare, and document; do not modify code, implement the redesign, or produce an implementation roadmap in this phase.

Use the current local working trees under `/home/hunyuan2333/Phinix`, especially `Phinix-Rework`, its `Dependencies/Phinix.Common`, and `Phinix-Example-Plugin`. There are substantial local changes. An audit of remote main alone will miss the current theme and API work. Verify the code and distinguish implemented behavior, documented proposals, and behavior not yet verified in-game. In documents with historical progress entries, read the latest status and subsequent user decisions.

### A. Theme support is a permanent product capability

The theme system is already Host infrastructure shared by built-in and third-party plugins. Treat its public contracts and user capabilities as requirements of the redesign.

Current contracts include:

- `IClientThemeReader`, `IClientTheme`, and immutable `ClientThemeSnapshot`: keyed colors, float parameters, change notifications, and captured drawing palettes.
- `IClientThemeRegistry` / `IClientThemeRegistration`: registration bound to the actual activating module, owner namespaces, color defaults/fallbacks, ranged float parameters, labels/groups/descriptions, batch Commit, and disposal on module stop.
- `IClientThemeCatalog` / `IClientThemeWindowService`: discovery, selection, editable drafts, preview, per-theme user overrides, diagnostics, refresh, and the independent theme window.
- `IClientThemePackageService` / `IClientThemePackageExporter`: local package management and ZIP export.

Preserve theme selection, editing, preview/application, per-theme overrides, plugin-defined theme keys, and hot application. The entry remains usable from Host settings without requiring Chat or Store to be enabled. The redesign may reorganize these interactions but must account for each capability.

Use the existing canonical keys, such as `ui.text.primary`, `ui.text.secondary`, `ui.background`, `ui.surface`, `ui.separator`, `ui.hover`, `ui.accent`, and the existing control/status/module keys. Names suggested earlier in this prompt are design concepts to map against this namespace, not permission to rename persisted keys or introduce a second token system.

Audit the existing resolver before proposing derived colors. It already handles explicit overrides, theme values, the built-in baseline, declared inheritance/fallback keys, and registration defaults. Explicit inherit affects this order. Partial themes and absent optional plugins are normal; missing keys must remain usable through defined fallback. Preserve unknown keys and explicit values through editing/export, and avoid automatically flattening all inherited defaults into user files.

Theme XML already has versioned input handling: historical v1/v2/v3 files are decoded by `ThemeInputAdapter`, while managed packages accept canonical v2/v3 documents. Do not describe schema/versioning as entirely missing. Evaluate any additional schema needs against that actual boundary. Assess radius/spacing/typography/animation customization separately; the existing float API does not imply that all layout dimensions are already themeable.

### B. Preserve the current local theme package flow

T5 local closure has been implemented in code:

1. The editor exports a standard pure-data theme ZIP with name, author, stable package ID, version, and local path feedback.
2. Store handles local theme ZIP installation, update, and uninstall without DevMode; code-plugin sideloading retains its separate DevMode requirement.
3. Installed variants enter the theme selector and apply without restarting the game.
4. Explicit uninstall of the selected package returns selection to default while retaining user profiles. Accidental loss/corruption or an update removing a variant preserves selection intent and uses fallback display.

The selector's local import button has been removed; export remains. Existing user XML/settings are still supported, and XML Import/Export methods remain in the public catalog contract. Do not infer that removing a UI button authorized deleting those contracts, existing user files, or compatibility readers.

Storage/recovery and selection post-processing remain infrastructure responsibilities, not view logic. The package system uses independent managed theme storage and shared owned-file transaction machinery; do not replace it with direct ZIP extraction or delete files from a UI callback.

Local code has been compiled/packaged; this does not establish that game behavior, crash recovery, or three-platform desktop integration has been verified. Online theme admission, publication-flow changes, online releases, and shortcut uploads are deferred together. Do not add upload/login/publication work to this audit. Existing online functionality has not been disabled.

### C. Plugin openness must survive the redesign

Built-in Chat, Trade, Inventory, Store, and maintained plugins are plugins with ordinary discovery, registration, dependency, and lifecycle rules. The Host must remain independent of plugin implementations and business Contracts.

Current UI extension surfaces include `IMainTabProvider`, `IServerSidebarProvider`, `INoticeBannerProvider`, `IBadgeProvider`, `IUiAcceptKeyHandler`, `IClientSettingsPanelProvider`, and optional responsive layout interfaces. The shell currently collects and presents providers. A redesigned navigation/sidebar/settings structure must explain how registered third-party content remains discoverable, accessible, ordered, and usable when optional modules are absent.

Do not reduce extensibility to a fixed set of hardcoded official tabs, panels, menu items, or colors. A new plugin should be able to register ordinary capabilities without editing Host code. Evaluate discovery, overflow, narrow-window handling, localization, empty provider sets, and error isolation as part of the UX.

Keep plugin-owned business contexts and operations in plugin Contracts. General presentation/ownership plumbing belongs in Host abstractions. Existing `ClientWidgets` and responsive layout helpers are useful foundations; extend or reorganize presentation while preserving public capability and ownership semantics. Any proposed replacement must document how current providers and plugin authors can continue using it.

### D. Account for the APIs already available and their actual limits

- `IClientExtensionPointService` provides module-owned typed action points and contributions. Actions can be presented as context menus, toolbars, or row operations. Collection produces an owned short-lived result set with labels, disabled reasons, ordering, context checks, and execution results. Closing/replacing the view or stopping the owner must release it.
- `IClientApiDirectory` provides typed provider snapshots and handles that revalidate the exact registration and module availability. Registered and available are distinct; missing optional capabilities are normal. Returned business APIs are borrowed.
- `IClientTaskService` provides owned main-thread task scopes and observable callback states. Callback completion is not remote/server/business confirmation; cancellation does not undo already executed effects.
- `IClientLifetimeService` provides actual-module scopes, owned synchronous resources, guarded main-thread subscriptions, stopping tokens, and lifecycle cleanup. Borrowed Host services must not be transferred into owned resource cleanup.

The typed action mechanism and Example demonstrations exist. This does **not** mean actual Chat/Trade/Inventory views already expose all planned menu, toolbar, detail, message-rendering, or content-contribution points. Module file resources/storage and broader business APIs also remain incomplete.

During the audit, identify concrete extension surfaces the proposed UX will require. Separate existing interfaces, missing typed contexts, missing view integration, and genuinely new contracts. More public APIs may be needed, but do not implement them in this audit or silently assume they exist. Every new API does not need a built-in consumer first; Example can demonstrate its semantics. It must still have coherent ownership, threading, state/result, and failure rules.

### E. Keep compatibility at its documented boundaries

Interpret the original compatibility requirement according to the current code and documented commitments:

- The maintained red packet and talent-trade plugins retain internal adapters supporting the pre-upgrade 1.9 client as well as the current client.
- Current UI and ordinary plugin business code should use current contracts without knowing historical API versions, theme schemas, protocol modes, or receipt/journal formats. Translation belongs in the relevant input, protocol, binding, or recovery adapter.
- Preserve required existing data readers and server/protocol behavior. Do not expand adapter-only legacy interfaces into new author APIs, and do not promise that new pure theme packages or the new UI automatically work on old clients.
- Product version, SDK/API version, CLR assembly identity, theme document version, package schema, and store catalog schema are separate. The current product compatibility version is 0.9.7, SDK/API is 2.0, and abstractions CLR identity remains 1.9; inspect actual declarations before judging incompatibility.

### F. Add an extension/theme preservation assessment to the report

For each proposed navigation change, component replacement, and major interaction, include:

1. Existing theme tokens/public contracts/provider capabilities affected.
2. How plugin-defined styling and third-party contributed content fit the new design.
3. Behavior when keys or optional providers are absent, and when a contribution becomes stale or fails.
4. Which needs can be met by existing APIs and which require a bounded API extension.
5. Any capability that would be removed, narrowed, hardcoded, or coupled to a specific plugin; flag it explicitly for product review instead of assuming visual redesign authorizes the change.

Useful code entry points, relative to `Phinix-Rework`:

- `Client/ClientExtensionAbstractions/UI/IClientTheme.cs`, `IClientThemePackages.cs`, and `ClientWidgets.cs`.
- `Client/ClientExtensionAbstractions/Framework/IClientExtensionPoints.cs`, `IClientApiDirectory.cs`, `IClientTaskService.cs`, and `IClientLifetimeService.cs`.
- `Client/Source/UI/UiTheme.cs`, `ThemeInputAdapter.cs`, `ThemeSettingsWindow.cs`, `ClientThemePackageService.cs`, and `ThemePackageExportWindow.cs`.
- `Client/Source/GUI/Windows and panels/ServerTab.cs` and the actual plugin views under `Extensions`.
- `Dependencies/Phinix.Common/Common/Utils/Framework/Themes` and Store's `LocalThemePackagesWindow.cs`.
- `docs/Design-Philosophy.md`, `docs/Compatibility-Boundaries.md`, and the latest entries in `docs/branch-local/dev/Theme-Package-T5-Implementation.md`, `Plugin-Api-Integration-Standard.md`, `Provider-and-Lifetime-Api-Implementation.md`, and `Theme-and-Plugin-Publishing-Flows.md`.
- The current `Phinix-Example-Plugin/example` source and `Api-Semantics-Example.md`, demonstrating public author APIs without private-UI reflection.

The desired outcome is a redesign assessment that preserves and improves theme usability and plugin integration capacity while clarifying presentation and user flows.


## Further clarification — Host-rendered plugin UI is part of the audit

The intended redesign includes rewriting appropriate parts of the Host UI layer and providing public rendering/composition capabilities for plugin UI. Assess an architecture in which plugins provide presentation descriptions, data snapshots, and user actions, and the Host handles reusable layout, controls, visual states, and rendering. This capability must be available equally to built-in and third-party plugins.

Business rules, validation authority, inventories/trade state, protocol handling, and authoritative operation outcomes remain with their owning plugins. Host rendering does not transfer ownership of those businesses into Host code. General presentation contracts must not require Host references to Chat/Trade/Inventory implementations or Contracts.

### G. Determine the scope of Host rendering responsibility

As part of the audit, classify the actual UI surfaces into:

1. Host shell responsibilities: navigation, page/container layout, sidebar placement, settings sections, window/dialog shells, overflow, and common state presentation.
2. Reusable plugin presentation: lists/rows, forms/fields, detail sections, cards, actions/toolbars/context menus, validation feedback, empty/loading/error states, and confirmation/selection interactions that the existing product actually needs.
3. Specialized content that may require an owned custom renderer or a constrained self-drawing region.
4. Business operations/state that remain outside the renderer.

For each surface, identify its current owner, entry point, rendering method, business dependencies, theme usage, and public integration contract. Distinguish rearranging the shell from taking responsibility for content inside plugin panels.

Current `IMainTabProvider.Draw(Rect)` and `IServerSidebarProvider.Draw(Rect)` let plugins imperatively draw their contents. Existing `ClientWidgets` are reusable immediate-mode helpers, and typed action points provide operation composition. These are foundations, but they are not an implemented general Host renderer of plugin-declared forms/lists/pages. A shell redesign cannot automatically interpret and restyle arbitrary existing Draw callbacks.

### H. Assess a public rendering/composition model

Compare realistic bounded approaches: shared theme-aware components, plugin-supplied presentation descriptors rendered by Host, and a combination with controlled custom drawing. Recommend a coherent model based on actual flows and Unity/Verse constraints. Do not assume a universal declarative UI language, a new general-purpose UI engine, or automatic conversion of existing plugin drawing code is necessary.

The assessment should explain:

- What a plugin provides and what the Host renders; minimum metadata and stable identities for pages, sections, rows, fields, and actions where required.
- How projected read-only presentation state and invalidation/version information reach the renderer without exposing mutable authoritative data or wire DTOs.
- How focus, selection, scrolling, input validation, pending operations, and errors are owned and retained across refresh/layout changes.
- How user intent reaches the owning plugin; render/measure/collect must not execute business actions, and callback completion must not be displayed as business confirmation.
- How theme snapshots, plugin-owned key registration, semantic roles, missing-key fallback, and hot application work within every supported rendering path.
- How third parties compose or contribute content/actions without editing Host or requiring private UI reflection.
- How renderer/provider callbacks are bound to module lifetimes, isolated on failure, and released when a view closes or an owner stops.
- How optional/missing renderers/providers and stale content degrade safely; how custom rendering preserves GUI state, layout boundaries, and the existing theme contract.
- How repeated layout/render passes avoid repeated registration, subscription, file access, business operations, and unnecessary per-frame allocations; how large lists can remain virtualized.

Distinguish interface-design gaps from missing Host implementations and actual plugin migration work. Public rendering functionality belongs in the audit and subsequent roadmap; implementing it is still outside this audit phase.

### I. Expand the audit deliverable

Include a Host/plugin rendering responsibility matrix, an assessment of existing immediate-mode providers versus the proposed public composition model, and a list of rendering/API gaps needed by the redesigned flows. Identify a small representative Host-rendered plugin flow suitable for a later Example experiment.

The outcome should support both a redesigned first-party UI and reusable rendering capabilities for independent plugin authors. Preserve existing theme capabilities and API openness in that assessment. Existing Draw entry points and adapters should be inventoried with concrete consumers and migration needs; neither immediate deletion nor permanent duplicate UI frameworks should be assumed.
