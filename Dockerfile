FROM ://mcr.microsoft.com AS base
WORKDIR /app
EXPOSE 5040

# Указываем приложению слушать нужный порт
ENV ASPNETCORE_URLS=http://+:5040

FROM ://mcr.microsoft.com AS build
ARG configuration=Release
WORKDIR /src

# Копируем файл проекта и восстанавливаем зависимости
# Файл копируется прямо в /src/
COPY ["TinyCityCardGame_online.csproj", "./"]
RUN dotnet restore "TinyCityCardGame_online.csproj"

# Копируем остальные файлы и собираем проект
COPY . .
RUN dotnet build "TinyCityCardGame_online.csproj" -c $configuration -o /app/build

FROM build AS publish
ARG configuration=Release
RUN dotnet publish "TinyCityCardGame_online.csproj" -c $configuration -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "TinyCityCardGame_online.dll"]
