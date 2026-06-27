param(
    [switch]$OnlineAudit,
    [switch]$EnforceRepositoryHygiene
)

$ErrorActionPreference = "Stop"
$Root = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$Failures = New-Object System.Collections.Generic.List[string]
$Warnings = New-Object System.Collections.Generic.List[string]

function Add-Failure {
    param([string]$Message)
    $Failures.Add($Message) | Out-Null
    Write-Host "[FAIL] $Message" -ForegroundColor Red
}

function Add-Warning {
    param([string]$Message)
    $Warnings.Add($Message) | Out-Null
    Write-Host "[WARN] $Message" -ForegroundColor Yellow
}

function Invoke-External {
    param(
        [string]$Name,
        [scriptblock]$Command
    )

    Write-Host "[RUN ] $Name"
    & $Command
    if ($LASTEXITCODE -ne 0) {
        Add-Failure "$Name exited with code $LASTEXITCODE."
    }
}

function Test-GitHygiene {
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        Add-Warning "git is not available; repository hygiene checks were skipped."
        return
    }

    $tracked = @(& git -C $Root ls-files)
    $artifactPatterns = @(
        "^\.vs/",
        "(^|/)bin/",
        "(^|/)obj/",
        "^artifacts/",
        "^IPC\.Gateway\.Web/node_modules/",
        "^IPC\.Gateway\.Web/dist/",
        "(^|/)Data/Logs/",
        "\.log$"
    )

    $trackedArtifacts = @($tracked | Where-Object {
        $path = ($_ -replace "\\", "/")
        $artifactPatterns | Where-Object { $path -match $_ } | Select-Object -First 1
    })

    if ($trackedArtifacts.Count -gt 0) {
        $message = "Tracked generated/local files found: $($trackedArtifacts.Count). Clean with git rm --cached after validating the branch."
        if ($EnforceRepositoryHygiene) {
            Add-Failure $message
        } else {
            Add-Warning $message
            $trackedArtifacts | Select-Object -First 20 | ForEach-Object { Write-Host "       $_" }
        }
    }

    $eol = @(& git -C $Root ls-files --eol)
    $sourcePattern = "\.(cs|csproj|props|targets|slnx|json|md|ts|vue|js|css|html|ps1|sh|yml|yaml)$"
    $binarySources = @($eol | Where-Object {
        $_ -match "(i/-text|w/-text)" -and
        $_ -match $sourcePattern -and
        $_ -notmatch "(^|/)(bin|obj|node_modules|artifacts|\.vs)/"
    })

    if ($binarySources.Count -gt 0) {
        Add-Warning "Source-like files are still detected as binary by Git: $($binarySources.Count). Re-normalize after .gitattributes is committed."
        $binarySources | Select-Object -First 20 | ForEach-Object { Write-Host "       $_" }
    }
}

function Test-NuGetDependencies {
    $projectFiles = Get-ChildItem -Path $Root -Recurse -Filter "*.csproj" |
        Where-Object { $_.FullName -notmatch "\\(bin|obj)\\" }

    foreach ($project in $projectFiles) {
        [xml]$xml = Get-Content -LiteralPath $project.FullName -Raw
        $refs = @($xml.Project.ItemGroup.PackageReference)
        foreach ($ref in $refs) {
            if ($null -eq $ref) {
                continue
            }

            $id = [string]$ref.Include
            $version = [string]$ref.Version
            if ([string]::IsNullOrWhiteSpace($id)) {
                $id = [string]$ref.Update
            }

            if ([string]::IsNullOrWhiteSpace($version)) {
                Add-Failure "$($project.Name) package '$id' does not declare an explicit version."
                continue
            }

            if ($version -match "[\*\[\]\(\),]") {
                Add-Failure "$($project.Name) package '$id' uses non-exact NuGet version '$version'."
            }

            if ($version -match "-") {
                Add-Warning "$($project.Name) package '$id' uses prerelease version '$version'."
            }
        }
    }

    $lockFiles = @(Get-ChildItem -Path $Root -Recurse -Filter "packages.lock.json" |
        Where-Object { $_.FullName -notmatch "\\(bin|obj)\\" })
    if ($lockFiles.Count -eq 0) {
        Add-Warning "No NuGet packages.lock.json files found yet. Run dotnet restore once after this change to generate them."
    }
}

function Test-NpmDependencies {
    $packageJson = Join-Path $Root "IPC.Gateway.Web\package.json"
    $packageLock = Join-Path $Root "IPC.Gateway.Web\package-lock.json"

    if (-not (Test-Path $packageJson)) {
        Add-Warning "Frontend package.json not found; npm dependency checks were skipped."
        return
    }

    if (-not (Test-Path $packageLock)) {
        Add-Failure "Frontend package-lock.json is missing."
        return
    }

    if (-not (Get-Command node -ErrorAction SilentlyContinue)) {
        Add-Warning "node is not available; npm lock consistency checks were skipped."
        return
    }

    $nodeScript = @'
const fs = require("fs");
const path = require("path");
const root = process.argv[2];
const pkg = JSON.parse(fs.readFileSync(path.join(root, "IPC.Gateway.Web", "package.json"), "utf8"));
const lock = JSON.parse(fs.readFileSync(path.join(root, "IPC.Gateway.Web", "package-lock.json"), "utf8"));
const deps = Object.assign({}, pkg.dependencies || {}, pkg.devDependencies || {});
const exactVersion = /^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$/;
const errors = [];

for (const [name, declared] of Object.entries(deps)) {
  if (!exactVersion.test(declared)) {
    errors.push(`${name} is not pinned to an exact version: ${declared}`);
  }

  const locked = lock.packages && lock.packages[`node_modules/${name}`];
  if (!locked) {
    errors.push(`${name} is missing from package-lock.json`);
  } else if (locked.version !== declared) {
    errors.push(`${name} package.json=${declared} package-lock=${locked.version}`);
  }
}

const rootPackage = lock.packages && lock.packages[""];
for (const section of ["dependencies", "devDependencies"]) {
  const rootDeps = (rootPackage && rootPackage[section]) || {};
  for (const [name, declared] of Object.entries(pkg[section] || {})) {
    if (rootDeps[name] !== declared) {
      errors.push(`package-lock root ${section}.${name}=${rootDeps[name] || "<missing>"} but package.json=${declared}`);
    }
  }
}

if (errors.length) {
  for (const error of errors) console.error(error);
  process.exit(1);
}
'@

    $nodeScript | node - $Root
    if ($LASTEXITCODE -ne 0) {
        Add-Failure "Frontend package.json and package-lock.json are not supply-chain consistent."
    }
}

Write-Host "IPC Gateway supply-chain checks"
Write-Host "Root: $Root"

Test-GitHygiene
Test-NuGetDependencies
Test-NpmDependencies

if ($OnlineAudit) {
    Invoke-External "NuGet vulnerable package audit" {
        dotnet list (Join-Path $Root "IPC.Gateway.slnx") package --vulnerable --include-transitive
    }

    Invoke-External "npm audit" {
        npm --prefix (Join-Path $Root "IPC.Gateway.Web") audit --omit dev
    }
} else {
    Write-Host "[SKIP] Online vulnerability audit. Pass -OnlineAudit when registry access is available."
}

if ($Failures.Count -gt 0) {
    Write-Host "Supply-chain checks failed: $($Failures.Count) failure(s), $($Warnings.Count) warning(s)." -ForegroundColor Red
    exit 1
}

Write-Host "Supply-chain checks passed with $($Warnings.Count) warning(s)." -ForegroundColor Green
