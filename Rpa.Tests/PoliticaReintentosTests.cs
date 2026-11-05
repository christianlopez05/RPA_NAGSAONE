using Rpa.Comun.Modelos;
using Rpa.Comun.Reglas;

namespace Rpa.Tests;

// La regla de reintentos es de todos los robots (docs/rpa-robots.md, sección 5).
public class PoliticaReintentosTests
{
    [Fact]
    public void Falla_despues_de_guardar_va_a_REV_aunque_queden_intentos() =>
        Assert.Equal(EstadosTarea.Revision,
            PoliticaReintentos.EstadoTrasFallo(ClsResultadoEjecucion.Fallo("x", guardadoIntentado: true), 0, 3));

    [Fact]
    public void Error_definitivo_va_a_ERR_sin_reintentar() =>
        Assert.Equal(EstadosTarea.Error,
            PoliticaReintentos.EstadoTrasFallo(ClsResultadoEjecucion.Fallo("x", guardadoIntentado: false, definitivo: true), 0, 3));

    [Theory]
    [InlineData(0, "PEN")]
    [InlineData(1, "PEN")]
    [InlineData(2, "ERR")]
    public void Falla_antes_de_guardar_reintenta_hasta_el_tope(int intentosPrevios, string esperado) =>
        Assert.Equal(esperado,
            PoliticaReintentos.EstadoTrasFallo(ClsResultadoEjecucion.Fallo("x", guardadoIntentado: false), intentosPrevios, 3));
}
