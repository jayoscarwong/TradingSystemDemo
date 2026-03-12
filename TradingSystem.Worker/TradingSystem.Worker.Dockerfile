FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy the csproj and restore dependencies
# Note: I adjusted the destination to match the source folder name
COPY ["TradingSystem.Application/TradingSystem.Application.csproj", "TradingSystem.Application/"]
COPY ["TradingSystem.Domain/TradingSystem.Domain.csproj", "TradingSystem.Domain/"]
COPY ["TradingSystem.Infrastructure/TradingSystem.Infrastructure.csproj", "TradingSystem.Infrastructure/"]
COPY "TradingSystem.Worker/TradingSystem.Worker.csproj" "TradingSystem.Worker/"
RUN dotnet restore "TradingSystem.Worker/TradingSystem.Worker.csproj"

# Copy everything else and build
COPY . .
WORKDIR "/src/TradingSystem.Worker"
RUN dotnet publish "TradingSystem.Worker.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# The fix: Note the space before the final dot
COPY --from=build /app/publish .

# Don't forget to point to your actual DLL
ENTRYPOINT ["dotnet", "TradingSystem.Worker.dll"]