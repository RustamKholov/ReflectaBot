FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 5211

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["ReflectaBot.csproj", "."]
RUN dotnet restore "ReflectaBot.csproj"
COPY . .
WORKDIR "/src"
RUN dotnet build "ReflectaBot.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "ReflectaBot.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

COPY .env* ./

ENTRYPOINT ["dotnet", "ReflectaBot.dll"]
