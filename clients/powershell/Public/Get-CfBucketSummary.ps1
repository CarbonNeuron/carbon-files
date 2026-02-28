function Get-CfBucketSummary {
    <#
    .SYNOPSIS
        Gets a text summary of a bucket.
    .PARAMETER Id
        The bucket ID.
    .EXAMPLE
        Get-CfBucketSummary -Id "abc1234567"
    #>
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory, Position = 0, ValueFromPipelineByPropertyName)]
        [string]$Id
    )

    process {
        Invoke-CfApiRequest -Method Get -Path "/api/buckets/$Id/summary"
    }
}
