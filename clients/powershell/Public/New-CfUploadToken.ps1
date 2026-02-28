function New-CfUploadToken {
    <#
    .SYNOPSIS
        Creates an upload token for a bucket.
    .PARAMETER BucketId
        The bucket ID.
    .PARAMETER ExpiresIn
        Optional expiry (e.g., "1h", "1d").
    .PARAMETER MaxUploads
        Optional maximum number of uploads allowed.
    .EXAMPLE
        New-CfUploadToken -BucketId "abc1234567" -ExpiresIn "24h" -MaxUploads 10
    #>
    [CmdletBinding(SupportsShouldProcess)]
    [OutputType([PSCustomObject])]
    param(
        [Parameter(Mandatory, Position = 0, ValueFromPipelineByPropertyName)]
        [Alias('Id')]
        [string]$BucketId,

        [Parameter()]
        [string]$ExpiresIn,

        [Parameter()]
        [int]$MaxUploads
    )

    process {
        $body = @{}
        if ($ExpiresIn) { $body.expires_in = $ExpiresIn }
        if ($MaxUploads -gt 0) { $body.max_uploads = $MaxUploads }

        if ($PSCmdlet.ShouldProcess($BucketId, 'Create upload token')) {
            Invoke-CfApiRequest -Method Post -Path "/api/buckets/$BucketId/tokens" -Body $body
        }
    }
}
