# Attribution and data provenance

## CrescentCompass

The plugin structure and inventory interoperability were informed by the adjacent CrescentCompass project, licensed AGPL-3.0-or-later. `tools/UiPreview/SoftwareRenderer.cs` is copied from that project. Its license is preserved as `LICENSE.txt`.

Project: https://github.com/wenwen-ff14/Occult_Crescent_guide_TC

## FFXIV Teamcraft

Copyright (c) 2017 Flavien Normand. MIT license, reproduced in `Teamcraft-LICENSE`.

The catalog uses item IDs, English names, instance metadata, and item-to-instance loot associations from commit `f43013b5f535e5b54bcb6f6f2fc4f646ea77696f` of https://github.com/ffxiv-teamcraft/ffxiv-teamcraft.

## FFXIV Consolegameswiki

Supplemental factual item-to-vault relationships were transcribed from the Possible/Potential Rewards sections of the ten treasure-dungeon pages. No article prose, icons, or screenshots are bundled. The pages' contributors are credited; exact URLs and revision IDs are listed in `docs/catalog-sources.json` in the source package. These relationships were normalized to game item IDs, merged with Teamcraft and deduplicated. No license grant for the original articles is implied by this project's code license.

Site: https://ffxiv.consolegameswiki.com/

The G10 outdoor Loot section of Timeworn Gazelleskin Map is also included as factual item IDs, scoped separately from all treasure dungeons.

## Huiji FFXIV wiki

The G10 outdoor table (all three columns) and the three Uznair instance data tables were manually transcribed as factual item IDs from publicly indexed pages on 2026-10-14. Exact source URLs, category groups and scope IDs are retained in `docs/g10-supplement.json`; the raw website returned HTTP 403, so these are indexed snapshots rather than revision exports. Credit to the Huiji wiki data contributors, including 旋风3721. No article prose or images are distributed.

## FF14ハウジング攻略レポ

Factual item names followed by explicit drop quantities were matched to item IDs in the author's firsthand patch 4.05 Canals runs (articles 4721, 4744, 4759 at https://ff14house.com/). These records apply only to the Lost Canals. Sources found only in those historical logs are identified in the interface and audit; retired main-stat materia are excluded. Source URLs and download hashes are recorded in `docs/catalog-sources.json`. No original article prose or images are distributed.

## Pandora's Box

The item action -> inventory context menu -> Decipher callback sequence in `MapDecipherService.cs` was adapted from `PandorasBox/Features/Actions/HotbarMapDecipher.cs`, commit `850ba0a3a97ec3788602a18f774f80696ae762ee` of https://github.com/PunishXIV/PandorasBox. Copyright (c) 2023, Puni.sh; BSD 3-Clause license, reproduced in `PandorasBox-LICENSE`. TreasureKeeperGlobal adds its own request state machine, localized prompt and dialog identity checks, inventory checks, map browser and empty-slot hotbar placement.

Source: https://github.com/PunishXIV/PandorasBox/blob/850ba0a3a97ec3788602a18f774f80696ae762ee/PandorasBox/Features/Actions/HotbarMapDecipher.cs

## FINAL FANTASY XIV

The 2026-10-15 community observation snapshot was shared with the user's authorization. It contains item IDs and the treasure-dungeon scope only, with no player identity, exact acquisition timestamps, or personal discard settings. The source is identified as `Community-observed-2026-10-15` and is not claimed to be a complete server reward table.

Traditional Chinese names, category IDs, territory IDs, and item availability were verified using the user's installed game data. FINAL FANTASY XIV and its game data belong to SQUARE ENIX. No game client DLLs, SDK binaries, or standalone game textures are redistributed.
