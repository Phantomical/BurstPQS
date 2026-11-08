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

    # Downloads from GitHub fail intermittently. Successful ones stay in the
    # CKAN cache so each retry only needs to fetch what is left.
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        & $ckan install --gamedir "$installdir" --headless @extra --no-recommends -c $_.FullName
        if ($LASTEXITCODE -eq 0) { break }
    }
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
