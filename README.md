# AppProxy

Проксификатор для Windows 10/11 x64. Интерфейс написан на C# с Windows Forms и .NET 10. Маршрутизацией через виртуальный адаптер TUN занимается отдельный процесс [sing-box 1.14.4](https://github.com/SagerNet/sing-box/tree/v1.14.4).

## Скачать и запустить

1. Скачайте `AppProxy-win-x64.zip` из [Releases](https://github.com/Ambrozy/proxy-win/releases).
2. Распакуйте весь архив и запустите `AppProxy.exe`. Подтвердите запрос Windows на запуск от имени администратора.
3. Нажмите **Из списка…**, найдите программу по названию или имени EXE и выберите её. Список строится по ярлыкам меню «Пуск» и путям программ, зарегистрированным в Windows. Если нужной программы нет, нажмите **Файл…** и выберите её `.exe`.
4. Укажите протокол, сервер, порт и при необходимости логин и пароль.
5. Нажмите **Включить**. Когда маршрутизация запустится, перезапустите выбранное приложение. Для отключения нажмите **Остановить**.

SOCKS5 передаёт TCP и UDP при поддержке `UDP ASSOCIATE` сервером. HTTPS CONNECT передаёт TCP и блокирует UDP для распознанного приложения. Правило сопоставляет полный путь `.exe`. Другие процессы, системный DNS от имени службы Windows и трафик вне TUN могут идти напрямую. Приложение не гарантирует блокировку всех прямых соединений.

## Собрать из исходников

Потребуется .NET 10 SDK.

```powershell
dotnet publish AppProxy.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o publish
```

Скачайте оригинальную сборку [sing-box 1.14.4 для Windows x64](https://github.com/SagerNet/sing-box/releases/tag/v1.14.4) и распакуйте её файлы в `publish/engine/`. Убедитесь, что файл движка находится по пути `publish/engine/sing-box.exe`. Запустите `publish/AppProxy.exe`.

Исходный код поставляемой версии движка доступен в [официальном репозитории sing-box](https://github.com/SagerNet/sing-box/tree/v1.14.4).
