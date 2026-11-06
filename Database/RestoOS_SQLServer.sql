/* =====================================================================
   RestoOS — Base de datos en Microsoft SQL Server
   ---------------------------------------------------------------------
   Requiere: SQL Server 2017 o superior (sirve Express / LocalDB).
   Ejecutar completo en SSMS o Azure Data Studio (usa separadores GO).

   OJO: el bloque "Limpieza" borra y vuelve a crear todas las tablas.
   Úsalo para instalar desde cero; no lo ejecutes sobre datos reales.

   Contenido:
     1. Base de datos        5. Vistas para Reportes
     2. Limpieza             6. Procedimientos almacenados
     3. Tablas               7. Datos iniciales y de ejemplo
     4. Índices
   Diseño explicado en ANALISIS_BD.md (misma carpeta).
   La app lo trae embebido y lo ejecuta sola si la base RestoOS no existe (Data/Db.cs).
   ===================================================================== */

SET NOCOUNT ON;
GO

/* ===================== 1. Base de datos ===================== */
IF DB_ID(N'RestoOS') IS NULL
    CREATE DATABASE RestoOS COLLATE Modern_Spanish_CI_AS;
GO
USE RestoOS;
GO

/* ===================== 2. Limpieza (reinstalación) ===================== */
DROP PROCEDURE IF EXISTS dbo.sp_AnularVenta;
DROP PROCEDURE IF EXISTS dbo.sp_CobrarPedido;
DROP PROCEDURE IF EXISTS dbo.sp_EnviarComandas;
DROP PROCEDURE IF EXISTS dbo.sp_ValidarPin;
DROP PROCEDURE IF EXISTS dbo.sp_GuardarPin;
DROP VIEW IF EXISTS dbo.vw_VentasDetalle;
DROP VIEW IF EXISTS dbo.vw_VentasPorDia;
DROP VIEW IF EXISTS dbo.vw_ProductosMasVendidos;
DROP VIEW IF EXISTS dbo.vw_InsumosStockBajo;
DROP VIEW IF EXISTS dbo.vw_MesasEstado;
DROP TABLE IF EXISTS
    dbo.Pago, dbo.MovimientoInventario, dbo.Venta, dbo.DetallePedido, dbo.Comanda,
    dbo.Pedido, dbo.TurnoCaja, dbo.Reserva, dbo.Cliente, dbo.ProductoInsumo,
    dbo.ProductoEtiqueta, dbo.Etiqueta, dbo.Producto, dbo.Categoria, dbo.Insumo,
    dbo.CategoriaInsumo, dbo.Mesa, dbo.Sector, dbo.RolModulo, dbo.Usuario, dbo.Rol,
    dbo.Modulo, dbo.Estacion, dbo.Plataforma, dbo.Configuracion;
DROP TYPE IF EXISTS dbo.TipoPago;
GO

/* ===================== 3. Tablas ===================== */

/* ---------- Configuración y módulos (settings.xml, tax.dat, modules.dat) ---------- */

-- Una sola fila (Id = 1) con los datos del negocio.
CREATE TABLE dbo.Configuracion (
    Id                 TINYINT       NOT NULL CONSTRAINT PK_Configuracion PRIMARY KEY,
    NombreRestaurante  NVARCHAR(120) NOT NULL CONSTRAINT DF_Configuracion_Nombre DEFAULT (N'Mi restaurante'),
    Direccion          NVARCHAR(200) NULL,
    Telefono           NVARCHAR(30)  NULL,
    SimboloMoneda      NVARCHAR(5)   NOT NULL CONSTRAINT DF_Configuracion_Moneda DEFAULT (N'$'),
    LogoRuta           NVARCHAR(260) NULL,
    ImpuestoPorDefecto TINYINT       NOT NULL CONSTRAINT DF_Configuracion_Impuesto DEFAULT (8),
    PropinaActiva      BIT           NOT NULL CONSTRAINT DF_Configuracion_PropinaActiva DEFAULT (0),
    PropinaPorcentaje  DECIMAL(5,2)  NOT NULL CONSTRAINT DF_Configuracion_Propina DEFAULT (10),
    CONSTRAINT CK_Configuracion_FilaUnica CHECK (Id = 1),
    CONSTRAINT CK_Configuracion_Impuesto  CHECK (ImpuestoPorDefecto IN (0, 8, 19, 27)),
    CONSTRAINT CK_Configuracion_Propina   CHECK (PropinaPorcentaje BETWEEN 0 AND 100)
);

-- Claves iguales a ModuleStore.Keys (+ "pos", que siempre está activo).
CREATE TABLE dbo.Modulo (
    Clave       VARCHAR(20)  NOT NULL CONSTRAINT PK_Modulo PRIMARY KEY,
    Nombre      NVARCHAR(60) NOT NULL,
    Activo      BIT          NOT NULL CONSTRAINT DF_Modulo_Activo DEFAULT (1),
    Obligatorio BIT          NOT NULL CONSTRAINT DF_Modulo_Obligatorio DEFAULT (0),
    CONSTRAINT CK_Modulo_Obligatorio CHECK (Obligatorio = 0 OR Activo = 1)
);

/* ---------- Usuarios, roles y permisos ---------- */

CREATE TABLE dbo.Rol (
    Id     INT          IDENTITY(1,1) NOT NULL CONSTRAINT PK_Rol PRIMARY KEY,
    Nombre NVARCHAR(30) NOT NULL CONSTRAINT UQ_Rol_Nombre UNIQUE
);

-- Qué secciones (módulos) puede abrir cada rol.
CREATE TABLE dbo.RolModulo (
    RolId       INT         NOT NULL CONSTRAINT FK_RolModulo_Rol    REFERENCES dbo.Rol (Id) ON DELETE CASCADE,
    ModuloClave VARCHAR(20) NOT NULL CONSTRAINT FK_RolModulo_Modulo REFERENCES dbo.Modulo (Clave) ON DELETE CASCADE,
    CONSTRAINT PK_RolModulo PRIMARY KEY (RolId, ModuloClave)
);

-- El PIN nunca se guarda en texto plano: SHA-256 de (sal + PIN). Ver sp_GuardarPin / sp_ValidarPin.
CREATE TABLE dbo.Usuario (
    Id            INT              IDENTITY(1,1) NOT NULL CONSTRAINT PK_Usuario PRIMARY KEY,
    Nombre        NVARCHAR(80)     NOT NULL CONSTRAINT UQ_Usuario_Nombre UNIQUE,
    RolId         INT              NOT NULL CONSTRAINT FK_Usuario_Rol REFERENCES dbo.Rol (Id),
    PinSal        UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_Usuario_PinSal DEFAULT (NEWID()),
    PinHash       VARBINARY(32)    NULL,
    Activo        BIT              NOT NULL CONSTRAINT DF_Usuario_Activo DEFAULT (1),
    FechaCreacion DATETIME2(0)     NOT NULL CONSTRAINT DF_Usuario_Fecha DEFAULT (SYSDATETIME())
);

/* ---------- Salón (mesas.dat) ---------- */

CREATE TABLE dbo.Sector (
    Id     INT          IDENTITY(1,1) NOT NULL CONSTRAINT PK_Sector PRIMARY KEY,
    Nombre NVARCHAR(40) NOT NULL CONSTRAINT UQ_Sector_Nombre UNIQUE
);

CREATE TABLE dbo.Mesa (
    Id         INT          IDENTITY(1,1) NOT NULL CONSTRAINT PK_Mesa PRIMARY KEY,
    Nombre     NVARCHAR(30) NOT NULL CONSTRAINT UQ_Mesa_Nombre UNIQUE,      -- "Mesa 5", "Barra 1"
    SectorId   INT          NOT NULL CONSTRAINT FK_Mesa_Sector REFERENCES dbo.Sector (Id),
    Capacidad  TINYINT      NOT NULL CONSTRAINT DF_Mesa_Capacidad DEFAULT (4),
    Estado     NVARCHAR(20) NOT NULL CONSTRAINT DF_Mesa_Estado DEFAULT (N'Libre'),
    PuedeAbrir BIT          NOT NULL CONSTRAINT DF_Mesa_PuedeAbrir DEFAULT (1),
    Activa     BIT          NOT NULL CONSTRAINT DF_Mesa_Activa DEFAULT (1),  -- baja lógica: hay ventas que la referencian
    CONSTRAINT CK_Mesa_Capacidad CHECK (Capacidad BETWEEN 1 AND 50),
    CONSTRAINT CK_Mesa_Estado    CHECK (Estado IN (N'Libre', N'Ocupada', N'Cuenta pedida', N'Reservada', N'Por limpiar'))
);

/* ---------- Menú digital (menu.xml) ---------- */

-- Estaciones de cocina del KDS.
CREATE TABLE dbo.Estacion (
    Id     INT          IDENTITY(1,1) NOT NULL CONSTRAINT PK_Estacion PRIMARY KEY,
    Nombre NVARCHAR(30) NOT NULL CONSTRAINT UQ_Estacion_Nombre UNIQUE
);

CREATE TABLE dbo.Categoria (
    Id         INT          IDENTITY(1,1) NOT NULL CONSTRAINT PK_Categoria PRIMARY KEY,
    Nombre     NVARCHAR(60) NOT NULL CONSTRAINT UQ_Categoria_Nombre UNIQUE,
    Posicion   INT          NOT NULL CONSTRAINT DF_Categoria_Posicion DEFAULT (0),
    EstacionId INT          NULL CONSTRAINT FK_Categoria_Estacion REFERENCES dbo.Estacion (Id),  -- a qué estación van sus comandas
    Activo     BIT          NOT NULL CONSTRAINT DF_Categoria_Activo DEFAULT (1)  -- baja lógica: puede tener platos con ventas
);

CREATE TABLE dbo.Producto (
    Id               INT           IDENTITY(1,1) NOT NULL CONSTRAINT PK_Producto PRIMARY KEY,
    CategoriaId      INT           NOT NULL CONSTRAINT FK_Producto_Categoria REFERENCES dbo.Categoria (Id),
    Nombre           NVARCHAR(120) NOT NULL CONSTRAINT UQ_Producto_Nombre UNIQUE,
    Descripcion      NVARCHAR(500) NULL,
    PrecioSalon      DECIMAL(12,2) NOT NULL,
    PrecioDelivery   DECIMAL(12,2) NOT NULL,
    ComisionDelivery DECIMAL(5,2)  NOT NULL CONSTRAINT DF_Producto_Comision DEFAULT (15),
    SinComision      BIT           NOT NULL CONSTRAINT DF_Producto_SinComision DEFAULT (0),
    Disponible       BIT           NOT NULL CONSTRAINT DF_Producto_Disponible DEFAULT (1),
    SugerenciaChef   BIT           NOT NULL CONSTRAINT DF_Producto_Chef DEFAULT (0),
    Posicion         INT           NOT NULL CONSTRAINT DF_Producto_Posicion DEFAULT (0),
    ImagenRuta       NVARCHAR(260) NULL,
    PorcionesDia     INT           NULL,  -- NULL = ilimitado (en la app era -1)
    Activo           BIT           NOT NULL CONSTRAINT DF_Producto_Activo DEFAULT (1),
    CONSTRAINT CK_Producto_Precios   CHECK (PrecioSalon >= 0 AND PrecioDelivery >= 0),
    CONSTRAINT CK_Producto_Comision  CHECK (ComisionDelivery BETWEEN 0 AND 100),
    CONSTRAINT CK_Producto_Porciones CHECK (PorcionesDia IS NULL OR PorcionesDia >= 0)
);

-- Tags ("Carnes Grill", "Nuevo") y filtros dietarios ("Vegetariano", "Sin TACC").
CREATE TABLE dbo.Etiqueta (
    Id     INT          IDENTITY(1,1) NOT NULL CONSTRAINT PK_Etiqueta PRIMARY KEY,
    Nombre NVARCHAR(40) NOT NULL,
    Tipo   NVARCHAR(10) NOT NULL,
    CONSTRAINT UQ_Etiqueta UNIQUE (Nombre, Tipo),
    CONSTRAINT CK_Etiqueta_Tipo CHECK (Tipo IN (N'Etiqueta', N'Dietario'))
);

CREATE TABLE dbo.ProductoEtiqueta (
    ProductoId INT NOT NULL CONSTRAINT FK_ProductoEtiqueta_Producto REFERENCES dbo.Producto (Id) ON DELETE CASCADE,
    EtiquetaId INT NOT NULL CONSTRAINT FK_ProductoEtiqueta_Etiqueta REFERENCES dbo.Etiqueta (Id) ON DELETE CASCADE,
    CONSTRAINT PK_ProductoEtiqueta PRIMARY KEY (ProductoId, EtiquetaId)
);

/* ---------- Inventario ---------- */

CREATE TABLE dbo.CategoriaInsumo (
    Id     INT          IDENTITY(1,1) NOT NULL CONSTRAINT PK_CategoriaInsumo PRIMARY KEY,
    Nombre NVARCHAR(40) NOT NULL CONSTRAINT UQ_CategoriaInsumo_Nombre UNIQUE
);

CREATE TABLE dbo.Insumo (
    Id                INT           IDENTITY(1,1) NOT NULL CONSTRAINT PK_Insumo PRIMARY KEY,
    Nombre            NVARCHAR(80)  NOT NULL CONSTRAINT UQ_Insumo_Nombre UNIQUE,
    CategoriaInsumoId INT           NULL CONSTRAINT FK_Insumo_Categoria REFERENCES dbo.CategoriaInsumo (Id),
    Unidad            VARCHAR(5)    NOT NULL,
    Stock             DECIMAL(12,3) NOT NULL CONSTRAINT DF_Insumo_Stock DEFAULT (0),  -- puede quedar negativo: muestra faltante real
    StockMinimo       DECIMAL(12,3) NOT NULL CONSTRAINT DF_Insumo_Minimo DEFAULT (0),
    CostoUnitario     DECIMAL(12,2) NOT NULL CONSTRAINT DF_Insumo_Costo DEFAULT (0),
    Activo            BIT           NOT NULL CONSTRAINT DF_Insumo_Activo DEFAULT (1),
    -- Misma regla que Insumo.Estado en la app: crítico <= 50 % del mínimo, bajo <= mínimo.
    Estado AS (CASE WHEN Stock <= StockMinimo * 0.5 THEN N'Crítico'
                    WHEN Stock <= StockMinimo       THEN N'Bajo'
                    ELSE N'OK' END),
    CONSTRAINT CK_Insumo_Unidad CHECK (Unidad IN ('kg', 'g', 'L', 'ml', 'und')),
    CONSTRAINT CK_Insumo_Valores CHECK (StockMinimo >= 0 AND CostoUnitario >= 0)
);

-- Receta: cuánto insumo se descuenta por cada unidad vendida del plato (InsumoLink).
CREATE TABLE dbo.ProductoInsumo (
    ProductoId INT           NOT NULL CONSTRAINT FK_ProductoInsumo_Producto REFERENCES dbo.Producto (Id) ON DELETE CASCADE,
    InsumoId   INT           NOT NULL CONSTRAINT FK_ProductoInsumo_Insumo   REFERENCES dbo.Insumo (Id) ON DELETE CASCADE,
    Cantidad   DECIMAL(10,3) NOT NULL,
    CONSTRAINT PK_ProductoInsumo PRIMARY KEY (ProductoId, InsumoId),
    CONSTRAINT CK_ProductoInsumo_Cantidad CHECK (Cantidad > 0)
);

/* ---------- Clientes y reservas ---------- */

CREATE TABLE dbo.Cliente (
    Id            INT           IDENTITY(1,1) NOT NULL CONSTRAINT PK_Cliente PRIMARY KEY,
    Nombre        NVARCHAR(100) NOT NULL,
    Telefono      NVARCHAR(30)  NULL,
    Correo        NVARCHAR(120) NULL,
    Direccion     NVARCHAR(200) NULL,
    FechaRegistro DATETIME2(0)  NOT NULL CONSTRAINT DF_Cliente_Fecha DEFAULT (SYSDATETIME())
);

CREATE TABLE dbo.Reserva (
    Id        INT           IDENTITY(1,1) NOT NULL CONSTRAINT PK_Reserva PRIMARY KEY,
    ClienteId INT           NOT NULL CONSTRAINT FK_Reserva_Cliente REFERENCES dbo.Cliente (Id),
    MesaId    INT           NULL CONSTRAINT FK_Reserva_Mesa REFERENCES dbo.Mesa (Id),
    FechaHora DATETIME2(0)  NOT NULL,
    Personas  TINYINT       NOT NULL,
    Estado    NVARCHAR(15)  NOT NULL CONSTRAINT DF_Reserva_Estado DEFAULT (N'Confirmada'),
    Notas     NVARCHAR(200) NULL,
    CONSTRAINT CK_Reserva_Personas CHECK (Personas BETWEEN 1 AND 100),
    CONSTRAINT CK_Reserva_Estado   CHECK (Estado IN (N'Confirmada', N'En curso', N'Cancelada'))
);

/* ---------- Pedidos, cocina y delivery ---------- */

CREATE TABLE dbo.Plataforma (
    Id     INT          IDENTITY(1,1) NOT NULL CONSTRAINT PK_Plataforma PRIMARY KEY,
    Nombre NVARCHAR(30) NOT NULL CONSTRAINT UQ_Plataforma_Nombre UNIQUE,
    Activa BIT          NOT NULL CONSTRAINT DF_Plataforma_Activa DEFAULT (1)
);

-- Apertura y cierre de caja (requisitos §8). Opcional para cobrar mientras la UI no lo use.
CREATE TABLE dbo.TurnoCaja (
    Id               INT           IDENTITY(1,1) NOT NULL CONSTRAINT PK_TurnoCaja PRIMARY KEY,
    UsuarioId        INT           NOT NULL CONSTRAINT FK_TurnoCaja_Usuario REFERENCES dbo.Usuario (Id),
    FechaApertura    DATETIME2(0)  NOT NULL CONSTRAINT DF_TurnoCaja_Apertura DEFAULT (SYSDATETIME()),
    FondoInicial     DECIMAL(12,2) NOT NULL CONSTRAINT DF_TurnoCaja_Fondo DEFAULT (0),
    FechaCierre      DATETIME2(0)  NULL,
    EfectivoEsperado DECIMAL(12,2) NULL,
    EfectivoContado  DECIMAL(12,2) NULL,
    Diferencia AS (EfectivoContado - EfectivoEsperado),
    CONSTRAINT CK_TurnoCaja_Fondo  CHECK (FondoInicial >= 0),
    CONSTRAINT CK_TurnoCaja_Cierre CHECK (FechaCierre IS NULL OR FechaCierre >= FechaApertura)
);

-- Pedido = ticket abierto del POS (tickets.dat). Reúne salón, para llevar, domicilio propio y plataformas.
CREATE TABLE dbo.Pedido (
    Id               INT           IDENTITY(1,1) NOT NULL CONSTRAINT PK_Pedido PRIMARY KEY,
    Origen           NVARCHAR(12)  NOT NULL,
    MesaId           INT           NULL CONSTRAINT FK_Pedido_Mesa       REFERENCES dbo.Mesa (Id),
    PlataformaId     INT           NULL CONSTRAINT FK_Pedido_Plataforma REFERENCES dbo.Plataforma (Id),
    CodigoExterno    NVARCHAR(30)  NULL,  -- "#R-8821" de Rappi, etc.
    ClienteId        INT           NULL CONSTRAINT FK_Pedido_Cliente    REFERENCES dbo.Cliente (Id),
    DireccionEntrega NVARCHAR(200) NULL,
    UsuarioId        INT           NULL CONSTRAINT FK_Pedido_Usuario    REFERENCES dbo.Usuario (Id),  -- quién lo abrió
    Estado           NVARCHAR(10)  NOT NULL CONSTRAINT DF_Pedido_Estado DEFAULT (N'Abierto'),
    EstadoDelivery   NVARCHAR(20)  NULL,
    FechaApertura    DATETIME2(0)  NOT NULL CONSTRAINT DF_Pedido_Apertura DEFAULT (SYSDATETIME()),
    FechaCierre      DATETIME2(0)  NULL,
    Notas            NVARCHAR(200) NULL,
    CONSTRAINT CK_Pedido_Origen CHECK (Origen IN (N'Salón', N'Para llevar', N'Domicilio', N'Delivery')),
    CONSTRAINT CK_Pedido_Estado CHECK (Estado IN (N'Abierto', N'Cobrado', N'Anulado')),
    -- Mesa solo en salón; plataforma solo en delivery; domicilio exige cliente y dirección.
    CONSTRAINT CK_Pedido_Mesa       CHECK (MesaId IS NULL OR Origen = N'Salón'),
    CONSTRAINT CK_Pedido_Plataforma CHECK ((Origen = N'Delivery' AND PlataformaId IS NOT NULL)
                                        OR (Origen <> N'Delivery' AND PlataformaId IS NULL AND CodigoExterno IS NULL)),
    CONSTRAINT CK_Pedido_Domicilio  CHECK (Origen <> N'Domicilio' OR (ClienteId IS NOT NULL AND DireccionEntrega IS NOT NULL)),
    CONSTRAINT CK_Pedido_EstadoDelivery CHECK (
        (Origen IN (N'Delivery', N'Domicilio')
            AND EstadoDelivery IN (N'Nuevo', N'En preparación', N'Listo para rider', N'Entregado'))
        OR (Origen NOT IN (N'Delivery', N'Domicilio') AND EstadoDelivery IS NULL))
);

-- Comanda enviada a una estación del KDS (agrupa líneas del pedido).
CREATE TABLE dbo.Comanda (
    Id            INT          IDENTITY(1,1) NOT NULL CONSTRAINT PK_Comanda PRIMARY KEY,
    PedidoId      INT          NOT NULL CONSTRAINT FK_Comanda_Pedido   REFERENCES dbo.Pedido (Id),
    EstacionId    INT          NOT NULL CONSTRAINT FK_Comanda_Estacion REFERENCES dbo.Estacion (Id),
    Estado        NVARCHAR(12) NOT NULL CONSTRAINT DF_Comanda_Estado DEFAULT (N'Nuevo'),  -- = enum KdsStatus
    FechaCreacion DATETIME2(0) NOT NULL CONSTRAINT DF_Comanda_Fecha DEFAULT (SYSDATETIME()),
    FechaListo    DATETIME2(0) NULL,
    CONSTRAINT CK_Comanda_Estado CHECK (Estado IN (N'Nuevo', N'Preparacion', N'Listo'))
);

-- Línea del pedido (OrderLine). Nombre y precio se copian al agregar: cambiar la carta no altera ventas pasadas.
CREATE TABLE dbo.DetallePedido (
    Id             INT           IDENTITY(1,1) NOT NULL CONSTRAINT PK_DetallePedido PRIMARY KEY,
    PedidoId       INT           NOT NULL CONSTRAINT FK_DetallePedido_Pedido   REFERENCES dbo.Pedido (Id),
    ProductoId     INT           NOT NULL CONSTRAINT FK_DetallePedido_Producto REFERENCES dbo.Producto (Id),
    NombreProducto NVARCHAR(120) NOT NULL,
    PrecioUnitario DECIMAL(12,2) NOT NULL,
    Cantidad       INT           NOT NULL CONSTRAINT DF_DetallePedido_Cantidad DEFAULT (1),
    Notas          NVARCHAR(200) NULL,  -- "sin cebolla", "término medio"
    ComandaId      INT           NULL CONSTRAINT FK_DetallePedido_Comanda REFERENCES dbo.Comanda (Id),  -- NULL = aún no enviada a cocina
    Subtotal AS (CAST(PrecioUnitario * Cantidad AS DECIMAL(14,2))) PERSISTED,
    CONSTRAINT CK_DetallePedido_Cantidad CHECK (Cantidad > 0),
    CONSTRAINT CK_DetallePedido_Precio   CHECK (PrecioUnitario >= 0)
);

/* ---------- Ventas y pagos ---------- */

-- Cobro de un pedido. El impuesto se suma al subtotal, como hace PosForm.
CREATE TABLE dbo.Venta (
    Id                 INT           IDENTITY(1,1) NOT NULL CONSTRAINT PK_Venta PRIMARY KEY,
    PedidoId           INT           NOT NULL CONSTRAINT FK_Venta_Pedido REFERENCES dbo.Pedido (Id)
                                              CONSTRAINT UQ_Venta_Pedido UNIQUE,
    UsuarioId          INT           NOT NULL CONSTRAINT FK_Venta_Usuario REFERENCES dbo.Usuario (Id),  -- cajero
    TurnoCajaId        INT           NULL CONSTRAINT FK_Venta_Turno REFERENCES dbo.TurnoCaja (Id),
    Fecha              DATETIME2(0)  NOT NULL CONSTRAINT DF_Venta_Fecha DEFAULT (SYSDATETIME()),
    Subtotal           DECIMAL(14,2) NOT NULL,
    Descuento          DECIMAL(14,2) NOT NULL CONSTRAINT DF_Venta_Descuento DEFAULT (0),
    ImpuestoPorcentaje TINYINT       NOT NULL,
    ImpuestoValor      DECIMAL(14,2) NOT NULL,
    Propina            DECIMAL(14,2) NOT NULL CONSTRAINT DF_Venta_Propina DEFAULT (0),
    Total              DECIMAL(14,2) NOT NULL,
    Estado             NVARCHAR(10)  NOT NULL CONSTRAINT DF_Venta_Estado DEFAULT (N'Cobrado'),
    AnuladaPorId       INT           NULL CONSTRAINT FK_Venta_AnuladaPor REFERENCES dbo.Usuario (Id),
    MotivoAnulacion    NVARCHAR(200) NULL,
    FechaAnulacion     DATETIME2(0)  NULL,
    CONSTRAINT CK_Venta_Impuesto  CHECK (ImpuestoPorcentaje IN (0, 8, 19, 27)),
    CONSTRAINT CK_Venta_Valores   CHECK (Descuento BETWEEN 0 AND Subtotal AND ImpuestoValor >= 0 AND Propina >= 0),
    CONSTRAINT CK_Venta_Total     CHECK (Total = Subtotal - Descuento + ImpuestoValor + Propina),
    CONSTRAINT CK_Venta_Estado    CHECK (Estado IN (N'Cobrado', N'Anulado')),
    CONSTRAINT CK_Venta_Anulacion CHECK (
        (Estado = N'Cobrado' AND AnuladaPorId IS NULL AND MotivoAnulacion IS NULL AND FechaAnulacion IS NULL)
        OR (Estado = N'Anulado' AND AnuladaPorId IS NOT NULL AND LEN(MotivoAnulacion) > 0 AND FechaAnulacion IS NOT NULL))
);

-- Uno o varios pagos por venta: permite pago dividido y mixto (efectivo + tarjeta).
CREATE TABLE dbo.Pago (
    Id            INT           IDENTITY(1,1) NOT NULL CONSTRAINT PK_Pago PRIMARY KEY,
    VentaId       INT           NOT NULL CONSTRAINT FK_Pago_Venta REFERENCES dbo.Venta (Id),
    Metodo        NVARCHAR(10)  NOT NULL,
    Monto         DECIMAL(14,2) NOT NULL,   -- parte del total que cubre este pago
    MontoRecibido DECIMAL(14,2) NULL,       -- solo efectivo: lo que entregó el cliente
    Cambio AS (MontoRecibido - Monto),
    CONSTRAINT CK_Pago_Metodo CHECK (Metodo IN (N'Efectivo', N'Tarjeta')),
    CONSTRAINT CK_Pago_Monto  CHECK (Monto > 0),
    CONSTRAINT CK_Pago_Recibido CHECK (
        (Metodo = N'Efectivo' AND (MontoRecibido IS NULL OR MontoRecibido >= Monto))
        OR (Metodo = N'Tarjeta' AND MontoRecibido IS NULL))
);

-- Kárdex: toda entrada/salida de insumos queda registrada (+ entra, − sale).
CREATE TABLE dbo.MovimientoInventario (
    Id        BIGINT        IDENTITY(1,1) NOT NULL CONSTRAINT PK_MovimientoInventario PRIMARY KEY,
    InsumoId  INT           NOT NULL CONSTRAINT FK_Movimiento_Insumo  REFERENCES dbo.Insumo (Id),
    Tipo      NVARCHAR(10)  NOT NULL,
    Cantidad  DECIMAL(12,3) NOT NULL,
    Fecha     DATETIME2(0)  NOT NULL CONSTRAINT DF_Movimiento_Fecha DEFAULT (SYSDATETIME()),
    VentaId   INT           NULL CONSTRAINT FK_Movimiento_Venta   REFERENCES dbo.Venta (Id),
    UsuarioId INT           NULL CONSTRAINT FK_Movimiento_Usuario REFERENCES dbo.Usuario (Id),
    Nota      NVARCHAR(200) NULL,
    CONSTRAINT CK_Movimiento_Tipo     CHECK (Tipo IN (N'Entrada', N'Venta', N'Ajuste', N'Anulación')),
    CONSTRAINT CK_Movimiento_Cantidad CHECK (Cantidad <> 0)
);
GO

-- Tipo tabla para enviar los pagos de un cobro desde C# (SqlDbType.Structured).
CREATE TYPE dbo.TipoPago AS TABLE (
    Metodo        NVARCHAR(10)  NOT NULL,
    Monto         DECIMAL(14,2) NOT NULL,
    MontoRecibido DECIMAL(14,2) NULL
);
GO

/* ===================== 4. Índices ===================== */

-- Un solo ticket abierto por mesa (igual que TicketStore).
CREATE UNIQUE INDEX UX_Pedido_MesaAbierta ON dbo.Pedido (MesaId)
    WHERE Estado = N'Abierto' AND MesaId IS NOT NULL;
-- No duplicar un pedido de plataforma.
CREATE UNIQUE INDEX UX_Pedido_CodigoExterno ON dbo.Pedido (PlataformaId, CodigoExterno)
    WHERE CodigoExterno IS NOT NULL;
-- Un turno de caja abierto por usuario.
CREATE UNIQUE INDEX UX_TurnoCaja_Abierto ON dbo.TurnoCaja (UsuarioId)
    WHERE FechaCierre IS NULL;

CREATE INDEX IX_Pedido_Estado        ON dbo.Pedido (Estado, Origen);
CREATE INDEX IX_DetallePedido_Pedido ON dbo.DetallePedido (PedidoId);
CREATE INDEX IX_DetallePedido_Prod   ON dbo.DetallePedido (ProductoId);
CREATE INDEX IX_Comanda_Estado       ON dbo.Comanda (Estado, EstacionId);
CREATE INDEX IX_Venta_Fecha          ON dbo.Venta (Fecha) INCLUDE (Total, Estado, ImpuestoValor);
CREATE INDEX IX_Pago_Venta           ON dbo.Pago (VentaId);
CREATE INDEX IX_Movimiento_Insumo    ON dbo.MovimientoInventario (InsumoId, Fecha);
CREATE INDEX IX_Movimiento_Venta     ON dbo.MovimientoInventario (VentaId) WHERE VentaId IS NOT NULL;
CREATE INDEX IX_Reserva_Fecha        ON dbo.Reserva (FechaHora);
CREATE INDEX IX_Producto_Categoria   ON dbo.Producto (CategoriaId, Posicion);
GO

/* ===================== 5. Vistas para Reportes ===================== */

-- Ventas con su origen, mesa/plataforma, cajero y métodos de pago (tabla de ReportesForm).
CREATE VIEW dbo.vw_VentasDetalle AS
SELECT v.Id                AS VentaId,
       v.Fecha,
       p.Origen,
       COALESCE(m.Nombre, pl.Nombre, p.Origen) AS Destino,
       u.Nombre            AS Cajero,
       v.Subtotal, v.Descuento, v.ImpuestoPorcentaje, v.ImpuestoValor, v.Propina, v.Total,
       v.Estado,
       (SELECT STRING_AGG(pg.Metodo, N' + ') FROM dbo.Pago pg WHERE pg.VentaId = v.Id) AS Metodos,
       (SELECT STRING_AGG(CONCAT(N'x', d.Cantidad, N' ', d.NombreProducto), N', ')
          FROM dbo.DetallePedido d WHERE d.PedidoId = p.Id) AS Detalle
FROM dbo.Venta v
JOIN dbo.Pedido p        ON p.Id = v.PedidoId
JOIN dbo.Usuario u       ON u.Id = v.UsuarioId
LEFT JOIN dbo.Mesa m     ON m.Id = p.MesaId
LEFT JOIN dbo.Plataforma pl ON pl.Id = p.PlataformaId;
GO

-- Totales por día y origen (salón + delivery unificados, requisito de Reportes).
CREATE VIEW dbo.vw_VentasPorDia AS
SELECT CAST(v.Fecha AS DATE) AS Dia,
       p.Origen,
       COUNT(*)              AS NumVentas,
       SUM(v.Subtotal)       AS Subtotal,
       SUM(v.Descuento)      AS Descuentos,
       SUM(v.ImpuestoValor)  AS Impuestos,
       SUM(v.Propina)        AS Propinas,
       SUM(v.Total)          AS Total
FROM dbo.Venta v
JOIN dbo.Pedido p ON p.Id = v.PedidoId
WHERE v.Estado = N'Cobrado'
GROUP BY CAST(v.Fecha AS DATE), p.Origen;
GO

CREATE VIEW dbo.vw_ProductosMasVendidos AS
SELECT d.ProductoId,
       pr.Nombre,
       c.Nombre        AS Categoria,
       SUM(d.Cantidad) AS Unidades,
       SUM(d.Subtotal) AS Ingresos
FROM dbo.DetallePedido d
JOIN dbo.Venta v     ON v.PedidoId = d.PedidoId AND v.Estado = N'Cobrado'
JOIN dbo.Producto pr ON pr.Id = d.ProductoId
JOIN dbo.Categoria c ON c.Id = pr.CategoriaId
GROUP BY d.ProductoId, pr.Nombre, c.Nombre;
GO

CREATE VIEW dbo.vw_InsumosStockBajo AS
SELECT i.Id, i.Nombre, ci.Nombre AS Categoria, i.Stock, i.StockMinimo, i.Unidad, i.Estado
FROM dbo.Insumo i
LEFT JOIN dbo.CategoriaInsumo ci ON ci.Id = i.CategoriaInsumoId
WHERE i.Activo = 1 AND i.Estado <> N'OK';
GO

-- Plano del salón: estado de cada mesa y total de su ticket abierto.
CREATE VIEW dbo.vw_MesasEstado AS
SELECT m.Id, m.Nombre, s.Nombre AS Sector, m.Capacidad, m.Estado, m.PuedeAbrir,
       p.Id AS PedidoAbiertoId,
       ISNULL((SELECT SUM(d.Subtotal) FROM dbo.DetallePedido d WHERE d.PedidoId = p.Id), 0) AS TotalAbierto
FROM dbo.Mesa m
JOIN dbo.Sector s      ON s.Id = m.SectorId
LEFT JOIN dbo.Pedido p ON p.MesaId = m.Id AND p.Estado = N'Abierto'
WHERE m.Activa = 1;
GO

/* ===================== 6. Procedimientos almacenados ===================== */

-- Asigna o cambia el PIN de un usuario (4 a 8 dígitos). Genera sal nueva cada vez.
CREATE PROCEDURE dbo.sp_GuardarPin
    @UsuarioId INT,
    @Pin       NVARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;
    IF @Pin IS NULL OR LEN(@Pin) NOT BETWEEN 4 AND 8 OR @Pin LIKE N'%[^0-9]%'
        THROW 50010, N'El PIN debe tener entre 4 y 8 dígitos.', 1;

    DECLARE @Sal UNIQUEIDENTIFIER = NEWID();
    UPDATE dbo.Usuario
       SET PinSal  = @Sal,
           PinHash = HASHBYTES('SHA2_256', CONVERT(NVARCHAR(36), @Sal) + @Pin)
     WHERE Id = @UsuarioId;

    IF @@ROWCOUNT = 0
        THROW 50011, N'El usuario no existe.', 1;
END;
GO

-- Devuelve el usuario activo dueño del PIN (vacío si no coincide).
-- @Rol opcional: p. ej. N'Administrador' para autorizar una anulación.
CREATE PROCEDURE dbo.sp_ValidarPin
    @Pin NVARCHAR(20),
    @Rol NVARCHAR(30) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (1) u.Id, u.Nombre, r.Nombre AS Rol
    FROM dbo.Usuario u
    JOIN dbo.Rol r ON r.Id = u.RolId
    WHERE u.Activo = 1
      AND u.PinHash = HASHBYTES('SHA2_256', CONVERT(NVARCHAR(36), u.PinSal) + @Pin)
      AND (@Rol IS NULL OR r.Nombre = @Rol)
    ORDER BY u.Id;
END;
GO

-- Agrupa las líneas no enviadas por estación y crea una comanda por estación (lo que hace ComandaStore).
CREATE PROCEDURE dbo.sp_EnviarComandas
    @PedidoId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Nuevas TABLE (ComandaId INT, EstacionId INT);

    BEGIN TRANSACTION;

    INSERT INTO dbo.Comanda (PedidoId, EstacionId)
    OUTPUT inserted.Id, inserted.EstacionId INTO @Nuevas (ComandaId, EstacionId)
    SELECT DISTINCT @PedidoId, c.EstacionId
    FROM dbo.DetallePedido d
    JOIN dbo.Producto pr ON pr.Id = d.ProductoId
    JOIN dbo.Categoria c ON c.Id = pr.CategoriaId
    WHERE d.PedidoId = @PedidoId AND d.ComandaId IS NULL AND c.EstacionId IS NOT NULL;

    UPDATE d
       SET ComandaId = n.ComandaId
    FROM dbo.DetallePedido d
    JOIN dbo.Producto pr ON pr.Id = d.ProductoId
    JOIN dbo.Categoria c ON c.Id = pr.CategoriaId
    JOIN @Nuevas n       ON n.EstacionId = c.EstacionId
    WHERE d.PedidoId = @PedidoId AND d.ComandaId IS NULL;

    COMMIT TRANSACTION;

    SELECT n.ComandaId, e.Nombre AS Estacion
    FROM @Nuevas n
    JOIN dbo.Estacion e ON e.Id = n.EstacionId;
END;
GO

/* Cobra un pedido abierto en una sola transacción:
   crea la venta y sus pagos, descuenta insumos y porciones del día,
   cierra el pedido y deja la mesa "Por limpiar".
   Devuelve los insumos que quedaron en stock bajo/crítico. */
CREATE PROCEDURE dbo.sp_CobrarPedido
    @PedidoId           INT,
    @UsuarioId          INT,
    @ImpuestoPorcentaje TINYINT,
    @Descuento          DECIMAL(14,2) = 0,
    @Propina            DECIMAL(14,2) = 0,
    @Pagos              dbo.TipoPago READONLY,
    @VentaId            INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @Estado NVARCHAR(10), @MesaId INT;
        SELECT @Estado = Estado, @MesaId = MesaId
        FROM dbo.Pedido WITH (UPDLOCK, HOLDLOCK)
        WHERE Id = @PedidoId;

        IF @Estado IS NULL
            THROW 50001, N'El pedido no existe.', 1;
        IF @Estado <> N'Abierto'
            THROW 50002, N'El pedido ya fue cobrado o anulado.', 1;

        DECLARE @Subtotal DECIMAL(14,2) =
            (SELECT SUM(Subtotal) FROM dbo.DetallePedido WHERE PedidoId = @PedidoId);
        IF @Subtotal IS NULL
            THROW 50003, N'El pedido no tiene productos.', 1;
        IF @Descuento < 0 OR @Descuento > @Subtotal
            THROW 50004, N'El descuento no es válido.', 1;

        DECLARE @Impuesto DECIMAL(14,2) =
            ROUND((@Subtotal - @Descuento) * @ImpuestoPorcentaje / 100.0, 2);
        DECLARE @Total DECIMAL(14,2) = @Subtotal - @Descuento + @Impuesto + @Propina;

        IF NOT EXISTS (SELECT 1 FROM @Pagos)
            THROW 50005, N'Debe registrar al menos un pago.', 1;
        IF (SELECT SUM(Monto) FROM @Pagos) <> @Total
            THROW 50006, N'La suma de los pagos no coincide con el total de la venta.', 1;

        DECLARE @TurnoId INT =
            (SELECT Id FROM dbo.TurnoCaja WHERE UsuarioId = @UsuarioId AND FechaCierre IS NULL);

        INSERT INTO dbo.Venta (PedidoId, UsuarioId, TurnoCajaId, Subtotal, Descuento,
                               ImpuestoPorcentaje, ImpuestoValor, Propina, Total)
        VALUES (@PedidoId, @UsuarioId, @TurnoId, @Subtotal, @Descuento,
                @ImpuestoPorcentaje, @Impuesto, @Propina, @Total);
        SET @VentaId = SCOPE_IDENTITY();

        INSERT INTO dbo.Pago (VentaId, Metodo, Monto, MontoRecibido)
        SELECT @VentaId, Metodo, Monto, MontoRecibido FROM @Pagos;

        -- Descuento de inventario según la receta de cada plato.
        INSERT INTO dbo.MovimientoInventario (InsumoId, Tipo, Cantidad, VentaId, UsuarioId)
        SELECT pi.InsumoId, N'Venta', -SUM(pi.Cantidad * d.Cantidad), @VentaId, @UsuarioId
        FROM dbo.DetallePedido d
        JOIN dbo.ProductoInsumo pi ON pi.ProductoId = d.ProductoId
        WHERE d.PedidoId = @PedidoId
        GROUP BY pi.InsumoId;

        UPDATE i
           SET Stock = i.Stock + m.Cantidad
        FROM dbo.Insumo i
        JOIN dbo.MovimientoInventario m ON m.InsumoId = i.Id
        WHERE m.VentaId = @VentaId AND m.Tipo = N'Venta';

        -- Porciones del día (solo productos con límite).
        UPDATE pr
           SET PorcionesDia = CASE WHEN pr.PorcionesDia > x.Unidades THEN pr.PorcionesDia - x.Unidades ELSE 0 END
        FROM dbo.Producto pr
        JOIN (SELECT ProductoId, SUM(Cantidad) AS Unidades
              FROM dbo.DetallePedido WHERE PedidoId = @PedidoId
              GROUP BY ProductoId) x ON x.ProductoId = pr.Id
        WHERE pr.PorcionesDia IS NOT NULL;

        UPDATE dbo.Pedido
           SET Estado = N'Cobrado', FechaCierre = SYSDATETIME()
         WHERE Id = @PedidoId;

        IF @MesaId IS NOT NULL
            UPDATE dbo.Mesa SET Estado = N'Por limpiar' WHERE Id = @MesaId;

        COMMIT TRANSACTION;

        SELECT i.Id, i.Nombre, i.Stock, i.StockMinimo, i.Unidad, i.Estado
        FROM dbo.Insumo i
        WHERE i.Estado <> N'OK'
          AND i.Id IN (SELECT InsumoId FROM dbo.MovimientoInventario WHERE VentaId = @VentaId);
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO

/* Anula una venta (requisitos §8): exige un administrador distinto al cajero
   y un motivo; devuelve los insumos al inventario. */
CREATE PROCEDURE dbo.sp_AnularVenta
    @VentaId      INT,
    @SupervisorId INT,
    @Motivo       NVARCHAR(200)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @Estado NVARCHAR(10), @CajeroId INT, @PedidoId INT;
        SELECT @Estado = Estado, @CajeroId = UsuarioId, @PedidoId = PedidoId
        FROM dbo.Venta WITH (UPDLOCK, HOLDLOCK)
        WHERE Id = @VentaId;

        IF @Estado IS NULL
            THROW 50020, N'La venta no existe.', 1;
        IF @Estado = N'Anulado'
            THROW 50021, N'La venta ya está anulada.', 1;
        IF LEN(LTRIM(RTRIM(ISNULL(@Motivo, N'')))) = 0
            THROW 50022, N'El motivo de anulación es obligatorio.', 1;
        IF @SupervisorId = @CajeroId
            THROW 50023, N'La anulación debe autorizarla un usuario distinto al cajero.', 1;
        IF NOT EXISTS (SELECT 1 FROM dbo.Usuario u JOIN dbo.Rol r ON r.Id = u.RolId
                       WHERE u.Id = @SupervisorId AND u.Activo = 1 AND r.Nombre = N'Administrador')
            THROW 50024, N'Solo un administrador puede autorizar anulaciones.', 1;

        UPDATE dbo.Venta
           SET Estado = N'Anulado', AnuladaPorId = @SupervisorId,
               MotivoAnulacion = LTRIM(RTRIM(@Motivo)), FechaAnulacion = SYSDATETIME()
         WHERE Id = @VentaId;

        UPDATE dbo.Pedido SET Estado = N'Anulado' WHERE Id = @PedidoId;

        -- Reversa del descuento de inventario.
        INSERT INTO dbo.MovimientoInventario (InsumoId, Tipo, Cantidad, VentaId, UsuarioId, Nota)
        SELECT InsumoId, N'Anulación', -Cantidad, @VentaId, @SupervisorId, LTRIM(RTRIM(@Motivo))
        FROM dbo.MovimientoInventario
        WHERE VentaId = @VentaId AND Tipo = N'Venta';

        UPDATE i
           SET Stock = i.Stock + m.Cantidad
        FROM dbo.Insumo i
        JOIN dbo.MovimientoInventario m ON m.InsumoId = i.Id
        WHERE m.VentaId = @VentaId AND m.Tipo = N'Anulación';

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO

/* ===================== 7. Datos iniciales y de ejemplo ===================== */

INSERT INTO dbo.Configuracion (Id, NombreRestaurante, ImpuestoPorDefecto) VALUES (1, N'Mi restaurante', 8);

-- Mismos valores por defecto que ModuleStore (KDS apagado).
INSERT INTO dbo.Modulo (Clave, Nombre, Activo, Obligatorio) VALUES
    ('pos',        N'Punto de venta',  1, 1),
    ('salon',      N'Gestión de mesas', 1, 0),
    ('menu',       N'Menú digital',    1, 0),
    ('kds',        N'Cocina (KDS)',    0, 0),
    ('inventario', N'Inventario',      1, 0),
    ('reservas',   N'Reservas',        1, 0),
    ('delivery',   N'Delivery',        1, 0),
    ('reportes',   N'Reportes',        1, 0);

INSERT INTO dbo.Rol (Nombre) VALUES (N'Administrador'), (N'Cajero'), (N'Mesero'), (N'Cocina');

INSERT INTO dbo.RolModulo (RolId, ModuloClave)
SELECT r.Id, m.Clave
FROM dbo.Rol r
CROSS JOIN dbo.Modulo m
WHERE r.Nombre = N'Administrador'
   OR (r.Nombre = N'Cajero' AND m.Clave IN ('pos', 'salon', 'menu', 'reservas', 'delivery'))
   OR (r.Nombre = N'Mesero' AND m.Clave IN ('pos', 'salon'))
   OR (r.Nombre = N'Cocina' AND m.Clave IN ('kds'));

INSERT INTO dbo.Usuario (Nombre, RolId)
SELECT v.Nombre, r.Id
FROM (VALUES (N'Administrador', N'Administrador'), (N'Caja 1', N'Cajero')) v (Nombre, Rol)
JOIN dbo.Rol r ON r.Nombre = v.Rol;

-- PIN de demostración (el mismo 1234 de AdminCodeForm). Cámbialos antes de usar la app en un negocio.
DECLARE @IdAdmin INT = (SELECT Id FROM dbo.Usuario WHERE Nombre = N'Administrador');
DECLARE @IdCaja  INT = (SELECT Id FROM dbo.Usuario WHERE Nombre = N'Caja 1');
EXEC dbo.sp_GuardarPin @UsuarioId = @IdAdmin, @Pin = N'1234';
EXEC dbo.sp_GuardarPin @UsuarioId = @IdCaja,  @Pin = N'0000';
GO

INSERT INTO dbo.Sector (Nombre) VALUES (N'Principal'), (N'Terraza'), (N'Barra'), (N'VIP');

-- Mesas por defecto de MesaStore.
INSERT INTO dbo.Mesa (Nombre, SectorId, Capacidad, PuedeAbrir)
SELECT v.Nombre, s.Id, v.Capacidad, v.PuedeAbrir
FROM (VALUES
    (N'Mesa 1', N'Principal', 2, 1), (N'Mesa 2', N'Principal', 4, 1),
    (N'Mesa 3', N'Principal', 4, 1), (N'Mesa 4', N'Principal', 6, 1),
    (N'Mesa 5', N'Principal', 2, 1), (N'Mesa 6', N'Principal', 4, 1),
    (N'Mesa 7', N'Terraza',   4, 0), (N'Mesa 8', N'Terraza',   4, 1),
    (N'Barra 1', N'Barra',    2, 1), (N'VIP 1',  N'VIP',       8, 0)
) v (Nombre, Sector, Capacidad, PuedeAbrir)
JOIN dbo.Sector s ON s.Nombre = v.Sector;

INSERT INTO dbo.Estacion (Nombre) VALUES (N'Parrilla'), (N'Fría'), (N'Barra');
INSERT INTO dbo.Plataforma (Nombre) VALUES (N'Rappi'), (N'Uber Eats'), (N'DiDi Food');

-- Categorías de la carta de ejemplo (MenuStore.SampleData) con su estación de cocina.
INSERT INTO dbo.Categoria (Nombre, Posicion, EstacionId)
SELECT v.Nombre, v.Posicion, e.Id
FROM (VALUES
    (N'Entradas',        0, N'Fría'),
    (N'Parrilla',        1, N'Parrilla'),
    (N'Pizzas y Pastas', 2, N'Parrilla'),
    (N'Del Mar',         3, N'Parrilla'),
    (N'Bebidas',         4, N'Barra'),
    (N'Postres',         5, N'Fría')
) v (Nombre, Posicion, Estacion)
JOIN dbo.Estacion e ON e.Nombre = v.Estacion;

-- Productos de la carta de ejemplo. PrecioDelivery = salón × 1,15 redondeado a la centena (como la app).
INSERT INTO dbo.Producto (CategoriaId, Nombre, Descripcion, PrecioSalon, PrecioDelivery, Disponible, SugerenciaChef, Posicion)
SELECT c.Id, v.Nombre, v.Descripcion, v.Salon, v.Delivery, v.Disponible, v.Chef, v.Posicion
FROM (VALUES
    (N'Parrilla', N'Ojo de Bife con Papas Rústicas', N'400 g de carne premium madurada a las brasas, con manteca de hierbas y papas al romero.', 58000, 66700, 1, 1, 0),
    (N'Pizzas y Pastas', N'Pizza Margherita DOC', N'Masa madre de 48 h, tomates San Marzano, mozzarella di bufala y albahaca.', 36000, 41400, 1, 0, 1),
    (N'Del Mar', N'Salmón Rosado a las Brasas', N'Filete de salmón con puré de coliflor al azafrán y espárragos salteados.', 64000, 73600, 0, 0, 2),
    (N'Parrilla', N'Hamburguesa Trufada Deluxe', N'Doble medallón de picaña (240 g), cheddar madurado, cebolla caramelizada y mayonesa de trufa.', 42000, 48300, 1, 0, 3),
    (N'Entradas', N'Empanadas Criollas (x3)', N'Masa de maíz rellena de carne desmechada y papa, con ají y guacamole.', 18000, 20700, 1, 0, 4),
    (N'Entradas', N'Burrata con Tomates Asados', N'Burrata sobre tomates cherry rostizados, pesto y pan de masa madre.', 32000, 36800, 1, 1, 5),
    (N'Pizzas y Pastas', N'Ravioles de Cuatro Quesos', N'Pasta fresca rellena de cuatro quesos en mantequilla y salvia.', 39000, 44800, 1, 0, 6),
    (N'Del Mar', N'Ceviche Peruano', N'Pesca blanca en leche de tigre, cebolla morada, ají limo, choclo y camote.', 34000, 39100, 0, 0, 7),
    (N'Bebidas', N'Limonada de Coco', N'Limonada frappé con crema de coco y hierbabuena.', 12000, 13800, 1, 0, 8),
    (N'Bebidas', N'Cerveza Artesanal IPA', N'Pinta de IPA local con notas cítricas.', 15000, 17200, 1, 0, 9),
    (N'Postres', N'Volcán de Chocolate', N'Bizcocho tibio de chocolate 70 % con centro líquido y helado de vainilla.', 19000, 21800, 1, 1, 10),
    (N'Postres', N'Tiramisú Casero', N'Bizcocho con café, crema de mascarpone y cacao amargo.', 17000, 19600, 1, 0, 11)
) v (Categoria, Nombre, Descripcion, Salon, Delivery, Disponible, Chef, Posicion)
JOIN dbo.Categoria c ON c.Nombre = v.Categoria;

INSERT INTO dbo.Etiqueta (Nombre, Tipo) VALUES
    (N'Carnes Grill', N'Etiqueta'), (N'Horno de Barro', N'Etiqueta'), (N'Pesca del Día', N'Etiqueta'),
    (N'Top Ventas QR', N'Etiqueta'), (N'Plato Más Vendido', N'Etiqueta'), (N'Para compartir', N'Etiqueta'),
    (N'Nuevo', N'Etiqueta'), (N'Local', N'Etiqueta'), (N'Postre estrella', N'Etiqueta'),
    (N'Sin TACC', N'Dietario'), (N'Vegetariano', N'Dietario'), (N'Gluten', N'Dietario'),
    (N'Lácteos', N'Dietario'), (N'Picante', N'Dietario');

INSERT INTO dbo.ProductoEtiqueta (ProductoId, EtiquetaId)
SELECT p.Id, e.Id
FROM (VALUES
    (N'Ojo de Bife con Papas Rústicas', N'Carnes Grill', N'Etiqueta'), (N'Ojo de Bife con Papas Rústicas', N'Sin TACC', N'Dietario'),
    (N'Pizza Margherita DOC', N'Horno de Barro', N'Etiqueta'), (N'Pizza Margherita DOC', N'Vegetariano', N'Dietario'),
    (N'Salmón Rosado a las Brasas', N'Pesca del Día', N'Etiqueta'), (N'Salmón Rosado a las Brasas', N'Sin TACC', N'Dietario'),
    (N'Hamburguesa Trufada Deluxe', N'Top Ventas QR', N'Etiqueta'), (N'Hamburguesa Trufada Deluxe', N'Plato Más Vendido', N'Etiqueta'),
    (N'Hamburguesa Trufada Deluxe', N'Gluten', N'Dietario'), (N'Hamburguesa Trufada Deluxe', N'Lácteos', N'Dietario'),
    (N'Empanadas Criollas (x3)', N'Para compartir', N'Etiqueta'), (N'Empanadas Criollas (x3)', N'Sin TACC', N'Dietario'),
    (N'Empanadas Criollas (x3)', N'Picante', N'Dietario'),
    (N'Burrata con Tomates Asados', N'Nuevo', N'Etiqueta'), (N'Burrata con Tomates Asados', N'Vegetariano', N'Dietario'),
    (N'Burrata con Tomates Asados', N'Lácteos', N'Dietario'),
    (N'Ravioles de Cuatro Quesos', N'Vegetariano', N'Dietario'), (N'Ravioles de Cuatro Quesos', N'Lácteos', N'Dietario'),
    (N'Ceviche Peruano', N'Pesca del Día', N'Etiqueta'), (N'Ceviche Peruano', N'Sin TACC', N'Dietario'),
    (N'Ceviche Peruano', N'Picante', N'Dietario'),
    (N'Limonada de Coco', N'Vegetariano', N'Dietario'), (N'Limonada de Coco', N'Sin TACC', N'Dietario'),
    (N'Cerveza Artesanal IPA', N'Local', N'Etiqueta'), (N'Cerveza Artesanal IPA', N'Gluten', N'Dietario'),
    (N'Volcán de Chocolate', N'Postre estrella', N'Etiqueta'), (N'Volcán de Chocolate', N'Vegetariano', N'Dietario'),
    (N'Volcán de Chocolate', N'Lácteos', N'Dietario'),
    (N'Tiramisú Casero', N'Vegetariano', N'Dietario'), (N'Tiramisú Casero', N'Lácteos', N'Dietario')
) v (Producto, Etiqueta, Tipo)
JOIN dbo.Producto p ON p.Nombre = v.Producto
JOIN dbo.Etiqueta e ON e.Nombre = v.Etiqueta AND e.Tipo = v.Tipo;

INSERT INTO dbo.CategoriaInsumo (Nombre) VALUES
    (N'Carnes'), (N'Pescados'), (N'Lácteos'), (N'Verduras'), (N'Secos'), (N'Panadería'), (N'Bebidas'), (N'Postres');

-- Insumos de InventarioForm + los que usan las recetas de la carta (stock de ejemplo).
INSERT INTO dbo.Insumo (Nombre, CategoriaInsumoId, Unidad, Stock, StockMinimo, CostoUnitario)
SELECT v.Nombre, c.Id, v.Unidad, v.Stock, v.Minimo, v.Costo
FROM (VALUES
    (N'Carne de res (lomo)',    N'Carnes',    'kg',  4.2, 5,   28000),
    (N'Queso mozzarella',       N'Lácteos',   'kg',  1.1, 2,   18000),
    (N'Tomate chonto',          N'Verduras',  'kg',  8,   3,    3500),
    (N'Cerveza artesanal',      N'Bebidas',   'und', 2,   6,    4500),
    (N'Harina trigo',           N'Secos',     'kg', 12,   5,    2800),
    (N'Ojo de bife madurado',   N'Carnes',    'kg',  6,   3,   62000),
    (N'Carne de res molida',    N'Carnes',    'kg',  5,   2,   24000),
    (N'Carne desmechada',       N'Carnes',    'kg',  3,   1,   26000),
    (N'Salmón fresco',          N'Pescados',  'kg',  2,   1,   78000),
    (N'Pescado blanco',         N'Pescados',  'kg',  2,   1,   38000),
    (N'Queso cheddar',          N'Lácteos',   'kg',  1.5, 0.5, 42000),
    (N'Queso parmesano',        N'Lácteos',   'kg',  1,   0.3, 85000),
    (N'Burrata',                N'Lácteos',   'und', 8,   4,    9000),
    (N'Mantequilla de romero',  N'Lácteos',   'kg',  0.5, 0.2, 30000),
    (N'Papa criolla',           N'Verduras',  'kg', 10,   4,    4000),
    (N'Coliflor',               N'Verduras',  'kg',  3,   1,    5000),
    (N'Tomate cherry',          N'Verduras',  'kg',  2,   1,    9000),
    (N'Limón',                  N'Verduras',  'kg',  4,   2,    4500),
    (N'Harina de maíz',         N'Secos',     'kg',  6,   2,    3200),
    (N'Pasta fresca',           N'Secos',     'kg',  4,   2,   16000),
    (N'Chocolate 70%',          N'Postres',   'kg',  1,   0.5, 48000),
    (N'Pan brioche',            N'Panadería', 'und', 24, 10,    1500),
    (N'Crema de coco',          N'Bebidas',   'L',   2,   1,   15000),
    (N'Helado de vainilla',     N'Postres',   'L',   3,   1,   18000)
) v (Nombre, Categoria, Unidad, Stock, Minimo, Costo)
JOIN dbo.CategoriaInsumo c ON c.Nombre = v.Categoria;

-- Recetas (InsumoLink de la carta de ejemplo).
INSERT INTO dbo.ProductoInsumo (ProductoId, InsumoId, Cantidad)
SELECT p.Id, i.Id, v.Cantidad
FROM (VALUES
    (N'Ojo de Bife con Papas Rústicas', N'Ojo de bife madurado', 0.4),
    (N'Ojo de Bife con Papas Rústicas', N'Papa criolla', 0.25),
    (N'Ojo de Bife con Papas Rústicas', N'Mantequilla de romero', 0.02),
    (N'Pizza Margherita DOC', N'Harina trigo', 0.25),
    (N'Pizza Margherita DOC', N'Queso mozzarella', 0.18),
    (N'Pizza Margherita DOC', N'Tomate chonto', 0.12),
    (N'Salmón Rosado a las Brasas', N'Salmón fresco', 0.25),
    (N'Salmón Rosado a las Brasas', N'Coliflor', 0.15),
    (N'Hamburguesa Trufada Deluxe', N'Carne de res molida', 0.24),
    (N'Hamburguesa Trufada Deluxe', N'Queso cheddar', 0.06),
    (N'Hamburguesa Trufada Deluxe', N'Pan brioche', 1),
    (N'Empanadas Criollas (x3)', N'Harina de maíz', 0.12),
    (N'Empanadas Criollas (x3)', N'Carne desmechada', 0.1),
    (N'Burrata con Tomates Asados', N'Burrata', 1),
    (N'Burrata con Tomates Asados', N'Tomate cherry', 0.1),
    (N'Ravioles de Cuatro Quesos', N'Pasta fresca', 0.2),
    (N'Ravioles de Cuatro Quesos', N'Queso parmesano', 0.04),
    (N'Ceviche Peruano', N'Pescado blanco', 0.18),
    (N'Ceviche Peruano', N'Limón', 0.1),
    (N'Limonada de Coco', N'Limón', 0.08),
    (N'Limonada de Coco', N'Crema de coco', 0.05),
    (N'Cerveza Artesanal IPA', N'Cerveza artesanal', 1),
    (N'Volcán de Chocolate', N'Chocolate 70%', 0.06),
    (N'Volcán de Chocolate', N'Helado de vainilla', 0.08)
) v (Producto, Insumo, Cantidad)
JOIN dbo.Producto p ON p.Nombre = v.Producto
JOIN dbo.Insumo   i ON i.Nombre = v.Insumo;
GO

PRINT N'RestoOS creada correctamente.';
GO
