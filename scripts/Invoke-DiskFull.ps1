[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = Split-Path $PSScriptRoot -Parent
$reportRoot = Join-Path $repo 'test-results/disk-full'
$standardExe = Join-Path $repo 'dist/win-x64/JTCStamper.App.exe'
if (-not (Test-Path -LiteralPath $standardExe)) { throw 'Build distribution packages before the disk-full test.' }
New-Item -ItemType Directory -Force $reportRoot | Out-Null
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$isAdmin = ([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    @{ Status = 'blocked'; Reason = 'An elevated Windows process is required to create and attach the disposable VHD.' } |
        ConvertTo-Json | Set-Content (Join-Path $reportRoot 'setup.json') -Encoding utf8
    throw 'Disk-full VHD test requires an elevated process; no disk was created or filled.'
}
$token = [guid]::NewGuid().ToString('N')
$label = 'JTCFULL_' + $token.Substring(0,8)
$hostRoot = Join-Path ([IO.Path]::GetTempPath()) ('jtc-full-' + $token)
$vhd = Join-Path $hostRoot 'test.vhd'
$hostDrive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($hostRoot))
if ($hostDrive.AvailableFreeSpace -lt 1GB) { throw 'At least 1 GiB of host free space is required.' }
New-Item -ItemType Directory $hostRoot | Out-Null
$attached = $false
$detached = $false
$testExit = $null
Push-Location $repo
try {
    dotnet build tests/JTCStamper.DiskFull -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Disk-full runner build failed.' }
    # Only a new file under our unique temporary folder can be selected.
    if (Test-Path -LiteralPath $vhd) { throw 'Refusing an existing VHD.' }
    $commands = @(
        ('create vdisk file="' + $vhd + '" maximum=256 type=fixed'),
        ('select vdisk file="' + $vhd + '"'),
        'attach vdisk',
        'exit'
    )
    $commandFile = Join-Path $hostRoot 'create.txt'
    if ($vhd -match '[^\x00-\x7F]') { throw 'DiskPart test path must be ASCII; use an ASCII TEMP path.' }
    $commands | Set-Content $commandFile -Encoding ascii
    diskpart /s $commandFile | Out-File (Join-Path $reportRoot 'setup-diskpart.txt')
    if ($LASTEXITCODE -ne 0) { throw 'Creating or attaching the dedicated VHD failed.' }
    $image = Get-DiskImage -ImagePath $vhd
    $attached = $image.Attached
    if (-not $attached) { throw 'Test VHD is not attached.' }
    # Resolve the disk from the exact file we just created. Never accept a caller-supplied disk number.
    $disk = $image | Get-Disk
    if (@($disk).Count -ne 1 -or $disk.IsBoot -or $disk.IsSystem -or $disk.Size -ne 256MB -or $disk.PartitionStyle -ne 'RAW') {
        throw 'Refusing to initialize a disk that does not match the new blank test VHD.'
    }
    $disk | Initialize-Disk -PartitionStyle GPT
    $partition = New-Partition -DiskNumber $disk.Number -UseMaximumSize -AssignDriveLetter
    $volume = $partition | Format-Volume -FileSystem NTFS -NewFileSystemLabel $label -Confirm:$false -Force
    if ($volume.FileSystemLabel -ne $label -or $volume.Size -gt 256MB -or $volume.Size -lt 32MB -or -not $volume.DriveLetter) {
        throw 'Unexpected test volume identity or size.'
    }
    $driveRoot = [string]$volume.DriveLetter + ':\'
    [IO.File]::WriteAllText((Join-Path $driveRoot '.jtc-full-volume'), $token)
    @{ Status = 'ready'; VhdBytes = 256MB; VolumeBytes = $volume.Size; FileSystem = $volume.FileSystem;
        VolumeLabel = $label; HostFreeBytesBefore = $hostDrive.AvailableFreeSpace; Elevated = $isAdmin } |
        ConvertTo-Json | Set-Content (Join-Path $reportRoot 'setup.json') -Encoding utf8
    dotnet run --project tests/JTCStamper.DiskFull -c Release --no-build -- $driveRoot $reportRoot $token $standardExe
    $testExit = $LASTEXITCODE
    if ($testExit -ne 0) { throw "Disk-full checks failed: exit $testExit" }
}
finally {
    try {
        # Also check after a partial attach failure. Never detach a physical disk or another VHD.
        if (Test-Path -LiteralPath $vhd) {
            $image = Get-DiskImage -ImagePath $vhd
            if ($image.Attached) { Dismount-DiskImage -ImagePath $vhd }
            $detached = -not (Get-DiskImage -ImagePath $vhd).Attached
            if (-not $detached) { throw 'Test VHD remains attached; retaining its file for cleanup.' }
            Remove-Item -LiteralPath $vhd
        }
    }
    finally {
        @{ Detached = $detached; VhdFileRemoved = -not (Test-Path -LiteralPath $vhd); TestExitCode = $testExit } |
            ConvertTo-Json | Set-Content (Join-Path $reportRoot 'cleanup.json') -Encoding utf8
        Pop-Location
    }
}
