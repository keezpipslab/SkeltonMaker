<#
Puts this branch on the other PC (the second performer's Link PC): pushes it to GitHub, and sends
the commits there over SSH. Unity on that PC compiles the new scripts when its window gets focus.

    .\deploy.ps1                 # push, send the commits, and write ThisPc.json where it's missing
    .\deploy.ps1 -Settings       # also write ThisPc.json again on both PCs
    .\deploy.ps1 -Shift 0        # with -Settings: every skeleton on its real performer
    .\deploy.ps1 -Pc pc2 -Path C:\Unity\SkeletonMaker

ThisPc.json (next to Assets, not in git) is what differs between the two PCs running the same
scene: this PC is performer 1 and sends to the other's address, the other is performer 2 and
sends back to this one, and each is shown to the other moved by its stage shift.

The first time, set the other PC up with setup-pc2.ps1 (run there) and add it to ~/.ssh/config
here as "Host pc2". Stop Play on the other PC before deploying.
#>
param(
    [string]$Pc = "pc2",
    [string]$Path = "C:\Unity\SkeletonMaker",
    [switch]$Settings,
    # Meters: this PC's body is shown at -Shift on x, the other's at +Shift, so 2*Shift further apart.
    [double]$Shift = 1
)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$branch = (git rev-parse --abbrev-ref HEAD).Trim()
if (git status --porcelain --untracked-files=no) { Write-Warning "Uncommitted changes stay here: only what is committed goes over." }

Write-Host "Pushing $branch..."
git push -q -u origin $branch
if ($LASTEXITCODE) { throw "git push failed" }

# The other PC can't reach GitHub's credentials over SSH (Windows keeps them per logon), so
# the commits go straight from here: a git bundle of what it lacks, copied over with scp.
# The default shell of Windows' OpenSSH is cmd.
$there = (ssh $Pc "cd /d $Path && git rev-parse HEAD")
if ($LASTEXITCODE) { throw "reaching $Path on $Pc failed" }
$there = "$there".Trim()
$here = (git rev-parse $branch).Trim()
if ($there -eq $here) {
    Write-Host "$Pc already has $branch."
} else {
    Write-Host "Sending $branch to $Pc..."
    $range = $branch
    git merge-base --is-ancestor $there $here 2>$null
    if (-not $LASTEXITCODE) { $range = "$there..$branch" }
    $bundle = Join-Path $env:TEMP "skeletonmaker-deploy.bundle"
    git bundle create -q $bundle $range
    if ($LASTEXITCODE) { throw "making the bundle failed" }
    scp -q $bundle "${Pc}:$($Path -replace '\\','/')/.git/deploy.bundle"
    if ($LASTEXITCODE) { throw "copying the bundle failed" }
    Remove-Item $bundle
    ssh $Pc "cd /d $Path && git fetch -q .git\deploy.bundle +${branch}:refs/remotes/pc1/$branch && (git checkout -q $branch 2>nul || git checkout -q -b $branch pc1/$branch) && git merge -q --ff-only pc1/$branch && del .git\deploy.bundle && git log --oneline -1"
    if ($LASTEXITCODE) { throw "updating $branch on $Pc failed" }
}

# ThisPc.json on both sides. Each one's address as the other sees it: the other PC's is the one ssh
# connects to, and this PC's is the one the other PC's SSH server saw the connection come from.
$herePath = Join-Path $PSScriptRoot "ThisPc.json"
$thereHas = (ssh $Pc "if exist $Path\ThisPc.json (echo yes) else (echo no)").Trim() -eq "yes"
if ($Settings -or -not (Test-Path $herePath) -or -not $thereHas) {
    $connection = ("$(ssh $Pc 'echo %SSH_CONNECTION%')".Trim() -split '\s+')
    if ($connection.Count -lt 3) { throw "couldn't tell the two PCs' addresses apart" }
    $hereIp = $connection[0]
    $thereIp = $connection[2]
    $inv = [System.Globalization.CultureInfo]::InvariantCulture
    $neg = (-$Shift).ToString($inv)
    $pos = $Shift.ToString($inv)
    $hereJson = "{ `"performerId`": 1, `"remoteHost`": `"$thereIp`", `"stageShift`": { `"x`": $neg, `"y`": 0, `"z`": 0 } }"
    $thereJson = "{ `"performerId`": 2, `"remoteHost`": `"$hereIp`", `"stageShift`": { `"x`": $pos, `"y`": 0, `"z`": 0 } }"
    if ($Settings -or -not (Test-Path $herePath)) {
        Set-Content -Path $herePath -Value $hereJson -Encoding ascii
        Write-Host "Here:  $hereJson"
    }
    if ($Settings -or -not $thereHas) {
        $tmp = Join-Path $env:TEMP "ThisPc.json"
        Set-Content -Path $tmp -Value $thereJson -Encoding ascii
        scp -q $tmp "${Pc}:$($Path -replace '\\','/')/ThisPc.json"
        if ($LASTEXITCODE) { throw "copying ThisPc.json failed" }
        Remove-Item $tmp
        Write-Host "There: $thereJson"
    }
}

Write-Host "Done. Give Unity on $Pc focus to compile, then press Play on both (Link up first)."
