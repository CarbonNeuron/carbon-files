function Test-CfHealth {
    <#
    .SYNOPSIS
        Checks the server health status.
    .EXAMPLE
        Test-CfHealth
    #>
    [CmdletBinding()]
    [OutputType([PSCustomObject])]
    param()

    process {
        Invoke-CfApiRequest -Method Get -Path '/healthz'
    }
}
