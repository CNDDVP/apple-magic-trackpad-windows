# Magic Trackpad 控制中心

适用于现有 `苹果妙控板驱动_MagicTrackpad_v2.2` 方案的独立 Windows 设置界面。

## 启动

双击桌面上的「妙控板控制中心」，或运行本目录的 `MagicControlCenter.exe`。

本程序放在现有方案目录的子文件夹中，自动读取上级的 `MagicForceClick.json`。请勿在子文件夹放置另一个同名 JSON，否则会将其视为独立配置。

## 能做什么

- 调节轻压跨屏、经典手感和深按预览参数。
- 调整 Windows 基础指针速度、轻点、滚动、缩放和滚动方向。
- 查看后台、Raw Input、指针输出和配置热加载状态。
- 打开 Windows 触控板设置、原参数窗口和 AmtPtpControlPanel 驱动面板。

保存增强参数前，在上级 `control-center-backups` 创建原 JSON 备份。保存成功不代表后台已经生效，只有诊断状态中的生效参数与配置一致，界面才显示「后台已生效」。

原三指、四指动作继续交给 Windows；键盘映射未接入，本面板不显示无后端的键盘开关。

## 构建与验证

在仓库根目录执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\Build-ControlCenter.ps1
```

输出在 `artifacts\MagicControlCenter.exe`。构建会运行底层测试；窗口测试需在真实 Windows 桌面会话运行 `MagicControlCenter.UiTests.exe`，它会操作隔离配置副本并输出截图。

独立控制中心面向 Windows 11 / .NET Framework 4.8，日常启动和设置不依赖 WSL、浏览器或额外 SDK。

### 应用图标

图标沿用界面的绿白配色，以银白妙控板和滑动轨迹表示触控操作。可编辑源文件位于 `docs/assets/control-center-icon.svg`，配套 PNG 为 1024 像素；`MagicControlCenter/MagicControlCenter.ico` 包含 16 至 256 像素的 10 档尺寸。

构建脚本将 ICO 嵌入程序，窗口标题栏与任务栏从程序读取同一图标。桌面快捷方式使用随程序部署的 ICO 文件；升级快照会保存和恢复原有快捷方式及图标文件。

### 真实桌面验收

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-ControlCenter.ps1 -BuildDirectory .\artifacts
```

该脚本需要交互式 Windows 桌面。当前验收基准为主屏 150%（144 DPI），并检查所有已连接显示器的 DPI 切换。后台构建会话不能替代真实桌面检查。每轮生成独立目录，记录构建与测试程序哈希、退出码、完整结果和截图；只有全部 17 项完成且退出码为 0 才算通过。

覆盖五页导航、一次缩放与卡片边界、开关保存、预设与放弃修改、跨屏 DPI、JSON 备份和回读、并发冲突、未保存关闭的取消与确认、长页滚动。底层另有 7 项测试，包括高级/未知 JSON 字段保留。

文字布局另行检查同级控件遮挡与字体所需高度，覆盖 100%、125%、150% 三个实际显示器上的全部五页，以及各显示器上的新窗口启动。截图通过原生窗口打印获取，避免 `DrawToBitmap` 反向绘制控件而掩盖遮挡；部署后仍需复核正式窗口的实际显示。

### 部署与独立回退

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Deploy-ControlCenter.ps1 -ProductionDirectory "<现有方案目录>" -BuildDirectory .\artifacts -AcceptanceDirectory "<通过验收的当轮目录>"
```

部署脚本要求候选 EXE、DPI 配置和测试程序与完整验收记录一致。可先加 `-WhatIf` 做只读预检。它只部署控制中心，保留父目录的驱动、增强后台和 JSON；部署前保存整个旧控制中心，失败会恢复旧文件。

回退入口是部署记录 `RollbackSnapshot` 指向的目录中的 `Rollback.cmd`。回退脚本先核验备份哈希，再恢复旧文件并清理本次新增的已登记文件，不覆盖父目录中的用户手感配置。备份必须完整保留。

保存时保留高级参数与未知 JSON 字段，并检测打开面板后的外部编辑。若看到“保存未完成”，可先复制自己的修改，再点“放弃修改”或诊断页的“重新读取”，读取外部最新版本后继续编辑。程序不会自动合并两人的修改。

“已保存，后台已生效”要求保存后的新鲜诊断状态和实际参数一致；文件写入成功本身不足以确认热加载。部署后的正式 UI、工具入口与热加载往返结果应另行记录，不能只沿用隔离测试结论。

热加载往返检查是单独的显式操作：`MagicControlCenter.LiveTests.exe --production-roundtrip <现有方案目录> <已验收EXE的SHA256> <结果目录>`。它先备份生产 JSON，再通过控制中心保存逻辑临时增加 1 ms 稳定时间，确认后台的新鲜生效参数，随后通过保存逻辑恢复原值，最后恢复原始文件字节并再次核对后台。若检测到其他程序同时修改配置，会保留外部修改并报错，不擅自覆盖。日常构建和隔离 UI 验收不会运行这项生产检查。

需要管理员部署驱动或调整系统范围设置时，仍使用原方案脚本。本控制中心自身不要求管理员权限。
