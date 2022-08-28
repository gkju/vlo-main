FROM mcr.microsoft.com/dotnet/aspnet:7.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src
COPY ["vlo-main/vlo-main.csproj", "vlo-main/"]
RUN dotnet restore "vlo-main/vlo-main.csproj"
COPY . .
WORKDIR "/src/vlo-main"
RUN dotnet build "vlo-main.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "vlo-main.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "vlo-main.dll"]
