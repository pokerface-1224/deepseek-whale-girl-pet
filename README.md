# 鲸鱼娘桌宠

随 DeepSeek Harness 启动，在 Windows 桌面显示鲸鱼娘。插件仅有 Host 入口，不注册 Harness 内部浮层、侧边栏召唤按钮或 Web 立绘路由。

## 安装

下载并解压发布包，在 DeepSeek Harness 的插件管理中安装解压后的本地目录（包含 `package.json`），并在桌面 profile 中启用插件。安装包自带 `WhalePet.exe`，不需要另行下载桌宠程序；仅支持 Windows。

## 启动与退出

桌面 profile 启用本插件时，Host 自动启动 `desktop-pet/WhalePet.exe`。安装或更新后，完全退出 Harness（包括托盘）再重新打开即可加载插件。

- 最小化 Harness 后，桌宠仍可见；Host 退出或禁用插件时，桌宠自动退出。
- 同一 Windows 登录会话只显示一个桌宠实例。
- 手动退出桌宠后，本轮不会强制重启；重启 Harness 可再次召唤。
- 直接双击 `desktop-pet/WhalePet.exe` 为独立模式，不跟随 Harness 退出。已有独立实例时，插件不会创建第二只。
- 非 Windows 平台不启动桌宠。启动失败不会阻断 Harness。

## 操作

- 左键拖动：移动位置并保存。
- 点击（包括双击）：立即恢复普通立绘，移除状态道具和提示。
- 右键：调整视角、大小、气泡、投影、置顶、回到右下角或退出。
- 闲置 12 秒：开始轮换思考、发呆、摸鱼、无聊、玩耍，每种状态持续约 10 秒；点击后重新计时。
- 右键「状态」可立即选择上述状态；「视角」切换普通立绘的正面、侧面、背面。

背景使用 Windows 分层窗口逐像素透明，透明区域可穿透鼠标；保留立绘边缘和投影的半透明度。五种状态使用按设定图新绘制的独立透明立绘：托腮思考、坐姿发呆、靠尾巴喝茶摸鱼、捧脸无聊、抱鲸鱼玩偶跃起；辅以轻微待机动画。这些是本地待机状态，未绑定 DSH 的模型推理状态。

## 文件

| 文件 | 用途 |
|---|---|
| `index.js` | 注册 Host 生命周期 |
| `native-host.js` | 启动原生进程，通过标准输入管道管理生命周期 |
| `desktop-pet/WhalePet.exe` | 原生 WinForms 透明桌宠 |
| `desktop-pet/src/Program.cs` | 原生桌宠源码 |
| `desktop-pet/art/*.png` | 普通三视角与五种独立动作透明立绘 |
| `cordis.patch.yml` | profile 插件注册 |
| `locale/*.json`、`icon.png` | 插件卡片文案与图标 |
| `art/raw/` | 原始立绘和美术检查图 |

## 构建与验证

```powershell
& ./desktop-pet/tools/build-desktop-pet.ps1
node tools/check_package.mjs
node tools/test_native_host.mjs
```

原生源码变更后需重新编译。清单检查验证无 Web 客户端入口以及原生资源齐全；生命周期测试验证真实窗口创建、单实例、管道断开退出和启动失败降级。
