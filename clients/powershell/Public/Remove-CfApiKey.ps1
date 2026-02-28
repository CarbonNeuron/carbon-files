function Remove-CfApiKey {
    <#
    .SYNOPSIS
        Revokes an API key.
    .PARAMETER Prefix
        The key prefix (e.g., "cf4_abc123").
    .EXAMPLE
        Remove-CfApiKey -Prefix "cf4_abc123"
    #>
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
    param(
        [Parameter(Mandatory, Position = 0, ValueFromPipelineByPropertyName)]
        [string]$Prefix
    )

    process {
        if ($PSCmdlet.ShouldProcess($Prefix, 'Revoke API key')) {
            Invoke-CfApiRequest -Method Delete -Path "/api/keys/$Prefix"
        }
    }
}
