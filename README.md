# Display Quick Control

轻量 Windows 11 系统托盘工具，用 ColorControl 的 NVIDIA 预设切换 HDR、刷新率和色深。C# / .NET 10 LTS / WPF，无第三方 UI 框架、无 Electron、无管理员权限要求。正常启动只显示托盘图标。

| 模式 | 分辨率 | 刷新率 | HDR | 色深 | 预设默认名称 |
|---|---|---|---|---|---|
| HDR | 2560 × 1440 | 144 Hz | 开 | 10 bit | HDR-144 |
| SDR | 2560 × 1440 | 180 Hz | 关 | 8 bit | SDR-180 |
| HDR Safe | 2560 × 1440 | 120 Hz | 开 | 10 bit | HDR-120 |

三个预设均需在 ColorControl 中设置 RGB / Full Range。应用不创建、不修改 ColorControl 预设。

## 当前交付与验证范围

已在 macOS ARM64 主机上完成 Windows WPF Release 编译及 Windows x64、ARM64 交叉发布；安全测试使用模拟 ColorControl 进程，不操作显示硬件。Windows 的托盘交互、混合 DPI、真实 DisplayConfig、ColorControl 和海信 G5 Ultra / RTX 4060 切换仍需在目标机器验收，不能将交叉编译视为硬件通过。参见 [验收步骤](docs/windows-validation.md)。

截图位置预留：`docs/screenshots/control-panel-dark.png`、`control-panel-light.png`、`settings.png`。未附伪造运行截图。

## 启动与首次配置

1. 将 `artifacts/win-x64/DisplayQuickControl.exe` 复制到 Windows 11 的固定目录，例如 `C:\Tools\DisplayQuickControl\`。自包含版本不需要为本应用安装 .NET。ARM64 使用对应目录的 exe；ColorControl 和 GPU 支持另行确认。
2. 双击 exe，点击系统托盘显示器图标。如果被折叠，打开任务栏的隐藏图标区域。
3. 点击齿轮，Browse 选择真实的 `ColorControl.exe`，保存；路径、预设名均可修改。
4. 面板显示器下拉框选择要读取/验证的显示器。默认读取主显示器，选择会保存。三个 ColorControl 预设必须配置为同一个显示器。
5. 点击模式卡片执行一次预设，等待显示器恢复，应用将读取真实状态验证。HDR Safe 始终需要手动点击。

再次启动只会向已有实例发送打开面板请求。托盘右键提供打开面板、刷新状态、设置、退出。Esc / 点击面板外隐藏；设置窗口失焦不会关闭。模式执行期间禁止退出和更改设置。

## 安装和配置 ColorControl

从 [官方 Releases](https://github.com/Maassoft/ColorControl/releases) 下载并解压。开发时核实的最新发布为 [v11.0.0.0](https://github.com/Maassoft/ColorControl/releases/tag/v11.0.0.0)，官方要求 **.NET Desktop Runtime 10 与 ASP.NET Core Runtime 10**。这是 ColorControl 自身的依赖；Display Quick Control 的自包含发布不能代替它们。无需为本工具安装 ColorControl Windows Service。

在 ColorControl 的 NVIDIA Controller 中先选择海信 G5 Ultra，创建三个命名预设。ColorControl 不同版本布局可能变化，应以当前界面的 NVIDIA 预设选项为准：

1. 创建 `HDR-144`，显式设置分辨率 2560×1440、刷新率 144Hz、HDR **On**、色深 10 bpc、RGB、动态范围 Full。
2. 创建 `SDR-180`，同分辨率、180Hz、HDR **Off**、8 bpc、RGB、Full。
3. 创建 `HDR-120`，同分辨率、120Hz、HDR **On**、10 bpc、RGB、Full。
4. 对分辨率/刷新率、HDR、色彩等各个要应用的配置启用 **Include**；只填写值但没有 Include，可能不会应用。HDR 使用明确 On/Off，不能用 Toggle，否则重复点击无法保证目标状态。
5. 保存并分别在 ColorControl 中手动应用一次，用 Windows 高级显示设置核对。确保预设绑定目标显示器，而非不确定的全局选择。

实际启动参数来自 [官方 README](https://github.com/Maassoft/ColorControl#command-line-interface) 和 [CommandLineHandler.cs](https://github.com/Maassoft/ColorControl/blob/master/ColorControl/CommandLineHandler.cs)：

```powershell
& 'C:\Tools\ColorControl\ColorControl.exe' --nvpreset 'HDR-144' --nogui
```

程序用 `ProcessStartInfo.ArgumentList`，不经过命令解释器，支持空格和中文名称。ColorControl 已运行和未运行时均使用同一官方 CLI。`NvService.ExecutePresetAsync` / CLI 某些失败路径可能仍以 0 退出，因此程序也检查已知错误输出，并独立验证真实显示状态；不能只依赖退出码。更新 ColorControl 后如有行为变化，请先检查其官方 `--help` 和日志。

## 状态、异常与安全行为

- `QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS)` 读取活动路径、桌面分辨率和 rational 刷新率；`DisplayConfigGetDeviceInfo` 读取实际设备名称、Advanced Color、色深和颜色编码。GPU 名称由源 GDI 名称映射 `EnumDisplayDevices` 获取，未硬编码显卡或显示器。
- 首先查询 SDK 的 `GET_ADVANCED_COLOR_INFO_2`（type 15），以 `activeColorMode == HDR` 判断，避免把 WCG 当 HDR；不支持时回退旧 Advanced Color API，排除 `wideColorEnforced`。API 失败时 HDR / 色深 / 编码显示 Unknown，绝不根据最后点击推断。
- Windows 返回的 bits-per-channel 和编码属于驱动报告，可能不可用；本应用不将 RGB 当默认实际值。DisplayConfig 不提供可靠的 NVIDIA Full/Limited 动态范围，本应用不显示“Full 已验证”；需要在 ColorControl / NVIDIA 控制面板核对。
- Active 卡片依据 HDR + 刷新率容差 `< 1 Hz`。切换后还检查 2560×1440、已知的色深和 RGB；未知的色深/编码不会伪造，结果消息会说明未验证部分。
- 一次用户动作最多启动一次 preset，执行期间其他模式禁用。退出后等 1.8 秒，再读取同一物理目标。失败或不匹配只提示，不会自动重试、不自动切换 HDR Safe。
- 30 秒超时只结束界面等待，不强杀 ColorControl。它仍在运行时执行门锁保持关闭，新的切换请求被拒绝，直至原进程退出。请处理 ColorControl 的挂起窗口；不要通过重启应用绕过正在执行的操作。
- 读取拓扑遇到缓冲区变化最多重新查询 3 次，这只是读操作，不会重新应用预设。显示器断连时清除旧状态并显示不可用。
- 打开面板、Refresh、切换后读取；监听 `WM_DISPLAYCHANGE` / `WM_SETTINGCHANGE` 并用单次 800ms 防抖，仅面板可见时刷新。没有显示状态定时轮询。部分驱动不会针对 HDR 变化发送消息，此时重新打开或手动刷新。
- 单次状态匹配只表示当前快照吻合，不代表已证明 DP 链路长期稳定。

## UI、定位与 DPI

深浅色圆角卡片、DWM 圆角/阴影、hover/pressed/active/disabled/loading、150ms 淡入与 8 DIP 位移。面板宽约 400 DIP，按内容自适应高度；小工作区内可滚动。图标为原创多尺寸监视器图案和系统 Fluent 字体，不使用 emoji 或第三方图标素材。

使用 `Shell_NotifyIconGetRect` 获得托盘图标的物理坐标，`MonitorFromPoint` / `GetMonitorInfo` 选择对应屏幕工作区，移动窗口至目标屏后用 `GetDpiForWindow` 计算尺寸。根据工作区边缘识别任务栏方向，自动隐藏时参考图标距离；图标位置查询失败时使用鼠标位置。支持负坐标屏幕布局、PerMonitorV2、100%–200% 缩放；实际系统组合需按验收表检查。系统仅在指定托盘显示图标，不强行复制到其他任务栏。

主面板设置为工具窗口、Topmost、不显示任务栏按钮，也不参与常规 Alt+Tab；设置窗口是独立普通窗口，方便操作。系统 DWM 策略决定最终阴影效果，未注入 Explorer 或修改系统 Quick Settings。

## 快捷键、主题与开机启动

| 操作 | 默认快捷键 |
|---|---|
| HDR 144 | Ctrl + Alt + H |
| SDR 180 | Ctrl + Alt + S |
| HDR Safe | Ctrl + Alt + J |

设置中修改组合，例如 `Ctrl+Shift+F8`；数字键可写 `D1`。单个输入留空可关闭该项，或取消 Enable global hotkeys 全部关闭。Windows `RegisterHotKey` + `MOD_NOREPEAT`，冲突会指出对应模式和 Windows 错误。Win 组合部分被系统保留，建议 Ctrl+Alt。应用不注册 Win+Alt+B。

主题默认 System，跟随 Windows 应用主题；也可选 Dark / Light。开机启动使用用户级 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`，不需要管理员权限、服务或计划任务。需要保存固定 exe 位置；移动后重新保存启动设置。任务管理器可能另外禁用了启动项，可在 Windows 的“启动应用”重新启用。

Display Settings 使用 `ms-settings:display`。HDR 快捷入口尝试 `ms-settings:display-hdr`，该 URI 在部分 Windows 版本可能回到设置首页；届时用 Display Settings → HDR。Microsoft 的通用 URI 清单没有给出跨所有版本稳定的专用 HDR 页面保证，所以本应用保留标准显示设置入口。

## 配置和日志

```text
%LOCALAPPDATA%\DisplayQuickControl\settings.json
%LOCALAPPDATA%\DisplayQuickControl\Logs\
```

配置通过临时文件原子替换，损坏或读取失败使用默认值并给出提示。保存路径、三个预设名、目标显示器路径、快捷键、主题、行为和启动偏好，不写安装目录。`tray.ico` 是本地缓存的原创图标。

日志最多保留最近约 7 个文件，每个约 1 MiB，按 UTC 时间记录启动、命令、显示状态、错误码。不会记录可执行文件路径、用户填写的预设名、显示器序列号、设备路径或原始 CLI 输出。日志写入失败不影响启动。

卸载：先关闭开机启动，退出应用，再删除程序目录；如不需要配置和日志，可删除上述 LocalAppData 目录。没有修改 Windows 系统文件。

## 开发与编译

开发环境：.NET 10 SDK；Windows 11 + Visual Studio 2026 / 合适的 C# 工具，或者命令行。最终程序只能在 Windows 运行，macOS/Linux 可以启用 Windows targeting 交叉编译，不能启动 WPF。

```powershell
dotnet build DisplayQuickControl.sln -c Release
dotnet run --project DisplayQuickControl
```

`dotnet run` 是开发方式；日常运行发布目录的 `.exe`。没有外部 NuGet UI/MVVM 依赖，小型 ObservableObject/Command 实现减少样板与依赖。WPF 运行库是平台组成部分。

目录结构：

```text
DisplayQuickControl/
  App.xaml[.cs]              生命周期、服务组装
  Models/                   配置、实际状态、目标模式
  ViewModels/               面板状态机、设置验证、命令
  Views/                    控制面板、设置窗口
  Services/                 Display、ColorControl、托盘、热键、启动、主题、单实例等
  Native/NativeMethods.cs   SDK 结构体和 P/Invoke
  Resources/                主题样式和原创图标
tests/                      安全测试、假 CLI 进程、Windows WPF smoke
scripts/                    发布和图标生成
```

## 发布 exe

自包含单文件（无需安装本应用的 .NET 运行时）：

```powershell
dotnet publish DisplayQuickControl -p:PublishProfile=win-x64-self-contained
dotnet publish DisplayQuickControl -p:PublishProfile=win-arm64-self-contained
```

Framework-dependent 单文件（体积小，目标 Windows 需要同架构 .NET Desktop Runtime 10）：

```powershell
dotnet publish DisplayQuickControl -p:PublishProfile=win-x64-framework
dotnet publish DisplayQuickControl -p:PublishProfile=win-arm64-framework
```

对应输出在 `artifacts/win-x64`、`win-arm64`、`win-x64-framework`、`win-arm64-framework`。亦可用 `./scripts/publish.ps1 -Runtime win-x64 [-FrameworkDependent]`。

等价直接命令：

```powershell
dotnet publish DisplayQuickControl -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o artifacts/win-x64
dotnet publish DisplayQuickControl -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=None -o artifacts/win-x64-framework
```

WPF 不启用 trimming / NativeAOT。自包含单文件首次启动会由 .NET 解包原生库到用户临时缓存，仍不要求安装；运行时无需所有源码。ColorControl 是用户另行安装和配置的独立程序，未打包进去。ARM64 交叉发布成功不代表 RTX 4060 / NVIDIA ColorControl 的 ARM64 驱动适用。

## 基础测试

```powershell
dotnet build tests/ColorControl.Fixture -c Release
dotnet run --project tests/DisplayQuickControl.Tests -c Release -- tests/ColorControl.Fixture/bin/Release/net10.0/ColorControl.exe
dotnet run --project tests/DisplayQuickControl.WindowsSmoke -c Release
```

macOS/Linux 的 fixture 文件没有 `.exe` 扩展名，运行时需要能定位 .NET（非标准 SDK 路径可设置 `DOTNET_ROOT`）。安全测试直接链接实际 Models / Services / Native 文件，以真实子进程检查参数、失败、超时、互斥和不重试，也检查 JSON 恢复与多 DPI 几何边界。WindowsSmoke 只在 Windows 交互桌面运行，检查 XAML 窗口、绑定、主题和热键冲突，不应用任何预设。

Windows CI 配置见 `.github/workflows/windows.yml`。无可访问显示会话时，DisplayConfig 的权限/不支持错误会明确 skip；GUI 或硬件验收不能靠这一结果代替。

## 常见问题

**HDR 切换黑屏一次正常吗？** 模式协商过程中可能短暂黑屏。本程序等待约 1.8 秒再查询；如果链路仍在变更可手动 Refresh。反复断连不属于一次切换成功的证明，应用不会重复触发。

**为什么 180Hz + HDR + 10bit 可能不稳定？** 更高刷新率和色深增加链路负载，具体是否需要 DSC、线材和接口质量、显卡驱动、显示器固件及其兼容性均可能相关。这里不能仅凭型号确定根因。三个预设用降低刷新率或色深作为用户可控的替代路径，不能承诺修复硬件问题。

**为什么 HDR 144 没有成功？** 核对路径、预设名称、目标屏和各设置 Include；确认 HDR 是 On 而非 Toggle；检查 ColorControl 官方日志及 Windows 高级显示。如果 API 不可用，状态会 Unknown，不会假装成功。可以手动选择 HDR Safe 或 SDR，不能自动重试。

**ColorControl 找不到怎么办？** 打开 Settings → Browse 重新选择 exe，确认未移动/删除。路径为空时应用仍可读取显示状态；ColorControl 启动失败还需检查它自己的运行时依赖。

**快捷键冲突怎么办？** 设置会显示注册失败项；改用其他组合、留空禁用该项、或取消全局启用后保存。设置可能已保存，而冲突项未注册，提示会明确说明。

**状态显示 Unknown？** 驱动、远程桌面、断连或 Windows 版本可能不提供相关数据。先确认目标屏仍连接并刷新；应用不会使用默认 10bit / RGB 填空。动态范围 Full 一直需要在 NVIDIA / ColorControl 中检查。

**ColorControl 报超时？** 本程序不会杀死它或再次应用，存活期间保持执行锁。检查 ColorControl 是否有等待处理的窗口，待它结束后再手动操作。不要因为暂时黑屏而连续启动本工具的副本。

**启动后没有主窗口？** 正常行为：程序进入托盘，图标可能在隐藏区域。再次启动 exe 可打开已有实例的面板。

## 官方实现依据

- [ColorControl README / CLI](https://github.com/Maassoft/ColorControl#command-line-interface)
- [ColorControl NVIDIA 执行源码](https://github.com/Maassoft/ColorControl/blob/master/ColorControl/Services/NVIDIA/NvService.cs)
- [Microsoft DisplayConfig 设备信息类型](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/ne-wingdi-displayconfig_device_info_type)
- [Microsoft Windows SDK wingdi.h](https://github.com/microsoft/win32metadata/blob/main/generation/WinSDK/RecompiledIdlHeaders/um/wingdi.h)（结构、位字段、类型值）
- [Shell_NotifyIconGetRect](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shell_notifyicongetrect)
- [Windows Settings URI](https://learn.microsoft.com/en-us/windows/apps/develop/launch/launch-settings)
- [.NET 官方支持周期](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)
