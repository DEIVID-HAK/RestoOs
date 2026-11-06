using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Data.SqlClient;

namespace app_escritorio.Data
{
    /// <summary>
    /// Conexión a SQL Server para todos los "Store" de Data/.
    /// La cadena se lee de %LocalAppData%\RestoOS\conexion.txt; si no existe se usa LocalDB (viene con Visual Studio).
    /// Si la base RestoOS no existe, se crea la primera vez con el script embebido (Database/RestoOS_SQLServer.sql).
    /// </summary>
    public static class Db
    {
        public const string DatabaseName = "RestoOS";
        public const string DefaultConnection =
            @"Server=(localdb)\MSSQLLocalDB;Database=RestoOS;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=30";

        private static string _connectionString;

        /// <summary>Archivo opcional con la cadena de conexión (una línea). Útil para un SQL Server en otra máquina de la red.</summary>
        public static string ConnectionFile => Path.Combine(LocalSettings.AppDataDir, "conexion.txt");

        public static string ConnectionString => _connectionString ?? (_connectionString = ReadConnectionString());

        public static string ServerName
        {
            get
            {
                try { return new SqlConnectionStringBuilder(ConnectionString).DataSource; }
                catch { return "(cadena de conexión inválida)"; }
            }
        }

        private static string ReadConnectionString()
        {
            try
            {
                if (File.Exists(ConnectionFile))
                {
                    string text = File.ReadAllText(ConnectionFile).Trim();
                    if (text.Length > 0) return text;
                }
            }
            catch { }
            return DefaultConnection;
        }

        /// <summary>Vuelve a leer conexion.txt (después de que el usuario lo corrige).</summary>
        public static void ResetConnectionString() => _connectionString = null;

        // ===================== Arranque =====================

        /// <summary>
        /// Comprueba la conexión, crea la base si no existe y aplica las migraciones pendientes.
        /// Devuelve false si el usuario decide salir.
        /// </summary>
        public static bool StartupCheck()
        {
            while (true)
            {
                try
                {
                    EnsureDatabase();
                    Migrate();
                    return true;
                }
                catch (Exception ex)
                {
                    var answer = MessageBox.Show(
                        "No se pudo conectar a la base de datos.\n\n" +
                        "Servidor: " + ServerName + "\n" +
                        "Error: " + ex.Message + "\n\n" +
                        "Instala SQL Server Express o LocalDB, o escribe la cadena de conexión en:\n" + ConnectionFile + "\n\n" +
                        "¿Reintentar?",
                        "RestoOS", MessageBoxButtons.RetryCancel, MessageBoxIcon.Error);
                    if (answer != DialogResult.Retry) return false;
                    ResetConnectionString();
                }
            }
        }

        /// <summary>Crea la base RestoOS con el script embebido si todavía no existe.</summary>
        public static void EnsureDatabase()
        {
            var builder = new SqlConnectionStringBuilder(ConnectionString);
            string database = string.IsNullOrWhiteSpace(builder.InitialCatalog) ? DatabaseName : builder.InitialCatalog;
            builder.InitialCatalog = "master";

            using (var cn = new SqlConnection(builder.ConnectionString))
            {
                cn.Open();
                using (var check = new SqlCommand("SELECT DB_ID(@db)", cn))
                {
                    check.Parameters.AddWithValue("@db", database);
                    object id = check.ExecuteScalar();
                    if (id != null && id != DBNull.Value) return;
                }

                if (!string.Equals(database, DatabaseName, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("La base \"" + database + "\" no existe. Créala con Database/RestoOS_SQLServer.sql.");

                foreach (string batch in ScriptBatches())
                    using (var cmd = new SqlCommand(batch, cn) { CommandTimeout = 180 })
                        cmd.ExecuteNonQuery();
            }
            SqlConnection.ClearAllPools();
        }

        /// <summary>Cambios de esquema posteriores a la primera versión del script (idempotentes).</summary>
        private static void Migrate()
        {
            Execute(@"IF COL_LENGTH('dbo.Categoria', 'Activo') IS NULL
                          ALTER TABLE dbo.Categoria ADD Activo BIT NOT NULL CONSTRAINT DF_Categoria_Activo DEFAULT (1);");
        }

        private static IEnumerable<string> ScriptBatches()
        {
            string script;
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("RestoOS.sql"))
            {
                if (stream == null) throw new InvalidOperationException("No se encontró el script RestoOS.sql dentro de la aplicación.");
                using (var reader = new StreamReader(stream)) script = reader.ReadToEnd();
            }
            foreach (string part in Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
                if (part.Trim().Length > 0) yield return part;
        }

        // ===================== Ayudantes =====================

        public static SqlConnection Open()
        {
            var cn = new SqlConnection(ConnectionString);
            cn.Open();
            return cn;
        }

        /// <summary>Comando con parámetros en pares nombre/valor: Cmd(cn, tx, "... @Id", "@Id", 5). null se envía como NULL.</summary>
        public static SqlCommand Cmd(SqlConnection cn, SqlTransaction tx, string sql, params object[] parameters)
        {
            var cmd = new SqlCommand(sql, cn, tx);
            for (int i = 0; i + 1 < parameters.Length; i += 2)
                cmd.Parameters.AddWithValue((string)parameters[i], parameters[i + 1] ?? DBNull.Value);
            return cmd;
        }

        public static int Execute(string sql, params object[] parameters)
        {
            using (var cn = Open())
            using (var cmd = Cmd(cn, null, sql, parameters))
                return cmd.ExecuteNonQuery();
        }

        public static object Scalar(string sql, params object[] parameters)
        {
            using (var cn = Open())
            using (var cmd = Cmd(cn, null, sql, parameters))
            {
                object value = cmd.ExecuteScalar();
                return value == DBNull.Value ? null : value;
            }
        }

        /// <summary>Lee filas y las convierte con <paramref name="map"/>.</summary>
        public static List<T> Query<T>(string sql, Func<SqlDataReader, T> map, params object[] parameters)
        {
            var list = new List<T>();
            using (var cn = Open())
            using (var cmd = Cmd(cn, null, sql, parameters))
            using (var rd = cmd.ExecuteReader())
                while (rd.Read()) list.Add(map(rd));
            return list;
        }

        public static string Str(SqlDataReader rd, string column)
        {
            int i = rd.GetOrdinal(column);
            return rd.IsDBNull(i) ? null : Convert.ToString(rd.GetValue(i));
        }

        public static int Int(SqlDataReader rd, string column)
        {
            int i = rd.GetOrdinal(column);
            return rd.IsDBNull(i) ? 0 : Convert.ToInt32(rd.GetValue(i));
        }

        public static int? IntOrNull(SqlDataReader rd, string column)
        {
            int i = rd.GetOrdinal(column);
            return rd.IsDBNull(i) ? (int?)null : Convert.ToInt32(rd.GetValue(i));
        }

        public static decimal Dec(SqlDataReader rd, string column)
        {
            int i = rd.GetOrdinal(column);
            return rd.IsDBNull(i) ? 0m : Convert.ToDecimal(rd.GetValue(i));
        }

        public static bool Bool(SqlDataReader rd, string column)
        {
            int i = rd.GetOrdinal(column);
            return !rd.IsDBNull(i) && Convert.ToBoolean(rd.GetValue(i));
        }

        public static DateTime Date(SqlDataReader rd, string column)
        {
            int i = rd.GetOrdinal(column);
            return rd.IsDBNull(i) ? DateTime.MinValue : Convert.ToDateTime(rd.GetValue(i));
        }

        /// <summary>Mensaje de error uniforme. Los errores de los procedimientos (THROW 50xxx) ya vienen en español.</summary>
        public static void ShowError(string action, Exception ex)
        {
            string detail = ex is SqlException sql && sql.Number >= 50000 ? sql.Message : ex.Message;
            MessageBox.Show("No se pudo " + action + ".\n\n" + detail, "RestoOS", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
