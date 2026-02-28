function Update-CfBucket {
    <#
    .SYNOPSIS
        Updates a bucket's properties.
    .PARAMETER Id
        The bucket ID.
    .PARAMETER Name
        New name for the bucket.
    .PARAMETER Description
        New description for the bucket.
    .PARAMETER ExpiresIn
        New expiry duration or datetime.
    .EXAMPLE
        Update-CfBucket -Id "abc1234567" -Name "new-name"
    #>
    [CmdletBinding(SupportsShouldProcess)]
    [OutputType([PSCustomObject])]
    param(
        [Parameter(Mandatory, Position = 0, ValueFromPipelineByPropertyName)]
        [string]$Id,

        [Parameter()]
        [string]$Name,

        [Parameter()]
        [string]$Description,

        [Parameter()]
        [string]$ExpiresIn
    )

    process {
        $body = @{}
        if ($Name) { $body.name = $Name }
        if ($Description) { $body.description = $Description }
        if ($ExpiresIn) { $body.expires_in = $ExpiresIn }

        if ($body.Count -eq 0) {
            Write-Warning 'No properties specified to update.'
            return
        }

        if ($PSCmdlet.ShouldProcess($Id, 'Update bucket')) {
            Invoke-CfApiRequest -Method Patch -Path "/api/buckets/$Id" -Body $body
        }
    }
}
