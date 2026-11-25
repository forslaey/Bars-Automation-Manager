# Подпись Менеджера автоматизации Барс

Текущая подпись сборки — «Шаклеин Павел Эдуардович». Открытая часть находится в `signing/Bars-Automation-Manager-CodeSigning.cer`. Отпечаток — `5E05A9087A2BA3FF1186A72825CCA1B40B73B7EB`.

Сертификат самоподписанный. Целостность файла и метка времени проверяются отдельно от доверия Windows. Автообновление сверяет отпечаток и доверие к издателю в Windows.

Сборка с подписью:

```powershell
.\tools\Build-OneFileRust.ps1 -OutputDirectory C:\ServiceManager3000\releases -Sign -EmbedWorkstationSettings
```

Проверка готового файла:

```powershell
.\tools\Test-FileSignature.ps1 -FilePath C:\ServiceManager3000\releases\Bars.Automation.Manager.1.11.2.exe -CertificateThumbprint 5E05A9087A2BA3FF1186A72825CCA1B40B73B7EB
```

Настройка подписи хранится в `%LOCALAPPDATA%\BARS\Менеджер автоматизации Барс\signing.json`. При следующей обычной сборке используется существующий сертификат. Поворот подписи выполняется отдельно по решению владельца через `New-SigningCertificate.ps1 -Rotate`.

Перед сборкой задай BARS_RELEASE_PASSWORD и BARS_REMOTE_STANDARD_PASSWORD в окружении процесса сборки. Итог передачи — один подписанный EXE со встроенными настройками. В итоговом сообщении укажи папку C:\ServiceManager3000\releases. Владелец загружает EXE на GitHub вручную после проверки.
