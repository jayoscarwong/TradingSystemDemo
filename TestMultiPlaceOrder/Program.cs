using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddEnvironmentVariables(prefix: "TS_LOADTEST_")
    .Build();

var settings = configuration.GetSection("TestMultiPlaceOrder").Get<LoadTestSettings>()
    ?? throw new InvalidOperationException("Missing TestMultiPlaceOrder configuration.");

settings.Validate();

using var httpClient = new HttpClient
{
    Timeout = TimeSpan.FromSeconds(30)
};

var runner = new LoadTestRunner(httpClient, settings);
await runner.RunAsync();

internal sealed class LoadTestRunner
{
    private readonly HttpClient _httpClient;
    private readonly LoadTestSettings _settings;
    private readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public LoadTestRunner(HttpClient httpClient, LoadTestSettings settings)
    {
        _httpClient = httpClient;
        _settings = settings;
    }

    public async Task RunAsync()
    {
        Console.WriteLine("Authenticating administrator...");
        var adminToken = await LoginAsync(_settings.AdminUsername, _settings.AdminPassword);

        Console.WriteLine($"Registering {_settings.TraderCount} trader accounts and {_settings.VisitorCount} visitor accounts...");
        var traders = await RegisterAccountsAsync("Traders", _settings.TraderCount);
        var visitors = await RegisterAccountsAsync("Visitors", _settings.VisitorCount);

        Console.WriteLine("Enabling all generated accounts through the admin approval flow...");
        foreach (var account in traders.Concat(visitors))
        {
            await EnableAccountAsync(adminToken, account.AccountId);
        }

        Console.WriteLine("Authenticating generated accounts...");
        var authenticatedTraders = await LoginAccountsAsync(traders);
        var authenticatedVisitors = await LoginAccountsAsync(visitors);

        Console.WriteLine("Running concurrent order placement and read-only traffic...");
        var placeOrdersTask = RunTraderOrdersAsync(authenticatedTraders);
        var readTrafficTask = RunVisitorReadsAsync(authenticatedVisitors);

        await Task.WhenAll(placeOrdersTask, readTrafficTask);

        var orderSummary = placeOrdersTask.Result;
        var readSummary = readTrafficTask.Result;

        Console.WriteLine();
        Console.WriteLine("Load test complete.");
        Console.WriteLine($"Orders placed: {orderSummary.SuccessCount} succeeded, {orderSummary.FailureCount} failed.");
        Console.WriteLine($"Visitor reads: {readSummary.SuccessCount} succeeded, {readSummary.FailureCount} failed.");
    }

    private async Task<List<GeneratedAccount>> RegisterAccountsAsync(string groupName, int count)
    {
        var accounts = new List<GeneratedAccount>(count);

        for (var index = 0; index < count; index++)
        {
            var uniqueSuffix = $"{groupName[..3].ToLowerInvariant()}{DateTime.UtcNow:yyyyMMddHHmmss}{index:D2}{Random.Shared.Next(1000, 9999)}";
            var username = $"{groupName[..3].ToLowerInvariant()}_{uniqueSuffix}";

            var payload = new
            {
                name = $"{groupName} Demo {index + 1}",
                username,
                email = $"{username}@loadtest.tradingsystem.local",
                password = _settings.DefaultUserPassword,
                requestedGroup = groupName
            };

            using var response = await _httpClient.PostAsJsonAsync($"{_settings.AuthBaseUrl}/api/TradeAccounts/register", payload);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Registration failed for {username}: {(int)response.StatusCode} {body}");
            }

            var parsed = JsonSerializer.Deserialize<RegistrationEnvelope>(body, _serializerOptions)
                ?? throw new InvalidOperationException($"Unable to parse registration response for {username}.");

            if (parsed.Account?.Id == null)
            {
                throw new InvalidOperationException($"Registration response for {username} did not contain account details.");
            }

            accounts.Add(new GeneratedAccount(parsed.Account.Id.Value, username, _settings.DefaultUserPassword, groupName));
        }

        return accounts;
    }

    private async Task EnableAccountAsync(string adminToken, long accountId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"{_settings.AuthBaseUrl}/api/TradeAccounts/{accountId}/enable");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        using var response = await _httpClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Enable account {accountId} failed: {(int)response.StatusCode} {body}");
        }
    }

    private async Task<List<AuthenticatedAccount>> LoginAccountsAsync(IEnumerable<GeneratedAccount> accounts)
    {
        var authenticated = new List<AuthenticatedAccount>();
        foreach (var account in accounts)
        {
            var token = await LoginAsync(account.Username, account.Password);
            authenticated.Add(new AuthenticatedAccount(account.AccountId, account.Username, account.GroupName, token));
        }

        return authenticated;
    }

    private async Task<string> LoginAsync(string username, string password)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            $"{_settings.AuthBaseUrl}/api/Auth/login",
            new
            {
                username,
                password
            });

        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Login failed for {username}: {(int)response.StatusCode} {body}");
        }

        var payload = JsonSerializer.Deserialize<LoginResponse>(body, _serializerOptions)
            ?? throw new InvalidOperationException($"Unable to parse login response for {username}.");

        if (string.IsNullOrWhiteSpace(payload.AccessToken))
        {
            throw new InvalidOperationException($"Login response for {username} did not include an access token.");
        }

        return payload.AccessToken;
    }

    private async Task<ExecutionSummary> RunTraderOrdersAsync(IReadOnlyCollection<AuthenticatedAccount> traders)
    {
        var successCount = 0;
        var failureCount = 0;

        await Parallel.ForEachAsync(
            traders,
            new ParallelOptions { MaxDegreeOfParallelism = _settings.MaxParallelism },
            async (trader, cancellationToken) =>
            {
                for (var index = 0; index < _settings.OrdersPerTrader; index++)
                {
                    try
                    {
                        var ticker = _settings.Tickers[Random.Shared.Next(_settings.Tickers.Length)];
                        var payload = new
                        {
                            orderId = Guid.NewGuid(),
                            stockTicker = ticker,
                            bidAmount = Math.Round(Random.Shared.NextDouble() * 250 + 25, 4),
                            volume = Math.Round(Random.Shared.NextDouble() * 25 + 1, 4),
                            isBuy = Random.Shared.Next(0, 2) == 0,
                            serverId = Random.Shared.Next(1, 4)
                        };

                        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_settings.ApiBaseUrl}/api/Trades");
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", trader.AccessToken);
                        request.Content = JsonContent.Create(payload);

                        using var response = await _httpClient.SendAsync(request, cancellationToken);
                        if (response.IsSuccessStatusCode)
                        {
                            Interlocked.Increment(ref successCount);
                        }
                        else
                        {
                            Interlocked.Increment(ref failureCount);
                            Console.WriteLine($"Order request failed for {trader.Username}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(cancellationToken)}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref failureCount);
                        Console.WriteLine($"Order exception for {trader.Username}: {ex.Message}");
                    }
                }
            });

        return new ExecutionSummary(successCount, failureCount);
    }

    private async Task<ExecutionSummary> RunVisitorReadsAsync(IReadOnlyCollection<AuthenticatedAccount> visitors)
    {
        var successCount = 0;
        var failureCount = 0;

        await Parallel.ForEachAsync(
            visitors,
            new ParallelOptions { MaxDegreeOfParallelism = _settings.MaxParallelism },
            async (visitor, cancellationToken) =>
            {
                for (var index = 0; index < _settings.PriceReadsPerVisitor; index++)
                {
                    try
                    {
                        var ticker = _settings.Tickers[Random.Shared.Next(_settings.Tickers.Length)];

                        using var priceRequest = new HttpRequestMessage(HttpMethod.Get, $"{_settings.ApiBaseUrl}/api/Trades/price/{ticker}");
                        priceRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", visitor.AccessToken);
                        using var priceResponse = await _httpClient.SendAsync(priceRequest, cancellationToken);

                        using var monitoringRequest = new HttpRequestMessage(HttpMethod.Get, $"{_settings.ApiBaseUrl}/api/Tasks/monitoring/overview");
                        monitoringRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", visitor.AccessToken);
                        using var monitoringResponse = await _httpClient.SendAsync(monitoringRequest, cancellationToken);

                        if (priceResponse.IsSuccessStatusCode && monitoringResponse.IsSuccessStatusCode)
                        {
                            Interlocked.Increment(ref successCount);
                        }
                        else
                        {
                            Interlocked.Increment(ref failureCount);
                            Console.WriteLine($"Read request failed for {visitor.Username}: price={(int)priceResponse.StatusCode}, monitoring={(int)monitoringResponse.StatusCode}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref failureCount);
                        Console.WriteLine($"Read exception for {visitor.Username}: {ex.Message}");
                    }
                }
            });

        return new ExecutionSummary(successCount, failureCount);
    }
}

internal sealed class LoadTestSettings
{
    public string AuthBaseUrl { get; set; } = "http://localhost:8081";
    public string ApiBaseUrl { get; set; } = "http://localhost:8080";
    public string AdminUsername { get; set; } = "admin";
    public string AdminPassword { get; set; } = "Admin123!ChangeMe";
    public string DefaultUserPassword { get; set; } = "Trader123!";
    public int TraderCount { get; set; } = 5;
    public int VisitorCount { get; set; } = 5;
    public int OrdersPerTrader { get; set; } = 25;
    public int PriceReadsPerVisitor { get; set; } = 20;
    public int MaxParallelism { get; set; } = 10;
    public string[] Tickers { get; set; } = Array.Empty<string>();

    public void Validate()
    {
        if (TraderCount < 1)
        {
            throw new InvalidOperationException("TraderCount must be at least 1.");
        }

        if (VisitorCount < 1)
        {
            throw new InvalidOperationException("VisitorCount must be at least 1.");
        }

        if (MaxParallelism < 1)
        {
            throw new InvalidOperationException("MaxParallelism must be at least 1.");
        }

        if (Tickers.Length == 0)
        {
            throw new InvalidOperationException("At least one ticker must be configured.");
        }
    }
}

internal sealed record GeneratedAccount(long AccountId, string Username, string Password, string GroupName);

internal sealed record AuthenticatedAccount(long AccountId, string Username, string GroupName, string AccessToken);

internal sealed record ExecutionSummary(int SuccessCount, int FailureCount);

internal sealed class LoginResponse
{
    public string? AccessToken { get; set; }
}

internal sealed class RegistrationEnvelope
{
    public RegistrationAccount? Account { get; set; }
}

internal sealed class RegistrationAccount
{
    public long? Id { get; set; }
}
