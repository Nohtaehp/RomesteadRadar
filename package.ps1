# 一键编译 + 安装到 BepInEx\plugins + 生成 Nexus/Vortex 兼容 zip
# 用法：.\package.ps1 [-OutputDir "dist"] [-GameDir "游戏目录"]
# 版本号只在仓库根目录的 version.txt 里维护
param(
    [string]$OutputDir = "dist",
    [string]$GameDir = "E:\SteamLibrary\steamapps\common\romestead"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$Version = (Get-Content (Join-Path $root "version.txt") -Raw).Trim()
if ([string]::IsNullOrWhiteSpace($Version)) {
    throw "version.txt 里没有版本号。"
}
$game = $GameDir
$plugins = Join-Path $game "BepInEx\plugins"
$dll = Join-Path $root "out\RomesteadRadar.dll"

# 1. 编译并安装到插件目录（build.ps1 内部会检查 BepInEx 并复制 DLL）
& (Join-Path $root "build.ps1") -GameDir $game
if (-not (Test-Path $dll)) {
    throw "没有找到编译产物：$dll"
}

# 2. 准备打包目录：DLL 放在 zip 根目录。
#    Vortex 的 BepInEx 安装器会把根目录的 .dll 自动部署到 BepInEx\plugins。
$dist = Join-Path $root $OutputDir
$staging = Join-Path $dist "_staging"
New-Item -ItemType Directory -Force -Path $dist | Out-Null
if (Test-Path $staging) {
    Remove-Item $staging -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $staging | Out-Null

Copy-Item $dll (Join-Path $staging "RomesteadRadar.dll") -Force

# 压缩包内的 README 直接复用仓库根目录 README.md，保证与 GitHub 上显示的内容一致。
# 显式按 UTF-8 读取并写出带 BOM 的 UTF-8，避免 PowerShell 默认编码把中文读成乱码。
$readmePath = Join-Path $root "README.md"
$readme = [System.IO.File]::ReadAllText($readmePath, [System.Text.Encoding]::UTF8)
$readmeOut = Join-Path $staging "README.txt"
[System.IO.File]::WriteAllText($readmeOut, $readme, (New-Object System.Text.UTF8Encoding($true)))

# 3. 压缩。注意压缩 staging 里的内容，而不是 staging 文件夹本身，避免 zip 里多套一层目录。
$zip = Join-Path $dist ("RomesteadRadar-v{0}.zip" -f $Version)
if (Test-Path $zip) {
    Remove-Item $zip -Force
}
Compress-Archive -Path (Join-Path $staging "*") -DestinationPath $zip -CompressionLevel Optimal
Remove-Item $staging -Recurse -Force

$hash = (Get-FileHash $zip -Algorithm SHA256).Hash
Write-Host ""
Write-Host "打包完成："
Write-Host "  zip   ：$zip"
Write-Host "  插件  ：$(Join-Path $plugins 'RomesteadRadar.dll')"
Write-Host "  SHA256：$hash"
Write-Host ""
