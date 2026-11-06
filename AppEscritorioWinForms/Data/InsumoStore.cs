using System;
using System.Collections.Generic;
using System.Linq;
using app_escritorio.Models;

namespace app_escritorio.Data
{
    /// <summary>Unidades de medida que acepta la base (CHECK de Insumo.Unidad).</summary>
    public static class Unidades
    {
        public static readonly string[] Todas = { "kg", "g", "L", "ml", "und" };

        /// <summary>"Kilos" → "kg", "litro" → "L"... null si no es una unidad válida.</summary>
        public static string Normalizar(string unidad)
        {
            switch ((unidad ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "kg": case "kilo": case "kilos": case "kilogramo": case "kilogramos": return "kg";
                case "g": case "gr": case "gramo": case "gramos": return "g";
                case "l": case "lt": case "litro": case "litros": return "L";
                case "ml": case "mililitro": case "mililitros": return "ml";
                case "und": case "un": case "u": case "unidad": case "unidades": return "und";
                default: return null;
            }
        }

        /// <summary>Convierte entre kg/g y L/ml. null si las unidades no son compatibles.</summary>
        public static decimal? Convertir(decimal cantidad, string de, string a)
        {
            de = Normalizar(de);
            a = Normalizar(a);
            if (de == null || a == null) return null;
            if (de == a) return cantidad;
            if ((de == "g" && a == "kg") || (de == "ml" && a == "L")) return cantidad / 1000m;
            if ((de == "kg" && a == "g") || (de == "L" && a == "ml")) return cantidad * 1000m;
            return null;
        }
    }

    /// <summary>
    /// Insumos del inventario (tablas Insumo, CategoriaInsumo y MovimientoInventario).
    /// Cada cambio de stock hecho a mano queda en el kárdex como "Entrada" o "Ajuste".
    /// </summary>
    public static class InsumoStore
    {
        public static List<Insumo> Load()
        {
            return Db.Query(@"SELECT i.Id, i.Nombre, ISNULL(c.Nombre, N'Sin categoría') AS Categoria,
                                     i.Stock, i.Unidad, i.StockMinimo, i.CostoUnitario
                              FROM dbo.Insumo i
                              LEFT JOIN dbo.CategoriaInsumo c ON c.Id = i.CategoriaInsumoId
                              WHERE i.Activo = 1
                              ORDER BY i.Nombre",
                rd => new Insumo(Db.Str(rd, "Nombre"), Db.Str(rd, "Categoria"), Db.Dec(rd, "Stock"), Db.Str(rd, "Unidad"),
                                 Db.Dec(rd, "StockMinimo"), Db.Dec(rd, "CostoUnitario")) { Id = Db.Int(rd, "Id") });
        }

        /// <summary>Categorías de insumo existentes (para los filtros y el diálogo).</summary>
        public static List<string> Categories()
        {
            try { return Db.Query("SELECT Nombre FROM dbo.CategoriaInsumo ORDER BY Nombre", rd => Db.Str(rd, "Nombre")); }
            catch { return new List<string>(); }
        }

        /// <summary>Nombre → unidad de los insumos activos (para vincular insumos a un plato).</summary>
        public static Dictionary<string, string> NamesWithUnit()
        {
            try
            {
                return Db.Query("SELECT Nombre, Unidad FROM dbo.Insumo WHERE Activo = 1 ORDER BY Nombre",
                        rd => new { N = Db.Str(rd, "Nombre"), U = Db.Str(rd, "Unidad") })
                    .ToDictionary(x => x.N, x => x.U, StringComparer.OrdinalIgnoreCase);
            }
            catch { return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); }
        }

        /// <summary>Crea o actualiza un insumo. Devuelve el mensaje de error o null si todo salió bien.</summary>
        public static string Save(Insumo ins, string oldName = null)
        {
            string unit = Unidades.Normalizar(ins.Unit);
            if (unit == null) return "Unidad no válida. Usa: " + string.Join(", ", Unidades.Todas) + ".";
            ins.Unit = unit;

            try
            {
                using (var cn = Db.Open())
                using (var tx = cn.BeginTransaction())
                {
                    object existing = Db.Cmd(cn, tx, "SELECT Id FROM dbo.Insumo WHERE Nombre = @n AND Id <> @id AND Activo = 1",
                        "@n", ins.Name, "@id", ins.Id).ExecuteScalar();
                    if (existing != null && existing != DBNull.Value)
                    {
                        tx.Rollback();
                        return "Ya existe un insumo llamado \"" + ins.Name + "\".";
                    }

                    if (ins.Id == 0)
                    {
                        // Si existió y se dio de baja, se reactiva (las recetas pueden seguir apuntándolo).
                        object old = Db.Cmd(cn, tx, "SELECT Id FROM dbo.Insumo WHERE Nombre = @n", "@n", ins.Name).ExecuteScalar();
                        if (old != null && old != DBNull.Value) ins.Id = Convert.ToInt32(old);
                    }

                    object categoriaId = string.IsNullOrWhiteSpace(ins.Category) ? null : Db.Cmd(cn, tx,
                        @"DECLARE @c INT = (SELECT Id FROM dbo.CategoriaInsumo WHERE Nombre = @n);
                          IF @c IS NULL BEGIN INSERT INTO dbo.CategoriaInsumo (Nombre) VALUES (@n); SET @c = SCOPE_IDENTITY(); END;
                          SELECT @c;", "@n", ins.Category.Trim()).ExecuteScalar();

                    decimal previousStock = 0m;
                    bool isNew = ins.Id == 0;
                    if (isNew)
                    {
                        ins.Id = Convert.ToInt32(Db.Cmd(cn, tx,
                            @"INSERT INTO dbo.Insumo (Nombre, CategoriaInsumoId, Unidad, Stock, StockMinimo, CostoUnitario)
                              OUTPUT inserted.Id VALUES (@n, @c, @u, 0, @min, @cost)",
                            "@n", ins.Name, "@c", categoriaId, "@u", unit, "@min", ins.Min, "@cost", ins.Cost).ExecuteScalar());
                    }
                    else
                    {
                        previousStock = Convert.ToDecimal(Db.Cmd(cn, tx, "SELECT Stock FROM dbo.Insumo WHERE Id = @id", "@id", ins.Id).ExecuteScalar());
                        Db.Cmd(cn, tx,
                            @"UPDATE dbo.Insumo SET Nombre = @n, CategoriaInsumoId = @c, Unidad = @u, StockMinimo = @min,
                                                    CostoUnitario = @cost, Activo = 1
                              WHERE Id = @id",
                            "@n", ins.Name, "@c", categoriaId, "@u", unit, "@min", ins.Min, "@cost", ins.Cost, "@id", ins.Id).ExecuteNonQuery();
                    }

                    decimal diff = ins.Stock - previousStock;
                    if (diff != 0)
                    {
                        Db.Cmd(cn, tx,
                            @"INSERT INTO dbo.MovimientoInventario (InsumoId, Tipo, Cantidad, UsuarioId, Nota) VALUES (@i, @t, @q, @u, @nota);
                              UPDATE dbo.Insumo SET Stock = Stock + @q WHERE Id = @i;",
                            "@i", ins.Id, "@t", previousStock == 0m && diff > 0 ? "Entrada" : "Ajuste", "@q", diff,
                            "@u", Sesion.UsuarioId > 0 ? (object)Sesion.UsuarioId : null,
                            "@nota", "Ajuste manual desde Inventario").ExecuteNonQuery();
                    }

                    tx.Commit();
                }
            }
            catch (Exception ex)
            {
                return ex.Message;
            }

            if (!string.IsNullOrEmpty(oldName) && !string.Equals(oldName, ins.Name, StringComparison.Ordinal))
                MenuStore.RenameInsumoLinks(oldName, ins.Name);
            return null;
        }

        /// <summary>Cuántos platos usan el insumo en su receta.</summary>
        public static int RecipeCount(Insumo ins)
        {
            try { return Convert.ToInt32(Db.Scalar("SELECT COUNT(*) FROM dbo.ProductoInsumo WHERE InsumoId = @i", "@i", ins.Id)); }
            catch { return 0; }
        }

        /// <summary>Da de baja el insumo y lo quita de las recetas.</summary>
        public static string Delete(Insumo ins)
        {
            try
            {
                Db.Execute(@"DELETE FROM dbo.ProductoInsumo WHERE InsumoId = @i;
                             UPDATE dbo.Insumo SET Activo = 0 WHERE Id = @i;", "@i", ins.Id);
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
            MenuStore.RemoveInsumoLinks(ins.Name);
            return null;
        }
    }
}
