function Send-CfFile {
    <#
    .SYNOPSIS
        Uploads a file to a bucket.
    .PARAMETER BucketId
        The bucket ID.
    .PARAMETER FilePath
        Path to the local file to upload.
    .PARAMETER DestinationPath
        Optional destination path in the bucket. Defaults to the filename.
    .PARAMETER Token
        Optional upload token (cfu_* prefix) instead of API key auth.
    .EXAMPLE
        Send-CfFile -BucketId "abc1234567" -FilePath ./report.pdf
    .EXAMPLE
        Send-CfFile -BucketId "abc1234567" -FilePath ./data.csv -DestinationPath "reports/data.csv"
    #>
    [CmdletBinding(SupportsShouldProcess)]
    [OutputType([PSCustomObject])]
    param(
        [Parameter(Mandatory, Position = 0, ValueFromPipelineByPropertyName)]
        [Alias('Id')]
        [string]$BucketId,

        [Parameter(Mandatory, Position = 1)]
        [ValidateScript({ Test-Path $_ -PathType Leaf })]
        [string]$FilePath,

        [Parameter()]
        [string]$DestinationPath,

        [Parameter()]
        [string]$Token
    )

    process {
        $file = Get-Item $FilePath
        $fileName = if ($DestinationPath) { $DestinationPath } else { $file.Name }

        if ($PSCmdlet.ShouldProcess("$fileName -> $BucketId", 'Upload file')) {
            $uri = "$($script:CfConnection.BaseUri)/api/buckets/$BucketId/upload/stream"
            $query = "filename=$([uri]::EscapeDataString($fileName))"
            if ($Token) { $query += "&token=$([uri]::EscapeDataString($Token))" }
            $uri += "?$query"

            $headers = $script:CfConnection.Headers.Clone()
            $headers['Content-Type'] = 'application/octet-stream'

            $fileStream = [System.IO.File]::OpenRead($file.FullName)
            try {
                $response = Invoke-RestMethod -Method Put -Uri $uri -Headers $headers -Body $fileStream
                if ($response) { ConvertTo-PascalCaseKeys $response }
            }
            finally {
                $fileStream.Dispose()
            }
        }
    }
}
