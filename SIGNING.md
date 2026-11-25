# Подпись Менеджера автоматизации Барс

Текущая подпись сборки — «Шаклеин Павел Эдуардович». Открытая часть находится в `signing/Bars-Automation-Manager-CodeSigning.cer`. Отпечаток — `5E05A9087A2BA3FF1186A72825CCA1B40B73B7EB`.

Сертификат самоподписанный. Целостность файла и метка времени проверяются отдельно от доверия Windows. Автообновление сверяет отпечаток и доверие к издателю в Windows.

Сборка с подписью:

```powershell
.\tools\Build-OneFileRust.ps1 -OutputDirectory .\releases -Sign
```

Проверка готового файла:

```powershell
.\tools\Test-FileSignature.ps1 -FilePath .\releases\Bars.Automation.Manager.1.11.2.exe -CertificateThumbprint 5E05A9087A2BA3FF1186A72825CCA1B40B73B7EB
```

Настройка подписи хранится в `%LOCALAPPDATA%\BARS\Менеджер автоматизации Барс\signing.json`. При следующей обычной сборке используется существующий сертификат. Поворот подписи выполняется отдельно по решению владельца через `New-SigningCertificate.ps1 -Rotate`.