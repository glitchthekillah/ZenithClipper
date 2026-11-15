import json,struct,math
from pathlib import Path
root=Path('tests/NativeAac.Tests');j=json.loads((root/'probe.json').read_text());s=j['streams'][0]
assert s['codec_name']=='aac' and s['profile']=='LC' and int(s['sample_rate'])==48000 and int(s['channels'])==2,s
packets=j['packets'];assert len(packets)==94,len(packets)
raw=(root/'publish/decoded.pcm').read_bytes();assert len(raw)==94*1024*4,len(raw)
y=struct.unpack('<'+'h'*(len(raw)//2),raw)[::2]
assert max(abs(v) for v in y)>500
# Frequency from positive crossings after startup; no subjective playback.
cross=[i for i in range(4097,len(y)-2048) if y[i-1]<=0<y[i]]
freq=48000*(len(cross)-1)/(cross[-1]-cross[0]);assert abs(freq-440)<1,freq
rms=math.sqrt(sum(v*v for v in y[4096:-2048])/len(y[4096:-2048]));assert 600<rms<1100,rms
for a,b in zip(packets,packets[1:]):assert int(b['pts'])>int(a['pts'])
print(f'codec=AAC-LC sampleRate=48000 channels=2 packets={len(packets)} decodedSamples={len(y)} inputSamples=96000 paddingOrPrimingDifference={len(y)-96000} toneHz={freq:.3f} rms={rms:.2f}')
print('Nominal packet timeline increments1024; ADTS does not represent AAC priming/edit-list metadata. Exact audible alignment is not established by tone decode.')
