// =============================================================================
// NasSystem.Infrastructure - AssemblyInfo.cs
// 程序集级特性
// =============================================================================

using System.Runtime.CompilerServices;

// 转码参数构建（FfmpegTranscodeService.BuildTranscodeArguments）是纯静态
// 函数但是实现细节，不对外公开；单测直接覆盖分支（split/var_stream_map/
// 编码器/音频），集成测试验真实 ffmpeg 产物结构。同 Api 层做法放行测试程序集
[assembly: InternalsVisibleTo("NasSystem.UnitTests")]
[assembly: InternalsVisibleTo("NasSystem.IntegrationTests")]
