$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot '..\..\src\DumpToTxt.Lite\DumpToTxt.ps1'
$tokens = $null; $errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($source, [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw $errors[0] }
# Load only the two functions under test. Never execute the script's GUI/dump entry point.
foreach ($name in @('Get-ConfigPaths', 'Save-Settings')) {
    $function = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name }, $true)
    . ([scriptblock]::Create($function.Extent.Text))
}
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('dtt-lite-config-' + [guid]::NewGuid().ToString('N'))
$previousAppData = $env:APPDATA
$previousProgramData = $env:PROGRAMDATA
try {
    $env:APPDATA = $fixture
    $env:PROGRAMDATA = $fixture
    $paths = Get-ConfigPaths
    [IO.Directory]::CreateDirectory($paths.UserDir) | Out-Null
    [IO.File]::WriteAllText($paths.User, '{"Style":"Json","SensitiveValuePatterns":["private"],"future":{"enabled":true},"ExtSet":[".cs"]}')
    Save-Settings ([pscustomobject]@{ ExtSet=@('.md'); DotFilesAllow=@('.env'); ExcludeRegex='(?!)' })
    $back = Get-Content -LiteralPath $paths.User -Raw | ConvertFrom-Json
    if ($back.Style -ne 'Json' -or $back.SensitiveValuePatterns[0] -ne 'private' -or -not $back.future.enabled -or $back.ExtSet[0] -ne '.md') {
        throw 'Lite rewrote settings outside its three owned fields.'
    }
    Write-Host 'PASS: Lite preserves v2 safety and unknown settings while editing its own fields.'
} finally {
    $env:APPDATA = $previousAppData
    $env:PROGRAMDATA = $previousProgramData
    $resolvedFixture = [IO.Path]::GetFullPath($fixture)
    if (-not $resolvedFixture.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe fixture cleanup path.' }
    if (Test-Path -LiteralPath $resolvedFixture) { Remove-Item -LiteralPath $resolvedFixture -Recurse -Force }
}
