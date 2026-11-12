# ActivityLibraryBase

Базовые классы активностей для построения библиотек активностей в Windows Workflow Foundation (`.NET Framework 4.7.2`).

## Что внутри

- `BaseCodeActivity`, `TimeoutCodeActivity` — базовые реализации `CodeActivity`/`NativeActivity` с логированием и поддержкой таймаутов.
- Локализованные атрибуты (`LocalizedDisplayName`, `LocalizedDescription`, `LocalizedCategory`) для панели активностей студии.
- Операции ожидания и опроса: `Waiter`, `WaiterThrowLastException`, `PollingOperation`, `TimingOperation` и вспомогательные типы времени (`Timing`).
- Хелперы дизайнера, конвертеры и расширения для описания активностей.

## Назначение

Пакет служит фундаментом для библиотек активностей RPA (например, `Activities.Browser`) и используется роботами на `net472`.
