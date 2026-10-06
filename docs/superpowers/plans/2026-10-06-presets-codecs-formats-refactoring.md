# Presets, Codecs and Export Formats Refactoring Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Decouple compression quality profiles from video codecs, adapt the UI dynamically based on the export format (MP4, GIF, MP3), and fix output path and hardware encoder bugs.

**Architecture:** Refactor `CompressionProfile` to represent codec-agnostic quality strategies (CRF/CQ and speed presets); update `FfmpegService.BuildCompressionArguments` to synthesize correct FFmpeg CLI arguments given the selected codec and hardware encoder (NVENC, QSV, AMF); update `MainViewModel` and `MainWindow.xaml` to dynamically hide non-relevant parameters when exporting to GIF or MP3.

**Tech Stack:** C# 13, .NET 9, WPF, CommunityToolkit.Mvvm, WPF-UI, FFmpeg CLI, xUnit.

**Spec:** `docs/superpowers/specs/2026-10-06-presets-codecs-formats-refactoring-design.md`

## Global Constraints

- .NET 9.0 WPF application (`LowSizeMp4.csproj`).
- Preserve all existing file paths and namespaces (`LowSizeMp4`).
- Maintain backward compatibility in `SettingsService` for previously saved preset names.
- Do not modify files outside `d:\projects_c#\lowsizemp4`.
- Keep FFmpeg argument order clean: input flags before `-i`, encoding flags and output filters before output file.

## Review Focus

1. **Preset and Codec Orthogonality:** Selecting "Max Compression" with "H.264" must use `libx264 -crf 28`, while with "H.265" it must use `libx265 -crf 30 -tag:v hvc1`, and with "AV1" it must use `libsvtav1 -crf 32`.
2. **GPU Encoder Flavor Compatibility:** Hardware encoding on Intel QuickSync must output `-global_quality`, AMD must output `-qp_p`, NVIDIA must output `-rc:v vbr -cq:v`, avoiding crashes on non-NVENC cards.
3. **GIF and MP3 Isolation:** Selecting GIF or MP3 must never include video codec, crf, or target size flags in the output arguments.
4. **Queue Path Preservation:** Changing export format or suffix must never overwrite the `OutputPath` of already completed items (`item.IsCompleted == true`).
5. **Settings Migration:** Old configuration files with `LastPresetName = "Баланс H.264 (Рекомендуется)"` must safely map to the new `balance` profile without null reference exceptions.

---

### Task 1: Setup Test Project and Argument Generation Tests

**Files:**
- Create: `LowSizeMp4.Tests/LowSizeMp4.Tests.csproj`
- Create: `LowSizeMp4.Tests/FfmpegArgumentsTests.cs`

**Interfaces:**
- Consumes: `LowSizeMp4.CompressionProfile`, `LowSizeMp4.FfmpegService`
- Produces: Test harness executing `dotnet test`

- [ ] **Step 1: Create test project and reference main project**
  Run:
  ```powershell
  dotnet new xunit -o LowSizeMp4.Tests
  dotnet add LowSizeMp4.Tests/LowSizeMp4.Tests.csproj reference LowSizeMp4/LowSizeMp4.csproj
  ```

- [ ] **Step 2: Write failing unit tests for new `BuildCompressionArguments` signature**
  Create `LowSizeMp4.Tests/FfmpegArgumentsTests.cs` with tests asserting:
  - H.264 + Balance profile produces `-c:v libx264 -crf 23 -preset medium`
  - H.265 + Max Compression profile produces `-c:v libx265 -crf 30 -preset slow -tag:v hvc1`
  - AV1 + High Quality profile produces `-c:v libsvtav1 -crf 24 -preset 5`
  - MP3 format produces `-vn -c:a libmp3lame -b:a 192k`
  - GIF format produces palettegen/paletteuse and `-loop 0`

- [ ] **Step 3: Run tests to verify compilation failure**
  Run: `dotnet test LowSizeMp4.Tests`
  Expected: FAIL (compilation errors due to old `CompressionProfile` signature and parameters).

- [ ] **Step 4: Commit test harness scaffolding**
  ```powershell
  git add LowSizeMp4.Tests
  git commit -m "test: add xUnit test project and argument generation test cases"
  ```

---

### Task 2: Refactor `CompressionProfile` and `SettingsService`

**Files:**
- Modify: `LowSizeMp4/CompressionProfile.cs`
- Modify: `LowSizeMp4/SettingsService.cs`

**Interfaces:**
- Consumes: None
- Produces:
  ```csharp
  public sealed record CompressionProfile(
      string Id, string Name, string Badge, string Description,
      int H264Crf, int HevcCrf, int Av1Crf,
      string CpuPreset, string Av1CpuPreset, int GpuQualityLevel);
  ```
  And `UserSettings.AudioBitrate` property.

- [ ] **Step 1: Update `CompressionProfile.cs`**
  Replace constructor parameters with `Id`, `Name`, `Badge`, `Description`, `H264Crf`, `HevcCrf`, `Av1Crf`, `CpuPreset`, `Av1CpuPreset`, `GpuQualityLevel`.

- [ ] **Step 2: Update `UserSettings` in `SettingsService.cs`**
  Add `public string AudioBitrate { get; set; } = "192 kbps";`.
  Add backward-compatible preset resolver helper method `ResolveProfile(string? savedNameOrId, IEnumerable<CompressionProfile> profiles)` that maps legacy names like `"Баланс H.264 (Рекомендуется)"` to `"balance"`.

- [ ] **Step 3: Verify build of core models**
  Run: `dotnet build LowSizeMp4/LowSizeMp4.csproj` (note: MainViewModel will have errors until Task 4, which is expected).

- [ ] **Step 4: Commit**
  ```powershell
  git add LowSizeMp4/CompressionProfile.cs LowSizeMp4/SettingsService.cs
  git commit -m "refactor: update CompressionProfile record and UserSettings schema"
  ```

---

### Task 3: Refactor `FfmpegService.BuildCompressionArguments` & Add Resolution Probe

**Files:**
- Modify: `LowSizeMp4/FfmpegService.cs`
- Test: `LowSizeMp4.Tests/FfmpegArgumentsTests.cs`

**Interfaces:**
- Consumes: `CompressionProfile`, `GpuEncoderInfo`
- Produces:
  - `BuildCompressionArguments(CompressionProfile profile, bool isTargetSizeMode, int targetSizeMb, double? durationSeconds, string resolution, string fps, bool useInterpolation, bool isMuteAudio, bool useGpu, GpuEncoderInfo gpuInfo, string videoCodec = "H.264 (AVC)", string exportFormat = "MP4 Видео", string audioBitrate = "192 kbps")`
  - `Task<string?> TryGetVideoResolutionAsync(string inputPath, CancellationToken cancellationToken)`

- [ ] **Step 1: Implement `BuildCompressionArguments`**
  - Implement MP3 branch: `-vn -c:a libmp3lame -b:a {bitrate}k`.
  - Implement GIF branch: compute scale & fps filters, append `split[s0][s1];[s0]palettegen=max_colors=128[p];[s1][p]paletteuse=dither=bayer -loop 0`.
  - Implement MP4 branch:
    - Target size mode: calculate bitrate, set `-c:v`, `-b:v`, `-maxrate`, `-bufsize`.
    - Quality mode:
      - AV1: if GPU -> vendor flags; if CPU -> `-c:v libsvtav1 -crf {profile.Av1Crf} -preset {profile.Av1CpuPreset}`.
      - HEVC: if GPU -> vendor flags + `-tag:v hvc1`; if CPU -> `-c:v libx265 -crf {profile.HevcCrf} -preset {profile.CpuPreset} -tag:v hvc1`.
      - H.264: if GPU -> vendor flags; if CPU -> `-c:v libx264 -crf {profile.H264Crf} -preset {profile.CpuPreset}`.
    - Audio: `if (isMuteAudio) -an else -c:a aac -b:a 128k`.
    - Standard flags: `-map 0:v:0 -map 0:a? -pix_fmt yuv420p -movflags +faststart -sn`.

- [ ] **Step 2: Implement `TryGetVideoResolutionAsync` in `FfmpegService`**
  Use `ffprobe` with `-v error -select_streams v:0 -show_entries stream=width,height -of csv=s=x:p=0` to return strings like `"1920x1080"`.

- [ ] **Step 3: Run tests in `LowSizeMp4.Tests`**
  Run: `dotnet test LowSizeMp4.Tests`
  Expected: PASS.

- [ ] **Step 4: Commit**
  ```powershell
  git add LowSizeMp4/FfmpegService.cs LowSizeMp4.Tests/FfmpegArgumentsTests.cs
  git commit -m "feat: rewrite BuildCompressionArguments for dynamic codecs and add resolution probing"
  ```

---

### Task 4: Update `MainViewModel` with Dynamic Properties and Bug Fixes

**Files:**
- Modify: `LowSizeMp4/MainViewModel.cs`

**Interfaces:**
- Consumes: `CompressionProfile`, `FfmpegService`, `SettingsService`
- Produces:
  - New profile list in `Profiles`
  - Properties `IsVideoExport`, `IsGifExport`, `IsAudioExport`, `AudioBitrates`, `SelectedAudioBitrate`
  - Throttled metadata extraction with `SemaphoreSlim(3)`
  - Preserved `OutputPath` in `UpdateAllOutputPaths`

- [ ] **Step 1: Replace `Profiles` with the 4 universal strategies**
  Define `balance`, `max_compression`, `high_quality`, and `fast` profiles with exact CRF values and descriptions from the spec.

- [ ] **Step 2: Add export format properties and audio bitrate support**
  Add:
  - `bool IsVideoExport => SelectedExportFormat == "MP4 Видео"`
  - `bool IsGifExport => SelectedExportFormat == "GIF Анимация"`
  - `bool IsAudioExport => SelectedExportFormat == "MP3 Аудио"`
  - `ObservableCollection<string> AudioBitrates` with `"128 kbps"`, `"192 kbps"`, `"320 kbps"`
  - `[ObservableProperty] private string _selectedAudioBitrate = "192 kbps";`
  - Update `OnSelectedExportFormatChanged` to notify `IsVideoExport`, `IsGifExport`, `IsAudioExport`.

- [ ] **Step 3: Fix `UpdateAllOutputPaths` bug**
  Add guard: `if (item.IsCompleted) continue;` so finished jobs never have their paths altered.

- [ ] **Step 4: Add `SemaphoreSlim(3)` and real resolution probe in `AddFiles`**
  In `AddFiles`, throttle background worker tasks using `SemaphoreSlim(3)`. Call `TryGetVideoResolutionAsync` and set `item.ResolutionFormatted` to the probed resolution (e.g., `"1920x1080"`), falling back to extension only if probe fails.

- [ ] **Step 5: Pass new arguments in `StartCompressionAsync` and `GenerateQuickSampleAsync`**
  Pass `SelectedVideoCodec`, `SelectedExportFormat`, and `SelectedAudioBitrate` to `BuildCompressionArguments`.

- [ ] **Step 6: Run build and tests**
  Run: `dotnet test LowSizeMp4.Tests`
  Run: `dotnet build LowSizeMp4/LowSizeMp4.csproj`
  Expected: PASS.

- [ ] **Step 7: Commit**
  ```powershell
  git add LowSizeMp4/MainViewModel.cs
  git commit -m "refactor: update MainViewModel profiles, dynamic export states, and queue path protection"
  ```

---

### Task 5: Update `MainWindow.xaml` with Dynamic Visibility Panels

**Files:**
- Modify: `LowSizeMp4/MainWindow.xaml`

**Interfaces:**
- Consumes: `MainViewModel.IsVideoExport`, `MainViewModel.IsGifExport`, `MainViewModel.IsAudioExport`, `MainViewModel.AudioBitrates`, `MainViewModel.SelectedAudioBitrate`
- Produces: Responsive UI reacting to export format dropdown

- [ ] **Step 1: Fix navigation radio buttons converter**
  Replace `Converter={x:Static local:Converters.PresetSelectedConverter}` in sidebar radio buttons with `Converter={x:Static local:Converters.IntEqualsConverter}`.

- [ ] **Step 2: Wrap MP4-specific controls in video export container**
  Wrap quality mode switcher, profile listbox, target size buttons, codec selector, and audio mute checkbox in:
  `Visibility="{Binding IsVideoExport, Converter={x:Static local:Converters.BoolToVisibleConverter}}"`

- [ ] **Step 3: Add GIF animation configuration card**
  When `IsGifExport` is true, show an info badge:
  `«Конвертация видео в зацикленную анимацию с оптимизированной палитрой»`, keeping only Resolution, FPS, and Interpolation.

- [ ] **Step 4: Add MP3 audio configuration card**
  When `IsAudioExport` is true, show an info badge:
  `«Извлечение звуковой дорожки из видеофайла»` and ComboBox for `AudioBitrates` bound to `SelectedAudioBitrate`.

- [ ] **Step 5: Verify XAML compile and rendering**
  Run: `dotnet build LowSizeMp4/LowSizeMp4.csproj`
  Expected: Build succeeds with 0 errors.

- [ ] **Step 6: Commit**
  ```powershell
  git add LowSizeMp4/MainWindow.xaml
  git commit -m "feat: adapt MainWindow UI dynamically for video, gif and audio export modes"
  ```

---

### Task 6: Full Verification and Smoke Testing

**Files:**
- Modify: `LowSizeMp4.Tests/FfmpegArgumentsTests.cs` (additional test permutations)

- [ ] **Step 1: Run complete test suite**
  Run: `dotnet test LowSizeMp4.Tests -v normal`
  Expected: All tests PASS.

- [ ] **Step 2: Test settings load/save round-trip**
  Add unit test verifying `UserSettings` serializes and deserializes `AudioBitrate` and `LastPresetName` correctly.

- [ ] **Step 3: Build Release binary**
  Run: `dotnet build LowSizeMp4/LowSizeMp4.csproj -c Release`
  Expected: Build succeeds with 0 errors.

- [ ] **Step 4: Commit final verification**
  ```powershell
  git add LowSizeMp4.Tests
  git commit -m "test: verify all codec permutations and settings persistence"
  ```
