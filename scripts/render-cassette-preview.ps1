# VBWR B
# Project: VoiceDucker
# Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
# Modified with AI: OpenAI Codex; native WinUI cassette preview, 2026-10-10.
# Human guidance: Verify the Cassette 1984 visualizer design.
# Evidence: Synthetic render and passive loopback; no microphone, mixer or startup changes.
# Copyright (c) 2026 Umberto Giacobbi
# SPDX-License-Identifier: MIT
# VBWR E

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$previewRoot = Join-Path $projectRoot 'artifacts\cassette-preview'
$appRoot = Join-Path $previewRoot 'app'
New-Item -ItemType Directory -Path $previewRoot -Force | Out-Null
$sourceRoot = [System.Security.SecurityElement]::Escape($projectRoot)
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows10.0.26100.0</TargetFramework>
    <TargetPlatformMinVersion>10.0.22000.0</TargetPlatformMinVersion>
    <UseWinUI>true</UseWinUI>
    <WindowsPackageType>None</WindowsPackageType>
    <WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
    <Platform>x64</Platform>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <ApplicationManifest>$sourceRoot\app.manifest</ApplicationManifest>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.WindowsAppSDK" Version="2.5.1" />
    <PackageReference Include="Microsoft.Windows.SDK.BuildTools" Version="10.0.28000.2705" />
    <PackageReference Include="NAudio.Wasapi" Version="2.2.1" />
    <Compile Include="$sourceRoot\Visuals\CassetteVisualizer.cs" Link="CassetteVisualizer.cs" />
    <Compile Include="$sourceRoot\Audio\AudioSpectrum.cs" Link="AudioSpectrum.cs" />
    <Compile Include="$sourceRoot\Audio\ProcessAudioVisualizer.cs" Link="ProcessAudioVisualizer.cs" />
    <Compile Include="$sourceRoot\Audio\PlaybackMonitor.cs" Link="PlaybackMonitor.cs" />
    <Compile Include="$sourceRoot\Audio\PlaybackIdentity.cs" Link="PlaybackIdentity.cs" />
    <Content Include="$sourceRoot\Assets\Fonts\PressStart2P-Regular.ttf" Link="Assets\Fonts\PressStart2P-Regular.ttf" CopyToOutputDirectory="PreserveNewest" CopyToPublishDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $previewRoot 'CassettePreview.csproj') -Encoding utf8

@'
<Application x:Class="CassettePreview.App"
 xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
 xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
 <Application.Resources><ResourceDictionary><ResourceDictionary.MergedDictionaries>
  <XamlControlsResources xmlns="using:Microsoft.UI.Xaml.Controls" />
 </ResourceDictionary.MergedDictionaries></ResourceDictionary></Application.Resources>
</Application>
'@ | Set-Content -LiteralPath (Join-Path $previewRoot 'App.xaml') -Encoding utf8

@'
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using VoiceDucker.Audio;
using VoiceDucker.Visuals;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace CassettePreview;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, args) =>
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "preview-error.txt"), args.Exception.ToString());
            args.Handled = true;
            Exit();
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new Window { Title = "Cassette visualizer test preview" };
        _window.AppWindow.ResizeClient(new SizeInt32(520, 400));
        _window.AppWindow.Move(new PointInt32(-10000, -10000));
        var visualizer = new CassetteVisualizer();
        var panel = new StackPanel { Spacing = 6 };
        var title = new Grid();
        title.Children.Add(new TextBlock
        {
            Text = "SPOTIFY", FontFamily = CassetteVisualizer.RetroFont, FontSize = 10,
            Foreground = CassetteVisualizer.Brush(0xE8E2CE)
        });
        title.Children.Add(new TextBlock
        {
            Text = "85%", FontFamily = CassetteVisualizer.RetroFont, FontSize = 10,
            Foreground = CassetteVisualizer.Brush(0xE8E2CE), HorizontalAlignment = HorizontalAlignment.Right
        });
        var slider = new Slider { Minimum = 0, Maximum = 100, Value = 85 };
        slider.Resources["SliderTrackFill"] = CassetteVisualizer.Brush(0x111411);
        slider.Resources["SliderTrackValueFill"] = CassetteVisualizer.Brush(0xD3CEBA);
        slider.Resources["SliderThumbBackground"] = CassetteVisualizer.Brush(0xE8E2CE);
        panel.Children.Add(title);
        panel.Children.Add(slider);
        panel.Children.Add(visualizer);
        var card = new Border
        {
            Child = panel, Width = 440, Padding = new Thickness(12), CornerRadius = new CornerRadius(4),
            BorderBrush = CassetteVisualizer.Brush(0x4B4E48), BorderThickness = new Thickness(1),
            Background = CassetteVisualizer.Brush(0x272B28),
            VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center
        };
        var root = new Grid { Padding = new Thickness(12) };
        root.Children.Add(card);
        _window.Content = root;
        root.Loaded += async (_, _) =>
        {
            try
            {
                var scale = root.XamlRoot.RasterizationScale;
                _window.AppWindow.ResizeClient(new SizeInt32((int)(500 * scale), (int)(390 * scale)));
                await Task.Delay(100);
                var analyzer = new AudioSpectrum();
                const int frames = 4410;
                for (var iteration = 0; iteration < 15; iteration++)
                {
                    var pcm = new short[frames * 2];
                    for (var index = 0; index < frames; index++)
                    {
                        var time = (iteration * frames + index) / (double)AudioSpectrum.SampleRate;
                        double value = 0;
                        foreach (var hz in new[] { 90, 180, 440, 900, 1800, 4000, 9000 })
                            value += 0.022 * Math.Sin(2 * Math.PI * hz * time);
                        pcm[index * 2] = (short)(32767 * value);
                        pcm[index * 2 + 1] = (short)(32767 * value * 1.6);
                    }
                    visualizer.Update(analyzer.Process(pcm), muted: false);
                    await Task.Delay(33);
                }
                card.UpdateLayout();
                var fullHeight = card.ActualHeight;
                var bitmap = new RenderTargetBitmap();
                await bitmap.RenderAsync(card);
                var buffer = await bitmap.GetPixelsAsync();
                var pixels = new byte[buffer.Length];
                using (var reader = DataReader.FromBuffer(buffer)) reader.ReadBytes(pixels);
                var folder = await StorageFolder.GetFolderFromPathAsync(AppContext.BaseDirectory);
                var file = await folder.CreateFileAsync("cassette-native-preview.png", CreationCollisionOption.ReplaceExisting);
                using var output = await file.OpenAsync(FileAccessMode.ReadWrite);
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
                encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                    (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels);
                await encoder.FlushAsync();
                visualizer.Visibility = Visibility.Collapsed;
                card.UpdateLayout();
                if (fullHeight - card.ActualHeight < 100)
                    throw new InvalidOperationException("Hiding the visualizer did not collapse the card.");
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "preview-result.txt"),
                    $"PASS: native control render {bitmap.PixelWidth}x{bitmap.PixelHeight}; full={fullHeight:F1}, hidden={card.ActualHeight:F1}.");
                // Validate COM activation under WinUI/WinRT as well as in the headless test.
                using var monitor = new PlaybackMonitor();
                var stream = monitor.Read().Where(stream => stream.IsSpotify && !stream.Muted && stream.Peak > 0.001)
                    .FirstOrDefault();
                if (stream is not null)
                {
                    visualizer.Visibility = Visibility.Visible;
                    card.UpdateLayout();
                    var capture = new ProcessAudioVisualizer(stream.ProcessId);
                    var observed = false;
                    try
                    {
                        for (var index = 0; index < 45; index++)
                        {
                            await Task.Delay(33);
                            var frame = capture.Frame;
                            if (frame.Error is not null) throw new InvalidOperationException(frame.Error);
                            observed |= frame.Ready && frame.LeftRms + frame.RightRms > 0;
                            visualizer.Update(frame, muted: false);
                        }
                        if (!observed) throw new InvalidOperationException("WinUI process capture did not receive live audio.");
                    }
                    finally
                    {
                        capture.Dispose();
                        await capture.Completion.WaitAsync(TimeSpan.FromSeconds(5));
                    }
                    File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "preview-result.txt"),
                        Environment.NewLine + "PASS: live Spotify capture under WinUI/WinRT; clean stop.");
                }
                else
                {
                    File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "preview-result.txt"),
                        Environment.NewLine + "SKIP: no audible Spotify session for the optional WinUI live check.");
                }
            }
            catch (Exception exception)
            {
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "preview-error.txt"), exception.ToString());
            }
            finally { _window.Close(); Exit(); }
        };
        _window.Activate();
    }
}
'@ | Set-Content -LiteralPath (Join-Path $previewRoot 'App.xaml.cs') -Encoding utf8

& dotnet publish (Join-Path $previewRoot 'CassettePreview.csproj') -c Debug -p:ImportDirectoryBuildProps=false `
    -p:SelfContained=true -p:PublishReadyToRun=false -o $appRoot --nologo
if ($LASTEXITCODE -ne 0) { throw 'Native visualizer preview build failed.' }
# Reset only result markers, never the app's settings or running instance.
foreach ($marker in @('preview-error.txt', 'preview-result.txt')) {
    $markerPath = Join-Path $appRoot $marker
    if (Test-Path -LiteralPath $markerPath) { Remove-Item -LiteralPath $markerPath }
}
$process = Start-Process -FilePath (Join-Path $appRoot 'CassettePreview.exe') `
    -WorkingDirectory $appRoot -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit(20000)) { throw 'Native preview did not finish within 20 seconds.' }
$errorPath = Join-Path $appRoot 'preview-error.txt'
if (Test-Path -LiteralPath $errorPath) { throw (Get-Content -LiteralPath $errorPath -Raw) }
$resultPath = Join-Path $appRoot 'preview-result.txt'
if (-not (Test-Path -LiteralPath $resultPath)) { throw "Native preview exited without a result: $($process.ExitCode)" }
Get-Content -LiteralPath $resultPath
Write-Host "Preview: $(Join-Path $appRoot 'cassette-native-preview.png')"
