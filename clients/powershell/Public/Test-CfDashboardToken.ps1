function Test-CfDashboardToken {
    <#
    .SYNOPSIS
        Validates the current dashboard token.
    .EXAMPLE
        Test-CfDashboardToken
    #>
    [CmdletBinding()]
    [OutputType([PSCustomObject])]
    param()

    process {
        Invoke-CfApiRequest -Method Get -Path '/api/tokens/dashboard/me'
    }
}
