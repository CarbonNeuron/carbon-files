function New-CfDashboardToken {
    <#
    .SYNOPSIS
        Creates a dashboard authentication token.
    .PARAMETER ExpiresIn
        Optional expiry (default/max: 24h).
    .EXAMPLE
        New-CfDashboardToken -ExpiresIn "12h"
    #>
    [CmdletBinding(SupportsShouldProcess)]
    [OutputType([PSCustomObject])]
    param(
        [Parameter()]
        [string]$ExpiresIn
    )

    process {
        $body = @{}
        if ($ExpiresIn) { $body.expires_in = $ExpiresIn }

        if ($PSCmdlet.ShouldProcess('Dashboard', 'Create dashboard token')) {
            Invoke-CfApiRequest -Method Post -Path '/api/tokens/dashboard' -Body $body
        }
    }
}
