using System;
using System.Collections.Generic;
using app_escritorio.Models;

namespace app_escritorio.Data
{
    /// <summary>Reservas (tablas Reserva, Cliente y Mesa).</summary>
    public static class ReservaStore
    {
        public const string SinMesa = "Sin mesa";

        /// <summary>Reservas de un día.</summary>
        public static List<Reserva> LoadDay(DateTime day)
        {
            return Db.Query(@"SELECT r.Id, r.FechaHora, c.Nombre AS Cliente, c.Telefono, r.Personas, m.Nombre AS Mesa, r.Estado
                              FROM dbo.Reserva r
                              JOIN dbo.Cliente c   ON c.Id = r.ClienteId
                              LEFT JOIN dbo.Mesa m ON m.Id = r.MesaId
                              WHERE r.FechaHora >= @desde AND r.FechaHora < @hasta
                              ORDER BY r.FechaHora",
                rd => new Reserva(Db.Date(rd, "FechaHora"), Db.Str(rd, "Cliente"), Db.Int(rd, "Personas"),
                                  Db.Str(rd, "Mesa") ?? SinMesa, Db.Str(rd, "Estado"), Db.Str(rd, "Telefono") ?? string.Empty)
                      { Id = Db.Int(rd, "Id") },
                "@desde", day.Date, "@hasta", day.Date.AddDays(1));
        }

        /// <summary>Crea o actualiza la reserva. El cliente se busca por nombre y teléfono (o se crea).</summary>
        public static void Save(Reserva r)
        {
            object id = Db.Scalar(@"
                DECLARE @cliente INT = (SELECT TOP (1) Id FROM dbo.Cliente
                                        WHERE Nombre = @n AND ISNULL(Telefono, N'') = ISNULL(@t, N'') ORDER BY Id DESC);
                IF @cliente IS NULL
                BEGIN
                    INSERT INTO dbo.Cliente (Nombre, Telefono) VALUES (@n, @t);
                    SET @cliente = SCOPE_IDENTITY();
                END;
                DECLARE @mesa INT = (SELECT Id FROM dbo.Mesa WHERE Nombre = @m AND Activa = 1);

                IF @id = 0
                BEGIN
                    INSERT INTO dbo.Reserva (ClienteId, MesaId, FechaHora, Personas, Estado)
                    VALUES (@cliente, @mesa, @f, @p, @e);
                    SET @id = SCOPE_IDENTITY();
                END
                ELSE
                    UPDATE dbo.Reserva SET ClienteId = @cliente, MesaId = @mesa, FechaHora = @f, Personas = @p, Estado = @e
                    WHERE Id = @id;
                SELECT @id;",
                "@n", r.Cliente.Trim(), "@t", string.IsNullOrWhiteSpace(r.Telefono) ? null : r.Telefono.Trim(),
                "@m", r.Mesa == SinMesa ? null : r.Mesa, "@f", r.Fecha, "@p", (byte)Math.Max(1, Math.Min(100, r.Personas)),
                "@e", r.Estado, "@id", r.Id);
            if (id != null) r.Id = Convert.ToInt32(id);
        }

        public static void Delete(Reserva r)
        {
            Db.Execute("DELETE FROM dbo.Reserva WHERE Id = @id", "@id", r.Id);
        }
    }
}
