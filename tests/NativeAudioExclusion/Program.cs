using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using ZenithClipper;
using ZenithClipper.Native.Audio;

if(args.Length!=2) throw new ArgumentException("Require owned included and excluded tone PIDs.");
int includedPid=int.Parse(args[0]), excludedPid=int.Parse(args[1]);
using CancellationTokenSource timeout=new(TimeSpan.FromSeconds(15));
string outputFolder=AppContext.BaseDirectory;
string[] baselineRules=["Spotify"];
string[] exclusionRules=["Spotify","ZenithToneExclude"];
AudioProcessView? baselineView=null;
for(int attempt=0;attempt<30;attempt++)
{
    baselineView=NativeAudioSessions.Read(baselineRules);
    if(baselineView.Included.Contains(includedPid)&&baselineView.Included.Contains(excludedPid))break;
    await Task.Delay(100,timeout.Token);
}
var snapshot=NativeAudioSessions.Processes();
string includedName=snapshot.Names.GetValueOrDefault(includedPid)??"missing";
string excludedName=snapshot.Names.GetValueOrDefault(excludedPid)??"missing";
Console.WriteLine($"Owner metadata include={includedPid}:{includedName} exclude={excludedPid}:{excludedName}");
if(!AudioPolicy.Excluded(includedName,["ZenithToneInclude"])||!AudioPolicy.Excluded(excludedName,["ZenithToneExclude"]))
    throw new InvalidOperationException("Owned playback process names do not match the test policy.");
if(baselineView is null||!baselineView.Included.Contains(includedPid)||!baselineView.Included.Contains(excludedPid))
    throw new InvalidOperationException("Both owned tone playback sessions must exist before baseline capture.");
AudioProcessView excludedView=NativeAudioSessions.Read(exclusionRules);
if(!excludedView.Included.Contains(includedPid)||excludedView.Included.Contains(excludedPid)||!excludedView.Excluded.Contains(excludedPid)||
    AudioPolicy.RootsContainExcluded(excludedView.Included,excludedView.Parents,[excludedPid]))
    throw new InvalidOperationException("Before-capture process-tree exclusion policy did not isolate the owners.");
Console.WriteLine($"Policy verified before capture: baselineRoots={string.Join(',',baselineView.Included)} excludedRoots={string.Join(',',excludedView.Included)} blockedPid={excludedPid}");
byte[] baseline=await Capture(baselineRules,"baseline.pcm");
byte[] filtered=await Capture(exclusionRules,"excluded.pcm");
var first=Spectrum(baseline);var second=Spectrum(filtered);
double suppression=Db(second.Hz880/first.Hz880), preserved=Db(second.Hz440/first.Hz440), residual=Db(second.Hz880/second.Hz440);
Console.WriteLine(FormattableString.Invariant($"Baseline:440={Db(first.Hz440):F3}dBFS 880={Db(first.Hz880):F3}dBFS"));
Console.WriteLine(FormattableString.Invariant($"Excluded:440={Db(second.Hz440):F3}dBFS 880={Db(second.Hz880):F3}dBFS"));
Console.WriteLine(FormattableString.Invariant($"880 attenuation={suppression:F3}dB;440 preservation={preserved:F3}dB;880/440 residual={residual:F3}dB"));
bool pass=first.Hz440>1e-9&&first.Hz880>1e-9&&Math.Abs(preserved)<6&&suppression< -40&&residual< -40;
string json=FormattableString.Invariant($"{{\"includedPid\":{includedPid},\"excludedPid\":{excludedPid},\"baseline440Power\":{first.Hz440:R},\"baseline880Power\":{first.Hz880:R},\"filtered440Power\":{second.Hz440:R},\"filtered880Power\":{second.Hz880:R},\"attenuation880Db\":{suppression:R},\"preservation440Db\":{preserved:R},\"residual880to440Db\":{residual:R},\"pass\":{pass.ToString().ToLowerInvariant()}}}");
File.WriteAllText(Path.Combine(outputFolder,"result.json"),json);
Console.WriteLine(pass?"PASS before-capture owner exclusion and PCM FFT":"FAIL spectral acceptance");
return pass?0:2;

async Task<byte[]> Capture(string[] rules,string name)
{
    using NativeAudioMixer mixer=new(rules);
    await mixer.StartAsync(timeout.Token);
    await Task.Delay(200,timeout.Token);
    mixer.BeginEpoch();
    byte[] captured=new byte[150*3840], packet=new byte[3840];
    using AudioPacer pacer=new();Stopwatch clock=Stopwatch.StartNew();
    for(int index=0;index<150;index++)
    {
        pacer.WaitUntil(clock,index*0.02,timeout.Token);
        if(mixer.Faulted)throw new IOException("Native audio capture faulted.");
        mixer.Read20Milliseconds(packet);packet.CopyTo(captured,index*3840);
    }
    File.WriteAllBytes(Path.Combine(outputFolder,name),captured);
    Console.WriteLine($"Captured {name} bytes={captured.Length} stereoPCM16/48000 rules={string.Join(',',rules)}");
    return captured;
}
static double Db(double power)=>10*Math.Log10(Math.Max(power,1e-30));
static (double Hz440,double Hz880) Spectrum(byte[] pcm)
{
    const int n=65536,rate=48000;
    int frameCount=pcm.Length/4,offset=(frameCount-n)/2;
    if(offset<0)throw new ArgumentException("Insufficient PCM frames for FFT.");
    Complex[] bins=new Complex[n];double windowEnergy=0;
    for(int index=0;index<n;index++)
    {
        int position=(offset+index)*4;
        short left=(short)(pcm[position]|pcm[position+1]<<8),right=(short)(pcm[position+2]|pcm[position+3]<<8);
        double mono=(left+(double)right)/65536.0;
        double window=0.5*(1-Math.Cos(2*Math.PI*index/(n-1)));
        windowEnergy+=window*window;bins[index]=new Complex(mono*window,0);
    }
    for(int i=1,j=0;i<n;i++)
    {
        int bit=n>>1;for(; (j&bit)!=0;bit>>=1)j^=bit;j^=bit;
        if(i<j)(bins[i],bins[j])=(bins[j],bins[i]);
    }
    for(int length=2;length<=n;length<<=1)
    {
        Complex step=Complex.FromPolarCoordinates(1,-2*Math.PI/length);
        for(int start=0;start<n;start+=length)
        {
            Complex factor=Complex.One;
            for(int k=0;k<length/2;k++)
            {
                Complex even=bins[start+k],odd=bins[start+k+length/2]*factor;
                bins[start+k]=even+odd;bins[start+k+length/2]=even-odd;factor*=step;
            }
        }
    }
    double Band(int hz)
    {
        double energy=0;
        for(int bin=(int)Math.Ceiling((hz-5.0)*n/rate);bin<=(int)Math.Floor((hz+5.0)*n/rate);bin++)
            energy+=bins[bin].Real*bins[bin].Real+bins[bin].Imaginary*bins[bin].Imaginary;
        return energy*2/(n*windowEnergy);
    }
    return(Band(440),Band(880));
}

sealed class AudioPacer:IDisposable
{
    readonly nint timer=CreateWaitableTimerExW(0,0,2,0x1F0003);
    public AudioPacer(){if(timer==0)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());}
    public void WaitUntil(Stopwatch clock,double due,CancellationToken token)
    {
        double left;
        while((left=due-clock.Elapsed.TotalSeconds)>0)
        {
            token.ThrowIfCancellationRequested();
            if(left>0.0003){long ticks=-(long)((left-0.00015)*10000000);if(!SetWaitableTimer(timer,ref ticks,0,0,0,false)||WaitForSingleObject(timer,1000)!=0)throw new IOException("Audio test pacer failed.");}
            else Thread.SpinWait(100);
        }
    }
    public void Dispose()=>CloseHandle(timer);
    [DllImport("kernel32.dll",SetLastError=true)]static extern nint CreateWaitableTimerExW(nint attributes,nint name,uint flags,uint access);
    [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]static extern bool SetWaitableTimer(nint timer,ref long due,int period,nint callback,nint argument,[MarshalAs(UnmanagedType.Bool)]bool resume);
    [DllImport("kernel32.dll")]static extern uint WaitForSingleObject(nint handle,uint ms);
    [DllImport("kernel32.dll")][return:MarshalAs(UnmanagedType.Bool)]static extern bool CloseHandle(nint handle);
}
