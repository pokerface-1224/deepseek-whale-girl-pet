' 双击这个文件启动鲸鱼娘桌宠（VBScript 入口）。
'
' 相比 .cmd 启动器，它：
'   * 不经过 cmd.exe，所以没有代码页/中文路径的编码问题；
'   * 以隐藏窗口方式拉起进程，不会闪出黑色控制台；
'   * 路径写死为相对当前脚本，避免被当成命令解析。
'
' 如果双击后弹出系统权限对话框，那属于 Windows 对该可执行文件的策略，
' 与本脚本无关 —— 请看 tools\ 下说明或告诉开发者弹窗的原文。

Option Explicit

Dim shell, fso, here, exe
Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")

here = fso.GetParentFolderName(WScript.ScriptFullName)
exe = fso.BuildPath(here, "WhalePet.exe")

If Not fso.FileExists(exe) Then
  MsgBox "找不到 WhalePet.exe：" & vbCrLf & exe & vbCrLf & vbCrLf & _
         "请先运行 tools\build-desktop-pet.ps1 编译。", 16, "鲸鱼娘桌宠"
  WScript.Quit 1
End If

If Not fso.FolderExists(fso.BuildPath(here, "art")) Then
  MsgBox "找不到立绘目录 art\，无法启动。", 16, "鲸鱼娘桌宠"
  WScript.Quit 1
End If

' 0 = 隐藏控制台窗口，False = 不等待退出
shell.Run """" & exe & """", 0, False
