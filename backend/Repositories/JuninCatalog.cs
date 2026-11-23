namespace DecibelCity.Api.Repositories;

/// <summary>
/// Catálogo territorial de Junín del INEI para 2017: 9 provincias y 124 distritos
/// (copia exacta de <c>repositories/catalogs.py</c> del backend Python retirado).
/// Fuente: INEI, Fecundidad y sus Diferenciales (2022), pp. 74–77, cotejado con el
/// Compendio Junín 2010, cuadro 1.5, pp. 56–58 (tildes y forma completa de Santa Bárbara de Carhuacayán).
/// Solo se cargan nombres y UBIGEO. La provincia de cada distrito son los cuatro primeros dígitos de su UBIGEO.
/// </summary>
public static class JuninCatalog
{
    /// <summary>Rol asignado a las cuentas registradas.</summary>
    public const string DefaultRole = "ciudadano";

    /// <summary>UBIGEO del departamento de Junín.</summary>
    public const string DepartmentUbigeo = "12";

    /// <summary>Provincias (UBIGEO, nombre).</summary>
    public static IReadOnlyList<(string Ubigeo, string Name)> Provinces { get; } =
    [
        ("1201", "Huancayo"), ("1202", "Concepción"), ("1203", "Chanchamayo"), ("1204", "Jauja"),
        ("1205", "Junín"), ("1206", "Satipo"), ("1207", "Tarma"), ("1208", "Yauli"), ("1209", "Chupaca"),
    ];

    /// <summary>Distritos (UBIGEO, nombre).</summary>
    public static IReadOnlyList<(string Ubigeo, string Name)> Districts { get; } =
    [
        // 1201 Huancayo (28)
        ("120101", "Huancayo"), ("120104", "Carhuacallanga"), ("120105", "Chacapampa"), ("120106", "Chicche"),
        ("120107", "Chilca"), ("120108", "Chongos Alto"), ("120111", "Chupuro"), ("120112", "Colca"),
        ("120113", "Cullhuas"), ("120114", "El Tambo"), ("120116", "Huacrapuquio"), ("120117", "Hualhuas"),
        ("120119", "Huancán"), ("120120", "Huasicancha"), ("120121", "Huayucachi"), ("120122", "Ingenio"),
        ("120124", "Pariahuanca"), ("120125", "Pilcomayo"), ("120126", "Pucará"), ("120127", "Quichuay"),
        ("120128", "Quilcas"), ("120129", "San Agustín"), ("120130", "San Jerónimo de Tunán"), ("120132", "Saño"),
        ("120133", "Sapallanga"), ("120134", "Sicaya"), ("120135", "Santo Domingo de Acobamba"), ("120136", "Viques"),
        // 1202 Concepción (15)
        ("120201", "Concepción"), ("120202", "Aco"), ("120203", "Andamarca"), ("120204", "Chambara"),
        ("120205", "Cochas"), ("120206", "Comas"), ("120207", "Heroínas Toledo"), ("120208", "Manzanares"),
        ("120209", "Mariscal Castilla"), ("120210", "Matahuasi"), ("120211", "Mito"), ("120212", "Nueve de Julio"),
        ("120213", "Orcotuna"), ("120214", "San José de Quero"), ("120215", "Santa Rosa de Ocopa"),
        // 1203 Chanchamayo (6)
        ("120301", "Chanchamayo"), ("120302", "Perené"), ("120303", "Pichanaqui"), ("120304", "San Luis de Shuaro"),
        ("120305", "San Ramón"), ("120306", "Vitoc"),
        // 1204 Jauja (34)
        ("120401", "Jauja"), ("120402", "Acolla"), ("120403", "Apata"), ("120404", "Ataura"),
        ("120405", "Canchayllo"), ("120406", "Curicaca"), ("120407", "El Mantaro"), ("120408", "Huamalí"),
        ("120409", "Huaripampa"), ("120410", "Huertas"), ("120411", "Janjaillo"), ("120412", "Julcán"),
        ("120413", "Leonor Ordóñez"), ("120414", "Llocllapampa"), ("120415", "Marco"), ("120416", "Masma"),
        ("120417", "Masma Chicche"), ("120418", "Molinos"), ("120419", "Monobamba"), ("120420", "Muqui"),
        ("120421", "Muquiyauyo"), ("120422", "Paca"), ("120423", "Paccha"), ("120424", "Pancán"),
        ("120425", "Parco"), ("120426", "Pomacancha"), ("120427", "Ricrán"), ("120428", "San Lorenzo"),
        ("120429", "San Pedro de Chunán"), ("120430", "Sausa"), ("120431", "Sincos"), ("120432", "Tunán Marca"),
        ("120433", "Yauli"), ("120434", "Yauyos"),
        // 1205 Junín (4)
        ("120501", "Junín"), ("120502", "Carhuamayo"), ("120503", "Ondores"), ("120504", "Ulcumayo"),
        // 1206 Satipo (9)
        ("120601", "Satipo"), ("120602", "Coviriali"), ("120603", "Llaylla"), ("120604", "Mazamari"),
        ("120605", "Pampa Hermosa"), ("120606", "Pangoa"), ("120607", "Río Negro"), ("120608", "Río Tambo"),
        ("120609", "Vizcatán del Ene"),
        // 1207 Tarma (9)
        ("120701", "Tarma"), ("120702", "Acobamba"), ("120703", "Huaricolca"), ("120704", "Huasahuasi"),
        ("120705", "La Unión"), ("120706", "Palca"), ("120707", "Palcamayo"), ("120708", "San Pedro de Cajas"),
        ("120709", "Tapo"),
        // 1208 Yauli (10)
        ("120801", "La Oroya"), ("120802", "Chacapalpa"), ("120803", "Huay-Huay"), ("120804", "Marcapomacocha"),
        ("120805", "Morococha"), ("120806", "Paccha"), ("120807", "Santa Bárbara de Carhuacayán"),
        ("120808", "Santa Rosa de Sacco"), ("120809", "Suitucancha"), ("120810", "Yauli"),
        // 1209 Chupaca (9)
        ("120901", "Chupaca"), ("120902", "Ahuac"), ("120903", "Chongos Bajo"), ("120904", "Huachac"),
        ("120905", "Huamancaca Chico"), ("120906", "San Juan de Iscos"), ("120907", "San Juan de Jarpa"),
        ("120908", "Tres de Diciembre"), ("120909", "Yanacancha"),
    ];

    /// <summary>Vinculación de los puntos ilustrativos: nombre del distrito → UBIGEO.</summary>
    public static IReadOnlyDictionary<string, string> HotspotDistrictCodes { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal) { ["Huancayo"] = "120101", ["Chilca"] = "120107" };

    /// <summary>Categorías de fuentes (código, nombre, descripción).</summary>
    public static IReadOnlyList<(string Code, string Name, string Description)> Categories { get; } =
    [
        ("TRANSPORTE", "Transporte", "Tránsito vehicular particular y transporte público"),
        ("COMERCIO", "Comercio", "Actividad comercial, comercio ambulante y perifoneo"),
        ("CONSTRUCCION", "Construcción", "Obras de construcción y obras viales"),
        ("ACTIVIDAD_RECREATIVA", "Actividad recreativa", "Música, eventos y actividades de entretenimiento"),
        ("OTRAS", "Otras", "Fuentes sin clasificación específica en el formulario"),
    ];

    /// <summary>Zonificaciones (código, nombre, descripción).</summary>
    public static IReadOnlyList<(string Code, string Name, string Description)> Zonifications { get; } =
    [
        ("ZONA_PROTECCION_ESPECIAL", "Zona de protección especial", "Clasificación pendiente de asignación territorial validada"),
        ("RESIDENCIAL", "Residencial", "Clasificación residencial; sin límites acústicos normativos cargados"),
        ("COMERCIAL", "Comercial", "Clasificación comercial; sin límites acústicos normativos cargados"),
        ("INDUSTRIAL", "Industrial", "Clasificación industrial; sin límites acústicos normativos cargados"),
    ];
}
