using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

public struct Weather
{
    public string Country { get; set; }
    public string Name { get; set; }
    public double Temp { get; set; }
    public string Description { get; set; }
    
    public override string ToString() //выводит что 
    {
        return $"Weather in {Name}, {Country}: {Temp:F1}°C, {Description}";
    }
}

public class City
{
    public string Name { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    
    public override string ToString()
    {
        return $"{Name} (Lat: {Latitude:F2}, Lon: {Longitude:F2})";
    }
}

public class WeatherService
{
    private readonly string _apiKey;
    private readonly HttpClient _httpClient;
    
    public WeatherService(string apiKey)
    {
        _apiKey = apiKey;
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
    }
    
    public async Task<Weather> GetWeatherAsync(double lat, double lon) //получение погоды по координатам 
    {
        try
        {
            string url = $"https://api.openweathermap.org/data/2.5/weather?lat={lat}&lon={lon}&appid={_apiKey}&units=metric&lang=en";
            
            HttpResponseMessage response = await _httpClient.GetAsync(url); //формируем запрос 
            response.EnsureSuccessStatusCode(); //проверка на успешность выполнения 
            
            string json = await response.Content.ReadAsStringAsync();
            using JsonDocument doc = JsonDocument.Parse(json);
            
            var root = doc.RootElement;
            
            return new Weather
            {
                Country = root.GetProperty("sys").GetProperty("country").GetString(),
                Name = root.GetProperty("name").GetString(),
                Temp = root.GetProperty("main").GetProperty("temp").GetDouble(),
                Description = root.GetProperty("weather")[0].GetProperty("description").GetString()
            };
        }
        catch (Exception ex)
        {
            throw new Exception($"Error fetching weather data: {ex.Message}", ex);
        }
    }
}

class Program
{
    private static List<City> cities = new List<City>();
    private static WeatherService weatherService;
    
    private const string API_KEY = "09e629c93df19c440f9b32c3ed0ead4d";
    
    static async Task Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.InputEncoding = System.Text.Encoding.UTF8;

        string projectDir = Directory.GetCurrentDirectory();
        
        if (projectDir.Contains("bin") && (projectDir.Contains("Debug") || projectDir.Contains("Release")))
        {
            projectDir = Directory.GetParent(Directory.GetParent(projectDir).FullName).FullName;
        }
        
        Console.WriteLine($"Project directory: {projectDir}");
        
        string cityFilePath = Path.Combine(projectDir, "/home/vlada/Изображения/city.txt");
        string citiesCsvPath = Path.Combine(projectDir, "cities.csv");
        
        Console.WriteLine($"Using API key: {API_KEY.Substring(0, 8)}...");
        weatherService = new WeatherService(API_KEY);
        
        bool citiesLoaded = false;
        
        if (File.Exists(cityFilePath))
        {
            Console.WriteLine($"\nFound {cityFilePath}, converting to CSV format...");
            ConvertCityFileProperly(cityFilePath, citiesCsvPath);
            Console.WriteLine("Press any key to continue...");
            Console.ReadKey();
        }
        else
        {
            Console.WriteLine($"\nFile {cityFilePath} not found.");
            Console.WriteLine("Please make sure city.txt is in the project directory.");
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
            return;
        }
        
        if (File.Exists(citiesCsvPath))
        {
            Console.WriteLine($"\nLoading cities from {citiesCsvPath}...");
            citiesLoaded = LoadAllCitiesFromCSV(citiesCsvPath);
        }
        
        if (!citiesLoaded)
        {
            Console.WriteLine("\nFailed to load cities. Exiting...");
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
            return;
        }
        
        Console.WriteLine($"\n Successfully loaded {cities.Count} cities!");
        Console.WriteLine("Press any key to continue to main menu...");
        Console.ReadKey();
        
        bool exit = false;
        
        while (!exit)
        {
            Console.Clear();
            Console.WriteLine($" Cities loaded: {cities.Count}");
            Console.WriteLine("1. Show city list");
            Console.WriteLine("2. Get weather for city");
            Console.WriteLine("3. Exit");
            Console.Write("Select option: ");
            
            string choice = Console.ReadLine();
            
            switch (choice)
            {
                case "1":
                    ShowCityList();
                    break;
                    
                case "2":
                    await GetWeatherForCityAsync();
                    break;
                    
                case "3":
                    exit = true;
                    Console.WriteLine("\nGoodbye!");
                    break;
                    
                default:
                    Console.WriteLine("\nInvalid option. Press any key to continue...");
                    Console.ReadKey();
                    break;
            }
        }
    }
    
    static void ConvertCityFileProperly(string inputFile, string outputFile)
    {
        try
        {
            Console.WriteLine($"\nReading {inputFile}...");
            string[] lines = File.ReadAllLines(inputFile, Encoding.UTF8);
            Console.WriteLine($"Found {lines.Length} lines");
            
            List<string> convertedLines = new List<string>();
            int convertedCount = 0;
            int errorCount = 0;
            
            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) //пропуск пустых строк 
                    continue;

                string[] parts = line.Split('\t');
                
                if (parts.Length >= 2)
                {
                    string name = parts[0].Trim();
                    string coordsPart = parts[1].Trim();

                    coordsPart = coordsPart.Replace(" ", "");
                    string[] coords = coordsPart.Split(',');
                    
                    if (coords.Length == 2)
                    {
                        string latStr = coords[0];
                        string lonStr = coords[1];

                        if (double.TryParse(latStr, System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out double lat) &&
                            double.TryParse(lonStr, System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out double lon))
                        {
                            convertedLines.Add($"{name},{lat},{lon}");
                            convertedCount++;
                        }
                        else
                        {
                            errorCount++;
                        }
                    }
                    else
                    {
                        errorCount++;
                    }
                }
                else
                {
                    errorCount++;
                }
            }
            
            File.WriteAllLines(outputFile, convertedLines, Encoding.UTF8);
            Console.WriteLine($"\n Conversion complete:");
            Console.WriteLine($"   Successfully converted: {convertedCount}");
            Console.WriteLine($"   Errors: {errorCount}");
            Console.WriteLine($"   Saved to: {outputFile}");
            
            if (convertedCount > 0)
            {
                Console.WriteLine("\nFirst 5 converted cities:");
                for (int i = 0; i < Math.Min(5, convertedLines.Count); i++)
                {
                    string[] parts = convertedLines[i].Split(',');
                    if (parts.Length >= 3)
                    {
                        Console.WriteLine($"  {i+1}. {parts[0]} - {parts[1]}, {parts[2]}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n Error converting file: {ex.Message}");
        }
    }
    
    static bool LoadAllCitiesFromCSV(string filePath)
    {
        cities.Clear();
        
        try
        {
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"\n File {filePath} not found.");
                return false;
            }

            string[] lines = File.ReadAllLines(filePath, Encoding.UTF8);
            
            int loaded = 0;
            int errors = 0;
            
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                
                string[] parts = line.Split(',');
                
                if (parts.Length >= 3)
                {
                    string name = parts[0].Trim();
                    string latStr = parts[1].Trim();
                    string lonStr = parts[2].Trim();

                    latStr = latStr.Replace(" ", "");
                    lonStr = lonStr.Replace(" ", "");

                    if (double.TryParse(latStr, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double lat) &&
                        double.TryParse(lonStr, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double lon))
                    {
                        cities.Add(new City
                        {
                            Name = name,
                            Latitude = lat,
                            Longitude = lon
                        });
                        loaded++;
                    }
                    else
                    {
                        errors++;
                    }
                }
                else
                {
                    errors++;
                }
            }
            return cities.Count > 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n Error loading cities: {ex.Message}");
            return false;
        }
    }
    
    static void ShowCityList()
    {
        Console.Clear();
        Console.WriteLine($"Total cities: {cities.Count}");
        
        if (cities.Count == 0)
        {
            Console.WriteLine("No cities loaded.");
        }
        else
        {
            int citiesToShow = Math.Min(25, cities.Count);
            Console.WriteLine($"Showing first {citiesToShow} cities:\n");
            
            for (int i = 0; i < citiesToShow; i++)
            {
                Console.WriteLine($"{i + 1:D3}. {cities[i]}");
            }
            
            if (cities.Count > 25)
            {
                Console.WriteLine($"\n... and {cities.Count - 25} more cities not shown.");
            }
        }
        
        Console.WriteLine("Press any key to return to menu...");
        Console.ReadKey();
    }
    
    static async Task GetWeatherForCityAsync()
    {
        Console.Clear();
        Console.WriteLine("      Получение погоды ");
        
        if (cities.Count == 0)
        {
            Console.WriteLine("\n No cities loaded.");
            Console.WriteLine("Press any key to return to menu...");
            Console.ReadKey();
            return;
        }
        
        int citiesToShow = Math.Min(20, cities.Count);
        Console.WriteLine($"\nSelect city (1-{citiesToShow}):\n");
        
        for (int i = 0; i < citiesToShow; i++)
        {
            Console.WriteLine($"{i + 1:D2}. {cities[i].Name}");
        }
        
        if (cities.Count > 20)
        {
            Console.WriteLine($"\n... and {cities.Count - 20} more cities available.");
        }
        
        Console.WriteLine($"\n0. View all cities ({cities.Count} total)");
        Console.Write("Enter city number: ");
        
        if (int.TryParse(Console.ReadLine(), out int choice))
        {
            if (choice == 0)
            {
                ShowFullCityListForSelection();
                return;
            }
            else if (choice >= 1 && choice <= citiesToShow)
            {
                City selectedCity = cities[choice - 1];
                await FetchAndDisplayWeather(selectedCity);
            }
            else
            {
                Console.WriteLine("\nInvalid selection.");
                Console.WriteLine("Press any key to return to menu...");
                Console.ReadKey();
            }
        }
        else
        {
            Console.WriteLine("\n Please enter a valid number.");
            Console.WriteLine("Press any key to return to menu...");
            Console.ReadKey();
        }
    }
    
    static void ShowFullCityListForSelection()
    {
        Console.Clear();
        Console.WriteLine("      Select from all cities ");
        Console.WriteLine($"Total cities: {cities.Count}");
        
        for (int i = 0; i < cities.Count; i++)
        {
            Console.WriteLine($"{i + 1:D3}. {cities[i].Name}");
        }

        Console.Write($"Enter city number (1-{cities.Count}): ");
        
        if (int.TryParse(Console.ReadLine(), out int choice) && 
            choice >= 1 && choice <= cities.Count)
        {
            City selectedCity = cities[choice - 1];
            _ = FetchAndDisplayWeather(selectedCity);
        }
        else
        {
            Console.WriteLine("\n Invalid selection.");
            Console.WriteLine("Press any key to return to menu...");
            Console.ReadKey();
        }
    }
    
    static async Task FetchAndDisplayWeather(City selectedCity)
    {
        Console.Clear();
        Console.WriteLine($"\n Loading weather for: {selectedCity.Name}");
        Console.WriteLine($" Coordinates: {selectedCity.Latitude:F4}, {selectedCity.Longitude:F4}");
        Console.WriteLine("\n" + new string('.', 50));
        
        try
        {
            var weatherTask = weatherService.GetWeatherAsync(
                selectedCity.Latitude, 
                selectedCity.Longitude);

            Console.Write("\nFetching data ");
            while (!weatherTask.IsCompleted)
            {
                Console.Write(".");
                await Task.Delay(400);
            }
            Console.WriteLine(" Выполнено");
            
            Weather weather = await weatherTask;
            
            Console.WriteLine($"Город:       {weather.Name}");
            Console.WriteLine($"страна:    {weather.Country}");
            Console.WriteLine($"Температура: {weather.Temp:F1}°C");
            Console.WriteLine($"Описание : {weather.Description}");
            Console.WriteLine($"Координаты: {selectedCity.Latitude:F4}, {selectedCity.Longitude:F4}");
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"\n Network error: {ex.Message}");
            Console.WriteLine("Please check your internet connection.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n Error: {ex.Message}");
        }
        
        Console.WriteLine("Press any key to return to menu...");
        Console.ReadKey();
    }
}
