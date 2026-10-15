# 本次构建验证记录

日期：2026-10-15（Asia/Shanghai）。主机：macOS ARM64。SDK：.NET 10.0.402。

- `dotnet build DisplayQuickControl.sln -c Release`：通过，0 警告、0 错误，包含 WPF 主项目及 WindowsSmoke 项目。
- 安全测试：52 项断言全部通过。使用真实启动的模拟 ColorControl 子进程，不修改显示设置。
- Windows x64 自包含单文件发布：通过，PE x86-64 GUI executable。
- Windows ARM64 自包含单文件发布：通过，PE Aarch64 GUI executable。
- Windows x64 / ARM64 Framework-dependent 单文件发布：通过。
- Windows WPF smoke：已实现、已编译；当前 macOS 无法运行，未标为通过。
- 真实 Windows DisplayConfig、托盘/混合 DPI、HDR/刷新率/色深及海信 / NVIDIA 硬件切换：当前主机无法执行，待实机验收。
- GitHub Actions：已提供 workflow，尚未上传或执行云端任务。

测试覆盖刷新率容差、无效状态、分辨率/色深/编码不匹配、原生结构布局、100%–200% DPI 定位几何、四边任务栏、负屏幕坐标、JSON 损坏恢复/Unicode保存、日志保留、CLI 参数边界、非零退出和零退出错误输出、并发拒绝、超时锁和零自动重试。

自定义模式新增覆盖：不同分辨率/小数刷新率/YCbCr 状态匹配、非法目标和重复模式拒绝、唯一备用模式、旧版配置及空快捷键迁移、草稿隔离、模式添加/删除/排序后的持久保存。WindowsSmoke 已加入编辑器表单校验、添加/排序/删除、备用模式互斥、窗口和动态卡片绑定检查；当前主机只能编译这些 WPF 检查，未在 Windows 执行。

硬件与交互验收步骤见 [windows-validation.md](windows-validation.md)。发布包校验值见 `artifacts/SHA256SUMS.txt`。
