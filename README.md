Lemiesze Calculator — Windows WPF .NET 10

Otwórz LemieszeDesktop.csproj w Rider.

Katalog zapisuje się wyłącznie lokalnie w %LOCALAPPDATA%\LemieszeCalculator\catalog.json.

Ikony w folderze icon pochodzą z przesłanego projektu.
Aby ustawić ikonę pliku EXE, dodaj plik icon/logo.ico przed kompilacją.

Publikacja: dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
