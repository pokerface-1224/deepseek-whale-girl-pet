# 鲸鱼娘桌宠 (DeepSeek Whale Girl Pet)

[![Code License: MIT](https://img.shields.io/badge/Code_License-MIT-blue.svg)](LICENSE)
[![Art License: CC BY-NC-SA 4.0](https://img.shields.io/badge/Art_License-CC_BY--NC--SA_4.0-orange.svg)](LICENSE)
[![Platform: Windows](https://img.shields.io/badge/Platform-Windows-0078D6.svg)](#系统要求)

专为 **DeepSeek Harness** 设计的 Windows 原生桌面宠物插件。将鲸鱼女仆带到你的桌面，支持多种交互动作，并提供与 DeepSeek Harness 实时同步的迷你操作面板。

---

## ✨ 核心特性

- **轻量原生桌面伴侣**：基于 Windows 分层窗口实现像素级透明与点击穿透，支持自由拖动、贴边停靠与多视角切换（正面/侧面/背面）。
- **丰富互动与微表情**：摸头互动、待机打瞌睡、沉思、摸鱼等多种生动动作状态。
- **迷你 Web 面板 (WebView2)**：右键一键呼出迷你面板，轻量快捷交互。
- **多端会话双向同步**：迷你面板与 DeepSeek Harness 主窗口之间双向实时同步激活会话，换端操作无缝衔接。
- **生命周期协同**：随 DeepSeek Harness 自动托管启停，单实例互斥保护。

---

## 🛠️ 系统要求

- **操作系统**：Windows 10 / 11 (x64)
- **运行环境**：
  - .NET Framework 4.6.2 及以上（Windows 系统内置）
  - Microsoft Edge WebView2 Runtime（Windows 10/11 通常已内置）
- **宿主程序**：[DeepSeek Harness](https://github.com/deepseek-ai/deepseek-harness)

---

## 🚀 安装与使用

### 作为 DeepSeek Harness 插件使用
1. 将本项目目录放置在 DeepSeek Harness 插件目录或以私有插件引入。
2. 在对应 profile 的配置中启用 `@local/dsh-pet-whale`。
3. 启动 DeepSeek Harness，桌宠将自动在屏幕右下角唤起。

### 常用操作
| 操作 | 响应 |
|---|---|
| **左键拖拽** | 移动桌宠位置并自动保存坐标 |
| **左键双击** | 摸头互动并恢复默认正面立绘 |
| **鼠标右键** | 唤出快捷菜单（切换视角、动作状态、暂时隐藏、置顶设置等） |
| **打开迷你面板** | 右键菜单选择“打开迷你面板”，快捷唤出与主端同步的对话窗口 |
| **召唤/重新唤醒桌宠** | **侧边栏图标**：点击 Harness 左下角鲸鱼娘专属图标<br/>**斜杠命令**：输入 `/whale-girl` 并回车<br/>**全局快捷键**：按下 `Alt+W` |

---

## 💻 本地构建与测试

如需二次开发或重新编译原生程序：

```powershell
# 编译 C# 原生桌宠程序 (WhalePet.exe)
& ./desktop-pet/tools/build-desktop-pet.ps1

# 执行静态包结构校验
node tools/check_package.mjs

# 执行会话同步机制单元测试
node tools/test_sync.mjs

# 校验客户端入口与原生进程确认协议
node tools/test_client.mjs
node tools/test_native_host.mjs

# Windows：实际启动桌宠，验证隐藏后唤醒与重复实例唤醒
node tools/test_native_host.mjs --native
```

---

## 📄 开源许可证

本项目采用分项许可协议：

- **代码部分**：基于 [MIT License](LICENSE) 许可证开源。涵盖所有源代码、配置、编译构建脚本与底层通信实现。
- **美术与角色图像素材**：涉及“鲸鱼娘”形象的所有角色立绘、动作素材、应用图标及预览图（包含 `desktop-pet/art/` 目录下的所有图像素材、`icon.png` 等）采用 **[CC BY-NC-SA 4.0 (知识共享 署名-非商业性使用-相同方式共享 4.0 国际许可协议)](https://creativecommons.org/licenses/by-nc-sa/4.0/deed.zh-hans)** 授权。
  - 允许在非商业用途下免费使用、传播与二次创作；
  - 二创或转载须保留原作者署名，并以相同协议进行共享发布；
  - **严禁任何商业盈利性用途**。
