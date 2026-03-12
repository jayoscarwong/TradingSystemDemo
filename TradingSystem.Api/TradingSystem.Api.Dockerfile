FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# 1. Copy ALL .csproj files to their respective folders
# We keep the names exactly as they appear in your screenshot
COPY ["TradingSystem.Api/TradingSystem.Api.csproj", "TradingSystem.Api/"]
COPY ["TradingSystem.Application/TradingSystem.Application.csproj", "TradingSystem.Application/"]
COPY ["TradingSystem.Domain/TradingSystem.Domain.csproj", "TradingSystem.Domain/"]
COPY ["TradingSystem.Infrastructure/TradingSystem.Infrastructure.csproj", "TradingSystem.Infrastructure/"]

# 2. Restore the API (this will now find the other .csproj files correctly)
RUN dotnet restore "TradingSystem.Api/TradingSystem.Api.csproj"

# 3. Copy the entire source tree
COPY . .

# 4. Build and Publish
WORKDIR "/src/TradingSystem.Api"
RUN dotnet publish "TradingSystem.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# --- Runtime Stage ---
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "TradingSystem.Api.dll"]