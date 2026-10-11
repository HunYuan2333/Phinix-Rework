Using the UX/UI/theme audit you just completed, produce a concrete end-to-end redesign roadmap for Phinix Rework.

Do NOT start implementation yet.

The roadmap must cover:
- UX improvements
- Figma-based UI migration
- design-system/component work
- theme-loader evolution
- compatibility with existing themes
- plugin UI compatibility
- backward compatibility with old clients
- testing
- rollout strategy

Visual reference remains:

https://www.figma.com/design/Mqxq3scgyAjpLFXbGJv78n/Phinix-Rework-Redesign?node-id=1-4&t=FHRwtAkrEoAjF7ah-1

Important constraints:
- preserve current business logic where possible
- preserve protocol behavior unless a roadmap item explicitly requires server/protocol work
- preserve old-client compatibility
- preserve plugin architecture
- do not invent unsupported product features
- do not replace the existing theme loader with a parallel system
- new UI must integrate with the existing theme infrastructure
- implementation target is RimWorld / Unity IMGUI / Verse, not WebView

## Roadmap goals

Create a staged migration plan that minimizes regression risk.

Prefer incremental migration over a full rewrite.

The plan should distinguish between:
- visual-only changes
- UX changes
- client-logic changes
- plugin API changes
- theme-system changes
- server/protocol changes
- compatibility-sensitive changes

## Phase planning

Design a roadmap with clear phases.

At minimum, consider phases like:

### Phase 0 — Baseline / Safety
- freeze current behavior
- add screenshots/reference states
- identify regression-sensitive flows
- establish test checklist
- document current theme behavior
- document current plugin UI contracts

### Phase 1 — Design System Foundation
- map Figma tokens
- extend existing theme loader
- define fallbacks for old themes
- define spacing/radius/typography constants
- define reusable drawing helpers
- define reusable controls
- avoid changing product behavior

### Phase 2 — Core Chat UI Migration
- migrate main chat window
- message rows
- member panel
- member search/filter
- composer
- top navigation
- settings entry
- special message cards

### Phase 3 — Secondary Modules
- trade
- inventory
- store
- legacy red packet functionality
- plugin-hosted / plugin-provided UI areas

### Phase 4 — UX Improvements
Only include UX changes that can be made safely after the visual migration.

Separate:
- UI-only UX improvements
- client-logic UX improvements
- server/protocol-dependent UX improvements

### Phase 5 — Compatibility / Theme Migration
- test old themes
- migration/fallback behavior
- optional theme schema/versioning
- plugin compatibility
- old-client compatibility validation

### Phase 6 — Polish / Testing / Release
- scale testing
- localization
- Chinese text
- 1080p / 1440p / 4K
- performance
- hover/focus/disabled states
- long-message cases
- long usernames
- empty/loading/error states
- regression testing

You may adjust the phase structure if the audit suggests a better sequence.

## For each roadmap item

Include:
- objective
- scope
- affected modules/files/components
- dependencies
- risk level
- compatibility impact
- testing requirements
- whether it can be done independently
- whether it requires product decisions
- recommended order

Classify each item as:
- Low risk
- Medium risk
- High risk

Also classify by type:
- Visual
- UX
- Client logic
- Theme system
- Plugin system
- Protocol/server
- Compatibility
- Testing/infrastructure

## Theme Loader Migration Plan

Include a dedicated theme migration section.

Specify:
- which old tokens should remain
- which tokens should be added
- fallback rules for missing fields
- whether theme schema versioning is needed
- whether radius/spacing should remain fixed or become configurable
- how old themes remain functional
- how plugin-provided UI inherits theme values

Prefer:
backward-compatible extension > breaking schema rewrite.

Do not discard existing user themes unless there is a strong technical reason.

## Plugin Compatibility

Because Phinix is plugin-friendly, explain how the new UI design system should be exposed to plugin UI.

Consider:
- shared component API
- shared theme tokens
- layout helpers
- compatibility shims
- avoiding plugins hardcoding colors/styles
- keeping third-party plugin UI visually coherent with the new design

Do not propose a breaking plugin API unless necessary.

If a breaking API would be valuable, place it in a clearly separated future major-version section.

## UX Roadmap Separation

Very important:
Do not mix visual polish with behavioral redesign.

Separate UX work into:

1. UX changes that require no behavior change
2. UX changes that require client-side logic changes
3. UX changes that require plugin changes
4. UX changes that require server/protocol work
5. UX ideas that should remain future candidates only

Examples of future candidates should NOT automatically be scheduled for implementation.

## Output format

Produce:

1. Executive summary
2. Recommended migration strategy
3. Phase-by-phase roadmap
4. Dependency graph / ordering constraints
5. Risk matrix
6. Theme-loader migration plan
7. Plugin compatibility plan
8. UX roadmap
9. Testing strategy
10. Suggested milestone boundaries
11. What NOT to change yet
12. First implementation batch recommendation

End with a recommended first implementation batch that is small enough to review safely.

Do NOT modify code yet.

The purpose of this step is to agree on the plan before implementation begins.

---

## Additional ordering requirements — coordinate API work with the redesign

This section supplements the original prompt. This step still produces a roadmap only. Implementation must follow a separately agreed scope; do not treat roadmap generation as permission to modify code or publish releases.

Carry forward the theme and extensibility requirements from the audit prompt supplement. Preserve the current theme infrastructure, local theme package/export capabilities, plugin equality, and public extension capacity. Inspect the current local working tree and distinguish implemented APIs from proposals and unverified runtime behavior.

### 1. Establish the interaction architecture before specifying component changes

Use the audit to decide the proposed information architecture, navigation, principal user flows, entry points, and state presentation. Document those decisions before decomposing the visual migration.

Behavioral changes must remain explicitly classified and reviewed. However, do not postpone all UX reasoning until after rebuilding the visuals: discoverability, navigation, state presentation, and plugin-content placement influence the shell and component contracts from the beginning. Safe presentation improvements may accompany their corresponding module migration; server/protocol-dependent behavior remains separately scoped.

### 2. Produce a bounded API dependency assessment

For every proposed flow and extension surface, identify:

- The current public interface and actual implementation, if one exists.
- Any missing typed business context, query, operation/result contract, or contribution surface.
- Whether only the new view needs to consume an existing API.
- Whether a general Host contract must be extended or the contract belongs in the owning plugin's Contracts.
- Whether the missing capability blocks the next migration batch or can be added later.

Current foundations already include themes/registration/catalog/package services, shared widgets and responsive providers, typed action contributions, provider availability queries, main-thread task results, and module-owned lifetime scopes. These should be reused.

Actual Chat/Trade/Inventory contribution integration, richer detail/message/content contributions, and module file-resource/storage APIs are not all implemented. Do not assume that a generic action mechanism means every business view already exposes a usable point.

Do not require completion of every conceivable plugin API before UI work begins. Each migration batch needs its own sufficient, coherent contract baseline. Conversely, do not implement the whole UI privately and postpone public access until the end: third-party participation must be considered when defining the affected surface.

Example may demonstrate new API semantics without requiring a built-in consumer for every new contract. For a real UI integration, also identify the view/provider adapter that presents the contribution. Preserve actual-module ownership, borrowed API lifetimes, typed contexts, stale-context checks, and precise business outcomes.

### 3. Use this preferred dependency order, adjusting it with audit evidence

1. **Baseline and design decisions:** current behavior/state references, information architecture, desired user flows, plugin capability map, and theme/API preservation requirements.
2. **Contracts required by the next migration:** fill the bounded API gaps; define typed contexts/results and owner/lifecycle rules; map new visual roles into the existing theme registry and fallback model. Distinguish these prerequisites from general API work that can continue independently.
3. **Shared presentation foundations:** theme-aware drawing/control helpers, layout/typography constants, reusable state presentation, and component composition. Preserve current providers and document any proposed optional extensions/adapters.
4. **Neutral shell and one complete Chat slice:** navigation/sidebar/settings integration that accommodates plugin providers, plus a reviewable existing Chat flow from user intent through result/error presentation. Avoid replacing all modules before one complete flow can be assessed.
5. **Remaining modules, one vertical slice at a time:** Trade, Inventory, Store, maintained plugins, and other provider areas; complete each slice's styling, state handling, API access, and ownership cleanup together.
6. **Cross-module polish and verification:** localization, scale/resolution, long content, performance, optional/failed providers, themes, and lifecycle scenarios. Consolidate findings from earlier batches.

Prefer small vertical slices over either a full rewrite or a long sequence of visual-only components with no complete user flow. The first implementation batch should identify a concrete visible outcome, the exact prerequisite contracts, and what evidence is required to review it safely.

### 4. Make compatibility and extensibility continuous requirements

The later compatibility phase should consolidate verification; it must not be the first time compatibility is considered. Every affected batch should address:

- Existing user themes, missing keys, explicit inheritance, unknown-key preservation, plugin-defined keys, and hot application.
- Optional/missing/disabled plugin providers, contribution overflow, ordering, localization, and narrow-window access.
- Current public plugin contracts and both maintained plugins' internal 1.9 adapters.
- Existing persistent data/recovery readers and unchanged server/protocol behavior.

Legacy translation remains at binding/input/protocol/recovery boundaries. New views and ordinary business code use current contracts. Do not interpret old-client compatibility as permission to spread version/schema branches throughout the new UI, or assume new theme ZIP capabilities are available on old clients.

If navigation requires stable provider identifiers or additional presentation metadata, inspect the existing contracts and propose the smallest coherent extension. Do not identify a plugin by translated labels or hardcode a closed list of official modules. Explain how independent plugin authors can add content without changing Host implementation.

### 5. Specify coordination and handoff boundaries

Include a responsibility/dependency table for API infrastructure, theme/component work, Host shell, and business-plugin views. Make clear which batch owns each shared contract or theme-key change.

If API and redesign work are performed in separate sessions sharing the workspace, do not schedule simultaneous edits to the same abstractions, `ClientWidgets`, theme resolver, startup/service wiring, or module Contracts. API work that does not affect a migration surface can continue independently after checking file/dependency overlap. UI work should consume the agreed current contracts rather than create a duplicate service/registry/styling implementation.

Recommend a handoff after the roadmap identifies the next batch's API gaps and those prerequisites are implemented/documented. Do not wait for the entire UI redesign to finish before extending necessary APIs, and do not wait for an unlimited API expansion program before migrating the UI.

### 6. Keep online work deferred

T5 local theme ZIP export, installation/update/uninstall, discovery, and recovery post-processing have been implemented in code; game and crash-recovery verification remain outstanding. Account for these existing capabilities and their verification needs rather than schedule them as entirely new features.

Shortcut uploads, online theme admission/catalog changes, existing publication-flow changes, and new online releases are deferred together. A release-strategy section may document future gates and open decisions, but must not automatically schedule GitHub submissions, uploads, Index publication, or a new account/hosting service into the immediate implementation batches. Existing online features have not been disabled.

End the roadmap with a clearly bounded first batch and its API prerequisites, preserving the original requirement to agree on the plan before implementation.


## Further clarification — roadmap for Host rendering capabilities and plugin migration

The planned implementation must include public Host rendering/composition functionality for plugin UI, alongside the visual and UX redesign. Built-in and third-party plugins should be able to describe common presentation needs and use the same Host components/rendering path, while retaining ownership of business state, validation, protocols, and operation outcomes.

The audit must first determine which surfaces the Host should render and which specialized surfaces need controlled custom drawing. Use that responsibility matrix to scope the roadmap. Do not interpret this addition as implementation authorization during roadmap generation.

### 7. Include contracts and a minimal renderer in the API prerequisite stage

Revise the dependency assessment to cover a usable public presentation contract **and its Host rendering implementation**. A collection of interfaces or low-level queries alone is not a complete prerequisite for migrating plugin UI.

For the first selected flow, specify:

- The plugin-provided presentation model/component composition, typed context where needed, stable identities, read snapshots, and invalidation rules.
- The Host-owned controls/layout/rendering and the source of theme values.
- The binding from user intent to plugin-owned operations and their actual results.
- Ownership of view state, registrations, callbacks, and resources; close/stop/stale-context behavior.
- How current imperative Draw providers continue working during the bounded migration and which concrete consumers can subsequently move or be removed.
- A representative Example experiment that uses the public contract and the real minimal Host renderer, rather than drawing a second private implementation inside Example.

The minimum renderer should be narrowly scoped to the selected existing use case, enough to prove layout, state, theme, lifecycle, and operation semantics. Do not schedule a universal UI framework or every potential component before the first migration slice.

### 8. Recommended implementation handoffs

After the audit and roadmap are agreed, use this sequence:

1. **Architecture agreement:** decide Host/plugin responsibilities, the initial composition approach, required existing product surfaces, and the first reviewable flow.
2. **API and renderer proof:** implement/document the required public UI contracts, a minimal working Host renderer, and an Example using it. Verify preservation of theme registration/customization and third-party access. Keep the preserved old-client bindings at their adapters.
3. **Design-system and shell implementation:** evolve those agreed contracts/rendering foundations into the new theme-aware controls, layout and shared shell; review any further API changes before expanding migration.
4. **Real plugin migration:** migrate Chat and then the remaining selected modules in complete slices, using the same public presentation facilities. A business plugin may project its own data into general presentation models; Host does not compile against its business types.
5. **Consolidation:** assess actual migrated consumers before deleting redundant drawing paths or adapters, then consolidate compatibility, usability, performance, and lifecycle verification. Online publication remains deferred.

The prerequisite handoff is a sufficient contract baseline plus a functioning rendering example, not completion of all future APIs and not completion of the final visual system. Interface and implementation refinements may be needed as the first real flow is migrated; specify how they will be reviewed without creating competing public contracts.

### 9. Coordinate renderer ownership across sessions

If separate sessions implement API prerequisites and final UI work, assign one owner per shared rendering service, component contract, theme-key change, and startup registration. Treat the minimal renderer as a foundation that the redesign session extends; do not have each session create an independent framework.

Avoid simultaneous edits to shared abstractions, `ClientWidgets`, `UiTheme`, renderer registration/service wiring, or the same plugin Contracts. Record current interfaces, Example behavior, implemented rendering features, deferred surfaces, and known unverified cases at the handoff. Non-overlapping foundation work can proceed independently.

### 10. Preserve extensibility through the migration

The roadmap must explain how a third-party author can add a page/section, present common data/forms, contribute actions, use theme values, handle optional capabilities, and release the view through public facilities. Define only the surfaces needed by actual audited flows, with a consistent extension model for later additions.

A controlled custom-rendering path may be appropriate for specialized game content. Document its ownership, measurement/layout, theme use, failure isolation, and lifecycle requirements. Do not assume arbitrary old Draw callbacks can be automatically converted or fully controlled by the new renderer; evaluate explicit adapters/migration and retain only the necessary boundaries.

Apply compatibility, partial-theme fallback, plugin equality, and result-state correctness in each rendering/migration slice. Include these requirements in the responsibility table, dependency graph, first-batch recommendation, and review checklist. The intended result is a public rendering foundation that enables the redesigned UI and third-party integration together.
