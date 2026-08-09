# Legacy compatibility contract

## Immutable inputs

| Input | SHA-256 |
| --- | --- |
| `war3_macro_gui.ahk` | `135F6071CC715B3DCAB5577D507B7D0590ED32F8D439EF5833EA3669C12B29AD` |
| `war3_npc_macro.ahk` | `60763D6F95FCCC82385A43C31C2E89FB793E0CC275949B49DC7E2C75638B9357` |

The complete GUI script is the product behavior oracle. The smaller NPC script remains a historical template only.

## Required behavior

- Five fixed NPC definitions and seven fixed farm actions.
- Eight configurable flows with eight ordered groups per flow.
- Twelve skill slots and six item slots.
- Per-flow key, skill, hero-select, NPC-click, chat, teleport, NPC-mouse, and release-mouse delays.
- Group pre-actions: none, key, or public chat.
- Release modes: none, skill key, item key, skill slot, or item slot.
- Screen-coordinate NPC and release targeting.
- F5 captures the selected farm target; F6 captures the selected NPC. Up/Down cycle the two selections, leaving F7/F8 available for user bindings.
- Configurable stop hotkey; one tap stops and pauses, two taps within 350 ms resume.
- Game-window gating, manual binding, clipboard window diagnostics, and skip-window-check mode.
- UTF-8 INI and hero profile import/export without changing the legacy source files.

## Non-goals for the parity release

- Game memory, `Game.dll`, world-coordinate projection, skill cooldown overlays, injection, or image recognition.
- Behavior changes disguised as refactoring.
