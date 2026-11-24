# Подпись Менеджера автоматизации Барс

Сборка 1.11.0 подписана сертификатом «Менеджер автоматизации Барс (Павел Шаклеин)». Открытая часть находится в `signing/Bars-Automation-Manager-CodeSigning.cer`. Отпечаток — `6953D55E71360D565B095F1BF0B1442B4A211A93`.

Сертификат самоподписанный. Целостность файла и метка времени проверяются отдельно от доверия Windows. Автообновление сверяет отпечаток и доверие к издателю в Windows.

Сборка с подписью:

```powershell
.\tools\Build-OneFileRust.ps1 -OutputDirectory .\bin -Sign
```

Проверка готового файла:

```powershell
.\tools\Test-FileSignature.ps1 -FilePath .\bin\Bars.Automation.Manager.1.11.0.exe -CertificateThumbprint 6953D55E71360D565B095F1BF0B1442B4A211A93
```

Настройка подписи хранится в `%LOCALAPPDATA%\BARS\Менеджер автоматизации Барс\signing.json`. При следующей обычной сборке используется существующий сертификат. Поворот подписи выполняется отдельно по решению владельца через `New-SigningCertificate.ps1 -Rotate`.