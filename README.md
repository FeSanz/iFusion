# iFusion — Consulta de Órdenes de Trabajo (Oracle Fusion Cloud)

Aplicación de escritorio **Windows Forms (.NET Framework 4.7.2)**, misma tecnología, tema (`SimulatorLook`) y esquema de instalador (Inno Setup + firma) que WOW Scales 3.

- **Instancias** (botón *Instancias*): alias, URL base, usuario y contraseña/token. La contraseña se cifra con DPAPI (usuario de Windows) en `%LocalAppData%\iFusion\instances.json`. El selector del header cambia de ambiente sin reiniciar.
- **Búsqueda**: OT, estatus (multi), artículo, centro de trabajo y organización. Pestañas Procesos (`/processWorkOrders`) y Discreta (`/workOrders`).
- **Vista**: cabecera de la OT y, por operación, Materiales / Recursos / Salidas (solo procesos).
- **Ver Stock**: `itemsV2` → sin lote GET directo; con lote batch GET de `itemStockingInquiries`.
- **Incrementar stock**: Miscellaneous Receipt en batch (`POST /fscmRestApi/resources/latest`, `inventoryStagedTransactions`).
- Endpoints en `Models/EndPoints.cs`.

## Compilar e instalar
```powershell
msbuild iFusion.csproj -restore -p:Configuration=Release
installers\build-installer.ps1      # compila, firma y genera installers\Output\iFusion Setup.exe
```
La firma usa `%USERPROFILE%\.ifusion-signing\ifusion.pfx` (o `IFUSION_PFX` / `IFUSION_PFX_PASSWORD`); el certificado es autofirmado: en el instalador hay una tarea opcional para confiar en él.
