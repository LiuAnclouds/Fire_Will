param(
    [Parameter(Mandatory = $true)][int]$ProcessId,
    [Parameter(Mandatory = $true)][string]$OutputPath
)

# Run elevated when Warcraft was launched as administrator.

$ErrorActionPreference = 'Stop'
$modules = Get-Process -Id $ProcessId -Module | ForEach-Object {
    [pscustomobject]@{
        name = $_.ModuleName
        path = $_.FileName
        base = '0x{0:X}' -f $_.BaseAddress.ToInt64()
        size = $_.ModuleMemorySize
    }
}
$modules | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
