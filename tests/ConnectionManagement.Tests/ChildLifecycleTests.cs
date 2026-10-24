using System.Diagnostics;
using System.Security.Cryptography;
using Kordra.Modules.ConnectionManagement.Keychain;
using Xunit;

namespace Kordra.Credentials.Tests;

public sealed class ChildLifecycleTests
{
    [Theory]
    [InlineData("success")][InlineData("nonzero")][InlineData("hang")][InlineData("unreaped")][InlineData("stderr")][InlineData("launch-hang")][InlineData("cancelled-before-start")]
    public async Task Exact_owned_process_is_bounded_reaped_or_explicitly_unknown(string mode)
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"Kordra.slnx")))root=root.Parent;
        var executable=Path.Combine(root!.FullName,"src/Host/Kordra.Api/bin/Release/net10.0/Kordra.Api");
        var installation=Guid.NewGuid();var reference=Guid.NewGuid();var request=new KeychainRequest(1,Guid.NewGuid(),installation,reference,"/private/synthetic-do-not-open.keychain");
        var configuration=new KeychainAccessConfiguration(installation,executable,Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(executable))),new Dictionary<Guid,string>{{Guid.NewGuid(),request.KeychainPath}});
        using var output=new MemoryStream();await KeychainProtocol.WriteResultAsync(output,request.RequestId,[0,255,42],default);
        var process=new FakeProcess(output.ToArray(),mode);var factory=new Factory(process);
        using var cancellation=new CancellationTokenSource();if(mode=="cancelled-before-start")cancellation.Cancel();
        using var result=await new NativeSecretLookup(configuration,factory).ReadCoreAsync(request,cancellation.Token);
        if(mode=="success"){Assert.NotNull(result.Bytes);Assert.True(result.Quiescent);Assert.Equal(0,process.Kills);}
        else{Assert.Null(result.Bytes);Assert.Equal(mode is not ("unreaped" or "launch-hang"),result.Quiescent);}
        if(mode is "hang" or "unreaped" or "stderr")Assert.Equal(1,process.Kills);
        if(mode=="cancelled-before-start")Assert.Equal(0,factory.Starts);
        else Assert.Equal(1,factory.Starts);
        if(mode=="launch-hang"){factory.AllowLaunch.Set();await process.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(2));Assert.Equal(1,process.Kills);}
        else if(mode=="unreaped"){process.Release();await process.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(2));}
        else if(factory.Starts>0)Assert.True(process.Disposed.Task.IsCompleted);
    }
    private sealed class Factory(FakeProcess process):IKeychainProcessFactory
    {
        public int Starts; public readonly ManualResetEventSlim AllowLaunch=new(false);
        public IKeychainProcess Start(ProcessStartInfo start)
        {
            Interlocked.Increment(ref Starts); if(process.Mode=="launch-hang")AllowLaunch.Wait();Assert.False(start.UseShellExecute);Assert.True(start.RedirectStandardInput&&start.RedirectStandardOutput&&start.RedirectStandardError);
            Assert.Equal(new[]{"--internal-keychain-read"},start.ArgumentList);Assert.DoesNotContain("PASSWORD",string.Join(",",start.Environment.Keys));
            Assert.Equal("0",start.Environment["DOTNET_EnableDiagnostics"]);return process;
        }
    }
    private sealed class FakeProcess:IKeychainProcess
    {
        private readonly TaskCompletionSource _exit=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Disposed=new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly string _mode;public string Mode=>_mode;public int Kills;
        public Stream Input{get;}=new MemoryStream();public Stream Output{get;}public Stream Errors{get;}
        public FakeProcess(byte[] frame,string mode){_mode=mode;Output=new MemoryStream(frame);Errors=new MemoryStream(mode=="stderr"?new byte[1025]:[]);if(mode is "success" or "nonzero")_exit.TrySetResult();}
        public bool HasExited=>_exit.Task.IsCompleted;public int ExitCode=>_mode=="nonzero"?2:0;
        public void Kill(){Kills++;if(_mode!="unreaped")_exit.TrySetResult();}
        public Task WaitForExitAsync(CancellationToken ct=default)=>_exit.Task.WaitAsync(ct);
        public void Release()=>_exit.TrySetResult();
        public void Dispose(){Input.Dispose();Output.Dispose();Errors.Dispose();Disposed.TrySetResult();}
    }
}
