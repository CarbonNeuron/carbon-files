function Invoke-CfApiRequest {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [Microsoft.PowerShell.Commands.WebRequestMethod]$Method,

        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter()]
        [hashtable]$Body,

        [Parameter()]
        [hashtable]$Query,

        [Parameter()]
        [hashtable]$ExtraHeaders,

        [Parameter()]
        [string]$OutFile,

        [Parameter()]
        [string]$ContentType,

        [Parameter()]
        [object]$RawBody
    )

    if (-not $script:CfConnection.BaseUri) {
        throw 'Not connected. Run Connect-CfServer first.'
    }

    $uri = "$($script:CfConnection.BaseUri.TrimEnd('/'))$Path"

    # Append query parameters
    if ($Query -and $Query.Count -gt 0) {
        $pairs = $Query.GetEnumerator() | Where-Object { $null -ne $_.Value } |
            ForEach-Object { "$([uri]::EscapeDataString($_.Key))=$([uri]::EscapeDataString($_.Value))" }
        if ($pairs) {
            $uri += "?$($pairs -join '&')"
        }
    }

    $params = @{
        Method  = $Method
        Uri     = $uri
        Headers = $script:CfConnection.Headers.Clone()
    }

    if ($ExtraHeaders) {
        foreach ($key in $ExtraHeaders.Keys) {
            $params.Headers[$key] = $ExtraHeaders[$key]
        }
    }

    if ($Body) {
        $params.Body = $Body | ConvertTo-Json -Depth 10
        $params.ContentType = 'application/json'
    }

    if ($RawBody) {
        $params.Body = $RawBody
        if ($ContentType) { $params.ContentType = $ContentType }
    }

    if ($OutFile) {
        $params.OutFile = $OutFile
    }

    try {
        $response = Invoke-RestMethod @params -ErrorAction Stop
        if ($response -is [System.Management.Automation.PSObject]) {
            ConvertTo-PascalCaseKeys $response
        }
        else {
            $response
        }
    }
    catch {
        $err = $_.ErrorDetails.Message | ConvertFrom-Json -ErrorAction SilentlyContinue
        if ($err.error) {
            $msg = $err.error
            if ($err.hint) { $msg += " (Hint: $($err.hint))" }
            $ex = [System.InvalidOperationException]::new($msg)
            $record = [System.Management.Automation.ErrorRecord]::new(
                $ex, 'CarbonFilesApiError', 'InvalidOperation', $uri)
            $PSCmdlet.ThrowTerminatingError($record)
        }
        else {
            throw
        }
    }
}
