# 实际验证记录

日期：2026-10-04（Asia/Shanghai）。本机 Windows x64，.NET SDK 10.0.103，Runtime 10.0.3。此记录对应 **1.2.0** 构建与测试；原 1.0.0 现场缺陷、1.1.0 修复后的文字联调，以及新增媒体的服务端确认分别说明。

## 构建与复现

入站语音识别条件修复后的最终完整流水实际执行：

```powershell
powershell -NoProfile -File scripts/Test-All.ps1 -OutputDirectory artifacts/tests/final-v120-voice-inbound
```

完整流程退出码 0：锁定依赖恢复 → 全 solution Release 构建（0 警告、0 错误）→ 单元测试 → 本地 codec 测试 → Windows x64 自包含发布 → 发布 EXE 的离线 CLI 进程端到端 → 发布后再次锁定恢复。Directory.Build.props 固定 RuntimeIdentifiers=win-x64；后一次 locked restore 通过，确认发布未导致锁文件漂移。

复现时为 OutputDirectory 使用新的目录，避免覆盖原证据。测试项目为 Console 程序，dotnet run 才执行这些检查；dotnet test 不运行它们。离线进程项目必须指向真实发布的 weixin.exe。

最终进程汇总完成时间为 **2026-10-04 07:22:01 北京时间**。以下为最终测试成品的 SHA-256，与 [最终进程证据](evidence/cli-e2e-summary.json)的发布 provenance 对应。旧 06:44:52 的 53/53 摘要另存为 [历史证据](evidence/cli-e2e-summary-064452.json)；后续发布若二进制变化，须重新运行并更新证据。

| 文件 | SHA-256 |
|---|---|
| weixin.exe | 5602D2145983424ADD1F7E755CAC37FA2AE64B86EC0C129E4061B441AF381344 |
| weixin.dll | A8BA0DA6D10D5460524E743DCC146EA0FB54348918EE50DBCC7156F9CE7E4E89 |
| Weixin.Protocol.dll | 903A69C9CF5A3E1CCC5168EF0DF5AD45B9DF4865C0702AB9584DCA8600F7454E |

## 单元回归

单元测试于 **2026-10-04 07:20:35 至 07:20:41 北京时间**执行，退出 0，七个套件全部通过。原始汇总为 artifacts/tests/final-v120-voice-inbound/unit-summary.json，分发副本为 [单元证据](evidence/unit-summary.json)。

| 套件 | 最终结果 |
|---|---|
| ILinkClientTests | 12 个具名组通过 |
| PersistentBotRunnerTests | 12 个具名组通过 |
| RunnerRegressionTests | 10 个具名组通过 |
| MediaModelAndMarkdownTests | 19 个具名组通过 |
| MediaClientTests | 7 个具名组通过 |
| TypingClientTests | 13 个具名组通过 |
| StateVaultTests | 套件通过，未逐组输出计数 |

前六项合计 **73 个具名测试组**；StateVault 另计为已通过套件，不编造其中的组数。单元测试使用模拟 HTTP 和真实 Windows DPAPI，不连接账号或发送微信消息。typing 覆盖请求头/字段、显式 ret=0、绑定范围内存缓存、缺票据、空成功响应、取消/超时及票据不进入持久状态或默认诊断。

覆盖可选 ret/errcode、合法空对象、已提供非零或错误类型结果码、空更新保留游标、绑定身份及记录关联、跨 bot/非完成态过滤、异常批次拒绝提交游标、同业务键不同内容拒绝、发送前本地校验、辅助通知失败不阻断接收、Sending/Unknown 恢复、取消及收件原子持久化。媒体组覆盖 AES-128-ECB/PKCS#7、两种密钥形式、媒体描述符、大小保护、明文图片、CDN 错误分类、下载冲突和取消；Markdown 组覆盖官方语法过滤、代理对、分段摘要与作业恢复。

StateVault 覆盖缺失文件、真实 DPAPI 加密往返及非明文检查、独占锁、损坏头部/密文/短文件、预取消/保存中取消、尺寸超限保留旧文件及释放后重开。

原 1.0.0 的 11 个 ILink 组和 12 个 Runner 组属于历史基线，不是本版最终总数。

## 实际本地 codec 验证

统一流水另执行 tests/Weixin.VoiceCodec.Tests，于 **07:20:45 北京时间**完成，**15/15 通过，0 失败**，退出 0；这 15 案例独立于上述 73 个单元组。实际运行固定 Node.js 24.19.0/silk-wasm 3.7.1，使用合成音频、不访问账号状态、网络请求为 0。覆盖 PCM/WAV 编码、SILK 解码、末帧/短输入补齐、终止包、格式/容量/时长保护、缺失运行时、实际子进程取消和超时，以及无原始诊断泄漏的桥接错误。

[codec 逐案证据](evidence/voice-codec-summary.json)及[独立互操作结果](evidence/voice-codec-interop.json)记录与固定上游 API 逐字节比较：encode/decode 均相同，合成样本 2000 毫秒、PCM 96000 字节、SILK 6804 字节。这个合成样本与实际手机未收到的 6440 字节 SILK 是不同测试产物，不能互换成手机成功证据。运行时来源与完整许可参见 runtime/voice/provenance.json。

## 发布程序的离线进程端到端

最终完整运行 **56/56 通过，0 失败，退出码 0**。原始汇总为 artifacts/tests/final-v120-voice-inbound/cli-e2e/tests-summary.json，记录逐案结果、时间、进程退出码、源码与测试成品哈希；分发副本为 [进程证据](evidence/cli-e2e-summary.json)。汇总明确标记 realWeChatDeliveryVerified=false，真实入站及手机结果另列。

测试真实启动 weixin.exe，通过 stdin、UTF-8 文件、实际进程退出及 Windows DPAPI 状态检查 CLI。覆盖扫码/验证码及二维码清理、文字过滤/echo、重启去重、client_id 持久化、断连/损坏确认后的 Unknown、不重发、发送中强制终止恢复、独占锁、Bot 会话失效、正常取消及停止通知、参数错误和状态隔离。

新增媒体案例覆盖图片/视频/文件/音频附件/原生 MP3 和 SILK 上传、CDN 重试/4xx、禁止未知 CDN、大小/时长/编码签名保护、强制终止恢复、相同媒体业务键跳过重复上传且拒绝改文件、四类媒体下载、损坏下载恢复、已有目标冲突、CDN 403 不误报 Bot 会话失效、明文图片及超限拒绝。Markdown 案例覆盖过滤、分段重放与部分 Unknown 不自动重试。

真实入站发现问题后新增三项离线进程回归，使用合成音频及固定运行时：

- encode_type=4 而实际字节为 SILK：保留原文件，并生成与 decode-voice 结果相同的正确 WAV。
- 缺少 encode_type 而实际字节为 SILK：同样自动生成正确 WAV，不靠声明枚举猜测编码。
- 实际不是 SILK：原字节完整保留，明确报告解码失败，不生成 WAV 或其他额外文件。

中间一次媒体测试 **41/42 通过**：一例 CLI 进程退出 0，但无标准输出，也无 HTTP 轨迹。原失败被保留；单例复查通过，不能据此抹去失败。测试器增加通用空输出强制失败断言后，再次完整运行 42 案例全部通过。该次异常的根因没有充分证据定位，不宣称所有偶发故障已解释。

加入 typing/codec 后的首次 53 案例运行为 **52/53**：唯一失败的 typing 两次 echo 案例只给 1.5 秒窗口，短于生产端至少 3 秒发送间隔。将测试窗口改为至少 5 秒，保留生产节流；06:44:52 历史统一流水完整 53/53 通过。另有一次全 solution 构建因并行真实验收 helper 占用旧 DLL 失败，结束监听并释放占用后重建为 0 警告/0 错误。这些中间未通过记录予以保留。

--offline-fixture <path> 必须配合专用 --state；夹具与状态位于带匹配哨兵的 fixture-UUID 目录，只接受虚构测试凭据。测试 handler 完全不创建 socket，没有夹具出错/耗尽后回退真实网络的路径，live 与 offline-fixture 状态不能混用。测试不读取默认绑定状态。强制终止测试实际终止模拟发送中的 CLI，仍不是在真实账号发送时终止进程的证据。

## 真实账号联调

本人已在手机扫码并确认，独立 DPAPI 状态已保存。原 1.0.0 首次 listen 因错误要求响应必须含结果码而退出；扫码成功不能写成旧版收发通过。官方结果码为可选，1.1.0 修复后重新启动监听。

修复后实际收到普通测试文字 wxe2e，用户在手机确认对应回复。随后采用手机容易输入的短口令 **ok7**：本机精确匹配入站文字，唯一来源键对应 Sent 记录，发送内容 SHA-256 与“已收到：ok7”一致，用户明确确认手机收到该回复。该次监听退出 0，待处理收件数为 0。脱敏证据为 [短口令记录](evidence/short-marker-evidence.json)和[手机人工确认](evidence/human-confirmations.json)。

先前长编号 B9D2 没有精确匹配，用户因手机输入不便改发短字母；这里不登记 B9D2 通过，也不把 wxe2e 当作最初提示的另一编号。

本轮 1.2.0 迭代的真实发送记录如下；各发送请求均获 API 确认并持久保存 Sent，但 API 接受与手机显示/播放是不同证据。实机发生于不同迭代，[打字状态证据](evidence/typing-evidence.json)和短口令证据分别记录当时三个程序文件摘要；它们不全部等于最终发布二进制。[出站持久记录](evidence/outbound-media-evidence.json)与人工确认共同说明已执行的媒体行为，不能改写为最终全部线上场景复测。

| 项目 | 已观察结果 | 手机端/返送核对 |
|---|---|---|
| 一段经官方规则过滤的 Markdown 文本 | API 接受，Sent 持久记录 | 用户明确确认正常接收；未逐项核验全部 Markdown 语法 |
| 2 秒 MP3 音频文件附件 | API 接受，Sent 持久记录 | 没有手机接收/播放确认，不记通过；不计为原生语音 |
| 2 秒原生 MP3 voice | API 接受，Sent 持久记录 | 没有手机送达确认，不记通过 |
| 随后发送的 2 秒原生 SILK voice | 上传及 VOICE 请求 API 接受，Sent 持久记录 | **本人明确“没有收到”；原生语音手机验收失败** |
| 补齐元数据的 2 秒原生 SILK voice | SDK helper 显式设置 encode_type=6、sample_rate=24000、bits_per_sample=16、playtime=2000；API 接受，Sent 持久记录 | **用户再次明确没有原生语音气泡；仍失败** |
| 真实手机语音原样回发，2800 毫秒 SILK | 3585 字节真实入站样本，完整 encode_type=6/24000 Hz/16 位/2800 毫秒元数据；API 接受，Sent 持久记录 | **用户仍明确没有原生气泡；真实样本原样回发也失败**，保留此前合成样本失败 |
| 3 秒蓝色 MP4 video | API 接受，Sent 持久记录 | 用户明确确认正常接收及直接播放通过 |
| 手机返送语音及视频 | 早先 180 秒及 300 秒 v7x 窗口没有样本；u8v 下载 2429 字节 .voice、290181 字节 .mp4，手动语音及完整视频解码成功；修复后新语音自动生成 2800 毫秒 WAV、Inbox=0、CLI/helper 退出 0 | 真实接收与语音自动解码通过；u8v 标记未匹配/helper 退出 1 保留，新语音模式没有文字 nonce；不记作媒体自动回传通过 |

早先零消息窗口的脱敏记录见 [入站媒体未验证证据](evidence/inbound-media-unverified.json)：2026-10-04 06:37:20 至 06:42:21 北京时间，expectedMessageObserved=false、otherMessageCount=0、downloadedMedia 为空。该窗口没有样本，不能判断问题发生在手机发送还是服务端/接收路径；保留原记录，不用后续成功覆盖它。

后续 **07:08:39 至 07:12:40 北京时间**的 [u8v 入站媒体证据](evidence/inbound-media-u8v.json)记录 otherMessageCount=3、两份实际下载文件、CLI 退出 0、Inbox=0。otherMessageCount 是消费 CLI 方括号输出行的计数，不是严格消息总数。它使用 06:44:52 历史发布 CLI DLL（D58B9C 开头），不是上表最终 A8BA0D 开头版本。语音为 2429 字节、SHA-256 48E922FCBEDE982622FF5D699E28462A4F3FEADFB907244046AA219DC538EF97；视频为 290181 字节、SHA-256 C4703728D3C7D123D7DD28DDFEB5A6B2B73C73FD890D1FCE8733A7864FCEB138。expectedMessageObserved=false、对应回复记录为空，辅助程序因此退出 1；这证明媒体已收到而指定口令未匹配，不能登记 u8v 精确文字验收通过。

实际语音带 0x02 + #!SILK_V3 腾讯签名，使用当时发布版 decode-voice 成功解码为 **2000 毫秒、96044 字节 WAV**。实际视频探测为 **HEVC 720×1280、AAC 44100 Hz 单声道、2.801667 秒**，FFmpeg 完整解码到 null 退出 0；该证据范围为本地格式解析及完整解码，详见 [原始媒体解析记录](evidence/inbound-media-inspection.json)。真实 VOICE 的 encode_type 不为 6，旧 listen 只按此枚举条件判断而没有自动解码；已改为按官方 VOICE 路径尝试 SILK，并由 codec 校验实际字节，这次手动解码与下一轮自动解码分别记录。用户称“只收到了文字”；--echo 的现有行为仅回复文字，没有把媒体再发回手机。公开包仅保留结果与摘要，不分发真实媒体本体或私有路径。

修复后的新窗口为 **07:19:41 至 07:22:42 北京时间**，使用显式 --expect-media voice、--exe 和起始为空的 --download-dir，无需文字 nonce；用户已明确确认发送了手机语音。在监听过程中实际保存 **3585 字节原文件及 134444 字节 WAV**，[自动解码检查](evidence/inbound-voice-autodecode-inspection.json)核实 RIFF、PCM 数据 134400 字节、24000 Hz、单声道、16 位和 **2800 毫秒**。原文件 SHA-256 C483C114865124FA9A32FDF2AA52E1259F6CBF8F8965EA52343956EC5A6A77D6，WAV SHA-256 9A205E5EB4EF4F783FD42FF2C4074A2C39766CC6611A9E21DF774ED2C77EB947。[监听结束证据](evidence/inbound-voice-autodecode-evidence.json)记录 expectedMedia=['voice']、expectedMediaObserved=true、CLI/helper 退出 0、Inbox=0、seenKeyCount=7。marker=null、expectedMessageObserved=false 是此次未要求文字口令的正常结果；不登记为精确文字标记通过。

该次 CLI DLL 摘要 **A8BA0DA6D10D5460524E743DCC146EA0FB54348918EE50DBCC7156F9CE7E4E89** 与协议 DLL 摘要 **903A69C9CF5A3E1CCC5168EF0DF5AD45B9DF4865C0702AB9584DCA8600F7454E** 均与最终全量流水产物相同，真实自动接收解码覆盖最终生产字节。它与旧 u8v 手动解码分别记录，不因入站成功改写原生语音出站失败。

媒体模式必须在本次空目录内出现相应 raw 与 WAV，并核对 CLI 退出码和媒体观测结果。生产 listener 按腾讯当前 media-download.ts/silkToWav 对每个 VOICE 尝试 SILK，不限定 encode_type=6；codec 校验实际签名、包结构及保护上限，其他编码或损坏输入保留 raw，不生成伪 WAV。最终流水后还为辅助程序补充 --expect-media 必须同时指定 --exe 的入口保护，避免不监听的空验收；该 helper 已单独构建为 0 警告/0 错误，使用无账号的负例验证拒绝组合且没有生成证据或状态。此辅助检查不计入 56 个发布 CLI 案例，也不宣称在该 guard 之后再次跑过全 solution 流水。

完整元数据的单次发送于 **2026-10-04 07:05:36 至 07:05:37 北京时间**完成，样本 6440 字节、2000 毫秒；请求与持久摘要见 [完整元数据证据](evidence/native-full-metadata-evidence.json)，手机再次未收到见 [人工确认](evidence/human-confirmations.json)的 outbound-native-silk-full-metadata 项。它只确认该次 API 接受与手机失败，不提供服务端失败原因或可用格式保证。

**07:26:08 至 07:26:10** 又使用同一 helper 原样回发真实手机语音，[回发元数据](evidence/native-real-voice-replay-evidence.json)记录 3585 字节、2800 毫秒、encode_type=6、24000 Hz、16 位，样本 SHA-256 与本次自动接收的 raw 一致，API 接受、Sent 持久确认，辅助程序退出 0。用户仍明确没有原生气泡，[人工确认](evidence/human-confirmations.json)追加 outbound-native-real-inbound-silk-replay、phoneReceived=false；**真实样本原样回发也失败**，排除了本次仅因合成样本而失败的解释，既往两次失败不抹去。上传与发送 API 接受不等于手机送达，尚无明确服务端支持条件，不能据此断言全部账号/格式不支持。只分发元数据和摘要，不复制声音本体。该测试协议 DLL 为 07CD9A 开头的辅助版本，不作为最终 903A69 开头生产 DLL 的实机发送复测，也不计入 56 个离线进程案例。

该实验使用 tests/Weixin.LiveE2E 新增的显式 --native-silk 模式，要求 --state、--native-silk <文件>、--source-key <64位十六进制业务键>、--output <证据文件>，不允许同时指定 --exe。它先解码取得实际时长再发送完整元数据，拒绝已有业务键的再次上传或发送。模式在 06:44:52 历史生产流水之后新增；已单独运行 dotnet build tests/Weixin.LiveE2E/Weixin.LiveE2E.csproj -c Release --no-restore，0 警告、0 错误，并实际执行一次。该次辅助程序加载的 Weixin.Protocol.dll SHA-256 为 **07CD9A6F895660B6B68EC525BE91E1B8B131B95FB73BAD67DB0CF772FA3426AB**，不等于上表当时发布协议 DLL 的 **903A69C9CF5A3E1CCC5168EF0DF5AD45B9DF4865C0702AB9584DCA8600F7454E**。该 helper 测试本身未修改生产源码及成品；不计入当时 73 个单元组、本地 codec 15 案例或发布 CLI 53 案例，也没有据此宣称再次跑过全 solution 流水。

音频附件不代替原生语音气泡验收，保存原始 SILK 不代替解码或通用播放验收。确认须能对应已发送测试内容；SendReceipt.Sent、HTTP 2xx 或模拟字节往返均不足以独立证明手机实际送达。某次监听正常退出或重启成功，也不代表真实断网恢复或无限期稳定。

本次合成 SILK 省略和补齐上述元数据的两次请求及真实手机 SILK 原样回发均出现 API 接受而手机未收到，MP3 原生请求也没有取得手机送达确认，不能将原生语音发送登记为通过。固定官方 VoiceItem 的采样率、位深与时长等均为可选字段，未声明补齐 24000 Hz/16 位即可原生送达。官方当前 main 仍为 24de5c9eb0dd5e595d7e2d090ed8a3f82870d42c，最新稳定 tag v2.4.9 未新增原生 voice 出站入口，现有 audio/* 发送按 FILE 附件处理。官方仓库 issues 215/126/91/209 中的相似复现均属用户报告，核查未见可核实维护者承诺；不能把报告中的“曾经成功”或类型声明当作正式可用保证。

打字状态进行了两轮真实测试。第一轮设置 30 秒测试窗口，用户反馈“没有看到”；按用户要求再次测试，第二轮设置 60 秒，开始请求被 HTTP 接受后用户明确确认“有了”。两轮 CLI 均退出 0，开始/取消请求均获 HTTP 接受。第二轮开始显示手机验收通过，保留首轮未见事实；没有确认指示器持续显示了整个窗口，phoneCancelConfirmed 仍为 null，取消不记为手机确认已消失。

本地 prepare-voice/decode-voice 使用随包 Node.js 24.19.0 与 silk-wasm 3.7.1，不读取账号状态或发送网络消息。PCM16 单声道 24000 Hz 编码可形成腾讯 SILK 头；接收 VOICE 时由 codec 校验实际 SILK 字节并尝试解码为 WAV，失败保留原文件。腾讯插件实际使用 decode；本程序新增 encode 属使用同依赖公开 API 的扩展，本地编解码通过不能作为原生 voice 手机可用证据。

真实凭据、二维码及原始聊天不进入分发包；本地绑定状态供本人保留，不导出 token。公开记录只保留脱敏结果、验收口令及版本/摘要信息。

## 电脑版原描述符回发实验

本机微信版本 **4.1.15.13**。北京时间 **07:50:41 至 07:50:43**，LiveE2E 新增的显式 --replay-inbound-voice 模式处理一次真实入站 VOICE，保留其原字段、原 CDN 描述符及对应消息上下文发送 Bot 回复。样本声明 encode_type=4、sample_rate=16000、bits_per_sample=16、playtime=2320；实际 1600 字节通过腾讯 SILK 签名及 codec 校验，解码成 111404 字节、2320 毫秒 WAV。该观察说明这一次声明编码与实际字节格式不同，不改写官方枚举映射，也不推定全部语音如此。

[公开标量证据](evidence/native-descriptor-replay-public.json)仅保留元数据、摘要和状态：API 接受，Sent，handlerAcknowledged=true、pendingInboxCount=0、停止通知已确认；不公开 CDN URL/查询参数、AES、上下文、原始文本或声音。原字段与原 CDN 同时保留，不能从本实验推导单字段的因果关系；原 CDN 复用不作为生产功能支持。辅助代码单独 Debug 构建 0 警告、0 错误，协议 DLL 摘要 **EEF0CFAEB46A572C6D6D902B713107991970BA634AF28472482E46D4ED1118B9** 与最终 Release 协议 DLL 不同；生产源码及 73/15/56 的来源未变，不将新 helper 计入这 56 案例。

截至 **07:51:42**，距上述实验开始至少 60 秒，电脑版仍没有新的 Bot 原生语音气泡，按该窗口记失败；不能排除未知更长延迟。此前三次手机 SILK 失败不覆盖、不删除。新短口令 **d9k** 于 **07:50:58 至 07:51:44** 由最终发布 EXE 精确接收，唯一 Sent 记录与回复摘要匹配、CLI 退出 0、Inbox=0，电脑版直接可见完整“已收到：d9k”。[文字证据](evidence/desktop-text-evidence.json)的 EXE/CLI/协议 DLL 摘要分别为 5602D2/A8BA0D/903A69 开头，与最终流水相同；此条真实文字验证覆盖最终生产字节。

[电脑版综合观察](evidence/desktop-client-e2e-public.json)另记录：旧蓝色三秒视频点击后官方播放器进度到 00:03；旧 Markdown 卡片的 bold/inlinecode 测试内容以普通文本可见，不扩展为全部语法渲染已验证；音频 FILE 显示为附件，未据此验证播放或原生气泡。未保留截图或联系人信息，原生语音目标尚未通过。

本轮最终 EXE typing --run-for 60 于 **07:52:03 至 07:53:04**运行。开始请求获 HTTP 接受，顶部直接可见“对方正在输入…”；在本轮进程退出前已恢复 Bot 名，未记录消失的精确时刻。到期自动取消报“微信打字状态连接失败”、**CLI 退出 2**，没有打印自动取消已获 HTTP 接受，见 [定时失败证据](evidence/desktop-typing-evidence.json)。顶部消失发生在取消确认和单独 stop 之前，原因未知，不能归因自动取消、显式 stop 或推定具体 TTL，也不能登记完整 60 秒持续显示。

随后 **07:58:33** 单独运行 stop，打印“打字取消请求已被 HTTP 接受”、**CLI 退出 0**，见 [显式 stop 证据](evidence/desktop-typing-stop-evidence.json)。之后 UI 仍无 typing；只确认这次 stop 的 HTTP 成功，不将已发生的消失归功于 stop。定时 CLI 失败、开始显示确认和显式 stop 成功分别保留；此前手机 phoneCancelConfirmed=null 不改。生产源码未变，73/15/56 仍对应原最终流水；新增 Debug helper 与本轮 UI 实验不计入这些测试。

## 原在线探测与报告检查

原 C# probe 与独立 PowerShell Probe-Qr.ps1 均成功：HTTP 200、ret=0，存在 qrcode/qrcode_img_content 字段。probe 只创建短期二维码，不扫码或收发账号消息；真实扫码及消息证据另列。原离线 demo 解析 message_id=18446744073709551615 保持精度；qr-demo 的 example.com 内容不能用于微信登录。

HTML 报告由本地脚本生成。原报告核对过结构，但浏览器视觉检查未完成：当时 Browser 后端未连接且工具拒绝 file://；没有通过更换 URL 或浏览器表面绕过限制。当前结论以本 Markdown 记录与脱敏测试证据为准，生成 HTML 本身不证明真实服务验收。

## 未验证范围及交付核对

真实网络中断/恢复、24 小时以上连续运行、跨设备状态迁移、长期账号限制或封禁情况，以及全部服务端状态/灰度路由尚未验证。24 小时为报告提出的投产观察建议，原始用户没有指定该时长，本轮未执行，不记为通过。扫码验证码/路由分支的离线通过也不能写成真实账号遇到过这些状态。

公开字段和请求头按腾讯源码默认值对齐，bot_agent=OpenClaw 仅用于官方说明的日志监控，不参与鉴权或路由，没有免封证明。DPAPI 与脱敏减少本地意外暴露，不隐藏合法服务端必要鉴权字段。不能保证零封号或永久稳定。

源码/Windows 分发包的文件哈希见同目录 SHA256SUMS.txt，可用 Get-FileHash -Algorithm SHA256 核对。打包允许清单只纳入代码、报告、许可及脱敏证据；应核对包中没有 .git、bin/obj、临时二维码、DPAPI 状态、真实聊天或未明确许可的第三方参考源码。包条目检查与哈希不能代替上述运行测试。
