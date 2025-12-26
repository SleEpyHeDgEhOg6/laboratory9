 using System;
 using System.Collections.Generic;
 using System.IO;
 using System.Linq;
 using System.Net.Http;
 using System.Text.Json;
 using System.Text.Json.Serialization;
 using System.Threading;
 using System.Threading.Tasks;
 
 public class StockCandleResponse
 {
     [JsonPropertyName("s")]
     public string Status { get; set; }
     
     [JsonPropertyName("c")]
     public decimal[] Close { get; set; }
     
     [JsonPropertyName("h")]
     public decimal[] High { get; set; }
     
     [JsonPropertyName("l")]
     public decimal[] Low { get; set; }
     
     [JsonPropertyName("o")]
     public decimal[] Open { get; set; }
     
     [JsonPropertyName("t")]
     public long[] Timestamp { get; set; }
     
     [JsonPropertyName("v")]
     public long[] Volume { get; set; }
 }
 
 public class StockPriceResult
 {
     public string Ticker { get; set; }
     public decimal AveragePrice { get; set; }
     public bool Success { get; set; }
     public string ErrorMessage { get; set; }
 }
 
 public class StockDataService : IDisposable  //сервис данных
 {
     private readonly HttpClient _httpClient;
     private readonly string _apiToken;
     
     public StockDataService(string apiToken)
     {
         _httpClient = new HttpClient();
         _apiToken = apiToken;
         _httpClient.Timeout = TimeSpan.FromSeconds(30);
     }
     
     public async Task<StockCandleResponse?> GetStockDataAsync(string ticker, DateTime fromDate, DateTime toDate)
     {
         try
         {
             string from = fromDate.ToString("yyyy-MM-dd"); //преобразуем даты 
             string to = toDate.ToString("yyyy-MM-dd");
             
             string url = $"https://api.marketdata.app/v1/stocks/candles/D/{ticker}/?from={from}&to={to}&token={_apiToken}";
             
             HttpResponseMessage response = await _httpClient.GetAsync(url); //выполняем запрос 
             
             if (!response.IsSuccessStatusCode)
             {
                 Console.WriteLine($"HTTP error for {ticker}: {response.StatusCode}");
                 return null;
             }
             
             string json = await response.Content.ReadAsStringAsync(); //чтение ответа 
             
             if (string.IsNullOrWhiteSpace(json) || json.Contains("error")) //проверка содержимого json файла 
             {
                 Console.WriteLine($"Invalid response for {ticker}: {json}");
                 return null;
             }
             
             var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
             var result = JsonSerializer.Deserialize<StockCandleResponse>(json, options); //преобразуем json в объект с#
             
             if (result?.Status != "ok" || result.High == null || result.Low == null)
             {
                 Console.WriteLine($"Invalid data for {ticker}");
                 return null;
             }
             
             return result;
         }
         catch (Exception ex)
         {
             Console.WriteLine($"Error getting data for {ticker}: {ex.Message}");
             return null;
         }
     }
     
     public void Dispose() //освобождает ресурсы 
     {
         _httpClient?.Dispose();
     }
 }
 
 public class PriceCalculator  //средняя годовая цена 
 {
     public static decimal CalculateYearlyAverage(StockCandleResponse stockData)
     {
         if (stockData?.High == null || stockData.Low == null || 
             stockData.High.Length == 0 || stockData.Low.Length == 0)
             return 0;
         
         decimal totalSum = 0;
         int validDays = 0;
         
         for (int i = 0; i < stockData.High.Length; i++)
         {
             decimal dailyAverage = (stockData.High[i] + stockData.Low[i]) / 2;
             totalSum += dailyAverage;
             validDays++;
         }
         
         return validDays == 0 ? 0 : totalSum / validDays;
     }
 }
 
 public class ThreadSafeFileWriter : IDisposable //запись в файл из нескольких потоков 
 {
     private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
     private readonly string _filePath;
     
     public ThreadSafeFileWriter(string filePath)
     {
         _filePath = filePath;
         File.WriteAllText(_filePath, string.Empty);
     }
     
     public async Task WriteResultAsync(StockPriceResult result)
     {
         await _semaphore.WaitAsync();
         try
         {
             string line = $"{result.Ticker}:{result.AveragePrice:F4}";
             using (var writer = new StreamWriter(_filePath, true))
             {
                 await writer.WriteLineAsync(line);
             }
             Console.WriteLine($"Written: {line}");
         }
         finally
         {
             _semaphore.Release();
         }
     }
     
     public void Dispose()
     {
         _semaphore?.Dispose();
     }
 }
 
 public class StockPriceApplication : IDisposable //управляет всем процессом обработки тикеров, контролирует параллельное выполнение
 {
     private readonly StockDataService _stockService;
     private readonly ThreadSafeFileWriter _fileWriter;
     private readonly string _tickersFilePath;
     
     public StockPriceApplication(string apiToken, string outputFilePath, string tickersFilePath)
     {
         _stockService = new StockDataService(apiToken);
         _fileWriter = new ThreadSafeFileWriter(outputFilePath);
         _tickersFilePath = tickersFilePath;
     }
     
     public async Task ProcessStocksAsync(int maxConcurrentTasks = 3) //ограничивает параллельные запросы 
     {
         var tickers = await ReadTickersFromFileAsync();
         
         Console.WriteLine($"Found tickers: {tickers.Length}");
         Console.WriteLine($"Starting processing with {maxConcurrentTasks} concurrent tasks");
         
         using var semaphore = new SemaphoreSlim(maxConcurrentTasks, maxConcurrentTasks);
         var tasks = new List<Task>();
         
         foreach (var ticker in tickers)
         {
             await semaphore.WaitAsync();
             
             var task = Task.Run(async () =>
             {
                 try
                 {
                     await ProcessSingleStockAsync(ticker);
                 }
                 finally
                 {
                     semaphore.Release();
                 }
             });
             
             tasks.Add(task);
             await Task.Delay(100);
         }
         
         await Task.WhenAll(tasks);
         Console.WriteLine("All tickers processed!");
     }
     
     private async Task<string[]> ReadTickersFromFileAsync() //чтение файла
     {
         if (!File.Exists(_tickersFilePath))
             throw new FileNotFoundException($"Tickers file not found: {_tickersFilePath}");
         
         var lines = await File.ReadAllLinesAsync(_tickersFilePath);
         return lines
             .Where(line => !string.IsNullOrWhiteSpace(line))
             .Select(line => line.Trim())
             .ToArray();
     }
     
     private async Task ProcessSingleStockAsync(string ticker) //обработка одного тикера 
     {
         try
         {
             Console.WriteLine($"Processing: {ticker}");
             
             DateTime toDate = DateTime.Today;
             DateTime fromDate = toDate.AddDays(-360); 
             
             var stockData = await _stockService.GetStockDataAsync(ticker, fromDate, toDate); //получение данных
             
             if (stockData == null)
             {
                 await _fileWriter.WriteResultAsync(new StockPriceResult
                 {
                     Ticker = ticker,
                     AveragePrice = 0,
                     Success = false,
                     ErrorMessage = "No data"
                 });
                 return;
             }
             
             decimal averagePrice = PriceCalculator.CalculateYearlyAverage(stockData);
             
             await _fileWriter.WriteResultAsync(new StockPriceResult
             {
                 Ticker = ticker,
                 AveragePrice = averagePrice,
                 Success = true
             });
             
             Console.WriteLine($"Done: {ticker} - ${averagePrice:F4}");
         }
         catch (Exception ex)
         {
             Console.WriteLine($"Error with {ticker}: {ex.Message}");
             await _fileWriter.WriteResultAsync(new StockPriceResult
             {
                 Ticker = ticker,
                 AveragePrice = 0,
                 Success = false,
                 ErrorMessage = ex.Message
             });
         }
     }
     
     public void Dispose()
     {
         _stockService?.Dispose();
         _fileWriter?.Dispose();
     }
 }
 
 class Program
 {
     static async Task Main(string[] args)
     {
         string apiToken = "RnJjXzEzUXdnQ2c2bXc2LXh5akpVbTVOYXRQTFJkbmFyZE1SMmpMVVc4Yz0";
         string tickersFilePath = "/home/vlada/Рабочий стол/АЯ /ticker.txt";
         string outputFilePath = "stock_prices_result.txt";
         int maxConcurrentTasks = 3;
         
         Console.WriteLine("Starting stock price application...");
         Console.WriteLine($"Results will be saved to: {outputFilePath}");
         
         using var app = new StockPriceApplication(apiToken, outputFilePath, tickersFilePath); //создание и использование приложения 
         
         try
         {
             await app.ProcessStocksAsync(maxConcurrentTasks);
             Console.WriteLine($"Results saved to: {outputFilePath}");
 
             string fullPath = Path.GetFullPath(outputFilePath);
             Console.WriteLine($"Full path: {fullPath}");
         }
         catch (Exception ex)
         {
             Console.WriteLine($"Error: {ex.Message}");
         }
         
         Console.WriteLine("Press any key to exit...");
         Console.ReadKey();
     }
 }
 
 
 
 
