using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using app_escritorio.Utils;

namespace app_escritorio.Data
{
    /// <summary>Mesa del salón (tabla Mesa).</summary>
    public class MesaInfo
    {
        public MesaInfo(string name, string sector, string capacity, string status, string mockTotal, bool canOpen)
        {
            Name = name; Sector = sector; Capacity = capacity; Status = status; MockTotal = mockTotal; CanOpen = canOpen;
        }

        /// <summary>Id en la base (0 = mesa nueva, aún sin guardar).</summary>
        public int Id { get; set; }
        public string Name { get; set; }
        public string Sector { get; set; }
        /// <summary>Texto que muestra la UI ("4 personas"); en la base se guarda solo el número.</summary>
        public string Capacity { get; set; }
        public string Status { get; set; }
        /// <summary>Ya no se usa (era el total de demostración); se conserva para no romper TableDialog.</summary>
        public string MockTotal { get; set; }
        public bool CanOpen { get; set; }

        /// <summary>"Mesa 5" → "5"; "Barra 1" → "1".</summary>
        public string Number
        {
            get
            {
                var parts = (Name ?? string.Empty).Split(' ');
                return parts.Length > 1 ? parts[parts.Length - 1] : Name;
            }
        }

        public decimal RealTotal => TicketStore.GetSubtotalFor(Name);

        /// <summary>Total del ticket abierto de la mesa en el POS.</summary>
        public string Total => RealTotal.ToString("C0", CultureInfo.GetCultureInfo("es-CO"));

        public Color StatusColor
        {
            get
            {
                switch (Status)
                {
                    case "Libre": return Theme.Tertiary;
                    case "Ocupada": return Theme.Secondary;
                    case "Cuenta pedida": return Theme.Primary;
                    case "Por limpiar": return Theme.Error;
                    default: return Theme.OnSurfaceVariant;
                }
            }
        }

        /// <summary>"4 personas" → 4 (entre 1 y 50; 4 si no hay número).</summary>
        public static byte ParseCapacity(string text)
        {
            var m = Regex.Match(text ?? string.Empty, @"\d+");
            int n = m.Success && int.TryParse(m.Value, out int v) ? v : 4;
            return (byte)Math.Max(1, Math.Min(50, n));
        }

        public static string FormatCapacity(int n) => n == 1 ? "1 persona" : n + " personas";
    }

    /// <summary>Lee y guarda las mesas en SQL Server (tablas Mesa y Sector).</summary>
    public static class MesaStore
    {
        public static readonly string[] Sectors = { "Principal", "Terraza", "Barra", "VIP" };
        public static readonly string[] Statuses = { "Libre", "Ocupada", "Cuenta pedida", "Reservada", "Por limpiar" };

        /// <summary>Mesas activas ordenadas por sector y nombre.</summary>
        public static List<MesaInfo> Load()
        {
            try
            {
                return Db.Query(@"SELECT m.Id, m.Nombre, s.Nombre AS Sector, m.Capacidad, m.Estado, m.PuedeAbrir
                                  FROM dbo.Mesa m JOIN dbo.Sector s ON s.Id = m.SectorId
                                  WHERE m.Activa = 1
                                  ORDER BY s.Id, m.Id",
                    rd => new MesaInfo(Db.Str(rd, "Nombre"), Db.Str(rd, "Sector"), MesaInfo.FormatCapacity(Db.Int(rd, "Capacidad")),
                                       Db.Str(rd, "Estado"), "$0", Db.Bool(rd, "PuedeAbrir"))
                    { Id = Db.Int(rd, "Id") });
            }
            catch (Exception ex)
            {
                Db.ShowError("cargar las mesas", ex);
                return new List<MesaInfo>();
            }
        }

        /// <summary>Nombres de las mesas activas (para elegir mesa en Reservas).</summary>
        public static List<string> Names() => Load().Select(m => m.Name).ToList();

        /// <summary>
        /// Guarda la lista completa: inserta las nuevas (Id = 0), actualiza las existentes
        /// y da de baja (Activa = 0) las que ya no están. No se borran porque puede haber ventas que las referencian.
        /// </summary>
        public static void Save(IEnumerable<MesaInfo> mesas)
        {
            var list = mesas.ToList();
            try
            {
                using (var cn = Db.Open())
                using (var tx = cn.BeginTransaction())
                {
                    foreach (var m in list)
                    {
                        int sectorId = SectorId(cn, tx, m.Sector);
                        string status = Statuses.Contains(m.Status) ? m.Status : "Libre";
                        byte capacity = MesaInfo.ParseCapacity(m.Capacity);

                        if (m.Id == 0)
                        {
                            // Si la mesa existió y se dio de baja, se reactiva con el mismo nombre.
                            object existing = Db.Cmd(cn, tx, "SELECT Id FROM dbo.Mesa WHERE Nombre = @n", "@n", m.Name).ExecuteScalar();
                            if (existing != null && existing != DBNull.Value) m.Id = Convert.ToInt32(existing);
                        }

                        if (m.Id == 0)
                        {
                            m.Id = Convert.ToInt32(Db.Cmd(cn, tx,
                                @"INSERT INTO dbo.Mesa (Nombre, SectorId, Capacidad, Estado, PuedeAbrir)
                                  OUTPUT inserted.Id VALUES (@n, @s, @c, @e, @a)",
                                "@n", m.Name, "@s", sectorId, "@c", capacity, "@e", status, "@a", m.CanOpen).ExecuteScalar());
                        }
                        else
                        {
                            Db.Cmd(cn, tx,
                                @"UPDATE dbo.Mesa SET Nombre = @n, SectorId = @s, Capacidad = @c, Estado = @e, PuedeAbrir = @a, Activa = 1
                                  WHERE Id = @id",
                                "@n", m.Name, "@s", sectorId, "@c", capacity, "@e", status, "@a", m.CanOpen, "@id", m.Id).ExecuteNonQuery();
                        }
                    }

                    string keep = string.Join(",", list.Select(m => m.Id.ToString(CultureInfo.InvariantCulture)));
                    Db.Cmd(cn, tx, "UPDATE dbo.Mesa SET Activa = 0 WHERE Activa = 1" + (keep.Length > 0 ? " AND Id NOT IN (" + keep + ")" : ""))
                      .ExecuteNonQuery();

                    tx.Commit();
                }
            }
            catch (Exception ex)
            {
                Db.ShowError("guardar las mesas", ex);
            }
        }

        /// <summary>Cambia solo el estado de una mesa (lo usa el POS al abrir y cobrar).</summary>
        public static void SetStatus(string mesa, string status)
        {
            try { Db.Execute("UPDATE dbo.Mesa SET Estado = @e WHERE Nombre = @n AND Activa = 1", "@e", status, "@n", mesa); }
            catch { }
        }

        private static int SectorId(Microsoft.Data.SqlClient.SqlConnection cn, Microsoft.Data.SqlClient.SqlTransaction tx, string sector)
        {
            string name = string.IsNullOrWhiteSpace(sector) ? "Principal" : sector.Trim();
            return Convert.ToInt32(Db.Cmd(cn, tx,
                @"DECLARE @id INT = (SELECT Id FROM dbo.Sector WHERE Nombre = @n);
                  IF @id IS NULL BEGIN INSERT INTO dbo.Sector (Nombre) VALUES (@n); SET @id = SCOPE_IDENTITY(); END;
                  SELECT @id;", "@n", name).ExecuteScalar());
        }
    }
}
