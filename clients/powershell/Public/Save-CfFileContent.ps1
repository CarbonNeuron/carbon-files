function Save-CfFileContent {
    <#
    .SYNOPSIS
        Downloads a file's content from a bucket.
    .PARAMETER BucketId
        The bucket ID.
    .PARAMETER Path
        The file path within the bucket.
    .PARAMETER OutPath
        Local path to save the file. Defaults to the filename in current directory.
    .EXAMPLE
        Save-CfFileContent -BucketId "abc1234567" -Path "report.pdf" -OutPath ./report.pdf
    .EXAMPLE
        Get-CfFile -BucketId "abc1234567" | Save-CfFileContent -OutPath ./downloads/
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory, Position = 0, ValueFromPipelineByPropertyName)]
        [Alias('Id')]
        [string]$BucketId,

        [Parameter(Mandatory, Position = 1, ValueFromPipelineByPropertyName)]
        [string]$Path,

        [Parameter(Position = 2)]
        [string]$OutPath
    )

    process {
        $fileName = [System.IO.Path]::GetFileName($Path)
        if (-not $OutPath) {
            $OutPath = $fileName
        }
        elseif (Test-Path $OutPath -PathType Container) {
            $OutPath = Join-Path $OutPath $fileName
        }

        $encodedPath = [uri]::EscapeDataString($Path)
        Invoke-CfApiRequest -Method Get -Path "/api/buckets/$BucketId/files/$encodedPath/content" -OutFile $OutPath
        Get-Item $OutPath
    }
}
