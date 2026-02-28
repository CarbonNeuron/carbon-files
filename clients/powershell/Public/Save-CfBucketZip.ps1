function Save-CfBucketZip {
    <#
    .SYNOPSIS
        Downloads a bucket's contents as a ZIP file.
    .PARAMETER Id
        The bucket ID.
    .PARAMETER OutPath
        Path to save the ZIP file. Defaults to "{Id}.zip" in current directory.
    .EXAMPLE
        Save-CfBucketZip -Id "abc1234567" -OutPath ./backup.zip
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory, Position = 0, ValueFromPipelineByPropertyName)]
        [string]$Id,

        [Parameter(Position = 1)]
        [string]$OutPath
    )

    process {
        if (-not $OutPath) { $OutPath = "$Id.zip" }
        Invoke-CfApiRequest -Method Get -Path "/api/buckets/$Id/zip" -OutFile $OutPath
        Get-Item $OutPath
    }
}
