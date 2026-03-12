# 1. Update the SDK to 10.0
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy ALL .csproj files to their respective folders
COPY ["TradingSystem.Api/TradingSystem.Api.csproj", "TradingSystem.Api/"]
COPY ["TradingSystem.Application/TradingSystem.Application.csproj", "TradingSystem.Application/"]
COPY ["TradingSystem.Domain/TradingSystem.Domain.csproj", "TradingSystem.Domain/"]
COPY ["TradingSystem.Infrastructure/TradingSystem.Infrastructure.csproj", "TradingSystem.Infrastructure/"]

# Restore the API 
RUN dotnet restore "TradingSystem.Api/TradingSystem.Api.csproj"

# Copy the entire source tree
COPY . .

# Build and Publish
WORKDIR "/src/TradingSystem.Api"
RUN dotnet publish "TradingSystem.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# 2. Update the Runtime to 10.0
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "TradingSystem.Api.dll"]