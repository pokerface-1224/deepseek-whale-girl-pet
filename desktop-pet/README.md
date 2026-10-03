# Windows 原生桌宠

由 DeepSeek Harness 插件自动启动，也可直接双击 `WhalePet.exe` 独立运行。请保留同目录的 `art` 文件夹。

支持透明背景、拖动、五种动作、点击恢复普通立绘和浅蓝色右键菜单。详细说明见项目根目录 README。

支持将本地文件拖到角色本体上投喂：直接移至回收站，成功后显示进食立绘与爱心动效，每个文件恢复10点饱腹值。支持多选，不接受文件夹；饱腹值可从右键菜单查看，规则详见项目根目录 README。

构建：`powershell -ExecutionPolicy Bypass -File tools/build-desktop-pet.ps1`。需要 Windows 自带 .NET Framework 4.x。
