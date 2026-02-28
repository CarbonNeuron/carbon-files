function Remove-CfBucket {
    <#
    .SYNOPSIS
        Deletes a bucket and all its files.
    .PARAMETER Id
        The bucket ID to delete.
    .EXAMPLE
        Remove-CfBucket -Id "abc1234567"
    .EXAMPLE
        Get-CfBucket | Where-Object Name -like "temp*" | Remove-CfBucket
    #>
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
    param(
        [Parameter(Mandatory, Position = 0, ValueFromPipelineByPropertyName)]
        [string]$Id
    )

    process {
        if ($PSCmdlet.ShouldProcess($Id, 'Delete bucket')) {
            Invoke-CfApiRequest -Method Delete -Path "/api/buckets/$Id"
        }
    }
}
