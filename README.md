# LowSizeMp4

<div align="center">

<img src="LowSizeMp4/Assets/AppIcon.ico" width="80" height="80" alt="LowSizeMp4 Icon" />

### Простой и быстрый компрессор видео для Windows

Десктопная оболочка для FFmpeg с интерфейсом в стиле Windows 11. Помогает быстро ужать видео под лимиты Discord, Telegram или почты без ручного подбора битрейтов в консоли.

[![GitHub release](https://img.shields.io/badge/release-v2.5-blue.svg)](https://github.com/RilleSB/LowSizeMP4/releases)
[![.NET](https://img.shields.io/badge/.NET-9.0_Windows-512BD4.svg?logo=dotnet)](https://dotnet.microsoft.com/)
[![UI](https://img.shields.io/badge/UI-WPF--UI_4.3-0078D4.svg)](https://github.com/lepoco/wpfui)
[![FFmpeg](https://img.shields.io/badge/Powered_by-FFmpeg-007808.svg?logo=ffmpeg)](https://ffmpeg.org/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

[**Скачать LowSizeMp4.exe (v2.5)**](https://github.com/RilleSB/LowSizeMP4/releases/latest) • [Все релизы](https://github.com/RilleSB/LowSizeMP4/releases)

</div>

---

## Что умеет

### Режимы сжатия
* **Под целевой размер (Fit to MB):** выбираешь нужный лимит (10 МБ для бесплатного Discord, 25 МБ для почты, 50 МБ для Telegram или любой свой объём) — приложение само рассчитывает видеобитрейт с учётом длительности файла, звука и оверхеда контейнера.
* **По качеству:** 4 универсальных профиля (Баланс, Максимальное сжатие, Высокое качество, Быстро). Профили не привязаны жестко к одному кодеку: параметры квантования (CRF/CQ) и скорость автоматически подстраиваются под выбранный формат и кодировщик.

### Форматы и кодеки
* **MP4 Видео:**
  * **H.264 (AVC)** — универсальный вариант, читается практически везде.
  * **H.265 (HEVC)** — эффективное сжатие с тегом `-tag:v hvc1`, чтобы ролики нативно открывались на устройствах Apple и в стандартном плеере Windows.
  * **AV1** — максимальная степень сжатия (программный `libsvtav1` или аппаратные блоки GPU).
* **GIF Анимация:** двухпроходная генерация палитры (`palettegen` + `paletteuse`) с зацикливанием (`-loop 0`), без артефактов и цветового шума.
* **MP3 Аудио:** быстрое извлечение звуковой дорожки с настраиваемым битрейтом от 96 до 320 kbps.

### Аппаратное ускорение (GPU)
Автоматически определяет доступные в системе энкодеры и задействует видеокарту при кодировании:
* **NVIDIA** — NVENC (`h264_nvenc`, `hevc_nvenc`, `av1_nvenc`)
* **Intel** — QuickSync (`h264_qsv`, `hevc_qsv`, `av1_qsv`)
* **AMD** — AMF (`h264_amf`, `hevc_amf`, `av1_amf`)

### Удобство в повседневной работе
* **Тест за 5 секунд:** кнопка предпросмотра вырезает 5-секундный отрезок из середины ролика, сжимает с текущими настройками и открывает в плеере. Можно сразу оценить картинку, не дожидаясь рендера всего видео.
* **Встроенная обрезка:** установка таймкодов начала и конца ролика прямо в карточке файла (при сжатии в точный размер лимит применяется именно к вырезанному фрагменту).
* **Индикация в таскбаре:** прогресс кодирования отображается на значке приложения в панели задач Windows, в окне считаются FPS, скорость и примерное время до конца (ETA).
* **Очередь файлов и Drag & Drop:** перетаскивание файлов пачкой, поддержка аргументов командной строки («Открыть с помощью...»).
* **Автоматизация:** звуковые уведомления, авто-открытие папки с результатом и возможность перевести ПК в спящий режим или выключить после завершения всей очереди.
* **Загрузка FFmpeg:** если на компьютере не установлен FFmpeg, его можно скачать в один клик прямо из вкладки настроек.
* **Внешний вид:** поддержка Mica и Acrylic (Windows 11), светлая/тёмная темы и свободная настройка акцентного цвета (включая ввод произвольного HEX-кода).

---

## Быстрый старт

### Готовая сборка
Просто скачай [**LowSizeMp4.exe**](https://github.com/RilleSB/LowSizeMP4/releases/latest) или архив `LowSizeMp4-v2.5-win-x64.zip`.  
Файл собран в режиме *Self-Contained* — устанавливать .NET или сторонние библиотеки не нужно, работает на Windows 10 (1809+) и Windows 11 x64 из коробки.

### Сборка из исходников
Требуется [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0).

```bash
# Клонирование репозитория
git clone https://github.com/RilleSB/LowSizeMP4.git
cd LowSizeMP4/LowSizeMp4

# Запуск
dotnet run

# Сборка автономного исполняемого файла
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ../publish
```

---

## Стек технологий

* **Платформа:** C# 13, .NET 9 (WPF)
* **Интерфейс:** [WPF-UI 4.3](https://github.com/lepoco/wpfui) (Windows 11 Fluent Design)
* **Архитектура:** [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet)
* **Медиа-движок:** [FFmpeg](https://ffmpeg.org/) & [FFprobe](https://ffmpeg.org/ffprobe.html)
* **Тесты:** xUnit (.NET 9)

---

## Лицензия

[MIT](LICENSE)
