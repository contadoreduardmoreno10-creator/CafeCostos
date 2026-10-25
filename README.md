# CaféCostos

Aplicación de escritorio para Windows (WPF · .NET 8 · SQL Server LocalDB) para microempresas tostadoras de café: costeo con absorción de mermas, inventario por lotes con trazabilidad, ventas de contado/crédito, cartera y copias de seguridad de un clic. Instalación «llave en mano» con un solo `.exe`.

## Ciclo cubierto
`Lote de café seco → Tostión (merma absorbida) → Molienda/fraccionamiento → Empaque (+ insumos) → Venta (por lote) → Cartera y abonos`

## Inicio rápido (desarrollador)
```powershell
dotnet test tests\CafeCostos.Tests
dotnet run --project src\CafeCostos.App
.\publicar.ps1        # genera publish\CafeCostos_Instalador_1.0.0.exe
```
El instalador descarga por sí mismo los componentes de Microsoft que falten (LocalDB y Visual C++). También se fabrica automáticamente en GitHub Actions (`.github/workflows/instalador.yml`).

## Documentación
* `docs/GUIA_TECNICA.md` — arquitectura, modelo de datos, fórmulas, transacciones, respaldos, instalador.
* `docs/MANUAL_USUARIO.md` — manual para el microempresario (se instala junto con el programa).

## Prueba de humo recomendada en Windows (equipo limpio)
1. Ejecutar el instalador → debe instalar LocalDB, crear la base y el acceso directo, y abrir el programa.
2. Registrar insumos (compra), un lote seco, una tostión, un empaque, una venta a crédito y un abono.
3. Verificar el panel: inventarios y cartera coinciden con lo registrado.
4. Crear copia en USB, registrar otra venta, restaurar la copia → la venta posterior desaparece y se generó la copia «antes_de_restaurar».
5. Cerrar el programa → se crea la copia automática del día en `…\Automáticas`.
