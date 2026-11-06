namespace app_escritorio.Data
{
    /// <summary>Usuario que inició sesión (lo usa el Shell para los permisos y el POS como cajero de cada venta).</summary>
    public static class Sesion
    {
        public static int UsuarioId { get; private set; }
        public static string Nombre { get; private set; } = string.Empty;
        public static string Rol { get; private set; } = string.Empty;

        public static bool EsAdministrador => Rol == "Administrador";

        public static void Iniciar(int usuarioId, string nombre, string rol)
        {
            UsuarioId = usuarioId;
            Nombre = nombre ?? string.Empty;
            Rol = rol ?? string.Empty;
        }

        public static void Cerrar() => Iniciar(0, string.Empty, string.Empty);

        /// <summary>
        /// Busca el usuario activo dueño del PIN (sp_ValidarPin). Con <paramref name="rol"/> solo acepta ese rol.
        /// Devuelve null si el PIN no coincide.
        /// </summary>
        public static (int Id, string Nombre, string Rol)? ValidarPin(string pin, string rol = null)
        {
            var rows = Db.Query("EXEC dbo.sp_ValidarPin @Pin, @Rol",
                rd => (Db.Int(rd, "Id"), Db.Str(rd, "Nombre"), Db.Str(rd, "Rol")),
                "@Pin", pin, "@Rol", rol);
            if (rows.Count == 0) return null;
            return rows[0];
        }
    }
}
