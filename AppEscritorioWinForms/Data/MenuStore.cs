using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using app_escritorio.Models;
using Microsoft.Data.SqlClient;

namespace app_escritorio.Data
{
    public class MenuData
    {
        public List<Category> Categories { get; set; } = new List<Category>();
        public List<MenuItem> Items { get; set; } = new List<MenuItem>();
    }

    /// <summary>
    /// Carta del restaurante en SQL Server (Categoria, Producto, Etiqueta, ProductoInsumo).
    /// <see cref="Current"/> es la carta compartida por el Menú digital y el POS: al publicar cambios se llama
    /// <see cref="Publish"/> y el POS se actualiza al instante con <see cref="MenuChanged"/>.
    /// </summary>
    public static class MenuStore
    {
        /// <summary>Carta vieja en XML: si la base no tiene platos, se importa una sola vez.</summary>
        public const string DefaultPath = "data/menu.xml";

        private static MenuData _current;

        /// <summary>Se dispara cuando la carta cambia (precio, disponibilidad, platos nuevos...).</summary>
        public static event Action MenuChanged;

        /// <summary>Carta en memoria (se carga de la base la primera vez).</summary>
        public static MenuData Current
        {
            get
            {
                if (_current != null) return _current;
                MenuData data;
                try { data = LoadFromDb(); }
                catch (Exception ex)
                {
                    Db.ShowError("cargar la carta", ex);
                    data = new MenuData();
                }

                bool imported = false;
                if (data.Items.Count == 0)
                {
                    var xml = Load(DefaultPath);
                    if (xml.Items.Count > 0) { data = xml; imported = true; }
                }
                Normalize(data);
                _current = data;
                if (imported) Publish();
                return _current;
            }
        }

        /// <summary>Guarda la carta en la base y avisa a las pantallas que la usan. Devuelve false si no se pudo guardar.</summary>
        public static bool Publish()
        {
            var data = Current;
            string problem = Validate(data);
            if (problem != null)
            {
                System.Windows.Forms.MessageBox.Show(problem, "Menú digital",
                    System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Information);
                return false;
            }

            try
            {
                SaveToDb(data);
            }
            catch (Exception ex)
            {
                Db.ShowError("publicar la carta", ex);
                return false;
            }
            MenuChanged?.Invoke();
            return true;
        }

        /// <summary>Vuelve a leer las porciones del día (el cobro las descuenta en la base) y avisa al POS.</summary>
        public static void ReloadStock()
        {
            if (_current == null) return;
            try
            {
                var stock = Db.Query("SELECT Id, PorcionesDia FROM dbo.Producto",
                    rd => new { Id = Db.Int(rd, "Id"), Porciones = Db.IntOrNull(rd, "PorcionesDia") })
                    .ToDictionary(x => x.Id, x => x.Porciones);
                foreach (var item in _current.Items)
                    if (item.DbId > 0 && stock.TryGetValue(item.DbId, out var p)) item.Stock = p ?? -1;
            }
            catch { return; }
            MenuChanged?.Invoke();
        }

        /// <summary>Inventario renombró un insumo: se actualizan las recetas en memoria para que el próximo "Publicar" no lo recree.</summary>
        public static void RenameInsumoLinks(string oldName, string newName)
        {
            if (_current == null) return;
            foreach (var link in _current.Items.SelectMany(i => i.Insumos))
                if (string.Equals(link.Nombre, oldName, StringComparison.OrdinalIgnoreCase)) link.Nombre = newName;
        }

        /// <summary>Inventario dio de baja un insumo: se quita de las recetas en memoria.</summary>
        public static void RemoveInsumoLinks(string name)
        {
            if (_current == null) return;
            foreach (var item in _current.Items)
                item.Insumos.RemoveAll(l => string.Equals(l.Nombre, name, StringComparison.OrdinalIgnoreCase));
        }

        public static Category CategoryOf(MenuItem item) => Current.Categories.FirstOrDefault(c => c.Id == item.CategoryId);

        // ===================== Lectura =====================

        private static MenuData LoadFromDb()
        {
            var data = new MenuData();
            var byDbId = new Dictionary<int, Category>();

            foreach (var c in Db.Query("SELECT Id, Nombre, Posicion FROM dbo.Categoria WHERE Activo = 1 ORDER BY Posicion, Id",
                         rd => new Category { DbId = Db.Int(rd, "Id"), Name = Db.Str(rd, "Nombre"), Position = Db.Int(rd, "Posicion") }))
            {
                data.Categories.Add(c);
                byDbId[c.DbId] = c;
            }
            if (data.Categories.Count == 0) return data;

            var items = Db.Query(@"SELECT Id, CategoriaId, Nombre, Descripcion, PrecioSalon, PrecioDelivery, ComisionDelivery, SinComision,
                                          Disponible, SugerenciaChef, Posicion, ImagenRuta, PorcionesDia
                                   FROM dbo.Producto WHERE Activo = 1 ORDER BY Posicion, Id",
                rd => new
                {
                    CategoriaId = Db.Int(rd, "CategoriaId"),
                    Item = new MenuItem
                    {
                        DbId = Db.Int(rd, "Id"),
                        Name = Db.Str(rd, "Nombre"),
                        Description = Db.Str(rd, "Descripcion") ?? string.Empty,
                        PriceSalon = Db.Dec(rd, "PrecioSalon"),
                        PriceDelivery = Db.Dec(rd, "PrecioDelivery"),
                        CommissionPercentage = Db.Dec(rd, "ComisionDelivery"),
                        NoCommission = Db.Bool(rd, "SinComision"),
                        IsAvailable = Db.Bool(rd, "Disponible"),
                        IsSuggestion = Db.Bool(rd, "SugerenciaChef"),
                        Position = Db.Int(rd, "Posicion"),
                        ImageUrl = Db.Str(rd, "ImagenRuta"),
                        Stock = Db.IntOrNull(rd, "PorcionesDia") ?? -1
                    }
                });

            var itemsByDbId = new Dictionary<int, MenuItem>();
            foreach (var row in items)
            {
                var cat = byDbId.TryGetValue(row.CategoriaId, out var c) ? c : data.Categories[0];
                row.Item.CategoryId = cat.Id;
                cat.ItemIds.Add(row.Item.Id);
                data.Items.Add(row.Item);
                itemsByDbId[row.Item.DbId] = row.Item;
            }

            foreach (var t in Db.Query(@"SELECT pe.ProductoId, e.Nombre, e.Tipo
                                         FROM dbo.ProductoEtiqueta pe JOIN dbo.Etiqueta e ON e.Id = pe.EtiquetaId",
                         rd => new { Producto = Db.Int(rd, "ProductoId"), Nombre = Db.Str(rd, "Nombre"), Tipo = Db.Str(rd, "Tipo") }))
            {
                if (!itemsByDbId.TryGetValue(t.Producto, out var item)) continue;
                (t.Tipo == "Dietario" ? item.DietaryFilters : item.Tags).Add(t.Nombre);
            }

            foreach (var r in Db.Query(@"SELECT pi.ProductoId, i.Nombre, pi.Cantidad, i.Unidad
                                         FROM dbo.ProductoInsumo pi JOIN dbo.Insumo i ON i.Id = pi.InsumoId",
                         rd => new { Producto = Db.Int(rd, "ProductoId"), Link = new InsumoLink
                         {
                             Nombre = Db.Str(rd, "Nombre"), Cantidad = Db.Dec(rd, "Cantidad"), Unidad = Db.Str(rd, "Unidad")
                         } }))
            {
                if (itemsByDbId.TryGetValue(r.Producto, out var item)) item.Insumos.Add(r.Link);
            }
            return data;
        }

        /// <summary>Lee una carta en XML (formato anterior). Solo se usa para importarla.</summary>
        public static MenuData Load(string filePath)
        {
            if (!File.Exists(filePath)) return new MenuData();
            try
            {
                var ser = new XmlSerializer(typeof(MenuData));
                using (var fs = File.OpenRead(filePath))
                    return ser.Deserialize(fs) as MenuData ?? new MenuData();
            }
            catch
            {
                return new MenuData();
            }
        }

        // ===================== Escritura =====================

        /// <summary>Nombres repetidos (la base no los admite).</summary>
        private static string Validate(MenuData data)
        {
            var catDup = data.Categories.GroupBy(c => (c.Name ?? "").Trim(), StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
            if (catDup != null) return "Hay dos categorías llamadas \"" + catDup.Key + "\". Cambia el nombre de una.";
            var itemDup = data.Items.GroupBy(i => (i.Name ?? "").Trim(), StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
            if (itemDup != null) return "Hay dos platos llamados \"" + itemDup.Key + "\". Cambia el nombre de uno antes de publicar.";
            if (data.Items.Any(i => string.IsNullOrWhiteSpace(i.Name))) return "Hay un plato sin nombre.";
            return null;
        }

        /// <summary>
        /// Sincroniza la carta completa en una transacción. Lo que ya no está en la carta se da de baja (Activo = 0),
        /// no se borra: las ventas pasadas siguen apuntando a esos platos.
        /// </summary>
        private static void SaveToDb(MenuData data)
        {
            var newCatIds = new Dictionary<Category, int>();
            var newItemIds = new Dictionary<MenuItem, int>();

            using (var cn = Db.Open())
            using (var tx = cn.BeginTransaction())
            {
                // 1. Bajas primero, para liberar nombres reutilizados en esta misma publicación.
                Deactivate(cn, tx, "Producto", data.Items.Select(i => i.DbId));
                Deactivate(cn, tx, "Categoria", data.Categories.Select(c => c.DbId));

                // 2. Categorías
                var catDbId = new Dictionary<Guid, int>();
                for (int i = 0; i < data.Categories.Count; i++)
                {
                    var c = data.Categories[i];
                    string name = c.Name.Trim();
                    FreeName(cn, tx, "Categoria", name, c.DbId);
                    int id = c.DbId;
                    if (id == 0)
                        id = Convert.ToInt32(Db.Cmd(cn, tx,
                            "INSERT INTO dbo.Categoria (Nombre, Posicion) OUTPUT inserted.Id VALUES (@n, @p)",
                            "@n", name, "@p", i).ExecuteScalar());
                    else
                        Db.Cmd(cn, tx, "UPDATE dbo.Categoria SET Nombre = @n, Posicion = @p, Activo = 1 WHERE Id = @id",
                            "@n", name, "@p", i, "@id", id).ExecuteNonQuery();
                    catDbId[c.Id] = id;
                    if (c.DbId == 0) newCatIds[c] = id;
                }

                // 3. Platos
                foreach (var item in data.Items)
                {
                    if (!catDbId.TryGetValue(item.CategoryId, out int categoriaId)) categoriaId = catDbId.Values.First();
                    string name = item.Name.Trim();
                    FreeName(cn, tx, "Producto", name, item.DbId);

                    var values = new object[]
                    {
                        "@cat", categoriaId, "@n", name, "@d", Cut(item.Description, 500),
                        "@ps", item.PriceSalon, "@pd", item.PriceDelivery,
                        "@com", Math.Max(0m, Math.Min(100m, item.CommissionPercentage)), "@sc", item.NoCommission,
                        "@disp", item.IsAvailable, "@chef", item.IsSuggestion, "@pos", item.Position,
                        "@img", Cut(item.ImageUrl, 260),
                        "@stock", item.Stock < 0 ? null : (object)item.Stock, "@id", item.DbId
                    };
                    int id = item.DbId;
                    if (id == 0)
                        id = Convert.ToInt32(Db.Cmd(cn, tx,
                            @"INSERT INTO dbo.Producto (CategoriaId, Nombre, Descripcion, PrecioSalon, PrecioDelivery, ComisionDelivery,
                                                        SinComision, Disponible, SugerenciaChef, Posicion, ImagenRuta, PorcionesDia)
                              OUTPUT inserted.Id
                              VALUES (@cat, @n, @d, @ps, @pd, @com, @sc, @disp, @chef, @pos, @img, @stock)", values).ExecuteScalar());
                    else
                        Db.Cmd(cn, tx,
                            @"UPDATE dbo.Producto
                                 SET CategoriaId = @cat, Nombre = @n, Descripcion = @d, PrecioSalon = @ps, PrecioDelivery = @pd,
                                     ComisionDelivery = @com, SinComision = @sc, Disponible = @disp, SugerenciaChef = @chef,
                                     Posicion = @pos, ImagenRuta = @img, PorcionesDia = @stock, Activo = 1
                               WHERE Id = @id", values).ExecuteNonQuery();
                    if (item.DbId == 0) newItemIds[item] = id;

                    SaveTags(cn, tx, id, item);
                    SaveRecipe(cn, tx, id, item);
                }

                tx.Commit();
            }

            // Los Id nuevos se asignan solo si la transacción se confirmó.
            foreach (var kv in newCatIds) kv.Key.DbId = kv.Value;
            foreach (var kv in newItemIds) kv.Key.DbId = kv.Value;
        }

        private static void Deactivate(SqlConnection cn, SqlTransaction tx, string table, IEnumerable<int> keepIds)
        {
            string keep = string.Join(",", keepIds.Where(id => id > 0));
            Db.Cmd(cn, tx, "UPDATE dbo." + table + " SET Activo = 0 WHERE Activo = 1" + (keep.Length > 0 ? " AND Id NOT IN (" + keep + ")" : ""))
              .ExecuteNonQuery();
        }

        /// <summary>Si un registro dado de baja tiene el nombre que se quiere usar, se le cambia para liberar el nombre.</summary>
        private static void FreeName(SqlConnection cn, SqlTransaction tx, string table, string name, int ownId)
        {
            Db.Cmd(cn, tx,
                "UPDATE dbo." + table + " SET Nombre = LEFT(Nombre, 40) + N' [baja ' + CAST(Id AS NVARCHAR(10)) + N']' " +
                "WHERE Nombre = @n AND Activo = 0 AND Id <> @id",
                "@n", name, "@id", ownId).ExecuteNonQuery();
        }

        private static void SaveTags(SqlConnection cn, SqlTransaction tx, int productoId, MenuItem item)
        {
            Db.Cmd(cn, tx, "DELETE FROM dbo.ProductoEtiqueta WHERE ProductoId = @p", "@p", productoId).ExecuteNonQuery();
            var tags = item.Tags.Select(t => (Nombre: t, Tipo: "Etiqueta"))
                .Concat(item.DietaryFilters.Select(t => (Nombre: t, Tipo: "Dietario")))
                .Where(t => !string.IsNullOrWhiteSpace(t.Nombre))
                .Select(t => (Nombre: t.Nombre.Trim(), t.Tipo))
                .Distinct();
            foreach (var t in tags)
            {
                Db.Cmd(cn, tx,
                    @"DECLARE @e INT = (SELECT Id FROM dbo.Etiqueta WHERE Nombre = @n AND Tipo = @t);
                      IF @e IS NULL BEGIN INSERT INTO dbo.Etiqueta (Nombre, Tipo) VALUES (@n, @t); SET @e = SCOPE_IDENTITY(); END;
                      IF NOT EXISTS (SELECT 1 FROM dbo.ProductoEtiqueta WHERE ProductoId = @p AND EtiquetaId = @e)
                          INSERT INTO dbo.ProductoEtiqueta (ProductoId, EtiquetaId) VALUES (@p, @e);",
                    "@n", t.Nombre, "@t", t.Tipo, "@p", productoId).ExecuteNonQuery();
            }
        }

        /// <summary>Receta del plato. Si el insumo no existe en el inventario se crea con stock 0.</summary>
        private static void SaveRecipe(SqlConnection cn, SqlTransaction tx, int productoId, MenuItem item)
        {
            Db.Cmd(cn, tx, "DELETE FROM dbo.ProductoInsumo WHERE ProductoId = @p", "@p", productoId).ExecuteNonQuery();
            foreach (var link in item.Insumos.Where(l => !string.IsNullOrWhiteSpace(l.Nombre) && l.Cantidad > 0)
                                             .GroupBy(l => l.Nombre.Trim(), StringComparer.OrdinalIgnoreCase).Select(g => g.First()))
            {
                string linkUnit = Unidades.Normalizar(link.Unidad) ?? "und";
                var row = Db.Cmd(cn, tx,
                    @"DECLARE @i INT = (SELECT Id FROM dbo.Insumo WHERE Nombre = @n);
                      IF @i IS NULL BEGIN INSERT INTO dbo.Insumo (Nombre, Unidad) VALUES (@n, @u); SET @i = SCOPE_IDENTITY(); END;
                      UPDATE dbo.Insumo SET Activo = 1 WHERE Id = @i AND Activo = 0;
                      SELECT Id, Unidad FROM dbo.Insumo WHERE Id = @i;",
                    "@n", link.Nombre.Trim(), "@u", linkUnit);
                int insumoId;
                string insumoUnit;
                using (var rd = row.ExecuteReader())
                {
                    rd.Read();
                    insumoId = Db.Int(rd, "Id");
                    insumoUnit = Db.Str(rd, "Unidad");
                }

                // La cantidad se guarda en la unidad del insumo (0,25 kg y 250 g son lo mismo).
                decimal cantidad = Unidades.Convertir(link.Cantidad, linkUnit, insumoUnit) ?? link.Cantidad;
                if (cantidad <= 0) continue;
                Db.Cmd(cn, tx, "INSERT INTO dbo.ProductoInsumo (ProductoId, InsumoId, Cantidad) VALUES (@p, @i, @c)",
                    "@p", productoId, "@i", insumoId, "@c", Math.Round(cantidad, 3)).ExecuteNonQuery();
            }
        }

        private static string Cut(string s, int max)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Trim();
            return s.Length <= max ? s : s.Substring(0, max);
        }

        /// <summary>Listas nunca nulas y al menos una categoría.</summary>
        private static void Normalize(MenuData data)
        {
            if (data.Categories == null) data.Categories = new List<Category>();
            if (data.Items == null) data.Items = new List<MenuItem>();
            foreach (var item in data.Items)
            {
                if (item.Tags == null) item.Tags = new List<string>();
                if (item.DietaryFilters == null) item.DietaryFilters = new List<string>();
                if (item.Variants == null) item.Variants = new List<MenuVariant>();
                if (item.Insumos == null) item.Insumos = new List<InsumoLink>();
            }
            if (data.Categories.Count == 0) data.Categories.Add(new Category { Name = "Platos Principales" });
        }
    }
}
