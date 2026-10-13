$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$dest = 'E:\VoiceDucker\Assets\MicrosoftStore'
$generatedRoot = 'E:\.codex\generated_images\01a12bf9-20e3-73b1-8294-42ffafad7a2d'
function Export-Png([string]$sourceName, [string]$fileName, [int]$width, [int]$height) {
    $source = Join-Path $generatedRoot $sourceName
    $original = [Drawing.Image]::FromFile($source)
    $bitmap = [Drawing.Bitmap]::new($width, $height, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.DrawImage($original, [Drawing.Rectangle]::new(0,0,$width,$height),0,0,$original.Width,$original.Height,[Drawing.GraphicsUnit]::Pixel)
        $bitmap.Save((Join-Path $dest $fileName), [Drawing.Imaging.ImageFormat]::Png)
        [pscustomobject]@{ File=$fileName; Native=('{0}x{1}' -f $original.Width,$original.Height); Export=('{0}x{1}' -f $width,$height) }
    } finally { $graphics.Dispose(); $bitmap.Dispose(); $original.Dispose() }
}
Export-Png 'exec-6b0923c3-7099-4f51-8584-986232ef8a76.png' 'poster-1440x2160.png' 1440 2160
Export-Png 'exec-6b0923c3-7099-4f51-8584-986232ef8a76.png' 'poster-720x1080.png' 720 1080
Export-Png 'exec-9f36a118-17c3-40e6-91b7-1838cea5464b.png' 'box-art-2160x2160.png' 2160 2160
Export-Png 'exec-9f36a118-17c3-40e6-91b7-1838cea5464b.png' 'box-art-1080x1080.png' 1080 1080
Export-Png 'exec-24360a30-db4b-4cfa-9a6f-8e5cb46dac5b.png' 'app-tile-imagegen-300.png' 300 300
Export-Png 'exec-24360a30-db4b-4cfa-9a6f-8e5cb46dac5b.png' 'app-tile-imagegen-150.png' 150 150
Export-Png 'exec-24360a30-db4b-4cfa-9a6f-8e5cb46dac5b.png' 'app-tile-imagegen-71.png' 71 71
Get-ChildItem -LiteralPath $generatedRoot -Filter '*.png' | ForEach-Object {
    $target = Join-Path "$dest\originals" $_.Name
    if (-not (Test-Path -LiteralPath $target)) { Copy-Item -LiteralPath $_.FullName -Destination $target }
    if ((Get-FileHash -LiteralPath $_.FullName).Hash -ne (Get-FileHash -LiteralPath $target).Hash) { throw "Hash mismatch: $($_.Name)" }
}
Get-ChildItem -LiteralPath $dest -Filter '*.png' | ForEach-Object {
    $img = [Drawing.Image]::FromFile($_.FullName)
    try {
        $limit = if ($_.Name -like 'app-tile*') { 5MB } else { 50MB }
        if ($_.Length -ge $limit) { throw "PNG too large: $($_.Name)" }
        [pscustomobject]@{File=$_.Name;Width=$img.Width;Height=$img.Height;MB=[Math]::Round($_.Length/1MB,2)}
    } finally { $img.Dispose() }
}
Get-ChildItem -LiteralPath $dest -Recurse -Filter '*.png' | Sort-Object FullName | ForEach-Object { '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName).Hash, $_.FullName.Substring($dest.Length+1) } | Set-Content -LiteralPath "$dest\PNG-SHA256SUMS.txt" -Encoding utf8
