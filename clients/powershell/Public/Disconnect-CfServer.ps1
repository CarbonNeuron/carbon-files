function Disconnect-CfServer {
    <#
    .SYNOPSIS
        Disconnects from the CarbonFiles server.
    .DESCRIPTION
        Clears the stored connection state (base URI and token).
    .EXAMPLE
        Disconnect-CfServer
    #>
    [CmdletBinding()]
    param()

    $script:CfConnection.BaseUri = $null
    $script:CfConnection.Token = $null
    $script:CfConnection.Headers = @{}

    Write-Verbose 'Disconnected from CarbonFiles server'
}
