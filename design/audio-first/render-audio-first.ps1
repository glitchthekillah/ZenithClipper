$helpers = Get-Content (Join-Path $PSScriptRoot 'render-cutline.ps1') -Raw
Invoke-Expression (($helpers.Substring(0,$helpers.IndexOf('$g.Clear('))) -replace '(?m)^\$out = .*\r?\n','')
function Orb($x,$y,$d) {
 $r=[System.Drawing.RectangleF]::new($x,$y,$d,$d)
 $glow=[System.Drawing.RectangleF]::Inflate($r,$d*.28,$d*.28)
 $gp=[System.Drawing.Drawing2D.GraphicsPath]::new();$gp.AddEllipse($glow)
 $gb=[System.Drawing.Drawing2D.PathGradientBrush]::new($gp);$gb.CenterColor=[System.Drawing.Color]::FromArgb(110,148,218,255);$gb.SurroundColors=@([System.Drawing.Color]::FromArgb(0,148,218,255));$g.FillEllipse($gb,$glow);$gb.Dispose();$gp.Dispose()
 $gp=[System.Drawing.Drawing2D.GraphicsPath]::new();$gp.AddEllipse($r);$b=[System.Drawing.Drawing2D.PathGradientBrush]::new($gp);$b.CenterPoint=[System.Drawing.PointF]::new(($x+$d*.34),($y+$d*.28));$cb=[System.Drawing.Drawing2D.ColorBlend]::new();$cb.Colors=@((C '#132B3B'),(C '#236388'),(C '#4FA6D3'),(C '#94DAFF'),(C '#E7F8FF'));$cb.Positions=[single[]]@(0,.22,.48,.78,1);$b.InterpolationColors=$cb;$g.FillEllipse($b,$r);$b.Dispose();$gp.Dispose()
}
function Toggle($x,$y,$on) { if($on){Round $x $y 42 23 11 '#94DAFF';Round ($x+23) ($y+4) 15 15 7 '#132C3D'}else{Round $x $y 42 23 11 '#35404B';Round ($x+4) ($y+4) 15 15 7 '#99A5B2'} }
function Slider($x,$y,$w,$v,$enabled=$true) { Line $x $y ($x+$w) $y '#303D4A' 4;if($enabled){Line $x $y ($x+$w*$v) $y '#94DAFF' 4;Round ($x+$w*$v-5) ($y-7) 10 14 3 '#D8F0FF'}else{Round ($x+$w*$v-5) ($y-7) 10 14 3 '#617080'} }
function Meter($x,$y,$w,$seed,$enabled=$true) { $rng=[Random]::new($seed);for($i=0;$i -lt $w;$i+=5){$level=4+$rng.Next(0,21);if($enabled){$color='#7DBBDF'}else{$color='#475563'};Rect ($x+$i) ($y+26-$level) 2 $level $color} }
function AppBadge($x,$y,$letters,$bg,$ink) { Round $x $y 40 40 10 $bg;Text $letters ($x+8) ($y+10) 15 $ink $true }
function Tag($s,$x,$y,$w,$on) { if($on){Pill $s $x $y $w '#20394B' '#A9DAF7'}else{Pill $s $x $y $w '#302D31' '#D9B8AC'} }
$g.Clear((C '#0D1116'))
Text 'ZENITHCLIPPER / AUDIO-FIRST CONCEPT' 62 35 12 '#89A0B5' $true
Text 'Your sound. Your rules.' 58 63 43 '#ECF4FC' $true
Text 'Choose what reaches your clips. Keep listening exactly as you are.' 62 124 17 '#9BADBF'
Pill 'DESIGN PREVIEW · SAMPLE LEVELS' 1316 55 302 '#172533' '#8FB0CA'

# A single audio routing workspace; app-wide clipping controls stay in its header.
Round 58 184 1564 837 14 '#171B21'
Orb 83 211 36
Text 'ZenithClipper' 135 210 22 '#EDF4FC' $true
Text 'AUDIO DESK' 136 242 10 '#8EA9BF' $true
Pill '●  Replay ready' 361 215 137 '#1B3040' '#9DD5F5'
Text 'Profile' 880 228 12 '#8CA5BB'
Pill 'Gaming + friends   ▾' 935 215 183 '#253749' '#D9E9F5'
Pill 'Save last 30s   Alt + C' 1194 211 208 '#94DAFF' '#0E2639'
Text 'Clips  ↗' 1441 224 13 '#BDCFDF'
Text '×' 1571 212 27 '#8DA6BB'
Line 83 277 1596 277 '#2A3A4B'

# Included and excluded paths are explained in the actual header rather than footnotes.
Text 'Build your clip mix' 88 302 29 '#E4EFF9' $true
Text 'Every app below still plays through your headphones.' 90 347 14 '#91A7BC'
Pill '+ Add app' 1100 310 98 '#253749' '#D7E8F6'
Round 1222 300 374 615 10 '#222A33'
Text 'SELECTED APP' 1246 321 10 '#819EB6' $true
AppBadge 1247 350 'D' '#354479' '#D5DDFF'
Text 'Discord' 1299 349 23 '#ECF3FB' $true
Text 'Voice chat · currently playing' 1299 379 12 '#8FA8C0'
Line 1247 416 1572 416 '#34485D'
Text 'Record this app' 1248 434 14 '#D8E9F7' $true;Toggle 1527 432 $true
Text 'Clip volume' 1248 488 13 '#A0B8CE';Text '85%' 1529 486 17 '#C4E7FF' $true
Slider 1250 534 318 .71
Text 'Quieter' 1248 552 10 '#829EB7';Text 'Original' 1518 552 10 '#829EB7'
Text 'CHANNEL TUNING' 1248 600 10 '#829EB7' $true
Text 'Balance' 1248 638 13 '#B8CCDF';Text 'Center' 1518 639 12 '#8FAAC1';Slider 1250 676 318 .5
Text 'Capture rule' 1248 720 13 '#B8CCDF';Pill 'Remember for this app   ▾' 1247 749 326 '#243A4E' '#BCD3E7'
Text 'Your listening volume stays unchanged.' 1248 808 12 '#8FAAC1'
Text 'Exclusions apply before capture.' 1248 833 12 '#8FAAC1'
Pill 'Reset app settings' 1248 873 156 '#22394D' '#9FBBD1'

Text 'APPLICATION' 92 399 10 '#7591AA' $true
Text 'YOU HEAR' 457 399 10 '#7591AA' $true
Text 'IN YOUR CLIP' 629 399 10 '#7591AA' $true
Text 'CLIP VOLUME' 866 399 10 '#7591AA' $true
Text 'TUNE' 1136 399 10 '#7591AA' $true
$apps=@(
 @('OW','Overwatch','Game audio','#4D3E2D','#E3CFB2',100,$true),
 @('D','Discord','Voice chat','#354479','#D5DDFF',85,$true),
 @('S','Spotify','Music','#203C35','#A6D8BC',100,$false),
 @('C','Chrome','Browser audio','#3B3541','#D1BEDB',100,$false)
)
for($idx=0;$idx -lt 4;$idx++){
 $a=$apps[$idx];$y=428+$idx*105;$on=[bool]$a[6]
 if($idx -eq 1){Round 81 $y 1117 97 7 '#293B49';Rect 81 ($y+14) 3 69 '#94DAFF'}
 AppBadge 99 ($y+20) $a[0] $a[3] $a[4]
 Text $a[1] 154 ($y+17) 18 '#DCEAF7' $true;Text $a[2] 155 ($y+47) 12 '#829EB7'
 Meter 455 ($y+20) 82 (51+$idx) $true
 Text 'Playing' 455 ($y+55) 10 '#8EABC4'
 if($on){Line 565 ($y+39) 612 ($y+39) '#6196BD' 1;Text '→' 598 ($y+27) 17 '#94DAFF';Tag 'Included' 634 ($y+23) 89 $true}
 else{Line 565 ($y+39) 584 ($y+39) '#596A7B' 1;Line 596 ($y+39) 612 ($y+39) '#596A7B' 1;Text '×' 581 ($y+26) 18 '#C1A193';Tag 'Excluded' 634 ($y+23) 89 $false}
 Toggle 748 ($y+25) $on
 Slider 862 ($y+40) 172 ([int]$a[5]/120) $on
 if($on){Text ($a[5].ToString()+'%') 1050 ($y+30) 13 '#B9D8F0'}else{Text '—' 1050 ($y+29) 14 '#5B748B'}
 Text '•••' 1141 ($y+24) 19 '#9DB9D1'
 if($idx -ne 1){Line 97 ($y+97) 1183 ($y+97) '#26384B'}
}
Text 'Excluded apps are never added to the recording mix.' 99 871 13 '#A4BBD0'
Text 'Rules stay with the app when it restarts.' 99 895 12 '#6F90AC'
Line 83 938 1596 938 '#2A3A4B'
Text 'Capture' 91 960 13 '#C7DDEE';Text 'Native size · Native frame rate' 162 961 12 '#85A4BE'
Text 'Shortcuts' 515 960 13 '#C7DDEE';Pill 'Alt + C' 593 952 75 '#253749' '#AFCAE0';Pill 'Shift + `' 676 952 85 '#253749' '#AFCAE0'
Text 'Capture settings  ↗' 1240 962 12 '#ACCAE1';Text 'Open editor  ↗' 1458 962 12 '#C3DEF2'
Text 'AUDIO DESK / EXPANDED VIEW' 62 1057 11 '#6C8CA9' $true
Text 'Blue orb preserved. Per-app volume and balance are proposed controls, subject to implementation.' 62 1081 12 '#6C8CA9'
$bmp.Save((Join-Path $PSScriptRoot 'zenithclipper-audio-desk.png'),[System.Drawing.Imaging.ImageFormat]::Png)

# Companion: practical default overlay with exclusion drawer and detailed tuning on demand.
$g.Clear((C '#0D1116'))
Text 'ZENITHCLIPPER / QUICK CAPTURE' 62 35 12 '#89A0B5' $true
Text 'Silence it in the clip. Keep it in your ears.' 58 63 40 '#ECF4FC' $true
Text 'A smaller everyday panel. App rules and deeper tuning are one click away.' 62 124 17 '#9BADBF'

Round 67 226 720 740 15 '#181F27'
Orb 94 252 39;Text 'ZenithClipper' 149 252 23 '#EDF4FC' $true
Text 'YOUR REPLAY, YOUR MIX' 151 285 10 '#779AB8' $true
Text '×' 737 255 26 '#8AA9C3'
Line 94 324 759 324 '#2B4055'
Round 98 350 6 6 3 '#94DAFF';Text 'Ready to clip' 116 341 13 '#A4D7F6';Text 'Pause' 710 341 12 '#8EACC5'
Text 'Save the last' 95 391 22 '#99B5CD'
Text '30' 86 420 91 '#EEF6FD' $true;Text 'seconds' 232 457 32 '#B6DDF6'
Pill '15s' 464 451 58 '#263E54' '#9EBBD2';Pill '30s' 529 451 58 '#94DAFF' '#142B3D';Pill '1m' 594 451 58 '#263E54' '#9EBBD2';Pill 'Custom' 659 451 99 '#263E54' '#9EBBD2'
Round 98 537 660 68 8 '#94DAFF';Text 'Save clip' 123 556 23 '#142B3D' $true;Pill 'Alt + C' 648 556 88 '#6FADD2' '#122F43'
Text 'Your clip includes' 98 651 17 '#D6E8F8' $true
Text 'Manage app audio  ↗' 595 653 13 '#8CCAF1'
Pill '✓  Overwatch' 98 695 143 '#223D52' '#BAE0FA';Pill '✓  Discord' 252 695 124 '#223D52' '#BAE0FA'
Text 'Kept out' 98 752 12 '#8BA8BF'
Pill '×  Spotify' 98 783 123 '#332F32' '#DAC0B4';Pill '×  Chrome' 232 783 124 '#332F32' '#DAC0B4'
Line 98 851 758 851 '#2B4055'
Text 'Recent clips  ↗' 98 879 14 '#C3DCEE';Text 'Editor  ↗' 310 879 14 '#C3DCEE';Text 'Settings' 676 879 14 '#8EACC5'
Text 'Shift + ` to dismiss' 98 932 11 '#6C90AD'

# Exclusion drawer with selected Spotify so the distinguishing feature is fully visible.
Round 826 226 786 740 15 '#222D38'
Text 'Choose what gets recorded' 856 258 27 '#ECF5FD' $true
Text 'Switch an app off here. You will still hear it.' 858 302 15 '#91B2CC'
Round 857 349 724 43 7 '#0E1F2F';Text 'Search apps, or add a rule by name…' 878 361 14 '#6F94B2';Text '+' 1544 354 26 '#A2CAE7'
Text 'APP' 860 419 10 '#7194B1' $true;Text 'CLIP LEVEL' 1249 419 10 '#7194B1' $true;Text 'RECORD' 1494 419 10 '#7194B1' $true
for($idx=0;$idx -lt 3;$idx++){
 $a=$apps[$idx];$y=453+$idx*103;$on=[bool]$a[6]
 if($idx -eq 2){Round 847 ($y-7) 743 98 9 '#243B50';Rect 847 ($y+8) 3 65 '#94DAFF'}
 AppBadge 864 ($y+8) $a[0] $a[3] $a[4];Text $a[1] 921 ($y+7) 19 '#D6E9F9' $true
 if($on){Text 'Included in your clip' 922 ($y+38) 12 '#7FA7C6'}else{Text 'Excluded · still playing for you' 922 ($y+38) 12 '#D1B6A8'}
 Slider 1252 ($y+28) 125 ([int]$a[5]/120) $on
 if($on){Text ($a[5].ToString()+'%') 1391 ($y+17) 14 '#BADCF5'}else{Text 'Off' 1391 ($y+17) 14 '#829DB4'}
 Toggle 1511 ($y+15) $on
}
Line 861 781 1579 781 '#354D63'
Text 'Spotify is excluded before capture.' 862 808 18 '#D5E9F8' $true
Text 'Your music keeps playing normally. This rule is remembered.' 862 841 13 '#95B2C9'
Pill 'More tuning  ↗' 862 893 146 '#2A465F' '#B5DDF9';Pill 'Done' 1487 887 92 '#94DAFF' '#142B3D'
Text 'QUICK PANEL + APP AUDIO DRAWER / CONCEPT ONLY' 69 1038 11 '#6C8CA9' $true
Text 'Static, code-drawn previews. No application changes or performance measurements.' 69 1063 12 '#6C8CA9'
$bmp.Save((Join-Path $PSScriptRoot 'zenithclipper-audio-panel.png'),[System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose();$bmp.Dispose()
Write-Output 'Rendered audio desk and quick-panel concepts.'

