# NPC Wanted System – GTA V Script

This script extends GTA V with an **NPC-based wanted system**.  
Cops no longer react only to the player, but also to **conflicts between NPCs** and will dynamically call for backup.

---

## Features

- Monitors the game world for fights between cops and NPCs
- Automatically detects an active **incident** near the player
- Spawns additional police units depending on the situation:
  - City patrol cars (LSPD)
  - Sheriff vehicles in the county
  - SWAT units in a FIB SUV
  - Police helicopter (Police Maverick) at higher escalation
- Multiple escalation stages:
  - small incident → few patrol cars
  - bigger incident / dead cops → more backup
  - heavy incident → SWAT and helicopters
- The player’s wanted level remains unchanged – the system only controls NPC police behavior

---

Make sure you have:

- **Grand Theft Auto V** (PC)
- **ScriptHookV**
- **ScriptHookVDotNet** (any compatible 3.x version)
- A working **scripts** folder in your GTA V root directory

## Installation

Place the script file in your GTA V scripts folder:

```text
Grand Theft Auto V\
  scripts\
    npcwanted.cs
