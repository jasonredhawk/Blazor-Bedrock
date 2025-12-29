# Use the official .NET 9.0 SDK image for building
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies
COPY ["Blazor Bedrock.csproj", "./"]
RUN dotnet restore "Blazor Bedrock.csproj"

# Copy everything else and build
COPY . .
RUN dotnet build "Blazor Bedrock.csproj" -c Release -o /app/build

# Publish the app
FROM build AS publish
RUN dotnet publish "Blazor Bedrock.csproj" -c Release -o /app/publish

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

# Copy published app
COPY --from=publish /app/publish .

# Expose port (Cloud Run uses PORT environment variable)
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Blazor Bedrock.dll"]

