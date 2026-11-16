# Srdp

极简现代远程桌面管理器（Windows 内嵌 RDP 引擎）。

## 特性

- **原生引擎**：内嵌 Microsoft MsTscAx ActiveX（与 mstsc 同源），画质与兼容性无损
- **会话分辨率跟随窗口**：缩放窗口 / 全屏后自动重协商分辨率，始终 1:1 原生像素，无缩放模糊
- **真全屏**：无边框覆盖整屏，顶部 mstsc 风格连接条（悬浮展开、离开自动缩回）
- **凭据加密**：密码 DPAPI 加密存储（绑定当前 Windows 用户），连接时自动填充通过 NLA
- **连接分组**：自定义分组、组折叠、右键移动连接
- **多标签**：同时连接多台主机，状态点实时显示（连接中/已连/断开）
- **现代暗色 UI**：无边框窗口、可折叠侧栏、全套暗色弹窗

## 编译

无需安装 SDK，使用系统自带的 .NET Framework 4.8 编译器：

```bat
csc /nologo /target:winexe /platform:x64 /out:Srdp.exe ^
  /r:"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\PresentationCore.dll" ^
  /r:"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\PresentationFramework.dll" ^
  /r:"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\WindowsBase.dll" ^
  /r:"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\WindowsFormsIntegration.dll" ^
  /r:"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.Xaml.dll" ^
  /r:"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.Windows.Forms.dll" ^
  /r:"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.Drawing.dll" ^
  /r:"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\Microsoft.CSharp.dll" ^
  /r:"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.Security.dll" ^
  Program.cs
```

## 使用

- 单击连接即建立会话；右键可编辑 / 移动分组 / 删除
- 标签页 ⛶ 或标题栏 ⛶ 进入全屏，顶部连接条悬浮展开退出
- 数据保存在 `%APPDATA%\Srdp\connections.json`（密码为 DPAPI 密文）

## 许可

MIT License
