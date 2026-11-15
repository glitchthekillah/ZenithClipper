using System.Diagnostics;
using System.Runtime.InteropServices;
using ZenithClipper.Native.Audio;

string output=Path.Combine(AppContext.BaseDirectory,"synthetic.aac");
using FileStream file=File.Create(output);
long packets=0,next=0;long first=-1;
using(var encoder=new AacEncoder((packet,index,duration)=>{
    if(index!=next||duration!=1024||packet.Length==0)throw new InvalidDataException("Bad packet timeline.");
    if(first<0)first=index;next+=duration;packets++;
    int length=packet.Length+7;if(length>8191)throw new InvalidDataException("ADTS frame overflow.");
    Span<byte> header=stackalloc byte[7];header[0]=0xFF;header[1]=0xF1;header[2]=0x4C;
    header[3]=(byte)(0x80|(length>>11));header[4]=(byte)(length>>3);header[5]=(byte)(((length&7)<<5)|0x1F);header[6]=0xFC;
    file.Write(header);file.Write(packet);
})){
    byte[] bytes=new byte[480*4];Span<short> pcm=MemoryMarshal.Cast<byte,short>(bytes.AsSpan());
    for(int offset=0;offset<96000;offset+=480){
        for(int i=0;i<480;i++){short value=(short)(1200*Math.Sin(2*Math.PI*440*(offset+i)/48000));pcm[i*2]=value;pcm[i*2+1]=value;}
        encoder.Encode(bytes,offset);
    }
    encoder.Complete();encoder.Complete();
}
file.Flush();
if(packets<93||packets>97)throw new InvalidDataException($"Unexpected sample coverage {packets*1024}.");
using var process=Process.GetCurrentProcess();process.Refresh();
Console.WriteLine($"packets={packets} firstSample={first} codedSamples={next} inputSamples=96000 asc={Convert.ToHexString(AacEncoder.AudioSpecificConfig)} workingMiB={process.WorkingSet64/1048576.0:F2} privateMiB={process.PrivateMemorySize64/1048576.0:F2} peakWorkingMiB={process.PeakWorkingSet64/1048576.0:F2}");
Console.WriteLine(output);


// Callback exceptions return through HRESULT and are rethrown in managed code; Dispose must still release native state.
using(var faulty=new AacEncoder((_,_,_)=>throw new IOException("intentional callback failure"))){
    bool observed=false;
    try { for(int i=0;i<30;i++)faulty.Encode(new byte[1920],i*480);faulty.Complete(); }
    catch(IOException ex) when(ex.Message=="intentional callback failure"){observed=true;}
    if(!observed)throw new InvalidOperationException("Callback failure was lost.");
}
using(var affinity=new AacEncoder((_,_,_)=>{})){
    Exception? error=null;var thread=new Thread(()=>{try{affinity.Encode(new byte[4],0);}catch(Exception ex){error=ex;}});
    thread.Start();thread.Join();if(error is not InvalidOperationException)throw new InvalidOperationException("Thread affinity not enforced.");
    try{affinity.Encode(new byte[3],0);throw new InvalidOperationException("Alignment not enforced.");}catch(ArgumentException){}
}
Console.WriteLine("callbackFailure=passed disposalAfterFailure=passed threadAffinity=passed frameAlignment=passed");

long offsetNext=48000;int offsetPackets=0;
using(var offsetEncoder=new AacEncoder((_,index,duration)=>{
    if(index!=offsetNext||duration!=1024)throw new InvalidDataException("MFT sample origin lost.");
    offsetNext+=duration;offsetPackets++;
})){
    for(int i=0;i<10;i++)offsetEncoder.Encode(new byte[1920],48000+i*480);
    offsetEncoder.Complete();
}
if(offsetPackets!=5)throw new InvalidDataException("Offset duration incorrect.");
Console.WriteLine("nonzeroMftTimestampOrigin=passed");
