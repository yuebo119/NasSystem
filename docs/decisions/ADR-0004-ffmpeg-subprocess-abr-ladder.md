# ADR-0004：视频转码采用 FFmpeg 子进程多码率阶梯，不引入进程内绑定

- 状态：已接受
- 日期：2026-10-19
- 相关：ADR-0003、docs/ROADMAP.md §3 Phase 3、docs/ROADMAP.md §9 第七轮

## 背景

Phase 3 落地的视频 HLS 转码是单档 1080p：FFmpeg 子进程一次调用输出一路
index.m3u8 加 ts 分片。这满足"移动端播放 1080p 子流"的初始目标，但不是
自适应（ABR）——弱网环境下播放器无法切档，只能缓冲或断流。

2026-10-19 调研了 C#/.NET 侧的"高性能自适应转码器"选项，结论分三层：

1. .NET 10 BCL 无任何媒体编码能力（发布亮点是 Blazor、minimal API 验证、
   JSON Patch、SSE、密码学与 JIT 性能），社区共识十几年一致：转码用 FFmpeg。
2. 进程内绑定唯一成熟选项是 FFmpeg.AutoGen 9.0.1.1（2026-08 发版，已从
   LGPL 转 MIT，绑定 FFmpeg 9.0.1，osu! Framework 等在生产使用）。许可
   障碍已消除，但 NuGet 包只含绑定 DLL，原生库需自备。
3. 硬件编码器直连绑定（NvEncSharp 等）为爱好者级：作者自述只测过 64 位
   Windows、大量 API 未测试、可能有转录错误。

## 决策

保持 FFmpeg 子进程架构，把单档升级为**一次调用出多码率阶梯**，并加一层
硬件编码探测回退：

1. **阶梯**：`ffmpeg -i` 探测源分辨率（不依赖 ffprobe 二进制），标准阶梯
   1080p/720p/480p 取高度不超过源的档位，不上放大；源低于 480p 出单档
   且高度=源高度。质量仍由 CRF 23 锚定，每档 `-maxrate`/`-bufsize` 锁进
   ABR 车道。
2. **一次调用多路**：`filter_complex` split + `var_stream_map`，一次解码
   N 路编码，输出 master.m3u8 加 v{N}/ 子目录。音频按探测结果每档映射
   （无音频源不映射，`-map 0:a` 会直接失败）。
3. **硬件编码探测**：对 h264_nvenc/qsv/amf/vaapi/videotoolbox 各跑一次
   320x240 真实小编码，产物非空才判可用。**编译进 FFmpeg 不等于运行时
   可达**——实测某开发机 qsv 在 `-encoders` 列表可见但 MFX session 创建
   失败。探测结果 TTL 缓存；探测到硬件用 `-b:v` 车道码率，否则回落
   libx264 + CRF；硬件运行时失败（如容器忘透传设备）自动降级软件重试一次。
4. **寻址**：master/variant/segment 由同一个 catch-all 路由服务，磁盘
   v{N}/ 布局经 HlsUrlRewriter 翻译为 `{视频路径}?variant=N&segment=xxx`
   query 寻址，段名仍过白名单。缓存键 = 路径 + 阶梯签名 + 编码器。
5. **诚实降级延续**：ffmpeg 缺失 501、探测失败降级单档 1080p、当前编码器
   与阶梯在配置页如实展示。

## 依据

- 自适应转码的性能大头在编码器，不在 .NET 与 FFmpeg 的边界。子进程方案
  已把内存风险归零；AutoGen 的"一次解码多路编码"收益，`filter_complex`
  命令行原样能拿到，不值得为之引入 unsafe 代码面与跨平台原生库分发。
- 硬件编码是唯一的真实数量级加速（5 到 10 倍），通过 FFmpeg 编码器后端
  拿到，不绕过 FFmpeg。
- 探针实测（2026-10-19，static ffmpeg build）：NVENC 可用（64x64 低于
  NVENC 最小维度，探针尺寸须 320x240）；QSV 不可用（MFX session -9）。
  证明"真实小编码探测"是必要设计，`-encoders` 列表会误报。

## 后果

- 播放侧零改动：hls.js 对 master playlist 自动 ABR，播放 URL 不变。
- 部署侧：Docker 镜像已装 ffmpeg；硬件加速需 `--gpus all`（NVENC）或
  `/dev/dri` 透传（QSV），缺失时自动回落软件编码，功能不降级。
- 转码缓存键变更，旧单档缓存自然废弃（清理入口已有）。
- 放弃的备选：FFmpeg.AutoGen 进程内绑定（内存风险与集成成本不对等）、
  NvEncSharp 直连（不成熟）、纯托管转码器（不存在）。

## 复核记录

- 2026-10-19 实现同日端到端验证：真实 1080p 带音频源经 API 上传，master
  三档（1920x1080/1280x720/854x480）、variant 子列表重写、segment 200
  （video/mp2t）、hls.js 视角的 URL 解析链全通；配置页展示 NVENC 硬件
  编码与三档阶梯。单测 192 + 集成 232 + 前端 166 全绿。
