# RevitAgent Windows 打包与交付

项目使用 WiX 生成单文件、x64、按计算机安装的 MSI。安装包已经包含 GUI、CLI、.NET 10/WPF 运行时、Revit 2019–2022 执行器和内置技能，用户不需要另装 .NET。

## 构建机准备

- Windows x64
- .NET 10 SDK
- WiX Toolset v7：`winget install wixtoolset.WiXToolset`

安装 WiX 后重新打开 PowerShell，确认 `dotnet --version` 和 `wix --version` 均可执行。

## 一键生成安装包

先退出正在运行的 RevitAgent，然后在仓库根目录执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build-msi.ps1
```

脚本会执行以下操作：

1. 以 `win-x64` 自包含方式发布 CLI 和 WPF GUI。
2. 收集 Revit 2019、2020、2021、2022 四套执行器。
3. 收集内置技能。
4. 生成 WiX 文件清单并构建 MSI。
5. 计算安装包的 SHA-256 校验值。

最终交付文件：

- `revit-agent.msi`
- `revit-agent.msi.sha256`

`revit-agent.wixpdb` 只用于安装问题调试，不需要发给普通用户。

## 用户安装和首次配置

1. 双击 `revit-agent.msi`，允许管理员权限。
2. 从开始菜单启动 **RevitAgent**。
3. 在软件“设置”页填写 API Base URL、模型名称和 API 密钥，保存后新建会话即可使用。
4. 选择目标 Revit 版本和 `.rvt` 模型。

目标电脑必须安装需要使用的 Autodesk Revit 版本。安装包不包含 Revit 本体，也不包含任何 API 密钥。

## 发布前检查

- 在干净的 Windows 虚拟机上安装、启动、升级和卸载。
- 检查开始菜单图标、亮暗主题和长对话滚动。
- 分别用实际安装的 Revit 版本验证模型执行。
- 将 `Directory.Build.props` 和 `installer.wxs` 中的版本号保持一致。
- 正式对外分发前使用可信代码签名证书签署 EXE 和 MSI；未签名安装包可能触发 Windows SmartScreen 提示。

## 升级

发布新版本时提升版本号并重新运行 `build-msi.ps1`。WiX 使用固定的 `UpgradeCode` 和自动生成的 `ProductCode`，新 MSI 会替换旧版本；降级安装会被阻止。
