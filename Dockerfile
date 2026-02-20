FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 5040

# Принудительно заставляем приложение слушать 5040 внутри контейнера
ENV ASPNETCORE_URLS=http://+:5040

# Стадия сборки
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG configuration=Release
WORKDIR /src

# Копируем .csproj из подпапки в корень /src внутри докера
COPY ["TinyCityCardGame_online.csproj", "./"]
RUN dotnet restore "TinyCityCardGame_online.csproj"

# Копируем всё содержимое корня (включая все подпапки)
COPY . .
WORKDIR "/src/TinyCityCardGame_online"
RUN dotnet build "TinyCityCardGame_online.csproj" -c $configuration -o /app/build

# Стадия публикации
FROM build AS publish
ARG configuration=Release
RUN dotnet publish "TinyCityCardGame_online.csproj" -c $configuration -o /app/publish /p:UseAppHost=false

# Финальный образ
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "TinyCityCardGame_online.dll"]
