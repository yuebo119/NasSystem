// =============================================================================
// NasSystem.Api - AssemblyInfo.cs
// 程序集级特性
// =============================================================================

using System.Runtime.CompilerServices;

// 序列化上下文是内部实现细节，不对外公开；集成测试要断言源生生成真的接上了
// （OriginatingResolver 必须指向上下文本体），故按 Domain 层的同一做法放行
[assembly: InternalsVisibleTo("NasSystem.IntegrationTests")]
