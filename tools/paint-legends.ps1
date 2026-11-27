# Renders the road paint legends (禁止变道 / NO LANE CHANGE ...) into ui/Paint-<lang>.png.
# Each legend is a vertical strip: one cell per character (CJK) or word (other scripts), read top to bottom,
# white ink on a transparent background. DirectionMarkers.cs turns the opaque pixels into road paint.
# The cell counts here must match the Legends table in src/DirectionMarkers.cs.
# Fonts come from Windows; Paint-en / Paint-zh-* are hand-made and not regenerated.
param([string]$Out=(Join-Path $PSScriptRoot '..\ui'))
Add-Type -AssemblyName System.Drawing
$legends=@(
 @{Id='ja';Font='Yu Gothic UI';Style='Bold';Cjk=$true;Cells=@('車','線','変','更','禁','止')},
 @{Id='ko';Font='Malgun Gothic';Style='Bold';Cjk=$true;Cells=@('차','로','변','경','금','지')},
 @{Id='de';Font='Bahnschrift SemiBold Condensed';Style='Regular';Cjk=$false;Cells=@('KEIN','SPURWECHSEL')},
 @{Id='fr';Font='Bahnschrift SemiBold Condensed';Style='Regular';Cjk=$false;Cells=@('CHANGEMENT','DE VOIE','INTERDIT')},
 @{Id='es';Font='Bahnschrift SemiBold Condensed';Style='Regular';Cjk=$false;Cells=@('NO','CAMBIAR','CARRIL')},
 @{Id='it';Font='Bahnschrift SemiBold Condensed';Style='Regular';Cjk=$false;Cells=@('VIETATO','CAMBIARE','CORSIA')},
 @{Id='pt';Font='Bahnschrift SemiBold Condensed';Style='Regular';Cjk=$false;Cells=@('PROIBIDO','MUDAR','DE FAIXA')},
 @{Id='pl';Font='Bahnschrift SemiBold Condensed';Style='Regular';Cjk=$false;Cells=@('ZAKAZ','ZMIANY','PASA')},
 @{Id='ru';Font='Bahnschrift SemiBold Condensed';Style='Regular';Cjk=$false;Cells=@('ПЕРЕСТРОЕНИЕ','ЗАПРЕЩЕНО')}
)
foreach($l in $legends){
 $cw=if($l.Cjk){64}else{256};$ch=64;$n=$l.Cells.Count
 $bmp=New-Object System.Drawing.Bitmap($cw,($ch*$n),[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
 $g=[System.Drawing.Graphics]::FromImage($bmp)
 $g.Clear([System.Drawing.Color]::Transparent)
 $g.TextRenderingHint=[System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
 $g.SmoothingMode=[System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
 $format=New-Object System.Drawing.StringFormat;$format.Alignment='Center';$format.LineAlignment='Center';$format.FormatFlags='NoWrap'
 for($i=0;$i -lt $n;$i++){
  $text=$l.Cells[$i]
  # Largest size that fits the cell with a small margin.
  $size=60
  do{
   $font=New-Object System.Drawing.Font($l.Font,$size,[System.Drawing.FontStyle]$l.Style,[System.Drawing.GraphicsUnit]::Pixel)
   $m=$g.MeasureString($text,$font,10000,$format)
   if($m.Width -le $cw*0.94 -and $m.Height -le $ch*1.18){break}
   $font.Dispose();$size-=1
  }while($size -gt 8)
  $rect=New-Object System.Drawing.RectangleF(0,($i*$ch+1),$cw,$ch)
  $g.DrawString($text,$font,[System.Drawing.Brushes]::White,$rect,$format)
  $font.Dispose()
 }
 $g.Dispose()
 $path=Join-Path $Out ('Paint-'+$l.Id+'.png')
 $bmp.Save($path,[System.Drawing.Imaging.ImageFormat]::Png);$bmp.Dispose()
 "{0} {1} cells -> {2}" -f $l.Id,$n,$path
}
