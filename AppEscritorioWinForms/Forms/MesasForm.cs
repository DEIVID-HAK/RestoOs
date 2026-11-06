using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using app_escritorio.Data;
using app_escritorio.UI;
using app_escritorio.Utils;
using app_escritorio.Views.Mesas;

namespace app_escritorio.Forms
{
    /// <summary>
    /// Mesas y Salón (migración de MesasView.xaml de WPF): plano de mesas por salón, detalle y apertura en el POS.
    /// Las mesas y sus estados se leen de SQL Server (MesaStore); el POS las marca "Ocupada" al abrir ticket y "Por limpiar" al cobrar.
    /// El diseño está en MesasForm.Designer.cs y en TableCard; aquí solo va la lógica.
    /// </summary>
    public partial class MesasForm : Form
    {
        private List<MesaInfo> _mesas = new List<MesaInfo>();
        private MesaInfo _selected;

        /// <summary>Pide abrir el POS con esta mesa (lo atiende ShellForm).</summary>
        public event Action<string> MesaParaPos;

        public MesasForm()
        {
            InitializeComponent();
            if (UiHelpers.IsDesignTime) return;

            cmbSector.SelectedIndex = 0;
            TicketStore.TicketChanged += TicketStore_TicketChanged;
            HandleDestroyed += (s, e) => TicketStore.TicketChanged -= TicketStore_TicketChanged;
            RefreshView();
        }

        /// <summary>Relee las mesas de la base y vuelve a pintar tarjetas y detalle (estados y totales del POS al día).</summary>
        public void RefreshView()
        {
            if (IsDisposed || Disposing) return;
            _mesas = MesaStore.Load();
            RenderTables();
            if (_selected != null) ShowDetail(_mesas.FirstOrDefault(m => m.Id == _selected.Id));
        }

        private void TicketStore_TicketChanged()
        {
            if (IsDisposed || Disposing || !Visible) return;
            RefreshView();
        }

        // ===================== Tarjetas =====================

        private void RenderTables()
        {
            if (IsDisposed || Disposing || flowTables == null || flowTables.IsDisposed) return;
            string sector = cmbSector?.SelectedItem?.ToString() ?? "Todos los salones";

            flowTables.SuspendLayout();
            foreach (Control c in flowTables.Controls.Cast<Control>().ToList()) c.Dispose();
            flowTables.Controls.Clear();

            foreach (var mesa in _mesas.Where(m => sector == "Todos los salones" || m.Sector == sector))
            {
                var card = new TableCard();
                card.SetMesa(mesa);
                card.CardClicked += (s, e) => ShowDetail(mesa);
                card.OpenClicked += (s, e) => OpenInPos(mesa);
                card.EditClicked += (s, e) => EditMesa(mesa);
                card.DeleteClicked += (s, e) => DeleteMesa(mesa);
                flowTables.Controls.Add(card);
            }
            flowTables.ResumeLayout(true);
        }

        private void CmbSector_SelectedIndexChanged(object sender, EventArgs e) => RenderTables();

        // ===================== Detalle =====================

        private void ShowDetail(MesaInfo mesa)
        {
            _selected = mesa;
            if (IsDisposed || Disposing) return;
            if (lblDetailName == null || lblDetailName.IsDisposed) return;
            if (mesa == null)
            {
                lblDetailName.Text = "Selecciona una mesa";
                lblDetailStatus.Text = string.Empty;
                lblDetailInfo.Text = string.Empty;
                lblDetailItems.Text = "Sin pedido activo.";
                lblDetailTotal.Text = string.Empty;
                btnOpenPos.Enabled = false;
                return;
            }

            lblDetailName.Text = mesa.Name;
            lblDetailStatus.Text = mesa.Status;
            lblDetailStatus.ColorOverride = mesa.StatusColor;
            lblDetailInfo.Text = mesa.Sector + " · " + mesa.Capacity;

            var ticket = TicketStore.GetTicketFor(mesa.Name);
            if (ticket.Count == 0)
                lblDetailItems.Text = "Sin pedido activo.";
            else
                lblDetailItems.Text = string.Join(Environment.NewLine, ticket.Select(l =>
                    l.Name + (l.Quantity > 1 ? " x" + l.Quantity : "") + "  " + l.TotalText +
                    (string.IsNullOrEmpty(l.Notes) ? "" : " (" + l.Notes + ")")));

            lblDetailTotal.Text = mesa.Total;
            btnOpenPos.Enabled = mesa.CanOpen;
        }

        private void BtnOpenPos_Click(object sender, EventArgs e)
        {
            if (_selected != null) OpenInPos(_selected);
        }

        private void OpenInPos(MesaInfo mesa)
        {
            if (mesa == null || !mesa.CanOpen) return;
            ShowDetail(mesa);
            MesaParaPos?.Invoke(mesa.Name);
        }

        // ===================== CRUD =====================

        private void BtnNew_Click(object sender, EventArgs e)
        {
            using (var dialog = new TableDialog(null))
            {
                if (dialog.ShowDialog(TopLevelControl ?? this) != DialogResult.OK || dialog.Result == null) return;
                if (_mesas.Any(m => m.Name.Equals(dialog.Result.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    MessageBox.Show("Ya existe una mesa con ese nombre.", "RestoOS", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                _mesas.Add(dialog.Result);
                MesaStore.Save(_mesas);
                RefreshView();
            }
        }

        private void EditMesa(MesaInfo mesa)
        {
            using (var dialog = new TableDialog(mesa))
            {
                if (dialog.ShowDialog(TopLevelControl ?? this) != DialogResult.OK || dialog.Result == null) return;
                if (_mesas.Any(m => m != mesa && m.Name.Equals(dialog.Result.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    MessageBox.Show("Ya existe otra mesa con ese nombre.", "RestoOS", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                int index = _mesas.IndexOf(mesa);
                if (index < 0) return;
                string oldName = mesa.Name;
                _mesas[index] = dialog.Result;
                if (!string.Equals(oldName, dialog.Result.Name, StringComparison.Ordinal))
                    TicketStore.Rename(oldName, dialog.Result.Name);
                MesaStore.Save(_mesas);
                if (_selected == mesa) _selected = dialog.Result;
                RefreshView();
            }
        }

        private void DeleteMesa(MesaInfo mesa)
        {
            if (MessageBox.Show("¿Eliminar " + mesa.Name + "?", "RestoOS", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _mesas.Remove(mesa);
            TicketStore.RemoveTicket(mesa.Name);
            MesaStore.Save(_mesas);
            if (_selected == mesa) ShowDetail(null);
            RefreshView();
        }
    }
}
