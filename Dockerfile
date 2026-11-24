FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src


COPY CampusLink.sln global.json ./
COPY */*.csproj ./
RUN for f in *.csproj; do mkdir -p "${f%.*}" && mv "$f" "${f%.*}/"; done
RUN dotnet restore CampusLink.sln

COPY . .
RUN dotnet publish CampusLink.Api/CampusLink.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "CampusLink.Api.dll"]