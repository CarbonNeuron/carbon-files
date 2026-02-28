function New-CfApiKey {
    <#
    .SYNOPSIS
        Creates a new API key.
    .PARAMETER Name
        A descriptive name for the key.
    .EXAMPLE
        New-CfApiKey -Name "CI/CD Pipeline"
    #>
    [CmdletBinding(SupportsShouldProcess)]
    [OutputType([PSCustomObject])]
    param(
        [Parameter(Mandatory, Position = 0)]
        [ValidateNotNullOrEmpty()]
        [string]$Name
    )

    process {
        if ($PSCmdlet.ShouldProcess($Name, 'Create API key')) {
            Invoke-CfApiRequest -Method Post -Path '/api/keys' -Body @{ name = $Name }
        }
    }
}
