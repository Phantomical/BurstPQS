# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## Unreleased

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