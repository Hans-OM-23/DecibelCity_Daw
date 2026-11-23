using System.Net;
using DecibelCity.Api.Errors;
using DecibelCity.Api.Repositories;
using DecibelCity.Api.Services.Security;
using DecibelCity.Api.Tests.Infrastructure;

namespace DecibelCity.Api.Tests.Postgres;

/// <summary>
/// Esquema v5 real, catálogos INEI, cuentas, sesiones y reportes sobre PostgreSQL.
/// Requiere <c>DECIBELCITY_TEST_DATABASE_URL</c> (base exclusiva terminada en <c>_test</c>).
/// </summary>
[Trait("Category", "PostgreSQL")]
public sealed class PostgresIntegrationTests
{
    [Fact]
    public async Task Initialize_CreatesSchemaV5WithElevenTablesAndJuninCatalog()
    {
        await using var db = await PostgresSchemaScope.CreateOrSkipAsync();
        using var factory = db.CreateFactory();
        using var client = factory.CreateBrowserClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/health", TestContext.Current.CancellationToken)).StatusCode);
        var tables = (await db.RowsAsync("""
            SELECT table_name FROM information_schema.tables
            WHERE table_schema = current_schema() AND table_type = 'BASE TABLE'
            """)).Select(r => (string)r[0]!).ToHashSet();
        Assert.Equal(new HashSet<string>(PostgresDatabaseInitializer.DomainTables) { "decibelcity_schema" }, tables);
        Assert.Equal(5, await db.ScalarAsync<int>("SELECT version FROM decibelcity_schema"));

        var catalog = await (await client.GetAsync("/api/territories", TestContext.Current.CancellationToken)).JsonAsync();
        Assert.Equal("""[{"id_departamento":1,"ubigeo":"12","nombre_departamento":"Junín"}]""", catalog.GetProperty("departamentos").GetRawText());
        Assert.Equal(9, catalog.GetProperty("provincias").GetArrayLength());
        var districts = catalog.GetProperty("distritos").EnumerateArray().ToList();
        Assert.Equal(124, districts.Count);
        var perProvince = districts.GroupBy(d => d.GetProperty("ubigeo").GetString()![..4]).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(new Dictionary<string, int>
        {
            ["1201"] = 28, ["1202"] = 15, ["1203"] = 6, ["1204"] = 34, ["1205"] = 4, ["1206"] = 9, ["1207"] = 9, ["1208"] = 10, ["1209"] = 9,
        }, perProvince);
        var names = districts.ToDictionary(d => d.GetProperty("ubigeo").GetString()!, d => d.GetProperty("nombre_distrito").GetString());
        Assert.Equal("Huancán", names["120119"]);
        Assert.Equal("Santa Bárbara de Carhuacayán", names["120807"]);
        Assert.Equal("San Pedro de Chunán", names["120429"]);
        Assert.Equal(1, await db.ScalarAsync<int>("SELECT id_provincia FROM provincias WHERE ubigeo = '1201'"));

        var links = await db.RowsAsync("""
            SELECT p.id_punto, d.ubigeo FROM puntos_criticos p JOIN distritos d USING (id_distrito) ORDER BY p.id_punto
            """);
        Assert.Equal(["120101", "120101", "120107"], links.Select(r => (string)r[1]!));
        var hotspots = await (await client.GetAsync("/api/hotspots", TestContext.Current.CancellationToken)).JsonAsync();
        Assert.Equal([74d, 78d, 69d], hotspots.EnumerateArray().Select(h => h.GetProperty("decibels").GetDouble()));
        Assert.Equal(5, (await (await client.GetAsync("/api/noise-sources", TestContext.Current.CancellationToken)).JsonAsync()).GetArrayLength());
    }

    [Fact]
    public async Task Initialize_IsIdempotentAndPreservesCustomizations()
    {
        await using var db = await PostgresSchemaScope.CreateOrSkipAsync();
        using (var first = db.CreateFactory())
        {
            first.CreateBrowserClient().Dispose();
        }

        await db.ExecuteAsync("UPDATE distritos SET nombre_distrito = 'Chilca (personalizado)', activo = FALSE WHERE ubigeo = '120107'");
        await db.ExecuteAsync("UPDATE roles_usuario SET descripcion = 'Descripción propia' WHERE nombre_rol = 'ciudadano'");

        using (var second = db.CreateFactory())
        {
            second.CreateBrowserClient().Dispose();
        }

        Assert.Equal(124L, await db.ScalarAsync<long>("SELECT COUNT(*) FROM distritos"));
        Assert.Equal(3L, await db.ScalarAsync<long>("SELECT COUNT(*) FROM puntos_criticos"));
        Assert.Equal(1L, await db.ScalarAsync<long>("SELECT COUNT(*) FROM roles_usuario"));
        var chilca = (await db.RowsAsync("SELECT nombre_distrito, activo FROM distritos WHERE ubigeo = '120107'")).Single();
        Assert.Equal("Chilca (personalizado)", chilca[0]);
        Assert.Equal(false, chilca[1]);
        Assert.Equal("Descripción propia", await db.ScalarAsync<string>("SELECT descripcion FROM roles_usuario"));
    }

    [Fact]
    public async Task Initialize_UpgradesV4InPlaceWithoutLosingData()
    {
        await using var db = await PostgresSchemaScope.CreateOrSkipAsync();
        using (var first = db.CreateFactory())
        {
            first.CreateBrowserClient().Dispose();
        }

        // Estructura v4: idéntica a v5 sin la tabla usuarios.
        await db.ExecuteAsync("DROP TABLE usuarios; UPDATE decibelcity_schema SET version = 4");

        using (var upgraded = db.CreateFactory())
        {
            upgraded.CreateBrowserClient().Dispose();
        }

        Assert.Equal(5, await db.ScalarAsync<int>("SELECT version FROM decibelcity_schema"));
        Assert.Equal(0L, await db.ScalarAsync<long>("SELECT COUNT(*) FROM usuarios"));
        Assert.Equal(3L, await db.ScalarAsync<long>("SELECT COUNT(*) FROM puntos_criticos"));
    }

    [Fact]
    public async Task Initialize_RejectsUnknownTables()
    {
        await using var db = await PostgresSchemaScope.CreateOrSkipAsync();
        await db.ExecuteAsync("CREATE TABLE ajena (id INTEGER)");
        using var factory = db.CreateFactory();

        var error = Assert.ThrowsAny<Exception>(() => factory.CreateBrowserClient());
        Assert.IsType<DatabaseMigrationException>(error.GetBaseException(), exactMatch: false);
    }

    [Fact]
    public async Task AccountAndReportFlow_PersistsOnlyDistrictAndOsmPlace()
    {
        await using var db = await PostgresSchemaScope.CreateOrSkipAsync();
        using var factory = db.CreateFactory();
        using var client = factory.CreateBrowserClient();

        var profile = await client.RegisterAsync();
        Assert.Equal("Huancayo", profile.GetProperty("nombre_distrito").GetString());
        Assert.Equal("Junín", profile.GetProperty("nombre_departamento").GetString());
        Assert.Equal("ciudadano", profile.GetProperty("rol").GetString());
        Assert.StartsWith("$argon2id$v=19$", await db.ScalarAsync<string>("SELECT password_hash FROM usuarios"), StringComparison.Ordinal);

        var userColumns = (await db.RowsAsync("""
            SELECT column_name FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = 'usuarios'
            """)).Select(r => (string)r[0]!).ToHashSet();
        Assert.DoesNotContain("id_provincia", userColumns);
        Assert.DoesNotContain("id_departamento", userColumns);

        var duplicate = await client.PostJsonAsync("/api/auth/register", TestData.Registration("ANA.QUISPE"));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostJsonAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostJsonAsync("/api/auth/login", TestData.Credentials(" Ana.Quispe "))).StatusCode);

        var response = await client.PostJsonAsync("/api/reports", TestData.Report());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var receipt = await response.JsonAsync();

        var stored = (await db.RowsAsync($"""
            SELECT r.ubicacion_heredada, f.codigo, r.descripcion, r.nivel_estimado_db, e.codigo_estado, r.id_lugar,
                   r.proveedor_lugar, r.es_historico, r.fecha_hora
            FROM reportes_ciudadanos r JOIN tipos_fuente_ruido f USING (id_tipo_fuente) JOIN estados_reporte e USING (id_estado)
            WHERE r.id_reporte = '{receipt.GetProperty("id").GetString()}'
            """)).Single();
        Assert.Null(stored[0]);
        Assert.Equal("traffic", stored[1]);
        Assert.Equal(74.0, stored[3]);
        Assert.Equal("received", stored[4]);
        Assert.Equal("W813046785", stored[5]);
        Assert.Equal("osm", stored[6]);
        Assert.Equal(false, stored[7]);
        var storedAt = new DateTimeOffset(DateTime.SpecifyKind((DateTime)stored[8]!, DateTimeKind.Utc));
        Assert.Equal(receipt.GetProperty("created_at").GetDateTimeOffset(), storedAt);

        var reportColumns = (await db.RowsAsync("""
            SELECT column_name FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = 'reportes_ciudadanos'
            """)).Select(r => (string)r[0]!);
        Assert.DoesNotContain(reportColumns, c => c.Contains("usuario", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Register_RejectsInactiveHierarchyAndMismatchedSelectors()
    {
        await using var db = await PostgresSchemaScope.CreateOrSkipAsync();
        using var factory = db.CreateFactory();
        using var client = factory.CreateBrowserClient();

        var chupaca = TestData.Registration();
        chupaca["id_distrito"] = await db.ScalarAsync<int>("SELECT id_distrito FROM distritos WHERE ubigeo = '120906'");
        chupaca["id_provincia"] = 1; // Huancayo: no corresponde.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostJsonAsync("/api/auth/register", chupaca)).StatusCode);

        await db.ExecuteAsync("UPDATE provincias SET activo = FALSE WHERE ubigeo = '1201'");
        var catalog = await (await client.GetAsync("/api/territories", TestContext.Current.CancellationToken)).JsonAsync();
        Assert.Equal(124 - 28, catalog.GetProperty("distritos").GetArrayLength());
        var response = await client.PostJsonAsync("/api/auth/register", TestData.Registration());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(0L, await db.ScalarAsync<long>("SELECT COUNT(*) FROM usuarios"));

        await db.ExecuteAsync("UPDATE provincias SET activo = TRUE; UPDATE roles_usuario SET activo = FALSE");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostJsonAsync("/api/auth/register", TestData.Registration())).StatusCode);
    }

    [Fact]
    public async Task Login_WithArgonCffiHashInDatabase_RehashesWeakParameters()
    {
        await using var db = await PostgresSchemaScope.CreateOrSkipAsync();
        using var factory = db.CreateFactory(productionArgon2: true);
        using var client = factory.CreateBrowserClient();
        await db.ExecuteAsync($"""
            INSERT INTO usuarios (id_rol, id_distrito, nombres, apellidos, telefono, direccion, nombre_usuario, password_hash)
            VALUES ((SELECT id_rol FROM roles_usuario WHERE nombre_rol = 'ciudadano'),
                    (SELECT id_distrito FROM distritos WHERE ubigeo = '120101'),
                    'Ana', 'Quispe', '964123456', 'Jr. Ica 123', 'python.debil', '{TestData.ArgonCffiWeakHash}')
            """);

        var response = await client.PostJsonAsync("/api/auth/login", TestData.Credentials("python.debil"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rehashed = await db.ScalarAsync<string>("SELECT password_hash FROM usuarios");
        Assert.StartsWith("$argon2id$v=19$m=65536,t=3,p=4$", rehashed, StringComparison.Ordinal);
        Assert.True(new Argon2PasswordHasher().Verify(rehashed, TestData.Password));
    }

    [Fact]
    public async Task DisabledAccount_CannotUseExistingSessionOrReport()
    {
        await using var db = await PostgresSchemaScope.CreateOrSkipAsync();
        using var factory = db.CreateFactory();
        using var client = factory.CreateBrowserClient();
        await client.RegisterAsync();

        await db.ExecuteAsync("UPDATE usuarios SET activo = FALSE");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/auth/session", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostJsonAsync("/api/reports", TestData.Report())).StatusCode);
        Assert.Equal(0L, await db.ScalarAsync<long>("SELECT COUNT(*) FROM reportes_ciudadanos"));
        Assert.Empty(factory.Nominatim.Requests);
    }

    [Fact]
    public async Task SessionSurvivesProfileChangesButNotRestart()
    {
        await using var db = await PostgresSchemaScope.CreateOrSkipAsync();
        string token;
        using (var factory = db.CreateFactory())
        using (var client = factory.CreateBrowserClient())
        {
            await client.RegisterAsync();
            await db.ExecuteAsync("UPDATE usuarios SET nombres = 'Nombre actualizado'");
            var session = await (await client.GetAsync("/api/auth/session", TestContext.Current.CancellationToken)).JsonAsync();
            Assert.Equal("Nombre actualizado", session.GetProperty("nombres").GetString());
            token = factory.Sessions.Create(1);
        }

        using var restarted = db.CreateFactory();
        Assert.Null(restarted.Sessions.Resolve(token));
    }
}
