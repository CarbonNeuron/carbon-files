function Remove-CfFile {
    <#
    .SYNOPSIS
        Deletes a file from a bucket.
    .PARAMETER BucketId
        The bucket ID.
    .PARAMETER Path
        The file path within the bucket.
    .EXAMPLE
        Remove-CfFile -BucketId "abc1234567" -Path "docs/readme.txt"
    #>
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
    param(
        [Parameter(Mandatory, Position = 0, ValueFromPipelineByPropertyName)]
        [Alias('Id')]
        [string]$BucketId,

        [Parameter(Mandatory, Position = 1, ValueFromPipelineByPropertyName)]
        [string]$Path
    )

    process {
        $encodedPath = [uri]::EscapeDataString($Path)
        if ($PSCmdlet.ShouldProcess("$BucketId/$Path", 'Delete file')) {
            Invoke-CfApiRequest -Method Delete -Path "/api/buckets/$BucketId/files/$encodedPath"
        }
    }
}
