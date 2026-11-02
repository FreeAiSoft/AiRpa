Отчёты складываются в `tmp\ui-snapshot\` и `tmp\ui-tree\` (папка `tmp` в `.gitignore`).
===NEW===
Отчёты складываются в `tmp\ui-snapshot\` и `tmp\ui-tree\` (папка `tmp` в `.gitignore`), отчёт пробника —
в `tools\designer-ui-diagnostics\probe\bin\probe-report.txt` (`probe\bin` в `.gitignore`).

Уже снятый снимок можно разобрать повторно — без запущенной студии:

```powershell
powershell -ExecutionPolicy Bypass -File tools\designer-ui-diagnostics\Get-UiSnapshot.ps1 -ImagePath tmp\shot.png
```
