using System;
using System.Collections.Generic;
using System.Windows.Forms;
using app_escritorio.Data;
using app_escritorio.UI;
using app_escritorio.Utils;

namespace app_escritorio.Forms
{
    /// <summary>
    /// Gestión de usuarios (reemplaza la pantalla Usuarios del sistema anterior, ahora sobre dbo.Usuario).
    /// Lista con búsqueda y filtros, alta/edición con rol y PIN, y activación/desactivación lógica.
    /// Solo la abre el Administrador desde el sidebar.
    /// </summary>
    public partial class UsuariosForm : Form
    {
        private List<UsuarioRow> _usuarios = new List<UsuarioRow>();
        private List<(int Id, string Nombre)> _roles = new List<(int Id, string Nombre)>();
        private bool _loading;

        public UsuariosForm()
        {
            InitializeComponent();
            if (UiHelpers.IsDesignTime) return;
            LoadData();
        }

        /// <summary>Recarga roles y usuarios. La llama el Shell cada vez que se muestra la sección.</summary>
        public void LoadData()
        {
            _loading = true;
            try
            {
                try
                {
                    _roles = UsuarioStore.Roles();
                }
                catch (Exception ex)
                {
                    Db.ShowError("cargar los roles", ex);
                    return;
                }

                int selected = cmbRol.SelectedIndex;
                cmbRol.Items.Clear();
                cmbRol.Items.Add("Todos los roles");
                foreach (var rol in _roles) cmbRol.Items.Add(rol.Nombre);
                cmbRol.SelectedIndex = selected >= 0 && selected < cmbRol.Items.Count ? selected : 0;

                Reload();
            }
            finally
            {
                _loading = false;
            }
        }

        private void Reload()
        {
            try
            {
                _usuarios = UsuarioStore.Listar();
            }
            catch (Exception ex)
            {
                Db.ShowError("cargar los usuarios", ex);
                return;
            }
            Render();
        }

        private void Render()
        {
            string texto = txtSearch.Text.Trim();
            string rol = cmbRol.SelectedIndex > 0 ? Convert.ToString(cmbRol.SelectedItem) : null;
            bool verInactivos = swShowInactive.Checked;

            grid.Rows.Clear();
            int mostrados = 0;

            foreach (var u in _usuarios)
            {
                if (!verInactivos && !u.Activo) continue;
                if (rol != null && !string.Equals(u.Rol, rol, StringComparison.OrdinalIgnoreCase)) continue;
                if (texto.Length > 0 &&
                    u.Nombre.IndexOf(texto, StringComparison.OrdinalIgnoreCase) < 0 &&
                    u.Rol.IndexOf(texto, StringComparison.OrdinalIgnoreCase) < 0) continue;

                int i = grid.Rows.Add(
                    u.Nombre,
                    u.Rol,
                    u.Activo ? "Activo" : "Inactivo",
                    u.TienePin ? "Sí" : "No",
                    u.FechaCreacion.ToString("dd/MM/yyyy"),
                    "Editar",
                    u.Activo ? "Desactivar" : "Activar");

                grid.Rows[i].Tag = u;
                if (!u.Activo) grid.Rows[i].DefaultCellStyle.ForeColor = Theme.OnSurfaceVariant;
                mostrados++;
            }

            lblCount.Text = mostrados + " de " + _usuarios.Count + " usuario(s)";
        }

        private void Filter_Changed(object sender, EventArgs e)
        {
            if (_loading) return;
            Render();
        }

        private void BtnAdd_Click(object sender, EventArgs e)
        {
            using (var dlg = new UsuarioDialogForm())
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                Reload();
            }
        }

        private void Grid_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= grid.Rows.Count) return;
            if (!(grid.Rows[e.RowIndex].Tag is UsuarioRow usuario)) return;

            if (e.ColumnIndex == colEditar.Index) Editar(usuario);
            else if (e.ColumnIndex == colToggle.Index) CambiarEstado(usuario);
        }

        private void Editar(UsuarioRow usuario)
        {
            using (var dlg = new UsuarioDialogForm(usuario))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                Reload();
            }
        }

        private void CambiarEstado(UsuarioRow usuario)
        {
            bool activar = !usuario.Activo;

            if (!activar && usuario.Id == Sesion.UsuarioId)
            {
                MessageBox.Show("No puedes desactivar el usuario con el que iniciaste sesión.",
                    "Usuarios", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!activar && usuario.Rol == "Administrador" && UsuarioStore.AdministradoresActivos(usuario.Id) == 0)
            {
                MessageBox.Show("Es el último administrador activo: crea o activa otro antes de desactivarlo.",
                    "Usuarios", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string accion = activar ? "activar" : "desactivar";
            var respuesta = MessageBox.Show("¿Seguro que quieres " + accion + " a \"" + usuario.Nombre + "\"?",
                "Usuarios", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (respuesta != DialogResult.Yes) return;

            try
            {
                UsuarioStore.SetActivo(usuario.Id, activar);
                Reload();
            }
            catch (Exception ex)
            {
                Db.ShowError(accion + " el usuario", ex);
            }
        }
    }
}
