# SQL Server Maintenance

SQL Server Maintenance to konsolowe narzędzie administracyjne służące do weryfikacji i konfiguracji środowiska serwerowego aplikacji (SQL Server, Harmonogram zadań Windows, pliki konserwacyjne).

Aplikacja umożliwia:

- sprawdzenie stanu dysków (pliki aplikacji oraz pliki bazy danych)
- wyświetlenie wersji i edycji SQL Server
- odczyt i zmianę godziny startu hurtowni danych
- weryfikację i konfigurację zadania Harmonogramu zadań
- sprawdzenie oraz kopiowanie plików konserwacyjnych do odpowiedniego katalogu 

## Uruchamianie

Program:

- musi być uruchomiony jako Administrator
- wymaga dokładnie dwóch parametrów startowych
- powinien być uruchamiany przez plik `.bat`

Przykład:
```bash
SQLServerMaintenance.exe --hurtownia=02:00 --konserwacja=03:00
```


## Parametry

### --hurtownia=HH:mm

Zmienia godzinę startu hurtowni danych.

Przykład:

```bash
--hurtownia=02:30
```

### --konserwacja=HH:mm

Tworzy lub aktualizuje zadanie Harmonogramu zadań:

- Nazwa: `Server Maintenance`
- Tryb: `DAILY`
- Użytkownik: `SYSTEM`
- Uprawnienia: `HIGHEST`
- Skrypt: `C:\ServerMaintenance\Service.bat`

Przykład:

```bash
--konserwacja=03:00
```


## Pliki konserwacyjne

Sprawdzany katalog:

```
C:\ServerMaintenance
```

Wymagane pliki:

- `Service.bat`
- `Service2.bat`
- `ServiceContainer.bat`

Pliki mogą zostać automatycznie wypakowane z zasobów aplikacji.


## Logika kopiowania

Program pyta użytkownika, czy skopiować pliki do katalogu `ServerMaintenance`.

Jeżeli użytkownik odmówi kopiowania, ale zmieni godzinę konserwacji, pliki zostaną automatycznie skopiowane po aktualizacji harmonogramu.


## Przeznaczenie

Narzędzie przeznaczone do wewnętrznej administracji i utrzymania środowiska serwerowego aplikacji.
