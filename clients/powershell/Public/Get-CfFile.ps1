function Get-CfFile {
    <#
    .SYNOPSIS
        Lists files in a bucket or gets a specific file's metadata.
    .PARAMETER BucketId
        The bucket ID.
    .PARAMETER Path
        Optional file path to get a specific file.
    .PARAMETER Limit
        Maximum number of results (default 50).
    .PARAMETER Offset
        Number of results to skip.
    .EXAMPLE
        Get-CfFile -BucketId "abc1234567"
    .EXAMPLE
        Get-CfFile -BucketId "abc1234567" -Path "docs/readme.txt"
    #>
    [CmdletBinding(DefaultParameterSetName = 'List')]
    [OutputType([PSCustomObject])]
    param(
        [Parameter(Mandatory, Position = 0, ValueFromPipelineByPropertyName)]
        [Alias('Id')]
        [string]$BucketId,

        [Parameter(Position = 1, ParameterSetName = 'ByPath')]
        [string]$Path,

        [Parameter(ParameterSetName = 'List')]
        [int]$Limit = 50,

        [Parameter(ParameterSetName = 'List')]
        [int]$Offset = 0
    )

    process {
        if ($Path) {
            $encodedPath = [uri]::EscapeDataString($Path)
            Invoke-CfApiRequest -Method Get -Path "/api/buckets/$BucketId/files/$encodedPath"
        }
        else {
            $query = @{
                limit  = $Limit.ToString()
                offset = $Offset.ToString()
            }
            $result = Invoke-CfApiRequest -Method Get -Path "/api/buckets/$BucketId/files" -Query $query
            if ($result.Items) { $result.Items } else { $result }
        }
    }
}
