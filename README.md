<div align="center">
  <img src="https://pan.samyyc.dev/s/VYmMXE" />
  <h2><strong>InfiniteAmmo</strong></h2>
  <h3>Controls infinite reserve and chamber ammo per player flag.</h3>
</div>

<p align="center">
  <img src="https://img.shields.io/badge/build-passing-brightgreen" alt="Build Status">
  <img src="https://img.shields.io/github/downloads/Shmitzas/InfiniteAmmo/total" alt="Downloads">
  <img src="https://img.shields.io/github/stars/Shmitzas/InfiniteAmmo?style=flat&logo=github" alt="Stars">
  <img src="https://img.shields.io/github/license/Shmitzas/InfiniteAmmo" alt="License">
</p>

## Features

- **Infinite chamber ammo** — restores `Clip1` to max after every shot so the weapon never runs dry mid-magazine.
- **Infinite reserve ammo** — replenishes reserve ammo (`ReserveAmmo[0]`) whenever a player reloads, preventing an empty-magazine situation.
- Both features can be **enabled independently** and optionally **restricted to a permission flag**, so you can grant them only to VIPs, admins, or any custom group.

## Configuration

The plugin is configured via `addons/swiftlys2/configs/InfiniteAmmo/config.jsonc`.

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `DetailedLogging` | `bool` | `false` | Enable verbose logging (gates `LogInformation`/`LogDebug`). |
| `InfiniteReserveAmmo` | `bool` | `true` | Enable infinite reserve ammo globally. |
| `InfiniteReserveAmmoFlag` | `string` | `""` | Permission flag required. Empty = everyone. |
| `InfiniteChamberAmmo` | `bool` | `false` | Enable infinite chamber ammo globally. |
| `InfiniteChamberAmmoFlag` | `string` | `""` | Permission flag required. Empty = everyone. |

### Example

```jsonc
{
    "InfiniteAmmo": {
        "DetailedLogging": false,
        "InfiniteReserveAmmo": true,
        "InfiniteReserveAmmoFlag": "",
        "InfiniteChamberAmmo": false,
        "InfiniteChamberAmmoFlag": "infiniteammo.chamber"
    }
}
```

## Building

```bash
dotnet publish -c Release
```

Output is placed in `build/publish/InfiniteAmmo/`.
