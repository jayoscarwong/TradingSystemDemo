FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["TradingSystem.Auth/TradingSystem.Auth.csproj", "TradingSystem.Auth/"]
COPY ["TradingSystem.Domain/TradingSystem.Domain.csproj", "TradingSystem.Domain/"]
COPY ["TradingSystem.Application/TradingSystem.Application.csproj", "TradingSystem.Application/"]
COPY ["TradingSystem.Infrastructure/TradingSystem.Infrastructure.csproj", "TradingSystem.Infrastructure/"]

RUN dotnet restore "TradingSystem.Auth/TradingSystem.Auth.csproj"

COPY . .
WORKDIR "/src/TradingSystem.Auth"
RUN dotnet publish "TradingSystem.Auth.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "TradingSystem.Auth.dll"]
