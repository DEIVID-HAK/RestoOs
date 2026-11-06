using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using app_escritorio.Forms;
using Microsoft.Data.SqlClient;

namespace app_escritorio.Data
{
    /// <summary>
    /// Tickets abiertos por mesa. Lo comparten el POS y Mesas.
    /// Cada ticket es un Pedido en estado "Abierto" con sus líneas en DetallePedido, así sobrevive a cierres
    /// y cortes de luz. En memoria se mantiene la lista enlazada que pinta el POS; solo se escribe en la base
    /// cuando el contenido del ticket cambia.
    /// </summary>
    public static class TicketStore
    {
        public const string NoTable = "Mesa no seleccionada";
        private const string GeneralKey = "__GENERAL__";

        private static readonly Dictionary<string, BindingList<OrderLine>> _tickets = new Dictionary<string, BindingList<OrderLine>>();
        /// <summary>Última versión guardada de cada ticket (para no reescribir si nada cambió).</summary>
        private static readonly Dictionary<string, string> _saved = new Dictionary<string, string>();
        private static bool _loaded;
        private static bool _errorShown;

        /// <summary>Se dispara cuando cambia cualquier ticket (para refrescar Mesas).</summary>
        public static event Action TicketChanged;

        public static BindingList<OrderLine> GetTicketFor(string mesa)
        {
            EnsureLoaded();
            return GetOrCreate(Normalize(mesa));
        }

        public static decimal GetSubtotalFor(string mesa) => GetTicketFor(mesa).Sum(l => l.LineTotal);

        public static void NotifyChanged()
        {
            EnsureLoaded();
            foreach (var key in _tickets.Keys.ToList()) Save(key);
            try { TicketChanged?.Invoke(); } catch { }
        }

        /// <summary>
        /// Al renombrar una mesa solo se mueve la clave en memoria: en la base el pedido apunta a la mesa por Id,
        /// así que el cambio de nombre de la mesa ya lo arrastra.
        /// </summary>
        public static void Rename(string oldName, string newName)
        {
            EnsureLoaded();
            string oldKey = Normalize(oldName), newKey = Normalize(newName);
            if (oldKey == newKey || !_tickets.TryGetValue(oldKey, out var list)) return;
            _tickets.Remove(oldKey);
            _tickets[newKey] = list;
            _saved[newKey] = _saved.TryGetValue(oldKey, out var sig) ? sig : string.Empty;
            _saved.Remove(oldKey);
        }

        /// <summary>Borra el ticket de una mesa eliminada (memoria y base).</summary>
        public static void RemoveTicket(string mesa)
        {
            EnsureLoaded();
            string key = Normalize(mesa);
            if (_tickets.TryGetValue(key, out var list) && list.Count > 0)
            {
                list.Clear();   // dispara Save(key), que borra el pedido abierto
            }
            _tickets.Remove(key);
            _saved.Remove(key);
        }

        /// <summary>Guarda el ticket ya mismo y devuelve el Id de su pedido abierto (0 si está vacío). Lo usa el cobro.</summary>
        public static int FlushAndGetPedidoId(string mesa)
        {
            EnsureLoaded();
            string key = Normalize(mesa);
            _saved.Remove(key);                  // obliga a escribir
            if (!Save(key, throwOnError: true)) return 0;
            using (var cn = Db.Open())
                return FindOpenPedido(cn, null, key) ?? 0;
        }

        // ===================== Internos =====================

        private static string Normalize(string mesa) =>
            string.IsNullOrWhiteSpace(mesa) || mesa == NoTable ? GeneralKey : mesa.Trim();

        private static BindingList<OrderLine> GetOrCreate(string key)
        {
            if (!_tickets.TryGetValue(key, out var list))
            {
                list = new BindingList<OrderLine>();
                list.ListChanged += (s, e) => Save(key);
                _tickets[key] = list;
                _saved[key] = string.Empty;
            }
            return list;
        }

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            try { Load(); }
            catch (Exception ex) { Db.ShowError("cargar los tickets abiertos", ex); }
        }

        /// <summary>Carga los pedidos abiertos (salón / para llevar) con sus líneas.</summary>
        private static void Load()
        {
            var rows = Db.Query(@"SELECT m.Nombre AS Mesa, d.ProductoId, d.NombreProducto, c.Nombre AS Categoria,
                                         d.PrecioUnitario, d.Cantidad, d.Notas
                                  FROM dbo.Pedido p
                                  JOIN dbo.DetallePedido d ON d.PedidoId = p.Id
                                  JOIN dbo.Producto pr     ON pr.Id = d.ProductoId
                                  JOIN dbo.Categoria c     ON c.Id = pr.CategoriaId
                                  LEFT JOIN dbo.Mesa m     ON m.Id = p.MesaId
                                  WHERE p.Estado = N'Abierto' AND p.PlataformaId IS NULL
                                  ORDER BY p.Id, d.Id",
                rd => new
                {
                    Key = Normalize(Db.Str(rd, "Mesa")),
                    Line = new OrderLine(new Product(Db.Str(rd, "NombreProducto"), Db.Str(rd, "Categoria"), Db.Dec(rd, "PrecioUnitario"))
                                         { ProductoId = Db.Int(rd, "ProductoId") })
                    {
                        Quantity = Db.Int(rd, "Cantidad"),
                        Notes = Db.Str(rd, "Notas") ?? string.Empty
                    }
                });

            foreach (var row in rows)
            {
                if (!_tickets.TryGetValue(row.Key, out var list))
                {
                    list = new BindingList<OrderLine>();
                    _tickets[row.Key] = list;
                }
                list.Add(row.Line);
            }
            foreach (var kv in _tickets)
            {
                string key = kv.Key;
                kv.Value.ListChanged += (s, e) => Save(key);
                _saved[key] = Signature(kv.Value);
            }
        }

        private static string Signature(IEnumerable<OrderLine> lines) =>
            string.Join("\n", lines.Where(l => l?.Product != null).Select(l =>
                l.Product.ProductoId + "|" + l.Name + "|" + l.Product.Price.ToString(CultureInfo.InvariantCulture) + "|" + l.Quantity + "|" + l.Notes));

        /// <summary>Escribe el ticket en la base si cambió. Devuelve false si hubo error.</summary>
        private static bool Save(string key, bool throwOnError = false)
        {
            if (!_loaded || !_tickets.TryGetValue(key, out var list)) return true;
            var lines = list.Where(l => l?.Product != null && l.Quantity > 0).ToList();
            string signature = Signature(lines);
            if (_saved.TryGetValue(key, out var last) && last == signature) return true;

            try
            {
                using (var cn = Db.Open())
                using (var tx = cn.BeginTransaction())
                {
                    int? mesaId = key == GeneralKey ? null : FindMesaId(cn, tx, key);
                    if (key != GeneralKey && !mesaId.HasValue)
                        throw new InvalidOperationException("La mesa \"" + key + "\" no existe en la base de datos.");
                    int? pedidoId = FindOpenPedido(cn, tx, key);

                    if (pedidoId.HasValue)
                        Db.Cmd(cn, tx, "DELETE FROM dbo.DetallePedido WHERE PedidoId = @p AND ComandaId IS NULL", "@p", pedidoId.Value)
                          .ExecuteNonQuery();

                    if (lines.Count == 0)
                    {
                        if (pedidoId.HasValue)
                        {
                            // Ticket vaciado: se borra el pedido (si no tiene comandas enviadas) y la mesa vuelve a quedar libre.
                            Db.Cmd(cn, tx, @"IF NOT EXISTS (SELECT 1 FROM dbo.DetallePedido WHERE PedidoId = @p)
                                             AND NOT EXISTS (SELECT 1 FROM dbo.Comanda WHERE PedidoId = @p)
                                                 DELETE FROM dbo.Pedido WHERE Id = @p;",
                                   "@p", pedidoId.Value).ExecuteNonQuery();
                            if (mesaId.HasValue)
                                Db.Cmd(cn, tx, "UPDATE dbo.Mesa SET Estado = N'Libre' WHERE Id = @m AND Estado = N'Ocupada'", "@m", mesaId.Value)
                                  .ExecuteNonQuery();
                        }
                    }
                    else
                    {
                        if (!pedidoId.HasValue)
                        {
                            pedidoId = Convert.ToInt32(Db.Cmd(cn, tx,
                                @"INSERT INTO dbo.Pedido (Origen, MesaId, UsuarioId) OUTPUT inserted.Id VALUES (N'Salón', @m, @u)",
                                "@m", mesaId, "@u", Sesion.UsuarioId > 0 ? (object)Sesion.UsuarioId : null).ExecuteScalar());
                            if (mesaId.HasValue)
                                Db.Cmd(cn, tx, @"UPDATE dbo.Mesa SET Estado = N'Ocupada'
                                                 WHERE Id = @m AND Estado IN (N'Libre', N'Reservada', N'Por limpiar')", "@m", mesaId.Value)
                                  .ExecuteNonQuery();
                        }

                        foreach (var l in lines)
                        {
                            Db.Cmd(cn, tx,
                                @"INSERT INTO dbo.DetallePedido (PedidoId, ProductoId, NombreProducto, PrecioUnitario, Cantidad, Notas)
                                  SELECT @p, COALESCE(NULLIF(@prod, 0), (SELECT Id FROM dbo.Producto WHERE Nombre = @n)), @n, @precio, @cant, @notas",
                                "@p", pedidoId.Value, "@prod", l.Product.ProductoId, "@n", l.Product.Name,
                                "@precio", l.Product.Price, "@cant", l.Quantity,
                                "@notas", string.IsNullOrWhiteSpace(l.Notes) ? null : l.Notes.Trim()).ExecuteNonQuery();
                        }
                    }

                    tx.Commit();
                }
                _saved[key] = signature;
                _errorShown = false;
                return true;
            }
            catch (Exception ex)
            {
                if (throwOnError) throw;
                if (!_errorShown) Db.ShowError("guardar el ticket", ex);
                _errorShown = true;
                return false;
            }
        }

        private static int? FindMesaId(SqlConnection cn, SqlTransaction tx, string name)
        {
            object id = Db.Cmd(cn, tx, "SELECT Id FROM dbo.Mesa WHERE Nombre = @n AND Activa = 1", "@n", name).ExecuteScalar();
            return id == null || id == DBNull.Value ? (int?)null : Convert.ToInt32(id);
        }

        /// <summary>Pedido abierto de la mesa; para el ticket general, el pedido abierto sin mesa ni plataforma.</summary>
        private static int? FindOpenPedido(SqlConnection cn, SqlTransaction tx, string key)
        {
            object id = Db.Cmd(cn, tx,
                @"SELECT TOP (1) p.Id
                  FROM dbo.Pedido p
                  LEFT JOIN dbo.Mesa m ON m.Id = p.MesaId
                  WHERE p.Estado = N'Abierto' AND p.PlataformaId IS NULL
                    AND ((@mesa IS NULL AND p.MesaId IS NULL) OR m.Nombre = @mesa)
                  ORDER BY p.Id",
                "@mesa", key == GeneralKey ? null : key).ExecuteScalar();
            return id == null || id == DBNull.Value ? (int?)null : Convert.ToInt32(id);
        }
    }
}
