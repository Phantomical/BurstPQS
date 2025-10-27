# Contributing

## Bug Reports
If you are reporting a bug please make sure to include KSP.log and/or Player.log
along with your current mod list. For best results, follow the instructions at
[How to Get Support][0].

[0]: https://forum.kerbalspaceprogram.com/topic/163863-how-to-get-support/

## Installing Dependencies
Background Thrust has integrations with some other mods. Some of these conflict
so they cannot all be installed in the same installation. As such, there is a
script that installs these dependencies into a few fake CKAN installs that you
will need to run before you can build anything.

On Windows, run
```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\setup-deps.ps1 -ckan path\to\ckan.exe
```

On Linux/MacOS run
```bash
./setup-deps.sh
```

These must be run in the repository root. They will create some fake ckan
installs under `deps/installs` and install the relevant mods within.


## Building
In order to build the mod you will need:
- the `dotnet` CLI

Next, you will want to create a `BurstPQS.props.user` file
in the repository root, like this one:
```xml
<?xml version="1.0" encoding="UTF-8"?>

<Project ToolsVersion="Current" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
    <PropertyGroup>
        <ReferencePath>$KSP_ROOT_PATH</ReferencePath>
    </PropertyGroup>
</Project>
```

Make sure to replace `$KSP_ROOT_PATH` with the path to your KSP installation.
If you have an install made via steam then you might be able to skip this step.

Finally, you can build by running either:
- `dotnet build` (for a debug build), or,
- `dotnet build -c Release` (for a release build)

This will create a `GameData\BurstPQS` folder which you can then drop
into your KSP install's `GameData` folder.

> ### Linking the output into your `GameData` folder
> If you're iterating on patches/code/whatever then you'll find that manually
> copying stuff into the `GameData` folder will get old really quickly. You can
> instead create a junction (on windows) or a symlink (on mac/linux) so that
> KSP will just look into the build artifact directory.
>
> To do this you will need to run the following command in an admin `cmd.exe`
> prompt (for windows) in your `GameData` directory:
> ```batch
> mklink /j BurstPQS C:\path\to\BurstPQS\repo\GameData\BurstPQS
> ```
>
> On Linux or MacOS you should be able to accomplish the same thing using `ln`.

## Formatting
We use `csharpier` to format C# code. You can first install it by running
```sh
dotnet tool restore
```

Once you have done that you can then format everything by running
```sh
dotnet csharpier format .
```
