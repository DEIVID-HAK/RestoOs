using System;
using System.Collections.Generic;

namespace app_escritorio.Data
{
    /// <summary>Fila de dbo.Usuario con el nombre del rol ya resuelto.</summary>
    public class UsuarioRow
    {
        public int Id;
        public string Nombre;
        public int RolId;
        public string Rol;
        public bool Activo;
        public bool TienePin;
        public DateTime FechaCreacion;
    }

    /// <summary>
    /// Usuarios del sistema (tabla dbo.Usuario). El PIN nunca se guarda en texto plano:
    /// se asigna con sp_GuardarPin (sal + SHA-256) y se valida con sp_ValidarPin.
    /// Los usuarios no se borran: se desactivan, porque Ventas, Turnos y Movimientos los referencian.
    /// </summary>
    public static class UsuarioStore
    {
        public static List<UsuarioRow> Listar()
        {
            return Db.Query(@"
                SELECT u.Id, u.Nombre, u.RolId, r.Nombre AS Rol, u.Activo,
                       CASE WHEN u.PinHash IS NULL THEN 0 ELSE 1 END AS TienePin,
                       u.FechaCreacion
                FROM dbo.Usuario u
                JOIN dbo.Rol r ON r.Id = u.RolId
                ORDER BY u.Nombre",
                rd => new UsuarioRow
                {
                    Id = Db.Int(rd, "Id"),
                    Nombre = Db.Str(rd, "Nombre"),
                    RolId = Db.Int(rd, "RolId"),
                    Rol = Db.Str(rd, "Rol"),
                    Activo = Db.Bool(rd, "Activo"),
                    TienePin = Db.Bool(rd, "TienePin"),
                    FechaCreacion = Db.Date(rd, "FechaCreacion")
                });
        }

        public static List<(int Id, string Nombre)> Roles()
        {
            return Db.Query("SELECT Id, Nombre FROM dbo.Rol ORDER BY Nombre",
                rd => (Db.Int(rd, "Id"), Db.Str(rd, "Nombre")));
        }

        public static bool NombreEnUso(string nombre, int exceptoId = 0)
        {
            object value = Db.Scalar(
                "SELECT COUNT(*) FROM dbo.Usuario WHERE Nombre = @Nombre AND Id <> @Id",
                "@Nombre", nombre, "@Id", exceptoId);
            return value != null && Convert.ToInt32(value) > 0;
        }

        /// <summary>Cantidad de administradores activos distintos de <paramref name="exceptoId"/>.</summary>
        public static int AdministradoresActivos(int exceptoId = 0)
        {
            object value = Db.Scalar(@"
                SELECT COUNT(*)
                FROM dbo.Usuario u
                JOIN dbo.Rol r ON r.Id = u.RolId
                WHERE u.Activo = 1 AND r.Nombre = N'Administrador' AND u.Id <> @Id",
                "@Id", exceptoId);
            return value == null ? 0 : Convert.ToInt32(value);
        }

        /// <summary>Crea el usuario y le asigna el PIN en la misma transacción.</summary>
        public static int Crear(string nombre, int rolId, string pin)
        {
            using (var cn = Db.Open())
            using (var tx = cn.BeginTransaction())
            {
                int id;
                using (var cmd = Db.Cmd(cn, tx,
                    @"INSERT INTO dbo.Usuario (Nombre, RolId) VALUES (@Nombre, @RolId);
                      SELECT CAST(SCOPE_IDENTITY() AS INT);",
                    "@Nombre", nombre, "@RolId", rolId))
                    id = Convert.ToInt32(cmd.ExecuteScalar());

                using (var cmd = Db.Cmd(cn, tx, "EXEC dbo.sp_GuardarPin @UsuarioId, @Pin",
                    "@UsuarioId", id, "@Pin", pin))
                    cmd.ExecuteNonQuery();

                tx.Commit();
                return id;
            }
        }

        public static void Actualizar(int id, string nombre, int rolId)
        {
            Db.Execute("UPDATE dbo.Usuario SET Nombre = @Nombre, RolId = @RolId WHERE Id = @Id",
                "@Nombre", nombre, "@RolId", rolId, "@Id", id);
        }

        public static void CambiarPin(int id, string pin)
        {
            Db.Execute("EXEC dbo.sp_GuardarPin @UsuarioId, @Pin", "@UsuarioId", id, "@Pin", pin);
        }

        public static void SetActivo(int id, bool activo)
        {
            Db.Execute("UPDATE dbo.Usuario SET Activo = @Activo WHERE Id = @Id",
                "@Activo", activo, "@Id", id);
        }
    }
}
