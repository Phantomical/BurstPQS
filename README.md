# BurstPQS
BurstPQS is a mod for Kerbal Space Program that makes terrain generation faster.
It aims to eliminate most, if not all, stutter caused by terrain generation while
also allowing planet mod authors to crank up both the detail and the amount of
noise used to make the planet.

As a player, all you need to do to get the benefits is install the mod. Any
planets that aren't supported will automatically fall back to stock so you
generally shouldn't have to worry about compatibility.

## How does it work?
Two main things:
* It builds multiple quads making up the terrain in parallel.
* It uses KSPBurst to compile the actual work of building the quads, which makes
  things ~6x faster all on its own.

It also makes a whole bunch of optimizations to other parts of the PQS code,
add up to make regular no-change frame updates about twice as fast.

## Dependencies
* [ModuleManager](https://forum.kerbalspaceprogram.com/index.php?/topic/50533-*)
* [HarmonyKSP](https://github.com/KSPModdingLibs/HarmonyKSP)
* [Kopernicus](https://github.com/Kopernicus/Kopernicus)
* [KSPTextureLoader](https://github.com/Phantomical/KSPTextureLoader)
* [KSPBurst](https://github.com/KSPModdingLibs/KSPBurst)

## Documentation
Are you an author of a mod that has custom PQSMods and you would like it to be
compatible with BurstPQS? Head on over to [the wiki][wiki] and check out the
guides there.

[wiki]: https://github.com/Phantomical/BurstPQS/wiki

## License
BurstPQS is available under the MIT license.
