Set-Location $PSScriptRoot\..
Write-Host "Остановка всех сервисов..."
docker-compose down
Write-Host "Система остановлена."