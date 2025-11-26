# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2025-01-25

### Added

- Initial release for SPT 4.0.x
- **Snapshot System**: Press F9 (configurable) in-raid to capture current equipment
- **Server-Side Restoration**: Automatic inventory restoration on death without Run-Through penalty
- **Per-Map Snapshots**: One snapshot per map, new snapshots replace previous ones
- **Full Inventory Capture**:
  - All equipment slots (head, armor, vest, backpack, weapons, etc.)
  - Secure container and contents
  - Pockets and contents
  - Nested items (items inside containers, magazines in weapons)
  - Item properties (durability, ammo counts, foldable states)
  - Magazine ammunition with correct slot mapping
- **Modded Items Support**: Works with any modded items using template IDs
- **Visual Notifications**: Large colored on-screen notifications:
  - Green: Snapshot saved successfully
  - Red: Errors or failures
  - Blue: Restoration confirmation
  - Yellow: Informational messages
- **BepInEx Configuration**: Configurable via F12 Configuration Manager:
  - Snapshot keybind
  - Include/exclude secure container
  - Include/exclude pockets
  - Enable/disable notifications

### Technical Details

- Client component: BepInEx plugin (.NET Framework 4.7.1)
- Server component: SPT mod (.NET 9.0)
- Uses SPT's dependency injection system
- Thread-safe state management for concurrent request handling
- Harmony patching via SPT.Reflection.Patching

### Compatibility

- SPT 4.0.x (tested on 4.0.6)
- BepInEx 5.x (included with SPT)
