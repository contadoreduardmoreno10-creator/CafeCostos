/* =====================================================================================
   CaféCostos · Script 001 · Esquema inicial
   Motor: SQL Server 2019+ (LocalDB / Express / Standard). El instalador incluye LocalDB 2022.
   (Compresión de datos disponible en todas las ediciones desde 2016 SP1; STRING_AGG desde 2017.)

   - Compresión PAGE en tablas de movimientos e históricos (crecen sin límite).
   - Compresión ROW en tablas maestras (pocas filas, se actualizan con frecuencia).
   - Inventarios valorizados por (Cantidad disponible, Valor disponible) para cuadre exacto.
   - Este script lo ejecuta la aplicación automáticamente al primer arranque.
     Los bloques se separan con GO; la aplicación los ejecuta uno a uno.
   ===================================================================================== */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/* -------------------------------------------------------------------------------------
   CONFIGURACIÓN
   ------------------------------------------------------------------------------------- */
CREATE TABLE dbo.Parametros
(
    Clave        varchar(60)    NOT NULL,
    Valor        nvarchar(400)  NULL,
    Descripcion  nvarchar(200)  NULL,
    CONSTRAINT PK_Parametros PRIMARY KEY CLUSTERED (Clave)
) WITH (DATA_COMPRESSION = ROW);
GO

CREATE TABLE dbo.Consecutivos
(
    Tipo       varchar(20)  NOT NULL,
    Prefijo    varchar(10)  NOT NULL,
    Siguiente  int          NOT NULL CONSTRAINT DF_Consecutivos_Siguiente DEFAULT (1),
    CONSTRAINT PK_Consecutivos PRIMARY KEY CLUSTERED (Tipo),
    CONSTRAINT CK_Consecutivos_Siguiente CHECK (Siguiente > 0)
) WITH (DATA_COMPRESSION = ROW);
GO

/* -------------------------------------------------------------------------------------
   1. MATERIA PRIMA: LOTES DE CAFÉ SECO
   ------------------------------------------------------------------------------------- */
CREATE TABLE dbo.LotesCafeSeco
(
    IdLote            int            IDENTITY(1,1) NOT NULL,
    Codigo            varchar(20)    NOT NULL,
    FechaRecepcion    date           NOT NULL,
    Proveedor         nvarchar(120)  NOT NULL,
    Origen            nvarchar(120)  NULL,      -- finca / región
    Variedad          nvarchar(80)   NULL,
    Humedad           decimal(5,2)   NULL,      -- % humedad al recibir
    KgRecibidos       decimal(12,3)  NOT NULL,
    CostoAdquisicion  decimal(18,2)  NOT NULL,
    CostoFlete        decimal(18,2)  NOT NULL CONSTRAINT DF_Lotes_Flete DEFAULT (0),
    CostoTotal        AS (CostoAdquisicion + CostoFlete) PERSISTED,
    CostoKg           AS (CAST((CostoAdquisicion + CostoFlete) / KgRecibidos AS decimal(18,4))) PERSISTED,
    KgDisponibles     decimal(12,3)  NOT NULL,
    ValorDisponible   decimal(18,2)  NOT NULL,
    Estado            varchar(10)    NOT NULL CONSTRAINT DF_Lotes_Estado DEFAULT ('ACTIVO'),
    Observaciones     nvarchar(400)  NULL,
    FechaRegistro     datetime2(0)   NOT NULL CONSTRAINT DF_Lotes_FechaRegistro DEFAULT (SYSDATETIME()),
    CONSTRAINT PK_LotesCafeSeco PRIMARY KEY CLUSTERED (IdLote),
    CONSTRAINT CK_Lotes_Kg CHECK (KgRecibidos > 0),
    CONSTRAINT CK_Lotes_Costos CHECK (CostoAdquisicion >= 0 AND CostoFlete >= 0),
    CONSTRAINT CK_Lotes_Disponible CHECK (KgDisponibles >= 0 AND KgDisponibles <= KgRecibidos AND ValorDisponible >= 0),
    CONSTRAINT CK_Lotes_Humedad CHECK (Humedad IS NULL OR (Humedad >= 0 AND Humedad <= 100)),
    CONSTRAINT CK_Lotes_Estado CHECK (Estado IN ('ACTIVO','AGOTADO','ANULADO'))
) WITH (DATA_COMPRESSION = PAGE);
GO
CREATE UNIQUE INDEX UX_Lotes_Codigo ON dbo.LotesCafeSeco (Codigo) WITH (DATA_COMPRESSION = PAGE);
CREATE INDEX IX_Lotes_Estado ON dbo.LotesCafeSeco (Estado, FechaRecepcion)
    INCLUDE (KgDisponibles, ValorDisponible) WITH (DATA_COMPRESSION = PAGE);
GO

/* -------------------------------------------------------------------------------------
   2. TOSTIÓN
   ------------------------------------------------------------------------------------- */
CREATE TABLE dbo.OrdenesTueste
(
    IdOrdenTueste    int            IDENTITY(1,1) NOT NULL,
    Numero           varchar(20)    NOT NULL,
    Fecha            date           NOT NULL,
    PerfilTueste     nvarchar(60)   NULL,
    KgSecos          decimal(12,3)  NOT NULL,
    KgTostados       decimal(12,3)  NOT NULL,
    KgMerma          AS (KgSecos - KgTostados) PERSISTED,
    PctMerma         decimal(7,3)   NOT NULL,
    CostoCafeSeco    decimal(18,2)  NOT NULL,
    CostoEnergia     decimal(18,2)  NOT NULL CONSTRAINT DF_Tueste_Energia DEFAULT (0),
    CostoManoObra    decimal(18,2)  NOT NULL CONSTRAINT DF_Tueste_MO DEFAULT (0),
    OtrosCostos      decimal(18,2)  NOT NULL CONSTRAINT DF_Tueste_Otros DEFAULT (0),
    CostoTotal       AS (CostoCafeSeco + CostoEnergia + CostoManoObra + OtrosCostos) PERSISTED,
    CostoKgTostado   decimal(18,4)  NOT NULL,
    KgDisponibles    decimal(12,3)  NOT NULL,
    ValorDisponible  decimal(18,2)  NOT NULL,
    Estado           varchar(10)    NOT NULL CONSTRAINT DF_Tueste_Estado DEFAULT ('ACTIVO'),
    Observaciones    nvarchar(400)  NULL,
    FechaRegistro    datetime2(0)   NOT NULL CONSTRAINT DF_Tueste_FechaRegistro DEFAULT (SYSDATETIME()),
    CONSTRAINT PK_OrdenesTueste PRIMARY KEY CLUSTERED (IdOrdenTueste),
    CONSTRAINT CK_Tueste_Kilos CHECK (KgSecos > 0 AND KgTostados > 0 AND KgTostados < KgSecos),
    CONSTRAINT CK_Tueste_Costos CHECK (CostoCafeSeco >= 0 AND CostoEnergia >= 0 AND CostoManoObra >= 0 AND OtrosCostos >= 0),
    CONSTRAINT CK_Tueste_Disponible CHECK (KgDisponibles >= 0 AND KgDisponibles <= KgTostados AND ValorDisponible >= 0),
    CONSTRAINT CK_Tueste_Estado CHECK (Estado IN ('ACTIVO','AGOTADO','ANULADO'))
) WITH (DATA_COMPRESSION = PAGE);
GO
CREATE UNIQUE INDEX UX_Tueste_Numero ON dbo.OrdenesTueste (Numero) WITH (DATA_COMPRESSION = PAGE);
CREATE INDEX IX_Tueste_Estado ON dbo.OrdenesTueste (Estado, Fecha)
    INCLUDE (KgDisponibles, ValorDisponible) WITH (DATA_COMPRESSION = PAGE);
GO

-- Detalle de lotes secos consumidos (permite mezclas / blends)
CREATE TABLE dbo.OrdenTuesteLotes
(
    IdOrdenTuesteLote  int            IDENTITY(1,1) NOT NULL,
    IdOrdenTueste      int            NOT NULL,
    IdLote             int            NOT NULL,
    KgUsados           decimal(12,3)  NOT NULL,
    CostoKg            decimal(18,4)  NOT NULL,
    CostoTotal         decimal(18,2)  NOT NULL,
    CONSTRAINT PK_OrdenTuesteLotes PRIMARY KEY CLUSTERED (IdOrdenTuesteLote),
    CONSTRAINT FK_OTL_Orden FOREIGN KEY (IdOrdenTueste) REFERENCES dbo.OrdenesTueste (IdOrdenTueste),
    CONSTRAINT FK_OTL_Lote  FOREIGN KEY (IdLote) REFERENCES dbo.LotesCafeSeco (IdLote),
    CONSTRAINT CK_OTL_Valores CHECK (KgUsados > 0 AND CostoTotal >= 0)
) WITH (DATA_COMPRESSION = PAGE);
GO
CREATE UNIQUE INDEX UX_OTL_OrdenLote ON dbo.OrdenTuesteLotes (IdOrdenTueste, IdLote) WITH (DATA_COMPRESSION = PAGE);
CREATE INDEX IX_OTL_Lote ON dbo.OrdenTuesteLotes (IdLote) WITH (DATA_COMPRESSION = PAGE);
GO

/* -------------------------------------------------------------------------------------
   3. INSUMOS DE EMPAQUE (bolsas, válvulas, etiquetas) + KARDEX
   ------------------------------------------------------------------------------------- */
CREATE TABLE dbo.Insumos
(
    IdInsumo       int            IDENTITY(1,1) NOT NULL,
    Codigo         varchar(20)    NOT NULL,
    Nombre         nvarchar(100)  NOT NULL,
    Unidad         nvarchar(20)   NOT NULL CONSTRAINT DF_Insumos_Unidad DEFAULT (N'und'),
    Stock          decimal(14,3)  NOT NULL CONSTRAINT DF_Insumos_Stock DEFAULT (0),
    ValorStock     decimal(18,2)  NOT NULL CONSTRAINT DF_Insumos_Valor DEFAULT (0),
    CostoPromedio  AS (CAST(CASE WHEN Stock > 0 THEN ValorStock / Stock ELSE 0 END AS decimal(18,4))) PERSISTED,
    UltimoCosto    decimal(18,4)  NOT NULL CONSTRAINT DF_Insumos_UltimoCosto DEFAULT (0),
    StockMinimo    decimal(14,3)  NOT NULL CONSTRAINT DF_Insumos_Minimo DEFAULT (0),
    Activo         bit            NOT NULL CONSTRAINT DF_Insumos_Activo DEFAULT (1),
    CONSTRAINT PK_Insumos PRIMARY KEY CLUSTERED (IdInsumo),
    CONSTRAINT CK_Insumos_Saldos CHECK (Stock >= 0 AND ValorStock >= 0 AND StockMinimo >= 0 AND UltimoCosto >= 0)
) WITH (DATA_COMPRESSION = ROW);
GO
CREATE UNIQUE INDEX UX_Insumos_Codigo ON dbo.Insumos (Codigo) WITH (DATA_COMPRESSION = ROW);
GO

CREATE TABLE dbo.MovimientosInsumo
(
    IdMovimiento   bigint         IDENTITY(1,1) NOT NULL,
    IdInsumo       int            NOT NULL,
    Fecha          datetime2(0)   NOT NULL,
    Tipo           varchar(12)    NOT NULL,      -- COMPRA, CONSUMO, AJUSTE, ANULACION
    Cantidad       decimal(14,3)  NOT NULL,      -- con signo: + entra, − sale
    CostoUnitario  decimal(18,4)  NOT NULL,
    ValorTotal     decimal(18,2)  NOT NULL,      -- con signo
    SaldoCantidad  decimal(14,3)  NOT NULL,
    SaldoValor     decimal(18,2)  NOT NULL,
    Documento      varchar(30)    NULL,
    Detalle        nvarchar(200)  NULL,
    CONSTRAINT PK_MovimientosInsumo PRIMARY KEY CLUSTERED (IdMovimiento),
    CONSTRAINT FK_MovInsumo_Insumo FOREIGN KEY (IdInsumo) REFERENCES dbo.Insumos (IdInsumo),
    CONSTRAINT CK_MovInsumo_Tipo CHECK (Tipo IN ('COMPRA','CONSUMO','AJUSTE','ANULACION')),
    CONSTRAINT CK_MovInsumo_Cantidad CHECK (Cantidad <> 0)
) WITH (DATA_COMPRESSION = PAGE);
GO
CREATE INDEX IX_MovInsumo_Insumo ON dbo.MovimientosInsumo (IdInsumo, Fecha, IdMovimiento) WITH (DATA_COMPRESSION = PAGE);
GO

/* -------------------------------------------------------------------------------------
   4. PRESENTACIONES COMERCIALES Y RECETA DE INSUMOS
   ------------------------------------------------------------------------------------- */
CREATE TABLE dbo.Presentaciones
(
    IdPresentacion  int            IDENTITY(1,1) NOT NULL,
    Nombre          nvarchar(80)   NOT NULL,
    GramosNetos     int            NOT NULL,
    TipoMolienda    varchar(10)    NOT NULL,
    GradoMolienda   nvarchar(40)   NULL,       -- fino, medio, grueso (solo molido)
    PrecioVenta     decimal(18,2)  NOT NULL CONSTRAINT DF_Presentaciones_Precio DEFAULT (0),
    Activo          bit            NOT NULL CONSTRAINT DF_Presentaciones_Activo DEFAULT (1),
    CONSTRAINT PK_Presentaciones PRIMARY KEY CLUSTERED (IdPresentacion),
    CONSTRAINT CK_Presentaciones_Gramos CHECK (GramosNetos > 0),
    CONSTRAINT CK_Presentaciones_Tipo CHECK (TipoMolienda IN ('GRANO','MOLIDO')),
    CONSTRAINT CK_Presentaciones_Precio CHECK (PrecioVenta >= 0)
) WITH (DATA_COMPRESSION = ROW);
GO
CREATE UNIQUE INDEX UX_Presentaciones_Nombre ON dbo.Presentaciones (Nombre) WITH (DATA_COMPRESSION = ROW);
GO

CREATE TABLE dbo.PresentacionInsumos
(
    IdPresentacion     int            NOT NULL,
    IdInsumo           int            NOT NULL,
    CantidadPorUnidad  decimal(10,4)  NOT NULL,
    CONSTRAINT PK_PresentacionInsumos PRIMARY KEY CLUSTERED (IdPresentacion, IdInsumo),
    CONSTRAINT FK_PI_Presentacion FOREIGN KEY (IdPresentacion) REFERENCES dbo.Presentaciones (IdPresentacion) ON DELETE CASCADE,
    CONSTRAINT FK_PI_Insumo FOREIGN KEY (IdInsumo) REFERENCES dbo.Insumos (IdInsumo),
    CONSTRAINT CK_PI_Cantidad CHECK (CantidadPorUnidad > 0)
) WITH (DATA_COMPRESSION = ROW);
GO

/* -------------------------------------------------------------------------------------
   5. PRODUCTO TERMINADO: lotes de producción empacados (molienda + fraccionamiento + empaque)
   ------------------------------------------------------------------------------------- */
CREATE TABLE dbo.InventarioEmpacado
(
    IdInventarioEmpacado  int            IDENTITY(1,1) NOT NULL,
    CodigoLote            varchar(20)    NOT NULL,
    Fecha                 date           NOT NULL,
    IdOrdenTueste         int            NOT NULL,
    IdPresentacion        int            NOT NULL,
    Unidades              int            NOT NULL,
    KgNetos               decimal(12,3)  NOT NULL,
    KgTostadoUsado        decimal(12,3)  NOT NULL,
    KgMermaMolienda       AS (KgTostadoUsado - KgNetos) PERSISTED,
    PctMermaMolienda      decimal(7,3)   NOT NULL,
    CostoCafe             decimal(18,2)  NOT NULL,
    CostoInsumos          decimal(18,2)  NOT NULL,
    CostoMolienda         decimal(18,2)  NOT NULL CONSTRAINT DF_IE_Molienda DEFAULT (0),
    CostoManoObra         decimal(18,2)  NOT NULL CONSTRAINT DF_IE_MO DEFAULT (0),
    OtrosCostos           decimal(18,2)  NOT NULL CONSTRAINT DF_IE_Otros DEFAULT (0),
    CostoTotal            AS (CostoCafe + CostoInsumos + CostoMolienda + CostoManoObra + OtrosCostos) PERSISTED,
    CostoUnitario         decimal(18,4)  NOT NULL,
    UnidadesDisponibles   int            NOT NULL,
    ValorDisponible       decimal(18,2)  NOT NULL,
    FechaVencimiento      date           NULL,
    Estado                varchar(10)    NOT NULL CONSTRAINT DF_IE_Estado DEFAULT ('ACTIVO'),
    Observaciones         nvarchar(400)  NULL,
    FechaRegistro         datetime2(0)   NOT NULL CONSTRAINT DF_IE_FechaRegistro DEFAULT (SYSDATETIME()),
    CONSTRAINT PK_InventarioEmpacado PRIMARY KEY CLUSTERED (IdInventarioEmpacado),
    CONSTRAINT FK_IE_Tueste FOREIGN KEY (IdOrdenTueste) REFERENCES dbo.OrdenesTueste (IdOrdenTueste),
    CONSTRAINT FK_IE_Presentacion FOREIGN KEY (IdPresentacion) REFERENCES dbo.Presentaciones (IdPresentacion),
    CONSTRAINT CK_IE_Unidades CHECK (Unidades > 0 AND UnidadesDisponibles >= 0 AND UnidadesDisponibles <= Unidades),
    CONSTRAINT CK_IE_Kilos CHECK (KgNetos > 0 AND KgTostadoUsado >= KgNetos),
    CONSTRAINT CK_IE_Costos CHECK (CostoCafe >= 0 AND CostoInsumos >= 0 AND CostoMolienda >= 0
                                   AND CostoManoObra >= 0 AND OtrosCostos >= 0 AND ValorDisponible >= 0),
    CONSTRAINT CK_IE_Estado CHECK (Estado IN ('ACTIVO','AGOTADO','ANULADO'))
) WITH (DATA_COMPRESSION = PAGE);
GO
CREATE UNIQUE INDEX UX_IE_CodigoLote ON dbo.InventarioEmpacado (CodigoLote) WITH (DATA_COMPRESSION = PAGE);
CREATE INDEX IX_IE_Disponibles ON dbo.InventarioEmpacado (Estado, IdPresentacion)
    INCLUDE (UnidadesDisponibles, ValorDisponible, CostoUnitario) WITH (DATA_COMPRESSION = PAGE);
CREATE INDEX IX_IE_Tueste ON dbo.InventarioEmpacado (IdOrdenTueste) WITH (DATA_COMPRESSION = PAGE);
GO

-- Insumos consumidos por cada lote empacado (auditoría del costo de la bolsa)
CREATE TABLE dbo.InventarioEmpacadoInsumos
(
    IdInventarioEmpacado  int            NOT NULL,
    IdInsumo              int            NOT NULL,
    Cantidad              decimal(14,3)  NOT NULL,
    CostoUnitario         decimal(18,4)  NOT NULL,
    CostoTotal            decimal(18,2)  NOT NULL,
    CONSTRAINT PK_IEInsumos PRIMARY KEY CLUSTERED (IdInventarioEmpacado, IdInsumo),
    CONSTRAINT FK_IEI_Empacado FOREIGN KEY (IdInventarioEmpacado) REFERENCES dbo.InventarioEmpacado (IdInventarioEmpacado),
    CONSTRAINT FK_IEI_Insumo FOREIGN KEY (IdInsumo) REFERENCES dbo.Insumos (IdInsumo),
    CONSTRAINT CK_IEI_Valores CHECK (Cantidad > 0 AND CostoTotal >= 0)
) WITH (DATA_COMPRESSION = PAGE);
GO

/* -------------------------------------------------------------------------------------
   6. CLIENTES
   ------------------------------------------------------------------------------------- */
CREATE TABLE dbo.Clientes
(
    IdCliente      int            IDENTITY(1,1) NOT NULL,
    Documento      varchar(20)    NULL,          -- NIT o cédula
    Nombre         nvarchar(150)  NOT NULL,
    Telefono       varchar(30)    NULL,
    Direccion      nvarchar(200)  NULL,
    Ciudad         nvarchar(80)   NULL,
    Email          varchar(120)   NULL,
    CupoCredito    decimal(18,2)  NULL,          -- NULL = sin límite · 0 = sin crédito
    DiasCredito    int            NOT NULL CONSTRAINT DF_Clientes_Dias DEFAULT (30),
    EsGenerico     bit            NOT NULL CONSTRAINT DF_Clientes_Generico DEFAULT (0),
    Activo         bit            NOT NULL CONSTRAINT DF_Clientes_Activo DEFAULT (1),
    FechaRegistro  datetime2(0)   NOT NULL CONSTRAINT DF_Clientes_FechaRegistro DEFAULT (SYSDATETIME()),
    CONSTRAINT PK_Clientes PRIMARY KEY CLUSTERED (IdCliente),
    CONSTRAINT CK_Clientes_Credito CHECK ((CupoCredito IS NULL OR CupoCredito >= 0) AND DiasCredito BETWEEN 0 AND 365)
) WITH (DATA_COMPRESSION = ROW);
GO
CREATE UNIQUE INDEX UX_Clientes_Documento ON dbo.Clientes (Documento) WHERE Documento IS NOT NULL WITH (DATA_COMPRESSION = ROW);
CREATE INDEX IX_Clientes_Nombre ON dbo.Clientes (Nombre) WITH (DATA_COMPRESSION = ROW);
GO

/* -------------------------------------------------------------------------------------
   7. VENTAS (comprobante de venta interno) Y DETALLE
   ------------------------------------------------------------------------------------- */
CREATE TABLE dbo.Ventas
(
    IdVenta           int            IDENTITY(1,1) NOT NULL,
    NumeroFactura     varchar(20)    NOT NULL,
    Fecha             date           NOT NULL,
    IdCliente         int            NOT NULL,
    TipoPago          varchar(10)    NOT NULL,
    MedioPago         nvarchar(30)   NULL,
    FechaVencimiento  date           NULL,
    Subtotal          decimal(18,2)  NOT NULL,
    Descuento         decimal(18,2)  NOT NULL CONSTRAINT DF_Ventas_Descuento DEFAULT (0),
    Total             AS (Subtotal - Descuento) PERSISTED,
    CostoTotal        decimal(18,2)  NOT NULL,
    Saldo             decimal(18,2)  NOT NULL,
    Estado            varchar(10)    NOT NULL,
    Observaciones     nvarchar(400)  NULL,
    MotivoAnulacion   nvarchar(200)  NULL,
    FechaAnulacion    datetime2(0)   NULL,
    FechaRegistro     datetime2(0)   NOT NULL CONSTRAINT DF_Ventas_FechaRegistro DEFAULT (SYSDATETIME()),
    CONSTRAINT PK_Ventas PRIMARY KEY CLUSTERED (IdVenta),
    CONSTRAINT FK_Ventas_Cliente FOREIGN KEY (IdCliente) REFERENCES dbo.Clientes (IdCliente),
    CONSTRAINT CK_Ventas_TipoPago CHECK (TipoPago IN ('CONTADO','CREDITO')),
    CONSTRAINT CK_Ventas_Estado CHECK (Estado IN ('PENDIENTE','PAGADA','ANULADA')),
    CONSTRAINT CK_Ventas_Valores CHECK (Subtotal >= 0 AND Descuento >= 0 AND Descuento <= Subtotal AND CostoTotal >= 0),
    CONSTRAINT CK_Ventas_Saldo CHECK (Saldo >= 0 AND Saldo <= Subtotal - Descuento),
    CONSTRAINT CK_Ventas_Credito CHECK (TipoPago = 'CONTADO' OR FechaVencimiento IS NOT NULL)
) WITH (DATA_COMPRESSION = PAGE);
GO
CREATE UNIQUE INDEX UX_Ventas_Numero ON dbo.Ventas (NumeroFactura) WITH (DATA_COMPRESSION = PAGE);
CREATE INDEX IX_Ventas_Fecha ON dbo.Ventas (Fecha) INCLUDE (Subtotal, Descuento, CostoTotal, Estado) WITH (DATA_COMPRESSION = PAGE);
CREATE INDEX IX_Ventas_Cliente ON dbo.Ventas (IdCliente, Fecha) WITH (DATA_COMPRESSION = PAGE);
-- Índice filtrado: solo facturas con saldo → consultas de cartera instantáneas
CREATE INDEX IX_Ventas_Cartera ON dbo.Ventas (IdCliente, FechaVencimiento)
    INCLUDE (Saldo, NumeroFactura) WHERE Estado = 'PENDIENTE' WITH (DATA_COMPRESSION = PAGE);
GO

CREATE TABLE dbo.DetalleVentas
(
    IdDetalleVenta        int            IDENTITY(1,1) NOT NULL,
    IdVenta               int            NOT NULL,
    IdInventarioEmpacado  int            NOT NULL,   -- lote de producción vendido (trazabilidad)
    Cantidad              int            NOT NULL,
    PrecioUnitario        decimal(18,2)  NOT NULL,
    Subtotal              AS (CAST(Cantidad * PrecioUnitario AS decimal(18,2))) PERSISTED,
    CostoUnitario         decimal(18,4)  NOT NULL,
    CostoTotal            decimal(18,2)  NOT NULL,
    CONSTRAINT PK_DetalleVentas PRIMARY KEY CLUSTERED (IdDetalleVenta),
    CONSTRAINT FK_DV_Venta FOREIGN KEY (IdVenta) REFERENCES dbo.Ventas (IdVenta),
    CONSTRAINT FK_DV_Empacado FOREIGN KEY (IdInventarioEmpacado) REFERENCES dbo.InventarioEmpacado (IdInventarioEmpacado),
    CONSTRAINT CK_DV_Valores CHECK (Cantidad > 0 AND PrecioUnitario >= 0 AND CostoTotal >= 0)
) WITH (DATA_COMPRESSION = PAGE);
GO
CREATE UNIQUE INDEX UX_DV_VentaLote ON dbo.DetalleVentas (IdVenta, IdInventarioEmpacado) WITH (DATA_COMPRESSION = PAGE);
CREATE INDEX IX_DV_Empacado ON dbo.DetalleVentas (IdInventarioEmpacado) WITH (DATA_COMPRESSION = PAGE);
GO

/* -------------------------------------------------------------------------------------
   8. CARTERA: ABONOS / RECIBOS DE CAJA
   ------------------------------------------------------------------------------------- */
CREATE TABLE dbo.AbonosCartera
(
    IdAbono         int            IDENTITY(1,1) NOT NULL,
    NumeroRecibo    varchar(20)    NOT NULL,
    IdVenta         int            NOT NULL,
    Fecha           date           NOT NULL,
    Valor           decimal(18,2)  NOT NULL,
    MedioPago       nvarchar(30)   NULL,
    Referencia      nvarchar(60)   NULL,
    Observaciones   nvarchar(200)  NULL,
    Anulado         bit            NOT NULL CONSTRAINT DF_Abonos_Anulado DEFAULT (0),
    FechaAnulacion  datetime2(0)   NULL,
    FechaRegistro   datetime2(0)   NOT NULL CONSTRAINT DF_Abonos_FechaRegistro DEFAULT (SYSDATETIME()),
    CONSTRAINT PK_AbonosCartera PRIMARY KEY CLUSTERED (IdAbono),
    CONSTRAINT FK_Abonos_Venta FOREIGN KEY (IdVenta) REFERENCES dbo.Ventas (IdVenta),
    CONSTRAINT CK_Abonos_Valor CHECK (Valor > 0)
) WITH (DATA_COMPRESSION = PAGE);
GO
CREATE UNIQUE INDEX UX_Abonos_Recibo ON dbo.AbonosCartera (NumeroRecibo) WITH (DATA_COMPRESSION = PAGE);
CREATE INDEX IX_Abonos_Venta ON dbo.AbonosCartera (IdVenta) INCLUDE (Valor, Anulado) WITH (DATA_COMPRESSION = PAGE);
CREATE INDEX IX_Abonos_Fecha ON dbo.AbonosCartera (Fecha) WITH (DATA_COMPRESSION = PAGE);
GO

/* -------------------------------------------------------------------------------------
   VISTAS DE CONSULTA
   ------------------------------------------------------------------------------------- */
CREATE OR ALTER VIEW dbo.vw_CarteraPendiente
AS
SELECT  v.IdVenta, v.NumeroFactura, v.Fecha, v.FechaVencimiento,
        v.IdCliente, c.Nombre AS Cliente, c.Documento AS DocumentoCliente, c.Telefono,
        v.Total, v.Saldo,
        CASE WHEN DATEDIFF(DAY, v.FechaVencimiento, CAST(GETDATE() AS date)) > 0
             THEN DATEDIFF(DAY, v.FechaVencimiento, CAST(GETDATE() AS date)) ELSE 0 END AS DiasVencido
FROM dbo.Ventas v
JOIN dbo.Clientes c ON c.IdCliente = v.IdCliente
WHERE v.Estado = 'PENDIENTE';
GO

CREATE OR ALTER VIEW dbo.vw_Trazabilidad
AS
SELECT  v.NumeroFactura, v.Fecha AS FechaVenta, c.Nombre AS Cliente,
        ie.CodigoLote AS CodigoLoteEmpacado, p.Nombre AS Presentacion, dv.Cantidad,
        ot.Numero AS NumeroTueste, ot.Fecha AS FechaTueste,
        l.Codigo AS CodigoLoteSeco, l.Proveedor, l.Origen
FROM dbo.DetalleVentas dv
JOIN dbo.Ventas v              ON v.IdVenta = dv.IdVenta
JOIN dbo.Clientes c            ON c.IdCliente = v.IdCliente
JOIN dbo.InventarioEmpacado ie ON ie.IdInventarioEmpacado = dv.IdInventarioEmpacado
JOIN dbo.Presentaciones p      ON p.IdPresentacion = ie.IdPresentacion
JOIN dbo.OrdenesTueste ot      ON ot.IdOrdenTueste = ie.IdOrdenTueste
JOIN dbo.OrdenTuesteLotes otl  ON otl.IdOrdenTueste = ot.IdOrdenTueste
JOIN dbo.LotesCafeSeco l       ON l.IdLote = otl.IdLote
WHERE v.Estado <> 'ANULADA';
GO

-- Estado de compresión y tamaño por tabla (se muestra en la pantalla «Sistema»)
CREATE OR ALTER VIEW dbo.vw_EstadoAlmacenamiento
AS
SELECT  t.name AS Tabla,
        (SELECT TOP (1) p.data_compression_desc FROM sys.partitions p
          WHERE p.object_id = t.object_id AND p.index_id IN (0,1)) AS Compresion,
        (SELECT SUM(p.rows) FROM sys.partitions p
          WHERE p.object_id = t.object_id AND p.index_id IN (0,1)) AS Filas,
        (SELECT CAST(SUM(ps.reserved_page_count) * 8 / 1024.0 AS decimal(12,2))
           FROM sys.dm_db_partition_stats ps WHERE ps.object_id = t.object_id) AS TamanoMB
FROM sys.tables t
WHERE t.is_ms_shipped = 0 AND t.name <> 'VersionEsquema';
GO

/* -------------------------------------------------------------------------------------
   MANTENIMIENTO AUTOMÁTICO DE ÍNDICES Y ESTADÍSTICAS
   REORGANIZE entre 5 % y 30 % de fragmentación, REBUILD por encima de 30 %.
   REBUILD conserva la compresión PAGE/ROW definida en cada índice.
   ------------------------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_MantenimientoIndices
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @sql nvarchar(max), @tabla nvarchar(300), @indice nvarchar(300), @frag float,
            @reorganizados int = 0, @reconstruidos int = 0;

    DECLARE cur CURSOR LOCAL FAST_FORWARD FOR
        SELECT QUOTENAME(s.name) + N'.' + QUOTENAME(o.name), QUOTENAME(i.name), ps.avg_fragmentation_in_percent
        FROM sys.dm_db_index_physical_stats(DB_ID(), NULL, NULL, NULL, 'LIMITED') ps
        JOIN sys.indexes i ON i.object_id = ps.object_id AND i.index_id = ps.index_id
        JOIN sys.objects o ON o.object_id = i.object_id
        JOIN sys.schemas s ON s.schema_id = o.schema_id
        WHERE o.is_ms_shipped = 0 AND i.name IS NOT NULL
          AND ps.page_count >= 50 AND ps.avg_fragmentation_in_percent >= 5;

    OPEN cur;
    FETCH NEXT FROM cur INTO @tabla, @indice, @frag;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        IF @frag >= 30
        BEGIN
            SET @sql = N'ALTER INDEX ' + @indice + N' ON ' + @tabla + N' REBUILD;';
            SET @reconstruidos += 1;
        END
        ELSE
        BEGIN
            SET @sql = N'ALTER INDEX ' + @indice + N' ON ' + @tabla + N' REORGANIZE;';
            SET @reorganizados += 1;
        END
        EXEC sys.sp_executesql @sql;
        FETCH NEXT FROM cur INTO @tabla, @indice, @frag;
    END
    CLOSE cur;
    DEALLOCATE cur;

    EXEC sys.sp_updatestats;

    SELECT @reorganizados AS Reorganizados, @reconstruidos AS Reconstruidos;
END
GO

/* -------------------------------------------------------------------------------------
   DATOS INICIALES
   ------------------------------------------------------------------------------------- */
INSERT INTO dbo.Consecutivos (Tipo, Prefijo, Siguiente) VALUES
 ('LOTE',    'LS-', 1),
 ('TUESTE',  'TU-', 1),
 ('EMPAQUE', 'LP-', 1),
 ('FACTURA', 'FV-', 1),
 ('RECIBO',  'RC-', 1);

INSERT INTO dbo.Parametros (Clave, Valor, Descripcion) VALUES
 ('Empresa.Nombre',      N'Mi Tostadora de Café', N'Nombre comercial que aparece en los comprobantes'),
 ('Empresa.NIT',         N'',     N'NIT o cédula del propietario'),
 ('Empresa.Direccion',   N'',     N'Dirección'),
 ('Empresa.Telefono',    N'',     N'Teléfono / WhatsApp'),
 ('Empresa.Ciudad',      N'',     N'Ciudad'),
 ('Factura.PiePagina',   N'Comprobante de venta de uso interno. No constituye factura electrónica de venta.', N'Texto al pie del comprobante'),
 ('Costeo.EnergiaPorKgSeco',         N'0',   N'Valor sugerido de gas/energía por kg seco tostado'),
 ('Costeo.ManoObraTuestePorKgSeco',  N'0',   N'Valor sugerido de mano de obra por kg seco tostado'),
 ('Costeo.MoliendaPorKg',            N'0',   N'Valor sugerido de molienda (energía/maquila) por kg empacado'),
 ('Costeo.ManoObraEmpaquePorUnidad', N'0',   N'Valor sugerido de mano de obra por bolsa empacada'),
 ('Costeo.MermaMoliendaEsperada',    N'1',   N'% de merma esperada en molienda/empaque'),
 ('Costeo.MargenObjetivo',           N'40',  N'% de margen bruto objetivo para sugerir precios'),
 ('Ventas.DiasCreditoDefecto',       N'30',  N'Plazo de crédito por defecto'),
 ('Respaldo.Carpeta',                N'',    N'Carpeta donde se guardan las copias de seguridad'),
 ('Respaldo.AutomaticoAlCerrar',     N'1',   N'1 = crear copia automática diaria al cerrar'),
 ('Respaldo.UltimoAutomatico',       N'',    N'Fecha del último respaldo automático'),
 ('Respaldo.Conservar',              N'15',  N'Número de copias automáticas que se conservan'),
 ('Mantenimiento.Ultimo',            N'',    N'Fecha del último mantenimiento de índices');

SET IDENTITY_INSERT dbo.Clientes ON;
INSERT INTO dbo.Clientes (IdCliente, Documento, Nombre, CupoCredito, DiasCredito, EsGenerico)
VALUES (1, '222222222222', N'CONSUMIDOR FINAL', 0, 0, 1);
SET IDENTITY_INSERT dbo.Clientes OFF;

INSERT INTO dbo.Insumos (Codigo, Nombre, Unidad, UltimoCosto, StockMinimo) VALUES
 ('INS-001', N'Bolsa 250 g (fondo plano, zíper)', N'und', 0, 50),
 ('INS-002', N'Bolsa 500 g (fondo plano, zíper)', N'und', 0, 50),
 ('INS-003', N'Bolsa 1 kg (fondo plano, zíper)',  N'und', 0, 20),
 ('INS-004', N'Válvula desgasificadora',          N'und', 0, 100),
 ('INS-005', N'Etiqueta frontal',                 N'und', 0, 100),
 ('INS-006', N'Sticker de lote / fecha',          N'und', 0, 100);

INSERT INTO dbo.Presentaciones (Nombre, GramosNetos, TipoMolienda, GradoMolienda, PrecioVenta) VALUES
 (N'250 g Grano',   250,  'GRANO',  NULL,     0),
 (N'250 g Molido',  250,  'MOLIDO', N'Medio', 0),
 (N'500 g Grano',   500,  'GRANO',  NULL,     0),
 (N'500 g Molido',  500,  'MOLIDO', N'Medio', 0),
 (N'1 kg Grano',    1000, 'GRANO',  NULL,     0);

-- Receta: cada bolsa lleva su bolsa del tamaño correcto + válvula + etiqueta + sticker
INSERT INTO dbo.PresentacionInsumos (IdPresentacion, IdInsumo, CantidadPorUnidad)
SELECT p.IdPresentacion, i.IdInsumo, 1
FROM dbo.Presentaciones p
JOIN dbo.Insumos i ON
      (i.Codigo = 'INS-001' AND p.GramosNetos = 250)
   OR (i.Codigo = 'INS-002' AND p.GramosNetos = 500)
   OR (i.Codigo = 'INS-003' AND p.GramosNetos = 1000)
   OR  i.Codigo IN ('INS-004','INS-005','INS-006');
GO
