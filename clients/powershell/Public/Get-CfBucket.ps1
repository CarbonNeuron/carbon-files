function Get-CfBucket {
    <#
    .SYNOPSIS
        Gets one or more buckets.
    .PARAMETER Id
        Get a specific bucket by ID. If omitted, lists all buckets.
    .PARAMETER Limit
        Maximum number of results (default 50).
    .PARAMETER Offset
        Number of results to skip.
    .PARAMETER IncludeExpired
        Include expired buckets in the list.
    .EXAMPLE
        Get-CfBucket
    .EXAMPLE
        Get-CfBucket -Id "abc1234567"
    #>
    [CmdletBinding(DefaultParameterSetName = 'List')]
    [OutputType([PSCustomObject])]
    param(
        [Parameter(Mandatory, Position = 0, ParameterSetName = 'ById',
                   ValueFromPipelineByPropertyName)]
        [string]$Id,

        [Parameter(ParameterSetName = 'List')]
        [int]$Limit = 50,

        [Parameter(ParameterSetName = 'List')]
        [int]$Offset = 0,

        [Parameter(ParameterSetName = 'List')]
        [switch]$IncludeExpired
    )

    process {
        if ($PSCmdlet.ParameterSetName -eq 'ById') {
            Invoke-CfApiRequest -Method Get -Path "/api/buckets/$Id"
        }
        else {
            $query = @{
                limit  = $Limit.ToString()
                offset = $Offset.ToString()
            }
            if ($IncludeExpired) { $query.include_expired = 'true' }
            $result = Invoke-CfApiRequest -Method Get -Path '/api/buckets' -Query $query
            if ($result.Items) { $result.Items } else { $result }
        }
    }
}
