param([Parameter(Mandatory)][string]$Server,[string]$LinuxRuntime='')
$ErrorActionPreference='Stop'
if($Server -notmatch '^[A-Za-z0-9_.@-]+$' -or $Server.StartsWith('-')){throw 'Use a configured SSH host or user@host.'}
$root=Split-Path $PSScriptRoot -Parent
if(!$LinuxRuntime){$LinuxRuntime=Join-Path $root 'tools/dotnet-runtime-linux-x64.tar.gz'}
if(!(Test-Path -LiteralPath $LinuxRuntime)){throw 'Supply a portable Linux .NET 10 runtime archive; this script never installs server packages.'}
$work=Join-Path $root ('work/server-payload-'+[Guid]::NewGuid().ToString('N'))
$payload=Join-Path $work 'payload'
New-Item -ItemType Directory -Path (Join-Path $payload 'tests'),(Join-Path $payload 'app') -Force | Out-Null
Copy-Item $LinuxRuntime (Join-Path $payload 'runtime.tar.gz')
foreach($file in @('ZenithClipper.Tests.dll','ZenithClipper.Tests.deps.json','ZenithClipper.Tests.runtimeconfig.json','ZenithClipper.Core.dll')){Copy-Item (Join-Path $root "tests/ZenithClipper.Tests/bin/Debug/net10.0/$file") (Join-Path $payload 'tests')}
Copy-Item (Join-Path $root 'build/linux/*') (Join-Path $payload 'app') -Recurse
# Core IL is platform-neutral; use the same newest release library as the Windows package.
Copy-Item (Join-Path $root 'build/windows/ZenithClipper.Core.dll') (Join-Path $payload 'app/ZenithClipper.Core.dll') -Force
Copy-Item (Join-Path $PSScriptRoot 'server-check.sh') (Join-Path $payload 'run.sh')
$archive=Join-Path $work 'payload.tar'
& tar -cf $archive -C $payload .
if($LASTEXITCODE){throw 'Could not prepare test payload.'}
$sshOptions=@('-o','BatchMode=yes','-o','StrictHostKeyChecking=yes','-o','ConnectTimeout=10')
$remoteFolder=''
try{
    $remoteFolder=(& ssh @sshOptions $Server 'mktemp -d /tmp/zenithclipper-test.XXXXXXXX').Trim()
    if($LASTEXITCODE -or $remoteFolder -notmatch '^/tmp/zenithclipper-test\.[A-Za-z0-9]+$'){throw 'Could not create a safe disposable server folder.'}
    Write-Host "Disposable server folder: $remoteFolder"
    & scp @sshOptions $archive "${Server}:$remoteFolder/payload.tar"
    if($LASTEXITCODE){throw 'Upload failed.'}
    $remoteCommand="tar -xf '$remoteFolder/payload.tar' -C '$remoteFolder' --no-same-owner --no-same-permissions && bash '$remoteFolder/run.sh' '$remoteFolder'"
    & ssh @sshOptions $Server $remoteCommand
    if($LASTEXITCODE){throw 'Linux checks failed. The server cleanup still runs.'}
}finally{
    if($remoteFolder -match '^/tmp/zenithclipper-test\.[A-Za-z0-9]+$'){
        $cleanup="if [ -d '$remoteFolder' ] && [ ! -L '$remoteFolder' ] && [ -O '$remoteFolder' ]; then rm -rf -- '$remoteFolder'; fi; test ! -e '$remoteFolder' && printf 'VERIFIED_REMOVED $remoteFolder\n'"
        & ssh @sshOptions $Server $cleanup
        if($LASTEXITCODE){Write-Error "Could not verify server cleanup: $remoteFolder"}
    }
    $safeBase=[IO.Path]::GetFullPath((Join-Path $root 'work'))+[IO.Path]::DirectorySeparatorChar
    if([IO.Path]::GetFullPath($work).StartsWith($safeBase,[StringComparison]::OrdinalIgnoreCase)){Remove-Item -LiteralPath $work -Recurse -Force}
}
