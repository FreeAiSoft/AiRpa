# План: голосовой ввод — микрофон + Vosk offline

> Статус: `planning`
> Модуль: `RPA.Chat` (хост студии `AiRpaStudio`)
> Связанные проекты: `AgentShared` (контракты `IMicrophoneRecorder`, `ISpeechToTextService`)

## Цель

Подключить в студии рабочий голосовой ввод:

1. Реализовать запись с микрофона (выбор устройства).
2. Реализовать локальное распознавание речи **Vosk offline** (без облака).
3. Добавить в настройки выбор микрофона и live-индикатор уровня входного шума для диагностики.
4. Подключить всё к существующей кнопке микрофона в чате (`ChatControl.AttachServices`).

## Исходное состояние

- Кнопка микрофона в `ChatControl` **уже есть** и реагирует на `LevelChanged`
  (меняет прозрачность кнопки).
- В `RPA.Chat\Views\ChatHostView.xaml.cs` голосовой ввод **не подключён** — стоит
  комментарий, что `IMicrophoneRecorder` / `ISpeechToTextService` поставляет «внешний проект».
- Реализаций рекордера (NAudio) и Vosk в `AI-RPA` и `AgentShared` нет.
- В настройках (`SettingsControl`) уже есть вкладка **Sound**: провайдер STT
  (Auto / OpenAI Whisper / Vosk), модель Whisper, язык, путь к Vosk-модели, авто-отправка.
- Выбора устройства микрофона и индикатора уровня шума **нет**.

## Что скачать и куда

### 1. Модель Vosk (обязательно, ~45 МБ)

- URL: `https://alphacephei.com/vosk/models/vosk-model-small-ru-0.22.zip`
- Распаковать в папку и указать её в настройках чата → **Sound → Vosk model directory**.
- Рекомендуемый путь:

```
C:\Users\Maxxx\AppData\Roaming\AiRpaStudio\Chat\vosk-models\vosk-model-small-ru-0.22
```

- Внутри должна лежать папка модели с `am/`, `conf/`, `graph/`, `ivector/` и т.д.

### 2. Библиотеки (качаются через NuGet автоматически)

- `NAudio` (запись с микрофона, ресемплинг) — стабильная `2.2.1`.
- `Vosk` (движок распознавания, включает нативную `libvosk.dll`) — `0.3.38`.

Добавляются как `PackageReference` в `RPA.Chat.csproj`; `libvosk.dll` (win-x64)
копируется в папку вывода по образцу уже существующего таргета
`CopyWebView2LoaderToOutput`.

## Шаги реализации

### Шаг 1 — пакеты (`RPA.Chat\RPA.Chat.csproj`)

- Добавить `PackageReference` на `NAudio` и `Vosk`.
- Добавить MSBuild-таргет копирования `libvosk.dll` (win-x64) рядом с `RPA.Chat.dll`.

### Шаг 2 — запись с микрофона (`RPA.Chat\Services\`)

- `MicrophoneDeviceInfo.cs` — перечисление устройств
  (`WaveInEvent.DeviceCount` + `GetCapabilities(i).ProductName`).
- `NaudioMicrophoneRecorder.cs` — реализация `IMicrophoneRecorder`:
  - `Start()` / `Stop()`, выбор устройства по номеру;
  - `WaveInEvent` + `MediaFoundationResampler` → 16 кГц / 16 бит / mono;
  - накопление PCM и расчёт RMS-уровня → событие `LevelChanged` (0..1, троттлинг 30–50 мс).
- `MicrophoneLevel.cs` — чистая функция расчёта уровня из `short[]` (для юнит-тестов).

### Шаг 3 — распознавание Vosk (`RPA.Chat\Services\`)

- `VoskSpeechToTextService.cs` — реализация `ISpeechToTextService`:
  - загрузка `Vosk.Model` из `VoskModelPath`;
  - конвертация PCM `short[]` → `float[]`;
  - `Recognizer.AcceptWaveform(...)` → текст.
- `SpeechToTextServiceFactory.cs` — по `SettingsData.SpeechToTextProvider`
  создаёт `VoskSpeechToTextService` (для `Vosk` / `Auto`); `OpenAiWhisper` — существующий облачный.

### Шаг 4 — сохранение выбора микрофона (`RPA.Chat\Services\MicrophoneSettings.cs`)

- По образцу `RpaAgentOptions`: файл
  `%AppData%\AiRpaStudio\Chat\microphone_settings.json`.
- Поле `DeviceNumber` (`-1` = устройство по умолчанию), методы `Load` / `Save` / `Reset`.

### Шаг 5 — настройки: host-секция «Микрофон» (`RPA.Chat\Views\MicrophoneSettingsView.xaml(.cs)`)

- `ComboBox` устройств (пункт «Устройство по умолчанию» + список из NAudio).
- `ProgressBar` уровня входного шума (0–100 %) — live-мониторинг выбранного устройства.
- Кнопка «Обновить список устройств».
- Вставить через `_mainTabs.SettingsPanel.InsertHostSection(...)` в `ChatHostView`.

### Шаг 6 — подключение в `ChatHostView`

- Создать рекордер + `VoskSpeechToTextService` и вызвать
  `_chatControl.AttachServices(recorder, stt)`.
- Подписаться на `_chatControl.VoiceError` → показывать ошибку в слот `VoiceErrorToastHost`
  (сейчас обработчика нет).

### Шаг 7 — тесты (TDD, `AiRpaStudio.Tests`)

- `MicrophoneLevelTests` — расчёт RMS из тестовых `short[]`.
- `MicrophoneSettingsTests` — round-trip сохранения/загрузки (через `ChatPaths.OverrideDataDirectory`).
- `SpeechToTextServiceFactoryTests` — выбор сервиса по провайдеру.
- `PcmToFloatConverterTests` — конвертация PCM → `float[]` для Vosk.

### Шаг 8 — сборка и проверка

- `MSBuild AiRpa.sln /p:Configuration=Debug` → 0 ошибок.
- Тесты: `vstest.console.exe AiRpaStudio.Tests\bin\Debug\net48\AiRpaStudio.Tests.dll`.
- Ручной прогон в студии:
  1. указать путь модели Vosk в настройках;
  2. выбрать микрофон;
  3. увидеть движение прогресса уровня шума;
  4. нажать кнопку микрофона и сказать фразу → текст появился в поле ввода.

## Итог «проверь, что всё работает»

Сейчас кнопка микрофона есть, но **не подключена**: `AttachServices` никто не вызывает,
реализации рекордера и Vosk отсутствуют. После выполнения шагов 1–8 появится рабочий
локальный голосовой ввод + выбор микрофона + диагностика уровня шума.
