function Set-MsixIdentity {
    param(
        [xml]$Manifest,
        [ValidateSet('Test', 'Store')][string]$Mode,
        [string]$ProductVersion,
        [object]$StoreIdentity
    )
    if ($ProductVersion -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') {
        throw 'MSIX requires a stable MAJOR.MINOR.PATCH product version.'
    }
    foreach ($part in $ProductVersion.Split('.')) {
        if ([long]$part -gt 65535) { throw 'MSIX version components must be at most 65535.' }
    }
    if ($ProductVersion.Split('.')[0] -eq '0' -and $ProductVersion -eq '0.0.0') {
        throw 'MSIX version must be greater than 0.0.0.0.'
    }
    $Manifest.Package.Identity.Version = "$ProductVersion.0"
    if ($Mode -eq 'Store') {
        foreach ($property in @('Name', 'Publisher', 'PublisherDisplayName')) {
            $value = if ($null -ne $StoreIdentity) { [string]$StoreIdentity.$property } else { '' }
            if ([string]::IsNullOrWhiteSpace($value) -or $value -match 'REPLACE_WITH|Local[ .]?Test' -or $value -ne $value.Trim()) {
                throw "Provide the reserved Partner Center $property; test/example identities cannot be submitted."
            }
        }
        if ($StoreIdentity.Name -notmatch '^[A-Za-z0-9][A-Za-z0-9.-]{2,49}$' -or $StoreIdentity.Name -match '\.\.') {
            throw 'Invalid Store package identity Name.'
        }
        if ($StoreIdentity.Publisher -notmatch '^CN=') { throw 'Use the exact Partner Center Publisher (CN=...).' }
        $Manifest.Package.Identity.Name = [string]$StoreIdentity.Name
        $Manifest.Package.Identity.Publisher = [string]$StoreIdentity.Publisher
        $Manifest.Package.Properties.PublisherDisplayName = [string]$StoreIdentity.PublisherDisplayName
        $Manifest.Package.Properties.DisplayName = 'JTC Stamper'
        $Manifest.Package.Applications.Application.VisualElements.DisplayName = 'JTC Stamper'
    }
    return $Manifest
}
