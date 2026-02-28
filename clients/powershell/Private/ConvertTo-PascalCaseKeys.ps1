function ConvertTo-PascalCaseKeys {
    [CmdletBinding()]
    [OutputType([PSCustomObject])]
    param(
        [Parameter(Mandatory, ValueFromPipeline)]
        [object]$InputObject
    )

    process {
        if ($InputObject -is [System.Collections.IList]) {
            $InputObject | ForEach-Object { ConvertTo-PascalCaseKeys $_ }
            return
        }

        if ($InputObject -isnot [System.Management.Automation.PSObject]) {
            return $InputObject
        }

        $result = [ordered]@{}
        foreach ($prop in $InputObject.PSObject.Properties) {
            $pascalName = ($prop.Name -split '_' | ForEach-Object {
                if ($_) { $_.Substring(0,1).ToUpper() + $_.Substring(1) }
            }) -join ''

            $value = if ($prop.Value -is [System.Management.Automation.PSObject] -or
                         $prop.Value -is [System.Collections.IList]) {
                ConvertTo-PascalCaseKeys $prop.Value
            } else {
                $prop.Value
            }
            $result[$pascalName] = $value
        }
        [PSCustomObject]$result
    }
}
