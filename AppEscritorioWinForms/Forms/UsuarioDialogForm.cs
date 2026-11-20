using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using app_escritorio.Data;
using app_escritorio.UI;

namespace app_escritorio.Forms
{
    /// <summary>
    /// Alta y edición de un usuario de dbo.Usuario (nombre, rol y PIN).
    /// Con <see cref="UsuarioRow"/> null crea uno nuevo; el PIN es obligatorio al crear y opcional al editar
    /// (vacío = conservar el actual). Los roles salen de la tabla Rol.
    /// </summary>
    public partial class UsuarioDialogForm : RDialogForm
    {
        private readonly UsuarioRow _usuario;
        private readonly List<(int Id, string Nombre)> _roles;

        public UsuarioDialogForm() : this(null) { }

        public UsuarioDialogForm(UsuarioRow usuario)
        {
            InitializeComponent();
            _usuario = usuario;

            if (UiHelpers.IsDesignTime)
            {
                cmbRol.Items.Add("Administrador");
                cmbRol.SelectedIndex = 0;
                return;
            }

            try
            {
                _roles = UsuarioStore.Roles();
            }
            catch (Exception ex)
            {
                Db.ShowError("cargar los roles", ex);
                _roles = new List<(int Id, string Nombre)>();
            }

            bool edit = _usuario != null;
            Text = edit ? "Editar usuario" : "Nuevo usuario";
            lblTitle.Text = Text;

            foreach (var rol in _roles) cmbRol.Items.Add(rol.Nombre);

            if (edit)
            {
                txtNombre.Text = _usuario.Nombre;
                int index = _roles.FindIndex(r => r.Id == _usuario.RolId);
                cmbRol.SelectedIndex = index >= 0 ? index : 0;
                lblPinHint.Text = "Deja el PIN vacío para conservar el actual. Si lo cambias, debe tener 4 a 8 dígitos.";
            }
            else
            {
                cmbRol.SelectedIndex = cmbRol.Items.Count > 0 ? 0 : -1;
                lblPinHint.Text = "El PIN es obligatorio y debe tener entre 4 y 8 dígitos numéricos.";
            }
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            string nombre = txtNombre.Text.Trim();
            if (nombre.Length == 0)
            {
                MessageBox.Show("Escribe el nombre del usuario.", "Usuarios", MessageBoxButtons.OK, MessageBoxIcon.Information);
                txtNombre.Focus();
                return;
            }

            if (cmbRol.SelectedIndex < 0 || cmbRol.SelectedIndex >= _roles.Count)
            {
                MessageBox.Show("Selecciona un rol.", "Usuarios", MessageBoxButtons.OK, MessageBoxIcon.Information);
                cmbRol.Focus();
                return;
            }

            bool edit = _usuario != null;
            string pin = txtPin.Text.Trim();

            if (!edit && pin.Length == 0)
            {
                MessageBox.Show("El PIN es obligatorio para un usuario nuevo.", "Usuarios", MessageBoxButtons.OK, MessageBoxIcon.Information);
                txtPin.Focus();
                return;
            }

            if (pin.Length > 0 && (pin.Length < 4 || pin.Length > 8 || !pin.All(char.IsDigit)))
            {
                MessageBox.Show("El PIN debe tener entre 4 y 8 dígitos numéricos.", "Usuarios", MessageBoxButtons.OK, MessageBoxIcon.Information);
                txtPin.Focus();
                return;
            }

            try
            {
                if (UsuarioStore.NombreEnUso(nombre, edit ? _usuario.Id : 0))
                {
                    MessageBox.Show("Ya existe un usuario con ese nombre.", "Usuarios", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    txtNombre.Focus();
                    return;
                }

                int rolId = _roles[cmbRol.SelectedIndex].Id;
                string rolNombre = _roles[cmbRol.SelectedIndex].Nombre;

                // No dejar el sistema sin administradores activos.
                if (edit && _usuario.Activo && _usuario.Rol == "Administrador" && rolNombre != "Administrador"
                    && UsuarioStore.AdministradoresActivos(_usuario.Id) == 0)
                {
                    MessageBox.Show("Este es el último administrador activo: no se puede cambiar de rol.",
                        "Usuarios", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                if (edit)
                {
                    UsuarioStore.Actualizar(_usuario.Id, nombre, rolId);
                    if (pin.Length > 0) UsuarioStore.CambiarPin(_usuario.Id, pin);
                }
                else
                {
                    UsuarioStore.Crear(nombre, rolId, pin);
                }

                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                Db.ShowError(edit ? "guardar el usuario" : "crear el usuario", ex);
            }
        }
    }
}
