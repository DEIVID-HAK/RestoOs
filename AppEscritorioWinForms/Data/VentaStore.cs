using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using app_escritorio.Models;
using Microsoft.Data.SqlClient;

namespace app_escritorio.Data
{
    /// <summary>
    /// Cobros y ventas (tablas Venta y Pago). El cobro lo hace sp_CobrarPedido en una sola transacción:
    /// guarda la venta y el pago, descuenta insumos y porciones del día, cierra el pedido y deja la mesa "Por limpiar".
    /// </summary>
    public static class VentaStore
    {
        /// <summary>Datos que el POS envía al cobrar.</summary>
        public class Cobro
        {
            public string Mesa { get; set; }
            /// <summary>"Salón", "Para llevar" o "Domicilio".</summary>
            public string Origen { get; set; } = "Salón";
            public int ImpuestoPct { get; set; }
            /// <summary>"Efectivo" o "Tarjeta".</summary>
            public string Metodo { get; set; } = "Efectivo";
            /// <summary>Lo que entregó el cliente (solo efectivo).</summary>
            public decimal Recibido { get; set; }
            public string Cliente { get; set; }
            public string Telefono { get; set; }
            public string Direccion { get; set; }
        }

        public class Resultado
        {
            public int VentaId { get; set; }
            public decimal Subtotal { get; set; }
            public decimal Impuesto { get; set; }
            public decimal Total { get; set; }
            /// <summary>Insumos que quedaron en stock bajo o crítico después de la venta.</summary>
            public List<string> AlertasStock { get; } = new List<string>();
        }

        /// <summary>
        /// Impuesto que se suma al subtotal, redondeado igual que sp_CobrarPedido (ROUND de SQL: mitades hacia arriba).
        /// El POS lo usa para mostrar el total: así el monto que se cobra coincide con el que valida la base.
        /// </summary>
        public static decimal CalcularImpuesto(decimal subtotal, int pct) =>
            Math.Round(subtotal * pct / 100m, 2, MidpointRounding.AwayFromZero);

        /// <summary>Cobra el ticket de la mesa. Lanza excepción con el motivo si la base rechaza el cobro.</summary>
        public static Resultado Cobrar(Cobro c)
        {
            int pedidoId = TicketStore.FlushAndGetPedidoId(c.Mesa);
            if (pedidoId == 0) throw new InvalidOperationException("El ticket está vacío.");

            var result = new Resultado();
            using (var cn = Db.Open())
            {
                object clienteId = null;
                if (c.Origen == "Domicilio")
                {
                    clienteId = Db.Cmd(cn, null,
                        @"DECLARE @id INT = (SELECT TOP (1) Id FROM dbo.Cliente
                                              WHERE Nombre = @n AND ISNULL(Telefono, N'') = ISNULL(@t, N'') ORDER BY Id DESC);
                          IF @id IS NULL
                          BEGIN
                              INSERT INTO dbo.Cliente (Nombre, Telefono, Direccion) VALUES (@n, @t, @d);
                              SET @id = SCOPE_IDENTITY();
                          END
                          ELSE UPDATE dbo.Cliente SET Direccion = @d WHERE Id = @id;
                          SELECT @id;",
                        "@n", c.Cliente.Trim(), "@t", Blank(c.Telefono), "@d", c.Direccion.Trim()).ExecuteScalar();
                }

                // El tipo de pedido se decide al cobrar (el ticket se crea como salón).
                Db.Cmd(cn, null,
                    @"UPDATE dbo.Pedido
                         SET Origen = @o,
                             MesaId = CASE WHEN @o = N'Salón' THEN MesaId ELSE NULL END,
                             ClienteId = @c,
                             DireccionEntrega = @d,
                             EstadoDelivery = CASE WHEN @o = N'Domicilio' THEN N'Nuevo' ELSE NULL END,
                             UsuarioId = ISNULL(UsuarioId, @u)
                       WHERE Id = @p",
                    "@o", c.Origen, "@c", clienteId, "@d", c.Origen == "Domicilio" ? c.Direccion.Trim() : null,
                    "@u", Sesion.UsuarioId > 0 ? (object)Sesion.UsuarioId : null, "@p", pedidoId).ExecuteNonQuery();

                result.Subtotal = Convert.ToDecimal(Db.Cmd(cn, null,
                    "SELECT ISNULL(SUM(Subtotal), 0) FROM dbo.DetallePedido WHERE PedidoId = @p", "@p", pedidoId).ExecuteScalar());
                result.Impuesto = CalcularImpuesto(result.Subtotal, c.ImpuestoPct);
                result.Total = result.Subtotal + result.Impuesto;

                var pagos = new DataTable();
                pagos.Columns.Add("Metodo", typeof(string));
                pagos.Columns.Add("Monto", typeof(decimal));
                pagos.Columns.Add("MontoRecibido", typeof(decimal));
                pagos.Rows.Add(c.Metodo, result.Total,
                    c.Metodo == "Efectivo" ? (object)Math.Max(c.Recibido, result.Total) : DBNull.Value);

                using (var cmd = new SqlCommand("dbo.sp_CobrarPedido", cn) { CommandType = CommandType.StoredProcedure })
                {
                    cmd.Parameters.AddWithValue("@PedidoId", pedidoId);
                    cmd.Parameters.AddWithValue("@UsuarioId", Sesion.UsuarioId);
                    cmd.Parameters.AddWithValue("@ImpuestoPorcentaje", (byte)c.ImpuestoPct);
                    var tvp = cmd.Parameters.AddWithValue("@Pagos", pagos);
                    tvp.SqlDbType = SqlDbType.Structured;
                    tvp.TypeName = "dbo.TipoPago";
                    var ventaId = cmd.Parameters.Add("@VentaId", SqlDbType.Int);
                    ventaId.Direction = ParameterDirection.Output;

                    using (var rd = cmd.ExecuteReader())
                        while (rd.Read())
                            result.AlertasStock.Add(Db.Str(rd, "Nombre") + ": " + Db.Dec(rd, "Stock").ToString("0.##") + " " +
                                                    Db.Str(rd, "Unidad") + " (" + Db.Str(rd, "Estado") + ")");
                    result.VentaId = Convert.ToInt32(ventaId.Value);
                }
            }

            MenuStore.ReloadStock();
            return result;
        }

        /// <summary>Anula una venta (sp_AnularVenta): exige un administrador distinto al cajero y un motivo.</summary>
        public static void Anular(int ventaId, int supervisorId, string motivo)
        {
            Db.Execute("EXEC dbo.sp_AnularVenta @VentaId = @v, @SupervisorId = @s, @Motivo = @m",
                "@v", ventaId, "@s", supervisorId, "@m", motivo);
        }

        /// <summary>Ventas desde una fecha, con sus líneas y cómo se pagaron (para Reportes e Historial).</summary>
        public static List<Venta> Load(DateTime desde)
        {
            var ventas = Db.Query(@"SELECT v.Id, v.Fecha, p.Origen, m.Nombre AS Mesa, pl.Nombre AS Plataforma, c.Nombre AS Cliente,
                                           u.Nombre AS Cajero, v.Subtotal, v.ImpuestoPorcentaje, v.ImpuestoValor, v.Total, v.Estado,
                                           ISNULL((SELECT SUM(Monto) FROM dbo.Pago WHERE VentaId = v.Id AND Metodo = N'Efectivo'), 0) AS Efectivo,
                                           ISNULL((SELECT SUM(Monto) FROM dbo.Pago WHERE VentaId = v.Id AND Metodo = N'Tarjeta'), 0) AS Tarjeta
                                    FROM dbo.Venta v
                                    JOIN dbo.Pedido p        ON p.Id = v.PedidoId
                                    JOIN dbo.Usuario u       ON u.Id = v.UsuarioId
                                    LEFT JOIN dbo.Mesa m     ON m.Id = p.MesaId
                                    LEFT JOIN dbo.Plataforma pl ON pl.Id = p.PlataformaId
                                    LEFT JOIN dbo.Cliente c  ON c.Id = p.ClienteId
                                    WHERE v.Fecha >= @desde
                                    ORDER BY v.Fecha",
                rd =>
                {
                    string origen = Db.Str(rd, "Origen");
                    string destino = Db.Str(rd, "Mesa") ?? Db.Str(rd, "Plataforma")
                                     ?? (origen == "Domicilio" ? "Domicilio · " + (Db.Str(rd, "Cliente") ?? "") : origen);
                    decimal efectivo = Db.Dec(rd, "Efectivo"), tarjeta = Db.Dec(rd, "Tarjeta");
                    string metodo = efectivo > 0 && tarjeta > 0 ? "Mixto" : tarjeta > 0 ? "Tarjeta" : "Efectivo";
                    return new Venta(Db.Date(rd, "Fecha"), destino, string.Empty, Db.Dec(rd, "Total"), metodo, Db.Str(rd, "Cajero"),
                                     Db.Str(rd, "Estado"), origen == "Delivery" || origen == "Domicilio" ? "Delivery" : "Salón")
                    {
                        Id = Db.Int(rd, "Id"),
                        Subtotal = Db.Dec(rd, "Subtotal"),
                        Impuesto = Db.Dec(rd, "ImpuestoValor"),
                        TaxPct = Db.Int(rd, "ImpuestoPorcentaje"),
                        Efectivo = efectivo,
                        Tarjeta = tarjeta
                    };
                },
                "@desde", desde);

            var byId = ventas.ToDictionary(v => v.Id);
            foreach (var line in Db.Query(@"SELECT v.Id AS VentaId, d.NombreProducto, d.Cantidad, d.PrecioUnitario
                                            FROM dbo.DetallePedido d JOIN dbo.Venta v ON v.PedidoId = d.PedidoId
                                            WHERE v.Fecha >= @desde
                                            ORDER BY d.Id",
                         rd => new { Venta = Db.Int(rd, "VentaId"), Item = new VentaItem(Db.Str(rd, "NombreProducto"), Db.Int(rd, "Cantidad"), Db.Dec(rd, "PrecioUnitario")) },
                         "@desde", desde))
            {
                if (byId.TryGetValue(line.Venta, out var v)) v.Items.Add(line.Item);
            }
            foreach (var v in ventas)
                v.Detalle = string.Join(", ", v.Items.Select(x => "x" + x.Cantidad + " " + x.Plato));
            return ventas;
        }

        private static object Blank(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }
}
