# 第三方来源与许可

协议实现参考 Tencent/openclaw-weixin 2.4.9。腾讯固定源代码快照保留官方 MIT 许可；随附原始许可：docs/Tencent-LICENSE.txt，源码包另有 research/upstream-snapshot/LICENSE。

官方源：https://github.com/Tencent/openclaw-weixin

1.2.0 的 MarkdownFormatting.cs 移植官方 src/messaging/markdown-filter.ts 的流式状态机；媒体请求、CDN AES-128-ECB/PKCS#7、随机 client_id 依据同一固定提交 24de5c9eb0dd5e595d7e2d090ed8a3f82870d42c 的公开实现。扩充的官方 MIT 快照随源码包提供，原版权声明及许可保留。C# 实现不是腾讯出品或对封号风险的保证。

1.2.1 的打字生命周期研究另固定 OpenClaw 2026.8.1 对应提交 ea806575e6450e4d1efdfc72c19f04be982a1b9b。源码包 research/typing-lifecycle-snapshot 保存所引用的腾讯调用器与 OpenClaw 生命周期文件、各自完整 MIT 许可、OpenClaw 原第三方通知及逐文件来源/哈希。它们是研究快照；本程序没有执行或安装 OpenClaw，也未捆绑这些通知中列出的整套应用依赖。C# 生命周期独立实现，停止时取消并等待在途请求的保护与该官方 SDK 有明确差异。

CLI 二维码生成依赖 QRCoder 1.7.0（MIT）。随附许可：docs/QRCoder-LICENSE.txt。

项目：https://github.com/Shane32/QRCoder

QRCoder 1.7.0 的传递依赖包括 System.Drawing.Common 6.0.0 和 Microsoft.Win32.SystemEvents 6.0.0（MIT）；PNG 渲染使用 QRCoder 字节输出，不调用 System.Drawing 渲染。实际 NuGet 包的原始许可与第三方声明分别随附于 docs/System.Drawing.Common-LICENSE.txt、docs/System.Drawing.Common-THIRD-PARTY-NOTICES.txt、docs/Microsoft.Win32.SystemEvents-LICENSE.txt、docs/Microsoft.Win32.SystemEvents-THIRD-PARTY-NOTICES.txt。

Windows 成品包含 .NET 10.0.3 运行时；该实际 runtime NuGet 包的原始许可及第三方声明随附于 docs/DOTNET-LICENSE.txt、docs/DOTNET-THIRD-PARTY-NOTICES.txt。

1.3.0 的完整托管 SILK 编码器取自 greepar/SilkCodec.NET，固定提交 01e40689c61e399e193524ea83a4f89e27604201。只收录 Managed 底层 C#，保留逐文件版权和命名空间，未收录 MP3/FFmpeg/NLayer 入口。原始 Apache-2.0 许可与包含 Jitsi、Skype BSD-3-Clause-Clear 的完整第三方声明分别位于 docs/Greepar-SilkCodec-LICENSE.txt、docs/Greepar-SilkCodec-THIRD-PARTY-NOTICES.txt。原/现文件哈希和本地修改记录见 research/greepar-silk-provenance.json。项目自有 MIT 许可不会覆盖这些文件。

托管 SILK 解码器及容器取自 DrAbcOfficial/SilkCodec.NET，固定提交 51205c364d685c78e64a0702474718358099caa3，原 MIT 许可见 docs/SilkCodec.NET-LICENSE.txt。本地增加严格格式拒绝、协作取消、输出保护、CDF/数组/指针边界检查，并拒绝将超长包静默转成 PLC。逐文件来源见 research/managed-silk-provenance.json。此组件内的简化编码器保留作源码对照，生产 VoiceCodec 编码调用前述完整编码器。

1.2.0/1.2.1 曾随附 Node.js 24.19.0 和 silk-wasm 3.7.1；1.3.0 已移除其运行时与执行脚本。旧许可和 provenance 归档于 research/legacy-codec-licenses，固定合成测试向量曾用其公开 API 生成，日常测试不执行它们。研究下载记录及静态 C/C++ 对照仍见 research/runtime-dependencies。

腾讯固定源码使用 silk-wasm 做入站解码；本项目的托管编解码和出站 VOICE 实验是独立扩展，不代表腾讯官方已提供原生语音外发路由。生产转换不需要 npm、Node、Python、FFmpeg、WASM 或外部 codec DLL。

旧 WorkBuddy 逆向项目及其他无明确 LICENSE 的 demo 仅作公开机制分析，不复制或分发其代码；见研究来源说明。
