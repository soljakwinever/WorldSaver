# Item Action System

## Purpose

Item actions are reusable behavior assets. Each `ItemData` stores the values
that make the behavior specific to that item.

This prevents shared actions from holding mutable or item-specific state.

## Asset structure

An item has:

- `action`: the reusable `ItemAction` behavior.
- `actionData`: only the polymorphic records required by that behavior.

Current records:

| Record | Consumer | Values |
| --- | --- | --- |
| `PlaceTileItemActionData` | `PlaceTileItemAction` | Tile and layer |
| `ToolHotbarActionData` | `ToolHotbarAction` | Tool |
| `MineTileToolActionData` | `MineTileToolAction` | Layer and mineable tags |
| `MineCoverageToolActionData` | `MineCoverageToolAction` | Normalized removal amount |
| `PlacePersistentNodeItemActionData` | `PlacePersistentNodeItemAction` | Node |

A tool item can contain multiple records. A pickaxe, for example, needs both
`ToolHotbarActionData` and `MineTileToolActionData`.

## Runtime flow

1. Assigning an item to the hotbar creates an `ItemActionBinding`.
2. The binding keeps the reusable action paired with its `ItemData`.
3. The binding inserts that item into `ActionContext`.
4. The action calls `ItemData.TryGetActionData<T>()` for its configuration.
5. `PlayerInteractionController` consumes one item only after a successful
   action when `ConsumesItem` is enabled.

Tool actions receive the same item through `ToolActionContext`. `ToolData`
checks its actions in order and runs the first one that accepts the context.
Tools without action assets use focused-object interaction.

`DestroyNodeToolAction` can filter targets by explicit `NodeData` assets,
`EntityTag` assets, or both. The two lists use OR matching. When both are
empty, every removable node is eligible.

`MineCoverageToolAction` removes the configured amount from the visible,
highest-priority coverage on the targeted tile. Add
`MineCoverageToolActionData` to the tool's item and set `amount` in normalized
coverage units.

## Persistent node placement

`PlacePersistentNodeItemAction`:

- snaps the target to a world cell;
- requires a loaded, restored chunk;
- rejects a cell containing another node collider;
- resolves the configured `NodeData` to an `EntityArchetype`;
- spawns and registers a runtime-persistent entity.

The node must have an `EntityArchetype` in `WorldData.runtimeEntityArchetypes`
or in a Resources folder. Save data stores the archetype ID, so `NodeData`
without an archetype cannot be placed persistently.

## Adding an item action

1. Add a serializable `ItemActionData` subtype containing only required values.
2. Add an `ItemAction` subtype.
3. Read the record with `context.Item.TryGetActionData<T>()`.
4. Keep the action asset stateless.
5. Add the action and its data record to each applicable `ItemData`.

For a tool action, read item-specific settings from `ToolActionContext.Item`.
Order the action on `ToolData` according to dispatch priority.

## Important rules

- Never cache an `ItemData` on an action asset.
- Treat action assets as shared and stateless.
- Return `false` when required action data is missing.
- Let the interaction controller handle item consumption.
- Register persistent node archetypes before using them in placement items.
