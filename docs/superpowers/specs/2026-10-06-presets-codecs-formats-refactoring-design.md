# Дизайн-документ: Рефакторинг пресетов сжатия, видеокодеков и форматов вывода

**Дата:** 2026-10-06  
**Проект:** LowSizeMp4  
**Статус:** Согласован / Готов к реализации  

---

## 1. Контекст и мотивация

В текущей версии `LowSizeMp4` наблюдается конфликт между тремя сущностями интерфейса и движка:
1. **Профили сжатия (`CompressionProfile`)**: жестко зашивали в своё название и аргументы конкретные кодеки (`libx264`, `libx265`), например: *«Баланс H.264»*, *«Сильное сжатие H.265»*.
2. **Селектор видеокодека (`SelectedVideoCodec`)**: предлагал выбор между `H.264`, `H.265` и `AV1`, но при выборе конфликтующего профиля (например, профиль H.265 + кодек H.264) код `BuildCompressionArguments` принудительно кодировал в H.265, игнорируя выбор пользователя.
3. **Селектор формата вывода (`SelectedExportFormat`)**: при выборе GIF или MP3 профили сжатия, режим точного размера и видеопараметры оставались видимыми в интерфейсе, но полностью отбрасывались в коде.

### Цель рефакторинга:
- Полностью развязать **пресеты качества** и **видеокодеки**: пресет отвечает только за баланс качества и скорости (CRF / CQ / preset скорости), а кодек выбирается независимо.
- Сделать интерфейс **динамическим**: при переключении между MP4, GIF и MP3 отображать только те параметры, которые актуальны для выбранного типа задачи.
- Исправить сопутствующие дефекты: перезапись путей завершенных файлов, отображение расширения вместо реального разрешения видео, неоптимальные флаги GPU для Intel QSV и AMD AMF, спам процессами при пакетном добавлении файлов.

---

## 2. Модели данных и настройки

### 2.1. Новая структура `CompressionProfile`
Файл: `LowSizeMp4/CompressionProfile.cs`

```csharp
namespace LowSizeMp4;

public sealed record CompressionProfile(
    string Id,
    string Name,
    string Badge,
    string Description,
    int H264Crf,          // CRF для H.264 (libx264)
    int HevcCrf,          // CRF для H.265 (libx265)
    int Av1Crf,           // CRF для AV1 (libsvtav1)
    string CpuPreset,     // Пресет скорости CPU для x264/x265 (medium, slow, faster)
    string Av1CpuPreset,  // Пресет скорости CPU для SVT-AV1 (6, 5, 8)
    int GpuQualityLevel   // Нормализованный уровень качества для GPU (23, 28, 19, 26)
)
{
    public override string ToString() => Name;
}
```

### 2.2. Предустановленные профили
В `MainViewModel.Profiles` инициализируются 4 универсальные стратегии:

| Id | Название | Бейдж | H.264 CRF / Preset | HEVC CRF / Preset | AV1 CRF / Preset | GPU Q | Описание |
|---|---|---|---|---|---|---|---|
| `balance` | Оптимальный (Баланс) | Рекомендуется | 23 / `medium` | 26 / `medium` | 28 / `6` | 23 | Оптимальный баланс размера и качества для большинства повседневных видео. |
| `max_compression` | Максимальное сжатие | Мин. размер | 28 / `slow` | 30 / `slow` | 32 / `5` | 28 | Максимальная экономия дискового пространства за счёт чуть более долгого сжатия. |
| `high_quality` | Высокое качество | Без потерь | 19 / `slow` | 22 / `slow` | 24 / `5` | 19 | Минимум визуальных потерь для важных записей и архива. |
| `fast` | Быстрое сжатие | Быстро | 26 / `faster` | 28 / `fast` | 32 / `8` | 26 | Ускоренный рендеринг за счёт чуть большего веса файла. |

### 2.3. Сохранение настроек (`UserSettings` и `SettingsService`)
- Поле `LastPresetName` сохраняется для обратной совместимости, но поиск при загрузке сопоставляет как новое имя / `Id`, так и старые имена (например, «Баланс H.264» → сопоставляется с `balance`).
- Добавляется поле `AudioBitrate` (по умолчанию `"192 kbps"`).

---

## 3. Движок кодирования (`FfmpegService`)

### 3.1. Сигнатура `BuildCompressionArguments`
```csharp
public string BuildCompressionArguments(
    CompressionProfile profile,
    bool isTargetSizeMode,
    int targetSizeMb,
    double? durationSeconds,
    string resolution,
    string fps,
    bool useInterpolation,
    bool isMuteAudio,
    bool useGpu,
    GpuEncoderInfo gpuInfo,
    string videoCodec = "H.264 (AVC)",
    string exportFormat = "MP4 Видео",
    string audioBitrate = "192 kbps")
```

### 3.2. Логика по форматам:

1. **Экспорт в MP3 Аудио (`exportFormat == "MP3 Аудио"`):**
   - `-vn -c:a libmp3lame -b:a {parsedKbps}k` (по умолчанию 192k).

2. **Экспорт в GIF Анимацию (`exportFormat == "GIF Анимация"`):**
   - Вычисление фильтра масштабирования по `resolution` (1080p, 720p, 480p или ограничение высоты для оригиналов).
   - Вычисление фильтра кадров `fps=X` (или `minterpolate` при `useInterpolation`).
   - Палитра: `-vf "{filters},split[s0][s1];[s0]palettegen=max_colors=128[p];[s1][p]paletteuse=dither=bayer" -loop 0`.

3. **Экспорт в MP4 Видео (`exportFormat == "MP4 Видео"`):**
   - **Фильтры:** масштабирование (`scale=-2:min(X\,ih)`) + частота кадров / интерполяция.
   - **Режим «Точный размер»:**
     - Расчет битрейта: `videoBitrateKbps = Math.Max(40, (int)(targetBits / (duration * 1000.0)) - audioBitrateKbps)`.
     - Видеокодек выбирается по `SelectedVideoCodec` (H.264, H.265 или AV1) с учетом GPU.
     - Аргументы: `-c:v {codec} -b:v {videoBitrateKbps}k -maxrate {videoBitrateKbps * 1.4:0}k -bufsize {videoBitrateKbps * 2:0}k {tagStr}`.
   - **Режим «По качеству»:**
     - **AV1:**
       - GPU: `av1_nvenc -rc:v vbr -cq:v {Q} -preset p5` / `av1_qsv -global_quality {Q}` / `av1_amf -rc cqp -qp_p {Q}`.
       - CPU: `-c:v libsvtav1 -crf {profile.Av1Crf} -preset {profile.Av1CpuPreset}`.
     - **H.265:**
       - GPU: `hevc_nvenc -rc:v vbr -cq:v {Q} -preset p5 -tag:v hvc1` / `hevc_qsv -global_quality {Q} -tag:v hvc1` / `hevc_amf -rc cqp -qp_p {Q} -tag:v hvc1`.
       - CPU: `-c:v libx265 -crf {profile.HevcCrf} -preset {profile.CpuPreset} -tag:v hvc1`.
     - **H.264:**
       - GPU: `h264_nvenc -rc:v vbr -cq:v {Q} -preset p5` / `h264_qsv -global_quality {Q}` / `h264_amf -rc cqp -qp_p {Q}`.
       - CPU: `-c:v libx264 -crf {profile.H264Crf} -preset {profile.CpuPreset}`.
   - **Аудио:**
     - `isMuteAudio` → `-an`
     - иначе → `-c:a aac -b:a 128k`
   - **Контейнер:**
     - `-map 0:v:0 -map 0:a? -pix_fmt yuv420p -movflags +faststart -sn`.

---

## 4. Пользовательский интерфейс (`MainWindow.xaml` & `MainViewModel`)

### 4.1. Свойства видимости в `MainViewModel`
- `IsVideoExport` => `SelectedExportFormat == "MP4 Видео"`
- `IsGifExport` => `SelectedExportFormat == "GIF Анимация"`
- `IsAudioExport` => `SelectedExportFormat == "MP3 Аудио"`
- `AudioBitrates` => `["128 kbps", "192 kbps", "320 kbps"]`
- `SelectedAudioBitrate` => `"192 kbps"` (с автосохранением в настройках)

### 4.2. Динамическое скрытие панелей в XAML
- Блок пресетов, переключатель режима качества, селектор видеокодека и настройки звука обёрнуты в контейнеры с `Visibility="{Binding IsVideoExport, Converter={x:Static local:Converters.BoolToVisibleConverter}}"`.
- При `IsGifExport` выводится карточка с подсказкой про GIF и релевантными полями (разрешение, FPS, интерполяция).
- При `IsAudioExport` выводится карточка с подсказкой про извлечение MP3 и выпадающим списком `AudioBitrates`.
- Боковая навигация: использование `IntEqualsConverter` вместо `PresetSelectedConverter`.

---

## 5. Сопутствующие исправления багов

1. **Защита готовых элементов в очереди:**
   В `MainViewModel.UpdateAllOutputPaths()` пропускаются элементы, у которых `item.IsCompleted == true`.
2. **Получение реального разрешения видео:**
   В `FfmpegService` добавляется метод `TryGetVideoResolutionAsync` (через `ffprobe -v error -select_streams v:0 -show_entries stream=width,height -of csv=s=x:p=0`), который заполняет `item.ResolutionFormatted` реальными значениями (например, `"1920x1080"`).
3. **Ограничение нагрузки при добавлении файлов:**
   В `AddFiles` использование `SemaphoreSlim(3)` для фонового парсинга превью и метаданных.

---

## 6. План тестирования и валидации

1. **Компиляция:** `dotnet build` на конфигурации `Release`/`Debug` без ошибок и предупреждений.
2. **Проверка генерации аргументов:**
   - H.264 + Баланс (CPU / GPU)
   - H.265 + Максимальное сжатие (CPU / GPU)
   - AV1 + Высокое качество (CPU / GPU)
   - Точный размер (10 МБ, 25 МБ)
   - GIF экспорт (с фильтром scale и палитрой)
   - MP3 экспорт (с битрейтом 192k / 320k)
3. **Проверка персистентности:**
   - Перезапуск приложения с сохранением новых профилей, выбранного кодека и битрейта MP3.
4. **Smoke-тест UI:**
   - Переключение формата в комбобоксе: блоки динамически скрываются/показываются.
   - Добавление видеофайлов в очередь: разрешение и длительность определяются корректно, пути готовых файлов не сбрасываются.
