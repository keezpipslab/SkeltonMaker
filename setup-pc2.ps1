<#
Run once on the OTHER PC (the second performer's Link PC), in PowerShell as administrator, so
deploy.ps1 on the first PC can reach it and the two SkeletonMakers can see each other:

    powershell -ExecutionPolicy Bypass -File setup-pc2.ps1 -Key "ssh-ed25519 AAAA... keez"

The key is the first PC's public key (type ~/.ssh/id_ed25519.pub there). It turns on the OpenSSH
server, lets that key in, lets SkeletonMaker's ports through the firewall and clones the project.

On the first PC, only the firewall part is needed (it receives the other body too):

    powershell -ExecutionPolicy Bypass -File setup-pc2.ps1 -FirewallOnly

Unity 6000.3.22f1 (with Android support not needed) and git have to be installed on the other PC.
#>
param(
    [string]$Key,
    [string]$Path = "C:\Unity\SkeletonMaker",
    [string]$Branch = "main",
    [int]$OscPort = 9000,
    [int]$XsensPort = 9763,
    [switch]$FirewallOnly,
    # Windows adds a Block rule for each new Unity editor the first time it is turned down on a
    # Public network, and a Block rule wins over any Allow rule: this switches those off.
    [switch]$UnblockUnity
)
$ErrorActionPreference = "Stop"

# SkeletonMaker: OSC between the two PCs, and the Xsens suit's stream from MVN
foreach ($rule in @(@{Name = "SkeletonMaker OSC (UDP $OscPort)"; Port = $OscPort }, @{Name = "SkeletonMaker Xsens (UDP $XsensPort)"; Port = $XsensPort })) {
    if (-not (Get-NetFirewallRule -DisplayName $rule.Name -ErrorAction SilentlyContinue)) {
        New-NetFirewallRule -DisplayName $rule.Name -Direction Inbound -Protocol UDP -LocalPort $rule.Port -Action Allow -Profile Private, Public | Out-Null
    }
}

$blocks = @(Get-NetFirewallRule -Direction Inbound -Action Block -Enabled True -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName -like "Unity*Editor*" })
if ($blocks.Count -gt 0) {
    if ($UnblockUnity) {
        $blocks | Disable-NetFirewallRule
        Write-Host "Switched off: $($blocks.DisplayName -join ', ')"
    } else {
        Write-Warning "These rules block the Unity editor and win over the allow rules, so nothing would arrive: $($blocks.DisplayName -join ', '). Run again with -UnblockUnity to switch them off."
    }
}

if ($FirewallOnly) { Write-Host "Firewall done."; return }
if (-not $Key) { throw "Give the first PC's public key with -Key (or use -FirewallOnly)." }

# OpenSSH server, started now and with Windows
$ssh = Get-WindowsCapability -Online -Name OpenSSH.Server*
if ($ssh.State -ne "Installed") { Add-WindowsCapability -Online -Name $ssh.Name | Out-Null }
Set-Service sshd -StartupType Automatic
Start-Service sshd

# For an administrator, Windows only reads this file, and only with these rights.
$keys = "C:\ProgramData\ssh\administrators_authorized_keys"
if (-not (Test-Path $keys) -or -not (Select-String -Path $keys -SimpleMatch $Key -Quiet)) { Add-Content -Path $keys -Value $Key -Encoding ascii }
icacls $keys /inheritance:r /grant "Administrators:F" /grant "SYSTEM:F" | Out-Null

if (-not (Test-Path $Path)) {
    git clone https://github.com/keezpipslab/SkeltonMaker.git $Path
    git -C $Path checkout $Branch
}

$ip = (Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.PrefixOrigin -in "Dhcp", "Manual" -and $_.IPAddress -notlike "169.*" } | Select-Object -First 1).IPAddress
Write-Host "Ready. This PC is $ip, as user $env:USERNAME."
Write-Host "On the first PC, add to ~/.ssh/config:"
Write-Host "    Host pc2`n        HostName $ip`n        User $env:USERNAME"
Write-Host "then run there: .\deploy.ps1"
Write-Host "Open $Path in Unity 6000.3.22f1 here once, so it imports, and answer any package dialog."
