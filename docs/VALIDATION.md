# 1.3.0 验证记录

当前源码调整（2026-10-08，尚未发布）：CLI 已停用原生语音发送，MP3 等音频使用 FILE 文件附件，保留原文件名和格式。新增离线验证见[音频附件验证](VALIDATION-AUDIO-ATTACHMENTS.md)。以下保留 1.3.0 原始记录与发布哈希，不代表当前修改后的字节。

日期：2026-10-04（Asia/Shanghai）。本版为纯 C# 迁移。历史 [1.2.1 记录](VALIDATION-1.2.1.md)和原证据保留，不转算为本版字节的验收。

## 最终结果

锁定依赖恢复、solution Release 构建、单元/codec/tool 检查、自包含发布、发布 EXE 的完整离线进程测试及发布后再次 locked restore 全部通过。构建零警告、零错误。原始最终目录：artifacts/tests/v130-final-source。

| 检查 | 结果与证据 |
| --- | --- |
| 单元 | 95 个具名组 + StateVault；八套件通过，[摘要](evidence/unit-summary-v130.json) |
| 纯 C# codec | 18/18，[摘要](evidence/voice-codec-summary-v130.json) |
| C# 项目工具 | 14/14，[摘要](evidence/project-tools-summary-v130.json) |
| 发布 EXE 进程端到端 | 65/65，[摘要](evidence/cli-e2e-summary-v130.json) |
| 完整 .NET 流程 | 八阶段均退出 0，[摘要](evidence/pipeline-summary-v130.json) |

最终进程检查于 2026-10-04 11:26:05 北京时间完成。177 个跟踪源码文件、六个发布入口文件的 SHA/大小与本机实际字节再次核对一致。进程测试明确 realWeChatDeliveryVerified=false；账号状态是专用 fixture，不能视为真实微信验收。

| 发布文件 | 最终 SHA-256 |
| --- | --- |
| weixin.exe | 1ABC717FAE57B8E7187434BC427EB8374EDE1E5182607F4F1DF6B47EF69AA25A |
| weixin.dll | 646D2EA89C76EFBE4D38160F3612A8CB01FDF8A764E0D0487D10591FBDBD0AEB |
| Weixin.Protocol.dll | 5E244DEDC8DDB98F262FEF91A1ECE21ADF109811C8BBE84B68F3E34021B9152B |
| Weixin.Silk.dll | 75B26CAF381506C8936638224F605960604F4044363B03B70B88D8DF1FD110B7 |

## 编解码与修复

生产 VoiceCodec 调用完整 Greepar/Jitsi 编码器及硬化后的 DrAbc 解码器，不启动其他语言的编解码器。18 组覆盖七种 API 采样率、WAV/PCM 同字节编码、12 个固定合成 SILK 向量、多帧及参考 PCM 哈希、短音频补齐、输出/时长保护、畸形包拒绝、逐帧取消、超时等待实际退出及八路并发。另有三个固定无效合成向量验证包继续超过五帧、声明继续但数据缺失、pitch 超过解码历史区域的拒绝。素材为合成音频，无真实账号或网络。

独立未改 Skype SDK 1.0.9 Decoder.c 解码新生产两秒 SILK：96000 字节 PCM 与 C# 逐字节一致，[参考摘要](evidence/managed-silk-sdk-reference-v130.json)。九个已建立参考的固定向量断言 PCM SHA；三个高采样率重采样向量只声称时长/样本正常，不能概括为全部 bit-exact。研究 C 可执行文件未分发、日常测试不调用。

独立审阅复现并修复三项格式问题：超长包静默转 PLC、超过五内部帧被截断、pitch 合法熵符号组合超过实际缓冲边界。保护先校验数组/指针区域再进入 DSP；畸形输入未变成成功音频。编码 staging 改为单一有界缓冲，避免 MemoryStream 扩容遗留旧音频数组；内部 DSP 状态仍由 GC 管理，未承诺全部清零。

初始 simplified encoder 15/16 的记录保留：原测试错误地把全零熵负载当作必然无效，但 SILK 无一般 CRC；修正测试为超初始区间全 FF，没有修改算法去符合错误预期。随后完整编码器 16/16、加入采样率与取消后的 17/17，以及边界修复后的 18/18 均留存于 artifacts/tests。原始独立复现与受保护后的复验记录均保留。

首次统一流水因两个旧测试项目的依赖锁文件尚未增加 Weixin.Silk 而在 locked restore 失败，保存在 artifacts/tests/v130-pure-csharp-final。显式更新锁后再 locked restore 通过。第二次完整 65/65 流水保留于 v130-pure-csharp-verified；清理一条旧 WASM 注释后重新构建并运行全部流水，最终以 v130-final-source 和上列字节为准。

## CLI 字段与恢复

本版新增三个进程组：七种显式采样率、位深以及各项独立省略；非法字段/命令组合在请求前拒绝；字段变化或 Unknown 不上传、不重发。断言读取实际 wire 的字段存在性及数值，未用 fixture BodySubset 缺失来伪证字段省略。无新字段时保持旧 payload fingerprint 字节，以便恢复先前 Sent。

原有扫码、收件过滤、游标、echo、重启去重、强制结束中的 Unknown、DPAPI 锁、CDN AES/大小/重试、Markdown 恢复、typing 生命周期和入站自动 SILK 解码均以最终 EXE 再跑。Console 测试需 dotnet run；dotnet test 不执行这些组。

## 真实微信：原生语音仍未确认

最终发布 EXE 实际把两秒 24 kHz PCM16 合成输入转成 Tencent SILK，得到 4756 字节、2000 ms。以 encode_type=6、sample_rate=24000、bits_per_sample=16 和独立业务键上传/发送；命令退出 0，唯一 Sent 保存。另一个文字提示“语音测试：2秒”也获唯一 Sent，并在电脑版机器人聊天直接可见。绑定状态没有积压 Inbox。

电脑版两次观察最新区域均未见本轮语音气泡，无法播放。客户端同时弹出存储不足提示，截至发布核查时尚未得到本轮手机确认；不能据此证明后端对所有账号都不支持，也不能声称本轮气泡成功。[公开证据](evidence/native-silk-v130.json)只保存合成字节 SHA、声明字段、状态计数及发布文件 SHA，不含账号、凭据、原聊天、媒体密钥或截图。原始状态与实机文件留在私有 artifacts。

历史手机语音回发同样没有成功气泡证据，文件音频附件仍与直接点播不同。官方公开正式路径继续把 audio 作为 FILE，原生外发条件未确定。文字、Markdown、视频、typing、文件的历史实机样本见 1.2.1；本版新增文字提示的真实可见证据不替代所有功能重测。

## 复现和发布审计

发布后的 MP3 单独试发见 [补充实验](EXPERIMENT-MP3.md)和[脱敏摘要](evidence/native-mp3-v130.json)。该次操作使用上列已验 EXE，没有更改生产代码；原 1.3.0 ZIP 不重新打包。

发布后又执行了[原版官方 Node 对照](EXPERIMENT-OFFICIAL-NODE.md)：七组模拟 HTTP 的本地断言通过，不是微信端到端验收。两笔实际 Node 发送各保存唯一 Sent、六请求 HTTP 200；电脑版显示 MP3 附件，未见手工 VOICE 的新气泡，手机未确认。C# 与 Node 的四组规范化 JSON 请求体一致，Content-Type 参数有差异，比较程序返回 1。原版 WASM 解码与独立 C 参考 PCM 不同，未声称位一致。完整范围见[新增证据](evidence/official-node-v130.json)；本次没有更改 C# 生产字节。

[WorkBuddy 5.6.2 安装包核查](EXPERIMENT-WORKBUDDY-562.md)另完成签名、17,800 个 ASAR 内部文件的 hash 校验、桌面及 CLI 调用链审阅，以及六组原始函数离线执行（6/6、真实网络为零）。三种音频均产生 FILE，视频产生 VIDEO；无文字 VOICE 的桌面下载 helper 返回 null，已有 ASR 文字可以提取。没有启动完整 WorkBuddy 或新增实机送达结果。43 个外置文件的索引差异已记录，未声称全包字节一致。详见[证据](evidence/workbuddy-562.json)。

运行 `dotnet run --project src/Weixin.Tools -- test-all --output <新目录>`，输出和版本发布目录都要为空。render-report、package、audit 同样为 C#；源码包包含 C#、固定合成向量、公开报告和静态上游研究/许可快照，成品不含 Node/WASM/Python/FFmpeg。打包后以当前绑定状态的已知凭据和指定私有媒体 SHA 审计，再验证公开源码克隆的字节与锁定构建；结果另附发布证据。
打包复核还发现本地旧 Node 下载 ZIP 被研究目录递归收录（未上传）。已加入研究二进制/脚本排除及审计拒绝，相关 C# 工具 14/14 重跑通过；公开源码包已降至约 1.2 MB。该修复只影响项目工具，最终生产源码和 EXE 六项哈希保持不变。
