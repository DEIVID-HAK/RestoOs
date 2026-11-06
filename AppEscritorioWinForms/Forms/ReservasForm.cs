using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using app_escritorio.Data;
using app_escritorio.Models;
using app_escritorio.UI;

namespace app_escritorio.Forms
{
    /// <summary>
    /// Reservas (migración de ReservasView.xaml de WPF): calendario a la izquierda y agenda del día a la derecha.
    /// Las reservas se guardan en SQL Server (ReservaStore); el cliente queda registrado en la tabla Cliente.
    /// </summary>
    public partial class ReservasForm : Form
    {
        private static readonly CultureInfo Co = CultureInfo.GetCultureInfo("es-CO");
        private List<Reserva> _day = new List<Reserva>();

        public ReservasForm()
        {
            InitializeComponent();
            if (UiHelpers.IsDesignTime) return;

            cmbEstado.SelectedIndex = 0;
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            DateTime day = calendar.SelectedDate;
            lblFecha.Text = day == DateTime.Today ? "Hoy · " + day.ToString("dddd d MMM", Co) : day.ToString("dddd d MMM yyyy", Co);
            string text = txtSearch.Text.Trim().ToLowerInvariant();
            string estado = cmbEstado.SelectedItem?.ToString() ?? "Todos";

            try
            {
                _day = ReservaStore.LoadDay(day);
            }
            catch (Exception ex)
            {
                Db.ShowError("cargar las reservas", ex);
                _day = new List<Reserva>();
            }

            grid.Rows.Clear();
            foreach (var r in _day.Where(r => (estado == "Todos" || r.Estado == estado)
                                              && (text.Length == 0 || r.Cliente.ToLowerInvariant().Contains(text) || r.Mesa.ToLowerInvariant().Contains(text)))
                                  .OrderBy(r => r.Fecha))
            {
                int i = grid.Rows.Add(r.HoraText, r.Cliente, r.PersonasText, r.Mesa, r.Estado, r.Telefono);
                grid.Rows[i].Tag = r;
                grid.Rows[i].Cells[colEstado.Index].Style.ForeColor = r.EstadoColor;
            }
        }

        private void Filter_Changed(object sender, EventArgs e)
        {
            if (!UiHelpers.IsDesignTime) ApplyFilter();
        }

        private void Calendar_DateChanged(object sender, EventArgs e)
        {
            if (!UiHelpers.IsDesignTime) ApplyFilter();
        }

        /// <summary>Guarda y muestra el día de la reserva.</summary>
        private void SaveAndShow(Reserva r)
        {
            try
            {
                ReservaStore.Save(r);
            }
            catch (Exception ex)
            {
                Db.ShowError("guardar la reserva", ex);
            }
            if (calendar.SelectedDate.Date == r.Fecha.Date) ApplyFilter();
            else calendar.SelectedDate = r.Fecha.Date; // refresca la agenda de ese día
        }

        private void BtnAdd_Click(object sender, EventArgs e)
        {
            using (var dlg = new ReservaDialogForm(null, calendar.SelectedDate))
                if (dlg.ShowDialog(TopLevelControl ?? this) == DialogResult.OK && dlg.Result != null)
                    SaveAndShow(dlg.Result);
        }

        private void Grid_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || !(grid.Rows[e.RowIndex].Tag is Reserva r)) return;

            if (e.ColumnIndex == colEditar.Index)
            {
                using (var dlg = new ReservaDialogForm(r, r.Fecha.Date))
                    if (dlg.ShowDialog(TopLevelControl ?? this) == DialogResult.OK && dlg.Result != null)
                        SaveAndShow(dlg.Result);
            }
            else if (e.ColumnIndex == colDelete.Index)
            {
                if (MessageBox.Show("¿Eliminar la reserva de " + r.Cliente + "?", "RestoOS", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    try { ReservaStore.Delete(r); }
                    catch (Exception ex) { Db.ShowError("eliminar la reserva", ex); }
                    ApplyFilter();
                }
            }
        }
    }
}
