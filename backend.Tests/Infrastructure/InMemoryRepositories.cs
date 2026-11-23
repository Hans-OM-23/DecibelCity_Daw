using System.Collections.Concurrent;
using DecibelCity.Api.Models.Domain;
using DecibelCity.Api.Models.Dtos;
using DecibelCity.Api.Repositories;
using DecibelCity.Api.Services;

namespace DecibelCity.Api.Tests.Infrastructure;

/// <summary>Repositorio de cuentas en memoria con las mismas reglas de jerarquía, rol y unicidad que PostgreSQL.</summary>
public sealed class InMemoryUserRepository : IUserRepository
{
    private readonly Lock _gate = new();
    private readonly Dictionary<int, StoredUser> _users = [];
    private int _nextId = 1;

    /// <summary>Departamentos (id, ubigeo, nombre, activo).</summary>
    public List<(Departamento Item, bool Activo)> Departamentos { get; } = [(new Departamento(1, "12", "Junín"), true)];

    /// <summary>Provincias.</summary>
    public List<(Provincia Item, bool Activo)> Provincias { get; } =
    [
        (new Provincia(1, 1, "1201", "Huancayo"), true),
        (new Provincia(9, 1, "1209", "Chupaca"), true),
    ];

    /// <summary>Distritos.</summary>
    public List<(Distrito Item, bool Activo)> Distritos { get; } =
    [
        (new Distrito(1, 1, "120101", "Huancayo"), true),
        (new Distrito(5, 1, "120107", "Chilca"), true),
        (new Distrito(116, 9, "120901", "Chupaca"), true),
    ];

    /// <summary>Estado del rol base.</summary>
    public bool RoleActive { get; set; } = true;

    /// <summary>Número de cuentas.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _users.Count;
            }
        }
    }

    /// <summary>Hash almacenado de una cuenta.</summary>
    /// <param name="nombreUsuario">Usuario.</param>
    /// <returns>Hash.</returns>
    public string HashOf(string nombreUsuario)
    {
        lock (_gate)
        {
            return _users.Values.Single(u => u.Profile.NombreUsuario == nombreUsuario).PasswordHash;
        }
    }

    /// <summary>Inserta una cuenta directamente (p. ej., con un hash de argon2-cffi).</summary>
    /// <param name="nombreUsuario">Usuario.</param>
    /// <param name="passwordHash">Hash.</param>
    /// <returns>Identificador.</returns>
    public int Seed(string nombreUsuario, string passwordHash)
    {
        lock (_gate)
        {
            var id = _nextId++;
            _users[id] = new StoredUser(Profile(id, nombreUsuario, 1), passwordHash, true);
            return id;
        }
    }

    /// <summary>Activa o desactiva todas las cuentas.</summary>
    /// <param name="active">Estado.</param>
    public void SetAllUsersActive(bool active)
    {
        lock (_gate)
        {
            foreach (var key in _users.Keys.ToList())
            {
                _users[key] = _users[key] with { Activo = active };
            }
        }
    }

    /// <summary>Elimina todas las cuentas.</summary>
    public void DeleteAll()
    {
        lock (_gate)
        {
            _users.Clear();
        }
    }

    /// <inheritdoc />
    public Task<TerritoryCatalog> ListTerritoriesAsync(CancellationToken cancellationToken = default)
    {
        var deps = Departamentos.Where(d => d.Activo).Select(d => d.Item).ToList();
        var provs = Provincias.Where(p => p.Activo && deps.Any(d => d.IdDepartamento == p.Item.IdDepartamento)).Select(p => p.Item).ToList();
        var dists = Distritos.Where(d => d.Activo && provs.Any(p => p.IdProvincia == d.Item.IdProvincia)).Select(d => d.Item).ToList();
        return Task.FromResult(new TerritoryCatalog(deps, provs, dists));
    }

    /// <inheritdoc />
    public Task<CreateUserResult> CreateUserAsync(UserRegistration registration, string passwordHash, CancellationToken cancellationToken = default)
    {
        var district = Distritos.FirstOrDefault(d => d.Item.IdDistrito == registration.IdDistrito);
        var province = Provincias.FirstOrDefault(p => p.Item.IdProvincia == registration.IdProvincia);
        var department = Departamentos.FirstOrDefault(d => d.Item.IdDepartamento == registration.IdDepartamento);
        if (district.Item is null || province.Item is null || department.Item is null
            || district.Item.IdProvincia != province.Item.IdProvincia || province.Item.IdDepartamento != department.Item.IdDepartamento
            || !district.Activo || !province.Activo || !department.Activo)
        {
            return Task.FromResult(CreateUserResult.Failed(CreateUserStatus.DistrictUnavailable));
        }

        if (!RoleActive)
        {
            return Task.FromResult(CreateUserResult.Failed(CreateUserStatus.DefaultRoleUnavailable));
        }

        lock (_gate)
        {
            if (_users.Values.Any(u => string.Equals(u.Profile.NombreUsuario, registration.NombreUsuario, StringComparison.OrdinalIgnoreCase)))
            {
                return Task.FromResult(CreateUserResult.Failed(CreateUserStatus.UsernameTaken));
            }

            var id = _nextId++;
            var profile = Profile(id, registration.NombreUsuario, registration.IdDistrito) with
            {
                Nombres = registration.Nombres,
                Apellidos = registration.Apellidos,
                Telefono = registration.Telefono,
                Direccion = registration.Direccion,
            };
            _users[id] = new StoredUser(profile, passwordHash, true);
            return Task.FromResult(new CreateUserResult(CreateUserStatus.Created, profile));
        }
    }

    /// <inheritdoc />
    public Task<StoredCredentials?> FindCredentialsAsync(string nombreUsuario, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var user = _users.Values.FirstOrDefault(u => string.Equals(u.Profile.NombreUsuario, nombreUsuario, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(user is null ? null : new StoredCredentials(user.Profile.IdUsuario, user.PasswordHash, user.Activo));
        }
    }

    /// <inheritdoc />
    public Task UpdatePasswordHashAsync(int idUsuario, string passwordHash, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _users[idUsuario] = _users[idUsuario] with { PasswordHash = passwordHash };
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<ProfileWithStatus?> GetProfileAsync(int idUsuario, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_users.TryGetValue(idUsuario, out var user)
                ? new ProfileWithStatus(user.Profile, user.Activo, RoleActive)
                : null);
        }
    }

    private UserProfile Profile(int id, string nombreUsuario, int idDistrito)
    {
        var district = Distritos.First(d => d.Item.IdDistrito == idDistrito).Item;
        var province = Provincias.First(p => p.Item.IdProvincia == district.IdProvincia).Item;
        var department = Departamentos.First(d => d.Item.IdDepartamento == province.IdDepartamento).Item;
        return new UserProfile(id, nombreUsuario, "Ana María", "Quispe Rojas", "+51 964 123 456", "Jr. Ica 123, Huancayo",
            district.IdDistrito, district.NombreDistrito, province.IdProvincia, province.NombreProvincia,
            department.IdDepartamento, department.NombreDepartamento, "ciudadano",
            new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
    }

    private sealed record StoredUser(UserProfile Profile, string PasswordHash, bool Activo);
}

/// <summary>Repositorio de puntos y reportes en memoria.</summary>
public sealed class InMemoryNoiseRepository : INoiseRepository
{
    /// <summary>Reportes guardados.</summary>
    public ConcurrentQueue<NoiseReport> Reports { get; } = new();

    /// <inheritdoc />
    public Task<IReadOnlyList<Hotspot>> ListHotspotsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(NoiseCatalog.DemoHotspots);

    /// <inheritdoc />
    public Task<IReadOnlyList<NoiseSourceOption>> ListSourcesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(NoiseCatalog.Sources);

    /// <inheritdoc />
    public Task SaveReportAsync(NoiseReport report, CancellationToken cancellationToken = default)
    {
        Reports.Enqueue(report);
        return Task.CompletedTask;
    }
}

/// <summary>Inicializador vacío para pruebas sin base de datos.</summary>
public sealed class NoopDatabaseInitializer : IDatabaseInitializer
{
    /// <inheritdoc />
    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
