<#
.SYNOPSIS
  Start/stop/status the portable PostgreSQL 17 used for local development WITHOUT Docker.
  Data dir: %LOCALAPPDATA%\StockPilotDev\data · Port: 5433 · user/password: postgres/postgres

.EXAMPLE
  ./scripts/dev-postgres.ps1 start
  ./scripts/dev-postgres.ps1 stop
  ./scripts/dev-postgres.ps1 status
#>
param([ValidateSet('start', 'stop', 'status')][string]$Action = 'status')

$root = Join-Path $env:LOCALAPPDATA 'StockPilotDev'
$bin = Join-Path $root 'pgsql\bin'
$data = Join-Path $root 'data'

if (-not (Test-Path "$bin\pg_ctl.exe")) {
    Write-Error "Portable PostgreSQL not found at $bin. Use Docker instead: docker compose up -d postgres"
    exit 1
}

switch ($Action) {
    'start' { & "$bin\pg_ctl.exe" -D $data -l "$root\pg.log" -o "-p 5433 -c listen_addresses=localhost" -w start }
    'stop' { & "$bin\pg_ctl.exe" -D $data -w stop }
    'status' { & "$bin\pg_ctl.exe" -D $data status }
}
