# Product versions use SemVer without build metadata. The SDK appends the Git revision.
function Test-ReleaseVersion {
    param([AllowNull()][AllowEmptyString()][string]$Version)
    $number = '(?:0|[1-9][0-9]*)'
    $identifier = '(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)'
    $pattern = '\A' + $number + '\.' + $number + '\.' + $number + '(?:-' + $identifier + '(?:\.' + $identifier + ')*)?\z'
    return $Version -cmatch $pattern
}
