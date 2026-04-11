# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## Unreleased

## v0.1.17
### Changed
* BurstPQS.dll now always has an AssemblyVersion of 1.0.0.0.
  KSPBurst needs the assembly version to match otherwise it won't be able
  to find the compiled methods.

### Addedd
* The Alt+F12 debug menu now has a texture exporter window.

## v0.1.16
### Fixed
* Fixed an occasional crash when building a PQS quad.

## v0.1.15
### Fixed
* Fixed an occasional null reference exception when switching scenes.
* Fixed a typo in the "burst compilation not enabled" error window.
* Fixed a bug where low-resolution PQS quads would not be properly made
  invisible when switching scenes.

## v0.1.14
### Added
* Added support for Kerbal Konstructs.

## v0.1.13
### Added
* Added support for VertexMitchellNetravaliHeightMap
* Added support for VertexHeightOblateAdvanced

### Fixed
* Fixed a bug where the terrain would be corrupted after a scene switch under
  certain conditions.

## v0.1.12
### Fixed
* Fixed a bug introduced when collecting errors for unsupported planets.

## v0.1.11
### Added
* Added a tooltip to the debug window that will show why a planet is in fallback mode.

### Changed
* Optimized BurstAnimationCurve.

## v0.1.10
### Fixed
* Fixed a bug where `FlattenArea` PQSMods would sometimes not apply when setting
  the quad height, but would otherwise work.
* Fixed a bug where the terrain height would be wrong at the very centre of
  the area for a `FlattenArea` PQSMod.

## v0.1.9
### Fixed
* Fix harmony.log.txt from appearing on the desktop.

## v0.1.8
### Fixed
* Control locks are now properly released when closing the warning popup window.
* Fixed an issue where BurstPQS was breaking burst-compilation in KSPBurst.

## v0.1.7
### Added
* Added a BurstPQS entry to the Alt+F12 that shows useful info for debugging.

### Fixed
* Fixed an issue causing parallax scatters to not show up when a planet is in
  fallback mode.
* Fallback mode is now a lot closer to stock and will now spread out PQS work
  across multiple frames, just like stock does.

## v0.1.6
### Fixed
* Actually include the guard fix that was supposed to be in v0.1.4.

## v0.1.5
### Added
* Added a warning notification in the main menu if burst compiled methods are
  not available.

## v0.1.4
### Fixed
* BurstPQS now validates whether configured MapSOs are supported before
  activating for a planet.
* Fixed a case where BurstPQS wasn't properly guarding against burst-compiled
  function pointers not being available.
* Silence useless burst compiler warnings about exceptions being thrown.

## v0.1.3
### Fixed
* Some PQSMods were not burst-compiled. All of these cases have been fixed.

### Changed
* Added a some more documentation for public API items.

## v0.1.2
### Changed
* Turns out MM was actually necessary.

## v0.1.1
### Changed
* BurstPQS no longer depends on ModuleManager.

## v0.1.0
This is the first release of BurstPQS.