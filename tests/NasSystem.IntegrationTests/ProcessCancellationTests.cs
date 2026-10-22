// =============================================================================
// NasSystem.IntegrationTests - ProcessCancellationTests.cs
// 外部进程取消语义：客户端断开时必须终止子进程，不能留孤儿
// =============================================================================

namespace NasSystem.IntegrationTests;

using System.Diagnostics;
using FluentAssertions;
using NasSystem.Infrastructure.Media;
using Xunit;

/// <summary>
/// 锁定 <c>FfmpegTranscodeService.RunProcessAsync</c> 的取消契约：
/// 取消令牌触发后，子进程必须被终止。用只写文件的子进程做被测对象，
/// 它对 stdout/stderr 管道关闭不敏感，因此"是否还活着"只由是否真的 Kill 决定。
/// </summary>
public sealed class ProcessCancellationTests : IDisposable
{
    private readonly string _workDir;
    private readonly string _marker;

    public ProcessCancellationTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), $"proc-cancel-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workDir);
        _marker = Path.Combine(_workDir, "marker.txt");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_workDir, recursive: true);
        }
        catch (IOException)
        {
            // 子进程已终止时不应占用；异常留给系统临时目录
        }
    }

    [Fact]
    public async Task RunProcessAsync_ClientCancelled_ChildProcessTerminated()
    {
        var (fileName, arguments) = BuildLongRunningWriter(_marker);
        var childPid = 0;
        using var cts = new CancellationTokenSource();

        var run = FfmpegTranscodeService.RunProcessAsync(
            fileName, arguments, cts.Token, timeout: null, onStarted: pid => childPid = pid);

        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!File.Exists(_marker) && DateTime.UtcNow < deadline)
            await Task.Delay(50);
        File.Exists(_marker).Should().BeTrue("子进程应已开始写标记文件");
        childPid.Should().BeGreaterThan(0, "启动回调必须给出子进程 PID");

        cts.Cancel();
        Func<Task> act = async () => await run;
        await act.Should().ThrowAsync<OperationCanceledException>();

        var exited = await WaitForExitAsync(childPid, TimeSpan.FromSeconds(5));
        exited.Should().BeTrue("客户端取消后必须终止子进程，不能留孤儿继续跑");
    }

    private static (string FileName, string Arguments) BuildLongRunningWriter(string marker)
    {
        // 子进程只写文件、不依赖 stdout/stderr：父进程释放管道不影响它的存活，
        // 于是"子进程是否还在"成为是否真的执行了 Kill 的判据
        if (OperatingSystem.IsWindows())
        {
            var script =
                $"$f = '{marker}'; 1..60 | ForEach-Object {{ Add-Content -LiteralPath $f -Value 'x'; Start-Sleep -Milliseconds 200 }}";
            return ("powershell.exe",
                $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{script}\"");
        }

        var sh = $"i=0; while [ $i -lt 60 ]; do echo x >> '{marker}'; i=$((i+1)); sleep 0.2; done";
        return ("/bin/sh", $"-c \"{sh}\"");
    }

    private static async Task<bool> WaitForExitAsync(int pid, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (!IsAlive(pid)) return true;
            await Task.Delay(100);
        }
        return !IsAlive(pid);
    }

    private static bool IsAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}