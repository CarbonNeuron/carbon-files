function Get-CfApiKey {
    <#
    .SYNOPSIS
        Lists all API keys.
    .PARAMETER Limit
        Maximum number of results (default 50).
    .PARAMETER Offset
        Number of results to skip.
    .EXAMPLE
        Get-CfApiKey
    #>
    [CmdletBinding()]
    [OutputType([PSCustomObject])]
    param(
        [Parameter()]
        [int]$Limit = 50,

        [Parameter()]
        [int]$Offset = 0
    )

    process {
        $query = @{
            limit  = $Limit.ToString()
            offset = $Offset.ToString()
        }
        $result = Invoke-CfApiRequest -Method Get -Path '/api/keys' -Query $query
        if ($result.Items) { $result.Items } else { $result }
    }
}
