function Connect-CfServer {
    <#
    .SYNOPSIS
        Connects to a CarbonFiles server.
    .DESCRIPTION
        Sets the base URI and authentication token for subsequent CarbonFiles cmdlet calls.
    .PARAMETER Uri
        The base URI of the CarbonFiles server (e.g., https://files.example.com).
    .PARAMETER Token
        The Bearer token for authentication (admin key, API key cf4_*, or dashboard JWT).
    .EXAMPLE
        Connect-CfServer -Uri "https://files.example.com" -Token "cf4_myapikey"
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory, Position = 0)]
        [ValidateNotNullOrEmpty()]
        [uri]$Uri,

        [Parameter(Mandatory, Position = 1)]
        [ValidateNotNullOrEmpty()]
        [string]$Token
    )

    $script:CfConnection.BaseUri = $Uri.ToString().TrimEnd('/')
    $script:CfConnection.Token = $Token
    $script:CfConnection.Headers = @{
        Authorization = "Bearer $Token"
    }

    Write-Verbose "Connected to $($script:CfConnection.BaseUri)"
}
