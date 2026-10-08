# 音频文件附件验证（未发布）

日期：2026-10-08，UTC。基线：`5464efc77c8c89f3489f4806f4ef370d09db76db`。本记录和 JSON 证据反映离线验收完成时的快照：当时修改位于 `main`，尚未提交或推送，没有新建分支、创建 PR、连接真实微信账号或发送真实消息。原 1.3.0 发布文件与历史实验不变。后续源码提交不改变本次测试结果，也不代表 Release 发布或实机验收。

## 行为

MP3 使用 `send-media --file audio.mp3 --kind audio` 按 FILE 文件附件发送：上传 `media_type=3`，消息项 `type=4`，文件名和原始字节保持不变。`audio` 与 `file` 共用同一文件路径；WAV 等其他输入保留原扩展名和内容，不转码、不改名为 MP3。没有新增编解码依赖。

CDN 上传是 AES 加密字节，`Content-Type` 保持 `application/octet-stream`。FILE 描述符不添加原生 voice 字段或自造的音频 MIME 字段。

CLI 的 `--kind voice`、`--duration-ms`、`--voice-encoding`、`--voice-sample-rate` 和 `--voice-bits-per-sample` 在参数解析时拒绝，并提示 MP3 文件附件用法；拒绝发生在文件读取、状态打开及 HTTP 之前。接收语音、下载、编解码及协议库历史模型保留。默认绑定目录仍为 `%LOCALAPPDATA%\WeixinCSharp`。

FILE 的旧业务指纹保留时长、编码的两个空字段，避免升级后同一 `source-key` 误传。已有 `Sent` 可原样恢复；`Unknown` 与中断的 `Sending` 不自动重发。原生 VOICE 的旧记录不会被自动改写成 FILE。

## 离线结果

Windows x64，.NET SDK `10.0.400-preview.0.26322.102`（本机安装版本，由仓库 `global.json` 的 rollForward 规则选择；本次未修改 SDK 配置）。执行：

```powershell
dotnet run --project src/Weixin.Tools -c Release --no-restore -- test-all --output artifacts/tests/audio-attachment-full-20261008
```

| 检查 | 结果 |
| --- | --- |
| 锁定依赖恢复、发布后再次恢复 | 通过，锁文件未改 |
| Solution Release 构建 | 通过，0 警告、0 错误 |
| 协议单元与 Windows DPAPI | 8 套件通过，95 个具名组加 StateVault |
| 托管 codec | 18/18 |
| C# 项目工具 | 14/14 |
| 新生成的 Windows EXE 离线进程测试 | 65/65 |
| 完整流程 | 8 阶段全部退出 0 |

新增/替换回归核对 MP3/WAV FILE 类型、原文件名与上传解密后的完整字节、实际请求 MIME、无 voice 描述符、帮助与拒绝文案。测试分别持有状态独占锁、使用未绑定路径和不存在的输入文件，确认停用入口在状态与输入访问前拒绝。直接种入旧版 FILE 指纹的 Sent/Unknown/Sending 记录，核对恢复、别名兼容及不重发；入站 SILK 解码与本地转换回归仍通过。

测试 transport 没有底层网络 handler；只使用明确标记的 fixture 状态。MP3 测试使用已有的协议字节夹具，不是播放器解码验收。`Sent` 与退出 0 仅表示模拟 API 接受，不能称为实际送达或播放成功。

原始日志在 `artifacts/tests/audio-attachment-full-20261008`，本地构建产物在 `artifacts/win-x64-1.3.0`，它们均由 Git 忽略，未发布。可供复核的无账号摘要见[离线证据](evidence/audio-attachments-20261008.json)。独立代码审查未发现阻塞缺陷，`git diff --check` 通过。

## 实机边界

原生气泡失败的具体原因仍未证实；本次按产品方向停用 CLI 入口，没有宣称修复该能力。[历史官方 Node 对照](EXPERIMENT-OFFICIAL-NODE.md)与原描述符重放证据仍保留。

未来如要验证当前构建的真实附件收取，需要另行明确授权向哪一个绑定会话发送哪一个实际 MP3 文件、允许的消息数，并由收件端确认显示为同名文件附件、能够下载/打开和播放；应记录客户端版本、观察时间、文件 SHA-256 和播放结果。此验证当前未执行。源码提交与推送不包含 PR、Release 发布或真实微信发送。
