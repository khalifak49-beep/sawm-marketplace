# ─── مرحلة البناء ───────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore src/Sawm.Web/Sawm.Web.csproj
RUN dotnet publish src/Sawm.Web/Sawm.Web.csproj -c Release -o /app /p:UseAppHost=false

# ─── مرحلة التشغيل ──────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# قاعدة بيانات SQLite خفيفة (لا تحتاج SQL Server في السحابة)
ENV DB_PROVIDER=sqlite

# يستمع على المنفذ الذي توفّره منصة الاستضافة ($PORT) أو 10000 افتراضياً
CMD ["sh", "-c", "ASPNETCORE_URLS=http://0.0.0.0:${PORT:-10000} dotnet Sawm.Web.dll"]
