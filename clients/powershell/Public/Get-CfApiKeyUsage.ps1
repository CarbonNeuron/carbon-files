function Get-CfApiKeyUsage {
    <#
    .SYNOPSIS
        Gets usage statistics for an API key.
    .PARAMETER Prefix
        The key prefix.
    .EXAMPLE
        Get-CfApiKeyUsage -Prefix "cf4_abc123"
    #>
    [CmdletBinding()]
    [OutputType([PSCustomObject])]
    param(
        [Parameter(Mandatory, Position = 0, ValueFromPipelineByPropertyName)]
        [string]$Prefix
    )

    process {
        Invoke-CfApiRequest -Method Get -Path "/api/keys/$Prefix/usage"
    }
}
