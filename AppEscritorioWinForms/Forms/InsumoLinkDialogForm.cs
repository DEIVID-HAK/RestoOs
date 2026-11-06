using System;
using System.Globalization;
using System.Windows.Forms;
using System.Collections.Generic;
using app_escritorio.Data;
using app_escritorio.Models;
using app_escritorio.UI;

namespace app_escritorio.Forms
{
    /// <summary>Vincula un insumo del almacén a un plato (cantidad que se descuenta por cada venta).</summary>
    public partial class InsumoLinkDialogForm : RDialogForm
    {
        public InsumoLink Result { get; private set; }

        /// <summary>Insumos del inventario (nombre → unidad).</summary>
        private readonly Dictionary<string, string> _insumos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public InsumoLinkDialogForm()
        {
            InitializeComponent();
            cmbUnidad.Text = "kg";
            if (UiHelpers.IsDesignTime) return;

            // La lista sale del inventario real; si se escribe un insumo nuevo, se crea al publicar la carta.
            foreach (var kv in InsumoStore.NamesWithUnit()) _insumos[kv.Key] = kv.Value;
            if (_insumos.Count > 0)
            {
                cmbInsumo.Items.Clear();
                foreach (var name in _insumos.Keys) cmbInsumo.Items.Add(name);
            }
            cmbInsumo.SelectedIndexChanged += (s, e) => SuggestUnit();
            cmbInsumo.Leave += (s, e) => SuggestUnit();
        }

        /// <summary>Al elegir un insumo existente se propone su unidad del inventario.</summary>
        private void SuggestUnit()
        {
            if (_insumos.TryGetValue(cmbInsumo.Text.Trim(), out var unit)) cmbUnidad.Text = unit;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            cmbInsumo.Focus();
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            string nombre = cmbInsumo.Text.Trim();
            if (nombre.Length == 0)
            {
                MessageBox.Show("Elige o escribe el insumo.", "RestoOS", MessageBoxButtons.OK, MessageBoxIcon.Information);
                cmbInsumo.Focus();
                return;
            }
            string raw = txtCantidad.Text.Trim().Replace(",", ".");
            if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal cantidad) || cantidad <= 0)
            {
                MessageBox.Show("Cantidad inválida. Ejemplo: 0,25", "RestoOS", MessageBoxButtons.OK, MessageBoxIcon.Information);
                txtCantidad.Focus();
                return;
            }
            string unidad = Unidades.Normalizar(string.IsNullOrWhiteSpace(cmbUnidad.Text) ? "und" : cmbUnidad.Text);
            if (unidad == null)
            {
                MessageBox.Show("Unidad no válida. Usa: " + string.Join(", ", Unidades.Todas) + ".", "RestoOS", MessageBoxButtons.OK, MessageBoxIcon.Information);
                cmbUnidad.Focus();
                return;
            }
            Result = new InsumoLink { Nombre = nombre, Cantidad = cantidad, Unidad = unidad };
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
