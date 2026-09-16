# Builds the backend into a container image. Docker builds this file
# top-to-bottom, one layer per instruction - ordering matters for caching: we
# restore NuGet packages from just the .csproj file BEFORE copying the rest
# of the source, so Docker can reuse the (slow) restore layer on rebuilds as
# long as the .csproj itself hasn't changed.
#
# For local development (this is what docker-compose.yml uses, bind-mounting
# the source over the top so edits show up without rebuilding), we use the
# full SDK image and run `dotnet watch` so the app rebuilds and restarts
# itself whenever a .cs file changes, giving a live-reload dev experience. A
# production image would instead multi-stage build down to the much smaller
# `aspnet` runtime-only image, which has no compiler and can only run an
# already-published app.
FROM mcr.microsoft.com/dotnet/sdk:8.0

WORKDIR /app

COPY IncidentFlow.Api/IncidentFlow.Api.csproj IncidentFlow.Api/
RUN dotnet restore IncidentFlow.Api/IncidentFlow.Api.csproj

COPY . .

EXPOSE 8000

# Binds Kestrel (ASP.NET Core's built-in web server) to all network
# interfaces on port 8000, not just localhost - required so the port mapping
# in docker-compose.yml can actually reach it from outside the container.
ENV ASPNETCORE_URLS=http://+:8000

CMD ["dotnet", "watch", "--project", "IncidentFlow.Api", "run", "--no-launch-profile"]
