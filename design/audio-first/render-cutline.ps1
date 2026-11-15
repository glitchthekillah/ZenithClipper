Add-Type -AssemblyName System.Drawing
$out = Join-Path $PSScriptRoot 'zenithclipper-cutline.png'
$bmp = [System.Drawing.Bitmap]::new(1680,1120)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'
$g.TextRenderingHint = 'AntiAliasGridFit'
function C($hex) { [System.Drawing.ColorTranslator]::FromHtml($hex) }
function Rect($x,$y,$w,$h,$color) { $b=[System.Drawing.SolidBrush]::new((C $color)); $g.FillRectangle($b,[single]$x,[single]$y,[single]$w,[single]$h); $b.Dispose() }
function Line($x,$y,$x2,$y2,$color,$width=1) { $p=[System.Drawing.Pen]::new((C $color),$width); $g.DrawLine($p,[single]$x,[single]$y,[single]$x2,[single]$y2); $p.Dispose() }
function Text($s,$x,$y,$size=12,$color='#ECF0F1',$bold=$false,$family='Segoe UI') { $style=[System.Drawing.FontStyle]::Regular; if($bold){$style=[System.Drawing.FontStyle]::Bold}; $f=[System.Drawing.Font]::new($family,[single]$size,$style,[System.Drawing.GraphicsUnit]::Pixel); $b=[System.Drawing.SolidBrush]::new((C $color)); $g.DrawString($s,$f,$b,[single]$x,[single]$y); $f.Dispose();$b.Dispose() }
function Round($x,$y,$w,$h,$r,$color) { $p=[System.Drawing.Drawing2D.GraphicsPath]::new();$d=$r*2;$p.AddArc($x,$y,$d,$d,180,90);$p.AddArc(($x+$w-$d),$y,$d,$d,270,90);$p.AddArc(($x+$w-$d),($y+$h-$d),$d,$d,0,90);$p.AddArc($x,($y+$h-$d),$d,$d,90,90);$p.CloseFigure();$b=[System.Drawing.SolidBrush]::new((C $color));$g.FillPath($b,$p);$p.Dispose();$b.Dispose() }
function Pill($s,$x,$y,$w,$fill='#252C31',$ink='#C5CED4') { Round $x $y $w 28 5 $fill; Text $s ($x+10) ($y+6) 11 $ink }
function Mark($x,$y,$size=24) { Line $x $y ($x+$size) $y '#B6E3F2' 3; Line ($x+$size) $y $x ($y+$size) '#B6E3F2' 3; Line $x ($y+$size) ($x+$size) ($y+$size) '#B6E3F2' 3;Line ($x+$size+6) ($y-3) ($x+$size-2) ($y+$size+3) '#B6E3F2' 2 }
function Wave($x,$y,$w,$h,$seed,$color) { $rng=[Random]::new($seed); for($i=0;$i -lt $w;$i+=3){$v=3+$rng.NextDouble()*($h/2-3);$env=.25+.75*[Math]::Abs([Math]::Sin($i*.019));Line ($x+$i) ($y+$h/2-$v*$env) ($x+$i) ($y+$h/2+$v*$env) $color 1} }
function Thumb($x,$y,$w,$h,$seed) { Rect $x $y $w $h '#202F3A';$rng=[Random]::new($seed);$colors=@('#314958','#3B5260','#4C5D65','#6F797B','#9BA4A2');for($i=0;$i -lt 13;$i++){ $bx=$x+$rng.Next(0,[int]$w);$bw=$rng.Next(8,30);$bh=$rng.Next(8,[int]$h);Rect $bx ($y+$h-$bh) ([Math]::Min($bw,$x+$w-$bx)) $bh $colors[$rng.Next(0,5)] };Rect $x ($y+$h-6) $w 6 '#1B252D' }
$g.Clear((C '#0B0F13'))
Text 'ZENITHCLIPPER  /  CUTLINE' 56 35 13 '#A6B7C3' $true
Text 'Keep the moment. Make it yours.' 54 64 37 '#EEF2F5' $true
Text 'A precise editing desk + a clipping panel that stays out of your way.' 57 120 15 '#91A0AC'
Pill 'CODE-DRAWN CONCEPT' 1426 51 198 '#17212A' '#9EB8C9'

# Editor window
Round 52 176 1174 840 10 '#171C22'
Rect 52 222 1174 1 '#323B45'
Mark 70 191 14
Text 'zenithclipper' 103 188 15 '#E7EEF3' $true
Text 'Friday night / highlights' 399 191 12 '#A6B2BE'
Text 'Local project' 560 191 11 '#6D8390'
Pill 'Export video  ↗' 1075 185 130 '#B6E3F2' '#15222D'
Text '−    □    ×' 1138 229 13 '#8E9AA5'

# Navigation rail
Rect 52 223 58 745 '#12171D'
Round 62 253 38 43 7 '#2B3A47'
Text '▦' 73 263 21 '#B6E3F2'
Text 'Media' 67 301 9 '#BAC9D3'
Text 'T' 75 351 21 '#9AA9B6' $true
Text 'Titles' 70 385 9 '#9AA9B6'
Text '◇' 73 435 25 '#9AA9B6'
Text 'Effects' 67 470 9 '#9AA9B6'
Text '♪' 76 521 22 '#9AA9B6'
Text 'Audio' 68 556 9 '#9AA9B6'
Text '?' 77 916 18 '#6F8191'

# Media bin
Rect 110 223 216 442 '#1B2129'
Text 'MEDIA' 130 244 10 '#8394A3' $true
Pill '+ Import' 239 237 69 '#2C3742' '#E8EDF1'
Round 128 284 180 30 4 '#141B22'; Text '⌕   Search your clips' 138 291 11 '#8295A6'
Round 123 328 191 142 5 '#2A3A49'
Thumb 131 336 175 87 41
Rect 260 401 41 16 '#131A21';Text '00:30' 264 401 10 '#EDF2F5'
Text 'The final push' 133 433 12 '#F0F3F5' $true
Text 'Video + audio · 165 fps' 133 451 10 '#9FB1C1'
Thumb 131 487 175 87 72
Rect 260 552 41 16 '#131A21';Text '00:18' 264 552 10 '#EDF2F5'
Text 'One more round' 133 584 12 '#D3DDE5' $true
Text 'Video + audio · 165 fps' 133 602 10 '#8496A7'
Text '2 clips' 131 642 10 '#8394A3'
Text 'All media stays on your device' 131 657 9 '#667D90'

# Viewer
Text 'PROGRAM' 345 242 10 '#8394A3' $true
Text 'Fit  ▾' 853 242 10 '#A3B1BE'
Text '½ preview' 905 242 10 '#8394A3'
Rect 343 278 622 337 '#0D141B'
$clip=$g.Save();$g.SetClip([System.Drawing.Rectangle]::new(343,278,622,337))
$brush=[System.Drawing.Drawing2D.LinearGradientBrush]::new([System.Drawing.Rectangle]::new(343,278,622,337),(C '#344B5A'),(C '#111D29'),30)
$g.FillRectangle($brush,343,278,622,337);$brush.Dispose()
# Architectural sample scene uses only solid vector geometry.
Rect 343 498 622 117 '#1B2D3B'
Rect 782 278 183 267 '#283C4B'
Rect 844 278 27 267 '#3C5260'
Rect 893 278 8 267 '#182B39'
$pts=[System.Drawing.Point[]]@([System.Drawing.Point]::new(343,615),[System.Drawing.Point]::new(657,461),[System.Drawing.Point]::new(965,615));$b=[System.Drawing.SolidBrush]::new((C '#314754'));$g.FillPolygon($b,$pts);$b.Dispose()
for($i=0;$i -lt 5;$i++){Line 657 461 (343+$i*156) 615 '#435963' 1}
Rect 366 296 132 21 '#17232E';Text 'SAMPLE TITLE COMPOSITION' 373 301 8 '#B7C6D0' $true
Text 'ONE MORE' 386 346 46 '#F2F6F8' $true
Text 'ROUND.' 386 394 64 '#B6E3F2' $true
Rect 390 484 41 3 '#B6E3F2'
Text 'FRIDAY NIGHT  /  HIGHLIGHTS' 390 506 10 '#B8C6CE' $true
Text 'ZENITHCLIPPER' 851 590 8 '#A8BAC7'
$g.Restore($clip)
Pill '▶' 610 628 36 '#2F414F' '#C1E9F5'
Text '00:12.08' 346 635 12 '#D4E1EA' $false 'Consolas'
Text '/ 00:48.00' 419 635 11 '#7A91A3' $false 'Consolas'
Text 'Set start  I     Set end  O' 791 635 10 '#ABBBC7'

# Inspector
Rect 984 223 242 442 '#1B2129'
Text 'CLIP' 1003 244 10 '#8394A3' $true
Text 'The final push' 1003 273 17 '#E6EDF2' $true
Text 'Video' 1004 312 12 '#B6E3F2' $true;Text 'Sound' 1064 312 12 '#8FA2B3';Text 'Speed' 1131 312 12 '#8FA2B3'
Line 1004 339 1205 339 '#35414D';Rect 1004 337 39 2 '#B6E3F2'
Text 'Frame' 1003 359 12 '#C9D6E0' $true
Text 'Aspect ratio' 1003 390 11 '#8FA2B3';Pill 'Original  ▾' 1108 382 97
Text 'Scale' 1003 431 11 '#8FA2B3';Line 1072 438 1166 438 '#536877' 2;Line 1072 438 1131 438 '#B6E3F2' 2;Round 1126 433 10 10 5 '#B6E3F2';Text '100%' 1176 432 10 '#C1CFDA'
Text 'Position' 1003 472 11 '#8FA2B3';Pill 'X   0' 1072 464 59;Pill 'Y   0' 1140 464 64
Line 1004 511 1205 511 '#303C48'
Text 'Adjustments' 1003 532 12 '#C9D6E0' $true
Text 'Brightness' 1003 570 11 '#8FA2B3';Line 1090 579 1187 579 '#536877' 2;Round 1134 574 10 10 5 '#A0B6C7'
Text 'Contrast' 1003 606 11 '#8FA2B3';Line 1090 614 1187 614 '#536877' 2;Round 1134 609 10 10 5 '#A0B6C7'

# Timeline
Rect 110 680 1116 288 '#151D25'
Line 110 680 1226 680 '#3A4855'
Text 'TIMELINE' 130 699 10 '#A8B7C4' $true
Text '↶    ↷' 233 694 18 '#9BAFBF'
Pill 'Split' 309 690 48 '#27343F'
Pill 'Unlink audio' 366 690 94 '#27343F'
Text 'Snap on' 1021 699 10 '#A1B3C0';Text '−   ━━●━━   +' 1100 697 11 '#A1B3C0'
for($i=0;$i -lt 6;$i++){ $tx=226+$i*183;Text ('00:' + ($i*10).ToString('00')) $tx 742 10 '#7F94A5' $false 'Consolas';Line $tx 762 $tx 930 '#253440' }
Text 'V1' 130 799 12 '#B6E3F2' $true 'Consolas';Text 'Video' 158 800 10 '#8CA4B5'
Text 'A1' 130 864 12 '#8DB5C7' $true 'Consolas';Text 'Sound' 158 866 10 '#8CA4B5'
Text 'T1' 130 918 12 '#BAAFA0' $true 'Consolas';Text 'Title' 158 920 10 '#8CA4B5'
Round 226 780 568 60 4 '#345467';Round 798 780 356 60 4 '#2B3D4D'
for($i=0;$i -lt 9;$i++){Thumb (230+$i*62) 802 58 34 (41+$i)}
for($i=0;$i -lt 5;$i++){Thumb (802+$i*69) 802 64 34 (72+$i)}
Text 'The final push' 239 784 10 '#E1EEF5' $true;Text 'One more round' 811 784 10 '#D6E3EB'
Rect 226 780 568 2 '#B6E3F2';Rect 226 780 3 60 '#B6E3F2';Rect 791 780 3 60 '#B6E3F2'
Round 226 850 568 46 3 '#223B49';Round 798 850 356 46 3 '#23343F';Wave 231 854 557 38 33 '#739CAF';Wave 803 854 346 38 22 '#65889D'
Round 334 908 288 27 3 '#3D3D3D';Rect 334 908 2 27 '#BDB0A0';Text 'T   One more round.' 347 914 10 '#E1D5C5'
Line 452 767 452 949 '#D7F4FF' 1
$pts=[System.Drawing.Point[]]@([System.Drawing.Point]::new(447,764),[System.Drawing.Point]::new(457,764),[System.Drawing.Point]::new(452,772));$b=[System.Drawing.SolidBrush]::new((C '#D7F4FF'));$g.FillPolygon($b,$pts);$b.Dispose()
Text 'Drag to arrange. Pull an edge to trim.' 130 950 10 '#6F889C'
Line 52 975 1226 975 '#303C48';Text '✓  Saved locally' 73 989 10 '#A1B5C4';Text '2560 × 1600  /  165 fps' 1011 989 10 '#8AA0B0'

# Compact capture companion
Text 'CAPTURE / QUICK PANEL' 1270 190 11 '#93A7B7' $true
Round 1264 222 364 551 10 '#1B232D'
Rect 1264 242 3 40 '#B6E3F2';Mark 1286 245 17;Text 'zenithclipper' 1320 243 17 '#E9EFF3' $true;Text '×' 1591 241 22 '#8EA3B4'
Round 1288 304 6 6 3 '#B6E3F2';Text 'READY TO CLIP' 1304 299 10 '#B9D4E3' $true
Text 'Pause' 1562 299 11 '#A0B2C0'
Text 'Keep the last' 1285 347 18 '#A7B9C7'
Text '30 seconds' 1281 368 43 '#EEF5F8' $true
Text 'Choose how far back to save.' 1286 428 12 '#8FA5B7'
Pill '15s' 1286 462 51 '#263441';Pill '30s' 1344 462 51 '#B6E3F2' '#172531';Pill '1m' 1402 462 51 '#263441';Pill '2m' 1460 462 51 '#263441';Pill 'Custom' 1518 462 85 '#263441'
Round 1286 511 317 54 6 '#B6E3F2';Text 'Save clip' 1305 526 17 '#172531' $true;Pill 'Alt + C' 1514 524 72 '#92B9CC' '#172531'
Line 1286 591 1603 591 '#354555'
Text 'SOUND IN CLIPS' 1286 611 10 '#93AABB' $true;Text 'Manage  ↗' 1538 609 11 '#BCDDEB'
Text 'Game + Discord' 1286 640 13 '#D5E2EB';Pill 'Included' 1530 634 73 '#2C424E' '#BDDDE9'
Text 'Spotify' 1286 677 13 '#91A7B8';Text 'Excluded' 1540 677 11 '#91A7B8'
Line 1286 713 1603 713 '#354555'
Text 'Recent clips  ↗' 1286 735 12 '#D0DEE8';Text 'Editor  ↗' 1431 735 12 '#D0DEE8';Text 'Settings' 1547 735 12 '#96AEBF'
Text 'Shift + ` to open or dismiss' 1285 791 12 '#7C94A9'
Text 'ONE VISUAL LANGUAGE.' 1270 861 11 '#9CB2C4' $true
Text 'Quiet surfaces. Sharp hierarchy.' 1270 891 15 '#CFDCE5'
Text 'The timeline is the focal point.' 1270 918 15 '#CFDCE5'
Text 'Capture stays a single action.' 1270 945 15 '#CFDCE5'

Text 'CUTLINE / CONCEPT STUDY' 56 1052 10 '#637F94' $true
Text 'Proposed interface · illustrative media · no measured performance claims' 56 1072 11 '#637F94'
Text 'Charcoal + glacier blue' 1460 1065 12 '#96ADBF'
$bmp.Save($out,[System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose();$bmp.Dispose()
Write-Output $out
