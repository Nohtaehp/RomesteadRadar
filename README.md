# Romestead Radar

Note: This README is bilingual. The Chinese section is provided below the English section.

A client-side BepInEx plugin for Romestead that adds a directional radar to the in-game minimap.

[English](#english) | [中文](#中文)

## English

### Description

Romestead Radar adds a fan-shaped directional pulse to the minimap. Select an item you have offered at an altar, a creature you have killed, or an unopened chest, then keep moving; the pulse points toward the nearest valid target.

It only shows direction, not exact positions. It works only in the outdoor overworld and searches only chunks that are already loaded. It is a navigation aid, not an ESP or map-reveal mod.

### Features

- **Item radar** — track items offered at an altar and point toward the nearest harvestable source.
- **Creature radar** — track creatures you have killed and point toward the nearest living individual. Kill records are stored per save file.
- **Chest radar** — unlocked after offering a chest at the altar; points toward the nearest unopened chest and excludes town chests and player-placed chests.
- Directional pulse appears only while moving; no permanent minimap marker.
- Outdoor only; hidden indoors and in dungeons.
- Loaded chunks only; does not reveal unloaded terrain.
- Item and creature names follow the game's current language; UI text is Chinese for Chinese, otherwise English.
- Optional Mod Settings Menu integration with an "Enable Radar" toggle.
- Client-side only; does not modify game saves.

### Installation

1. Install BepInExPack Romestead (BepInEx 6 CoreCLR for Romestead).
2. Close Romestead.
3. Copy `RomesteadRadar.dll` into `BepInEx\plugins\`.
4. Launch the game. A magnifier button appears in the top bar.
5. On first launch, `BepInEx\config\com.Nohtaehp.Romestead.Radar.cfg` is created.
6. Optional: install Mod Settings Menu to get the in-game toggle.

### Requirements

- Romestead `0.26.2.4`
- BepInExPack Romestead `0.4.1` (BepInEx 6 CoreCLR)
- Optional: Mod Settings Menu (`IceBoxStudio.Romestead.ModSettingsMenu`)
- Multiplayer: each player who wants the radar must install it on their own client; players without it are unaffected.
- Building from source: .NET 8 SDK and the game assemblies.

### Build from source

```powershell
.\build.ps1
```

Builds `RomesteadRadar/RomesteadRadar.csproj` and installs `RomesteadRadar.dll` to `BepInEx\plugins`. The default game path is `E:\SteamLibrary\steamapps\common\romestead`; override it with `.\build.ps1 -GameDir "your game path"`.

### Packaging

```powershell
.\package.ps1
```

Creates `dist\RomesteadRadar-v<version>.zip` containing the DLL and this README.

### Version

The version is maintained only in `version.txt`, in the format `game-mod`, for example `0.26.2.4-1.0.0`. `package.ps1` keeps the plugin version, assembly version, and zip filename in sync.

> BepInEx plugin metadata only accepts a three-part SemVer, so the build uses `0.26.2-1.0.0`; the assembly version remains `0.26.2.4`.

### Uninstall

Delete `BepInEx\plugins\RomesteadRadar.dll`. Optionally delete `BepInEx\config\com.Nohtaehp.Romestead.Radar.cfg` to clear kill records. The plugin does not write to game saves.

### Project structure

| Path | Description |
|---|---|
| `RomesteadRadar/` | C# source and project file |
| `version.txt` | Single source of version numbers |
| `build.ps1` | Build and install to `BepInEx\plugins` |
| `package.ps1` | Build, install, and create the release zip |

### Credits

Thanks to the people whose work made this plugin possible:

- **Ice Box Studio (ibox233)** — [BepInEx 6 For Romestead](https://github.com/ibox233/BepInEx-6-CoreCLR-For-Romestead) (also published as BepInExPack Romestead), the Romestead-specific BepInEx 6 CoreCLR mod loader this plugin depends on and runs under.
- **BepInEx team** — BepInEx 6 and HarmonyX, the framework underneath the loader.

## 中文

### 描述

Romestead Radar 是一个客户端 BepInEx 插件，为游戏内小地图添加扇形方向雷达。选择祭坛献祭过的物品、自己击杀过的生物，或未开启的宝箱，然后保持移动；雷达波会指向最近的有效目标。

雷达只提示方向，不显示精确位置。它只在大世界（室外）生效，并且只搜索玩家周围已加载的区块。它是导航辅助工具，不是透视或全图显示类 Mod。

### 功能

- **物品雷达**：追踪祭坛献祭过的物品，指向最近的采集来源。
- **生物雷达**：追踪自己击杀过的生物，指向最近的存活个体。击杀记录按存档分开保存。
- **宝箱雷达**：祭坛献祭过箱子后解锁，指向最近的未开启宝箱；自动排除城镇内和玩家自己放置的箱子。
- 只有移动时才会出现扇形雷达波，不会添加永久小地图标记。
- 仅在大世界显示，室内和地下城不显示。
- 只搜索已加载区块，不会透视未加载区域。
- 物品和生物名称跟随游戏当前语言；界面文本在中文游戏下显示中文，其他语言显示英文。
- 可选兼容 Mod Settings Menu，提供“启用雷达”开关。
- 纯客户端插件，不修改游戏存档。

### 安装

1. 安装 BepInExPack Romestead（Romestead 专用 BepInEx 6 CoreCLR）。
2. 关闭 Romestead。
3. 把 `RomesteadRadar.dll` 复制到 `BepInEx\plugins\`。
4. 启动游戏，顶栏会出现放大镜按钮。
5. 首次启动会生成 `BepInEx\config\com.Nohtaehp.Romestead.Radar.cfg`。
6. 可选：安装 Mod Settings Menu 后可在游戏设置里看到“启用雷达”开关。

### 前置要求

- Romestead `0.26.2.4`
- BepInExPack Romestead `0.4.1`（BepInEx 6 CoreCLR）
- 可选：Mod Settings Menu（`IceBoxStudio.Romestead.ModSettingsMenu`）
- 联机：每个想使用雷达的玩家都需要在自己的客户端安装；未安装的玩家不受影响。
- 从源码编译需要 .NET 8 SDK 和游戏程序集。

### 从源码构建

```powershell
.\build.ps1
```

编译 `RomesteadRadar/RomesteadRadar.csproj`，并把 `RomesteadRadar.dll` 安装到 `BepInEx\plugins`。默认游戏目录为 `E:\SteamLibrary\steamapps\common\romestead`，可用 `.\build.ps1 -GameDir "你的游戏目录"` 覆盖。

### 打包

```powershell
.\package.ps1
```

生成 `dist\RomesteadRadar-v<version>.zip`，其中包含 DLL 和本说明。

### 版本

版本号只在 `version.txt` 中维护，格式为 `游戏版本-Mod版本`，例如 `0.26.2.4-1.0.0`。`package.ps1` 会自动同步插件版本、程序集版本和 zip 文件名。

> BepInEx 的插件元数据只接受三段数字的 SemVer，因此构建时使用 `0.26.2-1.0.0`；程序集版本仍为 `0.26.2.4`。

### 卸载

删除 `BepInEx\plugins\RomesteadRadar.dll`；可选删除 `BepInEx\config\com.Nohtaehp.Romestead.Radar.cfg`（会清除击杀记录）。本插件不写入游戏存档。

### 项目结构

| 路径 | 说明 |
|---|---|
| `RomesteadRadar/` | C# 源码与项目文件 |
| `version.txt` | 版本号唯一来源 |
| `build.ps1` | 编译并安装到 `BepInEx\plugins` |
| `package.ps1` | 一键编译、安装并生成发布 zip |

### 致谢

感谢以下作者的工作让本插件得以实现：

- **Ice Box Studio（ibox233）** —— [BepInEx 6 For Romestead](https://github.com/ibox233/BepInEx-6-CoreCLR-For-Romestead)（Thunderstore 上名为 BepInExPack Romestead），本插件所依赖并运行的 Romestead 专用 BepInEx 6 CoreCLR 模组加载器。
- **BepInEx 团队** —— 加载器底层的 BepInEx 6 与 HarmonyX 框架。

---

**GitHub:** https://github.com/Nohtaehp/RomesteadRadar
