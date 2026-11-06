using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using app_escritorio.Data;
using app_escritorio.Models;
using app_escritorio.UI;
using app_escritorio.Utils;

namespace app_escritorio.Forms
{
    /// <summary>
    /// Inventario (migración de InventarioView.xaml de WPF): insumos con filtros, estado de stock y acciones.
    /// Los insumos se leen y guardan en SQL Server (InsumoStore); cada venta los descuenta según la receta del plato.
    /// </summary>
    public partial class InventarioForm : Form
    {
        private List<Insumo> _all = new List<Insumo>();
        private bool _loading;

        public InventarioForm()
        {
            InitializeComponent();
            if (UiHelpers.IsDesignTime) return;

            Reload();
        }

        /// <summary>Relee los insumos (el Shell lo llama al mostrar la pantalla: las ventas cambian el stock).</summary>
        public void Reload()
        {
            if (IsDisposed || Disposing) return;
            try
            {
                _all = InsumoStore.Load();
            }
            catch (Exception ex)
            {
                Db.ShowError("cargar el inventario", ex);
                _all = new List<Insumo>();
            }
            LoadCategories();
            ApplyFilter();
        }

        /// <summary>Categorías del filtro: las de la base más las de los insumos cargados.</summary>
        private void LoadCategories()
        {
            _loading = true;
            string selected = cmbCategory.SelectedItem?.ToString();
            cmbCategory.Items.Clear();
            cmbCategory.Items.Add("Todas");
            foreach (var c in InsumoStore.Categories().Concat(_all.Select(i => i.Category)).Distinct().OrderBy(c => c))
                cmbCategory.Items.Add(c);
            int idx = selected == null ? 0 : cmbCategory.Items.IndexOf(selected);
            cmbCategory.SelectedIndex = idx < 0 ? 0 : idx;
            _loading = false;
        }

        private void ApplyFilter()
        {
            string text = txtSearch.Text.Trim().ToLowerInvariant();
            string cat = cmbCategory.SelectedItem?.ToString() ?? "Todas";
            bool lowOnly = swLowOnly.Checked;

            grid.Rows.Clear();
            foreach (var i in _all)
            {
                if (cat != "Todas" && i.Category != cat) continue;
                if (text.Length > 0 && !i.Name.ToLowerInvariant().Contains(text)) continue;
                if (lowOnly && !i.IsLow) continue;

                int r = grid.Rows.Add(i.Name, i.Category, i.StockText, i.MinText, i.Estado, i.CostText);
                var row = grid.Rows[r];
                row.Tag = i;
                row.Cells[colStock.Index].Style.ForeColor = i.IsLow ? i.EstadoColor : Theme.OnSurface;
                row.Cells[colStock.Index].Style.Font = Theme.GetFont(9.75F, FontStyle.Bold);
                row.Cells[colEstado.Index].Style.ForeColor = i.EstadoColor;
            }

            int alerts = _all.Count(i => i.IsLow);
            lblAlerts.Text = alerts == 1 ? "1 alerta stock bajo" : alerts + " alertas stock bajo";
            pnlAlert.Visible = alerts > 0;
        }

        private void Filter_Changed(object sender, EventArgs e)
        {
            if (!UiHelpers.IsDesignTime && !_loading) ApplyFilter();
        }

        private void BtnAdd_Click(object sender, EventArgs e)
        {
            using (var dlg = new InsumoDialogForm(null))
                if (dlg.ShowDialog(TopLevelControl ?? this) == DialogResult.OK && dlg.Result != null)
                    SaveInsumo(dlg.Result, null);
        }

        private void SaveInsumo(Insumo ins, string oldName)
        {
            string error = InsumoStore.Save(ins, oldName);
            if (error != null)
                MessageBox.Show("No se pudo guardar el insumo.\n\n" + error, "Inventario", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Reload();
        }

        private void Grid_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || !(grid.Rows[e.RowIndex].Tag is Insumo ins)) return;

            if (e.ColumnIndex == colAjustar.Index)
            {
                using (var dlg = new InsumoDialogForm(ins))
                    if (dlg.ShowDialog(TopLevelControl ?? this) == DialogResult.OK && dlg.Result != null)
                        SaveInsumo(dlg.Result, ins.Name);
            }
            else if (e.ColumnIndex == colDelete.Index)
            {
                int platos = InsumoStore.RecipeCount(ins);
                string question = "¿Eliminar " + ins.Name + "?" +
                                  (platos > 0 ? "\n\nEstá en la receta de " + platos + " plato(s); se quitará de esas recetas." : "");
                if (MessageBox.Show(question, "RestoOS", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                string error = InsumoStore.Delete(ins);
                if (error != null)
                    MessageBox.Show("No se pudo eliminar el insumo.\n\n" + error, "Inventario", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Reload();
            }
        }
    }
}
