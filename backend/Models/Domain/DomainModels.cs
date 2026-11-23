using System.Text;

namespace DecibelCity.Api.Models.Domain;

/// <summary>Lugar verificado en Nominatim antes de guardar un reporte.</summary>
/// <param name="PlaceId">Identificador OSM verificado.</param>
/// <param name="Address">Dirección completa (<c>display_name</c>).</param>
/// <param name="Latitude">Latitud.</param>
/// <param name="Longitude">Longitud.</param>
public sealed record VerifiedPlace(string PlaceId, string Address, double Latitude, double Longitude);

/// <summary>Credenciales almacenadas. El hash nunca se imprime ni se serializa.</summary>
/// <param name="IdUsuario">Identificador de la cuenta.</param>
/// <param name="PasswordHash">Hash Argon2id en formato PHC.</param>
/// <param name="Activo">Estado de la cuenta.</param>
public sealed record StoredCredentials(int IdUsuario, string PasswordHash, bool Activo)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("IdUsuario = ").Append(IdUsuario).Append(", Activo = ").Append(Activo);
        return true;
    }
}

/// <summary>Perfil leído junto con el estado de cuenta y rol.</summary>
/// <param name="Profile">Perfil público.</param>
/// <param name="UsuarioActivo">Cuenta activa.</param>
/// <param name="RolActivo">Rol activo.</param>
public sealed record ProfileWithStatus(Dtos.UserProfile Profile, bool UsuarioActivo, bool RolActivo);
