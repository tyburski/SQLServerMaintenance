using Microsoft.Data.SqlClient;
using Microsoft.Win32;
using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;

class Program
{
    /*Program uruchamiany jest przez .bat, który dba o uprawniania administratora oraz ustawienie parametrów startowych*/
    static void Main(string[] args)
    {
        string targetDirectory = @"C:\ServerMaintenance";
        string taskName = "Server Maintenance";

        bool filesCopiedManually = false;
        bool copyAfterScheduleChange = false;

        /*Brak możliwości uruchomienia bez uprawnień administratora
          oraz bez parametrów startowych*/

        var errors = new List<string>();
        if (args == null || args.Length != 2)
        {
            errors.Add("Błąd: Brak parametrów uruchomieniowych");
        }

        if (!IsAdministrator())
        {
            errors.Add("Błąd: Aplikacja wymaga uprawnień administratora");
        }

        if (errors.Count > 0)
        {
            foreach (var error in errors)
            {
                WriteColored(error, ConsoleColor.Red);
            }
            Console.WriteLine();
            Console.WriteLine("Press any key to continue . . .");
            Console.ReadKey();
            return;

        }

        /*Pobranie danych z rejestru*/

        string regPath = @"SOFTWARE\WOW6432Node\ServerApp\ApplicationServer\Preferences";
        using (RegistryKey key = Registry.LocalMachine.OpenSubKey(regPath))
        {
            if (key != null)
            {
                string filesPath = key.GetValue("FilesPath")?.ToString();
                if (!string.IsNullOrEmpty(filesPath))
                {
                    /*Badanie dysku z plikami*/
                    try
                    {
                        string driveRoot = Path.GetPathRoot(filesPath);
                        DriveInfo drive = new DriveInfo(driveRoot);

                        WriteColored("Pliki:", ConsoleColor.DarkCyan);
                        Console.WriteLine($"Dysk {drive.Name.TrimEnd('\\')} {(drive.TotalSize - drive.AvailableFreeSpace) / 1024 / 1024 / 1024}/{drive.TotalSize / 1024 / 1024 / 1024}GB WOLNE MIEJSCE: {drive.AvailableFreeSpace / 1024 / 1024 / 1024}GB");
                        Console.WriteLine();
                    }
                    catch (Exception ex)
                    {
                        WriteColored($"Pliki: Błąd przy sprawdzaniu dysku: {ex.Message}", ConsoleColor.Red);
                    }
                }

                string hostName = key.GetValue("HostName")?.ToString();
                string databaseName = key.GetValue("Database")?.ToString();

                if (!string.IsNullOrEmpty(hostName) && !string.IsNullOrEmpty(databaseName))
                {
                    string connectionString = $"Server={hostName};Database=master;Trusted_Connection=True;Integrated Security=True;Persist Security Info=False;TrustServerCertificate=True;";

                    using (SqlConnection connection = new SqlConnection(connectionString))
                    {
                        try
                        {
                            connection.Open();

                            CheckDatabaseDisk(connection, databaseName);
                            CheckSqlVersion(connection);
                            CheckHurtownia(connection, databaseName);

                            Console.WriteLine();

                            HarmoCheck(taskName);

                            Console.WriteLine();

                            CheckFiles(targetDirectory);

                            (copyAfterScheduleChange, filesCopiedManually) = ProcessArgs(args, targetDirectory, connection, databaseName, taskName, filesCopiedManually);

                        }
                        catch (Exception ex)
                        {
                            WriteColored($"Błąd połączenia do SQL Server: {ex.Message}", ConsoleColor.Red);
                        }
                    }
                }
            }
        }

        if (copyAfterScheduleChange && !filesCopiedManually)
        {
            CopyFiles(targetDirectory);
        }
        Console.WriteLine();
    }

    static bool IsAdministrator()
    {
        WindowsIdentity identity = WindowsIdentity.GetCurrent();
        WindowsPrincipal principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    static void WriteColored(string text, ConsoleColor color)
    {
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ResetColor();
    }

    static bool AskYesNo(string prompt)
    {
        while (true)
        {
            Console.WriteLine();
            Console.WriteLine($"{prompt} [TAK/NIE]");
            string input = Console.ReadLine()?.Trim().ToUpper();
            if (input == "TAK") return true;
            if (input == "NIE") return false;
            WriteColored("Wpisz TAK lub NIE", ConsoleColor.Yellow);
        }
    }


    /*Sprawdzanie umiejscowienia plików bazy danych oraz badanie dysku*/
    static void CheckDatabaseDisk(SqlConnection connection, string databaseName)
    {
        string query = @"
            SELECT TOP 1 physical_name
            FROM sys.master_files
            WHERE database_id = DB_ID(@dbName);";

        using (SqlCommand command = new SqlCommand(query, connection))
        {
            command.Parameters.AddWithValue("@dbName", databaseName);
            using (SqlDataReader reader = command.ExecuteReader())
            {
                if (reader.Read())
                {
                    string filePath = reader["physical_name"].ToString();
                    string driveRoot = Path.GetPathRoot(filePath);


                    try
                    {
                        DriveInfo drive = new DriveInfo(driveRoot);

                        WriteColored("Baza danych:", ConsoleColor.DarkCyan);
                        Console.WriteLine($"Dysk {drive.Name.TrimEnd('\\')} {(drive.TotalSize - drive.AvailableFreeSpace) / 1024 / 1024 / 1024}/{drive.TotalSize / 1024 / 1024 / 1024}GB WOLNE MIEJSCE: {drive.AvailableFreeSpace / 1024 / 1024 / 1024}GB");
                        Console.WriteLine();
                    }
                    catch (Exception ex)
                    {
                        WriteColored($"Baza danych: Błąd przy sprawdzaniu dysku: {ex.Message}", ConsoleColor.Red);
                    }
                }
                else
                {
                    WriteColored("Nie znaleziono plików bazy danych", ConsoleColor.Yellow);
                }
            }
        }
    }

    /*Sprawdzenie wersji oraz edysji SQL Servera*/
    static void CheckSqlVersion(SqlConnection connection)
    {
        string versionQuery = @"
                SELECT  
                SERVERPROPERTY('ProductVersion') AS ProductVersion,
                SERVERPROPERTY('Edition')        AS Edition;";

        using (SqlCommand cmd = new SqlCommand(versionQuery, connection))
        using (SqlDataReader reader = cmd.ExecuteReader())
        {
            if (reader.Read())
            {
                WriteColored("SQL Server:", ConsoleColor.DarkCyan);
                Console.WriteLine($"Wersja: {reader["ProductVersion"]}");
                Console.WriteLine($"Edycja: {reader["Edition"]}");
                Console.WriteLine();
            }
        }
    }

    /*Sprawdzenie godziny urchomienia hurtowni danych*/
    static void CheckHurtownia(SqlConnection connection, string databaseName)
    {
        string query = $@"
            USE [{databaseName}];
            SELECT TOP 1 StartAt
            FROM Scheduler
            WHERE Type = 1;";

        try
        {
            using (SqlCommand cmd = new SqlCommand(query, connection))
            using (SqlDataReader reader = cmd.ExecuteReader())
            {
                if (reader.Read() && !reader.IsDBNull(reader.GetOrdinal("StartAt")))
                {
                    DateTime startAt = reader.GetDateTime(reader.GetOrdinal("StartAt"));
                    Console.ForegroundColor = ConsoleColor.DarkCyan;
                    Console.Write("Hurtownia danych: ");
                    Console.ResetColor();
                    Console.Write($"StartAt: {startAt:HH:mm}");
                    Console.WriteLine();
                }
                else
                {

                    Console.ForegroundColor = ConsoleColor.DarkCyan;
                    Console.Write("Hurtownia danych: ");
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.Write($"Nie ustawiono");
                    Console.ResetColor();
                    Console.WriteLine();
                }
            }
        }
        catch (Exception ex)
        {
            WriteColored($"Błąd pobierania danych z Scheduler: {ex.Message}", ConsoleColor.Red);
        }
    }

    /*Sprawdzenie czy jest ustawione zadanie z wyzwalaczem z harmonogramie*/
    static void HarmoCheck(string taskName)
    {
        string args = $"/Query /TN \"{taskName}\" /V /FO LIST";

        ProcessStartInfo psi = new ProcessStartInfo("schtasks.exe", args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using (Process process = Process.Start(psi))
        {
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.Write("Konserwacja serwera: ");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write($"Nie ustawiono");
                Console.ResetColor();
                Console.WriteLine();
                return;
            }

            var lines = output.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);

            string GetLineValue(string prefix)
            {
                var line = lines.FirstOrDefault(l => l.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
                if (line != null)
                {
                    int idx = line.IndexOf(':');
                    if (idx >= 0 && idx + 1 < line.Length)
                        return line.Substring(idx + 1).Trim();
                }
                return "-";
            }

            string scheduleType = GetLineValue("Schedule Type");
            string startTime = GetLineValue("Start Time");
            string taskToRun = GetLineValue("Task To Run");

            string hour = DateTime.TryParse(startTime, out DateTime parsed) ? parsed.ToString("HH:mm") : "-";

            var lista = new[]
            {
                $"Nazwa: {taskName}",
                $"Typ: {scheduleType}",
                $"Godzina: {hour}",
                $"Akcja: Uruchom '{taskToRun}'"
            };

            string label = "Konserwacja serwera: ";
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.Write(label);
            Console.ResetColor();
            Console.WriteLine(lista[0]);

            string indent = new string(' ', label.Length);
            foreach (var item in lista.Skip(1))
            {
                Console.WriteLine($"{indent}{item}");
            }
        }
    }

    /*Sprawadznie czy pliki .bat z logiką konserwacji serwera są umieszczone w odpowiednim folderze*/
    static void CheckFiles(string targetDirectory)
    {
        string[] expectedFiles =
        {
            "Service.bat",
            "Service2.bat",
            "ServiceContainer.bat"
        };

        string label = "Folder ServerMaintenance: ";
        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.Write(label);
        Console.ResetColor();

        string indent = new string(' ', label.Length);

        if (!Directory.Exists(targetDirectory))
        {
            WriteColored($"Folder {targetDirectory} nie istnieje", ConsoleColor.Yellow);
            return;
        }

        foreach (var file in expectedFiles)
        {
            string fullPath = Path.Combine(targetDirectory, file);
            if (file == expectedFiles.First())
            {
                if (File.Exists(fullPath))
                    WriteColored($"Plik {file}: OK", ConsoleColor.Green);
                else
                    WriteColored($"Plik {file}: BRAK", ConsoleColor.Red);
            }
            else
            {
                if (File.Exists(fullPath))
                    WriteColored($"{indent}Plik {file}: OK", ConsoleColor.Green);
                else
                    WriteColored($"{indent}Plik {file}: BRAK", ConsoleColor.Red);
            }

        }

        Console.WriteLine();
    }

    /*Kopiowanie plików z zasobu aplikacji do folderu*/
    static void CopyFiles(string targetDirectory)
    {
        string[] filesToCopy =
        {
            "Service.bat",
            "Service2.bat",
            "ServiceContainer.bat"
        };

        if (!Directory.Exists(targetDirectory))
            Directory.CreateDirectory(targetDirectory);

        var assembly = Assembly.GetExecutingAssembly();
        string projectName = assembly.GetName().Name;

        string label = "Kopiowanie plików: ";
        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.Write(label);
        Console.ResetColor();

        string indent = new string(' ', label.Length);

        foreach (var file in filesToCopy)
        {
            string resourceName = $"{projectName}.Files.{file}";
            string destinationPath = Path.Combine(targetDirectory, file);

            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    WriteColored(indent + $"Nie znaleziono zasobu: {resourceName}", ConsoleColor.Yellow);
                    continue;
                }

                using (FileStream fs = new FileStream(destinationPath, FileMode.Create, FileAccess.Write))
                    stream.CopyTo(fs);
            }

            if (file == filesToCopy.First())
            {
                WriteColored($"Wypakowano: {file}", ConsoleColor.Green);
            }
            else
            {
                WriteColored(indent + $"Wypakowano: {file}", ConsoleColor.Green);
            }

        }

        Console.WriteLine();
    }

    /*Proces ustawień. Według logiki gdy użytkownik anuluje kopiowanie plików, ale zmieni godzinę startu konserwacji pliki są kopiowane dla pewności.
      Jeśli potwierdzi kopiowanie wcześniej, nie uruchomi się ono podczas ustawiania konserwacji*/
    static (bool, bool) ProcessArgs(string[] args, string targetDirectory, SqlConnection connection, string databaseName, string taskName, bool filesCopiedManually)
    {
        bool copyAfterScheduleChange = false;

        if (args == null) return (false, filesCopiedManually);

        if (!filesCopiedManually)
        {
            Console.WriteLine();
            if (AskYesNo("Czy chcesz przekopiować pliki do folderu ServerMaintenance?"))
            {
                CopyFiles(targetDirectory);
                filesCopiedManually = true;
            }
            else
            {
                WriteColored("Operacja anulowana przez użytkownika", ConsoleColor.Yellow);
            }
        }

        foreach (var arg in args)
        {
            if (arg.StartsWith("--hurtownia=", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine();
                string timePart = arg.Substring("--hurtownia=".Length);
                if (DateTime.TryParseExact(timePart, "HH:mm", null, System.Globalization.DateTimeStyles.None, out DateTime dt))
                {
                    TimeSpan ts = dt.TimeOfDay;

                    if (AskYesNo($"Zmienić start hurtowni danych na {timePart}?"))
                    {
                        DateTime newDateTime = new DateTime(2018, 10, 3).Add(ts);

                        string updateQuery = $@"
                            USE [{databaseName}];
                            UPDATE Scheduler
                            SET StartAt = @StartAt
                            WHERE Type = 1;";

                        try
                        {
                            using (SqlCommand command = new SqlCommand(updateQuery, connection))
                            {
                                command.Parameters.Add("@StartAt", System.Data.SqlDbType.DateTime).Value = newDateTime;
                                int rowsAffected = command.ExecuteNonQuery();
                                WriteColored($"Skrypt wykonany pomyślnie. Zmienionych rekordów: {rowsAffected}", ConsoleColor.Green);
                            }
                        }
                        catch (Exception ex)
                        {
                            WriteColored($"Błąd podczas aktualizacji bazy: {ex.Message}", ConsoleColor.Red);
                        }
                    }
                    else
                    {
                        WriteColored("Operacja anulowana przez użytkownika", ConsoleColor.Yellow);
                    }
                }
                else
                {
                    WriteColored($"Nieprawidłowy format parametru HURTOWNIA: {timePart}", ConsoleColor.Red);
                    WriteColored($"OPERACJA ANULOWANA", ConsoleColor.Red);
                }
            }
            if (arg.StartsWith("--konserwacja=", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine();
                string kTimePart = arg.Substring("--konserwacja=".Length);
                if (DateTime.TryParseExact(kTimePart, "HH:mm", null, System.Globalization.DateTimeStyles.None, out DateTime kdt))
                {
                    TimeSpan kts = kdt.TimeOfDay;

                    if (AskYesNo($"Zmienić start konserwacji serwera na {kTimePart}?"))
                    {
                        string mainScript = Path.Combine(targetDirectory, "Service.bat");
                        string arguments = $"/Create /TN \"{taskName}\" /TR \"{mainScript}\" /SC DAILY /ST {kts:hh\\:mm} /RU SYSTEM /RL HIGHEST /F";

                        ProcessStartInfo psi = new ProcessStartInfo("schtasks.exe", arguments)
                        {
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };

                        using (Process process = Process.Start(psi))
                        {
                            string output = process.StandardOutput.ReadToEnd();
                            string error = process.StandardError.ReadToEnd();
                            process.WaitForExit();

                            WriteColored(output, ConsoleColor.Green);
                            WriteColored(error, ConsoleColor.Red);

                            if (process.ExitCode != 0)
                                WriteColored($"BŁĄD: schtasks zakończył się kodem {process.ExitCode}", ConsoleColor.Red);
                            else
                                copyAfterScheduleChange = true;
                        }
                    }
                    else
                        WriteColored("Operacja anulowana przez użytkownika", ConsoleColor.Yellow);
                }
                else
                {
                    WriteColored($"Nieprawidłowy format parametru KONSERWACJA: {kTimePart}", ConsoleColor.Red);
                    WriteColored($"OPERACJA ANULOWANA", ConsoleColor.Red);
                }
            }
        }
        return (copyAfterScheduleChange, filesCopiedManually);
    }
}
