# 显示快捷控制（Display Quick Control）

Windows 11 系统托盘工具，通过 ColorControl 的 NVIDIA 预设切换 HDR、刷新率和颜色设置。使用 C# / .NET 10 / WPF，界面为简体中文，正常启动后只显示托盘图标。

支持预设初始化、自定义模式同步、当前模式识别、卡片选中效果、全局快捷键、深浅色主题及开机启动。

## 默认模式

| 模式 | 分辨率 | 刷新率 | HDR | 颜色设置 | ColorControl 预设名 | 默认快捷键 |
|---|---|---|---|---|---|---|
| HDR 180 | 2560 × 1440 | 180Hz | 开启 | 8bit、RGB、Full | `HDR-180` | Ctrl + Alt + H |
| SDR 180 | 2560 × 1440 | 180Hz | 关闭 | NVIDIA 默认颜色设置 | `SDR-180` | Ctrl + Alt + S |
| HDR 安全备用 | 2560 × 1440 | 120Hz | 开启 | 10bit、RGB、Full | `HDR-120` | Ctrl + Alt + J |

这些模式是本工具内置的初始配置，并非从系统读取的可用模式列表。可以修改或删除，实际支持的组合取决于显示器、显卡、接口及驱动。

SDR 180 的色深、颜色编码和动态范围交给 NVIDIA 默认策略决定，不固定为 8bit、RGB 或 Full。HDR 安全备用始终需要手动点击，不会在切换失败后自动执行。

## 在 VS Code 中运行

需要 Windows 和 **.NET 10 SDK**。用 VS Code 打开项目根目录 `hdr_control`，按 **Ctrl + 反引号键** 打开终端，执行：

~~~powershell
dotnet run --project .\DisplayQuickControl
~~~

首次运行会自动还原依赖并编译。启动后点击右下角托盘中的显示器图标；如果被折叠，打开任务栏的隐藏图标区域。项目的 `.vscode` 配置也支持按 **F5** 调试。

重新编译或运行新版本前，先从本工具的托盘菜单选择“退出”；使用 VS Code 调试时也应先停止调试。旧实例未退出时，再次启动只会打开旧实例面板，无法加载新代码。

只编译、不启动：

~~~powershell
dotnet build .\DisplayQuickControl.sln -c Release -m:1
~~~

WPF 程序只能在 Windows 运行。macOS / Linux 可以启用 Windows targeting 交叉编译，不能运行 WPF 界面。

## 打包为 exe

以下命令均在项目根目录执行。

### Windows x64 自包含单文件

适用于常见的 Intel / AMD 64 位 Windows 电脑：

~~~powershell
dotnet publish .\DisplayQuickControl -c Release -p:PublishProfile=win-x64-self-contained
~~~

完成后打开输出目录：

~~~powershell
explorer .\artifacts\win-x64
~~~

生成的程序为 `artifacts/win-x64/DisplayQuickControl.exe`。复制到固定目录，例如 `C:\Tools\DisplayQuickControl\`，即可双击运行；本工具自带 .NET 运行时，无需携带源码。

**ColorControl 是独立程序，不包含在打包的 exe 中。** 仍需保留它，并在本工具设置中选择其路径；它自身的运行时依赖也需要单独满足。

### 其他发布方式

| 发布配置 | 架构 | 输出目录 | 本工具的运行时要求 |
|---|---|---|---|
| `win-x64-self-contained` | x64 | `artifacts/win-x64` | 自带运行时 |
| `win-arm64-self-contained` | ARM64 | `artifacts/win-arm64` | 自带运行时 |
| `win-x64-framework` | x64 | `artifacts/win-x64-framework` | 同架构 .NET Desktop Runtime 10 |
| `win-arm64-framework` | ARM64 | `artifacts/win-arm64-framework` | 同架构 .NET Desktop Runtime 10 |

将发布命令中的配置名替换为对应值即可。例如，生成体积更小、依赖已安装运行时的 x64 版本：

~~~powershell
dotnet publish .\DisplayQuickControl -c Release -p:PublishProfile=win-x64-framework
~~~

也可以使用发布脚本：

~~~powershell
.\scripts\publish.ps1 -Runtime win-x64
.\scripts\publish.ps1 -Runtime win-x64 -FrameworkDependent
~~~

WPF 发布不启用 trimming / NativeAOT。自包含单文件首次启动时会将原生库解包到用户临时缓存。ARM64 发布成功只说明程序可以生成，目标设备上的 NVIDIA 驱动和 ColorControl 支持情况仍需另行确认。

## 首次使用

1. 安装并解压 [ColorControl 11](https://github.com/Maassoft/ColorControl/releases/tag/v11.0.0.0)。该版本需要 **.NET Desktop Runtime 10** 和 **ASP.NET Core Runtime 10**，无需为本工具安装 ColorControl Windows Service。
2. 启动本工具，点击托盘图标，在面板顶部的下拉框选择目标显示器；默认使用主显示器。
3. 点击齿轮进入“设置”，使用“浏览…”选择真实的 `ColorControl.exe`，例如 `D:\ColorControl\ColorControl.exe`。
4. 退出正在运行的 ColorControl，点击“初始化 ColorControl 预设”，自动补齐当前模式对应的预设。
5. 保持勾选“保存模式时同步到 ColorControl”，点击“保存设置”，同步已有同名预设的显示参数。
6. 返回面板，点击需要的模式卡片，等待显示器恢复及状态核对完成。

初始化和保存设置只写入预设定义，不会立即改变显示状态。实际切换由模式卡片或全局快捷键触发。

## 当前模式和选中效果

主面板显示“当前模式”，例如“SDR 180”或“HDR 180”。匹配的卡片会显示蓝色背景、加粗边框和勾选标记，深色与浅色主题均支持。

- 识别依据是 Windows 实际状态，点击卡片不会直接把目标模式标记为已生效。
- 核对分辨率、HDR 状态和刷新率，刷新率允许小于 1Hz 的差异。
- 指定颜色的模式还会核对系统能够提供的色深和颜色编码；不可用的色彩信息会明确标注。
- 默认颜色模式不要求固定色深、编码或动态范围。Windows 无法验证 NVIDIA 控制面板中的默认颜色策略，需在那里核对该选项。
- 未匹配任何预设时显示“自定义模式”；HDR 状态未知时不选中卡片。多个预设参数相同时，并列显示匹配名称。

打开面板、点击刷新和切换结束时会读取状态；面板可见时也会响应系统显示设置变化。部分驱动不会针对 HDR 变化发送通知，此时可手动刷新。

## 自定义模式

从主面板点击“自定义模式”，或进入“设置 → 自定义模式…”。

可添加、编辑、删除及排序 1–32 个模式，配置名称、ColorControl 预设名称、分辨率、刷新率、HDR、颜色和快捷键。刷新率支持 `59.94` 等小数。

勾选“使用默认颜色设置”时，色深和编码选择器禁用，颜色由 NVIDIA 默认策略决定。取消勾选后可指定 6 / 8 / 10 / 12 / 16bit，以及 RGB、YCbCr 4:4:4 / 4:2:2 / 4:2:0；同步指定颜色时使用 Full 动态范围。填写参数不会使硬件原本不支持的组合变为可用。

主面板入口的“保存模式”会立即保存并同步；从设置打开的编辑器只修改草稿，还需点击“保存设置”。取消或关闭编辑窗口会放弃尚未保存的编辑。

最多可指定一个“手动安全备用模式”，始终由用户手动点击。编辑模式期间暂不执行全局模式快捷键，避免输入时误切换。

## 预设初始化与自动同步

初始化和同步目前支持 ColorControl 11，预设文件位于：

~~~text
%APPDATA%\Maassoft\ColorControl\NvPresets.json
~~~

| 操作 | 缺失的预设 | 已有同名预设 | 是否立即切换显示器 |
|---|---|---|---|
| 初始化 ColorControl 预设 | 创建 | 保留原样 | 否 |
| 开启同步后保存设置或模式 | 创建 | 更新对应显示参数 | 否 |
| 点击模式卡片 | 不创建 | 执行该预设 | 是 |

“保存模式时同步到 ColorControl”默认开启。同步按预设名称匹配，更新目标显示器、分辨率、刷新率、HDR 和颜色策略，并启用对应 Include。默认颜色会主动写入自动策略，恢复此前手动指定的颜色设置；HDR 使用明确的开启或关闭，不使用 Toggle。

已有预设的 ID、其他字段及无关预设会保留。新预设不添加 ColorControl 快捷键或启动触发器，全局快捷键由本工具管理。同步时各模式需使用不同的预设名称。

需要写入时请先退出 ColorControl，避免它之后用内存中的旧配置覆盖文件。原文件按 `NvPresets.json.dqc-日期-唯一标识.backup` 备份，再原子替换。损坏、检测到被同时修改或 ColorControl 仍在运行且需要写入时，会停止写入并提示原因；同步失败时尝试恢复本工具原有设置，保留编辑草稿以便重试。

初始化会立即写入 ColorControl，不受随后取消设置影响。删除模式或修改预设名称不会自动删除旧的 ColorControl 预设，避免影响其其他用途。

未配置 ColorControl 路径时只保存本工具设置；关闭自动同步时需自行在 ColorControl 中配置预设。目标显示器不可用时应重新选择显示器；ColorControl 无法区分同型号多屏时，可关闭同步后手动绑定。

## 快捷键、主题和开机启动

默认快捷键见上方模式表。在“自定义模式”中可修改为 `Ctrl+Shift+F8` 等组合；数字键使用 `D0`–`D9`。留空禁用单个模式的快捷键，也可在设置中取消“启用全局快捷键”。占用或冲突会显示提示。

主题支持“跟随系统 / 深色 / 浅色”。“随 Windows 启动”使用当前用户的注册表启动项，不需要管理员权限、服务或计划任务。启用前应将打包后的 exe 放在固定目录，移动程序后需重新保存启动设置。

托盘右键可打开控制面板、刷新状态、打开设置或退出。Esc 隐藏面板；点击外部隐藏面板及打开面板时刷新状态的行为可在设置中调整。

## 更新旧版本

1. 从托盘退出旧的“显示快捷控制”；使用 VS Code 调试时先停止调试。
2. 重新运行源码或替换发布文件。
3. 需要更新已有 ColorControl 预设时，退出 ColorControl，开启自动同步并点击“保存设置”。初始化只补齐缺失预设，不覆盖已有同名预设。

未修改过的旧 HDR 144 模式会迁移为 HDR 180Hz、8bit，保留快捷键。未自定义的旧 SDR 180 模式会迁移为默认颜色；明确选择固定颜色后，该选择会保留。已有自定义模式和预设名按迁移规则保留。

配置不依赖源码目录或 exe 目录，重新编译、打包不会清空用户设置。

## 配置、日志和卸载

~~~text
本工具配置：%LOCALAPPDATA%\DisplayQuickControl\settings.json
本工具日志：%LOCALAPPDATA%\DisplayQuickControl\Logs\
ColorControl 预设：%APPDATA%\Maassoft\ColorControl\NvPresets.json
ColorControl 日志：%APPDATA%\Maassoft\ColorControl\LogFile.txt
~~~

本工具保存程序路径、模式列表、目标显示器、颜色策略、快捷键、主题、同步开关和启动偏好。配置通过临时文件原子替换；损坏或非法配置会回退默认值并提示。

日志按 UTC 时间记录，保留最近约 7 个文件，每个约 1MiB；不记录用户填写的预设名、可执行文件路径、显示器序列号、设备路径或原始 CLI 输出。日志写入失败不影响启动。

卸载时先关闭“随 Windows 启动”，退出程序，再删除程序目录。可按需删除本工具的配置和日志；初始化或同步过的 ColorControl 预设及备份会继续保留。

## 常见问题

**启动后没有主窗口？** 正常启动只显示托盘图标。打开任务栏的隐藏图标区域，点击显示器图标即可打开面板。

**改代码后没有生效，或编译提示文件被占用？** 先退出托盘中的旧程序，再停止 VS Code 调试并重新运行。`MSB3021` / `MSB3027` 以及 `DisplayQuickControl.exe is being used by another process` 通常表示旧程序尚未退出。

**“请先退出 ColorControl”是固定说明吗？** 设置窗口底部出现这条错误时，表示本次写入前检测到 `ColorControl` 或 `ColorControl.UI` 进程仍在运行。即使没有主窗口或托盘图标，也可能有后台进程。正常退出后重试；若确认是挂起的进程，可在任务管理器“详细信息”中处理 `ColorControl.exe` / `ColorControl.UI.exe`。结束前应确认它没有正在执行显示切换，本工具不会自动强杀进程。

**初始化成功，为什么 SDR 仍使用固定颜色？** 初始化会保留已有同名预设。重启更新后的本工具，确认 SDR 180 已勾选“使用默认颜色设置”，再保持自动同步开启并点击“保存设置”，最后点击 SDR 180 卡片应用。

**点击卡片没有切换？** 确认程序路径有效、预设已创建且名称一致。保持同步开启并保存设置可补齐或更新预设；仅修改卡片名称或目标参数不会立即切换显示器。

**切换失败或状态不匹配？** 查看本工具与 ColorControl 日志，核对目标显示器和 Include。程序不会自动重试或自动退回安全备用模式。ColorControl 某些失败可能仍以 0 退出，因此程序还会检查已知错误输出并独立核对 Windows 状态。

**ColorControl 报超时？** 等待超过 30 秒时只结束界面等待，不杀死进程。原进程仍运行时阻止新命令，直到它退出。请检查是否有等待处理的窗口，避免通过重启工具绕过正在执行的操作。

**默认颜色为什么可能显示 10bit 或其他编码？** 颜色由驱动决定，8bit 和 RGB 不是固定目标。“当前状态”展示系统报告的实际值；未知信息会明确标注。Windows 无法可靠验证 NVIDIA 的默认颜色策略和 Full / Limited 动态范围。

**为什么 SDR 下“自动管理应用的颜色”仍然开启？** 这是 Windows 自动颜色管理（ACM），可用于 SDR 广色域显示，与 NVIDIA“默认颜色设置”独立。面板会单独显示此开关；旧系统无法读取时显示未知。程序会保留切换前的 ACM 选择，不把它强制设为开或关。历史日志没有记录 ACM，无法据此确定之前由谁开启。

**切回 SDR 180Hz 偶发发灰、发白或饱和度变化？** ColorControl 11 会先设置颜色再关闭 HDR，且使用旧的 Advanced Color 接口。新版在支持独立 HDR API 的系统上先关闭 HDR，连续两次读到 HDR 已关闭后才执行一次 SDR 颜色预设；若预设改变了可读取的 ACM 选择，再用独立 WCG API 恢复原选择。旧系统保留 ColorControl 兼容路径。NVIDIA 默认颜色策略仍由驱动决定，Windows 无法验证 Full / Limited 动态范围，修正顺序不代表已证明所有观感问题均已解决。如需对比 ACM 的影响，可在 SDR 下手动关闭或开启一次，并观察面板状态及相同图片。

**切换时短暂黑屏？** 模式切换可能伴随短暂黑屏。程序在命令结束后等待约 1.8 秒，再连续核对分辨率、刷新率、HDR、颜色编码、色深及可读取的 ACM 选择；每 0.5 秒读取，最多 12 次。未稳定匹配会提示错误，不重放预设。短时匹配不代表已证明链路长期稳定。

## 测试与验证

在项目根目录执行：

~~~powershell
dotnet build .\DisplayQuickControl.sln -c Release -m:1
dotnet run --project .\tests\DisplayQuickControl.Tests -c Release -- .\tests\ColorControl.Fixture\bin\Release\net10.0\ColorControl.exe
dotnet run --project .\tests\DisplayQuickControl.WindowsSmoke -c Release
~~~

截至 2026-10-15，本地已完成 Windows Release 编译、98 项基础与安全断言、WPF 窗口和绑定检查，以及用已安装的 ColorControl 11.0.0.0 程序库解析和往返序列化生成预设的兼容验证。覆盖初始化、默认颜色策略、自定义模式同步、备份、幂等、失败恢复、当前模式显示、卡片选中样式、退出 HDR 后才应用 SDR 颜色、ACM 选择保留及信号稳定性检查。

测试使用临时目录和模拟 ColorControl 子进程；兼容验证只解析配置，未通过这些测试执行真实显示切换。WPF 检查需要 Windows 交互桌面，测试快捷键已被占用时相应冲突检查会跳过。真实预设应用、混合 DPI、多屏及长期链路稳定性仍需按 [Windows 验收步骤](docs/windows-validation.md) 检查。

Windows CI 配置位于 [.github/workflows/windows.yml](.github/workflows/windows.yml)，包含构建、测试及 x64 / ARM64 发布；配置存在不代表远程流程已经运行。macOS / Linux 的模拟进程文件不带 `.exe` 扩展名，且无法运行 WPF 检查。

## 项目结构与实现依据

~~~text
DisplayQuickControl/
  App.xaml[.cs]              生命周期、服务组装和保存同步
  Models/                   配置、实际显示状态和目标模式
  ViewModels/               主面板、模式编辑和设置逻辑
  Views/                    主面板、设置和自定义模式窗口
  Services/                 预设同步、ColorControl CLI、显示读取、托盘等
  Native/NativeMethods.cs   Windows 结构体和 P/Invoke
  Resources/                主题样式和图标
tests/                      模拟 CLI、安全检查和 Windows WPF 检查
scripts/                    发布和图标生成
~~~

显示状态通过 `QueryDisplayConfig` / `DisplayConfigGetDeviceInfo` 读取，优先使用 Advanced Color Info 2 判断 HDR，不支持时回退旧 API。ColorControl 命令通过 `ProcessStartInfo.ArgumentList` 传参，不经过命令解释器，支持中文及含空格的预设名。

- [ColorControl 11 README 与 CLI](https://github.com/Maassoft/ColorControl/tree/v11.0.0.0#command-line-interface)
- [ColorControl NVIDIA 预设结构与颜色策略](https://github.com/Maassoft/ColorControl/blob/v11.0.0.0/Shared/Contracts/NVIDIA/NvPreset.cs)
- [ColorControl NVIDIA 执行逻辑](https://github.com/Maassoft/ColorControl/blob/v11.0.0.0/ColorControl/Services/NVIDIA/NvService.cs)
- [Microsoft DisplayConfig 设备信息类型](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/ne-wingdi-displayconfig_device_info_type)
- [Shell_NotifyIconGetRect](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shell_notifyicongetrect)
- [Windows 设置 URI](https://learn.microsoft.com/en-us/windows/apps/develop/launch/launch-settings)
