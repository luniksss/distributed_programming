Set-Location $PSScriptRoot\..
Write-Host "Запуск всех сервисов через docker-compose..."
docker-compose up -d
Write-Host "Система запущена. Nginx доступен по адресу http://localhost:8080"