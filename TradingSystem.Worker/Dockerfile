# 1. Update the SDK to 10.0
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy ALL .csproj files to their respective folders
#COPY ["TradingSystem.Api/TradingSystem.Api.csproj", "TradingSystem.Api/"]
COPY ["TradingSystem.Application/TradingSystem.Application.csproj", "TradingSystem.Application/"]
COPY ["TradingSystem.Domain/TradingSystem.Domain.csproj", "TradingSystem.Domain/"]
COPY ["TradingSystem.Infrastructure/TradingSystem.Infrastructure.csproj", "TradingSystem.Infrastructure/"]
COPY "TradingSystem.Worker/TradingSystem.Worker.csproj" "TradingSystem.Worker/"
RUN dotnet restore "TradingSystem.Worker/TradingSystem.Worker.csproj"

# Copy everything else and build
COPY . .
WORKDIR "/src/TradingSystem.Worker"
RUN dotnet publish "TradingSystem.Worker.csproj" -c Release -o /app/publish

# 2. Update the Runtime to 10.0
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# The fix: Note the space before the final dot
COPY --from=build /app/publish .

# Don't forget to point to your actual DLL
ENTRYPOINT ["dotnet", "TradingSystem.Worker.dll"]