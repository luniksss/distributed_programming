#!/bin/bash
cd "$(dirname "$0")/.."
docker compose up -d
echo "Система запущена."
echo "Nginx доступен по адресу http://localhost:8080"
echo "Экземпляры приложения: http://localhost:8081 и http://localhost:8082"