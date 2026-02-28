function New-CfBucket {
    <#
    .SYNOPSIS
        Creates a new bucket.
    .PARAMETER Name
        The name of the bucket.
    .PARAMETER Description
        Optional description.
    .PARAMETER ExpiresIn
        Optional expiry (e.g., "1h", "1d", "1w", "30d", or ISO 8601).
    .EXAMPLE
        New-CfBucket -Name "my-bucket" -ExpiresIn "7d"
    #>
    [CmdletBinding(SupportsShouldProcess)]
    [OutputType([PSCustomObject])]
    param(
        [Parameter(Mandatory, Position = 0)]
        [ValidateNotNullOrEmpty()]
        [string]$Name,

        [Parameter()]
        [string]$Description,

        [Parameter()]
        [string]$ExpiresIn
    )

    process {
        $body = @{ name = $Name }
        if ($Description) { $body.description = $Description }
        if ($ExpiresIn) { $body.expires_in = $ExpiresIn }

        if ($PSCmdlet.ShouldProcess($Name, 'Create bucket')) {
            Invoke-CfApiRequest -Method Post -Path '/api/buckets' -Body $body
        }
    }
}
