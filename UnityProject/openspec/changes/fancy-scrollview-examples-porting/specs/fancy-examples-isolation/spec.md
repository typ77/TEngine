## ADDED Requirements

### Requirement: Examples SHALL be isolated UIWindow units
Each Example SHALL be implemented as an independent UIWindow unit with its own code, Prefabs, resources, data, Context when needed, and runtime state.

#### Scenario: One Example fails
- **WHEN** any single Example fails to open
- **THEN** other Examples SHALL remain directly openable
- **AND** FancyTestHomeUI SHALL remain interactive

#### Scenario: Direct open without HomeUI
- **WHEN** `GameModule.UI.ShowUIAsync<ExampleXX_NameUI>()` is called
- **THEN** the Example SHALL initialize its own data and display normally
- **AND** it SHALL NOT require UserData from FancyTestHomeUI

### Requirement: Examples SHALL comply with TEngine UI lifecycle
Each Example SHALL follow TEngine UIWindow/UIWidget lifecycle and resource conventions.

#### Scenario: Example UIWindow opens
- **WHEN** an Example UIWindow is opened through `GameModule.UI.ShowUIAsync<T>()`
- **THEN** it SHALL initialize through ScriptGenerator, RegisterEvent, OnCreate, and OnRefresh
- **AND** it SHALL clean up its own resources through OnDestroy

#### Scenario: Example UIWindow closes
- **WHEN** an Example UIWindow is closed
- **THEN** it SHALL release all event listeners, cancel pending async operations, and unload Example-specific resources

### Requirement: Example assets SHALL be centralized and prefixed
Each Example-specific asset SHALL be stored under `Assets/AssetRaw/UI/FancyScrollViewExamples/` and use an `ExampleXX_` prefix.

#### Scenario: Example-specific asset lookup
- **WHEN** a Window Prefab, Cell Prefab, Material, Shader, Texture, or Animation belongs to Example04
- **THEN** its asset name SHALL begin with `Example04_`
- **AND** it SHALL NOT be referenced by another Example as a required resource

#### Scenario: Asset naming convention
- **WHEN** a new Example is added
- **THEN** all its assets SHALL follow the `ExampleXX_` prefix convention
- **AND** assets SHALL be placed in the appropriate subdirectory under `FancyScrollViewExamples/`

### Requirement: Shared mutable Example state SHALL be forbidden
Shared infrastructure SHALL NOT store mutable runtime state for the current Example, current Context, current Items, current Cell, or current resource handles.

#### Scenario: Switching between Examples
- **WHEN** the user switches from Example02 to Example03
- **THEN** Example02 runtime state SHALL NOT be reused by Example03
- **AND** Example03 SHALL initialize its own runtime state

#### Scenario: HomeUI does not own Example state
- **WHEN** FancyTestHomeUI closes
- **THEN** it SHALL only close the currently open Example
- **AND** it SHALL NOT call CloseAll or manipulate other Example state