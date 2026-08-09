# Legacy compatibility contract

## Immutable inputs

| Input | SHA-256 |
| --- | --- |
| `war3_macro_gui.ahk` | `135F6071CC715B3DCAB5577D507B7D0590ED32F8D439EF5833EA3669C12B29AD` |
| `war3_npc_macro.ahk` | `60763D6F95FCCC82385A43C31C2E89FB793E0CC275949B49DC7E2C75638B9357` |

The complete GUI script is the product behavior oracle. The smaller NPC script remains a historical template only.

## Current release behavior

- Five fixed NPC definitions and seven fixed farm tasks.
- Eight configurable flows with eight ordered groups per flow.
- Twelve skill slots and six item slots for platform mappings.
- Seven named skill release profiles (`Q技能`, `W技能`, `E技能`, `R技能`, `D技能`, `F技能`, `B技能`) and two named item profiles (`装备1`, `装备2`).
- Task startup and release selection are independent in every flow group; either side can be left at `无`.
- Per-flow key, skill, hero-select, NPC-click, chat, teleport, NPC-mouse, and release-mouse delays.
- Group pre-actions: none, key, or public chat.
- NPC screen-coordinate capture only; F6 records the selected NPC and Down cycles NPCs.
- Legacy release fields are read for one-time migration into named profiles and are not exposed by the current UI.
- Configurable stop hotkey; one tap stops and pauses, two taps within 350 ms resume.
- Game-window gating, manual binding, clipboard window diagnostics, and skip-window-check mode.
- UTF-8 INI and hero profile import/export without changing the legacy source files.

## Non-goals for the parity release

- Game memory, `Game.dll`, world-coordinate projection, skill cooldown overlays, injection, or image recognition.
- Behavior changes disguised as refactoring.
