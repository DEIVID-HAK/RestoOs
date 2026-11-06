using System;
using System.Collections.Generic;

namespace app_escritorio.Data
{
    /// <summary>
    /// Módulos activados/desactivados (tabla Modulo). Lo usan la pantalla Módulos, el sidebar y el POS.
    /// </summary>
    public static class ModuleStore
    {
        /// <summary>Claves de los módulos opcionales (el POS siempre está activo).</summary>
        public static readonly string[] Keys = { "salon", "menu", "kds", "inventario", "reservas", "delivery", "reportes" };

        private static Dictionary<string, bool> _store;

        /// <summary>Se dispara al activar/desactivar un módulo (clave, activo).</summary>
        public static event Action<string, bool> ModuleStateChanged;

        public static Dictionary<string, bool> Store
        {
            get
            {
                if (_store != null) return _store;
                _store = new Dictionary<string, bool>
                {
                    ["salon"] = true, ["menu"] = true, ["kds"] = false, ["inventario"] = true,
                    ["reservas"] = true, ["reportes"] = true, ["delivery"] = true
                };
                try
                {
                    foreach (var (clave, activo) in Db.Query("SELECT Clave, Activo FROM dbo.Modulo",
                                 rd => (Db.Str(rd, "Clave"), Db.Bool(rd, "Activo"))))
                        _store[clave.Trim().ToLowerInvariant()] = activo;
                }
                catch { }
                return _store;
            }
        }

        public static bool IsEnabled(string key) => key == "pos" || (Store.TryGetValue(key, out bool on) && on);

        public static void SetEnabled(string key, bool enabled)
        {
            if (key == "pos" || (IsEnabled(key) == enabled && Store.ContainsKey(key))) return;
            try
            {
                Db.Execute(@"UPDATE dbo.Modulo SET Activo = @a WHERE Clave = @k;
                             IF @@ROWCOUNT = 0 INSERT INTO dbo.Modulo (Clave, Nombre, Activo) VALUES (@k, @k, @a);",
                    "@k", key, "@a", enabled);
            }
            catch (Exception ex)
            {
                Db.ShowError("guardar el estado del módulo", ex);
                return;
            }
            Store[key] = enabled;
            ModuleStateChanged?.Invoke(key, enabled);
        }
    }
}
