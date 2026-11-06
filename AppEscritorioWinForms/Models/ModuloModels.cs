using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using app_escritorio.Utils;

namespace app_escritorio.Models
{
    // Modelos de los módulos KDS, Inventario, Reservas, Delivery y Reportes.
    // Inventario, Reservas y Reportes se leen de SQL Server (Data/*Store); KDS y Delivery siguen siendo DEMO en memoria.

    internal static class Fmt
    {
        public static readonly CultureInfo Co = CultureInfo.GetCultureInfo("es-CO");
        public static string Money(decimal v) => v.ToString("C0", Co);
    }

    // ===================== Cocina KDS =====================

    public enum KdsStatus { Nuevo, Preparacion, Listo }

    public class KdsItem
    {
        public string Line { get; set; }
        public string Note { get; set; }
        public KdsItem(string line, string note) { Line = line; Note = note ?? string.Empty; }
    }

    public class KdsOrder
    {
        public string Mesa { get; set; }
        public string Station { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<KdsItem> Items { get; } = new List<KdsItem>();
        public KdsStatus Status { get; set; } = KdsStatus.Nuevo;

        public KdsOrder(string mesa, string station, DateTime created, params KdsItem[] items)
        {
            Mesa = mesa; Station = station; CreatedAt = created;
            Items.AddRange(items);
        }

        public string CreatedText => "Pedido " + CreatedAt.ToString("HH:mm");

        public string Elapsed
        {
            get
            {
                int m = (int)(DateTime.Now - CreatedAt).TotalMinutes;
                return m < 1 ? "ahora" : "hace " + m + "m";
            }
        }

        /// <summary>Verde hasta 10 min, ámbar hasta 15, rojo después (igual que WPF).</summary>
        public Color ElapsedColor
        {
            get
            {
                double m = (DateTime.Now - CreatedAt).TotalMinutes;
                return m > 15 ? Theme.Error : m > 10 ? Theme.Secondary : Theme.Tertiary;
            }
        }
    }

    // ===================== Inventario =====================

    public class Insumo
    {
        /// <summary>Id en la tabla Insumo (0 = nuevo).</summary>
        public int Id { get; set; }
        public string Name { get; set; }
        public string Category { get; set; }
        public decimal Stock { get; set; }
        public string Unit { get; set; }
        public decimal Min { get; set; }
        public decimal Cost { get; set; }

        public Insumo(string name, string category, decimal stock, string unit, decimal min, decimal cost)
        {
            Name = name; Category = category; Stock = stock; Unit = unit; Min = min; Cost = cost;
        }

        public string StockText => Stock.ToString("0.##", Fmt.Co) + " " + Unit;
        public string MinText => Min.ToString("0.##", Fmt.Co) + " " + Unit;
        public string CostText => Fmt.Money(Cost);
        public bool IsLow => Stock <= Min;
        public bool IsCritical => Stock <= Min * 0.5m;
        public string Estado => IsCritical ? "Crítico" : IsLow ? "Bajo" : "OK";
        public Color EstadoColor => IsCritical ? Theme.Error : IsLow ? Theme.Secondary : Theme.Tertiary;
    }

    // ===================== Reservas =====================

    public class Reserva
    {
        /// <summary>Id en la tabla Reserva (0 = nueva).</summary>
        public int Id { get; set; }
        public DateTime Fecha { get; set; }
        public string Cliente { get; set; }
        public int Personas { get; set; }
        public string Mesa { get; set; }
        public string Estado { get; set; }
        public string Telefono { get; set; }

        public Reserva(DateTime fecha, string cliente, int personas, string mesa, string estado, string tel)
        {
            Fecha = fecha; Cliente = cliente; Personas = personas; Mesa = mesa; Estado = estado; Telefono = tel;
        }

        public static readonly string[] Estados = { "Confirmada", "En curso", "Cancelada" };
        public string HoraText => Fecha.ToString("HH:mm");
        public string PersonasText => Personas + " p";
        public Color EstadoColor => Estado == "Confirmada" ? Theme.Tertiary : Estado == "En curso" ? Theme.Secondary : Theme.Error;
    }

    // ===================== Delivery =====================

    public class PedidoDelivery
    {
        public DateTime Fecha { get; set; }
        public string Plataforma { get; set; }
        public string Id { get; set; }
        public string Cliente { get; set; }
        public string Detalle { get; set; }
        public decimal Total { get; set; }
        public string Estado { get; set; }

        public PedidoDelivery(DateTime fecha, string plataforma, string id, string cliente, string detalle, decimal total, string estado)
        {
            Fecha = fecha; Plataforma = plataforma; Id = id; Cliente = cliente; Detalle = detalle; Total = total; Estado = estado;
        }

        public static readonly string[] Plataformas = { "Rappi", "Uber Eats", "DiDi Food" };
        public static readonly string[] Estados = { "Nuevo", "En preparación", "Listo para rider", "Entregado" };

        public string HoraText => Fecha.ToString("HH:mm");
        public string TotalText => Fmt.Money(Total);

        public Color PlataformaColor =>
            Plataforma == "Rappi" ? Color.FromArgb(255, 99, 88) :
            Plataforma == "Uber Eats" ? Color.FromArgb(6, 193, 103) : Color.FromArgb(255, 138, 51);

        public Color EstadoColor =>
            Estado == "Nuevo" ? Theme.Secondary :
            Estado == "En preparación" ? Theme.Primary :
            Estado == "Listo para rider" ? Theme.Tertiary : Theme.OnSurfaceVariant;

        /// <summary>Texto del botón que avanza el estado (vacío si ya se entregó).</summary>
        public string AccionAvanzar =>
            Estado == "Nuevo" ? "▶ Preparar" :
            Estado == "En preparación" ? "✓ Listo rider" :
            Estado == "Listo para rider" ? "✓ Entregado" : "";

        public void Avanzar()
        {
            int i = Array.IndexOf(Estados, Estado);
            if (i >= 0 && i < Estados.Length - 1) Estado = Estados[i + 1];
        }
    }

    // ===================== Reportes =====================

    public class VentaItem
    {
        public string Plato { get; set; }
        public int Cantidad { get; set; }
        public decimal Precio { get; set; }
        public VentaItem(string plato, int cantidad, decimal precio) { Plato = plato; Cantidad = cantidad; Precio = precio; }
        public decimal Subtotal => Cantidad * Precio;
    }

    public class Venta
    {
        /// <summary>Id en la tabla Venta.</summary>
        public int Id { get; set; }
        public DateTime Fecha { get; set; }
        public string Mesa { get; set; }
        public string Detalle { get; set; }
        public decimal Total { get; set; }
        public string Metodo { get; set; }
        public string Cajero { get; set; }
        public string Estado { get; set; }
        public string Origen { get; set; }
        public List<VentaItem> Items { get; } = new List<VentaItem>();
        /// <summary>Tasa de impuesto aplicada a esta venta en % (0, 8, 19, 27).</summary>
        public int TaxPct { get; set; } = 8;
        /// <summary>Suma de las líneas, antes de impuesto.</summary>
        public decimal Subtotal { get; set; }
        /// <summary>Impuesto cobrado (guardado en la venta; el POS lo suma al subtotal).</summary>
        public decimal Impuesto { get; set; }
        /// <summary>Parte del total pagada en efectivo y con tarjeta (una venta puede tener las dos).</summary>
        public decimal Efectivo { get; set; }
        public decimal Tarjeta { get; set; }

        /// <summary>Impuesto de la venta tal como se cobró (antes se estimaba como incluido en el total, lo que no coincidía con el POS).</summary>
        public decimal TaxAmount() => Impuesto;

        public Venta(DateTime fecha, string mesa, string detalle, decimal total, string metodo, string cajero, string estado, string origen = "Salón")
        {
            Fecha = fecha; Mesa = mesa; Detalle = detalle; Total = total; Metodo = metodo; Cajero = cajero; Estado = estado; Origen = origen;
        }

        public string TotalText => Fmt.Money(Total);
        public Color EstadoColor => Estado == "Cobrado" ? Theme.Tertiary : Theme.Error;
    }
}
