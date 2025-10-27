param (
    [string] $ckan="ckan.exe",
    [switch] $asroot
)

if (Test-Path -Path .\deps\installs) {
    Remove-Item -Path .\deps\installs -Recurse -Force
}

$extra = @()
if ($asroot) {
    $extra += "--asroot"
}

Get-ChildItem .\deps -Filter *.ckan | Foreach-Object {
    $name = $_.Basename
    $installdir = ".\deps\installs\$name"

    & $ckan instance fake "BRP-$name" $installdir 1.12.5 @extra --game KSP --MakingHistory 1.9.1 --BreakingGround 1.7.1
    & $ckan instance forget "BRP-$name" @extra

    & $ckan update  --gamedir "$installdir" --headless @extra
    & $ckan install --gamedir "$installdir" --headless @extra --no-recommends -c $_.FullName
}
