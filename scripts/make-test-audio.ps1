$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$outputDirectory = Join-Path $projectRoot '.runtime'
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
$voice = New-Object -ComObject SAPI.SpVoice
$stream = New-Object -ComObject SAPI.SpFileStream
$format = New-Object -ComObject SAPI.SpAudioFormat
$format.Type = 18 # SAFT16kHz16BitMono
$stream.Format = $format
$stream.Open((Join-Path $outputDirectory 'speech.wav'), 3, $false)
try {
    foreach ($token in $voice.GetVoices()) { if ($token.GetDescription() -match 'English') { $voice.Voice = $token; break } }
    $voice.AudioOutputStream = $stream
    [void]$voice.Speak('This is a live caption test. The audio stays on this computer. We can read the subtitles while watching a video.')
} finally { $stream.Close() }
Write-Host 'Created .runtime/speech.wav. This script does not play or record system audio.'
