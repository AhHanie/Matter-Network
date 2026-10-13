# Matter Network

Matter Network is a Rimworld mod that adds an end-game digital storage network. Build a central controller, connect disk drives and access interfaces, then store colony items as data instead of spreading them across large stockpiles.

Steam Workshop: <https://steamcommunity.com/sharedfiles/filedetails/?id=3707570286>

## Features

- Networked item storage through controllers, disk drives, and access interfaces.
- Expandable matter disks from `1k` through `256k` tiers.
- Shared storage filters across connected network interfaces.
- Matter IO ports for automated import and export with adjacent storage.
- Network capacitors and reserve cells for backup power during outages.
- Fuel relays that refill nearby machines from connected network storage.
- Research progression for matter digitization, IO, power storage, and refueling.

## Installation

### Steam Workshop

Subscribe on the Steam Workshop page, enable Harmony, then enable Matter Network in RimWorld's mod list.

### Manual Install

Place this repository or release package in your RimWorld `Mods` directory:

```text
RimWorld/Mods/Matter Network/
```

The playable mod content is organized under `About/`, `1.6/`, `1.7/`, `Languages/`, and `Textures/`. RimWorld loads the version folder that matches the running game plus the shared root content (`Languages/`, `Textures/`).

## Basic Usage

1. Research **matter digitization**.
2. Build one **network controller** for a connected network.
3. Connect buildings with **network cable**.
4. Add **disk drives** and insert matter disks for capacity.
5. Use **network interfaces** to let pawns upload and retrieve stored items.
6. Add IO ports, capacitors, reserve cells, or fuel relays as the colony expands.

## Development

The C# project is `1.7/Source/Matter Network.csproj`, an SDK-style `net10.0` project. It requires the .NET 10 SDK, a RimWorld 1.7 installation, and a Harmony 3 `0Harmony.dll`.

```powershell
dotnet build "1.7/Source/Matter Network.csproj" -c Release -p:RimWorldDir="<RimWorld install folder>" -p:HarmonyDll="<path to Harmony 3 0Harmony.dll>"
```

Build output is written to `1.7/Assemblies/Matter Network.dll`.

## Repository Layout

- `About/` - mod metadata, preview image, Workshop tags, and published file ID.
- `1.6/Defs/` - XML defs for buildings, items, research, and categories.
- `1.6/Patches/` - XML mod compatibility patches.
- `1.6/Source/`, `1.7/Source/` - C# source, Harmony patches, UI, network logic, and project files for each RimWorld version.
- `1.7/Defs/`, `1.7/Patches/` - the 1.7 copies of the XML defs and patches.
- `Languages/` - English and Russian translations.
- `Textures/` - PNG sprites and UI icons.

## License

This project is licensed under the terms in `LICENSE`.
