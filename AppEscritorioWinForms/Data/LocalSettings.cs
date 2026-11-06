using System;
using System.IO;

namespace app_escritorio.Data
{
    /// <summary>
    /// Datos del negocio (tabla Configuracion, una sola fila).
    /// <see cref="AppDataDir"/> sigue siendo la carpeta local de la app (conexion.txt y respaldos).
    /// </summary>
    public static class LocalSettings
    {
        public static string AppDataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RestoOS");

        public class RestaurantInfo
        {
            public string Name { get; set; } = "Mi restaurante";
            public string Address { get; set; } = string.Empty;
            public string Phone { get; set; } = string.Empty;
            public string CurrencySymbol { get; set; } = "$";
            public string LogoPath { get; set; } = string.Empty;
        }

        /// <summary>Impuesto por defecto en % (0, 8, 19 ó 27). 8 si no se puede leer.</summary>
        public static int LoadTaxPercent()
        {
            try
            {
                object value = Db.Scalar("SELECT ImpuestoPorDefecto FROM dbo.Configuracion WHERE Id = 1");
                return value == null ? 8 : Convert.ToInt32(value);
            }
            catch { return 8; }
        }

        public static void SaveTaxPercent(int percent)
        {
            Db.Execute("UPDATE dbo.Configuracion SET ImpuestoPorDefecto = @p WHERE Id = 1", "@p", (byte)percent);
        }

        public static RestaurantInfo LoadRestaurant()
        {
            var info = new RestaurantInfo();
            try
            {
                var rows = Db.Query(
                    "SELECT NombreRestaurante, Direccion, Telefono, SimboloMoneda, LogoRuta FROM dbo.Configuracion WHERE Id = 1",
                    rd => new RestaurantInfo
                    {
                        Name = Db.Str(rd, "NombreRestaurante") ?? "Mi restaurante",
                        Address = Db.Str(rd, "Direccion") ?? string.Empty,
                        Phone = Db.Str(rd, "Telefono") ?? string.Empty,
                        CurrencySymbol = Db.Str(rd, "SimboloMoneda") ?? "$",
                        LogoPath = Db.Str(rd, "LogoRuta") ?? string.Empty
                    });
                if (rows.Count > 0) info = rows[0];
            }
            catch { }
            return info;
        }

        public static void SaveRestaurant(RestaurantInfo info)
        {
            Db.Execute(@"UPDATE dbo.Configuracion
                            SET NombreRestaurante = @n, Direccion = @d, Telefono = @t, SimboloMoneda = @m, LogoRuta = @l
                          WHERE Id = 1",
                "@n", info.Name, "@d", Blank(info.Address), "@t", Blank(info.Phone),
                "@m", string.IsNullOrWhiteSpace(info.CurrencySymbol) ? "$" : info.CurrencySymbol.Trim(), "@l", Blank(info.LogoPath));
        }

        /// <summary>Nombre del restaurante guardado en Configuración (o null).</summary>
        public static string LoadRestaurantName()
        {
            string name = LoadRestaurant().Name;
            return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        }

        /// <summary>Dirección del restaurante (se muestra bajo el nombre en el sidebar).</summary>
        public static string LoadAddress()
        {
            string address = LoadRestaurant().Address;
            return string.IsNullOrWhiteSpace(address) ? null : address.Trim();
        }

        public static bool IsModuleEnabled(string key) => ModuleStore.IsEnabled(key);

        private static object Blank(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }
}
