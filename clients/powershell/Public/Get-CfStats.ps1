function Get-CfStats {
    <#
    .SYNOPSIS
        Gets system statistics (admin only).
    .EXAMPLE
        Get-CfStats
    #>
    [CmdletBinding()]
    [OutputType([PSCustomObject])]
    param()

    process {
        Invoke-CfApiRequest -Method Get -Path '/api/stats'
    }
}
