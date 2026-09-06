# 一键构建：dotnet publish -> 复制 exe -> Inno Setup 编译安装包
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

Push-Location "$root\src\MiSwitcher"
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
if ($LASTEXITCODE -ne 0) { Pop-Location; exit $LASTEXITCODE }
Pop-Location

New-Item -ItemType Directory -Force "$root\publish" | Out-Null
Copy-Item "$root\src\MiSwitcher\bin\Release\net9.0-windows\win-x64\publish\米家三合一切换器.exe" "$root\publish\" -Force

$iscc = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
& $iscc "$root\installer.iss"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ''
Write-Host "安装包输出：$root\安装包"
