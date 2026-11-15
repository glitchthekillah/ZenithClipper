namespace ZenithClipper;

public static class TsSnapshot
{
    /// <summary>Return a prefix ending before the newest video PES. That frame may still be being written.</summary>
    public static long SafeLength(Stream input,long available)
    {
        if(!input.CanSeek)throw new ArgumentException("TS snapshot needs a seekable stream.");
        input.Position=0;long limit=available/188*188,lastVideo=0;Span<byte> packet=stackalloc byte[188];
        for(long position=0;position<limit;position+=188)
        {
            input.ReadExactly(packet);
            if(packet[0]!=0x47)break;
            if((packet[1]&0x40)==0)continue; // payload-unit start
            int control=(packet[3]>>4)&3;
            if(control is not (1 or 3))continue;
            int payload=control==3?5+packet[4]:4;
            if(payload+6<=188 && packet[payload]==0 && packet[payload+1]==0 && packet[payload+2]==1 && packet[payload+3] is >=0xe0 and <=0xef)lastVideo=position;
        }
        input.Position=0;return lastVideo;
    }
}
