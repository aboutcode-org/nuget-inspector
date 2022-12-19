# Sample-ASP.NET-project

Steps to run this ASP.NET project

1. cd Sample.Web && npm install
2. Create connectionString with name "DbContext" in ConnectionStrings.config in Sample.WebApi

```xml
<connectionStrings>
    <add name="DbContext" providerName="System.Data.SqlClient" connectionString="Server=tcp:your-server-url.com,1433;Database=sample;User ID=sampleAdmin@yourCompany;Password=1234;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;" />
</connectionStrings>
```

3. Project must have multiple startup projects (Solution Properties -> Common Properties -> Multiple startup projects: Sample.Web and Sample.WebApi
4. Run project
