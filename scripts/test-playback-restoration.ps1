# VoiceDucker - Copyright (c) 2026 Umberto Giacobbi. MIT license.
# Production scalar controller with fake devices and clock; no real audio APIs.
[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$testRoot=Join-Path $projectRoot 'artifacts\playback-restoration-tests'
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$sourceRoot=[Security.SecurityElement]::Escape($projectRoot)
@"
<Project Sdk="Microsoft.NET.Sdk">
 <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework>
 <ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors>
 <EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>
 <ItemGroup><Compile Include="Program.cs"/><Compile Include="FakeMixer.cs"/>
 <Compile Include="$sourceRoot\Audio\PlaybackSessions.cs" Link="PlaybackSessions.cs"/>
<Compile Include="$sourceRoot\Audio\PlaybackVolumeEvents.cs" Link="PlaybackVolumeEvents.cs"/>
<Compile Include="$sourceRoot\Audio\AudioDiagnostics.cs" Link="AudioDiagnostics.cs"/>
<Compile Include="$sourceRoot\Audio\DuckingSettings.cs" Link="DuckingSettings.cs"/>
<Compile Include="$sourceRoot\Audio\PlaybackIdentity.cs" Link="PlaybackIdentity.cs"/>
 </ItemGroup></Project>
"@ | Set-Content -LiteralPath (Join-Path $testRoot 'RestorationTests.csproj') -Encoding utf8
@'
namespace NAudio.CoreAudioApi.Interfaces {
 public enum AudioSessionState { AudioSessionStateActive,AudioSessionStateInactive,AudioSessionStateExpired }
 public enum AudioSessionDisconnectReason { DisconnectReasonDeviceRemoval }
 public interface IAudioSessionEvents {
 int OnSimpleVolumeChanged(float volume,bool mute,ref Guid context);
 int OnDisplayNameChanged(string value,ref Guid context); int OnIconPathChanged(string value,ref Guid context);
 int OnChannelVolumeChanged(uint count,IntPtr values,uint index,ref Guid context);
 int OnGroupingParamChanged(ref Guid id,ref Guid context);
 int OnStateChanged(AudioSessionState state); int OnSessionDisconnected(AudioSessionDisconnectReason reason);
 }
}
namespace VoiceDucker.Audio {
 internal sealed class FakeSession(string key,float volume) {
 public string Key{get;}=key; public float Volume{get;set;}=volume; public float Peak{get;set;}=.5f;
 public bool Muted{get;set;} public bool Present{get;set;}=true; public bool Active{get;set;}=true;
 public bool FailWrites{get;set;} public bool IgnoreWrites{get;set;} public float Offset{get;set;} public int Writes{get;set;}
 }
 internal sealed class FakeMixer(params FakeSession[] sessions):IPlaybackSource {
 public List<FakeSession> Sessions{get;}=[..sessions];
 public bool FailRead{get;set;}
 public PlaybackReadResult Read()=>FailRead?throw new InvalidOperationException("Synthetic read failure"):new(Sessions.Where(s=>s.Present).Select(s=>new PlaybackObservation(s.Key,"Spotify",101,s.Volume,s.Peak,s.Muted,s.Active)).ToArray(),[]);
 public float ReadVolume(string key)=>Sessions.Single(s=>s.Key==key).Volume;
 public float SetVolume(string key,float value) {
 var s=Sessions.Single(s=>s.Key==key); if(s.FailWrites)throw new InvalidOperationException("Synthetic write failure");
 s.Writes++; if(!s.IgnoreWrites)s.Volume=Math.Clamp(value+s.Offset,0,1); return s.Volume;
 }
 public void Dispose(){}
 }
}

'@ | Set-Content -LiteralPath (Join-Path $testRoot 'FakeMixer.cs') -Encoding utf8
@'
using VoiceDucker.Audio;
var checks = 0;
void Check(bool ok, string message) { checks++; if (!ok) throw new InvalidOperationException(message); }
void Near(float actual, float expected, string message) => Check(Math.Abs(actual-expected)<0.0001f, $"{message}: {actual:R} != {expected:R}");
long now=1000;
var settings = new DuckingSettings(50,1000,2000,true);
var events = new PlaybackVolumeEvents();
var music = new FakeSession("music",1);
using var source = new FakeMixer(music);
using var playback = new PlaybackSessions(source,events,()=>now);
void Tick(bool run,bool speak,int ms=0,DuckingSettings? s=null) { now+=ms; Check(playback.Update(run,speak,s??settings),"Discovery failed."); }
PlaybackStream Row(string key="music") => playback.Streams.Single(s=>s.Key==key);
Tick(false,false);
Check(music.Writes==0,"Initialization changed audio.");
playback.MonitoringEnabled();
Tick(true,false);
Tick(true,true);
Check(music.Writes==0 && !Row().IsLowered,"Reduction claimed before a mixer write.");
Tick(true,true,500); Near(music.Volume,.75f,"Fade midpoint");
Check(Row().IsLowered,"Observed attenuation missing.");
Tick(true,true,500); Near(music.Volume,.5f,"Fade target");
var writes=music.Writes;
for(var i=0;i<20;i++) Tick(true,true,50);
Check(music.Writes==writes,"Stable target written on polling.");
for(var cycle=0;cycle<8;cycle++) {
 Tick(true,false); Tick(true,false,1000); Near(music.Volume,.75f,"Restore midpoint");
 Tick(true,true); Tick(true,true,1000); Near(music.Volume,.5f,"Interrupted fade ratcheted");
 Near(Row().NormalVolume,1,"Fade rebased normal");
}
playback.SetNormalVolume("music",.13f); Tick(true,true);
Near(Row().NormalVolume,.13f,"Slider normal"); Near(music.Volume,.065f,"13 percent normal must duck to 6.5");
writes=music.Writes;
for(var i=0;i<10;i++) Tick(true,true,50);
Check(music.Writes==writes,"Stable slider projection written repeatedly.");
Tick(true,false); Tick(true,false,2000); Near(music.Volume,.13f,"Silence must use selected normal");
Tick(true,true); Tick(true,true,1000); Near(music.Volume,.065f,"New voice cycle");
playback.RequestStartupRestoration(); Tick(false,false);
Near(music.Volume,1,"Disable startup baseline"); Near(Row().NormalVolume,.13f,"Disable forgot normal");
Check(!playback.HasPendingRestoration,"Successful restore still pending.");
writes=music.Writes;
for(var i=0;i<10;i++) Tick(false,false,50);
Check(music.Writes==writes,"Mic-off polling undid startup restoration.");
playback.MonitoringEnabled(); Tick(true,false); Near(music.Volume,.13f,"Enable retained normal");
playback.SetNormalVolume("music",.6f); Tick(true,true,s:settings with {ReductionPercent=100,FadeDownMilliseconds=0});
Near(music.Volume,0,"100 percent attenuation"); Near(Row().NormalVolume,.6f,"Zero output destroyed normal");
writes=music.Writes;
playback.SetNormalVolume("music",.7f); Tick(true,true,s:settings with {ReductionPercent=100});
Check(Row().IsLowered && music.Writes==writes,"Changing normal at zero output lost attenuation or rewrote zero.");
playback.SetNormalVolume("music",.6f); Tick(true,true,s:settings with {ReductionPercent=100});
Tick(true,false,s:settings with {ReductionPercent=100}); Tick(true,false,2000,settings with {ReductionPercent=100});
Near(music.Volume,.6f,"Zero-output cycle normal restore");
playback.RequestStartupRestoration(); Tick(false,false); Near(music.Volume,1,"Zero-output cycle startup restore");

var sink=events.CreateSink("music");
var own=events.EventContext;
sink.OnSimpleVolumeChanged(.3f,false,ref own);
Check(events.Latest("music") is null,"Own EventContext treated as external.");
playback.SetNormalVolume("music",1); Tick(true,true,s:settings with {FadeDownMilliseconds=0});
var external=Guid.NewGuid(); music.Volume=.4f;
sink.OnSimpleVolumeChanged(.4f,false,ref external); Tick(true,true);
Near(Row().NormalVolume,.4f,"External callback payload lost"); Near(music.Volume,.2f,"External normal projection");
sink.OnSimpleVolumeChanged(.6f,false,ref external); Tick(true,true);
Near(Row().NormalVolume,.4f,"Delayed foreign echo changed normal");
Near(music.Volume,.2f,"Delayed foreign echo caused cumulative ducking");
music.Muted=true; sink.OnSimpleVolumeChanged(.2f,true,ref external); writes=music.Writes;
Tick(true,true); Near(Row().NormalVolume,.4f,"Mute-only event rebased normal");
Check(music.Writes==writes,"Muted playback changed.");
playback.RequestStartupRestoration(); Tick(false,false);
Check(playback.HasPendingRestoration && !playback.CanAdvanceRestoration,"Muted restoration falsely complete.");
Check(music.Writes==writes,"Restoration wrote muted playback.");
music.Muted=false; sink.OnSimpleVolumeChanged(.2f,false,ref external); Tick(false,false);
Near(music.Volume,1,"Deferred unmuted restoration");
Check(!playback.HasPendingRestoration,"Deferred restoration not acknowledged.");
sink.Deactivate(); sink.OnSimpleVolumeChanged(.1f,false,ref external);
Near(events.Latest("music")!.Volume,.2f,"Inactive callback accepted external change");

// User intent is ordered at issue time, independently of when the worker executes it.
var orderingSink=events.CreateSink("music");
music.Volume=.8f; orderingSink.OnSimpleVolumeChanged(.8f,false,ref external);
playback.SetNormalVolume("music",.36f); Tick(true,true);
Near(Row().NormalVolume,.36f,"Older external event overwrote newer UI command");
Near(music.Volume,.18f,"Newer UI command was not projected");
var queuedUiVersion=events.NextIntentVersion();
music.Volume=.8f; orderingSink.OnSimpleVolumeChanged(.8f,false,ref external);
playback.SetNormalVolume("music",.36f,queuedUiVersion); Tick(true,true);
Near(Row().NormalVolume,.8f,"Newer external event lost to an earlier queued UI command");
Near(music.Volume,.4f,"Newer external command was not projected");
playback.SetNormalVolume("music",.22f,queuedUiVersion); Tick(true,true);
Near(Row().NormalVolume,.8f,"An already superseded UI command was replayed");
orderingSink.Deactivate();

playback.SetNormalVolume("music",1); Tick(true,true,s:settings with {FadeDownMilliseconds=0});
music.Present=false; playback.RequestStartupRestoration(); Tick(false,false);
Check(playback.HasPendingRestoration && playback.RestorationError is not null,"Absent owned session falsely restored.");
music.Present=true; Tick(false,false); Near(music.Volume,1,"Reappearing session startup ownership");

var late=new FakeSession("late",.8f);
var silent=new FakeSession("silent",.7f){Peak=0};
var broken=new FakeSession("broken",.9f){FailWrites=true};
source.Sessions.AddRange([late,silent,broken]); Tick(true,true,s:settings with {FadeDownMilliseconds=0});
Near(late.Volume,.4f,"Late session duck"); Near(silent.Volume,.7f,"Silent session changed");
Check(playback.Errors.Any(e=>e.Contains("Synthetic write failure")),"Session failure not exposed.");
Check(Row("late").IsLowered,"Broken session blocked healthy playback.");
playback.SetNormalVolume("silent",.5f); Tick(true,true);
Check(silent.Writes==0,"Normal command changed silent session.");
silent.Peak=.5f; Tick(true,true); Near(silent.Volume,.25f,"Deferred normal command");
broken.FailWrites=false; playback.RequestStartupRestoration(); Tick(false,false);
Near(late.Volume,.8f,"Late startup restore"); Near(silent.Volume,.7f,"Silent-origin startup restore");
Near(broken.Volume,.9f,"Failed write lost restoration ownership");

var ignored=new FakeSession("ignored",.8f){IgnoreWrites=true};
var quantized=new FakeSession("quantized",1){Offset=.00005f};
source.Sessions.AddRange([ignored,quantized]); Tick(true,true,s:settings with {FadeDownMilliseconds=0});
Check(playback.Errors.Any(e=>e.Contains("did not confirm")),"Ignored write claimed successful.");
Check(!Row("ignored").IsLowered,"Ignored write reported attenuation.");
Near(quantized.Volume,.50005f,"Quantized target"); writes=quantized.Writes;
for(var i=0;i<20;i++) Tick(true,true,50,settings with {FadeDownMilliseconds=0});
Check(quantized.Writes==writes,"Quantized target written every tick.");
ignored.IgnoreWrites=false; quantized.Offset=0;
playback.RequestStartupRestoration(); Tick(false,false); Near(quantized.Volume,1,"Quantized startup restore");
Check(!playback.HasPendingRestoration,"Final restore unconfirmed.");
playback.SetNormalVolume("music",.8f); Tick(true,true); Near(music.Volume,.4f,"Exclusion preparation");
Tick(true,true,s:settings with {BlacklistedProcesses=["Spotify"]});
Tick(true,true,2000,settings with {BlacklistedProcesses=["Spotify"]});
Near(music.Volume,.8f,"Exclusion must restore selected normal");
Check(!Row().IsEligible && !Row().IsLowered,"Excluded stream remained eligible.");
playback.RequestStartupRestoration(); Tick(false,false); Near(music.Volume,1,"Exclusion replaced startup");

// Re-enabling after a deferred muted restore must not briefly raise startup volume.
var resumedSession=new FakeSession("resumed",1);
using var resumedSource=new FakeMixer(resumedSession);
using var resumed=new PlaybackSessions(resumedSource,new PlaybackVolumeEvents(),()=>now);
resumed.Update(false,false,settings);
resumed.SetNormalVolume("resumed",.4f); resumed.Update(true,true,settings);
Near(resumedSession.Volume,.2f,"Resume preparation");
resumedSession.Muted=true; resumed.RequestStartupRestoration(); resumed.Update(false,false,settings);
Check(resumed.HasPendingRestoration,"Muted restore was not retained.");
resumed.MonitoringEnabled(); writes=resumedSession.Writes;
resumed.Update(true,true,settings); resumedSession.Muted=false; resumed.Update(true,true,settings);
Near(resumedSession.Volume,.2f,"Re-enable caused a startup-volume bump");
Check(resumedSession.Writes==writes,"Re-enable unnecessarily rewrote an already correct target.");
resumed.RequestStartupRestoration(2000); resumed.Update(false,false,settings);
now+=1000; resumed.Update(false,false,settings); Near(resumedSession.Volume,.6f,"Exit fade midpoint");
now+=1000; resumed.Update(false,false,settings); Near(resumedSession.Volume,1,"Exit fade startup target");
Check(!resumed.HasPendingRestoration,"Exit fade did not finish restoration.");

// Actual slider intent is inverted once, using the gate and reduction consumed in this update.
var actualSession=new FakeSession("actual",1);
using var actualSource=new FakeMixer(actualSession);
var actualEvents=new PlaybackVolumeEvents();
using var actualController=new PlaybackSessions(actualSource,actualEvents,()=>now);
var instant=settings with {FadeDownMilliseconds=0,FadeUpMilliseconds=0};
void ActualTick(bool speaking,DuckingSettings? current=null)=>Check(actualController.Update(true,speaking,current??instant),"Actual slider discovery failed.");
PlaybackStream ActualRow()=>actualController.Streams.Single();
void FailedEdit(Task command,string expected) {
 Check(command.IsCompleted,"A failed actual-volume command did not settle.");
 try {command.GetAwaiter().GetResult(); Check(false,"Actual-volume command unexpectedly succeeded.");}
 catch(InvalidOperationException exception) {Check(exception.Message.Contains(expected),"Actual-volume failure was not descriptive: "+exception.Message);}
}
ActualTick(false);
var edit=actualController.SetPlaybackVolume("actual",.2f);
Check(!edit.IsCompleted,"Actual-volume intent was converted before a coherent update.");
ActualTick(true); edit.GetAwaiter().GetResult();
Near(ActualRow().NormalVolume,.4f,"20 percent actual must retain 40 percent normal at half gain");
Near(actualSession.Volume,.2f,"Actual slider was attenuated twice");
writes=actualSession.Writes;
for(var i=0;i<100;i++) {ActualTick(true); Near(ActualRow().NormalVolume,.4f,"Polling rebased actual into normal");}
Check(actualSession.Writes==writes,"Settled actual target was written on 100 polls.");
edit=actualController.SetPlaybackVolume("actual",.2f); ActualTick(true); edit.GetAwaiter().GetResult();
Check(actualSession.Writes==writes,"An unchanged actual slider request rewrote the mixer.");
ActualTick(false); Near(actualSession.Volume,.4f,"Voice release did not restore selected normal");
for(var cycle=0;cycle<8;cycle++) {ActualTick(true); Near(actualSession.Volume,.2f,"Voice cycle ratcheted actual"); ActualTick(false); Near(actualSession.Volume,.4f,"Voice cycle ratcheted normal");}
edit=actualController.SetPlaybackVolume("actual",.2f); ActualTick(false); edit.GetAwaiter().GetResult();
Near(ActualRow().NormalVolume,.2f,"Actual intent used a stale speaking state");
ActualTick(true); Near(actualSession.Volume,.1f,"Silence edit was not used by the next voice cycle");
edit=actualController.SetPlaybackVolume("actual",.2f); ActualTick(true,instant with {ReductionPercent=80}); edit.GetAwaiter().GetResult();
Near(ActualRow().NormalVolume,1,"Actual intent used a stale reduction setting");
Near(actualSession.Volume,.2f,"New reduction did not own actual conversion");
edit=actualController.SetPlaybackVolume("actual",.9f); ActualTick(true); edit.GetAwaiter().GetResult();
Near(ActualRow().NormalVolume,1,"Saturated actual intent exceeded normal unity"); Near(actualSession.Volume,.5f,"Saturated actual intent exceeded ducked ceiling");
ActualTick(true,instant with {ReductionPercent=100}); writes=actualSession.Writes;
foreach(var requested in new[]{0f,.2f}) {
 edit=actualController.SetPlaybackVolume("actual",requested); ActualTick(true,instant with {ReductionPercent=100});
 FailedEdit(edit,"reduction is 100%"); Near(ActualRow().NormalVolume,1,"Zero gain edit destroyed normal");
}
Check(actualSession.Writes==writes,"A rejected zero-gain edit wrote the mixer.");
ActualTick(false); Near(actualSession.Volume,1,"Rejected zero-gain edits damaged release volume");
actualSession.Peak=0; writes=actualSession.Writes;
edit=actualController.SetPlaybackVolume("actual",.2f); ActualTick(false); FailedEdit(edit,"silent");
Near(ActualRow().NormalVolume,1,"Silent edit changed normal"); Check(actualSession.Writes==writes,"Silent edit wrote the mixer.");
actualSession.Peak=.5f; actualSession.Muted=true;
edit=actualController.SetPlaybackVolume("actual",.2f); ActualTick(false); FailedEdit(edit,"muted");
Near(ActualRow().NormalVolume,1,"Muted edit changed normal"); Check(actualSession.Writes==writes,"Muted edit wrote the mixer."); actualSession.Muted=false;

var superseded=actualController.SetPlaybackVolume("actual",.1f);
edit=actualController.SetPlaybackVolume("actual",.3f);
Check(superseded.IsCompletedSuccessfully,"A superseded actual intent remained pending."); ActualTick(true); edit.GetAwaiter().GetResult();
Near(actualSession.Volume,.3f,"A superseded actual intent was replayed"); Near(ActualRow().NormalVolume,.6f,"Latest actual intent lost normal");
superseded=actualController.SetPlaybackVolume("actual",.1f); actualController.SetNormalVolume("actual",.8f);
Check(superseded.IsCompletedSuccessfully,"A newer normal command left the actual intent pending."); ActualTick(true); Near(actualSession.Volume,.4f,"A newer normal intent lost priority");
var actualSink=actualEvents.CreateSink("actual"); var foreign=Guid.NewGuid();
actualSession.Volume=.7f; actualSink.OnSimpleVolumeChanged(.7f,false,ref foreign);
edit=actualController.SetPlaybackVolume("actual",.2f); ActualTick(true); edit.GetAwaiter().GetResult();
Near(actualSession.Volume,.2f,"An older external callback overwrote the actual slider");
superseded=actualController.SetPlaybackVolume("actual",.1f);
actualSession.Volume=.8f; actualSink.OnSimpleVolumeChanged(.8f,false,ref foreign); ActualTick(true);
Check(superseded.IsCompletedSuccessfully,"A newer external intent left an actual command pending."); Near(actualSession.Volume,.4f,"A newer external intent lost priority");
actualSink.Deactivate();

actualSession.FailWrites=true; edit=actualController.SetPlaybackVolume("actual",.2f); ActualTick(true); FailedEdit(edit,"Synthetic write failure"); actualSession.FailWrites=false;
actualSession.IgnoreWrites=true; edit=actualController.SetPlaybackVolume("actual",.1f); ActualTick(true); FailedEdit(edit,"did not confirm"); actualSession.IgnoreWrites=false;
edit=actualController.SetPlaybackVolume("actual",.2f); actualSource.FailRead=true;
Check(!actualController.Update(true,true,instant),"A synthetic read failure claimed discovery success."); FailedEdit(edit,"could not be refreshed"); actualSource.FailRead=false; ActualTick(true);
edit=actualController.SetPlaybackVolume("actual",.2f); actualSession.Present=false; ActualTick(true); FailedEdit(edit,"unavailable"); actualSession.Present=true; ActualTick(true);
edit=actualController.SetPlaybackVolume("actual",.2f); actualController.RequestStartupRestoration(); FailedEdit(edit,"monitoring stopped");
Check(actualController.Update(false,false,instant),"Actual controller startup restoration discovery failed.");
Near(actualSession.Volume,1,"Actual slider changed the immutable startup baseline");
edit=actualController.SetPlaybackVolume("actual",.2f); actualController.Dispose();
Check(edit.IsFaulted,"Disposal left an actual-volume intent unresolved.");
try {edit.GetAwaiter().GetResult(); Check(false,"Disposed actual edit succeeded.");} catch(ObjectDisposedException) {Check(true,"Disposed actual intent settled.");}
Console.WriteLine($"PASS: {checks} synthetic playback assertions. No Core Audio, microphone, or real mixer access.");

'@ | Set-Content -LiteralPath (Join-Path $testRoot 'Program.cs') -Encoding utf8
& dotnet build (Join-Path $testRoot 'RestorationTests.csproj') -c Release -p:ImportDirectoryBuildProps=false --nologo
if($LASTEXITCODE -ne 0){throw 'Synthetic playback harness build failed.'}
& dotnet (Join-Path $testRoot 'bin\Release\net10.0\RestorationTests.dll')
if($LASTEXITCODE -ne 0){throw 'Synthetic playback checks failed.'}
