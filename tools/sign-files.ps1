<#
.SYNOPSIS
  Authenticode-signs files with signtool, using a certificate kept in repository secrets.

.DESCRIPTION
  Reads the certificate from the environment: PFX_BASE64 (the .pfx file, base64) and PFX_PASSWORD. With no certificate configured the
  files are left unsigned and the script succeeds (so releases keep working until a certificate is bought). With -AllowSelfSigned and no
  certificate, a throw-away self-signed certificate is used instead, which exercises everything except trust; this is what the release
  workflow's dry run does.

  Sets SIGNED=true in the GitHub Actions environment (GITHUB_ENV) only when a real certificate signed the files.
#>
param(
    [Parameter(Mandatory = $true)][string[]]$Path,
    [switch]$AllowSelfSigned
)

$ErrorActionPreference = 'Stop'

$files = @($Path | Where-Object { $_ })
if ($files.Count -eq 0) {
    Write-Host "Nothing to sign."
    return
}

$pfxBase64 = $env:PFX_BASE64
$password = $env:PFX_PASSWORD
$pfxFile = Join-Path ([IO.Path]::GetTempPath()) ('dyncamelo-sign-' + [guid]::NewGuid().ToString('N') + '.pfx')
$selfSigned = $false

if ([string]::IsNullOrWhiteSpace($pfxBase64)) {
    if (-not $AllowSelfSigned) {
        Write-Host "No signing certificate configured (secret SIGNING_PFX_BASE64): $($files.Count) file(s) left unsigned."
        return
    }
    Write-Host "No signing certificate configured: signing with a throw-away self-signed certificate to exercise the steps."
    # PowerShell 7 reaches the certificate cmdlets through the Windows PowerShell compatibility layer when it must.
    Import-Module PKI -ErrorAction SilentlyContinue
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=Dyncamelo dry-run signing' -CertStoreLocation Cert:\CurrentUser\My
    $password = [guid]::NewGuid().ToString('N')
    Export-PfxCertificate -Cert $cert -FilePath $pfxFile -Password (ConvertTo-SecureString $password -AsPlainText -Force) | Out-Null
    $selfSigned = $true
}
else {
    if ([string]::IsNullOrWhiteSpace($password)) {
        throw "SIGNING_PFX_BASE64 is set but SIGNING_PFX_PASSWORD is not."
    }
    [IO.File]::WriteAllBytes($pfxFile, [Convert]::FromBase64String($pfxBase64))
}

try {
    $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\' } |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    if (-not $signtool) {
        throw "signtool.exe was not found under the Windows 10 SDK on this machine."
    }

    foreach ($file in $files) {
        $signArgs = @('sign', '/fd', 'SHA256', '/f', $pfxFile, '/p', $password)
        if (-not $selfSigned) {
            $signArgs += @('/tr', 'http://timestamp.digicert.com', '/td', 'SHA256')
        }
        $signArgs += $file
        & $signtool.FullName @signArgs | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "signtool failed for $file (exit code $LASTEXITCODE)."
        }

        $signature = Get-AuthenticodeSignature $file
        if ($null -eq $signature.SignerCertificate) {
            throw "$file carries no signature after signtool ran."
        }
        if (-not $selfSigned -and $signature.Status -ne 'Valid') {
            throw "$file is signed but the signature is not valid: $($signature.Status) - $($signature.StatusMessage)"
        }
    }

    Write-Host "Signed $($files.Count) file(s)$(if ($selfSigned) { ' with a self-signed certificate (dry run)' })."
    if (-not $selfSigned -and $env:GITHUB_ENV) {
        Add-Content -Path $env:GITHUB_ENV -Value 'SIGNED=true'
    }
}
finally {
    Remove-Item $pfxFile -Force -ErrorAction SilentlyContinue
}
