# Renders language-neutral road paint for the lane rules into ui/:
#  Paint-speed-<n>.png  speed limit numbers painted on the lane (one cell, read along the lane)
#  Paint-notruck.png / Paint-nobike.png  crossed truck / bicycle pictograms for banned lanes
# White ink on transparent; src/RuleMarkings.cs turns opaque pixels into paint.
param([string]$Out=(Join-Path $PSScriptRoot '..\ui'))
Add-Type -AssemblyName System.Drawing
function New-Canvas($w,$h){$b=New-Object System.Drawing.Bitmap($w,$h,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb);$g=[System.Drawing.Graphics]::FromImage($b);$g.Clear([System.Drawing.Color]::Transparent);$g.TextRenderingHint='AntiAliasGridFit';$g.SmoothingMode='AntiAlias';return @($b,$g)}
$format=New-Object System.Drawing.StringFormat;$format.Alignment='Center';$format.LineAlignment='Center';$format.FormatFlags='NoWrap'
foreach($n in 20,30,40,50,60,70,80,100,120){
 $b,$g=New-Canvas 96 64
 $size=64
 do{$font=New-Object System.Drawing.Font('Bahnschrift SemiBold Condensed',$size,[System.Drawing.FontStyle]::Regular,[System.Drawing.GraphicsUnit]::Pixel);$m=$g.MeasureString("$n",$font,1000,$format);if($m.Width -le 92 -and $m.Height -le 76){break};$font.Dispose();$size--}while($size -gt 10)
 $g.DrawString("$n",$font,[System.Drawing.Brushes]::White,(New-Object System.Drawing.RectangleF(0,2,96,64)),$format);$font.Dispose();$g.Dispose()
 $b.Save((Join-Path $Out "Paint-speed-$n.png"),[System.Drawing.Imaging.ImageFormat]::Png);$b.Dispose()
}
# Simplified side-view silhouettes (same shapes as the 分道 buttons in the panel), drawn in a 96x96 cell.
$W=[System.Drawing.Brushes]::White
function Wheels($g,$xs,$y){foreach($x in $xs){$g.CompositingMode='SourceCopy';$g.FillEllipse((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::Transparent)),$x-11,$y-11,22,22);$g.CompositingMode='SourceOver';$g.FillEllipse($W,$x-8,$y-8,16,16)}}
function Holes($g,$rects){$g.CompositingMode='SourceCopy';$clear=New-Object System.Drawing.SolidBrush([System.Drawing.Color]::Transparent);foreach($r in $rects){$g.FillRectangle($clear,$r[0],$r[1],$r[2],$r[3])};$clear.Dispose();$g.CompositingMode='SourceOver'}
$shapes=@{
 car={param($g)$p=New-Object System.Drawing.Drawing2D.GraphicsPath;$p.AddLines([System.Drawing.PointF[]]@((New-Object System.Drawing.PointF(18,58)),(New-Object System.Drawing.PointF(24,46)),(New-Object System.Drawing.PointF(36,36)),(New-Object System.Drawing.PointF(58,36)),(New-Object System.Drawing.PointF(68,46)),(New-Object System.Drawing.PointF(78,49)),(New-Object System.Drawing.PointF(78,60)),(New-Object System.Drawing.PointF(18,60))));$p.CloseFigure();$g.FillPath($W,$p);Holes $g @(,@(38,40,10,7));Holes $g @(,@(50,40,9,7));Wheels $g @(32,64) 62}
 taxi={param($g)& $shapes.car $g;$g.FillRectangle($W,40,26,16,7)}
 bus={param($g)$g.FillRectangle($W,14,28,68,30);Holes $g @(@(19,33,10,9),@(33,33,10,9),@(47,33,10,9),@(61,33,10,9),@(74,33,5,15));Wheels $g @(28,68) 60}
 truck={param($g)$g.FillRectangle($W,12,30,44,28);$g.FillPolygon($W,[System.Drawing.PointF[]]@((New-Object System.Drawing.PointF(58,40)),(New-Object System.Drawing.PointF(74,40)),(New-Object System.Drawing.PointF(84,50)),(New-Object System.Drawing.PointF(84,58)),(New-Object System.Drawing.PointF(58,58))));Wheels $g @(26,72) 60}
 service={param($g)$g.FillRectangle($W,14,36,46,22);$g.FillPolygon($W,[System.Drawing.PointF[]]@((New-Object System.Drawing.PointF(62,42)),(New-Object System.Drawing.PointF(74,42)),(New-Object System.Drawing.PointF(82,50)),(New-Object System.Drawing.PointF(82,58)),(New-Object System.Drawing.PointF(62,58))));$g.FillRectangle($W,26,28,14,6);Wheels $g @(28,70) 60}
 moto={param($g)$g.FillPolygon($W,[System.Drawing.PointF[]]@((New-Object System.Drawing.PointF(26,60)),(New-Object System.Drawing.PointF(40,42)),(New-Object System.Drawing.PointF(56,42)),(New-Object System.Drawing.PointF(64,52)),(New-Object System.Drawing.PointF(76,52)),(New-Object System.Drawing.PointF(70,60))));$pen=New-Object System.Drawing.Pen([System.Drawing.Color]::White,6);$g.DrawLine($pen,58,32,64,46);$g.DrawLine($pen,54,32,64,32);$pen.Dispose();Wheels $g @(26,72) 62}
 bike={param($g)$font=New-Object System.Drawing.Font('Segoe UI Symbol',54,[System.Drawing.FontStyle]::Regular,[System.Drawing.GraphicsUnit]::Pixel);$g.DrawString([char]::ConvertFromUtf32(0x1F6B2),$font,$W,(New-Object System.Drawing.RectangleF(0,6,96,90)),$format);$font.Dispose()}
 ped={param($g)$font=New-Object System.Drawing.Font('Segoe UI Symbol',56,[System.Drawing.FontStyle]::Regular,[System.Drawing.GraphicsUnit]::Pixel);$g.DrawString([char]::ConvertFromUtf32(0x1F6B6),$font,$W,(New-Object System.Drawing.RectangleF(0,6,96,90)),$format);$font.Dispose()}
 uturn={param($g)$pen=New-Object System.Drawing.Pen([System.Drawing.Color]::White,9);$pen.StartCap='Flat';$g.DrawLine($pen,58,74,58,42);$g.DrawArc($pen,38,28,40,32,180,180);$g.DrawLine($pen,38,44,38,58);$pen.Dispose();$g.FillPolygon($W,[System.Drawing.PointF[]]@((New-Object System.Drawing.PointF(28,56)),(New-Object System.Drawing.PointF(48,56)),(New-Object System.Drawing.PointF(38,72))))}
}
# Pictograms are drawn upright (top = driving direction once painted). Paint-no<name>: inside a ring with a slash;
# Paint-only<name>: plain, for a lane reserved to one kind.
foreach($name in $shapes.Keys){
 foreach($crossed in $true,$false){
  if(-not $crossed -and $name -in 'ped','uturn'){continue}
  $b,$g=New-Canvas 96 96
  $g.TranslateTransform(48,48);$g.ScaleTransform(.78,.78);$g.TranslateTransform(-48,-48)
  & $shapes[$name] $g
  $g.ResetTransform()
  if($crossed){$pen=New-Object System.Drawing.Pen([System.Drawing.Color]::White,8);$g.DrawEllipse($pen,6,6,84,84);$g.DrawLine($pen,20,20,76,76);$pen.Dispose()}
  $g.Dispose()
  $b.Save((Join-Path $Out ("Paint-"+($(if($crossed){'no'}else{'only'}))+$name+".png")),[System.Drawing.Imaging.ImageFormat]::Png);$b.Dispose()
 }
}
'done'
