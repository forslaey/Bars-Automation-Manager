[CmdletBinding()]
param([string] $PublicDirectory, [switch] $Rotate)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($PublicDirectory)) { $PublicDirectory = Join-Path (Split-Path $PSScriptRoot -Parent) 'signing' }
$taskSettingsPath = Join-Path $env:LOCALAPPDATA 'BARS\Менеджер автоматизации Барс\signing.json'
if (-not $Rotate -and -not (Test-Path -LiteralPath $taskSettingsPath)) { $taskLegacySettings = Join-Path $env:LOCALAPPDATA 'BARS\Bars Service Manager 3000\signing.json'; if (Test-Path -LiteralPath $taskLegacySettings) { $taskSettingsPath = $taskLegacySettings } }
$taskSettings = $taskSettingsPath
if (-not $Rotate -and (Test-Path -LiteralPath $taskSettings)) {
    $taskSaved = Get-Content -LiteralPath $taskSettings -Raw | ConvertFrom-Json
    $taskExisting = Get-Item -LiteralPath ('Cert:\CurrentUser\My\' + $taskSaved.Thumbprint) -ErrorAction SilentlyContinue
    if ($taskExisting -and $taskExisting.HasPrivateKey -and $taskExisting.NotAfter -gt (Get-Date)) {
        Write-Output ('Сертификат уже существует: ' + $taskExisting.Thumbprint); return
    }
    throw 'Сохранённый сертификат недоступен или истёк. Сначала восстанови его из резервной копии; новый автоматически не создаётся.'
}
$taskCertificate = New-SelfSignedCertificate -Type CodeSigningCert `
    -Subject 'CN=Менеджер автоматизации Барс (Павел Шаклеин), O=ДатаКрат - ОТЭиВ, E=junior@aniello.ru' `
    -FriendlyName 'Менеджер автоматизации Барс — подпись Павла Шаклеина' `
    -CertStoreLocation 'Cert:\CurrentUser\My' -KeyAlgorithm RSA -KeyLength 3072 `
    -HashAlgorithm SHA256 -KeyUsage DigitalSignature -KeyExportPolicy Exportable `
    -Provider 'Microsoft Software Key Storage Provider' -NotAfter (Get-Date).AddYears(3)
New-Item -ItemType Directory -Path $PublicDirectory -Force | Out-Null
$taskPublic = Join-Path ([IO.Path]::GetFullPath($PublicDirectory)) 'Bars-Automation-Manager-CodeSigning.cer'
Export-Certificate -Cert $taskCertificate -FilePath $taskPublic -Type CERT | Out-Null
New-Item -ItemType Directory -Path (Split-Path $taskSettings -Parent) -Force | Out-Null
@{ Thumbprint = $taskCertificate.Thumbprint; PublicCertificate = $taskPublic } |
    ConvertTo-Json | Set-Content -LiteralPath $taskSettings -Encoding UTF8
Write-Output ('Создан сертификат: ' + $taskCertificate.Subject)
Write-Output ('Отпечаток: ' + $taskCertificate.Thumbprint)
Write-Output ('Действует до: ' + $taskCertificate.NotAfter.ToString('yyyy-MM-dd'))
Write-Output ('Открытый сертификат: ' + $taskPublic)
Write-Output 'Закрытый ключ хранится в личном хранилище Windows текущего пользователя. Доверие не добавлялось.'
