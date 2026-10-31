# 编译 Mod 并安装到 BepInEx\plugins
# 用法：.\build.ps1 [-GameDir "游戏目录"]
param(
    [string]$GameDir = "E:\SteamLibrary\steamapps\common\romestead"
)

$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
$game = $GameDir
$plugins = Join-Path $game "BepInEx\plugins"

if (-not (Test-Path (Join-Path $game "BepInEx\core\BepInEx.Core.dll"))) {
    throw "游戏目录里没有安装 BepInEx，请先安装 BepInExPack for Romestead。"
}

dotnet build (Join-Path $root "RomesteadRadar\RomesteadRadar.csproj") -c Release -o (Join-Path $root "out") -p:GameDir="$game" -p:BepInExCore="$game\BepInEx\core"
if ($LASTEXITCODE -ne 0) { throw "编译失败，未安装插件。" }

New-Item -ItemType Directory -Force -Path $plugins | Out-Null
Copy-Item (Join-Path $root "out\RomesteadRadar.dll") $plugins -Force

Write-Host ""
Write-Host "编译并安装完成。"
Write-Host "  插件：$(Join-Path $plugins 'RomesteadRadar.dll')"
Write-Host "  配置：$(Join-Path $game "BepInEx\config\com.Nohtaehp.Romestead.Radar.cfg")"
Write-Host "  日志：$(Join-Path $game 'BepInEx\LogOutput.log')"
Write-Host "直接启动游戏即可（BepInEx 通过 d3d11.dll 自动加载）。"
