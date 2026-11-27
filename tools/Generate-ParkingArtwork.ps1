# Original deterministic sign artwork; no external images or fonts are redistributed.
Add-Type -AssemblyName System.Drawing
$target=Join-Path $PSScriptRoot '../ui'
function Save-Art($name,$w,$height,$draw){
 $bmp=[Drawing.Bitmap]::new($w,$height);$g=[Drawing.Graphics]::FromImage($bmp)
 $g.SmoothingMode='AntiAlias';$g.TextRenderingHint='AntiAliasGridFit';$g.Clear([Drawing.Color]::Transparent)
 & $draw $g $w $height
 $bmp.Save((Join-Path $target ($name+'.png')),[Drawing.Imaging.ImageFormat]::Png);$g.Dispose();$bmp.Dispose()
}
Save-Art 'Parking-disc' 128 128 {param($g) $g.FillEllipse([Drawing.Brushes]::White,1,1,126,126)}
Save-Art 'Parking-ban' 128 128 {param($g) $pen=[Drawing.Pen]::new([Drawing.Color]::White,12);$g.DrawEllipse($pen,7,7,114,114);$g.DrawLine($pen,23,23,105,105);$pen.Dispose()}
Save-Art 'Parking-P' 128 128 {param($g) $font=[Drawing.Font]::new('Arial',82,[Drawing.FontStyle]::Bold,[Drawing.GraphicsUnit]::Pixel);$format=[Drawing.StringFormat]::new();$format.Alignment='Center';$format.LineAlignment='Center';$g.DrawString('P',$font,[Drawing.Brushes]::White,[Drawing.RectangleF]::new(0,0,128,128),$format);$font.Dispose();$format.Dispose()}
$labels=@{'zh-Hans'='禁止停车';'zh-Hant'='禁止停車';en='NO PARKING';ja='駐車禁止';ko='주차 금지';de='PARKEN VERBOTEN';fr='STATIONNEMENT INTERDIT';es='PROHIBIDO APARCAR';it='DIVIETO DI SOSTA';pl='ZAKAZ PARKOWANIA';pt='PROIBIDO ESTACIONAR';ru='СТОЯНКА ЗАПРЕЩЕНА'}
foreach($entry in $labels.GetEnumerator()){
 $label=$entry.Value
 Save-Art ('Parking-'+$entry.Key) 384 64 {param($g,$w,$height)
  $size=48.0;$font=$null
  do{if($font){$font.Dispose()};$font=[Drawing.Font]::new('Microsoft YaHei',$size,[Drawing.FontStyle]::Bold,[Drawing.GraphicsUnit]::Pixel);$size-=1}while($g.MeasureString($label,$font).Width -gt 378)
  $format=[Drawing.StringFormat]::new();$format.Alignment='Center';$format.LineAlignment='Center';$g.DrawString($label,$font,[Drawing.Brushes]::White,[Drawing.RectangleF]::new(0,0,$w,$height),$format);$font.Dispose();$format.Dispose()
 }
}
