# Apple Magic Trackpad Windows 11 Suite (驱动 + 原生手势同步 + Force Click 重压预览 + 自动自愈)

一套面向 **Windows 11 (24H2 / Build 26100+)** 的 **Apple Magic Trackpad (第 2 代 / 第 3 代 USB-C A3120 `PID_0324` / Lightning `PID_0265`)** 完整驱动、精确参数调优、Force Click 二段深压文件预览（联动 QuickLook）与后台自愈守护方案。

---

## 核心特性与解决痛点

1. **签名驱动开箱即用（蓝牙 + USB 双分支同步）**
   - 内置 `mac-precision-touchpad` v2.2 微软签名驱动（`AMD64` & `ARM64`，INF 版本 `2026.3982.1.1000`）。
   - 同时配置蓝牙（`AmtPtpHidFilter`）与 USB（`AmtPtpDeviceUsbUm`）双注册表分支，强制开启 `IgnoreButtonFinger = 0`（允许物理按住触控板的同时单指继续滑动拖拽）与防误触（`PalmRejection = 1`、`IgnoreNearFingers = 1`）。

2. **Windows 11 原生触控板 API (`SPI_GET/SETTOUCHPADPARAMETERS`) 精确位掩码同步**
   - 解决仅修改 `HKCU\...\PrecisionTouchPad` 注册表不即时生效、或重启/重连后系统设置与注册表脱节的问题。
   - 通过 `SystemParametersInfoW` (`0x00AE` / `0x00AF`) 原生接口读取并按位修改 `TOUCHPAD_PARAMETERS_V1.userFlags`：
     - **指针速度 (`CursorSpeed`)**：`14`（合法范围 `1~20`）
     - **关闭“轻点两下并拖动” (`TapAndDrag`, `0x08`)**：消除快速连续移动/点击时的粘滞与误拖拽，保留清脆的物理按压拖拽
     - **保留核心手势 (`userFlags = 439`)**：开启单指轻点 (`0x04`)、双指轻点右键 (`0x10`)、双指平滑滚动 (`0x80`)、双指捏合缩放 (`0x100`)、外接鼠标时保持启用 (`0x01`)，保留系统三指/四指多任务手势映射。

3. **macOS 风格 Force Click（二段深压触发 QuickLook 空格预览）+ 多屏物理速度对齐（PPI 自适应补偿）+ 类 macOS S型动态加速曲线**
   - `MagicForceClick.exe`（附完整 C# 源码 `MagicForceClick.cs`）开启 `PerMonitorV2` 物理像素感知，通过 Raw Input 监听 `ReportID = 0x05` 的 112 字节 MT2 原始触控报文。
   - **任意新电脑多显示器 PPI 自动等速补偿**：启动及显示器热插拔（`WM_DISPLAYCHANGE`）时，自动通过 `EnumDisplaySettings` 与 `GetDeviceCaps(HORZSIZE)` 读取每块显示器的 EDID 真实物理宽度（毫米）与物理分辨率，以主屏像素密度（`px/cm`）为 `1.000x` 基准，自动对副屏施加阻尼/增益补偿，使手指在妙控板上滑动 1cm 在不同尺寸/分辨率屏幕上的物理移动厘米数完全一致。
   - **类 macOS S型动态加速曲线（慢滑稳、快甩飞）**：仅在单指移动（`contactCount == 1`）时根据手指物理滑动速率 $v$（`units/ms`）按平滑 S 曲线 $G(v) = 0.95 + (3.40 - 0.95) \cdot \frac{(v/4.5)^{1.85}}{1+(v/4.5)^{1.85}}$ 动态注入加速位移（带亚像素余数累积），慢滑微操 `0.95x` 精准不发飘，中常速 `1.4x~2.2x` 轻快跟手，快甩最高 `3.40x` 轻松横跨多联屏；双指滚动与三/四指手势完全不受影响。
   - **二段深压预览**：普通点击峰值约 `65~115`，单指重压达到阈值（默认 `180`，支持 `120~250` 自定义）且未发生大幅拖拽位移时，在桌面 / 资源管理器 / QuickLook 窗口自动释放左键并通过命名管道秒调 **QuickLook** 预览文件（自带 20 个插件目录、21 个完整预览插件 DLL）。

4. **全自动自愈计划任务 (`\MagicTrackpad_AutoFix`)**
   - 自动注册三大触发器：**用户登录时**、**每 15 分钟定期巡检**、**系统睡眠唤醒事件 (`Power-Troubleshooter EventID=1`)**。
   - 自动关闭 Apple 触控板节点 Windows 节能断连项（`EnhancedPowerManagementEnabled = 0`、`SelectiveSuspendEnabled = 0`），自动修复 USB `MI_00` `Code 10` 异常节点，并通过底层 IOCTL (`0x00222000`) / 命名管道热重载驱动参数。

---

## 快速开始（一键安装）

### 环境要求
- **操作系统**：Windows 11 24H2 (Build 26100) 或更新版本（64 位）
- **硬件支持**：Apple Magic Trackpad 2 / 3（支持 USB-C `VID_05AC&PID_0324` 与 Lightning `VID_05AC&PID_0265`，蓝牙或有线连接均可）

### 安装步骤

1. **克隆或下载本仓库**：
   ```powershell
   git clone https://github.com/<your-username>/apple-magic-trackpad-windows.git
   cd apple-magic-trackpad-windows
   ```
2. **一键部署**：
   - 直接右键 **`一键配置妙控板(管理员运行).cmd`** -> **以管理员身份运行**；
   - 或在**当前登录用户的 64 位管理员 PowerShell** 中执行：
     ```powershell
     powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install_All_MagicTrackpad.ps1
     ```
   - **可选参数**：
     ```powershell
     # 自定义指针速度(1~20)、深压阈值(120~250)或额外安装三指拖拽 MSIX 应用
     .\Install_All_MagicTrackpad.ps1 -CursorSpeed 14 -ForceClickThreshold 180 -EnableThreeFingerDrag
     ```

> **说明**：为兼容 GitHub 单文件 100MB 限制，仓库中的便携版 `QuickLook_Portable_Clean.zip`（含完整 21 个预览插件）已切分为 `.part1` + `.part2`。运行 `Install_All_MagicTrackpad.ps1` 时，脚本会自动在 `[0/7]` 阶段将其流式无损合并为 `QuickLook_Portable_Clean.zip` 并执行全量 SHA256 校验，无需手动解压或安装 Git LFS。

---

## 给 AI 助手的一键配置 Prompt

如果你在新电脑上使用 AI Agent（如 Hermes / Claude Code / OpenClaw / Cursor），可直接发送以下指令让 AI 完成全套安装与核验：

```text
请读取仓库中的 README_AI配置指南.md。
先核对 Windows 版本、系统架构、妙控板型号和蓝牙适配器驱动，使用当前已登录用户的管理员权限运行 Install_All_MagicTrackpad.ps1；不要切换到另一个管理员账户。
默认：光标速度14、轻点点击开、轻点两下拖拽关、双指滚动与缩放开、IgnoreButtonFinger=0同时写入USB和蓝牙分支；保留三/四指手势映射。
原生参数必须通过 SPI_GET/SETTOUCHPADPARAMETERS 读取、修改、回读。TapAndDrag 是 userFlags 的 0x08 位，双指滚动是0x80位，缩放是0x100位（447清除0x08后为439）。
安装后核对驱动、任务触发器、自启项、预览程序、AutoFix日志与Install.result.json。
```

---

## 常用独立维护命令

- **仅读取并核验当前触控板 API 与注册表状态（不修改）**：
  ```powershell
  powershell.exe -ExecutionPolicy Bypass -File .\Set_MagicTrackpad_UserSettings.ps1 -ReadOnly
  ```
- **单独调整指针速度并同步 API 与注册表（例如设为 14）**：
  ```powershell
  powershell.exe -ExecutionPolicy Bypass -File .\Set_MagicTrackpad_UserSettings.ps1 -CursorSpeed 14
  ```
- **手动触发一次驱动电源与参数自愈重载**：
  ```powershell
  schtasks /run /tn "\MagicTrackpad_AutoFix"
  ```

---

## 仓库目录结构

| 文件 / 目录 | 说明 |
| --- | --- |
| `一键配置妙控板(管理员运行).cmd` | 管理员权限自提权一键安装入口 |
| `Install_All_MagicTrackpad.ps1` | 主安装脚本（分卷自动合并、SHA256 校验、自动备份、驱动安装、API 同步、QuickLook 部署、自启与计划任务注册） |
| `Set_MagicTrackpad_UserSettings.ps1` | Windows 11 原生 `SystemParametersInfoW` (`0x00AE`/`0x00AF`) 触控板参数与注册表双写同步脚本 |
| `MagicTrackpad_AutoFix.ps1` / `.vbs` | 后台自愈脚本与静默启动器（电源防断连、USB/蓝牙 `IgnoreButtonFinger=0` 守护、驱动 IOCTL/Pipe 热重载） |
| `Register_MagicTrackpad_AutoFix.ps1` | 注册 `\MagicTrackpad_AutoFix` 计划任务（登录 + 15 分钟周期 + 睡眠唤醒事件三触发器） |
| `MagicForceClick.cs` / `.exe` | 二段深压（Force Click）RawInput 守护进程源码与可执行文件（联动 QuickLook 预览） |
| `AmtPtpControlPanel.exe` | 驱动按压力度与触觉反馈控制面板 |
| `AMD64/`、`ARM64/` | Apple Precision Touchpad 微软签名驱动（v2.2，`2026.3982.1.1000`） |
| `QuickLook_Portable_Clean.zip.part1` / `.part2` | 去重清理后的 QuickLook 便携包分卷（含 20 个插件目录、21 个插件 DLL，安装时自动合并校验） |
| `ThreeFingersDragOnWindows_2.0.7_x64.Msix` | 可选的 Windows 三指拖拽增强工具包（通过 `-EnableThreeFingerDrag` 按需安装） |
| `Realtek_Bluetooth_Backup/` | RTL8851BU (`USB\VID_0BDA&PID_B851&MI_00`) 蓝牙适配器驱动离线备份（仅特定硬件按需手动恢复，主脚本不自动安装） |
| `README_AI配置指南.md` | 面向 AI Agent 与深入排障的技术规范、位掩码说明与回退指南 |
| `SHA256SUMS.json` | 全量文件 SHA256 校验清单 |

---

## 致谢与声明

- 触控板底层驱动基于开源项目 [imbushuo/mac-precision-touchpad](https://github.com/imbushuo/mac-precision-touchpad) 及其签名发行版（Bingxing Wang, Vito Plantamura）。
- 文件预览基于开源项目 [QL-Win/QuickLook](https://github.com/QL-Win/QuickLook)，可选三指拖拽包来自 [ClementGre/ThreeFingersDragOnWindows](https://github.com/ClementGre/ThreeFingersDragOnWindows)。
- 本仓库中的自动化配置、原生 API 同步、自愈守护脚本与 `MagicForceClick` 源码采用 [MIT License](LICENSE) 开源。
